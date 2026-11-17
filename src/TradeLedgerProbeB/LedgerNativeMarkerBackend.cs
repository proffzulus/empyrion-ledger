using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using Eleon.Modding;
using UnityEngine;

namespace TradeLedgerProbeB
{
    // Only the native private bookmark collection/commands are accessed; never native game SQL/files.
    internal sealed class LedgerNativeMarkerBackend : ILedgerMarkerBackend
    {
        private readonly Dictionary<int,FieldInfo> fields=new Dictionary<int,FieldInfo>();
        private readonly MethodInfo clientWorld,localPlayer,catalog,playfield,add,remove,update,enumerate;
        private readonly Type recordType,ownerType,callbackType;
        private readonly ConcurrentQueue<Action> completions=new ConcurrentQueue<Action>();
        private LedgerContext context;private IApplication application;
        private object enumerationOwner;
        private LedgerMarkerLoadFence loadFence=new LedgerMarkerLoadFence();
        private bool enumerationRequested,enumerationCompleted;
        private DateTime enumerationAt;
        private readonly ConcurrentQueue<EnumerationResult> enumerations=new ConcurrentQueue<EnumerationResult>();
        private sealed class EnumerationResult {internal object Owner;internal List<LedgerMarkerNative> Records;}
        internal int currentPlayer;internal string currentWorld;
        internal LedgerNativeMarkerBackend(Assembly game)
        {
            LedgerNativeContracts.Require(game,"markers");
            var m=game.ManifestModule;
            foreach(int t in new[]{0x040064e6,0x04004f4a,0x04002058,0x04001b31,0x04001a40,0x04001a3f,0x0400652c,0x04005aa0,
                0x04005d6f,0x04005d70,0x04005d71,0x04005dab,0x04005a10,0x04005a11,0x04005a12,0x04005a13,0x04005a14,
                0x04005a15,0x04005a16,0x04005a17,0x04005a18,0x04005a19,0x04005a1a,0x04005a1b,0x04005a1c,0x04005a1d,0x04005a1e})fields.Add(t,LedgerNativeContracts.Field(m,t));
            clientWorld=(MethodInfo)LedgerNativeContracts.Method(m,0x06006c05);localPlayer=(MethodInfo)LedgerNativeContracts.Method(m,0x060053af);
            catalog=(MethodInfo)LedgerNativeContracts.Method(m,0x06005eee);playfield=(MethodInfo)LedgerNativeContracts.Method(m,0x06005efe);add=(MethodInfo)LedgerNativeContracts.Method(m,0x06001b1b);remove=(MethodInfo)LedgerNativeContracts.Method(m,0x06001b22);update=(MethodInfo)LedgerNativeContracts.Method(m,0x06001b20);
            enumerate=(MethodInfo)LedgerNativeContracts.Method(m,0x06006004);
            if(enumerate.IsStatic||enumerate.ReturnType!=typeof(void)||enumerate.GetParameters().Length!=2||enumerate.GetParameters()[0].ParameterType.FullName!="Assembly-CSharp.DirectoryDictionary"||enumerate.GetParameters()[1].ParameterType.GetMethod("Invoke").GetParameters()[0].ParameterType!=typeof(List<>).MakeGenericType(fields[0x04005a16].DeclaringType))throw new InvalidDataException("Bookmark enumeration signature mismatch");
            recordType=fields[0x04005a16].DeclaringType;ownerType=fields[0x04005a14].FieldType;callbackType=add.GetParameters()[1].ParameterType;
            if(remove.IsStatic||remove.ReturnType!=typeof(void)||remove.GetParameters().Length!=1||remove.GetParameters()[0].ParameterType!=typeof(int))throw new InvalidDataException("Bookmark removal signature mismatch");
            if(recordType.FullName!="Assembly-CSharp.ActivatorManager"||ownerType.FullName!="Assembly-CSharp.DirectoryDictionary"||
                add.IsStatic||add.ReturnType!=typeof(void)||add.GetParameters().Length!=3||add.GetParameters()[0].ParameterType!=recordType||add.GetParameters()[2].ParameterType!=typeof(bool)||
                callbackType.GetMethod("Invoke").ReturnType!=typeof(void)||callbackType.GetMethod("Invoke").GetParameters()[0].ParameterType!=typeof(int)||
                fields[0x04005a18].FieldType!=typeof(Vector3)||fields[0x04005a1b].FieldType!=typeof(bool)||fields[0x04005a1c].FieldType!=typeof(bool)||fields[0x04005a1d].FieldType!=typeof(bool))throw new InvalidDataException("Bookmark signature mismatch");

            if(update.IsStatic||update.ReturnType!=typeof(void)||update.DeclaringType.FullName!="Assembly-CSharp.FunctionEditor"||update.GetParameters().Length!=1||update.GetParameters()[0].ParameterType!=recordType)throw new InvalidDataException("Bookmark update signature mismatch");
        }
        internal void Bind(LedgerContext c,IApplication app){context=c;application=app;Commands(c,app);}
        private object F(int token,object value){return fields[token].GetValue(value);}
        private void Set(int token,object target,object value){fields[token].SetValue(target,value);}
        private object Commands(LedgerContext context,IApplication app)
        {
            if(context==null||!context.Recordable||context.Mode!="SinglePlayer"||app==null||app.Mode.ToString()!="SinglePlayer"||app.State.ToString()!="Running"||
                !LedgerContext.MatchesCurrentSave(context,app.GetPathFor(AppFolder.SaveGame)))throw new InvalidOperationException("current-world/single-player guard");
            object scene=F(0x040064e6,null);if(scene==null)throw new InvalidOperationException("client scene unavailable");
            object w=clientWorld.Invoke(scene,null);if(w==null)throw new InvalidOperationException("client world unavailable");
            object player=localPlayer.Invoke(w,null);if(player==null)throw new InvalidOperationException("local player unavailable");
            int playerId=(int)F(0x04001b31,player);
            if(playerId<=0||app.LocalPlayer==null||app.LocalPlayer.Id!=playerId)throw new InvalidOperationException("local owner mismatch");
            object commands=F(0x04002058,player);
            if(commands==null||!ReferenceEquals(F(0x04001a3f,commands),player))throw new InvalidOperationException("bookmark owner mismatch");
            object db=F(0x0400652c,scene);if(db==null||!(bool)F(0x04005aa0,db))throw new InvalidOperationException("native bookmark persistence not ready");
            currentPlayer=playerId;currentWorld=context.WorldKey;return commands;
        }
        private IDictionary Bookmarks(object commands)
        {var result=F(0x04001a40,commands) as IDictionary;if(result==null||result.Count>4096)throw new InvalidOperationException("native local bookmark list unavailable/over limit");return result;}

        private LedgerMarkerNative Snapshot(object record)
        {
            if(record==null||record.GetType()!=recordType)throw new InvalidDataException("Unexpected native marker record");
            var pos=(Vector3)F(0x04005a18,record);
            return new LedgerMarkerNative{Id=(int)F(0x04005a10,record),Player=(int)F(0x04005a15,record),Type=Convert.ToInt32(F(0x04005a13,record)),
                Playfield=(string)F(0x04005a12,record),Label=(string)F(0x04005a16,record),X=pos.x,Y=pos.y,Z=pos.z,Shared=(bool)F(0x04005a1a,record),Universe=(bool)F(0x04005a1b,record),Approach=(bool)F(0x04005a1c,record),Hud=(bool)F(0x04005a1d,record)};
        }
        public IDictionary<int,LedgerMarkerNative> Read()
        {
            object commands=Commands(context,application);
            if(!ReferenceEquals(enumerationOwner,commands)){enumerationOwner=commands;loadFence=new LedgerMarkerLoadFence();enumerationRequested=enumerationCompleted=false;}
            EnumerationResult read;while(enumerations.TryDequeue(out read))if(ReferenceEquals(read.Owner,commands)){enumerationCompleted=true;loadFence.Completed(read.Records);}
            Action action;int drained=0;while(drained++<128&&completions.TryDequeue(out action))action();
            var result=new Dictionary<int,LedgerMarkerNative>();foreach(DictionaryEntry entry in Bookmarks(commands))
            {var snapshot=Snapshot(entry.Value);if(snapshot.Id!=(int)entry.Key)throw new InvalidDataException("Native marker ID/key disagree");if(snapshot.Player==currentPlayer&&!snapshot.Shared)result.Add(snapshot.Id,snapshot);}
            if(!loadFence.Admit(result)){
                if(!enumerationRequested||enumerationCompleted&&DateTime.UtcNow-enumerationAt>TimeSpan.FromSeconds(5)){
                    enumerationRequested=true;enumerationCompleted=false;enumerationAt=DateTime.UtcNow;
                    object scene=F(0x040064e6,null),db=F(0x0400652c,scene);
                    var ctor=ownerType.GetConstructors().Single(c=>c.GetParameters().Length==2&&c.GetParameters()[0].ParameterType.IsEnum&&c.GetParameters()[1].ParameterType==typeof(int));
                    object owner=ctor.Invoke(new object[]{Enum.ToObject(ctor.GetParameters()[0].ParameterType,1),currentPlayer});
                    var completion=new Enumeration(this,commands,currentPlayer);
                    var callback=Delegate.CreateDelegate(enumerate.GetParameters()[1].ParameterType,completion,typeof(Enumeration).GetMethod("Done"));
                    try{enumerate.Invoke(db,new[]{owner,(object)callback});}catch{enumerationCompleted=true;throw;}
                }
                throw new LedgerMarkerEnumerationPendingException();
            }
            return result;
        }
        private sealed class Enumeration
        {
            private readonly LedgerNativeMarkerBackend backend;private readonly object owner;private readonly int player;
            internal Enumeration(LedgerNativeMarkerBackend b,object o,int p){backend=b;owner=o;player=p;}
            public void Done(object values){
                var result=new List<LedgerMarkerNative>();
                try{var list=values as IList;if(list==null||list.Count>4096)throw new InvalidDataException("Private bookmark result unavailable");
                    foreach(var value in list){var n=backend.Snapshot(value);if(n.Player==player&&!n.Shared&&(n.Type==2||n.Type==3))result.Add(n);}
                }catch{result=null;}
                backend.enumerations.Enqueue(new EnumerationResult{Owner=owner,Records=result});
            }
        }
        private object Require(object commands,LedgerMarkerNative expected)
        {var map=Bookmarks(commands);if(!map.Contains(expected.Id)||!expected.Matches(Snapshot(map[expected.Id])))throw new InvalidOperationException("This marker changed; no native mutation performed");return map[expected.Id];}
        private void Project(object record,LedgerMarkerProjection projection)
        {Set(0x04005a16,record,projection.Label);Set(0x04005a1b,record,projection.Visible);Set(0x04005a1d,record,projection.Visible);Set(0x04005a1c,record,projection.Approach);}
        public LedgerMarkerNative Update(LedgerMarkerNative expected,LedgerMarkerProjection projection)
        {
            object commands=Commands(context,application);object original=Require(commands,expected);object copy=Activator.CreateInstance(recordType);
            foreach(var field in recordType.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))field.SetValue(copy,field.GetValue(original));
            Project(copy,projection);update.Invoke(commands,new[]{copy});
            var map=Bookmarks(commands);if(!map.Contains(expected.Id))throw new InvalidOperationException("Native update lost expected ID");
            return Snapshot(map[expected.Id]);
        }
        public void Delete(LedgerMarkerNative expected)
        {object commands=Commands(context,application);Require(commands,expected);remove.Invoke(commands,new object[]{expected.Id});if(Bookmarks(commands).Contains(expected.Id))throw new InvalidOperationException("Native removal not reflected");}
        public void Create(LedgerMarkerDestination target,LedgerMarkerProjection projection,Action<LedgerMarkerNative> done)
        {
            object commands=Commands(context,application);
            if(target.World!=currentWorld||target.Player!=currentPlayer)throw new InvalidOperationException("Marker destination scope mismatch");
            object scene=F(0x040064e6,null),w=clientWorld.Invoke(scene,null),current=F(0x04004f4a,w);
            if(current==null||(string)F(0x04005d71,current)!=context.Playfield)throw new InvalidOperationException("Current native PF mismatch");
            object descriptor=current;
            if(context.Playfield!=target.Playfield){object known=catalog.Invoke(null,null);descriptor=known==null?null:playfield.Invoke(known,new object[]{target.Playfield,false,false});}
            if(descriptor==null||(string)F(0x04005d71,descriptor)!=target.Playfield)throw new InvalidOperationException("Exact observed playfield descriptor unavailable");
            object record=Activator.CreateInstance(recordType);Project(record,projection);
            Set(0x04005a12,record,target.Playfield);Set(0x04005a18,record,new Vector3(target.X,target.Y,target.Z));
            Set(0x04005a11,record,F(0x04005d6f,descriptor));Set(0x04005a17,record,F(0x04005d70,descriptor));
            Set(0x04005a13,record,Enum.ToObject(fields[0x04005a13].FieldType,(bool)F(0x04005dab,descriptor)?2:3));Set(0x04005a15,record,currentPlayer);
            var ctor=ownerType.GetConstructors().Single(c=>c.GetParameters().Length==2&&c.GetParameters()[0].ParameterType.IsEnum&&c.GetParameters()[1].ParameterType==typeof(int));
            Set(0x04005a14,record,ctor.Invoke(new object[]{Enum.ToObject(ctor.GetParameters()[0].ParameterType,1),currentPlayer}));
            var completion=new Completion(this,record,done);Delegate callback=Delegate.CreateDelegate(callbackType,completion,typeof(Completion).GetMethod("Done"));
            try{add.Invoke(commands,new object[]{record,callback,false});}catch(Exception e){throw new LedgerMarkerUncertainException(e);}
        }
        private sealed class Completion
        {
            private readonly LedgerNativeMarkerBackend owner;private readonly object record;private readonly Action<LedgerMarkerNative> done;
            internal Completion(LedgerNativeMarkerBackend o,object r,Action<LedgerMarkerNative> callback){owner=o;record=r;done=callback;}
            public void Done(int id){owner.completions.Enqueue(()=>{if(id<0){done(null);return;}object commands=owner.Commands(owner.context,owner.application);var map=owner.Bookmarks(commands);if(!map.Contains(id))throw new InvalidOperationException("Native creation callback ID absent; outcome uncertain");var expected=owner.Snapshot(record);expected.Id=id;var actual=owner.Snapshot(map[id]);if(!expected.Matches(actual))throw new InvalidOperationException("Native creation callback differs; outcome uncertain");done(actual);});}
        }
    }
}
