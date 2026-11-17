using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TradeLedgerProbeB
{
    // Pure display transformations. Captured DTOs and SQLite verbatim bodies are never rewritten.
    public static class LedgerPresentation
    {
        private static readonly Regex Breaks = new Regex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        private static readonly Regex Paragraphs = new Regex(@"(?:</?p\s*>\s*)+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        private static readonly Regex Tags = new Regex(@"</?(?:color|b|i|u|s|size|p)(?:\s*=[^>]{0,128})?\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        private static readonly Regex Ngui = new Regex(@"\[(?:/?(?:c|b|i|u|s|sup|sub)|-|[0-9a-f]{6}|[0-9a-f]{8})\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        public static string Text(string source)
        {
            if (source == null) return "unknown";
            string text = source.Length > 16384 ? source.Substring(0,16384) : source;
            text = Breaks.Replace(text,"\n"); text = Paragraphs.Replace(text,"\n\n"); text = Tags.Replace(text,""); text = Ngui.Replace(text,"");
            return text.Replace("\r\n","\n").Replace("\r","\n").Replace("&nbsp;"," ").Replace("&lt;","<").Replace("&gt;",">").Replace("&quot;","\"").Replace("&amp;","&").Trim();
        }
        // Tooltips are plain GUI text, not an HTML surface. Decode before stripping, including
        // previously escaped rich text; leave mathematical angle/comparison characters intact.
        public static string TooltipText(string source)
        {
            string value=source??"";
            if(value.Length>16384)value=value.Substring(0,16384);
            for(int i=0;i<3;i++)value=Text(value);
            return value;
        }
        // Only a small allowlist is translated to Unity rich text; arbitrary captured tags never execute.
        public static string Rich(string source)
        {
            string plain=Text(source);
            return plain.Replace("<","&lt;").Replace(">","&gt;");
        }
        public static string ItemRich(string source)
        {
            return LedgerItemDisplay.NativeRich(source);
        }
        public static string RequestHeading(LedgerRequestRecord request)
        {
            Func<LedgerRequestedItem,string> item=i=>(i.Quantity.HasValue?i.Quantity.Value.ToString()+" ":"")+Text(i.Name);
            if(request.Fact.Alternatives.Count>0)return string.Join(" or ",request.Fact.Alternatives.Select(g=>"("+string.Join(", ",g.Inputs.Select(i=>item(request.Fact.Items[i])).ToArray())+")").ToArray());
            return string.Join(request.Fact.Relation=="any"?" or ":", ",request.Fact.Items.Select(item).ToArray());
        }
        public static string RequestBody(LedgerRequestRecord request)
        {
            string value=Rich(request.Source.Dialogue.BodyVerbatim);
            if(request.Fact.Conflict!=null)value="<color=#EDC685>Shown text differs from scenario checks; shown requirements retained.</color>\n"+value;
            // Longest literal first; no regex meaning or item/reward extraction is invented here.
            foreach(var i in request.Fact.Items.OrderByDescending(i=>i.Name.Length))
                value=value.Replace(Rich(i.Name),"<color=#76D8EF>"+Rich(i.Name)+"</color>");
            if(request.Fact.RewardText!=null)value=value.Replace(Rich(request.Fact.RewardText),"<color=#C6DC88>"+Rich(request.Fact.RewardText)+"</color>");
            return Regex.Replace(value,@"(?<![\d,])[\d,]{1,14}\s+credits\b",m=>"<color=#C6DC88>"+m.Value+"</color>",RegexOptions.IgnoreCase);
        }
        public static string Seen(string utc)
        { DateTime t; return DateTime.TryParse(utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out t) ? t.ToUniversalTime().ToString("dd MMM HH:mm 'UTC'",CultureInfo.InvariantCulture) : "time unknown"; }
        // Same-session sequence is authoritative for new captures, including a wall-clock adjustment.
        // Legacy data uses actual UTC, then same-session ticks/sample. Page/content hash are never clocks.
        public static int CompareDialogue(LedgerObservation a, LedgerObservation b)
        {
            bool session = !string.IsNullOrEmpty(a.Session) && a.Session == b.Session;
            if (session && a.Sequence > 0 && b.Sequence > 0) return a.Sequence.CompareTo(b.Sequence);
            DateTime at,bt;
            if (!DateTime.TryParse(a.Utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out at) ||
                !DateTime.TryParse(b.Utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out bt)) return 0;
            int time = at.CompareTo(bt); if(time!=0) return time;
            long ax,bx;
            if (session && long.TryParse(a.GameTicks,out ax) && long.TryParse(b.GameTicks,out bx) && ax!=bx) return ax.CompareTo(bx);
            if (session && !string.IsNullOrEmpty(a.Interaction) && a.Interaction==b.Interaction && a.Dialogue!=null && b.Dialogue!=null)
                return a.Dialogue.Sample.CompareTo(b.Dialogue.Sample);
            return 0;
        }
    }
    public sealed class LedgerTreeEntry { public string Category; public LedgerViewItem Item; public bool Expanded,PinnedGroup; }
    public static class LedgerUi
    {
        public static System.Collections.Generic.List<LedgerTreeEntry> Tree(System.Collections.Generic.IEnumerable<LedgerViewItem> items,System.Collections.Generic.ISet<string> collapsed,string search)
        {
            var result=new System.Collections.Generic.List<LedgerTreeEntry>();
            foreach(var group in items.GroupBy(i=>i.Category).OrderBy(g=>g.Key,StringComparer.OrdinalIgnoreCase))
            {
                bool expanded=!collapsed.Contains(group.Key)||!string.IsNullOrEmpty(search);
                result.Add(new LedgerTreeEntry{Category=group.Key,Expanded=expanded});
                if(expanded)result.AddRange(group.Select(i=>new LedgerTreeEntry{Category=group.Key,Item=i}));
            }
            return result;
        }
        public static void Swap(LedgerScopePicker a,LedgerScopePicker b)
        {var kind=a.Kind;var key=a.Key;var context=a.Context;a.Kind=b.Kind;a.Key=b.Key;a.Context=b.Context;b.Kind=kind;b.Key=key;b.Context=context;}
        public static string ScopeLabel(LedgerScope scope)
        {return scope==null?"unknown":Regex.Replace(scope.Label,@"\s*\[(?:NPC\s+)?\d+\]","");}
    }
    public sealed class LedgerLayout
    {
        public float Scale, Width, Height;
        public float ContentHeight { get { return Math.Max(260,Height-112); } }
        public float LeftWidth { get { return Math.Max(270,Math.Min(360,Width*.21f)); } }
        public float RightWidth { get { return Width-LeftWidth-48; } }
        public float ComparisonViewport(bool expanded){return Math.Max(170,ContentHeight-180-(expanded?120:0));}
        public const int BodyFont=16, SmallFont=15, HeadingFont=20, ControlHeight=28, Scrollbar=12;
        public const int TableRow=30, PairRow=46, TreeRow=26;
        public static int ScrollContentHeight(int count,int rowHeight){return Math.Max(34,count*rowHeight);}
        public static int FirstRow(float scroll,int count,int rowHeight)
        {return Math.Max(0,Math.Min(Math.Max(0,count-1),(int)Math.Floor(Math.Max(0,scroll)/rowHeight)));}
        public static int EndRow(float scroll,float viewport,int count,int rowHeight)
        {return Math.Min(count,FirstRow(scroll,count,rowHeight)+(int)Math.Ceiling(viewport/rowHeight)+2);}
        public static LedgerLayout For(int width,int height)
        {
            float scale = Math.Max(.75f,Math.Min(width/1920f,height/1080f));
            return new LedgerLayout {Scale=scale,Width=width/scale,Height=height/scale};
        }
    }
}
