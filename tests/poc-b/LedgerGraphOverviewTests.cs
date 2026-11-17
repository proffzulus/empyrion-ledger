using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using TradeLedgerProbeB;
namespace BOfflineTests
{
    internal static class LedgerGraphOverviewTests
    {
        private static LedgerBrowserCatalog Catalog(string kind,int n)
        {
            var c=new LedgerBrowserCatalog{CompleteRecipes=true};
            for(int i=0;i<n;i++){string k="item"+i;c.Items.Add(k,new LedgerBrowserItem{Key=k,InternalName=k,Name=i==0?"Root Item":i==2?"Long authoritative item name with variant Alpha":"Item "+i,Canonical=true,Category="Fixture"});}
            if(kind=="wide"){
                var r=new LedgerBrowserRecipe{Key="wide",OutputKey="item0",OutputCount=3,Manufacturing=true};
                for(int i=1;i<n;i++)r.Inputs.Add(new LedgerBrowserInput{ItemKey="item"+i,Quantity=i});c.Recipes.Add(r);
            }else for(int i=0;i<n-1;i++){
                var r=new LedgerBrowserRecipe{Key="recipe"+i,OutputKey="item"+i,OutputCount=i+1,Manufacturing=true};r.Inputs.Add(new LedgerBrowserInput{ItemKey="item"+(i+1),Quantity=i+2});
                if(kind=="mixed"&&i+3<n)r.Inputs.Add(new LedgerBrowserInput{ItemKey="item"+(i+3),Quantity=5});c.Recipes.Add(r);
            }
            c.Seal("synthetic overview fixture "+kind);return c;
        }
        private static LedgerBrowserState State(LedgerBrowserCatalog c)
        {var s=new LedgerBrowserState();s.Bind(c);s.SelectRoot(c,"item0");foreach(var key in c.Items.Keys.ToArray())s.Expand(c,key,true);return s;}
        private static string Semantics(LedgerBrowserState s)
        {return string.Join("|",s.Nodes.Keys.OrderBy(k=>k))+"#"+string.Join("|",s.Edges.Select(e=>e.Identity+":"+e.From+":"+e.To+":"+e.Label).OrderBy(k=>k));}
        internal static void Run(Action<bool,string> check,string root)
        {
            int count=0;Action<bool,string> c=(v,s)=>{check(v,"graph-overview: "+s);count++;};
            c(LedgerGraphGeometry.OverviewFont(.14f)==7&&LedgerGraphGeometry.OverviewFont(.59f)==9,"overview font follows horizontal zoom rather than overlarge font in narrow name strips");
            c(LedgerBrowserState.MinimumZoom<=.02f&&LedgerBrowserState.DetailZoom==.6f,"overview floor30x farther than old0.6; full cards restored at0.6");
            foreach(var kind in new[]{"small","wide","deep","mixed"}){
                var cat=Catalog(kind,kind=="small"?3:kind=="wide"?50:kind=="deep"?40:18);var s=State(cat);string original=Semantics(s);int revision=s.Revision;var source=cat.Fingerprint;
                foreach(var size in new[]{new[]{1000f,440f},new[]{1450f,680f},new[]{1700f,860f}}){
                    s.Fit(size[0],size[1]);var projection=LedgerGraphGeometry.Project(s,cat);
                    c(projection.Items.Count==cat.Items.Count&&projection.Items.Values.All(b=>b.Width>0&&b.Height>=1&&b.X>=0&&b.Y>=0&&b.Right<=size[0]&&b.Bottom<=size[1]),kind+" Fit contains every positive visible item box "+size[0]);
                    c(projection.Connections.Count==s.Edges.Count(e=>e.Identity.Contains(":in:")),kind+" overview retains every exact input relationship");
                    c(Semantics(s)==original&&s.Revision==revision&&cat.Fingerprint==source,kind+" Fit/projection never rebuilds or changes recipes/identity");
                }
                if(kind=="wide"||kind=="deep")c(s.Zoom<.6f&&s.Overview,kind+" large graph enters useful compact overview");
                if(kind=="deep")c(LedgerGraphGeometry.Project(s,cat).Items.Values.All(b=>b.Height>=15),"width-dominated overview retains readable name height rather than thin strips");
                var selected=s.Selected;float px=321,py=157;
                for(int cycle=0;cycle<10;cycle++){
                    s.ZoomAt(1/s.Zoom,px,py);float wx=(px-s.PanX)/s.Zoom,wy=(py-s.PanY)/s.ScaleY;
                    s.ZoomAt(.45f,px,py);c(s.Overview&&Math.Abs((px-s.PanX)/s.Zoom-wx)<.02&&Math.Abs((py-s.PanY)/s.ScaleY-wy)<.02,kind+" compact transition preserves anchored world point "+cycle);
                    s.ZoomAt(1/.45f,px,py);c(!s.Overview&&Math.Abs(s.Zoom-1)<.0001&&Math.Abs((py-s.PanY)/s.ScaleY-wy)<.02,kind+" full cards restore at same region "+cycle);
                }
                c(Semantics(s)==original&&s.Revision==revision&&s.Selected==selected,kind+" repeated zoom cycles retain selection and traversal state");
                s.Zoom=.5999f;float before=LedgerBrowserState.VerticalScale(s.Zoom);s.Zoom=.6f;c(!s.Overview&&Math.Abs(before-s.ScaleY)<.0002,kind+" detail threshold has continuous geometry, no layout jump");
                s.Zoom=.3f;s.PanX=70;s.PanY=50;s.Viewport(1000,440);float cx=(500-s.PanX)/s.Zoom,cy=(220-s.PanY)/s.ScaleY;s.Viewport(1400,640);
                c(Math.Abs((700-s.PanX)/s.Zoom-cx)<.01&&Math.Abs((320-s.PanY)/s.ScaleY-cy)<.01,kind+" viewport resize preserves center region in compact mode");
                s.Fit(1000,440);s.Center(1000,440);var chosen=LedgerGraphGeometry.Project(s,cat).Items[s.Selected];c(Math.Abs(chosen.X+chosen.Width/2-500)<.01&&Math.Abs(chosen.Y+chosen.Height/2-220)<.01,kind+" Center uses compact projection rather than old full-height transform");
            }
            foreach(int n in new[]{60,119}){
                var cat=Catalog(n==119?"wide":"deep",n);var s=State(cat);s.Fit(1000,440);var p=LedgerGraphGeometry.Project(s,cat);
                c(s.Nodes.Count<=120&&p.Items.Count==n&&p.Items.Values.All(b=>b.X>=0&&b.Right<=1000&&b.Y>=0&&b.Bottom<=440),"model-bound graph fits without old floor clipping "+n);
                c(p.Items.Values.All(b=>b.Width>0&&b.Height>=1),"even bounded extreme graph retains visible nodes "+n);
            }
            var resize=State(Catalog("wide",119));resize.Zoom=.02f;resize.Viewport(1000,240);float oldY=resize.ScaleY,centerY=(120-resize.PanY)/oldY;resize.Viewport(1000,440);
            c(resize.ScaleY>oldY&&Math.Abs((220-resize.PanY)/resize.ScaleY-centerY)<.02,"height-dependent name floor changes scale while keeping same center region");
            var state=State(Catalog("small",3));state.ZoomAt(float.MaxValue,10,20);c(state.Zoom==LedgerBrowserState.MaximumZoom,"large finite factor clamps at upper zoom");
            state.ZoomAt(float.Epsilon,10,20);c(state.Zoom==LedgerBrowserState.MinimumZoom,"small positive factor clamps at bounded overview floor");
            float pan=state.PanX,zoom=state.Zoom;foreach(float invalid in new[]{0f,-1,float.NaN,float.PositiveInfinity})state.ZoomAt(invalid,10,20);c(state.PanX==pan&&state.Zoom==zoom,"invalid wheel factors cannot poison zoom/pan");
            state.Fit(float.NaN,100);state.Fit(0,0);c(state.PanX==pan&&state.Zoom==zoom,"missing/transient viewport dimensions do not poison framing");
            // Direct-product usage remains deliberately narrow; recenter/How made still expands full ingredients.
            var usage=Catalog("mixed",12);state=new LedgerBrowserState();state.Bind(usage);state.SelectRoot(usage,"item11");string relation=Semantics(state);
            state.Fit(1000,440);state.ZoomAt(.4f,200,100);state.ZoomAt(2.5f,200,100);c(Semantics(state)==relation,"Used in overview does not eagerly add co-ingredients");
            string workspace=AppDomain.CurrentDomain.BaseDirectory;while(workspace!=null&&!File.Exists(Path.Combine(workspace,"PROJECT_BRIEF.md")))workspace=Path.GetDirectoryName(workspace.TrimEnd(Path.DirectorySeparatorChar));
            string ui=File.ReadAllText(Path.Combine(workspace,"src/TradeLedgerProbeB/LedgerBrowserWindow.cs"));int start=ui.IndexOf("if(browser.Overview){",StringComparison.Ordinal),end=ui.IndexOf("continue;",start,StringComparison.Ordinal);string compact=ui.Substring(start,end-start);
            c(compact.Contains("new GUIContent(item.Name)")&&compact.Contains("BrowserSelect(item.Key,false)")&&!compact.Contains("BrowserIcon(")&&!compact.Contains("BrowserMarket(")&&!compact.Contains("CanExpand("),"runtime overview uses selectable names only, no icon/Market/expansion work");
            c(ui.IndexOf("if(browser.Overview)return;",StringComparison.Ordinal)>0&&ui.Contains("Button(market,\"Market\")")&&ui.Contains("LedgerGraphGeometry.Icon(entry.Value,aspect)"),"batch labels omitted only in overview; full Market/icon path retained");
            c(ui.Contains("graphStyleScaleY!=browser.ScaleY")&&ui.Contains("browser.ScaleY+\"|\"+browser.PanX"),"resize-dependent scale invalidates both style and geometry caches without model rebuild");
            c(ui.Contains("graphProjectionKey!=projectionKey")&&ui.Contains("browser.Viewport(area.width,area.height)"),"real renderer retains cached projection and resize path");
            foreach(int width in new[]{1280,1920,2560}){
                var cat=Catalog("mixed",25);var s=State(cat);var l=LedgerLayout.For(width,width*9/16);float vw=l.Width-l.LeftWidth-44,vh=l.Height-400;s.Fit(vw,vh);
                LedgerGraphRefinementTests.Render(cat,Path.Combine(root,"overview-"+width+".png"),width,width*9/16,"item0",new string[0],true,s,false);
                s.ZoomAt(1/s.Zoom,vw*.55f,vh*.5f);LedgerGraphRefinementTests.Render(cat,Path.Combine(root,"detail-restored-"+width+".png"),width,width*9/16,"item0",new string[0],true,s,false);
                c(File.Exists(Path.Combine(root,"overview-"+width+".png"))&&File.Exists(Path.Combine(root,"detail-restored-"+width+".png")),"overview/detail shared-geometry non-Unity previews "+width);
            }
            File.WriteAllText(Path.Combine(root,"graph-overview-checks.txt"),count+" offline checks; live Unity renderer/input acceptance NOT RUN");
        }
    }
}
