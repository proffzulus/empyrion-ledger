using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal static class LedgerMarkerIdentityTests
    {
        private static T Copy<T>(T value){return LedgerJson.Decode<T>(LedgerJson.Encode(value));}
        private static LedgerBookmarkPlan Plan(int npc=10)
        {return new LedgerBookmarkPlan{World="identity-test-world",Playfield="Deltalan I",NpcId=npc,X=npc*10,Y=20,Z=30,ItemId=10103,Auxiliary=0,PlayerBuys=true,ItemName="Mining Equipment",UnitPrice="15446",Quantity="22",OfferKey="owned-identity-fixture:"+npc,Place="P3: Planetside Production",ColorHex="80E680"};}
        private sealed class Native : ILedgerMarkerBackend
        {
            internal Dictionary<int,LedgerMarkerNative> Map=new Dictionary<int,LedgerMarkerNative>();
            internal int Creates,Updates,Deletes,Next=100;
            internal LedgerMarkerLoadFence Fence;
            public IDictionary<int,LedgerMarkerNative> Read(){if(Fence!=null&&!Fence.Admit(Map))throw new LedgerMarkerEnumerationPendingException();return Map.ToDictionary(p=>p.Key,p=>Copy(p.Value));}
            public void Create(LedgerMarkerDestination d,LedgerMarkerProjection p,Action<LedgerMarkerNative> callback){var n=new LedgerMarkerNative{Id=Next++,Player=d.Player,Type=3,Playfield=d.Playfield,X=d.X,Y=d.Y,Z=d.Z,Label=p.Label,Hud=p.Visible,Universe=p.Visible,Approach=p.Approach};Map.Add(n.Id,n);Creates++;callback(Copy(n));}
            public LedgerMarkerNative Update(LedgerMarkerNative expected,LedgerMarkerProjection p){Require(expected);var n=Copy(expected);n.Label=p.Label;n.Hud=n.Universe=p.Visible;n.Approach=p.Approach;Map[n.Id]=n;Updates++;return Copy(n);}
            public void Delete(LedgerMarkerNative expected){Require(expected);Map.Remove(expected.Id);Deletes++;}
            private void Require(LedgerMarkerNative n){if(!Map.ContainsKey(n.Id)||!Map[n.Id].Matches(n))throw new Exception("Identity fixture refuses stale/unowned mutation");}
        }
        private static string Strip(string value)
        {
            var assembly=Assembly.LoadFrom(Path.Combine(Environment.GetEnvironmentVariable("EMPYRION_PROBE_MANAGED"),"NGUIAssembly.dll"));
            return (string)assembly.GetType("NGUIText",true).GetMethod("StripSymbols",new[]{typeof(string)}).Invoke(null,new object[]{value});
        }
        internal static void Run(Action<bool,string> check,string root)
        {
            int count=0;Action<bool,string> c=(ok,text)=>{check(ok,"marker identity: "+text);count++;};
            var m=new LedgerMarkerModel();var d=m.Ensure(Plan(),1002);var b=new Native();var coordinator=new LedgerMarkerCoordinator(m);
            string tag=LedgerMarkerIdentity.Tag(m.Data,d),scope,id;
            c(tag.Length==99&&LedgerMarkerIdentity.Parse(tag,out scope,out id)&&id==d.IdentityId,"bounded versioned UUID/scope/checksum payload parses");
            c(Strip(tag)=="","actual installed NGUI StripSymbols consumes the complete payload with zero visible characters");
            string projected=m.Project(d).Label;coordinator.Sync(b,d.World,d.Player);
            c(Strip(d.Native.Label)==Strip(projected),"actual native parser leaves approved visible Buy price/stock unchanged");
            c(d.Native.Label.EndsWith(tag)&&!m.Project(d).Label.Contains(tag),"only native projection carries identity; journal text remains clean");
            var round=Copy(m.Data);c(round.IdentityScope==m.Data.IdentityScope&&round.Destinations[0].IdentityId==d.IdentityId,"JSON save/reload preserves namespace and logical UUID");
            c(LedgerMarkerIdentity.Parse(Copy(d.Native).Label,out scope,out id),"native-text DTO serialization retains tag");
            c(!LedgerMarkerIdentity.Parse(tag.Substring(0,tag.Length-1),out scope,out id),"truncated tag never proves ownership");
            c(!LedgerMarkerIdentity.Parse(tag.Replace(tag.Substring(9,1),"Q"),out scope,out id),"non-hex payload rejected");
            c(!LedgerMarkerIdentity.Parse(tag.Replace("[544C31]","[544C32]"),out scope,out id),"unknown version rejected");
            string corrupt=tag.Substring(0,67)+(tag[67]=='0'?"1":"0")+tag.Substring(68);c(!LedgerMarkerIdentity.Parse(corrupt,out scope,out id),"checksum rejects changed scope/identity");
            c(!LedgerMarkerIdentity.Parse("Base [FFFFFF] ",out scope,out id),"ordinary colored player label is not ownership evidence");
            c(!LedgerMarkerIdentity.Parse(tag+tag,out scope,out id),"duplicate/malformed payload rejected");
            c(LedgerMarkerIdentity.Parse(tag.ToLowerInvariant(),out scope,out id),"case normalization preserves identity");
            c(!LedgerMarkerIdentity.Parse("long"+new string('x',2048)+tag,out scope,out id),"oversized labels fail closed");
            var old=Copy(d.Native);b.Map.Remove(old.Id);old.Id=777;b.Map.Add(old.Id,old);old.Label="Old patch label"+tag;
            var loaded=new LedgerMarkerModel(round);var current=loaded.Data.Destinations[0];var reload=new LedgerMarkerCoordinator(loaded);reload.Sync(b,d.World,d.Player);
            c(current.Native.Id==777&&b.Creates==1&&b.Map.Count==1,"changed runtime ID and old visible label rebind instead of duplicate creation");
            c(Strip(current.Native.Label)==Strip(projected),"old visible label refreshes in place with identity intact");
            int updates=b.Updates;for(int i=0;i<12;i++)reload.Sync(b,d.World,d.Player);c(b.Creates==1&&b.Updates==updates,"repeated reconciliation is write-idempotent");
            for(int i=0;i<3;i++){loaded=new LedgerMarkerModel(Copy(loaded.Data));current=loaded.Data.Destinations[0];reload=new LedgerMarkerCoordinator(loaded);reload.Sync(b,d.World,d.Player);}
            c(b.Map.Count==1&&b.Creates==1,"three full JSON reload/reconcile cycles retain one physical marker");
            var duplicate=Copy(current.Native);duplicate.Id=778;duplicate.Label="Sell: old - recheck"+tag;b.Map.Add(778,duplicate);reload.Sync(b,d.World,d.Player);
            c(b.Map.Count==1&&b.Deletes==1&&current.Native.Id==777,"only validated same-identity duplicates are removed despite changed labels");
            var baseMarker=Copy(current.Native);baseMarker.Id=800;baseMarker.Label="My base";b.Map.Add(800,baseMarker);
            var invalid=Copy(baseMarker);invalid.Id=801;invalid.Label=corrupt;b.Map.Add(801,invalid);reload.Sync(b,d.World,d.Player);
            c(b.Map.ContainsKey(800)&&b.Map.ContainsKey(801),"untagged base and invalid-tag markers remain untouched even at identical coordinates");
            var moved=Copy(current.Native);moved.Id=802;moved.X+=100;b.Map.Add(802,moved);int beforeMoved=b.Creates;reload.Sync(b,d.World,d.Player);
            c(current.Conflict&&b.Creates==beforeMoved&&b.Map.ContainsKey(802),"copied/moved valid tag is not sufficient target evidence; no mutation or duplicate");
            b.Map.Remove(802);reload.Sync(b,d.World,d.Player);c(!current.Conflict&&current.Native.Id==777,"resolved target conflict rebinds without a new marker");
            current.Alias="Renamed destination";current.Pinned=true;reload.Sync(b,d.World,d.Player);
            c(current.Native.Id==777&&current.Native.Label.EndsWith(tag)&&!current.Native.Approach,"alias/flags changes preserve identity and native ID");
            var otherModel=new LedgerMarkerModel();var other=otherModel.Ensure(Plan(),1002);
            c(LedgerMarkerIdentity.Scope(otherModel.Data,d.World,d.Player)!=LedgerMarkerIdentity.Scope(loaded.Data,d.World,d.Player),"new save document with identical world/name does not share ownership namespace");
            c(!LedgerMarkerIdentity.Owned(otherModel.Data,other,current.Native),"foreign save's valid tag is never adopted");
            var foreignBackend=new Native();foreignBackend.Map[current.Native.Id]=Copy(current.Native);other.Native=Copy(current.Native);other.Purposes[0].Visibility=-1;new LedgerMarkerCoordinator(otherModel).Sync(foreignBackend,d.World,d.Player);
            c(foreignBackend.Map.Count==1&&foreignBackend.Deletes==0&&foreignBackend.Updates==0,"foreign valid tag is not treated as legacy migration proof or deleted");
            c(LedgerMarkerIdentity.Scope(loaded.Data,d.World,1003)!=LedgerMarkerIdentity.Scope(loaded.Data,d.World,d.Player),"player separation");
            c(LedgerMarkerIdentity.Scope(loaded.Data,"other-world",d.Player)!=LedgerMarkerIdentity.Scope(loaded.Data,d.World,d.Player),"world separation");
            var second=loaded.Ensure(Plan(11),1002);c(LedgerMarkerIdentity.Tag(loaded.Data,second)!=tag,"distinct destinations receive distinct identities");
            second.Purposes[0].Visibility=-1;reload.Sync(b,d.World,d.Player);c(second.Native==null,"hidden destination never activated by identity adoption");
            current.Purposes[0].Visibility=-1;reload.Sync(b,d.World,d.Player);
            c(current.Native==null&&!b.Map.ContainsKey(777)&&b.Map.ContainsKey(800),"inactive trade waypoint removed without touching base or route memory");
            b.Map.Add(777,Copy(old));reload.Sync(b,d.World,d.Player);c(!b.Map.ContainsKey(777)&&current.Native==null,"late saved inactive waypoint cleaned without reactivation");
            current.Purposes[0].Visibility=1;reload.Sync(b,d.World,d.Player);int active=current.Native.Id;b.Map.Remove(active);reload.Sync(b,d.World,d.Player);
            c(current.Native!=null&&current.Native.Id!=active,"active native disappearance still recreates without implicit suppression");
            var detached=Copy(current.Native);loaded.Detach(current);reload.Sync(b,d.World,d.Player);
            c(b.Map.ContainsKey(detached.Id)&&current.Native.Id!=detached.Id,"explicit detach protects released native identity and uses a new identity");
            var deleted=Copy(current.Native);current.DeleteRequested=true;reload.Sync(b,d.World,d.Player);c(!loaded.Data.Destinations.Contains(current),"explicit logical Delete removes the owned destination");
            var orphan=Copy(deleted);orphan.Id=850;b.Map.Add(850,orphan);reload.Sync(b,d.World,d.Player);
            c(!b.Map.ContainsKey(850)&&b.Map.ContainsKey(detached.Id)&&b.Map.ContainsKey(800),"late orphan cleanup is scoped; detached and independent personal markers survive");
            Legacy(c);Delayed(c);RequestAndService(c);
            string folder=Path.Combine(root,"marker-identity");Directory.CreateDirectory(folder);
            var repo=new LedgerMarkerRepository(Path.Combine(folder,"LedgerMarkers.json"));repo.Save(m.Data);c(repo.Wait(5000),"normal atomic save-local repository publishes additive identity fields");
            var reopened=new LedgerMarkerModel(repo.Read());c(reopened.Data.IdentityScope==m.Data.IdentityScope&&reopened.Data.Destinations[0].IdentityId==m.Data.Destinations[0].IdentityId,"real repository reopen preserves document and destination identities");
            File.WriteAllBytes(Path.Combine(folder,"evidence.json"),LedgerJson.Encode(new Dictionary<string,string>{{"checks",count.ToString()},{"nativeParser","installed NGUIText.StripSymbols; non-game execution"},{"tagCharacters","99"},{"visibleCharacters",Strip(tag).Length.ToString()},{"liveUnity","NOT RUN"}}));
            Console.WriteLine("MARKER IDENTITY CHECKS "+count);
        }
        private static void Legacy(Action<bool,string> c)
        {
            var m=new LedgerMarkerModel();var d=m.Ensure(Plan(),1002);var b=new Native();new LedgerMarkerCoordinator(m).Sync(b,d.World,d.Player);
            string raw=m.Project(d).Label;d.Native.Label=raw;b.Map[d.Native.Id].Label=raw;var old=Copy(d.Native);b.Map.Clear();old.Id=999;b.Map.Add(999,old);
            new LedgerMarkerCoordinator(m).Sync(b,d.World,d.Player);
            c(d.Native.Id==999&&b.Creates==1&&d.Native.Label.EndsWith(LedgerMarkerIdentity.Tag(m.Data,d)),"unique complete legacy receipt survives ID churn and is tagged in place");
            var n=new LedgerMarkerModel();var target=n.Ensure(Plan(),1002);target.Native=Copy(old);var backend=new Native();backend.Map[999]=Copy(old);var dup=Copy(old);dup.Id=1000;backend.Map[1000]=dup;target.Native.Id=1;
            new LedgerMarkerCoordinator(n).Sync(backend,target.World,target.Player);
            c(target.Conflict&&backend.Creates==0&&backend.Deletes==0,"ambiguous untagged exact twins are neither deleted nor joined by a third marker");
            var legacy=Copy(n.Data);legacy.IdentityScope=null;foreach(var dest in legacy.Destinations)dest.IdentityId=null;
            var migrated=new LedgerMarkerModel(legacy);c(LedgerMarkerIdentity.ValidId(migrated.Data.IdentityScope)&&migrated.Data.Destinations.All(x=>LedgerMarkerIdentity.ValidId(x.IdentityId)),"missing legacy metadata migrates additively without dropping purposes");
            string identity=migrated.Data.Destinations[0].IdentityId;migrated=new LedgerMarkerModel(Copy(migrated.Data));c(migrated.Data.Destinations[0].IdentityId==identity,"migration is not regenerated on restart");
        }
        private static void Delayed(Action<bool,string> c)
        {
            var m=new LedgerMarkerModel();var d=m.Ensure(Plan(),1002);var b=new Native();var coord=new LedgerMarkerCoordinator(m);coord.Sync(b,d.World,d.Player);var saved=Copy(b.Map.Values.Single());saved.Id=900;
            b.Map.Clear();b.Fence=new LedgerMarkerLoadFence();d.Native=null;
            for(int i=0;i<20;i++){try{coord.Sync(b,d.World,d.Player);}catch(LedgerMarkerEnumerationPendingException){}}
            c(b.Creates==1,"arbitrarily delayed initial enumeration cannot create duplicates");
            b.Fence.Completed(new[]{saved});try{coord.Sync(b,d.World,d.Player);}catch(LedgerMarkerEnumerationPendingException){}
            c(b.Creates==1&&!b.Fence.Ready,"DB result alone is insufficient until native local collection is populated");
            b.Map.Add(saved.Id,saved);coord.Sync(b,d.World,d.Player);c(d.Native.Id==900&&b.Creates==1,"late actual marker adoption completes readiness without creation");
            var empty=new LedgerMarkerLoadFence();empty.Completed(new List<LedgerMarkerNative>());c(empty.Admit(new Dictionary<int,LedgerMarkerNative>()),"successful explicitly empty native SELECT is distinct from pending");
            var failed=new LedgerMarkerLoadFence();failed.Completed(null);c(!failed.Admit(new Dictionary<int,LedgerMarkerNative>()),"unavailable enumeration is never empty success");
        }
        private static void RequestAndService(Action<bool,string> c)
        {
            var m=new LedgerMarkerModel();var b=new Native();var coord=new LedgerMarkerCoordinator(m);var p=Plan();p.ItemId=0;p.RequestBody="Bring 25 Copper";p.RequestKey="occurrence-one";p.DeliveryText="Delivery 25 Copper";
            var d=m.Ensure(p,1002);coord.Sync(b,d.World,d.Player);int id=d.Native.Id;d.Purposes[0].RequestCompleted=true;coord.Sync(b,d.World,d.Player);
            c(d.Native==null&&!b.Map.ContainsKey(id),"Done request deletes its tagged native waypoint");
            m=new LedgerMarkerModel(Copy(m.Data));coord=new LedgerMarkerCoordinator(m);coord.Sync(b,d.World,d.Player);c(m.Data.Destinations[0].Native==null,"Done survives reload without recreating stale marker");
            var service=m.Ensure(Plan(12),1002);var purpose=service.Purposes[0];purpose.Service=true;purpose.ServiceAutoVisible=true;purpose.Visibility=0;purpose.UngroupedShown=false;coord.Sync(b,service.World,service.Player);
            c(service.Native!=null&&!service.Native.Approach,"service identity preserves persistent approach flags");
            int serviceId=service.Native.Id;b.Map.Remove(serviceId);coord.Sync(b,service.World,service.Player);c(service.Native!=null,"eligible service marker recreates after native disappearance");
            purpose.ServiceAutoVisible=false;purpose.Visibility=-1;coord.Sync(b,service.World,service.Player);c(!service.Native.Hud&&!service.Native.Universe,"suppressed/inactive service stays navigation-hidden when tagged");
            int creates=b.Creates;m=new LedgerMarkerModel(Copy(m.Data));coord=new LedgerMarkerCoordinator(m);coord.Sync(b,service.World,service.Player);c(b.Creates==creates&&!m.Data.Destinations.Last().Native.Hud,"service suppression remains hidden across reload");
        }
    }
}
