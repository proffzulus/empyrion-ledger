using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace TradeLedgerProbeB
{
    [DataContract] public sealed class LedgerNativeSpec
    {
        [DataMember] public int Token;
        // Token is Ledger's stable hook/contract ID. NativeToken is the audited
        // metadata location in this exact image; never reuse a shifted old token.
        [DataMember(EmitDefaultValue=false)] public int NativeToken;
        [DataMember] public string Owner,Name,Key,Body;
        [DataMember] public bool Static;
        [DataMember] public string[] Operands;
    }
    [DataContract] internal sealed class LedgerNativeProfile
    {
        [DataMember] public string Identity=null,Hash=null;
        [DataMember] public List<LedgerNativeSpec> Members=null;
        [DataMember] public Dictionary<string,int[]> Capabilities=null;
    }
    // Shared by the loader and optional adapter. Never invokes native members or reads native instances.
    // Build text/MVID identify evidence; permission comes from cached feature contracts.
    public static class LedgerNativeContracts
    {
        private static readonly object Gate=new object();
        private static readonly Dictionary<Module,Binding> Bindings=new Dictionary<Module,Binding>();
        private static readonly Dictionary<short,OpCode> Codes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).GroupBy(o=>o.Value).ToDictionary(g=>g.Key,g=>g.First());
        private static LedgerNativeProfile profile;
        public static Action<string,string> Report;
        private sealed class Binding
        {
            internal Module Module;internal bool Known;
            internal Dictionary<int,LedgerNativeSpec> Specs;
            internal readonly Dictionary<int,MemberInfo> Members=new Dictionary<int,MemberInfo>();
            internal readonly Dictionary<int,int> Canonical=new Dictionary<int,int>();
            internal readonly Dictionary<string,string> Results=new Dictionary<string,string>();
        }
        private static LedgerNativeProfile Profile
        {
            get{if(profile==null)using(var stream=typeof(LedgerNativeContracts).Assembly.GetManifestResourceStream("TradeLedgerProbeB.NativeContracts.json")){
                if(stream==null)throw new InvalidDataException("native-contract-resource-missing");
                profile=(LedgerNativeProfile)new DataContractJsonSerializer(typeof(LedgerNativeProfile),new DataContractJsonSerializerSettings{UseSimpleDictionaryFormat=true,MaxItemsInObjectGraph=1000000}).ReadObject(stream);
            }return profile;}
        }
        private static Binding For(Module module)
        {
            Binding b;if(Bindings.TryGetValue(module,out b))return b;
            if(module.Assembly.GetName().Name!="Assembly-CSharp")throw new InvalidDataException("native-contract-module-name");
            b=new Binding{Module=module,Known=Safe.Hash(module.Assembly.Location)==Profile.Hash,Specs=Profile.Members.ToDictionary(s=>s.Token)};
            Bindings.Add(module,b);return b;
        }
        public static void Require(Assembly game,string capability)
        {
            lock(Gate){var b=For(game.ManifestModule);string state;if(b.Results.TryGetValue(capability,out state)){if(state!="ready")throw new InvalidDataException(state);return;}
                try{int[] required;if(!Profile.Capabilities.TryGetValue(capability,out required))throw new InvalidDataException("capability-not-described:"+capability);
                    foreach(int token in required)Resolve(b,token);
                    b.Results[capability]="ready";Notify(capability,"ready profile="+Profile.Identity+" knownHash="+b.Known+" members="+required.Length+" live=unverified");
                }catch(Exception e){state="native-capability-unavailable:"+capability+":"+e.Message;b.Results[capability]=state;Notify(capability,state);throw new InvalidDataException(state,e);}
            }
        }
        private static void Notify(string capability,string state){try{var report=Report;if(report!=null)report("LedgerCapability-"+capability,capability+" "+state);}catch(Exception){}}
        public static FieldInfo Field(Module module,int token)
        {if(module.Assembly.GetName().Name!="Assembly-CSharp")return module.ResolveField(token);lock(Gate)return (FieldInfo)Resolve(For(module),token);}
        public static MethodBase Method(Module module,int token)
        {if(module.Assembly.GetName().Name!="Assembly-CSharp")return module.ResolveMethod(token);lock(Gate)return (MethodBase)Resolve(For(module),token);}
        public static int Token(MemberInfo member)
        {lock(Gate){Binding b;int value;if(Bindings.TryGetValue(member.Module,out b)&&b.Canonical.TryGetValue(member.MetadataToken,out value))return value;return member.MetadataToken;}}
        public static bool Certified(Module module){lock(Gate){Binding b;return Bindings.TryGetValue(module,out b)&&b.Results.Values.Any(v=>v=="ready");}}
        // Also used by the standalone installer. Reflection/IL only: no native
        // methods, instance fields, static initializers or Harmony hooks execute.
        public static string[] Inspect(Assembly game)
        {
            lock(Gate){return Profile.Capabilities.Keys.OrderBy(x=>x,StringComparer.Ordinal).Select(cap=>{
                try{Require(game,cap);return cap+"=ready";}catch(Exception e){return cap+"=unavailable: "+e.Message;}
            }).ToArray();}
        }
        public static bool Bound(MemberInfo member,int token)
        {lock(Gate){Binding b;MemberInfo value;return member!=null&&Bindings.TryGetValue(member.Module,out b)&&b.Members.TryGetValue(token,out value)&&value==member;}}
        private static MemberInfo Resolve(Binding b,int token)
        {
            MemberInfo value;if(b.Members.TryGetValue(token,out value))return value;
            LedgerNativeSpec spec;if(!b.Specs.TryGetValue(token,out spec))throw new InvalidDataException("unproven-member:"+token.ToString("X8"));
            value=null;if(b.Known)try{value=b.Module.ResolveMember(spec.NativeToken==0?token:spec.NativeToken);}catch(ArgumentException){value=null;}
            if(b.Known&&spec.NativeToken!=0&&(value==null||Key(value)!=spec.Key))throw new InvalidDataException("known-profile-native-id-key-mismatch:"+token.ToString("X8"));
            if(!b.Known||value==null||Key(value)!=spec.Key){
                var type=b.Module.GetType(spec.Owner,false,false);if(type==null)throw new InvalidDataException("missing-owner:"+spec.Owner);
                const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly;
                var candidates=(token>>24==4?type.GetFields(flags).Cast<MemberInfo>():type.GetMethods(flags).Cast<MemberInfo>().Concat(type.GetConstructors(flags))).ToArray();
                if(candidates.Length>4096)throw new InvalidDataException("owner-member-bound");
                var matches=candidates.Where(m=>Key(m)==spec.Key).Take(2).ToArray();
                if(matches.Length==0&&token>>24==6){
                    // A renamed method is admissible only in the exact proven owner,
                    // with unchanged ABI AND complete IL/operand/local/EH semantics.
                    // Never infer a field/owner by shape or bind a moved old token.
                    matches=candidates.Where(m=>RenameKey(Key(m))==RenameKey(spec.Key)&&SemanticBody(m,spec)).Take(2).ToArray();
                }
                if(matches.Length!=1)throw new InvalidDataException("missing-or-ambiguous-member:"+token.ToString("X8"));value=matches[0];
            }
            if(!b.Known&&!(Key(value)==spec.Key?ValidateBody(value,spec):RenameKey(Key(value))==RenameKey(spec.Key)&&SemanticBody(value,spec)))throw new InvalidDataException("changed-semantic-contract:"+token.ToString("X8"));
            int other;if(b.Canonical.TryGetValue(value.MetadataToken,out other)&&other!=token)throw new InvalidDataException("non-unique-native-binding:"+token.ToString("X8"));
            b.Members.Add(token,value);b.Canonical[value.MetadataToken]=token;return value;
        }
        private static string RenameKey(string key){var parts=key.Split('|');if(parts.Length==6&&parts[0]=="M")parts[2]="<method>";return string.Join("|",parts);}
        private static bool SemanticBody(MemberInfo member,LedgerNativeSpec expected)
        {
            var method=member as MethodBase;if(method==null||expected.Body=="none"||method.GetMethodBody()==null)return false;
            string[] operands;return Body(method,out operands)==expected.Body&&operands.SequenceEqual(expected.Operands??new string[0]);
        }
        public static string TypeKey(Type t)
        {
            if(t.IsByRef)return TypeKey(t.GetElementType())+"&";if(t.IsPointer)return TypeKey(t.GetElementType())+"*";
            if(t.IsArray)return TypeKey(t.GetElementType())+"["+new string(',',t.GetArrayRank()-1)+"]";
            if(t.IsGenericParameter)return (t.DeclaringMethod==null?"!":"!!")+t.GenericParameterPosition;
            if(t.IsGenericType&&!t.IsGenericTypeDefinition)return t.GetGenericTypeDefinition().FullName.Replace('+','/')+"<"+string.Join(",",t.GetGenericArguments().Select(TypeKey))+">";
            return t.FullName.Replace('+','/');
        }
        public static string Key(MemberInfo member)
        {
            var f=member as FieldInfo;if(f!=null)return "F|"+TypeKey(f.DeclaringType)+"|"+f.Name+"|"+TypeKey(f.FieldType)+"|"+f.IsStatic;
            var m=member as MethodBase;if(m==null)return "T|"+(member as Type==null?member.ToString():TypeKey((Type)member));
            var method=m as MethodInfo;string generic=method!=null&&method.IsGenericMethod&&!method.IsGenericMethodDefinition?"<"+string.Join(",",method.GetGenericArguments().Select(TypeKey))+">":"";
            return "M|"+TypeKey(m.DeclaringType)+"|"+m.Name+generic+"|"+(method==null?"System.Void":TypeKey(method.ReturnType))+"|"+string.Join(",",m.GetParameters().Select(p=>TypeKey(p.ParameterType)))+"|"+m.IsStatic;
        }
        public static bool ValidateBody(MemberInfo member,LedgerNativeSpec expected)
        {
            if(Key(member)!=expected.Key)return false;var m=member as MethodBase;if(m==null)return true;
            string[] operands;string hash=Body(m,out operands);return hash==expected.Body&&operands.SequenceEqual(expected.Operands??new string[0]);
        }
        public static string Body(MethodBase method,out string[] operands)
        {
            var body=method.GetMethodBody();if(body==null){operands=new string[0];return "none";}
            byte[] bytes=body.GetILAsByteArray();var refs=new List<string>();int at=0;
            while(at<bytes.Length){short code=bytes[at++];if(code==0xfe)code=(short)(0xfe00|bytes[at++]);OpCode op;if(!Codes.TryGetValue(code,out op))throw new InvalidDataException("unknown-IL-opcode");
                int size;switch(op.OperandType){case OperandType.InlineNone:size=0;break;case OperandType.ShortInlineBrTarget:case OperandType.ShortInlineI:case OperandType.ShortInlineVar:size=1;break;case OperandType.InlineVar:size=2;break;case OperandType.InlineI8:case OperandType.InlineR:size=8;break;case OperandType.InlineSwitch:size=4+4*BitConverter.ToInt32(bytes,at);break;default:size=4;break;}
                if(at+size>bytes.Length)throw new InvalidDataException("invalid-IL-boundary");
                if(op.OperandType==OperandType.InlineMethod||op.OperandType==OperandType.InlineField||op.OperandType==OperandType.InlineType||op.OperandType==OperandType.InlineTok||op.OperandType==OperandType.InlineString||op.OperandType==OperandType.InlineSig){
                    int token=BitConverter.ToInt32(bytes,at);string key;
                    if(op.OperandType==OperandType.InlineString)key="S|"+method.Module.ResolveString(token);
                    else if(op.OperandType==OperandType.InlineSig)key="SIG|"+Convert.ToBase64String(method.Module.ResolveSignature(token));
                    else key=Key(method.Module.ResolveMember(token,method.DeclaringType.IsGenericType?method.DeclaringType.GetGenericArguments():null,method.IsGenericMethod?method.GetGenericArguments():null));
                    refs.Add(key);Array.Clear(bytes,at,4);
                }at+=size;
            }
            // Local types and exception regions are part of the audited behavior, not just opcode bytes.
            refs.Add("LOCALS|"+string.Join(",",body.LocalVariables.Select(v=>TypeKey(v.LocalType)+(v.IsPinned?" pinned":""))));
            refs.Add("INIT|"+body.InitLocals);
            foreach(var clause in body.ExceptionHandlingClauses)refs.Add("EH|"+clause.Flags+"|"+clause.TryOffset+"|"+clause.TryLength+"|"+clause.HandlerOffset+"|"+clause.HandlerLength+"|"+(clause.Flags==ExceptionHandlingClauseOptions.Clause?TypeKey(clause.CatchType):clause.Flags==ExceptionHandlingClauseOptions.Filter?clause.FilterOffset.ToString():""));
            operands=refs.ToArray();using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
        }
    }
}
