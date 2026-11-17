using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public sealed partial class LedgerPrototypeWindow
    {
        private readonly LedgerBrowserState browser=new LedgerBrowserState();
        private LedgerBrowserCatalog browserCatalog;
        private LedgerNativeBrowser nativeBrowser;
        private bool browserReload,browserDetailsDirty;
        private long browserReadAt;
        private Vector2 browserItemsScroll,browserDetailScroll;
        private string browserDetailsKey,browserDetailText,browserTreeKey,browserQueryKey;
        private List<LedgerTreeEntry> browserTree;
        private float browserDetailWidth=-1,browserDetailHeight;
        private LedgerItemDetails browserDetails;
        private readonly Dictionary<string,LedgerIcon> browserIcons=new Dictionary<string,LedgerIcon>();
        private bool browserCenter,browserFrame;
        private int graphDragControl;
        private readonly LedgerGraphDrag graphDrag=new LedgerGraphDrag();
        private bool graphSuppressRelease;
        private LedgerGraphProjection graphProjection;
        private string graphProjectionKey;
        private GUIStyle graphText,graphSmall;
        private float graphStyleZoom,graphStyleScaleY;
        private void ResetItemBrowser()
        {
            browserCatalog=null;nativeBrowser=null;browserTree=null;browserTreeKey=null;browserReadAt=0;browserReload=true;browserDetailsKey=null;browserDetails=null;browserIcons.Clear();graphTitles.Clear();graphProjection=null;CancelGraphDrag();
            var empty=new LedgerBrowserCatalog();empty.Seal(world??"unloaded");browser.Bind(empty);
        }
        // Native metadata stays on the API/UI thread. No worker calls into Unity and no per-frame catalog/DB reads.
        private void PollItemBrowser(bool ready,long now)
        {
            if(!ready||world==null||NativeCatalog==null)return;
            if(!open||view!=5)return;
            if(!browserReload&&browserReadAt>0&&now-browserReadAt<30000)return;
            browserReload=false;browserReadAt=now;
            try
            {
                catalog=NativeCatalog.Read(true);
                if(nativeBrowser==null)nativeBrowser=new LedgerNativeBrowser(Application.GetType().Assembly,NativeCatalog);
                var next=nativeBrowser.Read(catalog,world+"|"+ProbeBuild.RuntimeHash);
                bool changed=browser.Bind(next);browserCatalog=next;browserDetailsDirty=true;detailsKey=null;
                if(changed){browserDetailsKey=null;detailsKey=null;browserIcons.Clear();browserCenter=true;}
                if(browser.Selected==null&&next.Items.Count>0){browser.SelectRoot(next,next.Query("",0)[0].Key);browserFrame=true;browserCenter=false;}
                Log("LedgerItemBrowserCatalog","entries="+next.Items.Count+" templates="+next.Recipes.Count+" completeRecipes="+next.CompleteRecipes+" changed="+changed+" fingerprint="+next.Fingerprint+" source=resolved-runtime-metadata");
            }
            catch(Exception e){browserCatalog=null;browserDetailsKey=null;browserReload=false;Log("LedgerItemBrowserUnavailable",e.ToString());}
        }
        private void BrowserSelect(string key,bool center=true)
        {if(browserCatalog==null||!browserCatalog.Items.ContainsKey(key))return;browser.Select(key);browserCenter=center;}
        private void BrowserCatalogSelect(string key)
        {
            if(browserCatalog==null||!browserCatalog.Items.ContainsKey(key))return;
            CancelGraphDrag();
            if(browser.SelectRoot(browserCatalog,key)){browserFrame=true;browserCenter=false;graphProjection=null;}
            else browserCenter=true;
        }
        private void CancelGraphDrag()
        {
            if(graphDrag.Active)graphSuppressRelease=true;
            graphDrag.Cancel();
            if(graphDragControl!=0&&GUIUtility.hotControl==graphDragControl)GUIUtility.hotControl=0;
            graphDragControl=0;
        }
        private void PollGraphDrag(bool visible)
        {
            if(graphDrag.Active&&(!Input.GetMouseButton(graphDrag.Button)||!UnityEngine.Application.isFocused||!visible))CancelGraphDrag();
        }
        // Handle captured motion/release BEFORE BeginGroup, which can mask outside events as Ignore.
        private void HandleGraphDrag()
        {
            Event ev=Event.current;var type=ev.rawType;
            if(graphSuppressRelease&&type==EventType.MouseUp){graphSuppressRelease=false;ev.Use();return;}
            if(!graphDrag.Active)return;
            if(GUIUtility.hotControl!=graphDragControl){CancelGraphDrag();return;}
            if(type==EventType.MouseDrag&&ev.button==graphDrag.Button)
            {float dx,dy;if(graphDrag.Move(ev.mousePosition.x,ev.mousePosition.y,out dx,out dy)){browser.PanX+=dx;browser.PanY+=dy;ev.Use();}}
            else if(type==EventType.MouseUp&&graphDrag.Release(ev.button))
            {GUIUtility.hotControl=0;graphDragControl=0;ev.Use();}
            else if(type==EventType.KeyDown&&ev.keyCode==KeyCode.Escape)CancelGraphDrag();
        }
        private void BrowserMarket(string key)
        {
            LedgerBrowserItem item;if(browserCatalog==null||!browserCatalog.Items.TryGetValue(key,out item))return;
            SelectCatalogItem(item.Selection,true); // Browser state is independent; returning never replaces graph/filter state.
        }
        private LedgerIcon BrowserIcon(LedgerBrowserItem item)
        {
            if(item.Icon==null)return null;LedgerIcon found;
            if(browserIcons.TryGetValue(item.Key,out found))return found;
            try{found=NativeCatalog.Icon(item.Icon);}catch(Exception){found=null;}
            if(browserIcons.Count<512)browserIcons[item.Key]=found;return found;
        }
        private bool BrowserMarketButton(Rect r,string key)
        {
            // Reuse the known native right-arrow asset as a compact offers-link icon, with an explicit hover label.
            if(ArtButton(r,"ArrowNavigationRight",">","Market offers for this item")){BrowserMarket(key);return true;}return false;
        }
        private void DrawItemBrowser()
        {
            Rect c=Content;float left=layout.LeftWidth;Rect right=new Rect(c.x+left+12,c.y,c.width-left-12,c.height);
            browser.Search=Search(new Rect(c.x,c.y,left,28),browser.Search,"LedgerCatalogSearch","Search catalog");
            string[] filters={"All","Craftable","Non-craftable"};
            for(int n=0;n<3;n++)if(GUI.Toggle(new Rect(c.x+n*left/3,c.y+32,left/3-3,26),browser.Filter==n,filters[n],GUI.skin.button))browser.Filter=n;
            if(browserCatalog==null){Label(new Rect(right.x,right.y,right.width,32),"Item catalog unavailable. Refresh to retry.");return;}
            var byKey=browserCatalog.Items;
            string queryKey=browserCatalog.Fingerprint+"|"+browser.Filter+"|"+browser.Search;
            string treeKey=queryKey+"|"+string.Join("|",browser.Collapsed.OrderBy(s=>s,StringComparer.Ordinal).ToArray());
            if(browserTree==null||browserTreeKey!=treeKey){browserTreeKey=treeKey;browserTree=LedgerUi.Tree(browserCatalog.Query(browser.Search,browser.Filter).Select(i=>i.Selection).ToList(),browser.Collapsed,browser.Search);if(browserQueryKey!=queryKey){browserItemsScroll.y=0;browserQueryKey=queryKey;}}
            var tree=browserTree;
            Rect port=new Rect(c.x,c.y+64,left,c.height-64);Backplate(port);float width=left-LedgerLayout.Scrollbar-3;
            browserItemsScroll=GUI.BeginScrollView(port,browserItemsScroll,new Rect(0,0,width,tree.Count*LedgerLayout.TreeRow),false,true);
            int start=LedgerLayout.FirstRow(browserItemsScroll.y,tree.Count,LedgerLayout.TreeRow),end=LedgerLayout.EndRow(browserItemsScroll.y,port.height,tree.Count,LedgerLayout.TreeRow);
            for(int n=start;n<end;n++)
            {
                var e=tree[n];Rect r=new Rect(0,n*LedgerLayout.TreeRow,width,LedgerLayout.TreeRow);
                if(e.Item==null){if(SelectRow(r,false)){if(!browser.Collapsed.Add(e.Category))browser.Collapsed.Remove(e.Category);}Label(r,(e.Expanded?"\u25BE  ":"\u25B8  ")+e.Category);}
                else {BrowserMarketButton(new Rect(width-27,r.y+2,25,r.height-4),e.Item.Key);if(SelectRow(new Rect(0,r.y,width-29,r.height),browser.Selected==e.Item.Key))BrowserCatalogSelect(e.Item.Key);GUI.Label(new Rect(18,r.y,width-49,r.height),LedgerItemDisplay.NativeRich(e.Item.Markup??e.Item.Name),itemButton);}
            }
            GUI.EndScrollView();
            LedgerBrowserItem selected;if(browser.Selected==null||!byKey.TryGetValue(browser.Selected,out selected)){Label(right,"Select an item");return;}
            var panels=new LedgerBrowserPanels(right.width,right.height);
            var graph=new Rect(right.x,right.y+panels.GraphTop,panels.GraphWidth,panels.GraphHeight);
            var side=new Rect(right.x+panels.GraphWidth+12,right.y,panels.DetailsWidth,right.height);
            Backplate(new Rect(right.x,right.y,panels.GraphWidth,58));
            var icon=BrowserIcon(selected);Rect ir=new Rect(right.x+6,right.y+5,48,48);
            if(icon!=null)GUI.DrawTextureWithTexCoords(ir,icon.Texture,icon.Uv);else GUI.Box(ir,new GUIContent("--","No item image"));
            if(ir.Contains(Event.current.mousePosition))iconHoverName=selected.Markup??selected.Name;
            Label(new Rect(right.x+64,right.y+3,panels.GraphWidth-110,30),LedgerItemDisplay.NativeRich(selected.Markup??selected.Name),true);
            BrowserMarketButton(new Rect(right.x+panels.GraphWidth-34,right.y+6,28,26),selected.Key);
            string craft=browserCatalog.Craftability(selected.Key)==LedgerCraftability.Craftable?"Recipes: "+browserCatalog.How(selected.Key).Count:browserCatalog.Craftability(selected.Key)==LedgerCraftability.NonCraftable?"No crafting recipe":"Craftability unavailable";
            Label(new Rect(right.x+64,right.y+33,panels.GraphWidth-72,23),craft);
            string[] actions={"< Item","Fit graph","Center selected","Clear graph","-","+"};var sizes=LedgerBrowserPanels.ActionWidths;float x=right.x,y=right.y+64;
            for(int i=0;i<actions.Length;i++){
                if(x+sizes[i]>right.x+panels.GraphWidth){x=right.x;y+=32;}
                if(Button(new Rect(x,y,sizes[i],28),actions[i])){
                    if(i==0){browser.Back();browserCenter=true;}else if(i==1)browser.Fit(graph.width,graph.height);else if(i==2)browserCenter=true;else if(i==3){browser.Clear();browserCenter=true;}else browser.ZoomAt(i==4?1/1.2f:1.2f,graph.width/2,graph.height/2);
                }x+=sizes[i]+6;
            }
            string notice=browser.Notice??(browser.Overview?"Overview - zoom in for item cards":!browserCatalog.CompleteRecipes?browserCatalog.Status:null);
            if(notice!=null)Label(new Rect(right.x,right.y+panels.GraphTop-24,panels.GraphWidth,24),notice);
            RefreshBrowserDetails(selected);DrawBrowserInformation(side);DrawBrowserGraph(graph);
        }
        private void DrawBrowserInformation(Rect area)
        {
            Backplate(area);Border(area);float width=area.width-LedgerLayout.Scrollbar-12;
            string body=browserDetailText??"Details unavailable";
            if(browserDetailWidth!=width){browserDetailWidth=width;browserDetailHeight=rich.CalcHeight(new GUIContent(body),width)+12;}
            var parts=browserDetails==null?new List<LedgerIngredient>():browserDetails.Ingredients;
            float height=browserDetailHeight+64+parts.Count*30;
            browserDetailScroll=GUI.BeginScrollView(new Rect(area.x+4,area.y+4,area.width-8,area.height-8),browserDetailScroll,new Rect(0,0,width,height),false,true);
            GUI.Label(new Rect(3,3,width-6,browserDetailHeight),body,rich);
            float y=browserDetailHeight+4;Label(new Rect(3,y,width-6,27),"Recipe",true);y+=28;
            Label(new Rect(3,y,width-6,26),browserDetails==null?"Recipe unavailable":browserDetails.OutputCount.HasValue?"Output: "+browserDetails.OutputCount:browserDetails.RecipeState);y+=30;
            foreach(var ingredient in parts){
                var rect=new Rect(2,y,width-4,28);if(ingredient.Auxiliary.HasValue&&SelectRow(rect,false)){string key=ingredient.Auxiliary.Value==0?ingredient.Id+":catalog":ingredient.Id+":"+ingredient.Auxiliary;BrowserSelect(key);}
                string line=(ingredient.Quantity.HasValue?ingredient.Quantity.ToString():"?")+" \u00D7 "+LedgerItemDisplay.NativeRich(ingredient.Name);
                GUI.Label(rect,line,itemButton);if(rect.Contains(Event.current.mousePosition))iconHoverName=line;y+=30;
            }GUI.EndScrollView();
        }
        private void RefreshBrowserDetails(LedgerBrowserItem selected)
        {
            string key=browserCatalog.Fingerprint+"|"+selected.Key;if(browserDetailsKey==key&&!browserDetailsDirty)return;
            if(browserDetailsKey!=key)browserDetailScroll=Vector2.zero;browserDetailsDirty=false;browserDetailsKey=key;browserDetails=null;browserDetailWidth=-1;
            if(!selected.Canonical&&selected.Auxiliary.GetValueOrDefault()!=0){browserDetailText="Variant details unavailable";return;}
            try
            {
                if(nativeDetails==null)nativeDetails=new LedgerNativeItemDetails(Application.GetType().Assembly,NativeCatalog);
                browserDetails=nativeDetails.Read(selected.Selection);
                browserDetailText=LedgerItemDisplay.Details(browserDetails);
            }
            catch(Exception e){browserDetailText="Item details unavailable";Log("LedgerBrowserDetailsUnavailable",e.GetType().Name);}
        }
        private readonly Dictionary<string,string> graphTitles=new Dictionary<string,string>();
        private static Rect GraphRect(LedgerGraphBox box){return new Rect(box.X,box.Y,box.Width,box.Height);}
        private void GraphStroke(LedgerGraphPoint a,LedgerGraphPoint b,float width,float height)
        {
            if(!LedgerGraphGeometry.Clip(ref a,ref b,width,height))return;
            if(Math.Abs(a.X-b.X)>.01f&&Math.Abs(a.Y-b.Y)>.01f)
            {
                int steps=(int)Math.Ceiling(Math.Max(Math.Abs(b.X-a.X),Math.Abs(b.Y-a.Y)));
                for(int i=0;i<=steps;i++){float t=i/(float)steps;float x=a.X+(b.X-a.X)*t,y=a.Y+(b.Y-a.Y)*t;GUI.DrawTexture(new Rect(Math.Max(0,x),Math.Max(0,y),Math.Min(1.5f,width-x),Math.Min(1.5f,height-y)),Texture2D.whiteTexture);}return;
            }
            // Axis aligned routed portions share the same clipped group.
            Rect line=new Rect(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(1.5f,Math.Abs(b.X-a.X)),Math.Max(1.5f,Math.Abs(b.Y-a.Y)));
            line=Rect.MinMaxRect(Math.Max(0,line.x),Math.Max(0,line.y),Math.Min(width,line.xMax),Math.Min(height,line.yMax));
            if(line.width>0&&line.height>0)GUI.DrawTexture(line,Texture2D.whiteTexture);
        }
        private void DrawConnections(LedgerGraphProjection graph,Rect area)
        {
            if(Event.current.type==EventType.Repaint)
            {
                var old=GUI.color;GUI.color=new Color(.35f,.7f,.85f,.9f);
                foreach(var link in graph.Connections)
                {
                    for(int n=1;n<link.Points.Count;n++)GraphStroke(link.Points[n-1],link.Points[n],area.width,area.height);
                    var end=link.Points[link.Points.Count-1];var prior=link.Points[link.Points.Count-2];float direction=end.X>=prior.X?1:-1;
                    if(end.X>=0&&end.X<=area.width&&end.Y>=0&&end.Y<=area.height)
                        for(int i=0;i<4;i++)GraphStroke(new LedgerGraphPoint(end.X-direction*(i*2+2),end.Y-i),new LedgerGraphPoint(end.X-direction*(i*2+2),end.Y+i+1),area.width,area.height);
                }
                GUI.color=old;
            }
            if(browser.Overview)return; // Keep topology strokes, omit detail-only batch labels.
            foreach(var link in graph.Connections)
            {
                var size=graphSmall.CalcSize(new GUIContent(link.Label));Rect label=new Rect(link.LabelAt.X-size.x/2-4,link.LabelAt.Y-2,size.x+8,21);
                if(!label.Overlaps(new Rect(0,0,area.width,area.height)))continue;
                Backplate(label,.98f);GUI.Label(new Rect(label.x+4,label.y,label.width-8,label.height),link.Label,graphSmall);
                if(label.Contains(Event.current.mousePosition))hoverLabel=LedgerGraphGeometry.Description(link,browserCatalog);
            }
        }
        private void DrawBrowserGraph(Rect area)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.GraphRender))
            {
            Backplate(area,.88f);Border(area);browser.Viewport(area.width,area.height);
            if(browserFrame){browser.Frame(area.width,area.height);browserFrame=false;browserCenter=false;}
            if(browserCenter){browser.Center(area.width,area.height);browserCenter=false;}
            if(graphText==null||graphStyleZoom!=browser.Zoom||graphStyleScaleY!=browser.ScaleY)
            {
                graphTitles.Clear();graphStyleZoom=browser.Zoom;graphStyleScaleY=browser.ScaleY;
                graphText=new GUIStyle(itemButton){clipping=TextClipping.Clip,richText=!browser.Overview,fontSize=browser.Overview?LedgerGraphGeometry.OverviewFont(browser.Zoom):Math.Max(11,(int)(LedgerLayout.BodyFont*browser.Zoom)),wordWrap=true,padding=new RectOffset(0,0,0,0)};
                graphSmall=new GUIStyle(graphText){fontSize=Math.Max(11,(int)(LedgerLayout.SmallFont*Math.Min(browser.Zoom,1))),wordWrap=false,alignment=TextAnchor.MiddleCenter};
            }
            string projectionKey=browserCatalog.Fingerprint+"|"+browser.Revision+"|"+browser.Nodes.Count+"|"+browser.Edges.Count+"|"+browser.Zoom+"|"+browser.ScaleY+"|"+browser.PanX+"|"+browser.PanY;
            if(graphProjection==null||graphProjectionKey!=projectionKey){graphProjectionKey=projectionKey;graphProjection=LedgerGraphGeometry.Project(browser,browserCatalog);}
            var projection=graphProjection;
            int control=GUIUtility.GetControlID(731026,FocusType.Passive);
            GUI.BeginGroup(area);
            try
            {
                DrawConnections(projection,area);
                foreach(var entry in projection.Items)
                {
                    Rect r=GraphRect(entry.Value);if(!r.Overlaps(new Rect(0,0,area.width,area.height)))continue;
                    LedgerBrowserItem item;if(!browserCatalog.Items.TryGetValue(entry.Key,out item))continue;
                    Backplate(r,.96f);Border(r);
                    if(browser.Overview){
                        // Names only. No icon lookup, Market/expansion controls or recipe parsing.
                        if(SelectRow(r,browser.Selected==item.Key))BrowserSelect(item.Key,false);
                        var overviewTitle=new Rect(r.x+1,r.y,r.width-2,r.height);
                        GUI.Label(overviewTitle,new GUIContent(item.Name),graphText);
                        if(r.Contains(Event.current.mousePosition))iconHoverName=item.Markup??item.Name;
                        continue;
                    }
                    var controls=LedgerGraphGeometry.Controls(entry.Value);
                    var how=GraphRect(controls[0]);var uses=GraphRect(controls[1]);var market=GraphRect(controls[2]);
                    if(Button(market,"Market")){CancelGraphDrag();BrowserMarket(item.Key);}
                    if(browser.CanExpand(browserCatalog,item.Key,true)&&GUI.Button(how,new GUIContent("+","Expand ingredients"),graphSmall))browser.Expand(browserCatalog,item.Key,true);
                    else if(!browser.CanExpand(browserCatalog,item.Key,true)&&browser.CanCollapse(browserCatalog,item.Key,true)&&GUI.Button(how,new GUIContent("-","Collapse ingredients"),graphSmall))browser.Collapse(browserCatalog,item.Key,true);
                    if(browser.CanExpand(browserCatalog,item.Key,false)&&GUI.Button(uses,new GUIContent("+","Expand products"),graphSmall))browser.Expand(browserCatalog,item.Key,false);
                    else if(!browser.CanExpand(browserCatalog,item.Key,false)&&browser.CanCollapse(browserCatalog,item.Key,false)&&GUI.Button(uses,new GUIContent("-","Collapse products"),graphSmall))browser.Collapse(browserCatalog,item.Key,false);
                    Rect title=GraphRect(LedgerGraphGeometry.Header(entry.Value));
                    if(SelectRow(new Rect(r.x+how.width+6,r.y,r.width-2*(how.width+6),market.y-r.y-2),browser.Selected==item.Key))BrowserSelect(item.Key,false);
                    var icon=BrowserIcon(item);
                    float aspect=icon==null||icon.Texture.height*icon.Uv.height<=0?1:icon.Texture.width*icon.Uv.width/(icon.Texture.height*icon.Uv.height);
                    Rect picture=GraphRect(LedgerGraphGeometry.Icon(entry.Value,aspect));
                    if(icon!=null)GUI.DrawTextureWithTexCoords(picture,icon.Texture,icon.Uv);
                    else GUI.Label(picture,new GUIContent("No image","Item image unavailable"),graphText);
                    if(picture.Contains(Event.current.mousePosition))iconHoverName=item.Markup??item.Name;
                    string cacheKey=browserCatalog.Fingerprint+"|"+item.Key+"|"+title.width+"|"+title.height;string caption;
                    if(!graphTitles.TryGetValue(cacheKey,out caption))
                    {
                        caption=LedgerItemDisplay.NativeRich(item.Markup??item.Name);
                        if(graphText.CalcHeight(new GUIContent(caption),title.width)>title.height)
                            caption=LedgerGraphGeometry.Title(LedgerPresentation.Text(item.Name),title.width,title.height,(value,w)=>graphText.CalcHeight(new GUIContent(value),w));
                        if(graphTitles.Count<512)graphTitles[cacheKey]=caption;
                    }
                    GUI.Label(title,caption,graphText);
                    if(title.Contains(Event.current.mousePosition))iconHoverName=item.Markup??item.Name;
                }
                Event ev=Event.current;Rect bounds=new Rect(0,0,area.width,area.height);
                if(GUI.enabled&&ev.type==EventType.ScrollWheel&&bounds.Contains(ev.mousePosition)){browser.ZoomAt(ev.delta.y>0?1/1.12f:1.12f,ev.mousePosition.x,ev.mousePosition.y);ev.Use();}
                if(GUI.enabled&&!graphDrag.Active&&ev.type==EventType.MouseDown&&bounds.Contains(ev.mousePosition)&&(ev.button==0||ev.button==2))
                {graphSuppressRelease=false;graphDrag.Begin(ev.button,ev.mousePosition.x+area.x,ev.mousePosition.y+area.y);graphDragControl=control;GUIUtility.hotControl=control;ev.Use();}
            }
            finally{GUI.EndGroup();}

            }
        }
    }
}
