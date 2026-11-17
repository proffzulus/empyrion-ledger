using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Eleon.Modding;
using UnityEngine;

namespace TradeLedgerProbeB
{
    // The list is a native map publication, NOT the World entity collection. Membership in
    // the current player's discovery set is proved BEFORE reading a POI's metadata.
    internal sealed class LedgerDiscoveredNative
    {
        private readonly Module module;private readonly object manager;private readonly MethodInfo localize,exists;private string content;
        private readonly Dictionary<int,FieldInfo> fields=new Dictionary<int,FieldInfo>();
        private readonly Dictionary<int,MethodInfo> methods=new Dictionary<int,MethodInfo>();
        private readonly LedgerObjectCaptureMemo<Sample> admitted=new LedgerObjectCaptureMemo<Sample>();
        private readonly LedgerObjectScanBudget budget=new LedgerObjectScanBudget();
        private object scopeWorld;private string scope;private int player;private object personalKey;
        private struct Sample : IEquatable<Sample>
        {
            internal int Id;internal string Name,Template,Group,Content;internal Vector3 Position;internal int? Faction,Owner;internal bool Poi,Profile;internal object Binding;
            public bool Equals(Sample s){return Id==s.Id&&Name==s.Name&&Template==s.Template&&Group==s.Group&&Content==s.Content&&Position==s.Position&&Faction==s.Faction&&Owner==s.Owner&&Poi==s.Poi&&Profile==s.Profile&&ReferenceEquals(Binding,s.Binding);}
        }
        internal LedgerDiscoveredNative(Assembly game)
        {
            LedgerNativeContracts.Require(game,"discovery");
            var ngui=Assembly.Load("NGUIAssembly");if(Safe.Hash(ngui.Location)!=BuildContract.Hashes["NGUIAssembly.dll"])throw new InvalidDataException("object-localization-build");
            localize=(MethodInfo)LedgerNativeContracts.Method(ngui.ManifestModule,0x060002ed);exists=ngui.GetType("Localization",true).GetMethod("Exists",new[]{typeof(string)});if(exists==null)throw new InvalidDataException("object-localization-contract");
            module=game.ManifestModule;manager=M(0xcdd0).Invoke(null,null);
            Bind(0x1e81,"Assembly-CSharp.PackageSerializer",typeof(int));Bind(0x1e8c,"Assembly-CSharp.PackageSerializer",typeof(string));
            Bind(0x1e99,"Assembly-CSharp.PackageSerializer",typeof(string));Bind(0x1e9a,"Assembly-CSharp.PackageSerializer",typeof(string));Bind(0x1e7e,"Assembly-CSharp.PackageSerializer",typeof(Vector3));
            Bind(0x332c,"Assembly-CSharp.DirectorySet",typeof(int));Bind(0x3331,"Assembly-CSharp.DirectorySet",typeof(bool));
            Bind(0x1b31,"Assembly-CSharp.ReferenceHelper",typeof(int));Bind(0x1b41,"Assembly-CSharp.ReferenceHelper",typeof(Vector3));
            Bind(0x1cee,"Assembly-CSharp.DatabaseToken",typeof(string));Bind(0x1cef,"Assembly-CSharp.DatabaseToken",typeof(string));
        }
        private void Bind(int token,string owner,Type type){var f=LedgerNativeContracts.Field(module,0x04000000+token);if(f.DeclaringType.FullName!=owner||f.FieldType!=type)throw new InvalidDataException("object-field-contract");fields[token]=f;}
        private object F(int token,object value){FieldInfo f;if(!fields.TryGetValue(token,out f))fields[token]=f=LedgerNativeContracts.Field(module,0x04000000+token);return f.GetValue(value);}
        private MethodInfo M(int token){MethodInfo m;if(!methods.TryGetValue(token,out m))methods[token]=m=(MethodInfo)LedgerNativeContracts.Method(module,0x06000000+token);return m;}
        private object World(LedgerContext c,IApplication app,int id)
        {
            if(c==null||!c.Recordable||c.Mode!="SinglePlayer"||app==null||app.State.ToString()!="Running"||app.Mode.ToString()!="SinglePlayer"||app.LocalPlayer==null||app.LocalPlayer.Id!=id||!LedgerContext.MatchesCurrentSave(c,app.GetPathFor(AppFolder.SaveGame)))return null;
            object scene=F(0x64e6,null),w=scene==null?null:M(0x6c05).Invoke(scene,null),d=w==null?null:F(0x4f4a,w);
            if(d==null||(string)F(0x5d71,d)!=c.Playfield||(int)M(0x53b0).Invoke(w,null)!=id)return null;
            string next=c.LocalSaveKey+"|"+c.WorldKey+"|"+c.Playfield+"|"+id;
            if(next!=scope||!ReferenceEquals(w,scopeWorld)){
                scope=next;scopeWorld=w;player=id;admitted.Clear();budget.Reset();content=(string)localize.Invoke(null,new object[]{"invTitle",true});if(content!=null&&content.Length>128)content=null;
                var ctor=(ConstructorInfo)LedgerNativeContracts.Method(module,0x06000d56);
                // Constructs only a boxed query key; never asks native code to create a discovery set.
                personalKey=ctor.Invoke(new object[]{Enum.ToObject(ctor.GetParameters()[0].ParameterType,1),id});
            }return w;
        }
        private bool Member(object w,int id,int kind)
        {object q=F(0x4f7e,w);return q!=null&&(bool)M(0x51c8).Invoke(q,new object[]{personalKey,((long)kind<<32)|(uint)id});}
        private object Registered(object w,int kind,int id)
        {var m=M(0x377d);object tree=F(0x4f34,w);return tree==null?null:m.Invoke(tree,new[]{Enum.ToObject(m.GetParameters()[0].ParameterType,kind),(object)id});}
        private void Faction(ref Sample s,object value)
        {if(value==null)return;var f=F(0x1305,value);if(!(f is FactionData))return;var data=(FactionData)f;s.Faction=(int)data.Group;s.Owner=data.Id;}
        private Sample Entity(object e)
        {
            var s=new Sample{Id=(int)F(0x1b31,e),Position=(Vector3)F(0x1b41,e),Binding=e,Poi=(bool)F(0x1b65,e),Content=content};
            if(module.GetType("Assembly-CSharp.MemoryHelper").IsInstanceOfType(e))s.Name=(string)F(0x1c15,e);
            if(module.GetType("Assembly-CSharp.DatabaseToken").IsInstanceOfType(e)){s.Template=(string)F(0x1cee,e);s.Group=(string)F(0x1cef,e);}
            Faction(ref s,F(0x1ba6,e));return s;
        }
        private string Display(string name)
        {return !string.IsNullOrEmpty(name)&&name.Length<=512&&(bool)exists.Invoke(null,new object[]{name})?(string)localize.Invoke(null,new object[]{name,true}):name;}
        private bool Known(Sample s){return admitted.Matches(s.Id,s);}
        private bool Send(LedgerContext c,Sample s,string evidence,string raw,Func<LedgerObservation,bool> submit)
        {
            if(Known(s))return true;
            string display=Display(s.Name);
            var fact=new LedgerObjectFact{Player=player,NativeId=s.Id,NativeName=s.Name,DisplayName=display,Template=s.Template,Group=s.Group,IsPoi=s.Poi,Category=s.Poi?"discovered POI":"observed structure",
                Evidence=evidence,CurrentInstanceState=!s.Profile,NameSource=s.Profile?"saved discovered POI name1E85;not refreshed live name":"bound instance name1C0E;active localization when key exists",TemplateSource="native concrete prefab filename",FactionSource=s.Profile?"saved discovered assignment1E9F;not refreshed live affiliation":"actual bound instance faction1B9F/1304",PositionSource=s.Profile?"saved discovered spawn1E77":"bound instance position1B3A",X=s.Position.x,Y=s.Position.y,Z=s.Position.z,FactionGroup=s.Faction,FactionId=s.Owner,RawLabel=raw,ContentIdentity=s.Content};
            var o=new LedgerObservation{Kind="place-observed",Context=c,Identity=new LedgerIdentity(),ObserverPlayer=player,Utc=DateTime.UtcNow.ToString("o"),DiscoveredObject=fact,Source=evidence};
            if(!LedgerDiscoveredObjects.Valid(o)||!submit(o))return false;
            admitted.Remember(s.Id,s,true);return true;
        }
        private Sample Raw(object raw)
        {var s=new Sample{Id=(int)F(0x1e81,raw),Name=(string)F(0x1e8c,raw),Template=(string)F(0x1e99,raw),Group=(string)F(0x1e9a,raw),Position=(Vector3)F(0x1e7e,raw),Poi=true,Profile=true,Binding=raw,Content=content};Faction(ref s,F(0x1ea6,raw));return s;}
        internal void Discover(LedgerContext c,IApplication app,int id,object name,object actor,object raw,Func<LedgerObservation,bool> submit)
        {
            object w=World(c,app,id);
            if(w==null||actor==null||actor.GetType().FullName!="Assembly-CSharp.SolutionLayout"||!ReferenceEquals(M(0x53af).Invoke(w,null),actor)||(int)F(0x1b31,actor)!=id||raw==null||raw.GetType().FullName!="Assembly-CSharp.PackageSerializer")return;
            int rawId=(int)F(0x1e81,raw);string group=(string)F(0x1e9a,raw);
            if(!LedgerObjectDiscoveryAdmission.LocalEvent(id,(int)F(0x1b31,actor),actor,M(0x53af).Invoke(w,null),w,scopeWorld,rawId>0,Member(w,rawId,2),name is string&&(string)name==group))return;
            var s=Raw(raw);
            if(!ReferenceEquals(World(c,app,id),w)||!ReferenceEquals(M(0x53af).Invoke(w,null),actor)||!Member(w,s.Id,2)||!Raw(raw).Equals(s))return;
            Send(c,s,"local-poi-discovery",s.Name,submit);
        }
        internal void Poll(LedgerContext c,IApplication app,int id,long now,Func<LedgerObservation,bool> submit)
        {
            object w=World(c,app,id);if(w==null){scopeWorld=null;scope=null;admitted.Clear();budget.Reset();return;}
            var tree=F(0x4f34,w);if(tree==null)return;var method=M(0x377b);
            var list=method.Invoke(tree,new[]{Enum.ToObject(method.GetParameters()[0].ParameterType,2)}) as IList;
            int queued=0;int start,count;if(budget.Take(list,list==null?0:list.Count,now,out start,out count))for(int i=0;i<count;i++){
                int at=(start+i)%list.Count;object marker=list[at];if(marker==null)continue;
                int entityId=(int)F(0x332c,marker);if(entityId<=0||!Member(w,entityId,2)||!(bool)F(0x3331,marker)||!ReferenceEquals(Registered(w,2,entityId),marker))continue;
                object entity=F(0x3332,marker);if(entity==null||!module.GetType("Assembly-CSharp.ReferenceHelper").IsInstanceOfType(entity)||(int)F(0x1b31,entity)!=entityId)continue;
                var s=Entity(entity);if(!s.Poi)continue;
                if(!ReferenceEquals(World(c,app,id),w)||!ReferenceEquals(F(0x4f34,w),tree)||!ReferenceEquals(method.Invoke(tree,new[]{Enum.ToObject(method.GetParameters()[0].ParameterType,2)}),list)||at>=list.Count||!ReferenceEquals(list[at],marker)||!Member(w,entityId,2)||!ReferenceEquals(Registered(w,2,entityId),marker)||!ReferenceEquals(F(0x3332,marker),entity)||!Entity(entity).Equals(s))continue;
                if(!Known(s)&&queued<2&&Send(c,s,"personal-discovery-map",null,submit))queued++;
            }
            Map(c,app,id,w,submit);
        }
        private object MapWindow(out UILabel label,out object marker)
        {
            label=null;marker=null;var stack=F(0xc181,manager) as Stack<string>;var windows=F(0xc180,manager) as IDictionary;
            if(stack==null||stack.Count==0||stack.Peek()!="Map"||windows==null||!windows.Contains("Map"))return null;
            object entry=windows["Map"];var root=F(0xc185,entry) as GameObject;var canvas=F(0xc189,entry) as CanvasGroup;
            if(!(bool)F(0xc187,entry)||root==null||!root.activeSelf||!root.activeInHierarchy||canvas!=null&&canvas.alpha<.99f)return null;
            var panels=F(0xc188,entry) as IList;if(panels==null||panels.Count==0||panels.Count>16)return null;
            foreach(var value in panels){var p=value as UIPanel;if(p==null||!p.enabled||!p.gameObject.activeInHierarchy||p.finalAlpha<.99f)return null;}
            object controller=F(0xc184,entry);if(controller==null||controller.GetType().FullName!="Assembly-CSharp.MemoryContextConnectionToken")return null;
            label=F(0xb5bc,controller) as UILabel;marker=F(0xb634,controller);
            return label==null||!label.enabled||!label.isVisible||!label.gameObject.activeInHierarchy||label.finalAlpha<.99f||string.IsNullOrEmpty(label.text)||label.text.Length>4096||!label.transform.IsChildOf(root.transform)?null:controller;
        }
        private void Map(LedgerContext c,IApplication app,int id,object w,Func<LedgerObservation,bool> submit)
        {
            UILabel label;object marker;object controller=MapWindow(out label,out marker);if(controller==null||marker==null||!module.GetType("Assembly-CSharp.DirectorySet").IsInstanceOfType(marker))return;
            int kind=Convert.ToInt32(F(0x332d,marker)),entityId=(int)F(0x332c,marker);if(kind!=0&&kind!=2||entityId<=0||!ReferenceEquals(Registered(w,kind,entityId),marker))return;
            var entity=F(0x3332,marker);if(entity==null||(int)F(0x1b31,entity)!=entityId)return;
            var s=Entity(entity);string text=label.text;if(string.IsNullOrEmpty(s.Name)||LedgerPresentation.Text(text).IndexOf(LedgerPresentation.Text(Display(s.Name)),StringComparison.Ordinal)<0)return;
            UILabel final;object selected;
            if(!ReferenceEquals(World(c,app,id),w)||!ReferenceEquals(MapWindow(out final,out selected),controller)||!ReferenceEquals(selected,marker)||!ReferenceEquals(final,label)||label.text!=text||!ReferenceEquals(F(0x3332,marker),entity)||!Entity(entity).Equals(s))return;
            Send(c,s,"visible-map",text,submit);
        }
    }
    // Pure bounded round robin. Replacing the publication restarts safely; stable lists make
    // fair progress, with no complete dictionary/list copies and no metadata reads on rejects.
    public sealed class LedgerObjectScanBudget
    {
        private object publication;private int cursor;private long next;
        public void Reset(){publication=null;cursor=0;next=0;}
        public bool Take(object list,int size,long now,out int start,out int count)
        {
            start=count=0;if(now<next)return false;next=now+1000;
            if(!ReferenceEquals(publication,list)){publication=list;cursor=0;}
            if(list==null||size<=0||size>32768){cursor=0;return false;}
            start=cursor%size;count=Math.Min(8,size);cursor=(start+count)%size;return true;
        }
    }
}
