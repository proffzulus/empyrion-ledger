using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    // All values are graph-group-local logical pixels. Parent origins/scales belong only to the caller's GUI group.
    public struct LedgerGraphPoint {public float X,Y;public LedgerGraphPoint(float x,float y){X=x;Y=y;}}
    public struct LedgerGraphBox
    {
        public float X,Y,Width,Height;public float Right{get{return X+Width;}}public float Bottom{get{return Y+Height;}}
        public LedgerGraphBox(float x,float y,float width,float height){X=x;Y=y;Width=width;Height=height;}
    }
    public sealed class LedgerItemConnection
    {
        public string RecipeKey,InputKey,OutputKey,Batch,Label;
        public int InputIndex,OutputCount;public int? InputCount;
        public readonly List<LedgerGraphPoint> Points=new List<LedgerGraphPoint>();
        public LedgerGraphPoint LabelAt;
    }
    public sealed class LedgerGraphProjection
    {
        public readonly Dictionary<string,LedgerGraphBox> Items=new Dictionary<string,LedgerGraphBox>();
        public readonly List<LedgerItemConnection> Connections=new List<LedgerItemConnection>();
    }
    public sealed class LedgerGraphDrag
    {
        public bool Active {get;private set;}public int Button {get;private set;}
        private float x,y;
        public void Begin(int button,float px,float py){Active=true;Button=button;x=px;y=py;}
        public bool Move(float px,float py,out float dx,out float dy)
        {dx=dy=0;if(!Active)return false;dx=px-x;dy=py-y;x=px;y=py;return true;}
        public bool Release(int button){if(!Active||button!=Button)return false;Cancel();return true;}
        public bool Validate(bool held,bool focus,bool visible)
        {if(Active&&(!held||!focus||!visible)){Cancel();return false;}return Active;}
        public void Cancel(){Active=false;}
    }
    public static class LedgerGraphGeometry
    {
        public static int OverviewFont(float zoom){return Math.Max(7,Math.Min(11,(int)(16*zoom)));}
        public static string BatchName(int n)
        {string s="";do{s=(char)('A'+n%26)+s;n=n/26-1;}while(n>=0);return s;}
        public static LedgerGraphProjection Project(LedgerBrowserState state,LedgerBrowserCatalog catalog)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.GraphBuild))
            {
            var result=new LedgerGraphProjection();
            foreach(var node in state.Nodes.Values.Where(n=>n.ItemKey!=null))result.Items[node.ItemKey]=new LedgerGraphBox(state.PanX+node.X*state.Zoom,state.PanY+node.Y*state.ScaleY,LedgerGraphNode.Width*state.Zoom,Math.Max(1,LedgerGraphNode.Height*state.ScaleY));
            int batch=0;
            foreach(var node in state.Nodes.Values.Where(n=>n.RecipeKey!=null).OrderBy(n=>n.RecipeKey,StringComparer.Ordinal))
            {
                var recipe=catalog.Recipe(node.RecipeKey);if(recipe==null||!result.Items.ContainsKey(recipe.OutputKey)||!state.Edges.Any(e=>e.From==node.Key&&e.To=="i:"+recipe.OutputKey))continue;
                string name=BatchName(batch++);
                for(int n=0;n<recipe.Inputs.Count;n++)
                {
                    var input=recipe.Inputs[n];
                    if(!result.Items.ContainsKey(input.ItemKey)||!state.Edges.Any(e=>e.Identity==recipe.Key+":in:"+n&&e.From=="i:"+input.ItemKey&&e.To==node.Key))continue;
                    result.Connections.Add(new LedgerItemConnection{RecipeKey=recipe.Key,InputIndex=n,InputKey=input.ItemKey,OutputKey=recipe.OutputKey,InputCount=input.Quantity,OutputCount=recipe.OutputCount,Batch=name,
                        Label=name+"  "+(input.Quantity.HasValue?input.Quantity.Value.ToString(CultureInfo.InvariantCulture):"?")+" > "+recipe.OutputCount.ToString(CultureInfo.InvariantCulture)});
                }
            }
            var outgoing=result.Connections.GroupBy(c=>c.InputKey).ToDictionary(g=>g.Key,g=>g.ToList());
            int route=0;
            foreach(var link in result.Connections)
            {
                var a=result.Items[link.InputKey];var b=result.Items[link.OutputKey];
                var fan=outgoing[link.InputKey];int fanIndex=fan.IndexOf(link);
                float port=fan.Count<=3?(fanIndex-(fan.Count-1)/2f)*Math.Min(24,a.Height/5):((fanIndex+1f)/(fan.Count+1)-.5f)*a.Height*.65f;
                float ax=a.Right,bx=b.X,ay=a.Y+a.Height/2+port,by=b.Y+b.Height/2;
                bool adjacent=b.X>a.X&&b.X-a.X<=(LedgerGraphNode.Width+171)*state.Zoom;
                int pairCount=result.Connections.Count(l=>l.InputKey==link.InputKey&&l.OutputKey==link.OutputKey);
                link.Points.Add(new LedgerGraphPoint(ax,ay));
                if(adjacent&&pairCount==1)
                {
                    // The entire bus lies in the empty gutter between adjacent dependency columns.
                    link.LabelAt=new LedgerGraphPoint((ax+bx)/2,(ay+by)/2-16);
                }
                else
                {
                    // A skipped layer/cycle must use a clear horizontal corridor, never cross a card.
                    float margin=(18+(route%7)*6)*state.Zoom,exit=ax+margin,entry=bx-margin;
                    float lane=ClearLane(result.Items.Values,exit,entry,ay,by,route,state.Zoom);
                    link.Points.Add(new LedgerGraphPoint(exit,ay));link.Points.Add(new LedgerGraphPoint(exit,lane));
                    link.Points.Add(new LedgerGraphPoint(entry,lane));link.Points.Add(new LedgerGraphPoint(entry,by));
                    link.LabelAt=new LedgerGraphPoint((exit+entry)/2,lane-18);
                }
                link.Points.Add(new LedgerGraphPoint(bx,by));route++;
            }
            if(state.Overview)return result; // No batch-label collision work for name-only overview.
            var labels=new List<LedgerGraphBox>();
            foreach(var link in result.Connections)
            {
                float width=Math.Max(70,link.Label.Length*8+12),height=23;
                var origin=link.LabelAt;
                for(int attempt=0;attempt<512;attempt++)
                {
                    float y=origin.Y+(attempt==0?0:((attempt+1)/2)*(attempt%2==1?-26:26));
                    var box=new LedgerGraphBox(origin.X-width/2,y-2,width,height);
                    if(result.Items.Values.Concat(labels).Any(b=>box.X<b.Right+3&&box.Right>b.X-3&&box.Y<b.Bottom+3&&box.Bottom>b.Y-3))continue;
                    link.LabelAt=new LedgerGraphPoint(origin.X,y);labels.Add(box);break;
                }
            }
            return result;

            }
        }
        private static float ClearLane(IEnumerable<LedgerGraphBox> boxes,float x1,float x2,float y1,float y2,int route,float zoom)
        {
            var all=boxes.ToList();float pad=12*zoom,offset=(route%7)*8*zoom;
            var choices=all.SelectMany(b=>new[]{b.Y-pad-offset,b.Bottom+pad+offset}).Distinct()
                .OrderBy(y=>Math.Abs(y-y1)+Math.Abs(y-y2)).ThenBy(y=>y);
            foreach(float y in choices)
                if(!all.Any(b=>y>b.Y-pad/2&&y<b.Bottom+pad/2&&Math.Max(x1,x2)>b.X-pad/2&&Math.Min(x1,x2)<b.Right+pad/2))return y;
            return all.Min(b=>b.Y)-pad-offset;
        }
        // Cohen-Sutherland clipping: shared by the real rectangular-stroke renderer and non-game previews/tests.
        private static int Code(LedgerGraphPoint p,float width,float height)
        {return(p.X<0?1:p.X>width?2:0)|(p.Y<0?4:p.Y>height?8:0);}
        public static bool Clip(ref LedgerGraphPoint a,ref LedgerGraphPoint b,float width,float height)
        {
            if(width<=0||height<=0)return false;
            for(int i=0;i<8;i++)
            {
                int ac=Code(a,width,height),bc=Code(b,width,height);if((ac|bc)==0)return true;if((ac&bc)!=0)return false;
                int c=ac!=0?ac:bc;float x,y;
                if((c&8)!=0){if(b.Y==a.Y)return false;x=a.X+(b.X-a.X)*(height-a.Y)/(b.Y-a.Y);y=height;}
                else if((c&4)!=0){if(b.Y==a.Y)return false;x=a.X+(b.X-a.X)*(-a.Y)/(b.Y-a.Y);y=0;}
                else if((c&2)!=0){if(b.X==a.X)return false;y=a.Y+(b.Y-a.Y)*(width-a.X)/(b.X-a.X);x=width;}
                else{if(b.X==a.X)return false;y=a.Y+(b.Y-a.Y)*(-a.X)/(b.X-a.X);x=0;}
                if(c==ac)a=new LedgerGraphPoint(x,y);else b=new LedgerGraphPoint(x,y);
            }
            return false;
        }
        public static string Description(LedgerItemConnection link,LedgerBrowserCatalog catalog)
        {
            var r=catalog.Recipe(link.RecipeKey);if(r==null)return "";
            string text="Batch "+link.Batch+": "+string.Join(" + ",r.Inputs.Take(12).Select(i=>(i.Quantity.HasValue?i.Quantity.Value.ToString(CultureInfo.InvariantCulture):"?")+" "+catalog.Items[i.ItemKey].Name).ToArray())+(r.Inputs.Count>12?" + "+(r.Inputs.Count-12)+" more inputs":"")+" -> "+r.OutputCount+" "+catalog.Items[r.OutputKey].Name+" (all inputs together)";
            return text.Length>2000?text.Substring(0,1997)+"...":text;
        }
        public static LedgerGraphBox[] Controls(LedgerGraphBox card)
        {
            float size=Math.Max(24,26*card.Width/LedgerGraphNode.Width),h=Math.Max(24,26*card.Height/LedgerGraphNode.Height);
            return new[]{new LedgerGraphBox(card.X+3,card.Y+(card.Height-size)/2,size,size),
                new LedgerGraphBox(card.Right-size-3,card.Y+(card.Height-size)/2,size,size),
                new LedgerGraphBox(card.X+3,card.Bottom-h-3,card.Width-6,h)};
        }
        public static LedgerGraphBox Header(LedgerGraphBox card)
        {return new LedgerGraphBox(card.X+5,card.Y+4,card.Width-10,Math.Max(23,28*card.Height/LedgerGraphNode.Height));}
        public static LedgerGraphBox Icon(LedgerGraphBox card,float aspect)
        {
            if(float.IsNaN(aspect)||float.IsInfinity(aspect)||aspect<=0)aspect=1;
            var controls=Controls(card);var head=Header(card);
            float width=card.Width-2*(controls[0].Width+10),height=controls[2].Y-head.Bottom-8;
            float w=Math.Min(width,height*aspect),h=w/aspect;
            return new LedgerGraphBox(card.X+(card.Width-w)/2,head.Bottom+4+(height-h)/2,w,h);
        }
        // Caller measures plain visible text with its actual font. Ellipsis replaces overflow, never clips partial glyphs/tags.
        public static string Title(string plain,float width,float height,Func<string,float,float> measure)
        {
            if(measure(plain,width)<=height)return plain;
            int lo=0,hi=plain.Length;while(lo<hi){int mid=(lo+hi+1)/2;if(measure(plain.Substring(0,mid)+"...",width)<=height)lo=mid;else hi=mid-1;}
            return plain.Substring(0,lo).TrimEnd()+"...";
        }
    }
}
