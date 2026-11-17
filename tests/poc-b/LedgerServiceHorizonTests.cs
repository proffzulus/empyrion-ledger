using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TradeLedgerProbeB;
namespace BOfflineTests
{
    internal static class LedgerServiceHorizonTests
    {
        private static T Copy<T>(T value){return LedgerJson.Decode<T>(LedgerJson.Encode(value));}
        private static LedgerObservation Device(long stock=1595,uint period=2400,double fuelHours=55,string place="Planet A",int id=1)
        {
            var c=LedgerContext.Create(Path.Combine(Path.GetTempPath(),"owned-horizon-fixture"),place,"Fixture System","SinglePlayer");
            var p=new LedgerProductionSnapshot{Kind="producer",Complete=true,Family="AutoMiner",Device="Auto Mining Device (Basic)",DefinitionName="AutoMiningDeviceT1",Definition=1108,Player=1002,Owner="player:1002",Block=id+",0,0",X=id,Y=2,Z=3,NavigationPosition=true,PositionSource="owned fixture",Epoch=c.WorldKey,Tick=1000,FuelTick=1000,FuelSeconds=fuelHours*3600,Capacity=1750,Period=period,NextTick=1000+period,Running=true,NextItem=40,Outputs=new List<LedgerProductionOutput>{new LedgerProductionOutput{Item=40,Auxiliary=0,Name="Neodymium Ore",Stock=stock}}};
            p.Key=LedgerProduction.Key(c,p);return new LedgerObservation{Kind="production",Context=c,Production=p,Utc="2026-11-06T00:00:00.0000000Z"};
        }
        private static LedgerServiceState State(LedgerObservation o,ulong? tick=null){return LedgerProductionServices.State(o.Production,tick??o.Production.Tick,o.Context.WorldKey);}
        internal static void Run(Action<bool,string> check,string root)
        {
            Action<bool,string> ok=(value,name)=>check(value,"service horizon "+name);
            ok(LedgerProductionDue.BufferSeconds(1750,1595,31)==5*3600,"1750 minus1595 at actual31/h gives exactly5 hours");
            ok(Math.Abs(LedgerProductionDue.BufferSeconds(1750,1595,50).Value/3600-3.1)<1e-9,"same free155 at50/h gives3.1 hours");
            ok(Math.Abs(LedgerProductionDue.BufferSeconds(1750,1595,17).Value/3600-155d/17)<1e-9,"same free155 at17/h gives155/17 hours");
            var h=new LedgerServiceThreshold{Hours=5};var fast=Device(period:1440);var slow=Device(period:4235,id:2);
            ok(State(fast).BufferAlert(h)&&!State(slow).BufferAlert(h),"same tier/capacity/stock differing native periods50 versus17/h produce different alerts");
            fast.Production.ShownQuality="1% (1/hour)";
            ok(State(fast).Due.Seconds==3.1*3600,"certified native period drives calculation, not tier or rounded/misleading display rate");
            ok(State(fast).Seconds==55*3600&&State(fast).Alert(h),"55h fuel does not prevent buffer-first warning inside5h");
            fast.Production.FuelSeconds=2*3600;
            ok(State(fast).Due.Seconds==2*3600&&State(fast).FuelAlert(h),"earlier fuel exhaustion wins over buffer");
            fast.Production.FuelSeconds=3.1*3600;
            ok(State(fast).Due.Hint=="Storage and fuel","exact coincident limits retain both causes");
            var boundary=Device(stock:790,period:1200);h.Hours=16;
            ok(State(boundary).Due.Seconds==16*3600&&!State(boundary).Alert(h)&&!State(boundary).BufferAlert(h),"strict16h storage equality is NOT a warning");
            ok(State(boundary,1001).Alert(h),"one game tick below16h storage boundary IS warning");
            boundary.Production.FuelSeconds=16*3600;
            ok(!State(boundary).Alert(h)&&!State(boundary).FuelAlert(h),"strict equality for fuel and storage tie is NOT warning");
            ok(State(boundary,1001).FuelAlert(h)&&State(boundary,1001).Alert(h),"one game tick below fuel threshold warns coherently");
            h.Hours=20;ok(State(boundary).Alert(h),"20h expedition horizon can include16h downtime");
            h.Hours=5;ok(!State(boundary).Alert(h),"near-base5h horizon does not warn16h downtime");
            foreach(string number in new[]{"0","0.05","5","16","20","55","1000000"}){double n;ok(LedgerServicePreferences.Number(number,false,out n),"valid configurable hours without5h cap "+number);}
            foreach(string number in new[]{"-1","NaN","Infinity","garbage","1000001"}){double n;ok(!LedgerServicePreferences.Number(number,false,out n),"invalid hours rejected "+number);}
            var full=Device(stock:1750);ok(State(full).Due.Text=="Now"&&State(full).BufferAlert(h),"observed full buffer is zero time and warns for positive horizon");
            full.Production.Period=0;ok(State(full).Alert(h),"full buffer evidence does not need a guessed positive rate");
            var emptyFuel=Device();emptyFuel.Production.FuelSeconds=0;ok(State(emptyFuel).Due.Text=="Now"&&State(emptyFuel).FuelAlert(h),"exact shown empty fuel is known zero warning");
            h.Hours=0;ok(!State(emptyFuel).Alert(h)&&!State(full).Alert(h),"strict zero-hour threshold does not warn equal zero");h.Hours=16;
            foreach(double? rate in new double?[]{null,0,-1,double.NaN,double.PositiveInfinity})ok(!LedgerProductionDue.BufferSeconds(1750,1595,rate).HasValue,"missing/invalid/zero rate never becomes finite buffer horizon "+rate);
            foreach(uint? period in new uint?[]{0,uint.MaxValue}){var o=Device();o.Production.Period=period;ok(!State(o).Due.Seconds.HasValue&&!State(o).Alert(h),"unsupported zero/sentinel native production rate has no fabricated timing");}
            var noCapacity=Device();noCapacity.Production.Capacity=null;noCapacity.Production.FuelSeconds=3*3600;
            ok(State(noCapacity).Due.Partial&&State(noCapacity).FuelAlert(h),"known early fuel bound warns with capacity unknown");
            var noFuel=Device(period:1440);noFuel.Production.FuelSeconds=null;noFuel.Production.FuelTick=null;
            ok(State(noFuel).Due.Partial&&State(noFuel).BufferAlert(h),"known early buffer upper bound warns with fuel unknown");
            var rows=new[]{noFuel};var model=new LedgerMarkerModel();var prefs=LedgerProductionServices.Preferences(model,noFuel.Context.WorldKey,1002);prefs.Hours=1;
            var places=new LedgerProductionPlaces(noFuel.Context.WorldKey,rows.Select(o=>o.Context));var overview=LedgerProductionServices.Overview(rows,1000,noFuel.Context.WorldKey,prefs,places);
            ok(overview.ReserveUnknown==1&&overview.ReserveKnown==0,"partial bound above horizon cannot certify whole-device sufficient reserve");
            ok(!State(full,999).Alert(h)&&!LedgerProductionServices.State(full.Production,1000,"another-epoch").Alert(h),"rollback/save mismatch cannot turn old full observation into active warning");
            var multi=Device(stock:400,period:7200);multi.Production.Family="Generator mode 3";multi.Production.Capacity=1000;multi.Production.Alternating=true;multi.Production.Outputs.Add(new LedgerProductionOutput{Item=41,Auxiliary=0,Name="Purified Water",Stock=400});
            ok(State(multi).Due.Seconds==20*3600&&!State(multi).Alert(h),"two products share one remaining200 buffer and one10/h production stream");
            overview=LedgerProductionServices.Overview(new[]{multi},1000,multi.Context.WorldKey,new LedgerServicePreferences{Hours=16},new LedgerProductionPlaces(multi.Context.WorldKey,new[]{multi.Context}));
            ok(overview.Devices==1&&overview.MeanFill==80&&overview.Resources.Count==2&&overview.Resources.Sum(x=>x.Stock)==800,"informational fill/counts/resources preserved, multi-output device counted once");
            var visit=Device(period:1440);model=new LedgerMarkerModel();prefs=LedgerProductionServices.Preferences(model,visit.Context.WorldKey,1002);prefs.Hours=16;prefs.Fill=99;
            places=new LedgerProductionPlaces(visit.Context.WorldKey,new[]{visit.Context});
            Action evaluate=()=>LedgerProductionServices.Evaluate(model,new[]{visit},visit.Context,1002,1000,false,places);
            evaluate();var d=model.Find(LedgerProduction.Bookmark(visit,visit.Context),1002);var purpose=d.Purposes.Single();var route=model.Data.Routes.Single();
            ok(purpose.ServiceAutoVisible&&d.Pinned&&model.Project(d).Label.Contains("C9A1FF"),"time-to-full inside16h creates persistent purple service destination despite legacy99% setting");
            prefs.Fill=1;evaluate();ok(purpose.ServiceAutoVisible&&model.Data.Destinations.Count==1,"legacy percent edit has no threshold effect or duplicates");
            prefs.For("all",true).Suppress=true;evaluate();ok(!purpose.ServiceAutoVisible,"inherited suppression mutes qualifying time warning");
            model.ShowRoute(route,true);evaluate();ok(model.Project(d).Visible,"manual Show remains independent from time alert suppression");
            model.ShowRoute(route,false);prefs.For("all").Suppress=false;evaluate();ok(purpose.ServiceAutoVisible,"unsuppress restores still-qualifying warning without crossing");
            visit=Copy(visit);visit.ObservationId="actual-shown-collection";visit.Utc="2026-11-06T00:01:00.0000000Z";visit.Production.Outputs[0].Stock=0;evaluate();
            ok(purpose.ServiceServiced&&!purpose.ServiceAutoVisible&&!model.Project(d).Visible,"actual shown collection with55h fuel and35h buffer clears alert/run, retains device");
            visit.Production.Outputs[0].Stock=1595;evaluate();ok(!purpose.ServiceServiced&&purpose.ServiceAutoVisible,"renewed inside-horizon condition rearms existing service run");
            prefs.For("all").Suppress=true;var file=Path.Combine(root,"service-horizon-portable","LedgerMarkers.json");var repo=new LedgerMarkerRepository(file);repo.Save(model.Data);ok(repo.Wait(5000),"time threshold and suppression publish atomically on disposable fixture");
            var loaded=new LedgerMarkerModel(new LedgerMarkerRepository(file).Read());ok(loaded.Data.Services.Single().Hours==16&&loaded.Data.Services.Single().For("all").Suppress,"restart retains horizon/suppression without changing observed records");
            var equal=Device(stock:900,period:1200,fuelHours:1,id:9);equal.Production.Capacity=960;
            var equalModel=new LedgerMarkerModel();LedgerProductionServices.Preferences(equalModel,equal.Context.WorldKey,1002).Hours=16;
            LedgerProductionServices.Evaluate(equalModel,new[]{equal},equal.Context,1002,1000,true);
            var equalDest=equalModel.Find(LedgerProduction.Bookmark(equal,equal.Context),1002);var equalPurpose=equalDest.Purposes.Single();
            equal=Copy(equal);equal.ObservationId="new-shown-boundary-service";equal.Utc="2026-11-06T00:02:00.0000000Z";equal.Production.Outputs[0].Stock=0;equal.Production.FuelSeconds=16*3600;
            LedgerProductionServices.Evaluate(equalModel,new[]{equal},equal.Context,1002,1000,false);
            ok(equalPurpose.ServiceServiced&&!equalPurpose.ServiceAutoVisible&&!equalModel.Project(equalDest).Visible,"actual shown empty/refuel at exact16h equality completes manual run without warning");
            LedgerProductionServices.Evaluate(equalModel,new[]{equal},equal.Context,1002,1001,false);
            ok(!equalPurpose.ServiceServiced&&equalPurpose.ServiceAutoVisible,"one game tick below16h equality re-arms the current real service condition");
            string old="{\"World\":\"legacy-world\",\"Player\":1002,\"Enabled\":false,\"Fill\":5,\"Hours\":16,\"Overrides\":[{\"Key\":\"all\",\"Fill\":99,\"Hours\":20,\"Suppress\":true}],\"Deleted\":[\"old-tombstone\"]}";
            var legacy=LedgerJson.Decode<LedgerServicePreferences>(Encoding.UTF8.GetBytes(old));LedgerServicePreferences.Validate(legacy);var effective=legacy.Effective(new[]{"all"});
            ok(!legacy.Enabled&&effective.Hours==20&&effective.Suppressed&&legacy.Deleted.Single()=="old-tombstone","legacy migration preserves ON/OFF, hours overrides, suppression and tombstones");
            ok(typeof(LedgerServiceThreshold).GetField("Fill")==null&&legacy.Fill==5&&legacy.For("all").Fill==99,"retired percent values round-trip only, no effective threshold field");
            var round=LedgerJson.Decode<LedgerServicePreferences>(LedgerJson.Encode(legacy));ok(round.Hours==16&&round.For("all").Hours==20&&round.Fill==5,"non-destructive legacy migration is idempotent across serialization");
            var ui=File.ReadAllText(Path.Combine(Workspace(),"src","TradeLedgerProbeB","LedgerProductionWindow.cs"));ok(!ui.Contains("Global fill threshold")&&!ui.Contains("Fill percent")&&!ui.Contains("Override fill")&&ui.Contains("Service hours")&&ui.Contains("DrawProductionFill")&&!ui.Contains("Service >")&&ui.Contains("Service horizon "),"actual UI removes percent threshold editors but keeps informational fill bar");
            foreach(int width in new[]{1280,1920,3840})Preview(ok,root,width);
        }
        private static string Workspace(){string p=AppDomain.CurrentDomain.BaseDirectory;while(p!=null&&!File.Exists(Path.Combine(p,"PROJECT_BRIEF.md")))p=Path.GetDirectoryName(p.TrimEnd(Path.DirectorySeparatorChar));return p;}
        private static void Preview(Action<bool,string> ok,string root,int width)
        {
            using(var b=new Bitmap(width,width*9/16))using(var g=Graphics.FromImage(b))using(var f=new Font("Segoe UI",19,GraphicsUnit.Pixel))
            {g.ScaleTransform(width/1920f,width/1920f);g.Clear(Color.FromArgb(25,47,65));g.DrawString("Production | Devices     Alerts ON     Suppress [ ]     Settings     Mark group",f,Brushes.LightBlue,35,30);
                g.DrawString("Defaults   Service hours [16]        Planet A    [x] Override hours [20]",f,Brushes.White,35,75);
                g.DrawString("Fill and ore quantities remain informational",f,Brushes.Gainsboro,35,115);float y=180;
                foreach(var o in new[]{Device(period:1440),Device(period:4235,id:2),Device(stock:0,id:3)}){var state=State(o);g.FillRectangle(Brushes.MidnightBlue,35,y,1840,68);g.DrawString("Auto Miner T1    Neodymium    "+state.Estimate.Stocks.Values.Sum()+"/1750    "+LedgerProductionServices.DisplayedFill(state.Fill)+"%    "+LedgerProductionDisplay.MinerRate(o.Production)+"    Service in "+state.Due.Text,f,state.Alert(new LedgerServiceThreshold{Hours=20})?Brushes.Plum:Brushes.White,45,y+16);y+=80;}
                b.Save(Path.Combine(root,"service-horizon-"+width+".png"),ImageFormat.Png);ok(y<1080,"non-game compact Settings/device bounds at "+width);}
        }
    }
}
