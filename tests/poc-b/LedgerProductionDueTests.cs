using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal static class LedgerProductionDueTests
    {
        private static T Copy<T>(T value){return LedgerJson.Decode<T>(LedgerJson.Encode(value));}
        private static LedgerObservation Device(int id=1,string place="Planet A",string family="AutoMiner")
        {
            var c=LedgerContext.Create(Path.Combine(Path.GetTempPath(),"owned-service-in-fixture"),place,"Fixture System","SinglePlayer");
            var p=new LedgerProductionSnapshot{Kind="producer",Complete=true,Family=family,Device=family=="AutoMiner"?"Auto Mining Device (Basic)":"Atmospheric Condenser",DefinitionName=family=="AutoMiner"?"AutoMiningDeviceT1":"fixture-generator",Definition=1108,Player=1002,
                Owner="player:1002",Block=id+",0,0",X=id,Y=2,Z=3,NavigationPosition=true,PositionSource="owned fixture",Epoch=c.WorldKey,Tick=1000,FuelTick=1000,FuelSeconds=1000,Capacity=4,Period=1200,NextTick=1600,Running=true,NextItem=40,
                Outputs=new List<LedgerProductionOutput>{new LedgerProductionOutput{Item=40,Auxiliary=0,Name="Fixture Ore",Stock=1}}};
            p.Key=LedgerProduction.Key(c,p);return new LedgerObservation{Kind="production",Context=c,Production=p,Utc="2026-10-31T00:00:00.0000000Z"};
        }
        private static LedgerProductionDueValue At(LedgerObservation o,double seconds=0){return LedgerProductionDue.Evaluate(o.Production,o.Production.Tick+(ulong)(seconds*20),o.Context.WorldKey);}
        internal static void Run(Action<bool,string> check,string root)
        {
            Action<bool,string> ok=(b,name)=>check(b,"service in "+name);
            var a=Device();var p=a.Production;var due=At(a);
            ok(due.Seconds==150&&!due.Partial&&due.Reason=="Storage limit","first native phase30s plus two60s cycles fills three slots, storage-first");
            ok(At(a,100).Seconds==50,"elapsed is subtracted once from baseline cycle deadline, not again from projected stock");
            ok(At(a,149.95).Text=="1s"&&At(a,150).Text=="Now","positive subsecond delay is not Now/0s; exact cycle boundary is Now");
            var fuelFirst=Copy(a);fuelFirst.Production.FuelSeconds=60;
            ok(At(fuelFirst).Seconds==60&&At(fuelFirst).Reason=="Operating/fuel limit"&&!At(fuelFirst).Partial,"fuel-first is earliest known blocking limit");
            ok(At(fuelFirst,100).Text=="Now"&&At(fuelFirst,100).Reason.StartsWith("Operating/fuel"),"after both nominal deadlines, original first limiter stays fuel rather than invented full stock");
            var tie=Copy(a);tie.Production.FuelSeconds=150;
            ok(At(tie).Seconds==150&&At(tie).Reason.Contains("coincide")&&At(tie,150).Text=="Now","exact storage/fuel tie and due boundary");
            var full=Copy(a);full.Production.Outputs[0].Stock=4;full.Production.FuelSeconds=null;full.Production.Period=null;
            ok(At(full).Text=="Now"&&At(full).Reason.Contains("shown storage"),"observed full capacity is immediate despite unknown other fields");
            var empty=Copy(a);empty.Production.FuelSeconds=0;empty.Production.Capacity=null;empty.Production.Running=false;
            ok(At(empty).Text=="Now"&&At(empty).Reason.Contains("shown operating fuel"),"observed coherent empty fuel is immediate, not unknown or a zero-rate guess");
            var partial=Copy(a);partial.Production.FuelSeconds=null;partial.Production.FuelTick=null;
            ok(At(partial).Seconds==150&&At(partial).Partial&&At(partial).Text=="\u22643m"&&At(partial).Reason.Contains("operating limit unknown")&&At(partial).Hint=="Storage","storage-only upper bound rounds UP for truthful inequality");
            ok(At(partial,150).Text=="Now","known first-block upper bound reached even with unknown other bound");
            partial=Copy(a);partial.Production.Capacity=null;
            ok(At(partial).Seconds==1000&&At(partial).Partial&&At(partial).Reason.Contains("storage limit unknown"),"fuel-only duration is partial rather than guaranteed safe unattended time");
            ok(At(partial,100).Seconds==900,"fuel-only positive phase forecast advances using game time once");
            partial.Production.FuelSeconds=null;
            ok(!At(partial).Seconds.HasValue&&At(partial).Text=="unknown","both missing limits remain unknown, never zero or infinity");
            foreach(string fault in new[]{"period-missing","period-zero","period-disabled","phase-missing","phase-backward","stopped","running-unknown","next-item","ambiguous-variant","no-output","capacity-zero","overflow"}){
                var bad=Copy(a);bad.Production.FuelSeconds=null;bad.Production.FuelTick=null;
                switch(fault){
                    case "period-missing":bad.Production.Period=null;break;
                    case "period-zero":bad.Production.Period=0;break;
                    case "period-disabled":bad.Production.Period=uint.MaxValue;break;
                    case "phase-missing":bad.Production.NextTick=null;break;
                    case "phase-backward":bad.Production.NextTick=999;break;
                    case "stopped":bad.Production.Running=false;break;
                    case "running-unknown":bad.Production.Running=null;break;
                    case "next-item":bad.Production.NextItem=999;break;
                    case "ambiguous-variant":bad.Production.Outputs.Add(new LedgerProductionOutput{Item=40,Auxiliary=1,Stock=1});break;
                    case "no-output":bad.Production.Outputs.Clear();break;
                    case "capacity-zero":bad.Production.Capacity=0;break;
                    case "overflow":bad.Production.Capacity=long.MaxValue;break;
                }
                ok(!At(bad).Seconds.HasValue,"unknown/unsupported storage input stays unknown: "+fault);
            }
            var zeroRate=Copy(a);zeroRate.Production.Period=0;
            ok(!At(zeroRate).Seconds.HasValue,"zero production rate does not consume fuel by invented wall time or create an urgent zero");
            var unknownRate=Copy(a);unknownRate.Production.Period=null;
            ok(At(unknownRate).Partial&&At(unknownRate).Seconds==1000&&!At(unknownRate,1).Seconds.HasValue,"actual baseline fuel can be a bound; missing rate cannot project it later");
            var phaseNow=Copy(a);phaseNow.Production.Capacity=2;phaseNow.Production.NextTick=phaseNow.Production.Tick;
            ok(At(phaseNow).Text=="1s"&&At(phaseNow,.05).Text=="Now","last shown baseline wins over a cycle still due at the same tick");
            var rounded=Copy(a);rounded.Production.Capacity=1000;rounded.Production.FuelTick=999;rounded.Production.ShownOperating="00:01";
            ok(At(rounded).Seconds==120&&At(rounded).Partial,"floored shown h:mm supplies next-minute upper bound, not exact fuel depletion");
            ok(At(rounded,61).Seconds==59&&At(rounded,61).Text=="\u226459s"&&At(rounded,120).Text=="Now","shown-minute uncertainty does not declare fuel empty prematurely at its lower bound");
            rounded.Production.ShownOperating="00:00";
            ok(At(rounded).Text=="\u22641m","shown00:00 alone is not proof of empty fuel");
            rounded.Production.Capacity=2;rounded.Production.ShownOperating="01:00";
            ok(At(rounded).Seconds==30&&!At(rounded).Partial,"storage before guaranteed shown-fuel lower bound is fully determined");
            foreach(string family in new[]{"AutoMiner","Generator mode 0","Generator mode 1","Generator mode 2","Generator mode 3"}){
                var o=Device(family:family);if(family=="Generator mode 3"){o.Production.Alternating=true;o.Production.Outputs.Add(new LedgerProductionOutput{Item=41,Name="Second product",Stock=1});}
                ok(At(o).Seconds==(family=="Generator mode 3"?90:150),"supported native family uses its shown period/capacity, not miner-only displayed rate: "+family);
            }
            var multi=Device(7,family:"Generator mode 3");multi.Production.Alternating=true;multi.Production.Capacity=100;multi.Production.Period=2000;multi.Production.NextTick=3000;multi.Production.FuelSeconds=100000;
            multi.Production.Outputs[0].Stock=36;multi.Production.Outputs.Add(new LedgerProductionOutput{Item=41,Auxiliary=0,Name="Second product",Stock=36});
            ok(At(multi).Seconds==2800&&At(multi,200).Seconds==2600,"two products sum72 in ONE100-slot store, one unit each100s cycle, not two independent capacity clocks");
            ok(LedgerProductionDue.Scope(new[]{multi},1000,multi.Context.WorldKey).Devices==1&&LedgerProductionDisplay.Lines(new[]{multi}).Count==2,"multi-product scope counts one physical device despite two table product lines");
            var lines=LedgerProductionDisplay.Lines(new[]{multi});var cells=LedgerProductionDisplay.DeviceCells(lines[0],LedgerProduction.Estimate(multi.Production,1000,multi.Context.WorldKey),DateTime.UtcNow,null,At(multi));
            ok(cells[7]==At(multi).Text&&LedgerProductionDisplay.DeviceCells(lines[1],LedgerProduction.Estimate(multi.Production,1000,multi.Context.WorldKey),DateTime.UtcNow,null,At(multi))[7]=="","Service in shown once alongside readable device identity, not doubled on second product");
            var collected=Copy(a);collected.Production.Tick=4000;collected.Production.FuelTick=4000;collected.Production.NextTick=4600;collected.Production.Outputs[0].Stock=0;
            ok(At(collected).Seconds==210&&At(a).Seconds==150,"new shown collection baseline lengthens storage service horizon without editing previous observation");
            var refueled=Copy(fuelFirst);refueled.Production.Tick=4000;refueled.Production.FuelTick=4000;refueled.Production.NextTick=4600;refueled.Production.FuelSeconds=1000;
            ok(At(refueled).Seconds==150&&At(refueled).Reason=="Storage limit","new shown refuel clears fuel-first constraint independently of storage");
            ok(LedgerProductionDue.Evaluate(a.Production,999,a.Context.WorldKey).Text=="unknown"&&LedgerProductionDue.Evaluate(a.Production,1000,"foreign").Text=="unknown","clock rollback/foreign save never imports future service time");
            ok(At(a,100000).Text=="Now"&&At(a).Seconds==At(a).Seconds,"game-time skip advances deadline; repeated same game tick/pause does not");
            var deferred=Copy(a);deferred.Production.Family="Industrial miner (deferred)";
            ok(!At(deferred).Seconds.HasValue,"industrial family remains deferred without fabricated operation");
            ok(LedgerProductionDue.Limits(150,null,false).Seconds==150&&!LedgerProductionDue.Limits(150,null,false).Partial&&LedgerProductionDue.Limits(150,null).Partial,"explicit fuel-not-applicable limit is distinct from missing fuel; no native family inferred fuel-free");
            foreach(int capacity in new[]{2,4,100})foreach(uint period in new uint[]{1200,2000})foreach(double fuel in new[]{60d,150d}){
                var sample=Copy(a);sample.Production.Capacity=capacity;sample.Production.Period=period;sample.Production.FuelSeconds=fuel;
                var boundary=At(sample);ulong deadline=sample.Production.Tick+(ulong)(boundary.Seconds.Value*20);
                var before=LedgerProduction.Estimate(sample.Production,deadline-1,sample.Context.WorldKey);var blocked=LedgerProduction.Estimate(sample.Production,deadline,sample.Context.WorldKey);
                ok(!before.Full&&!before.NeedsFuel&&(blocked.Full||blocked.NeedsFuel),"first-block result agrees with existing fuel-limited stock forecast on either side of boundary cap"+capacity+" period"+period+" fuel"+fuel);
            }
            string hash=LedgerJson.Hash(LedgerJson.Encode(a));for(int i=0;i<10;i++)At(a,i*100);
            ok(hash==LedgerJson.Hash(LedgerJson.Encode(a)),"pure service display calculation never rewrites recorded values/settings");
            string workspace=AppDomain.CurrentDomain.BaseDirectory;
            while(workspace!=null&&!File.Exists(Path.Combine(workspace,"PROJECT_BRIEF.md")))workspace=Path.GetDirectoryName(workspace.TrimEnd(Path.DirectorySeparatorChar));
            // Real-session replay is intentionally excluded from the public synthetic suite.
            string ui=File.ReadAllText(Path.Combine(workspace,"src/TradeLedgerProbeB/LedgerProductionWindow.cs"));
            ok(ui.Contains("productionServiceSort.Click(column)")&&ui.Contains("LedgerProductionDisplay.SortDevices(rows,productionServiceSort,tick,world)")&&ui.Contains("LedgerProductionDue.Scope(producers,tick,world)"),"actual GUI uses independent sortable device header and whole-place summary on existing game tick");
            SortingAndScope(ok,root,a,multi);
        }
        private static void SortingAndScope(Action<bool,string> ok,string root,LedgerObservation a,LedgerObservation multi)
        {
            var miner=Copy(a);miner.Production.Capacity=1750;miner.Production.Period=72000;miner.Production.NextTick=73000;miner.Production.FuelSeconds=50*3600;
            var condenser=Device(2,family:"Generator mode 3");condenser.Production.Capacity=1000;condenser.Production.Period=72000;condenser.Production.NextTick=73000;condenser.Production.FuelSeconds=9600;condenser.Production.Outputs[0].Name="Oxygen Canister";
            var unknown=Device(3);unknown.Production.Capacity=null;unknown.Production.FuelSeconds=null;
            var partial=Device(4);partial.Production.Capacity=null;partial.Production.FuelSeconds=9600;
            var now=Device(5);now.Production.Outputs[0].Stock=4;
            var moon=Device(6,"Planet A Moon");moon.Production.FuelSeconds=1;
            var rows=new[]{miner,condenser,unknown,partial,now}.ToList();var sort=new LedgerHeaderSort();
            var original=rows.Select(o=>o.Production.Key).ToArray();
            sort.Click(7);var ascending=LedgerProductionDue.Sort(rows,sort,1000,a.Context.WorldKey);
            ok(ascending[0]==now&&ascending[1]==condenser&&ascending[2]==partial&&ascending[3]==miner&&ascending[4]==unknown,"ascending: Now, finite/partial numeric time with exact first on tie, unknown last");
            ok(At(condenser).Text=="2h 40m"&&At(miner).Text=="50h 0m","condenser2h40m correctly precedes miner50h without importance policy");
            sort.Click(7);var descending=LedgerProductionDue.Sort(rows,sort,1000,a.Context.WorldKey);
            ok(descending[0]==miner&&descending[1]==condenser&&descending[2]==partial&&descending[3]==now&&descending[4]==unknown,"descending numeric time reverses known values; unknown still last, stable tie order");
            sort.Click(7);ok(LedgerProductionDue.Sort(rows,sort,1000,a.Context.WorldKey).Select(o=>o.Production.Key).SequenceEqual(original),"third-click default restores established device order without filters/selection mutation");
            var deposits=new LedgerHeaderSort();deposits.Click(2);sort.Click(7);
            ok(deposits.Column==2&&!deposits.Descending&&sort.Column==7,"device service sorting remains independent of existing deposit size/miner/fill sorts");
            var ordered=LedgerProductionDue.Sort(rows,sort,1000,a.Context.WorldKey);var selected=ordered.Single(o=>o.Production.Key==condenser.Production.Key);
            ok(ReferenceEquals(selected,condenser)&&LedgerProduction.Bookmark(selected,selected.Context).TargetKey==condenser.Production.Key,"sorted row actions keep exact device identity/recorded target");
            var scope=LedgerProductionDue.Scope(new[]{miner,condenser},1000,a.Context.WorldKey);
            ok(scope.Earliest.Seconds==9600&&scope.Limiting==condenser&&scope.Text=="Service in 2h 40m"&&scope.Hint.Contains("Atmospheric Condenser"),"scope minimum includes less important condenser and identifies its limiting reason/location");
            scope=LedgerProductionDue.Scope(new[]{miner,unknown},1000,a.Context.WorldKey);
            ok(scope.Text.Contains("\u226450h")&&scope.Bounded==1&&scope.Devices==2&&!scope.Hint.Contains("earlier")&&scope.Hint.EndsWith(": Fuel"),"unknown scope member cannot be omitted to claim entire planet safe for50h");
            scope=LedgerProductionDue.Scope(new[]{condenser,partial},1000,a.Context.WorldKey);
            ok(scope.Text.StartsWith("Service in \u22642h 40m")&&scope.Partial==1,"partial member makes aggregate upper-bound, even with exact earliest tie");
            scope=LedgerProductionDue.Scope(new[]{now,unknown},1000,a.Context.WorldKey);
            ok(scope.Text.StartsWith("Service in Now")&&scope.Bounded==1&&scope.Devices==2,"known immediate blocker remains Now with honest incomplete aggregate coverage");
            ok(LedgerProductionDue.Scope(new[]{unknown},1000,a.Context.WorldKey).Text.Contains("unknown")&&LedgerProductionDue.Scope(new LedgerObservation[0],1000,a.Context.WorldKey).Text=="Service in \u2014","all-unknown and no devices remain distinct from Now/unlimited");
            var places=new LedgerProductionPlaces(a.Context.WorldKey,new[]{a.Context,moon.Context});var locationRows=new[]{miner,condenser,moon};
            scope=LedgerProductionDue.Scope(locationRows.Where(o=>places.Contains("playfield:Planet A",o)),1000,a.Context.WorldKey);
            ok(scope.Earliest.Seconds==9600&&scope.Devices==2&&LedgerProductionDue.Scope(locationRows,1000,a.Context.WorldKey).Earliest.Seconds==1,"selected planet scope excludes moon by actual recorded place, broader view includes its earliest limit");
            ok(new[]{miner,condenser,unknown,partial,now,multi}.Select(o=>o.Production.Key).Distinct().Count()==6,"preview and overview fixture has six distinct physical device identities");
            var prefs=new LedgerServicePreferences{World=a.Context.WorldKey,Player=1002};var before=LedgerProductionServices.Overview(new[]{miner,condenser},1000,a.Context.WorldKey,prefs,places);string facts=LedgerJson.Hash(LedgerJson.Encode(new[]{miner,condenser}))+LedgerJson.Hash(LedgerJson.Encode(prefs));
            LedgerProductionDue.Scope(new[]{miner,condenser},2000,a.Context.WorldKey);var after=LedgerProductionServices.Overview(new[]{miner,condenser},1000,a.Context.WorldKey,prefs,places);
            ok(before.MeanFill==after.MeanFill&&before.Resources.Sum(x=>x.Stock)==after.Resources.Sum(x=>x.Stock)&&facts==LedgerJson.Hash(LedgerJson.Encode(new[]{miner,condenser}))+LedgerJson.Hash(LedgerJson.Encode(prefs)),"resource totals/fill mean, alert preferences and observations unaffected by Service in");
            foreach(bool leaf in new[]{false,true}){
                var headers=LedgerProductionDisplay.Headers(false,leaf);var columns=LedgerProductionDisplay.Columns(false,leaf);
                ok(headers.Contains("Service in")&&headers.Length==columns.Length&&Math.Abs(columns.Sum()-1)<.001&&headers.Contains("Location")==!leaf,"compact aligned Service in columns for "+(leaf?"leaf":"broad")+" scope");
            }
            string previews=Path.Combine(root,"service-in-previews");Directory.CreateDirectory(previews);
            foreach(int width in new[]{1920,3840})foreach(bool leaf in new[]{false,true}){
                Render(Path.Combine(previews,"devices-"+width+(leaf?"-leaf":"-broad")+".png"),width,leaf,new[]{miner,condenser,unknown,partial,now,multi});
                ok(File.Exists(Path.Combine(previews,"devices-"+width+(leaf?"-leaf":"-broad")+".png")),"shared-column non-game populated/partial/unknown layout rendered "+width+" "+leaf);
            }
            Render(Path.Combine(previews,"empty-1920.png"),1920,true,new LedgerObservation[0]);
        }
        private static void Render(string file,int width,bool leaf,IEnumerable<LedgerObservation> data)
        {
            var rows=data.ToList();string epoch=rows.Count==0?"":rows[0].Context.WorldKey;ulong tick=1000;
            using(var bitmap=new Bitmap(width,width*9/16))using(var g=Graphics.FromImage(bitmap))using(var font=new Font("Segoe UI",15,GraphicsUnit.Pixel)){
                g.ScaleTransform(width/1920f,width/1920f);g.Clear(Color.FromArgb(20,43,61));g.DrawString("Trade Ledger   Markets   NPC Requests   Locations   Comparison   Production   Item Browser   Markers",font,Brushes.LightBlue,20,20);
                g.DrawString("Places\n\nAll places\n  Fixture System\n    Planet A\n    Planet A Moon",font,Brushes.Gainsboro,20,90);
                g.DrawString("Devices     Deposits       Search                         Reset",font,Brushes.LightBlue,440,90);
                g.DrawString("Fill / fuel reserve and resource totals remain in their existing overview",font,Brushes.Gainsboro,440,126);
                g.DrawString(LedgerProductionDue.Scope(rows,tick,epoch).Text,font,Brushes.LightCyan,1510,126);
                var columns=LedgerProductionDisplay.Columns(false,leaf);var headers=LedgerProductionDisplay.Headers(false,leaf);headers[leaf?6:7]+=" ^";
                float y=180;Action<string[],bool> row=(cells,header)=>{float x=440;for(int n=0;n<cells.Length;n++){float w=columns[n]*1450;using(var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(cells[n]??"",font,header?Brushes.LightBlue:Brushes.White,new RectangleF(x+5,y+4,w-10,27),format);x+=w;}y+=34;};row(headers,true);
                var sort=new LedgerHeaderSort();sort.Click(7);
                foreach(var line in LedgerProductionDisplay.Lines(LedgerProductionDue.Sort(rows,sort,tick,epoch))){var p=line.Observation.Production;var cells=LedgerProductionDisplay.DeviceCells(line,LedgerProduction.Estimate(p,tick,epoch),DateTime.UtcNow,null,LedgerProductionDue.Evaluate(p,tick,epoch));double? fill=LedgerProductionServices.State(p,tick,epoch).Fill;cells[4]=line.First?(fill.HasValue?fill.Value.ToString("0.#")+"%":"?"):"";cells[cells.Length-1]=line.First?"Mark":"";row(LedgerProductionDisplay.VisibleCells(cells,false,leaf),false);}
                if(rows.Count==0)g.DrawString("No recorded devices in this place",font,Brushes.White,445,y+5);
                g.DrawString("NON-GAME synthetic preview / shared forecast, columns, mapping and sorting; native rendering remains owner test",font,Brushes.Gold,20,1030);bitmap.Save(file,ImageFormat.Png);
            }
        }
    }
}
