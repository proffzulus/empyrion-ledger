using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TradeLedgerProbeB
{
    public sealed class LedgerBrowserPanels
    {
        public readonly float GraphWidth,DetailsWidth,GraphTop,GraphHeight;public readonly int ToolbarRows;public static readonly float[] ActionWidths={76,90,126,106,34,34};
        public LedgerBrowserPanels(float width,float height){DetailsWidth=Math.Min(340,Math.Min(width*.42f,Math.Max(220,width*.28f)));GraphWidth=width-DetailsWidth-12;int rows=1;float x=0;foreach(float w in ActionWidths){if(x>0&&x+w>GraphWidth){rows++;x=0;}x+=w+6;}ToolbarRows=rows;GraphTop=64+ToolbarRows*32+24;GraphHeight=Math.Max(40,height-GraphTop);}
    }
    public enum LedgerCraftability { Unknown, Craftable, NonCraftable }
    public sealed class LedgerBrowserItem
    {
        public string Key,Name,Markup,Category,Icon,InternalName,Source,RecipeAlias;
        public int Id; public int? Auxiliary;
        public bool Canonical;
        public LedgerViewItem Selection {get{return new LedgerViewItem{Key=Key,Id=Id,Auxiliary=Auxiliary,Canonical=Canonical,Name=Name,Markup=Markup,Category=Category,Sprite=Icon};}}
    }
    public sealed class LedgerBrowserRecipe
    {
        public string Key,OutputKey,Source;
        public int OutputCount;
        public readonly List<LedgerBrowserInput> Inputs=new List<LedgerBrowserInput>();
        public readonly List<string> Devices=new List<string>();
        public bool Manufacturing;
    }
    public sealed class LedgerBrowserInput {public string ItemKey;public int? Quantity;}
    // Runtime adapter supplies effective definitions; this model has no game/reflection/file dependencies.
    public sealed class LedgerBrowserCatalog
    {
        public readonly Dictionary<string,LedgerBrowserItem> Items=new Dictionary<string,LedgerBrowserItem>(StringComparer.Ordinal);
        public readonly List<LedgerBrowserRecipe> Recipes=new List<LedgerBrowserRecipe>();
        public string Fingerprint,Status="Catalog unavailable";
        public bool CompleteRecipes;
        private Dictionary<string,List<LedgerBrowserRecipe>> incoming,outgoing;
        private Dictionary<string,LedgerBrowserRecipe> recipeIndex;
        public LedgerBrowserRecipe Recipe(string key){LedgerBrowserRecipe r;return recipeIndex!=null&&recipeIndex.TryGetValue(key,out r)?r:null;}
        public void Seal(string scope)
        {
            recipeIndex=Recipes.ToDictionary(r=>r.Key,StringComparer.Ordinal);
            incoming=new Dictionary<string,List<LedgerBrowserRecipe>>(StringComparer.Ordinal);outgoing=new Dictionary<string,List<LedgerBrowserRecipe>>(StringComparer.Ordinal);
            foreach(var r in Recipes.Where(r=>r.Manufacturing))
            {
                if(!incoming.ContainsKey(r.OutputKey))incoming[r.OutputKey]=new List<LedgerBrowserRecipe>();incoming[r.OutputKey].Add(r);
                foreach(string key in r.Inputs.Select(i=>i.ItemKey).Distinct()) {if(!outgoing.ContainsKey(key))outgoing[key]=new List<LedgerBrowserRecipe>();outgoing[key].Add(r);}
            }
            var s=new StringBuilder(scope).Append('|').Append(CompleteRecipes);
            foreach(var i in Items.Values.OrderBy(i=>i.Key,StringComparer.Ordinal))s.Append('|').Append(i.Key).Append(':').Append(i.InternalName).Append(':').Append(i.Markup).Append(':').Append(i.Icon).Append(':').Append(i.Category).Append(':').Append(i.RecipeAlias).Append(':').Append(i.Source);
            foreach(var r in Recipes){s.Append('|').Append(r.Key).Append(':').Append(r.OutputKey).Append(':').Append(r.OutputCount).Append(':').Append(r.Manufacturing).Append(':').Append(string.Join(",",r.Devices.ToArray()));foreach(var i in r.Inputs)s.Append(';').Append(i.ItemKey).Append('=').Append(i.Quantity);}
            using(var hash=SHA256.Create())Fingerprint=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(s.ToString()))).Replace("-","");
        }
        public List<LedgerBrowserRecipe> How(string key){List<LedgerBrowserRecipe> r;return incoming!=null&&incoming.TryGetValue(key,out r)?r:new List<LedgerBrowserRecipe>();}
        public List<LedgerBrowserRecipe> Uses(string key){List<LedgerBrowserRecipe> r;return outgoing!=null&&outgoing.TryGetValue(key,out r)?r:new List<LedgerBrowserRecipe>();}
        public LedgerCraftability Craftability(string key)
        {
            LedgerBrowserItem item;if(!Items.TryGetValue(key,out item))return LedgerCraftability.Unknown;
            if(How(key).Count>0)return LedgerCraftability.Craftable;
            // A form/variant with no exact output is not silently given the base definition's recipe.
            return CompleteRecipes&&item.Canonical&&string.IsNullOrWhiteSpace(item.RecipeAlias)?LedgerCraftability.NonCraftable:LedgerCraftability.Unknown;
        }
        public List<LedgerBrowserItem> Query(string search,int filter)
        {
            return Items.Values.Where(i=>(string.IsNullOrWhiteSpace(search)||i.Name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0||i.InternalName.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)
                &&(filter==0||Craftability(i.Key)==(filter==1?LedgerCraftability.Craftable:LedgerCraftability.NonCraftable)))
                .OrderBy(i=>i.Category,StringComparer.OrdinalIgnoreCase).ThenBy(i=>i.Name,StringComparer.OrdinalIgnoreCase).ThenBy(i=>i.Key,StringComparer.Ordinal).ToList();
        }
    }
    public sealed class LedgerGraphNode {public string Key,ItemKey,RecipeKey;public float X,Y;public const float Width=238,Height=192;}
    public sealed class LedgerGraphEdge
    {
        public string From,To,Label,Identity;
        // Independent user-opened directions can share the same recipe/input relationship.
        internal readonly HashSet<string> Expansions=new HashSet<string>(StringComparer.Ordinal);
    }
    public sealed class LedgerBrowserState
    {
        public string Search="",Selected,Notice,Root;public int Filter,Revision;
        public float PanX,PanY,Zoom=1;
        public readonly HashSet<string> Collapsed=new HashSet<string>();
        public readonly Dictionary<string,LedgerGraphNode> Nodes=new Dictionary<string,LedgerGraphNode>();
        public readonly List<LedgerGraphEdge> Edges=new List<LedgerGraphEdge>();
        private readonly Stack<string> back=new Stack<string>();
        private string fingerprint;
        private readonly Dictionary<string,int> expanded=new Dictionary<string,int>();
        public const int MaxNodes=120,MaxEdges=240,ExpansionBatch=8;
        public const float MinimumZoom=.02f,MaximumZoom=1.8f,DetailZoom=.6f;
        public bool Overview {get{return Zoom<DetailZoom;}}
        // Continuous semantic zoom: same model coordinates/identity, compact vertical
        // space below detail scale. No relayout/traversal or recipe rebuild on zoom.
        public static float VerticalScale(float zoom)
        {float t=Math.Max(0,Math.Min(1,(zoom-.3f)/.3f));t=t*t*(3-2*t);return zoom*(.24f+.76f*t);}
        private float overviewFloor;
        private int viewportRevision=-1;
        private float ScaleAt(float zoom){return Math.Max(VerticalScale(zoom),overviewFloor);}
        public float ScaleY {get{return ScaleAt(Zoom);}}
        private static bool Finite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
        public bool Bind(LedgerBrowserCatalog catalog)
        {
            if(fingerprint==catalog.Fingerprint)return false;
            fingerprint=catalog.Fingerprint;Nodes.Clear();Edges.Clear();back.Clear();expanded.Clear();Selected=null;Root=null;Notice=null;PanX=PanY=0;Zoom=1;Revision++;
            return true;
        }
        public void Select(string key,bool remember=true)
        {
            if(key==null)return;
            if(remember&&Selected!=null&&Selected!=key){if(back.Count<128)back.Push(Selected);}
            Selected=key;
            if(Root==null)Root=key;
            if(!Nodes.ContainsKey("i:"+key)) {if(Nodes.Count>=MaxNodes){Notice="Graph limit reached. Recenter an existing item or clear graph.";return;} Nodes.Add("i:"+key,new LedgerGraphNode{Key="i:"+key,ItemKey=key,X=0,Y=Nodes.Values.Count(n=>n.ItemKey!=null)*(LedgerGraphNode.Height+36)});Revision++;}
        }
        // Only the LEFT catalog starts a new graph. Card selection, Back, tabs and Markets do not.
        public bool SelectRoot(LedgerBrowserCatalog catalog,string key)
        {
            if(!catalog.Items.ContainsKey(key))return false;
            if(Root==key){Select(key);return false;}
            Nodes.Clear();Edges.Clear();expanded.Clear();back.Clear();Root=null;Selected=null;Notice=null;PanX=PanY=0;Zoom=1;Revision++;
            Select(key,false);
            if(catalog.How(key).Count>0)Expand(catalog,key,true);else if(catalog.Uses(key).Count>0)Expand(catalog,key,false);
            return true;
        }
        public bool CanExpand(LedgerBrowserCatalog catalog,string key,bool how)
        {
            int offset;expanded.TryGetValue(key+(how?":how":":uses"),out offset);
            return (how?catalog.How(key):catalog.Uses(key)).Skip(offset).Any(r=>Fits(r,key,how));
        }
        private static IEnumerable<int> Inputs(LedgerBrowserRecipe recipe,string key,bool how)
        {return Enumerable.Range(0,recipe.Inputs.Count).Where(n=>how||recipe.Inputs[n].ItemKey==key);}
        private bool Fits(LedgerBrowserRecipe recipe,string key,bool how)
        {
            var inputs=Inputs(recipe,key,how).ToList();
            int nodes=inputs.Select(n=>recipe.Inputs[n].ItemKey).Concat(new[]{recipe.OutputKey}).Distinct().Count(k=>!Nodes.ContainsKey("i:"+k))+(Nodes.ContainsKey("r:"+recipe.Key)?0:1);
            int edges=inputs.Count(n=>!Edges.Any(e=>e.Identity==recipe.Key+":in:"+n))+(Edges.Any(e=>e.Identity==recipe.Key+":out")?0:1);
            return Nodes.Count+nodes<=MaxNodes&&Edges.Count+edges<=MaxEdges;
        }
        public bool CanCollapse(LedgerBrowserCatalog catalog,string key,bool how)
        {string step=key+(how?":how":":uses");return Edges.Any(e=>e.Expansions.Contains(step));}
        public void Collapse(LedgerBrowserCatalog catalog,string key,bool how)
        {
            string step=key+(how?":how":":uses");
            foreach(var edge in Edges)edge.Expansions.Remove(step);
            Edges.RemoveAll(e=>e.Expansions.Count==0);expanded.Remove(step);
            // Keep independently explored branches, even if collapsing this direction disconnects
            // them from the root. A usage collapse must not undo an intentional full recipe opening.
            var keep=new HashSet<string>(Edges.SelectMany(e=>new[]{e.From,e.To}));
            if(Root!=null)keep.Add("i:"+Root);if(Selected!=null)keep.Add("i:"+Selected);
            foreach(string n in Nodes.Keys.Where(n=>!keep.Contains(n)).ToArray())Nodes.Remove(n);
            if(!Nodes.ContainsKey("i:"+Selected))Selected=Root;Relayout(catalog);
        }
        public void Back(){if(back.Count>0)Select(back.Pop(),false);}
        public void Clear(){Nodes.Clear();Edges.Clear();expanded.Clear();Notice=null;Root=Selected;Revision++;if(Selected!=null)Select(Selected,false);}
        private void Edge(string from,string to,string label,string identity,string expansion)
        {
            var edge=Edges.FirstOrDefault(e=>e.Identity==identity);
            if(edge==null){edge=new LedgerGraphEdge{From=from,To=to,Label=label,Identity=identity};Edges.Add(edge);}
            edge.Expansions.Add(expansion);
        }
        private void PlaceItem(string key,float x,float y)
        {
            string id="i:"+key;if(Nodes.ContainsKey(id))return;
            float step=LedgerGraphNode.Height+20,desired=(float)Math.Floor(y/step+.5f)*step;y=desired;
            int attempt=0;while(Nodes.Values.Any(n=>n.ItemKey!=null&&Math.Abs(n.X-x)<LedgerGraphNode.Width+24&&Math.Abs(n.Y-y)<LedgerGraphNode.Height+16))
            {attempt++;y=desired+((attempt+1)/2)*(attempt%2==1?step:-step);}
            Nodes.Add(id,new LedgerGraphNode{Key=id,ItemKey=key,X=x,Y=y});
        }
        public void Expand(LedgerBrowserCatalog catalog,string itemKey,bool how)
        {
            string previous=Selected;Select(itemKey,false);Selected=previous??itemKey;var source=Nodes.ContainsKey("i:"+itemKey)?Nodes["i:"+itemKey]:null;if(source==null)return;
            string step=itemKey+(how?":how":":uses");int offset;expanded.TryGetValue(step,out offset);
            var choices=how?catalog.How(itemKey):catalog.Uses(itemKey);
            Notice=choices.Count==0?(catalog.CompleteRecipes?"No manufacturing recipe in active data for this direction.":"Recipe extraction unavailable for this direction."):null;
            foreach(var r in choices.Skip(offset).Take(ExpansionBatch))
            {
                string rk="r:"+r.Key;var inputIndexes=Inputs(r,itemKey,how).ToList();
                if(!Fits(r,itemKey,how)){Notice="Graph limit reached (120 nodes / 240 links). Clear graph to explore a new branch.";break;}
                // Recipe identities remain in the model, but only item cards take layout space.
                if(!Nodes.ContainsKey(rk))Nodes.Add(rk,new LedgerGraphNode{Key=rk,RecipeKey=r.Key});
                float stepX=LedgerGraphNode.Width+170,stepY=LedgerGraphNode.Height+20;
                if(how)
                {
                    var inputs=r.Inputs.Select(i=>i.ItemKey).Distinct().ToList();
                    for(int n=0;n<inputs.Count;n++)PlaceItem(inputs[n],source.X-stepX,source.Y+(n-(inputs.Count-1)/2f)*stepY);
                }
                else PlaceItem(r.OutputKey,source.X+stepX,source.Y+offset*stepY);
                foreach(int n in inputIndexes){var input=r.Inputs[n];Edge("i:"+input.ItemKey,rk,input.Quantity.HasValue?input.Quantity.Value.ToString(CultureInfo.InvariantCulture):"?",r.Key+":in:"+n,step);}
                Edge(rk,"i:"+r.OutputKey,r.OutputCount.ToString(CultureInfo.InvariantCulture),r.Key+":out",step);offset++;
            }
            expanded[step]=offset;if(offset<choices.Count&&Notice==null)Notice="More recipes available: expand this direction again.";
            Relayout(catalog);
        }
        // Condense strongly connected components before longest-path ranking. Cycles share a
        // column, not duplicated subgraphs; their returning edges use outside lanes.
        public void Relayout(LedgerBrowserCatalog catalog)
        {
            var keys=Nodes.Values.Where(n=>n.ItemKey!=null).Select(n=>n.ItemKey).OrderBy(k=>k,StringComparer.Ordinal).ToList();
            var next=keys.ToDictionary(k=>k,k=>new HashSet<string>());var prior=keys.ToDictionary(k=>k,k=>new HashSet<string>());
            foreach(var n in Nodes.Values.Where(n=>n.RecipeKey!=null))
            {
                var recipe=catalog.Recipe(n.RecipeKey);if(recipe==null||!next.ContainsKey(recipe.OutputKey))continue;
                foreach(var edge in Edges.Where(e=>e.To==n.Key))
                {
                    string input=Nodes[edge.From].ItemKey;if(input==null||!next.ContainsKey(input))continue;
                    next[input].Add(recipe.OutputKey);prior[recipe.OutputKey].Add(input);
                }
            }
            var seen=new HashSet<string>();var order=new List<string>();Action<string> visit=null;
            visit=k=>{if(!seen.Add(k))return;foreach(var v in next[k].OrderBy(x=>x,StringComparer.Ordinal))visit(v);order.Add(k);};foreach(string k in keys)visit(k);
            var component=new Dictionary<string,int>();int group=0;Action<string> assign=null;
            assign=k=>{if(component.ContainsKey(k))return;component[k]=group;foreach(var v in prior[k].OrderBy(x=>x,StringComparer.Ordinal))assign(v);};
            foreach(string k in order.AsEnumerable().Reverse()){if(component.ContainsKey(k))continue;assign(k);group++;}
            var edges=Enumerable.Range(0,group).ToDictionary(k=>k,k=>new HashSet<int>());var degree=new int[group];var rank=new int[group];
            foreach(string k in keys)foreach(string v in next[k])if(component[k]!=component[v]&&edges[component[v]].Add(component[k]))degree[component[k]]++;
            var ready=new SortedSet<int>(Enumerable.Range(0,group).Where(k=>degree[k]==0));
            while(ready.Count>0){int k=ready.Min;ready.Remove(k);foreach(int v in edges[k].OrderBy(x=>x)){rank[v]=Math.Max(rank[v],rank[k]+1);if(--degree[v]==0)ready.Add(v);}}
            int rootRank=Root!=null&&component.ContainsKey(Root)?rank[component[Root]]:0;
            foreach(var layer in keys.GroupBy(k=>rank[component[k]]).OrderBy(g=>g.Key))
            {
                var rows=layer.OrderBy(k=>next[k].Where(v=>rank[component[v]]<layer.Key).Select(v=>(double)Nodes["i:"+v].Y).DefaultIfEmpty(0).Average()).ThenBy(k=>k,StringComparer.Ordinal).ToList();
                for(int n=0;n<rows.Count;n++){var node=Nodes["i:"+rows[n]];node.X=(rootRank-layer.Key)*(LedgerGraphNode.Width+170);node.Y=(n-(rows.Count-1)/2f)*(LedgerGraphNode.Height+44);}
            }
            float rootY=Root!=null&&Nodes.ContainsKey("i:"+Root)?Nodes["i:"+Root].Y:0;
            foreach(var n in Nodes.Values.Where(n=>n.ItemKey!=null))n.Y-=rootY;
            Revision++;
        }
        private float viewportWidth,viewportHeight;
        public void Viewport(float width,float height)
        {
            if(!Finite(width)||!Finite(height)||width<=0||height<=0)return;
            float oldScale=ScaleY;
            if(viewportRevision!=Revision||height!=viewportHeight){
                var items=Nodes.Values.Where(n=>n.ItemKey!=null).ToList();
                float span=items.Count==0?LedgerGraphNode.Height:items.Max(n=>n.Y)+LedgerGraphNode.Height-items.Min(n=>n.Y);
                overviewFloor=Math.Min(.08f,Math.Max(0,height-64)/Math.Max(LedgerGraphNode.Height,span));viewportRevision=Revision;
            }
            if(viewportWidth>0&&viewportHeight>0){PanX+=(width-viewportWidth)/2;PanY=height/2-(viewportHeight/2-PanY)*ScaleY/oldScale;}
            viewportWidth=width;viewportHeight=height;
        }
        public void Frame(float width,float height)
        {
            Fit(width,height);LedgerGraphNode n;
            if(Selected!=null&&Nodes.TryGetValue("i:"+Selected,out n)&&(PanX+n.X*Zoom<12||PanX+(n.X+LedgerGraphNode.Width)*Zoom>width-12||PanY+n.Y*ScaleY<12||PanY+(n.Y+LedgerGraphNode.Height)*ScaleY>height-12))Center(width,height);
        }
        public void Center(float width,float height)
        {LedgerGraphNode n;if(Selected!=null&&Nodes.TryGetValue("i:"+Selected,out n)){PanX=width/2-(n.X+LedgerGraphNode.Width/2)*Zoom;PanY=height/2-(n.Y+LedgerGraphNode.Height/2)*ScaleY;}}
        public void Fit(float width,float height)
        {
            if(!Finite(width)||!Finite(height)||width<=32||height<=32)return;
            var visible=Nodes.Values.Where(n=>n.ItemKey!=null).ToList();if(visible.Count==0)return;
            float minX=visible.Min(n=>n.X),minY=visible.Min(n=>n.Y),w=visible.Max(n=>n.X)+LedgerGraphNode.Width-minX,h=visible.Max(n=>n.Y)+LedgerGraphNode.Height-minY;
            // Preserve outside cycle/skip lanes, not just node rectangles. Vertical
            // compression is monotone, so a small fixed bisection fits either mode.
            overviewFloor=Math.Min(.08f,Math.Max(0,height-64)/Math.Max(LedgerGraphNode.Height,h));
            float low=MinimumZoom,high=Math.Min(1.4f,(width-32)/(w+160));
            high=Math.Max(low,high);
            for(int n=0;n<28;n++){float mid=(low+high)/2;if(h*ScaleAt(mid)+160*mid<=height-32)low=mid;else high=mid;}
            Zoom=low;PanX=(width-w*Zoom)/2-minX*Zoom;PanY=(height-h*ScaleY)/2-minY*ScaleY;
        }
        public void ZoomAt(float factor,float x,float y)
        {
            if(!Finite(factor)||factor<=0||!Finite(x)||!Finite(y)||!Finite(Zoom)||Zoom<=0)return;
            float next=Math.Max(MinimumZoom,Math.Min(MaximumZoom,Zoom*factor));
            PanX=x-(x-PanX)*next/Zoom;PanY=y-(y-PanY)*ScaleAt(next)/ScaleY;Zoom=next;
        }
    }
}
