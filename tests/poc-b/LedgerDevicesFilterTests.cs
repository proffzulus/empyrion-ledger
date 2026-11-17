using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal static class LedgerDevicesFilterTests
    {
        private static string Snapshot<T>(T value){return Convert.ToBase64String(LedgerJson.Encode(value));}
        private static LedgerObservation Device(int index,string place="Planet A",int item=40,int? aux=0,string name="Neodymium Ore")
        {
            var c=LedgerContext.Create(Path.Combine(Path.GetTempPath(),"devices-filter-fixture"),place,"Fixture System","SinglePlayer");
            var p=new LedgerProductionSnapshot{Kind="producer",Device="Auto Mining Device (Basic)",Family="AutoMiner",DefinitionName="AutoMiningDeviceT1",Definition=1108,Player=1002,Owner="player:1002",Block=index+",2,3",X=index*100,Y=2,Z=3,NavigationPosition=true,PositionSource="owned fixture",Complete=true,Tick=1000,FuelTick=1000,NextTick=2000,Period=2000,Capacity=1750,FuelSeconds=40*3600,Running=true,NextItem=item,Epoch=c.WorldKey,VisitUtc="2026-11-05T00:00:00Z",Outputs=new List<LedgerProductionOutput>{new LedgerProductionOutput{Item=item,Auxiliary=aux,Name=name,Stock=20}}};
            p.Key=LedgerProduction.Key(c,p);return new LedgerObservation{Kind="production",Context=c,Production=p,Utc=p.VisitUtc,GameTicks="1000",Source="owned synthetic Devices filter fixture"};
        }
        private static LedgerMarkerDestination Target(LedgerMarkerModel m,LedgerObservation o){return m.Find(LedgerProduction.Bookmark(o,o.Context),1002);}
        internal static void Run(Action<bool,string> check,string root)
        {
            Action<bool,string> ok=(v,t)=>check(v,"devices filters "+t);
            string resource=LedgerDeviceSelection.Resource(40,0),selected=null;
            selected=LedgerDeviceSelection.Toggle(selected,resource);ok(selected==resource,"resource click selects canonical output identity");
            string other=LedgerDeviceSelection.Resource(41,0);selected=LedgerDeviceSelection.Toggle(selected,other);ok(selected==other,"another resource replaces instead of accumulating");
            selected=LedgerDeviceSelection.Toggle(selected,other);ok(selected==null,"active click clears filter");
            var a=Device(1);var b=Device(2,item:41,name:"Copper Ore");var c=Device(3,"Planet B");var variant=Device(4,aux:1);var unknown=Device(5,aux:null);var collision=Device(6,item:42);
            var rows=new List<LedgerObservation>{a,b,c,variant,unknown,collision};
            var places=new LedgerProductionPlaces(a.Context.WorldKey,rows.Select(x=>x.Context));
            var scoped=rows.Where(x=>places.Contains("playfield:Planet A",x)).ToList();
            ok(scoped.Count==5,"location branch excludes other planet without dropping same-place devices");
            var matching=LedgerDeviceSelection.Filter(scoped,"nEoDyMiUm",resource);
            ok(matching.Count==1&&matching[0]==a,"case-insensitive text AND location AND resource identity intersect");
            ok(LedgerDeviceSelection.Filter(rows,"Planet B",resource).Single()==c,"search includes place and composes with item");
            ok(LedgerDeviceSelection.Filter(rows,"copper",resource).Count==0,"nonmatching search and resource produce empty result");
            ok(LedgerDeviceSelection.Filter(rows,"",resource).Count==2,"canonical item variants and same-name different identities remain distinct");
            ok(LedgerDeviceSelection.Filter(rows,"",LedgerDeviceSelection.Resource(40,null)).Single()==unknown,"unknown auxiliary does not masquerade as zero");
            ok(LedgerDeviceSelection.Filter(rows,"",null).Count==6,"clearing resource restores every scoped device");
            var multi=Device(7,item:50,name:"Oxygen Canister");multi.Production.Family="WaterGenerator";multi.Production.Outputs.Add(new LedgerProductionOutput{Item=51,Auxiliary=0,Name="Purified Water",Stock=0});
            var multiMatch=LedgerDeviceSelection.Filter(new[]{multi},"water",LedgerDeviceSelection.Resource(51,0));
            ok(multiMatch.Single()==multi&&LedgerProductionDisplay.Lines(multiMatch).Count==2,"multi-output match retains whole physical device and its other product");
            ok(LedgerDeviceSelection.Filter(new[]{multi},"",LedgerDeviceSelection.Resource(51,0)).Count==1,"observed zero stock remains a selectable resource");
            foreach(int width in new[]{1000,1230,1450,2900}){
                ok(LedgerDeviceSelection.Columns(width)<=6,"grid maximum six columns at width "+width);
                foreach(int count in new[]{0,1,6,8,13}){
                    bool fits=true;for(int i=0;i<count;i++){var cell=LedgerDeviceSelection.Cell(i,width);fits&=cell[0]>=0&&cell[0]+cell[2]<=width+.01f&&cell[1]+cell[3]<=LedgerDeviceSelection.Height(count,width);}
                    ok(fits,"all resource cells wrapped inside visible logical width "+width+" count "+count);
                }
            }
            ok(LedgerDeviceSelection.Rows(8,1230)==2&&LedgerDeviceSelection.Rows(13,1230)==3,"eight and thirteen resources occupy two and three rows");
            var model=new LedgerMarkerModel();var prefs=LedgerProductionServices.Preferences(model,a.Context.WorldKey,1002);prefs.Enabled=false;prefs.For("all",true).Suppress=true;prefs.Fill=5;
            LedgerProductionServices.Evaluate(model,rows,a.Context,1002,1000,false,places);
            ok(model.Data.Destinations.Count==0,"disabled alerts do not create unselected markers");
            var keys=new HashSet<string>(matching.Select(x=>x.Production.Key));
            LedgerProductionServices.Evaluate(model,rows,a.Context,1002,1000,true,places,"playfield:Planet A",keys);
            var d=Target(model,a);var purpose=d.Purposes.Single();var route=model.Data.Routes.Single();
            ok(model.Data.Destinations.Count==1&&d.Pinned&&purpose.Visibility==1&&model.Project(d).Visible,"Mark ensures only matching member and pins persistent service marker");
            ok(!route.Shown&&purpose.Routes.Contains(route.Id),"subset Mark records group membership without group-wide Show");
            ok(!prefs.Enabled&&prefs.For("all").Suppress&&prefs.Fill==5,"member Mark does not edit alert defaults suppression or thresholds");
            string routeState=Snapshot(route);
            for(int i=0;i<5;i++)LedgerProductionServices.Evaluate(model,rows,a.Context,1002,1000,true,places,"playfield:Planet A",keys);
            ok(model.Data.Destinations.Count==1&&model.Data.Routes.Count==1&&d.Purposes.Count==1,"repeated subset Mark creates no logical duplicate");
            ok(Snapshot(route)==routeState,"repeated subset Mark leaves group state byte-equivalent");
            ok(Target(model,b)==null&&Target(model,c)==null&&Target(model,variant)==null,"hidden search/resource/location members never acquire explicit marks");
            var bkeys=new HashSet<string>{b.Production.Key};LedgerProductionServices.Evaluate(model,rows,a.Context,1002,1000,true,places,"all",bkeys);
            var bd=Target(model,b);var bp=bd.Purposes.Single();bp.ServiceServiced=true;bp.Visibility=-1;string bbefore=Snapshot(bp);
            LedgerProductionServices.Evaluate(model,rows,a.Context,1002,1000,true,places,"all",keys);
            ok(Snapshot(bp)==bbefore&&!model.Project(bd).Visible,"partially existing group preserves nonmatching serviced/hidden manual state");
            ok(!purpose.ServiceRetired&&!bp.ServiceRetired,"filtered command retains full authoritative retirement cache");
            string beforeEmpty=Snapshot(model.Data);long revision=model.Revision;
            LedgerProductionServices.Evaluate(model,rows,a.Context,1002,1000,true,places,"all",new HashSet<string>());
            ok(Snapshot(model.Data)==beforeEmpty&&model.Revision==revision,"empty matching set is an exact no-op");
            LedgerDeviceSelection.Filter(rows,"",null);ok(Snapshot(model.Data)==beforeEmpty,"clearing filters never changes stored marks");
            var unknownKeys=new HashSet<string>{"not-active"};LedgerProductionServices.Evaluate(model,rows,a.Context,1002,1000,true,places,"all",unknownKeys);
            ok(model.Data.Destinations.Count==2&&!route.Shown,"stale unmatched command cannot create or show unrelated devices");
            string file=Path.Combine(root,"devices-filter-markers.json");var repo=new LedgerMarkerRepository(file);repo.Save(model.Data);ok(repo.Wait(5000),"subset preferences saved atomically in existing portable repository");
            var restored=new LedgerMarkerModel(new LedgerMarkerRepository(file).Read());
            ok(restored.Project(Target(restored,a)).Visible&&!restored.Project(Target(restored,b)).Visible&&!restored.Data.Routes.Single().Shown,"restart retains individual display and hidden group");
            ok(LedgerProductionServices.Preferences(restored,a.Context.WorldKey,1002).For("all").Suppress,"restart preserves explicit suppression");
            model.ShowRoute(route,true);var priorGroup=Snapshot(route);LedgerProductionServices.Evaluate(model,rows,a.Context,1002,1000,true,places,"all",keys);
            ok(route.Shown&&Snapshot(route)==priorGroup,"existing deliberately shown group is not globally overwritten by member Mark");
            foreach(var sample in new[]{"<color=#76D8EF>Miner</color> / Planet: <b>Storage</b>","&lt;color=#76D8EF&gt;Miner&lt;/color&gt; / Planet: &lt;b&gt;Storage&lt;/b&gt;","&amp;lt;b&amp;gt;Miner&amp;lt;/b&amp;gt; / Planet: Storage","[76D8EF]Miner[-] / Planet: [b]Storage[/b]"})
                ok(LedgerPresentation.TooltipText(sample)=="Miner / Planet: Storage","plain tooltip strips raw or escaped native rich markup "+sample.Substring(0,Math.Min(12,sample.Length)));
            ok(LedgerPresentation.TooltipText("Fuel <= 5h, fill < 5%")=="Fuel <= 5h, fill < 5%","plain tooltip preserves meaningful comparison symbols");
            ok(LedgerPresentation.TooltipText("<b>Miner<color=#0088ff> T1</b></color>")=="Miner T1","plain tooltip handles crossed closing tags without literal markup");
            ok(LedgerPresentation.TooltipText("One<br>Two")=="One\nTwo","plain tooltip preserves readable line breaks");
            string raw=a.Production.Device;a.Production.Device="&lt;b&gt;Miner&lt;/b&gt;";LedgerPresentation.TooltipText(a.Production.Device);ok(a.Production.Device=="&lt;b&gt;Miner&lt;/b&gt;","tooltip normalization never rewrites captured data");a.Production.Device=raw;
            var workspace=Workspace();var window=File.ReadAllText(Path.Combine(workspace,"src/TradeLedgerProbeB/LedgerProductionWindow.cs"));var native=File.ReadAllText(Path.Combine(workspace,"src/TradeLedgerProbeB/LedgerNativeBookmarks.cs"));var hover=File.ReadAllText(Path.Combine(workspace,"src/TradeLedgerProbeB/LedgerItemWindow.cs"));
            ok(window.Contains("matching.Select(o=>o.Production.Key)")&&window.Contains("matching.Count>0"),"actual Mark group supplies same filtered row identities and disables empty command");
            ok(native.Contains("Evaluate(Model,serviceDevices")&&native.Contains("places,place,members"),"native facade passes full trusted cache plus exact subset, not subset as authoritative history");
            ok(window.Contains("productionQuantityStyle")&&window.Contains("rect.width-quantityWidth-5"),"resource totals retain a separate visible quantity slot for long names");
            ok(hover.Contains("LedgerPresentation.TooltipText(value)")&&hover.Contains("value,text)"),"actual shared tooltip boundary uses plain GUI style, not HTML escapes");
            foreach(int width in new[]{1280,1920,3840})foreach(int count in new[]{0,8,13})Render(root,width,count);
        }
        private static string Workspace(){var p=AppDomain.CurrentDomain.BaseDirectory;while(p!=null&&!File.Exists(Path.Combine(p,"PROJECT_BRIEF.md")))p=Path.GetDirectoryName(p.TrimEnd(Path.DirectorySeparatorChar));return p;}
        private static void Render(string root,int width,int count)
        {
            var l=LedgerLayout.For(width,width*9/16);float nav=Math.Max(250,Math.Min(420,(l.Width-32)*.26f)),left=16+nav+12,w=l.Width-left-16;
            using(var bmp=new Bitmap(width,width*9/16))using(var g=Graphics.FromImage(bmp))using(var font=new Font("Segoe UI",16))using(var small=new Font("Segoe UI",14))using(var panel=new SolidBrush(System.Drawing.Color.FromArgb(220,20,43,61)))using(var active=new SolidBrush(System.Drawing.Color.FromArgb(255,44,99,128))){
                g.Clear(System.Drawing.Color.SlateGray);g.ScaleTransform(width/(float)l.Width,width/(float)l.Width);g.FillRectangle(panel,0,0,l.Width,l.Height);
                Action<string,float,float,float> text=(s,x,y,max)=>g.DrawString(s,font,Brushes.White,new RectangleF(x,y,max,27),new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap});
                text("Trade Ledger / Production",16,25,800);text("All places",24,110,nav-24);text("  Fixture system",24,145,nav-24);text("    Planet A",24,180,nav-24);text("Devices     Deposits     Search: Neodymium     Reset",left,98,w);
                text("[x] Alerts     [ ] Suppress                    Settings +       Mark group",left,132,w);text("Fill 1.2%    Fuel reserve 8 sufficient | 0 low | 0 unknown",left,166,w);
                for(int i=0;i<count;i++){var c=LedgerDeviceSelection.Cell(i,w);float x=left+c[0],y=199+c[1];g.FillRectangle(i==3?active:panel,x,y,c[2],c[3]);g.DrawString((i==3?"- ":"")+(i==3?"Neodymium Ore":i==7?"Long multi-word resource name":"Resource "+(i+1)),small,Brushes.LightBlue,new RectangleF(x+5,y+1,c[2]-63,23),new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap});g.DrawString((2000+i).ToString(),small,Brushes.LightBlue,new RectangleF(x+c[2]-58,y+1,53,23),new StringFormat{Alignment=StringAlignment.Far,FormatFlags=StringFormatFlags.NoWrap});}
                if(count==0)text("No matching resources",left,199,w);
                float header=239+LedgerDeviceSelection.Height(count,w)-28;text("Device            Product             Storage     Fill       Rate       Fuel       Service in    Mark",left,header,w);
                for(int i=0;i<(count==0?0:8);i++){float y=header+30+i*34;g.FillRectangle(panel,left,y,w,32);text("Miner T1          Neodymium Ore       200/1750    11%        31/h       50h        46h 20m       Mark",left+4,y,w-8);}
                var tooltip=new RectangleF(left+50,header+100,400,34);g.FillRectangle(Brushes.DarkSlateGray,tooltip);g.DrawRectangle(Pens.LightBlue,tooltip.X,tooltip.Y,tooltip.Width,tooltip.Height);
                g.DrawString(LedgerPresentation.TooltipText("&lt;b&gt;Miner T1&lt;/b&gt; / Planet A: Storage"),small,Brushes.White,tooltip);
                bmp.Save(Path.Combine(root,"devices-filter-"+width+"-"+count+".png"),ImageFormat.Png);
            }
        }
    }
}
