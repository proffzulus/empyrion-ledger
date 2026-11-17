using System;
using System.IO;
using System.Linq;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal static class LedgerDepositHudTests
    {
        private static T Copy<T>(T v){return LedgerJson.Decode<T>(LedgerJson.Encode(v));}
        private static LedgerObservation Deposit(string save="scanner-owned",string pf="Surface",int id=1,int player=42)
        {
            var c=LedgerContext.Create(Path.Combine(Path.GetTempPath(),save),pf,null,"SinglePlayer");
            var p=new LedgerProductionSnapshot{Kind="deposit",Player=player,Definition=10,DepositId=id,Resource="Iron",Device="Iron",X=id*100,Y=20,Z=30,Complete=true,Tick=100,Epoch=c.WorldKey};p.Key=LedgerProduction.Key(c,p);
            return new LedgerObservation{Kind="production",Context=c,Production=p,Identity=new LedgerIdentity(),Utc="2026-11-09T12:00:00.0000000Z",Source="owned synthetic HUD fixture"};
        }
        public static void Run(Action<bool,string> check,string root)
        {
            int n=0;Action<bool,string> test=(ok,message)=>{n++;check(ok,"scanner deposits: "+message);};
            string body="Iron Deposit\nSize: Small\nRemaining: 100%";
            var d=LedgerDepositDetails.Read(body,body+"\n1.4 km","Small",true);
            test(d!=null&&d.Size=="Small"&&d.PercentageText=="100%"&&d.RemainingPercent==100,"published detail body admits exact displayed values, distance excluded");
            test(LedgerDepositDetails.Read(body,body,"Small",false)==null,"hidden UI never admits cached details");
            test(LedgerDepositDetails.Read(body,"Iron Deposit\n1.4 km","Small",true)==null,"identity-only HUD cannot publish cached detail body");
            test(LedgerDepositDetails.Read(null,body,"Small",true)==null,"missing body rejected");
            test(LedgerDepositDetails.Read(body,body.Replace("100%","80%"),"Small",true)==null,"mixed publication rejected");
            test(LedgerDepositDetails.Read("Iron Deposit","Iron Deposit","Small",true)==null,"discovery name supplies neither size nor assumed 100 percent");
            var p=d;d=LedgerDepositDetails.Read("Size: Medium","Size: Medium","Medium",true);test(d.Size=="Medium"&&d.PercentageText==null&&d.RemainingPercent==null,"size-only display leaves percentage unknown");
            d=LedgerDepositDetails.Read("Remaining: 60%","Remaining: 60%","Small",true);test(d.Size==null&&d.RemainingPercent==60,"percentage-only display does not infer size");
            d=LedgerDepositDetails.Read("Size: Smallish","Size: Smallish","Small",true);test(d==null,"size must be the shown band, not a substring");
            foreach(string size in new[]{"Small","Medium","Малое","Mittel","Pequeño"}){d=LedgerDepositDetails.Read("Ore\n"+size+" 70%","Ore\n"+size+" 70%",size,true);test(d.Size==size&&d.RemainingPercent==70,"localized band retained: "+size);}
            d=LedgerDepositDetails.Read("[c][FFFFFF]Mittel 62,5%[-][/c]","[c][FFFFFF]Mittel 62,5%[-][/c]\n2 km","Mittel",true);test(d.Size=="Mittel"&&d.RemainingPercent==62.5,"NGUI colors and localized decimal preserve shown semantics");
            d=LedgerDepositDetails.Read("Small <5%","Small <5%","Small",true);test(d.PercentageText=="<5%"&&d.RemainingPercent==null,"native rounded <5 percent is not precise zero or five");
            foreach(string bad in new[]{"101%","999%","-10%","30% 80%","no percentage"})test(LedgerDepositDetails.Read(bad,bad,null,true)==null,"invalid/ambiguous percentage stays unknown: "+bad);
            test(LedgerDepositDetails.Read(new string('x',4097),new string('x',4097),"Small",true)==null,"payload bounded");
            var o=Deposit();p.Apply(o.Production,100,"visible scanner HUD fixture");
            var repeated=Copy(o);repeated.Production.Tick=120;p.Apply(repeated.Production,120,"visible scanner HUD fixture");test(LedgerProduction.ContentKey(o.Production)==LedgerProduction.ContentKey(repeated.Production),"unchanged HUD frames deduplicate despite new capture timestamps");
            var second=Deposit(id:2);LedgerDepositDetails.Read("Large 60%","Large 60%","Large",true).Apply(second.Production,100,"visible scanner HUD fixture");test(second.Production.Key!=o.Production.Key,"same-resource deposits retain separate native ID/position identity");
            test(Deposit(pf:"Moon").Production.Key!=o.Production.Key,"playfield transition isolates detail identity");test(Deposit(save:"scanner-other").Production.Key!=o.Production.Key,"save transition isolates identity");test(Deposit(player:43).Production.Key!=o.Production.Key,"local player transition isolates identity");
            var identity=Copy(o);identity.Production.Size=null;identity.Production.PercentageText=null;identity.Production.RemainingPercent=null;identity.Production.RawUi=null;identity.Production.DetailsTick=null;
            LedgerDepositDetails.Preserve(identity.Production,o.Production);test(identity.Production.Size=="Small"&&identity.Production.RemainingPercent==100&&identity.Production.DetailsTick==100,"identity-only discovery retains known details and their time");
            var partial=Copy(o);LedgerDepositDetails.Read("Medium","Medium","Medium",true).Apply(partial.Production,200,"visible Map fixture");LedgerDepositDetails.Preserve(partial.Production,o.Production);
            test(partial.Production.Size=="Medium"&&partial.Production.RemainingPercent==100&&partial.Production.SizeTick==200&&partial.Production.PercentageTick==100,"partial shown update preserves absent field with separate field time");
            var stale=Copy(o);LedgerDepositDetails.Read("Large 30%","Large 30%","Large",true).Apply(stale.Production,50,"older HUD fixture");LedgerDepositDetails.Preserve(stale.Production,o.Production);test(stale.Production.Size=="Small"&&stale.Production.RemainingPercent==100&&stale.Production.DetailsTick==100,"late stale details cannot overwrite newer shown values");
            string folder=Path.Combine(root,"scanner-deposit-store");using(var db=new LedgerDatabase(folder)){test(db.Save(o)=="committed","shown HUD uses normal durable storage");test(db.Save(second)=="committed","second same-resource deposit persists independently");var map=Copy(partial);map.Utc="2026-11-09T12:01:00.0000000Z";test(db.Save(map)=="committed","map fallback merges normally");var unseen=Copy(identity);unseen.Utc="2026-11-09T12:02:00.0000000Z";unseen.Production.DetailsTick=null;unseen.Production.Size=null;unseen.Production.PercentageText=null;test(db.Save(unseen)=="committed","later identity-only event preserves data in storage");}
            {var rows=LedgerViewData.Read(Path.Combine(folder,LedgerDatabase.FileName),o.Context.WorldKey,null,42).Production;var saved=rows.Single(x=>x.Production.Key==o.Production.Key);test(rows.Count==2&&saved.Production.Size=="Medium"&&saved.Production.PercentageText=="100%"&&saved.Production.DetailsTick==200,"read/restart retains per-deposit merged HUD/map details");test(rows.Single(x=>x.Production.Key==second.Production.Key).Production.RemainingPercent==60,"same-resource neighbor retains its own separately shown percentage");}
            var zero=Copy(o);LedgerDepositDetails.Read("Small 0%","Small 0%","Small",true).Apply(zero.Production,300,"shown zero");test(zero.Production.RemainingPercent==0&&LedgerProductionDisplay.DepositName(zero.Production).Contains("0%"),"observed exhausted deposit remains visible and zero is meaningful");
            var afterZero=Deposit();LedgerDepositDetails.Preserve(afterZero.Production,zero.Production);test(afterZero.Production.RemainingPercent==0&&afterZero.Production.PercentageText=="0%","identity-only rediscovery cannot erase observed exhaustion");
            var percentOnly=Copy(o);LedgerDepositDetails.Read("40%","40%",null,true).Apply(percentOnly.Production,400,"HUD percent-only");LedgerDepositDetails.Preserve(percentOnly.Production,o.Production);test(percentOnly.Production.Size=="Small"&&percentOnly.Production.SizeTick==100&&percentOnly.Production.RemainingPercent==40,"percentage-only update preserves earlier shown localized size and its timestamp");
            var unknown=Deposit();test(LedgerProductionDisplay.DepositName(unknown.Production).Contains("unknown"),"unobserved remaining remains explicitly unknown");
            string checkout=AppDomain.CurrentDomain.BaseDirectory;while(checkout!=null&&!File.Exists(Path.Combine(checkout,"PROJECT_BRIEF.md")))checkout=Path.GetDirectoryName(checkout.TrimEnd(Path.DirectorySeparatorChar));
            string source=File.ReadAllText(Path.Combine(checkout,"src/TradeLedgerProbeB/LedgerDepositHudNative.cs"));
            test(source.Contains("0xab6e")&&source.Contains("0x3368")&&source.Contains("0x332c")&&source.Contains("0x505f"),"native binding uses published view and exact marker/resource identity, never resource-name matching");
            test(source.Contains("HudCurrent(entry,controller,root,world)")&&source.Contains("views.Contains(marker)")&&source.Contains("label.text==rendered"),"world and publication are revalidated after copying");
            test(source.Contains("hudWork.Take(publicationCount,out start)")&&source.Contains("views.Count>1024")&&source.Contains("label.isVisible"),"published-only traversal is bounded and visibility-gated");
            string native=File.ReadAllText(Path.Combine(checkout,"src/TradeLedgerProbeB/LedgerProductionNative.cs"));test(native.Contains("Window(\"Map\"")&&native.Contains("ScannerDeposits(c,w,player,ticks)"),"scanner polling and Map fallback both remain wired");test(!native.Contains("0x5065")&&!native.Contains("0x5066")&&!native.Contains("0x506e"),"observer no longer reads hidden deposit depletion counters");
            Console.WriteLine("Scanner deposit checks passed: "+n+" (offline/native-contract fixtures; no live HUD execution)");
        }
    }
}
