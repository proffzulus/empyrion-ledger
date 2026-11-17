using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Trivial.Mono.Cecil;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
public static class LedgerContractEvidence {
 static string Tk(TypeReference t){
  if(t is PinnedType)return Tk(((PinnedType)t).ElementType);
  if(t is RequiredModifierType)return Tk(((RequiredModifierType)t).ElementType);if(t is OptionalModifierType)return Tk(((OptionalModifierType)t).ElementType);
  var gp=t as GenericParameter;if(gp!=null)return (gp.Type==GenericParameterType.Method?"!!":"!")+gp.Position;
  var gi=t as GenericInstanceType;if(gi!=null)return gi.ElementType.FullName+"<"+string.Join(",",gi.GenericArguments.Select(Tk))+">";
  var ar=t as ArrayType;if(ar!=null)return Tk(ar.ElementType)+"["+new string(',',ar.Rank-1)+"]";
  var br=t as ByReferenceType;if(br!=null)return Tk(br.ElementType)+"&";var pt=t as PointerType;if(pt!=null)return Tk(pt.ElementType)+"*";return t.FullName;
 }
 static string Bound(TypeReference t,MethodReference m){
  var p=t as GenericParameter;
  if(p!=null){var gm=m as GenericInstanceMethod;var gt=m.DeclaringType as GenericInstanceType;
   if(p.Type==GenericParameterType.Method&&gm!=null)return Tk(gm.GenericArguments[p.Position]);
   if(p.Type==GenericParameterType.Type&&gt!=null)return Tk(gt.GenericArguments[p.Position]);return Tk(t);}
  var gi=t as GenericInstanceType;if(gi!=null)return gi.ElementType.FullName+"<"+string.Join(",",gi.GenericArguments.Select(x=>Bound(x,m)))+">";
  var ar=t as ArrayType;if(ar!=null)return Bound(ar.ElementType,m)+"["+new string(',',ar.Rank-1)+"]";
  var br=t as ByReferenceType;if(br!=null)return Bound(br.ElementType,m)+"&";return Tk(t);
 }
 static string Key(MemberReference m){
  var f=m as FieldReference;if(f!=null)return "F|"+Tk(f.DeclaringType)+"|"+f.Name+"|"+Bound(f.FieldType,new MethodReference("field",f.FieldType,f.DeclaringType))+"|"+f.Resolve().IsStatic;
  var mm=m as MethodReference;if(mm!=null){var gm=mm as GenericInstanceMethod;return "M|"+Tk(mm.DeclaringType)+"|"+mm.Name+(gm==null?"":"<"+string.Join(",",gm.GenericArguments.Select(Tk))+">")+"|"+Bound(mm.ReturnType,mm)+"|"+string.Join(",",mm.Parameters.Select(p=>Bound(p.ParameterType,mm)))+"|"+(!mm.HasThis);}
  var t=m as TypeReference;return "T|"+(t==null?m.ToString():Tk(t));
 }
 public static void Contracts(string path,string tokensPath,string output){
  var json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};byte[] image=File.ReadAllBytes(path);int pe=BitConverter.ToInt32(image,0x3c),sections=BitConverter.ToUInt16(image,pe+6),table=pe+24+BitConverter.ToUInt16(image,pe+20);
  Func<int,int> offset=rva=>{for(int i=0;i<sections;i++){int at=table+i*40,va=BitConverter.ToInt32(image,at+12),size=Math.Max(BitConverter.ToInt32(image,at+8),BitConverter.ToInt32(image,at+16));if(rva>=va&&rva<va+size)return BitConverter.ToInt32(image,at+20)+rva-va;}throw new InvalidDataException("RVA outside section");};
  var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(Path.GetDirectoryName(path));using(var a=AssemblyDefinition.ReadAssembly(path,new ReaderParameters{AssemblyResolver=resolver})){
   var result=new List<object>();foreach(var line in File.ReadAllLines(tokensPath)){int token=Convert.ToInt32(line,16);var m=(MemberReference)a.MainModule.LookupToken(token);var md=m as MethodDefinition;var fd=m as FieldDefinition;var operands=new List<string>();string hash=null;
    if(md!=null){hash="none";if(md.HasBody){int at=offset(md.RVA),header=(image[at]&3)==2?1:(BitConverter.ToUInt16(image,at)>>12)*4;var bytes=new byte[md.Body.CodeSize];Array.Copy(image,at+header,bytes,0,bytes.Length);
     foreach(var i in md.Body.Instructions){var op=i.OpCode.OperandType;if(op==Trivial.Mono.Cecil.Cil.OperandType.InlineField||op==Trivial.Mono.Cecil.Cil.OperandType.InlineMethod||op==Trivial.Mono.Cecil.Cil.OperandType.InlineType||op==Trivial.Mono.Cecil.Cil.OperandType.InlineTok||op==Trivial.Mono.Cecil.Cil.OperandType.InlineString||op==Trivial.Mono.Cecil.Cil.OperandType.InlineSig){
      operands.Add(op==Trivial.Mono.Cecil.Cil.OperandType.InlineString?"S|"+(string)i.Operand:op==Trivial.Mono.Cecil.Cil.OperandType.InlineSig?"SIG|"+i.Operand:Key((MemberReference)i.Operand));Array.Clear(bytes,i.Offset+i.OpCode.Size,4);
     }}
     operands.Add("LOCALS|"+string.Join(",",md.Body.Variables.Select(v=>Tk(v.VariableType)+(v.IsPinned?" pinned":""))));operands.Add("INIT|"+md.Body.InitLocals);
     foreach(var eh in md.Body.ExceptionHandlers){string flag=eh.HandlerType==Trivial.Mono.Cecil.Cil.ExceptionHandlerType.Catch?"Clause":eh.HandlerType.ToString();operands.Add("EH|"+flag+"|"+eh.TryStart.Offset+"|"+((eh.TryEnd==null?md.Body.CodeSize:eh.TryEnd.Offset)-eh.TryStart.Offset)+"|"+eh.HandlerStart.Offset+"|"+((eh.HandlerEnd==null?md.Body.CodeSize:eh.HandlerEnd.Offset)-eh.HandlerStart.Offset)+"|"+(flag=="Clause"?Tk(eh.CatchType):flag=="Filter"?eh.FilterStart.Offset.ToString():""));}
     using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
    }}
    result.Add(new{Token=token,Owner=m.DeclaringType.FullName.Replace('/','+'),Name=m.Name,Key=Key(m),Static=fd==null?md.IsStatic:fd.IsStatic,Body=hash,Operands=operands});
   }File.WriteAllText(output,json.Serialize(result),new System.Text.UTF8Encoding(false));
  }
 }
}
