using System;
using System.IO;
using System.Linq;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal static class LedgerDepositDiscoveryTests
    {
        private static LedgerDepositDiscovery Bound()
        {
            object world=new object(),actor=new object(),resource=new object(),marker=new object();
            return new LedgerDepositDiscovery{ConfirmedEvent=true,Player=42,ActorPlayer=42,Actor=actor,LocalActor=actor,World=world,CurrentWorld=world,
                Scope="saveA|worldA|planetA|42",CurrentScope="saveA|worldA|planetA|42",Resource=resource,MarkerResource=resource,Marker=marker,ResourceMarker=marker,RegisteredMarker=marker,Discovered=true,ResourceId=7,MarkerId=7};
        }
        private static LedgerObservation Observation(int id=7)
        {
            var c=LedgerContext.Create(Path.Combine(Path.GetTempPath(),"owned-offscreen-save"),"PlanetA",null,"SinglePlayer");
            var p=new LedgerProductionSnapshot{Kind="deposit",Player=42,DepositId=id,Definition=10,Resource="Iron",Device="Iron",Complete=true,X=id*100,Y=20,Z=30,Epoch=c.WorldKey,Tick=100};p.Key=LedgerProduction.Key(c,p);
            return new LedgerObservation{Kind="production",Context=c,Identity=new LedgerIdentity(),Production=p,Utc="2026-11-09T15:00:00.0000000Z",Source="owned offscreen discovery fixture"};
        }
        public static void Run(Action<bool,string> check,string root)
        {
            int count=0;Action<bool,string> test=(ok,label)=>{count++;check(ok,"offscreen discovery: "+label);};
            string body="Iron Deposit\nSmall\n100%";var b=Bound();
            test(b.Admitted(),"exact local event/resource/current-world/discovered-marker binding admitted");
            var details=b.Read(body,"Small");test(details!=null&&details.Size=="Small"&&details.RemainingPercent==100,"offscreen event uses refreshed native UI cache, no visible UILabel required");
            test(b.Read(body,"Small")!=null,"event before HUD GameObject/label publication is independent of viewport lifecycle");
            foreach(string reason in new[]{"event","actor-id","actor-reference","world","scope","missing-scope","resource","missing-marker","direct-marker","registry-marker","undiscovered","id","negative-id","missing-world"}){
                b=Bound();switch(reason){case "event":b.ConfirmedEvent=false;break;case "actor-id":b.ActorPlayer=43;break;case "actor-reference":b.LocalActor=new object();break;case "world":b.CurrentWorld=new object();break;case "scope":b.CurrentScope="saveB|worldA|planetA|42";break;case "missing-scope":b.Scope=null;break;case "resource":b.MarkerResource=new object();break;case "missing-marker":b.Marker=null;break;case "direct-marker":b.ResourceMarker=new object();break;case "registry-marker":b.RegisteredMarker=null;break;case "undiscovered":b.Discovered=false;break;case "id":b.MarkerId++;break;case "negative-id":b.ResourceId=b.MarkerId=-1;break;case "missing-world":b.World=null;break;}
                test(!b.Admitted()&&b.Read(body,"Small")==null,"reject "+reason+" despite populated cache");
            }
            b=Bound();b.ConfirmedEvent=false;test(b.Read(body,"Small")==null,"constructor's precomputed body/discovered flag alone cannot replay on later polling");
            b=Bound();b.ResourceMarker=null;b.RegisteredMarker=null;test(b.Read(body,"Small")==null,"missing logical marker at discovery stays unknown rather than trusting later untimestamped publication");
            b=Bound();test(b.Read(null,"Small")==null&&b.Read("Iron Deposit","Small")==null,"empty/identity-only cache never infers size/100 percent");
            foreach(string size in new[]{"Small","Medium","Mittel","Pequeño","Малое"}){var d=Bound().Read("Iron\n"+size+"\n70%",size);test(d!=null&&d.Size==size&&d.RemainingPercent==70,"retain localized UI size: "+size);}
            var low=Bound().Read("Small <5%","Small");test(low.PercentageText=="<5%"&&low.RemainingPercent==null,"native <5 percent retained literally, not precise zero");
            foreach(string value in new[]{"0%","10%","20%","50%","90%","100%"}){var d=Bound().Read("Small "+value,"Small");test(d.PercentageText==value,"retain native rounded percentage "+value);}
            test(Bound().Read("Small","Small").RemainingPercent==null,"size-only cache does not invent percentage");
            test(Bound().Read("60%","Small").Size==null,"percentage-only cache does not invent size");
            test(Bound().Read("Small 999%","Small").RemainingPercent==null,"invalid cached percentage never becomes numeric fact");
            var o=Observation();details.Apply(o.Production,100,"local ResourceDiscovered23 fixture");
            var again=Observation();details.Apply(again.Production,120,"local ResourceDiscovered23 fixture");test(LedgerProduction.ContentKey(o.Production)==LedgerProduction.ContentKey(again.Production),"repeated unchanged discoveries deduplicate content");
            var neighbor=Observation(8);Bound().Read("Medium 80%","Medium").Apply(neighbor.Production,100,"local ResourceDiscovered23 fixture");test(o.Production.Key!=neighbor.Production.Key,"same-resource deposits use exact ID/definition/position key");
            var unknown=Observation();LedgerDepositDetails.Preserve(unknown.Production,o.Production);test(unknown.Production.Size=="Small"&&unknown.Production.DetailsTick==100,"missing publication after known discovery retains known details/time");
            var stale=Observation();Bound().Read("Medium 40%","Medium").Apply(stale.Production,50,"older discovery fixture");LedgerDepositDetails.Preserve(stale.Production,o.Production);test(stale.Production.RemainingPercent==100&&stale.Production.Size=="Small","older detail tick cannot overwrite newer known snapshot");
            var partial=Observation();Bound().Read("Medium","Medium").Apply(partial.Production,130,"partial discovery fixture");LedgerDepositDetails.Preserve(partial.Production,o.Production);test(partial.Production.Size=="Medium"&&partial.Production.PercentageText=="100%"&&partial.Production.PercentageTick==100,"partial cache preserves earlier percentage provenance");
            string folder=Path.Combine(root,"offscreen-discovery-store");using(var db=new LedgerDatabase(folder)){test(db.Save(o)=="committed"&&db.Save(neighbor)=="committed","offscreen observations use existing isolated durable storage");var rediscovery=Observation();rediscovery.Utc="2026-11-09T15:01:00.0000000Z";test(db.Save(rediscovery)=="committed","identity-only rediscovery still commits without deleting details");}
            var rows=LedgerViewData.Read(Path.Combine(folder,LedgerDatabase.FileName),o.Context.WorldKey,null,42).Production;
            test(rows.Count==2&&rows.Single(x=>x.Production.DepositId==7).Production.PercentageText=="100%"&&rows.Single(x=>x.Production.DepositId==8).Production.Size=="Medium","reload preserves separate discovered deposits and known values");
            test(LedgerDepositDetails.Read(body,body,"Small",false)==null,"visible-HUD parser gate remains closed when hidden; only separate event proof admits offscreen");
            string checkout=AppDomain.CurrentDomain.BaseDirectory;while(checkout!=null&&!File.Exists(Path.Combine(checkout,"PROJECT_BRIEF.md")))checkout=Path.GetDirectoryName(checkout.TrimEnd(Path.DirectorySeparatorChar));
            string native=File.ReadAllText(Path.Combine(checkout,"src/TradeLedgerProbeB/LedgerProductionNative.cs"));
            test(native.Contains("0x506f")&&native.Contains("0x377d")&&native.Contains("0x3331")&&native.Contains("proof.CurrentWorld=World(c,app,player)"),"native consumer checks exact direct/registered discovered marker and revalidates current world");
            test(!native.Contains("Invoke(0x37b1")&&!native.Contains("Invoke(0x37b8")&&!native.Contains("Invoke(0x3754")&&!native.Contains("0x5065")&&!native.Contains("0x5066"),"no mutating marker formatter or precise depletion counter access");
            test(native.Contains("Window(\"Map\"")&&native.Contains("ScannerDeposits(c,w,player,ticks)"),"Map and visible HUD fallbacks unchanged");
            test(native.Contains("type==23&&runtimeEnabled")&&native.Contains("new LedgerProductionNative(application.GetType().Assembly,application)"),"first admitted discovery initializes source instead of losing event before periodic poll");
            Console.WriteLine("Offscreen discovery checks passed: "+count+"; synthetic/API-contract fixtures, no live Unity execution.");
        }
    }
}
