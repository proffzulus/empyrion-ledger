using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Eleon.Modding;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public interface ILedgerProductionUiHost {void ProducerShown(object controller);string ProducerPickupBeginning(object[] args);void ProducerAction(string kind,object instance,object[] args,object result);}
    // Object-access boundary shared by the native reader and isolated interaction fixtures.
    // Admission, source identity, publication and final revalidation always stay in the reader.
    internal abstract class LedgerProductionUiAccess
    {
        internal virtual string TypeName(object value){return value==null?null:value.GetType().FullName;}
        internal virtual bool Same(object left,object right){return ReferenceEquals(left,right);}
        internal abstract bool RootReady(object root);
        internal abstract bool CanvasReady(object canvas);
        internal abstract bool PanelReady(object panel);
        internal abstract object Transform(object root);
        internal abstract object Root(object component);
        internal abstract object Stub(object root);
        internal abstract object Child(object root,string type);
        internal abstract bool Active(object component);
        internal abstract string Label(object value);
    }
    internal sealed class LedgerProductionUnityAccess:LedgerProductionUiAccess
    {
        private readonly Module module;
        internal LedgerProductionUnityAccess(Module module){this.module=module;}
        internal override bool Same(object left,object right){return (left as UnityEngine.Object)==(right as UnityEngine.Object);}
        internal override bool RootReady(object value){var root=value as GameObject;return root!=null&&root.activeInHierarchy&&root.activeSelf;}
        internal override bool CanvasReady(object value){var canvas=value as CanvasGroup;return canvas==null||canvas.alpha>=.99f;}
        internal override bool PanelReady(object value){var p=value as UIPanel;return p!=null&&p.enabled&&p.gameObject.activeInHierarchy&&p.finalAlpha>=.99f;}
        internal override object Transform(object value){return ((GameObject)value).transform;}
        internal override object Root(object value){var c=value as Component;return c==null?null:c.gameObject;}
        internal override object Stub(object value){return ((GameObject)value).GetComponent(module.GetType("Eleon.Ngui.WindowStub"));}
        internal override object Child(object value,string type){return ((GameObject)value).GetComponentInChildren(module.GetType(type));}
        internal override bool Active(object value){var c=value as Component;return c!=null&&c.gameObject.activeInHierarchy;}
        internal override string Label(object value){var l=value as UILabel;return l!=null&&l.gameObject.activeInHierarchy?l.text:null;}
    }
    // Immediate native UI reader. References never leave the API thread or enter persisted DTOs.
    internal sealed partial class LedgerProductionNative
    {
        private readonly Module module;private readonly object manager;
        private readonly LedgerProductionUiAccess ui;
        private readonly Func<int,object,object> fixtureRead;private readonly Func<int,object,object[],object> fixtureCall;private readonly Func<int,string> fixtureName;
        private readonly Dictionary<int,FieldInfo> fields=new Dictionary<int,FieldInfo>();
        private readonly LedgerNativeCatalog catalog;
        private readonly Dictionary<string,string> emitted=new Dictionary<string,string>();
        private string scope;private object activeModel,activeMapMarker;private string visit;
        private readonly LedgerProductionVisit lastShown=new LedgerProductionVisit();
        private int localPlayer;
        private readonly Dictionary<string,long> failures=new Dictionary<string,long>();
        internal string Stage="not-started",Detail="",AdmissionEvidence;private string configDetail,worldRejection;internal long Attempts,Admitted,HookCallbacks;internal int Interaction;internal bool SessionOpen;
        internal void Opened(){activeModel=null;visit=null;lastShown.Cancel();Interaction++;Attempts=Admitted=HookCallbacks=0;failures.Clear();Stage="opened-awaiting-visible-published-grid";Detail="";AdmissionEvidence=null;SessionOpen=true;}
        internal string EndSession(){SessionOpen=false;return "interaction="+Interaction+" callbacks="+HookCallbacks+" attempts="+Attempts+" admitted="+Admitted+" lastStage="+Stage+" detail="+Detail+" rejected="+string.Join(",",failures.Select(p=>p.Key+":"+p.Value));}
        private void Failed(string stage){if(!SessionOpen)return;long count;if(failures.TryGetValue(stage,out count))failures[stage]=count+1;else if(failures.Count<32)failures[stage]=1;}
        private object WindowRejected(string key,string reason,string detail=""){if(key=="ItemGenerator"){Stage="window-"+reason;Detail=detail;if(reason!="not-in-stack")Failed(Stage);}return null;}
        private IEnumerable<LedgerObservation> Rejected(string reason,string detail=""){Stage=reason;Detail=detail;Failed(reason);return new List<LedgerObservation>();}
        private string LabelText(int token,object controller){string text=ui.Label(F(token,controller));return text!=null&&text.Length<=512?text:null;}
        internal LedgerProductionNative(Assembly game,IApplication app)
        {
            LedgerNativeContracts.Require(game,"production");
            module=game.ManifestModule;ui=new LedgerProductionUnityAccess(module);manager=M(0xcdd0).Invoke(null,null);catalog=new LedgerNativeCatalog(game,app);catalog.Read();
        }
        // Offline interaction fixture injects only object access, never capture/admission decisions.
        internal LedgerProductionNative(object manager,LedgerProductionUiAccess ui,Func<int,object,object> read,Func<int,object,object[],object> call,Func<int,string> name)
        {this.manager=manager;this.ui=ui;fixtureRead=read;fixtureCall=call;fixtureName=name;}
        private string ItemName(int id){return fixtureName==null?catalog.ItemName(id):fixtureName(id);}
        private MethodInfo M(int t){return (MethodInfo)LedgerNativeContracts.Method(module,0x06000000+t);}
        private object F(int t,object o){if(fixtureRead!=null)return fixtureRead(t,o);try{FieldInfo f;if(!fields.TryGetValue(t,out f)){f=LedgerNativeContracts.Field(module,0x04000000+t);fields.Add(t,f);}return f.GetValue(o);}catch(Exception e){throw new InvalidDataException("production-field=0x"+(0x04000000+t).ToString("X8")+" targetType="+(o==null?"static/null":o.GetType().FullName),e);}}
        private T V<T>(int t,object o){return (T)F(t,o);}
        private object Invoke(int t,object o,params object[] args){return fixtureCall==null?M(t).Invoke(o,args):fixtureCall(t,o,args);}
        private object Window(string key,out GameObject root)
        {object raw;var value=WindowView(key,out raw);root=raw as GameObject;return value;}
        private object WindowView(string key,out object root)
        {
            root=null;var stack=F(0xc181,manager) as Stack<string>;var map=F(0xc180,manager) as IDictionary;
            if(stack==null||stack.Count==0||map==null||!map.Contains(key))return WindowRejected(key,"not-in-stack");
            bool direct=stack.Peek()==key;
            if(!direct){var order=stack.ToArray();if(key!="ItemGenerator"||order.Length!=2||order[0]!="Bag"||order[1]!=key)return WindowRejected(key,"blocked-top-window");}
            object entry=map[key];root=F(0xc185,entry);if(!V<bool>(0xc187,entry)||!ui.RootReady(root))return WindowRejected(key,"closed-or-inactive-root");
            var canvas=F(0xc189,entry);if(!ui.CanvasReady(canvas))return WindowRejected(key,"fading-canvas");
            var panels=F(0xc188,entry) as IList;if(panels==null||panels.Count==0||panels.Count>16)return WindowRejected(key,"panel-registration");
            foreach(var value in panels){if(!ui.PanelReady(value))return WindowRejected(key,"panel-not-ready");}
            object controller=F(0xc184,entry);
            if(key=="ItemGenerator"){
                if(controller==null||ui.TypeName(controller)!="Assembly-CSharp.ResourceSetConnectionToken"||!ui.Same(F(0xb4dd,controller),ui.Transform(root)))return WindowRejected(key,"controller-root");
                object config=F(0xc186,entry);if(!Config(config,F(0xb4dc,controller),root))return WindowRejected(key,"configuration-copy",configDetail);
                if(!direct){if(V<string>(0xc134,config)!="Bag"||!map.Contains("Bag"))return WindowRejected(key,"Bag-link");
                    object bagEntry=map["Bag"],bag=F(0xc184,bagEntry);var bagRoot=F(0xc185,bagEntry);
                    if(bag==null||ui.TypeName(bag)!="Assembly-CSharp.FormSiteConnectionToken"||ui.Same(bagRoot,root)||!ui.RootReady(bagRoot)||!V<bool>(0xc187,bagEntry)||!ui.Same(F(0xad27,bag),ui.Transform(bagRoot)))return WindowRejected(key,"Bag-registration-root","controller="+ui.TypeName(bag)+" expected=Assembly-CSharp.FormSiteConnectionToken");
                    if(!Config(F(0xc186,bagEntry),F(0xad20,bag),bagRoot))return WindowRejected(key,"Bag-configuration-copy",configDetail);
                    object owner=F(0xad25,bag);if(owner==null||V<int>(0x1b31,owner)!=localPlayer)return WindowRejected(key,"Bag-local-owner");
                    var bp=F(0xc188,bagEntry) as IList;if(bp==null||bp.Count==0||bp.Count>8)return WindowRejected(key,"Bag-panel-registration");foreach(var panel in bp){if(!ui.PanelReady(panel))return WindowRejected(key,"Bag-panel-not-ready");}
                }
            }
            return controller;
        }
        private bool Config(object a,object b,object root)
        {
            configDetail="configuration-type-or-root";
            if(a==null||b==null||a.GetType()!=b.GetType()||!a.GetType().IsValueType||ui.TypeName(a)!="Assembly-CSharp.FileTreeConnectionToken"||root==null)return false;
            var stub=ui.Stub(root);
            configDetail="same-root-controller-backed-WindowStub-missing";
            if(stub==null||!ui.Same(ui.Root(stub),root)||V<bool>(0xc197,stub))return false;
            configDetail=LedgerProduction.ConfigurationMismatch(t=>F(t,a),t=>F(t,b),t=>F(t,stub));return configDetail==null;
        }
        private object World(LedgerContext c,IApplication app,int player)
        {
            worldRejection=LedgerProduction.ScopeRejection(c,app,player);if(worldRejection!=null)return null;
            object scene=F(0x64e6,null);if(scene==null){worldRejection="native-scene-unavailable";return null;}
            object w=Invoke(0x6c05,scene);if(w==null){worldRejection="native-world-unavailable";return null;}
            object pf=F(0x4f4a,w);if(pf==null){worldRejection="native-descriptor-unavailable";return null;}
            if(V<string>(0x4f78,w)!=c.Playfield){worldRejection="native-world-playfield-mismatch";return null;}
            if(V<string>(0x5d71,pf)!=c.Playfield){worldRejection="native-descriptor-playfield-mismatch";return null;}
            localPlayer=player;string next=c.LocalSaveKey+"|"+c.WorldKey+"|"+c.Playfield+"|"+player;if(scope!=next){scope=next;activeModel=null;activeMapMarker=null;hudWork.Reset();emitted.Clear();visit=null;lastShown.Cancel();}return w;
        }
        private int Item(object stack){return V<int>(0x2fe9,F(0x2fdf,stack));}
        private int Count(object stack){return V<int>(0x2fe1,stack);}
        private bool Published(Array visible,Array model)
        {return LedgerProduction.PublishedMatches(visible,model,Item,Count);}
        private int Block(object raw){return (int)Invoke(0x11d5,raw);}
        private static string Vector(object value){var t=value.GetType();return string.Join(",",new[]{"x","y","z"}.Select(n=>Convert.ToInt32(t.GetField(n).GetValue(value)).ToString(CultureInfo.InvariantCulture)));}
        internal LedgerObservation Closed(int type,object key)
        {
            if(type==11&&key is string&&(string)key=="ItemGenerator"){activeModel=null;visit=null;return lastShown.Close();}return null;
        }
        internal LedgerObservation Finish(){hudWork.Reset();return lastShown.Close();}
        internal LedgerObservation ClosedIfMissing()
        {
            if(activeModel==null)return null;
            var stack=F(0xc181,manager) as Stack<string>;var windows=F(0xc180,manager) as IDictionary;
            if(stack!=null&&stack.Contains("ItemGenerator")&&windows!=null&&windows.Contains("ItemGenerator")&&V<bool>(0xc187,windows["ItemGenerator"]))return null;
            activeModel=null;visit=null;return lastShown.Close();
        }
        private LedgerObservation Capture(LedgerContext c,LedgerProductionSnapshot p)
        {p.Key=LedgerProduction.Key(c,p);return new LedgerObservation{Kind="production",Context=c,Identity=new LedgerIdentity(),Production=p,Source=p.Proof,Utc=DateTime.UtcNow.ToString("o"),GameTicks=p.Tick.ToString(CultureInfo.InvariantCulture)};}
        internal LedgerObservation Discover(LedgerContext c,IApplication app,int player,ulong ticks,object actor,object resource)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.ResourceDiscovery))
            {
            object w=World(c,app,player);
            if(w==null||actor==null||resource==null||actor.GetType().FullName!="Assembly-CSharp.SolutionLayout"||V<int>(0x1b31,actor)!=player||(int)Invoke(0x53b0,w)!=player||!ReferenceEquals(Invoke(0x53af,w),actor)||resource.GetType().FullName!="Assembly-CSharp.AddinResolver")return null;
            var p=Deposit(c,resource,player,ticks);string admittedScope=scope;
            // No resource registry scan: point-lookup ONLY the resource named by this local event.
            object marker=F(0x506f,resource),tree=F(0x4f34,w);
            if(marker==null||marker.GetType().FullName!="Assembly-CSharp.TreeNodeManager"||tree==null){p.Proof+="; discovery detail unavailable: logical marker not published";return Capture(c,p);}
            var lookup=M(0x377d);object kind=Enum.ToObject(lookup.GetParameters()[0].ParameterType,1);
            var proof=new LedgerDepositDiscovery{ConfirmedEvent=true,Player=player,ActorPlayer=V<int>(0x1b31,actor),Actor=actor,LocalActor=Invoke(0x53af,w),
                World=w,CurrentWorld=w,Scope=admittedScope,CurrentScope=scope,Resource=resource,MarkerResource=F(0x3368,marker),Marker=marker,ResourceMarker=F(0x506f,resource),
                RegisteredMarker=lookup.Invoke(tree,new[]{kind,(object)p.DepositId.Value}),Discovered=V<bool>(0x3331,marker),ResourceId=p.DepositId.Value,MarkerId=V<int>(0x332c,marker)};
            if(!proof.Admitted()){p.Proof+="; discovery detail unavailable: exact discovered marker binding not confirmed";return Capture(c,p);}
            string body=V<string>(0x336a,marker);var details=proof.Read(body,(string)Invoke(0x37ba,marker));
            // SelectNode53C9 sets discovered, calls virtual3721 ->3785 ->377E, THEN emits23
            // synchronously via6B7B/B63F. Constructor caches alone are never a permission.
            proof.CurrentWorld=World(c,app,player);proof.CurrentScope=scope;proof.LocalActor=Invoke(0x53af,w);
            proof.ActorPlayer=V<int>(0x1b31,actor);proof.ResourceId=V<int>(0x505f,resource);proof.MarkerId=V<int>(0x332c,marker);
            proof.ResourceMarker=F(0x506f,resource);proof.MarkerResource=F(0x3368,marker);proof.RegisteredMarker=lookup.Invoke(tree,new[]{kind,(object)p.DepositId.Value});proof.Discovered=V<bool>(0x3331,marker);
            if(!proof.Admitted()||V<string>(0x336a,marker)!=body||LedgerProduction.Key(c,Deposit(c,resource,player,ticks))!=LedgerProduction.Key(c,p))return null;
            if(details!=null){details.Apply(p,ticks,"local ResourceDiscovered23; native53C9 post-refresh3721/3785/377E; exact registered StubManager3351/5056; offscreen admitted");p.Proof=p.DetailsSource;}
            else p.Proof+="; discovery detail unavailable: empty/partial native UI cache";
            return Capture(c,p);

            }
        }
        private LedgerProductionSnapshot Deposit(LedgerContext c,object r,int player,ulong tick)
        {
            object pos=F(0x5060,r);var t=pos.GetType();int item=Block(F(0x5062,r));string resource=ItemName(item);
            var p=new LedgerProductionSnapshot{Kind="deposit",Player=player,DepositId=V<int>(0x505f,r),Resource=resource,Definition=item,Device=resource,Complete=true,
                X=Convert.ToSingle(t.GetField("x").GetValue(pos)),Y=Convert.ToSingle(t.GetField("y").GetValue(pos)),Z=Convert.ToSingle(t.GetField("z").GetValue(pos)),
                PositionSource="StoreQueue.5047 native deposit coordinates; Map377D->Vector3i7D9D->MemoryDictionary3709",NavigationPosition=true,Epoch=c.WorldKey,Tick=tick,Proof="public ResourceDiscovered local actor; identity only; size/percentage unobserved"};
            return p;
        }
        internal IEnumerable<LedgerObservation> Poll(LedgerContext c,IApplication app,int player,ulong ticks,object expectedController=null)
        {
            if(expectedController!=null)HookCallbacks++;if(expectedController!=null||SessionOpen)Attempts++;
            var result=new List<LedgerObservation>();object w=World(c,app,player);if(w==null){hudWork.Reset();return Rejected("current-save-player-world",worldRejection);}GameObject root;
            object producerRoot;object controller=WindowView("ItemGenerator",out producerRoot);
            // Both paths read ONLY the bound published UI copy. No hook callback is a payload permission.
            // API retry covers a ready view after the last callback; strict window/owner/publication proofs remain.
            if(controller!=null&&ui.TypeName(controller)=="Assembly-CSharp.ResourceSetConnectionToken")
            {
                Stage="current-model-and-instance-owner";Detail="";
                object model=F(0xb4de,controller);if(model==null)return Rejected("model-not-bound");string modelType=ui.TypeName(model);bool miner=modelType=="Assembly-CSharp.ConfigList";
                if(expectedController!=null&&(ui.TypeName(expectedController)!="Eleon.Ngui.ItemGridItemGeneratorOutput"||!ReferenceEquals(F(0xa9f6,expectedController),model)))return Rejected("callback-model-reference");
                int kind=Convert.ToInt32(F(0x5309,model));if((miner&&kind!=5)||(!miner&&((modelType!="Assembly-CSharp.DirectoryDesigner"&&modelType!="Assembly-CSharp.ConditionTable")||kind<1||kind>4)))return Rejected("unsupported-model-kind",modelType+" kind="+kind);
                // Only the explicit current player's opened personal producer, never a remote tile registry.
                var owner=LedgerProduction.ReadOwner(F,model);
                if(!LedgerProduction.LocalOwner(owner,player))return Rejected("producer-local-owner","52DD/1304 group="+(int)owner.Group+" id="+owner.Id+" local="+player);
                Stage="current-terrain-definition-position";
                object chunk=F(0x5266,model);if(chunk==null||V<int>(0x5262,model)!=-1)return Rejected("terrain-chunk-frame");
                int clr=(int)Invoke(0x4d54,chunk);object grid=Invoke(0x5705,model);object block=Invoke(0x53d1,w,clr,grid);int definition=Block(block);if(definition<=0)return Rejected("current-block-definition");
                Vector3 pos=(Vector3)Invoke(0x5706,model,w);if(pos==Vector3.zero)return Rejected("native-position-unavailable");
                Stage="current-bound-grid-publication";
                var input=ui.Child(producerRoot,"Eleon.Ngui.ItemGridItemGeneratorInput");var output=ui.Child(producerRoot,"Eleon.Ngui.ItemGridItemGeneratorOutput");
                if(input==null||output==null||!ReferenceEquals(F(0xa9f4,input),model)||expectedController!=null&&!ReferenceEquals(output,expectedController)||!ui.Active(input)||!ui.Active(output)||!ReferenceEquals(F(0xa9f6,output),model))return Rejected("current-input-output-grid");
                if(V<bool>(0xa9f7,output)||V<bool>(0xa9f8,output))return Rejected("publication-pending");
                Stage="copy-shown-labels-and-outputs";
                if(!ReferenceEquals(activeModel,model)){activeModel=model;visit=DateTime.UtcNow.ToString("o");}
                string name=ItemName(definition);var p=new LedgerProductionSnapshot{Kind="producer",Player=player,Definition=definition,DefinitionName=fixtureName==null?catalog.ItemInternalName(definition):fixtureName(definition),Device=name,Family=miner?"AutoMiner":"Generator mode "+kind,ClrIdx=clr,
                    Block=Vector(grid),Owner="player:"+owner.Id.ToString(CultureInfo.InvariantCulture),X=pos.x,Y=pos.y,Z=pos.z,NavigationPosition=true,PositionSource="current opened terrain producer/VectorSettings.56D1 native world position",
                    Epoch=c.WorldKey,Tick=ticks,VisitUtc=visit,CapacityRaw=V<int>(0x52fd,model),Capacity=LedgerProduction.KnownCapacity(V<int>(0x52fd,model)),Period=V<uint>(0x52f9,model),NextTick=V<ulong>(0x5300,model),Alternating=kind==3,Complete=true,
                    Running=Convert.ToInt32(F(0x5310,model))==0&&!V<bool>(0x52ff,model),Proof="visible local ItemGenerator/B2DD;private owner52DD/1304;bound published input/output copies;native period/capacity candidates"};
                p.ShownOperating=LabelText(0xb4e0,controller);p.ShownNext=LabelText(0xb4e1,controller);p.ShownQuality=LabelText(0xb4e3,controller);p.ShownStatus=LabelText(0xb4e2,controller);
                string rawUi="";foreach(int token in new[]{0xb4e0,0xb4e1,0xb4e2,0xb4e3,0xb4e4}){string label=this.ui.Label(F(token,controller));if(label!=null)rawUi+=label+"\n";}if(rawUi.Length>4096)rawUi=rawUi.Substring(0,4096);p.RawUi=rawUi;
                var stacks=F(0x52bb,model) as Array;if(stacks==null||stacks.Length>64)return Rejected("output-model-bound");
                // 5812 -> 33BA deep-copies stacks into A7D0 for native rendering: reference equality is wrong.
                var published=F(0xa9cd,output) as Array;if(!Published(published,stacks))return Rejected("published-output-copy-mismatch");
                p.Outputs=LedgerProduction.ReadPublishedOutputs(published,Item,Count,ItemName);
                int a=V<int>(0x530a,model),b=V<int>(0x530b,model);foreach(int id in new[]{a,b}.Where(id=>id>0).Distinct())if(!p.Outputs.Any(x=>x.Item==id))p.Outputs.Add(new LedgerProductionOutput{Item=id,Name=ItemName(id)});
                if(p.Outputs.Count>2)return Rejected("output-type-bound");p.NextItem=p.Alternating?(V<bool>(0x530c,model)?a:b):a;
                Stage="coherent-shown-fuel-and-deposit-association";
                var definitionData=catalog==null?null:catalog.Definition(definition);string fuelType=definitionData==null?null:catalog.FullProperty(definitionData,"FuelAccept");
                bool industrial=miner&&fuelType!=null&&fuelType.Contains("Eden_IndustrialFuel");
                if(industrial){p.Family="Industrial miner (deferred)";p.Period=null;p.Running=null;p.Proof+=";owner-deferred-industrial-calibration";}
                if(p.Period==uint.MaxValue)p.Period=null;

                // Pure native read formula includes only this visible fuel input plus work reservoir/current cycle.
                var fuel=F(0x52fe,model) as Array;
                if(fuel!=null&&fuel.Length<=64&&Published(F(0xa9cd,input) as Array,fuel))p.FuelSeconds=LedgerProduction.RenderedFuelSeconds(V<int>(0x5304,model),(int)Invoke(0x584b,model),V<float>(0xb4ea,controller));
                string fuelLabel=ui.Label(F(0xb4e0,controller));
                // Never project a just-refuelled model value before the corresponding native label is shown.
                if(p.FuelSeconds.HasValue&&(fuelLabel==null||fuelLabel!=(string)Invoke(0xc483,controller,(int)p.FuelSeconds.Value)))p.FuelSeconds=null;
                if(p.FuelSeconds.HasValue){p.FuelTick=ticks;p.FuelUtc=DateTime.UtcNow.ToString("o");}
                if(miner){object r=F(0x531c,model);if(r!=null&&r.GetType().FullName=="Assembly-CSharp.AddinResolver"){
                    var deposit=Deposit(c,r,player,ticks);deposit.Proof="direct already-opened miner.5303 deposit association; size/percentage not shown";p.Resource=deposit.Resource;p.DepositKey=deposit.Key=LedgerProduction.Key(c,deposit);result.Add(Capture(c,deposit));}}
                // Read twice by reference after copying, without a closing callback/stale object access.
                Stage="final-current-view-revalidation";
                if(!ReferenceEquals(F(0xb4de,controller),model)||!ReferenceEquals(F(0x52bb,model),stacks)||!ReferenceEquals(F(0xa9cd,output),published)||!Published(published,stacks)||!ReferenceEquals(WindowView("ItemGenerator",out producerRoot),controller))return Rejected("current-view-changed-during-copy");
                Stage="admitted-published-producer";Detail="type="+modelType+" kind="+kind+" outputTypes="+p.Outputs.Count+" ownerSource=52DD/1304 source="+(expectedController==null?"published-copy-poll":"output-postfix");Admitted++;
                if(SessionOpen&&Admitted==1)AdmissionEvidence="interaction="+Interaction+" "+Detail+" configProof=11-controller-fields+same-root-WindowStub.BF97/BF98 tick="+ticks;
                result.Add(Capture(c,p));
            }
            else if(expectedController==null)
            using(LedgerProfiler.Measure(LedgerPerfPath.MapDeposit))
            {
                controller=Window("Map",out root);
                if(controller!=null&&controller.GetType().FullName=="Assembly-CSharp.MemoryContextConnectionToken"){
                    object marker=F(0xb634,controller);var label=F(0xb5bc,controller) as UILabel;
                    if(marker!=null&&marker.GetType().FullName=="Assembly-CSharp.TreeNodeManager"&&VisibleDepositLabel(label)){
                        string body=V<string>(0x336a,marker),rendered=label.text;
                        if(PublishedDepositBody(body,rendered)){
                            var details=LedgerDepositDetails.Read(body,rendered,(string)Invoke(0x37ba,marker),true);var r=F(0x3368,marker);
                            if(details!=null&&r!=null&&r.GetType().FullName=="Assembly-CSharp.AddinResolver"&&ReferenceEquals(Window("Map",out root),controller)&&ReferenceEquals(F(0xb634,controller),marker)&&V<string>(0x336a,marker)==body&&label.text==rendered&&VisibleDepositLabel(label)){
                                var p=Deposit(c,r,player,ticks);details.Apply(p,ticks,"visible native Map hover; B433/3351 + published B3BB/3353");p.Proof=p.DetailsSource;
                                var o=Capture(c,p);if(!ReferenceEquals(marker,activeMapMarker)){activeMapMarker=marker;emitted.Remove(p.Key);}result.Add(o);
                            }
                        }else activeMapMarker=null;
                    }else activeMapMarker=null;
                }
                else activeMapMarker=null;
            }
            if(expectedController==null){if(fixtureRead==null)result.AddRange(ScannerDeposits(c,w,player,ticks));}else hudWork.Reset();
            return result;
        }
        internal bool Changed(LedgerObservation o)
        {
            lastShown.Shown(o,true);
            var p=o.Production;string hash=LedgerProduction.ContentKey(p),old;if(emitted.TryGetValue(p.Key,out old)&&old==hash)return false;
            if(emitted.Count>=1024)emitted.Clear();emitted[p.Key]=hash;return true;
        }
    }
    public sealed partial class Probe : ILedgerProductionUiHost
    {
        private LedgerProductionNative productionSource;private string productionError;private long productionRetry,productionShownRetry,productionPolled;
        private void EndProductionSession(string reason)
        {if(productionSource!=null&&productionSource.SessionOpen){bool failed=productionSource.Admitted==0;Write(failed?"ProductionCaptureSkipped":"ProductionCaptureSession",productionSource.EndSession()+" close="+reason);}}
        private void ProductionFailure(Exception e,string origin)
        {string message=e.ToString();if(message.Length>4000)message=message.Substring(0,4000);Write("ProductionCaptureError","origin="+origin+" stage="+(productionSource==null?"initialize":productionSource.Stage)+" "+message);}
        private void RecordProduction(IEnumerable<LedgerObservation> observations)
        {
            foreach(var o in observations)if(productionSource.Changed(o))Record(o);
            string evidence=productionSource.AdmissionEvidence;productionSource.AdmissionEvidence=null;
            if(evidence!=null)try{Trace(TraceLevel.Warn,"ProductionCaptureAdmitted",evidence);}catch(Exception){/* Diagnostic delivery never gates capture/storage. */}
        }
        private void FlushShownProduction()
        {
            EndProductionSession("unload-or-stop");
            var o=productionSource==null?null:productionSource.Finish();if(o==null||ledger==null)return;
            o.Session=session;o.Sequence=System.Threading.Interlocked.Increment(ref ledgerSequence);
            if(!ledger.Enqueue(o))Write("LedgerStorageError","last-shown production flush rejected; observation="+o.ObservationId);
        }
        private void PollProduction()
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.ProductionPoll))
            {
            if(!runtimeEnabled||!OnApiThread||!WorldReady||ledger==null||ledgerContext==null||!ledgerContext.Recordable||!ledger.OwnedPublicationAllowed)return;
            if(ElapsedMs<productionPolled)return;productionPolled=ElapsedMs+250;
            try{
                if(productionSource==null){if(ElapsedMs<productionRetry)return;productionRetry=ElapsedMs+10000;productionSource=new LedgerProductionNative(application.GetType().Assembly,application);}
                ulong tick=application.GameTicks;
                RecordProduction(productionSource.Poll(ledgerContext,application,localId,tick));
                var closed=productionSource.ClosedIfMissing();if(closed!=null){EndProductionSession("closed-metadata");if(closed.Context.LocalSaveKey==ledgerContext.LocalSaveKey&&closed.Context.WorldKey==ledgerContext.WorldKey&&closed.Context.Playfield==ledgerContext.Playfield)Record(closed);}
                productionError=null;
            }catch(Exception e){string error=e.GetBaseException().GetType().Name+":"+e.GetBaseException().Message;if(error!=productionError){productionError=error;ProductionFailure(e,"poll");}}

            }
        }
        public void ProducerShown(object controller)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.ProducerShown))
            {
            if(productionSource==null||!OnApiThread||!WorldReady||ledger==null||!ledger.OwnedPublicationAllowed||ElapsedMs<productionShownRetry)return;
            try{ulong tick=application.GameTicks;RecordProduction(productionSource.Poll(ledgerContext,application,localId,tick,controller));}
            catch(Exception e){productionShownRetry=ElapsedMs+10000;ProductionFailure(e,"output-postfix;retry10s");}

            }
        }
        private void ProductionEvent(int type,object name,object actor,object resource)
        {
            try{if(type==23&&runtimeEnabled&&OnApiThread&&WorldReady&&ledger!=null&&ledgerContext!=null&&ledgerContext.Recordable&&ledger.OwnedPublicationAllowed&&productionSource==null){if(ElapsedMs<productionRetry)return;productionRetry=ElapsedMs+10000;productionSource=new LedgerProductionNative(application.GetType().Assembly,application);}
                if(productionSource==null)return;if(type==10&&name is string&&(string)name=="ItemGenerator"){productionSource.Opened();return;}if(type==11){var shown=productionSource.Closed(type,name);if(name is string&&(string)name=="ItemGenerator")EndProductionSession("native-close");if(shown!=null&&ledgerContext!=null&&shown.Context.WorldKey==ledgerContext.WorldKey&&shown.Context.LocalSaveKey==ledgerContext.LocalSaveKey&&shown.Context.Playfield==ledgerContext.Playfield)Record(shown);return;}if(type!=23||!runtimeEnabled||!OnApiThread||!WorldReady||ledger==null||!ledger.OwnedPublicationAllowed)return;
                var o=productionSource.Discover(ledgerContext,application,localId,application.GameTicks,actor,resource);if(o!=null&&productionSource.Changed(o))Record(o);
            }catch(Exception e){ProductionFailure(e,"event");}
        }
    }
}
