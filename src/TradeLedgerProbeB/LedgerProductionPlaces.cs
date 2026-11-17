using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    public sealed class LedgerProductionPlace
    {
        public string Key,Label,Role;public int Depth;public LedgerProductionPlace Parent;
        public readonly HashSet<string> Playfields=new HashSet<string>(StringComparer.Ordinal);
        public readonly List<LedgerProductionPlace> Children=new List<LedgerProductionPlace>();
    }
    // Presentation of ALREADY recorded context only. Shared native sector coordinates prove a group;
    // a unique observed planet labels it, without inventing a moon's physical parent or unseen bodies.
    public sealed class LedgerProductionPlaces
    {
        private sealed class Body {public string Name,System,Coordinates,Sector,Kind,GroupName;public bool GroupConflict;}
        public readonly LedgerProductionPlace All=new LedgerProductionPlace{Key="all",Label="All places",Role="all",Depth=0};
        private readonly string world;
        private readonly Dictionary<string,LedgerProductionPlace> nodes=new Dictionary<string,LedgerProductionPlace>();
        private List<LedgerProductionPlace> visible;private HashSet<string> visibleCollapsed;
        public LedgerProductionPlaces(string world,IEnumerable<LedgerContext> contexts)
        {
            this.world=world;nodes.Add(All.Key,All);
            var bodies=contexts.Where(c=>c!=null&&c.WorldKey==world&&!string.IsNullOrWhiteSpace(c.Playfield)).GroupBy(c=>c.Playfield,StringComparer.Ordinal).Select(g=>new Body{
                Name=g.Key,GroupName=One(g.Select(c=>c.Geography==null?null:c.Geography.GroupName)),GroupConflict=g.Select(c=>c.Geography==null?null:c.Geography.GroupName).Where(n=>!string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).Take(2).Count()>1,System=One(g.Select(c=>c.SolarSystem)),Coordinates=One(g.Select(c=>c.Geography==null?null:Coordinate(c.Geography.SystemCoordinates))),
                Sector=One(g.Select(c=>c.Geography==null?null:Coordinate(c.Geography.SectorCoordinates))),Kind=One(g.Select(c=>c.Geography==null?null:c.Geography.BodyKind))}).ToList();
            foreach(var sys in bodies.GroupBy(b=>b.Coordinates!=null?"coordinates:"+b.Coordinates:"unresolved:"+(b.System??"unknown"),StringComparer.Ordinal)){
                string systemName=One(sys.Select(b=>b.System));var parent=Add(All,"system:"+sys.Key,(systemName??"System unknown")+(sys.First().Coordinates==null?" (position unknown)":""),"system");
                foreach(var sector in sys.GroupBy(b=>b.Coordinates!=null&&b.Sector!=null?b.Sector:"unknown:"+b.Name,StringComparer.Ordinal)){
                    bool known=sector.All(b=>b.Coordinates!=null&&b.Sector!=null);var group=parent;
                    if(known){var planets=sector.Where(b=>b.Kind=="Planet").ToList();string nativeName=sector.Any(b=>b.GroupConflict)?null:One(sector.Select(b=>b.GroupName));group=Add(parent,parent.Key+"|sector:"+sector.Key,planets.Count==1?planets[0].Name+" group":nativeName!=null?nativeName+" group":"Planet group unknown","group");}
                    foreach(var b in sector.OrderBy(b=>b.Kind=="Planet"?0:b.Kind=="Moon"?1:2).ThenBy(b=>b.Name,StringComparer.OrdinalIgnoreCase)){
                        string role=b.Kind=="Planet"?"planet":b.Kind=="Moon"?"moon":b.Kind=="Space"?"space":"type unknown";
                        var leaf=Add(group,"playfield:"+b.Name,b.Name+" ("+role+")",role);leaf.Playfields.Add(b.Name);group.Playfields.Add(b.Name);parent.Playfields.Add(b.Name);All.Playfields.Add(b.Name);
                    }
                }
            }
            Sort(All);
        }
        private static string One(IEnumerable<string> values){var a=values.Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).Take(2).ToArray();return a.Length==1?a[0]:null;}
        private static string Coordinate(string value){if(value==null)return null;var parts=value.Split(',');int x;if(parts.Length!=3||parts.Any(s=>!int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out x)))return null;return string.Join(",",parts.Select(s=>int.Parse(s,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)));}
        private LedgerProductionPlace Add(LedgerProductionPlace parent,string key,string label,string role)
        {var n=new LedgerProductionPlace{Key=key,Label=label,Role=role,Depth=parent.Depth+1,Parent=parent};parent.Children.Add(n);nodes.Add(key,n);return n;}
        private static void Sort(LedgerProductionPlace p){if(p.Role=="all"||p.Role=="system")p.Children.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.Label,b.Label));foreach(var c in p.Children)Sort(c);}
        public LedgerProductionPlace Resolve(string key){LedgerProductionPlace n;return key!=null&&nodes.TryGetValue(key,out n)?n:All;}
        public bool Contains(string key,LedgerObservation o){return o!=null&&o.Context!=null&&o.Context.WorldKey==world&&Resolve(key).Playfields.Contains(o.Context.Playfield);}
        public IEnumerable<string> Ancestry(string key)
        {var path=new List<string>();var place=Resolve(key);while(place!=null){path.Add(place.Key);place=place.Parent;}path.Reverse();return path;}
        public List<LedgerProductionPlace> Visible(HashSet<string> collapsed)
        {if(visible!=null&&visibleCollapsed.SetEquals(collapsed))return visible;var rows=new List<LedgerProductionPlace>();Visit(All,collapsed,rows);visibleCollapsed=new HashSet<string>(collapsed);return visible=rows;}
        private static void Visit(LedgerProductionPlace n,HashSet<string> collapsed,List<LedgerProductionPlace> rows){rows.Add(n);if(!collapsed.Contains(n.Key))foreach(var c in n.Children)Visit(c,collapsed,rows);}
    }
}
