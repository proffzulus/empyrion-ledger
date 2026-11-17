using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TradeLedgerProbeB
{
    public sealed class LedgerGeography
    {
        public string BodyKind, SystemCoordinates, SectorCoordinates, GroupName, Source;
        public static string Classify(bool isSpace,int type,int mapType)
        {
            if(isSpace)return type==1||type==4?"Unknown":"Space";
            if(type==2)return "Space";
            if(type==4||mapType==7)return "Moon";
            if(type==1||mapType==1)return "Planet";
            return null;
        }
    }
    // Detached observed-context metadata only. No entity discovery, native collection or DB access.
    public sealed class LedgerGeographyProjection
    {
        public const int Budget=8,PollMilliseconds=1000,RetryMilliseconds=5000;
        private LedgerViewData prepared;private string scope;private LedgerContext[][] groups=new LedgerContext[0][];
        private int cursor;private long next;
        private readonly Dictionary<string,long> retry=new Dictionary<string,long>(StringComparer.Ordinal);
        public static IEnumerable<LedgerContext> Contexts(LedgerViewData d)
        {
            return d.Markets.Concat(d.ObservedRequests.Select(r=>r.Source)).Concat(d.Requests).Concat(d.DialoguePages)
                .Concat(d.Production.Where(o=>o.Production!=null&&o.Production.Player==d.ManualPlayerId))
                .Concat(d.DiscoveredObjects.Where(r=>r.Latest!=null&&r.Latest.DiscoveredObject!=null&&r.Latest.DiscoveredObject.Player==d.ManualPlayerId).Select(r=>r.Latest))
                .Where(o=>o!=null&&o.Context!=null&&o.Context.Recordable&&o.Context.Mode=="SinglePlayer"&&o.Context.WorldKey==d.WorldKey&&(o.ObserverPlayer==0||o.ObserverPlayer==d.ManualPlayerId)).Select(o=>o.Context).Distinct();
        }
        private static bool Different(string a,string b){return !string.IsNullOrWhiteSpace(a)&&!string.IsNullOrWhiteSpace(b)&&a!=b;}
        private static bool Incomplete(LedgerContext c){return c.Geography==null||string.IsNullOrWhiteSpace(c.Geography.GroupName)||c.Geography.BodyKind==null||c.Geography.SystemCoordinates==null||c.Geography.SectorCoordinates==null;}
        public static bool Merge(LedgerContext c,LedgerGeography n)
        {
            if(c==null||n==null||string.IsNullOrWhiteSpace(n.SystemCoordinates)||string.IsNullOrWhiteSpace(n.SectorCoordinates))return false;
            var old=c.Geography;
            if(old!=null&&(Different(old.SystemCoordinates,n.SystemCoordinates)||Different(old.SectorCoordinates,n.SectorCoordinates)||Different(old.BodyKind,n.BodyKind)))return false;
            string body=old==null?n.BodyKind:old.BodyKind??n.BodyKind,sys=old==null?n.SystemCoordinates:old.SystemCoordinates??n.SystemCoordinates,
                sector=old==null?n.SectorCoordinates:old.SectorCoordinates??n.SectorCoordinates,name=old==null||string.IsNullOrWhiteSpace(old.GroupName)?n.GroupName:old.GroupName;
            if(old!=null&&old.BodyKind==body&&old.SystemCoordinates==sys&&old.SectorCoordinates==sector&&old.GroupName==name)return false;
            c.Geography=new LedgerGeography{BodyKind=body,SystemCoordinates=sys,SectorCoordinates=sector,GroupName=name,Source=(old==null?"":old.Source+";")+"observed-group-label-projection:"+n.Source};return true;
        }
        public static bool FillStored(LedgerViewData d)
        {
            bool changed=false;
            var records=Contexts(d).Where(c=>c.Geography!=null&&!string.IsNullOrWhiteSpace(c.Geography.SystemCoordinates)&&!string.IsNullOrWhiteSpace(c.Geography.SectorCoordinates));
            foreach(var g in records.GroupBy(c=>c.Geography.SystemCoordinates+"|"+c.Geography.SectorCoordinates)){
                var names=g.Select(c=>c.Geography.GroupName).Where(n=>!string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).Take(2).ToArray();if(names.Length!=1)continue;
                foreach(var c in g){var geo=c.Geography;changed|=Merge(c,new LedgerGeography{BodyKind=geo.BodyKind,SystemCoordinates=geo.SystemCoordinates,SectorCoordinates=geo.SectorCoordinates,GroupName=names[0],Source="same-world/player observed native system+sector"});}
            }
            return changed;
        }
        public bool Refresh(LedgerViewData d,LedgerContext current,long now,Func<LedgerContext,LedgerGeography> read)
        {
            if(d==null||current==null||!current.Recordable||current.Mode!="SinglePlayer"||d.WorldKey!=current.WorldKey||!(d.ManualPlayerId>0)||read==null)return false;
            string identity=current.LocalSaveKey+"|"+current.WorldKey+"|"+d.ManualPlayerId;
            if(scope!=identity){scope=identity;prepared=null;retry.Clear();cursor=0;next=0;}
            bool changed=false;
            if(!ReferenceEquals(prepared,d)){prepared=d;changed=FillStored(d);groups=Contexts(d).Where(c=>!string.IsNullOrWhiteSpace(c.Playfield)).GroupBy(c=>c.Playfield,StringComparer.Ordinal).Select(g=>g.ToArray()).Where(g=>g.Any(Incomplete)).ToArray();if(groups.Length>0)cursor%=groups.Length;}
            if(now<next)return changed;next=now+PollMilliseconds;
            int visited=0,reads=0;
            while(visited++<groups.Length&&reads<Budget){var group=groups[cursor++%groups.Length];if(cursor==int.MaxValue)cursor=0;
                if(!group.Any(Incomplete))continue;
                string pf=group[0].Playfield;long due;if(retry.TryGetValue(pf,out due)&&now<due)continue;retry[pf]=now+RetryMilliseconds;reads++;
                // Old portable observations can carry an old physical path. Resolve only their
                // exact known playfield inside the independently admitted current physical save.
                var target=LedgerContext.Create(current.SavePath,pf,group[0].SolarSystem,current.Mode);target.WorldKey=current.WorldKey;
                LedgerGeography native=null;try{native=read(target);}catch(Exception){/* Optional metadata can vanish during a transition; retry without failing the journal. */}
                foreach(var c in group)changed|=Merge(c,native);
            }
            if(changed){FillStored(d);groups=groups.Where(g=>g.Any(Incomplete)).ToArray();cursor=0;}return changed;
        }
    }
    public enum LedgerScopeKind { Poi, Body, Sector, System }
    public sealed class LedgerScope
    {
        public LedgerScopeKind Kind;
        public string Key,Label;
        // Detached navigator scope. Exact observed playfields only; never a name/proximity join.
        public string World;public string[] Playfields;public bool AllObserved;
    }
    public sealed class LedgerScopePicker
    {
        public LedgerScopeKind Kind;
        public string Key;
        public bool Expanded;
        public LedgerScope Context;
        public LedgerScopePicker(LedgerScopeKind kind){Kind=kind;}
        public LedgerScope Resolve(LedgerLocationIndex index)
        {
            if(Context!=null&&Context.Kind==Kind&&Context.Key==Key)return Context;
            var values=index.Scopes(Kind);
            if(Key==null&&values.Count>0)Key=values[0].Key;
            return values.FirstOrDefault(s=>s.Key==Key);
        }
        public void Change(LedgerScopeKind kind){if(Kind!=kind){Kind=kind;Key=null;Context=null;Expanded=false;}}
    }
    // Only header metadata of an exact already-observed playfield, never Sectors/POI/config enumeration.
    public static class LedgerGeographyHeader
    {
        public static string Parse(string text)
        {
            if(text==null||text.Length>4096)return null;
            foreach(string line in text.TrimStart('\ufeff').Split('\n'))
            {
                if(!line.StartsWith("PlayfieldType:",StringComparison.Ordinal))continue;
                string value=line.Substring(14).Split('#')[0].Trim().Trim('"','\'');
                return value=="Planet"||value=="Moon"||value=="Space"?value:null;
            }
            return null;
        }
        public static LedgerGeography ReadKnown(LedgerContext c)
        {
            if(c==null||!c.Recordable||c.Mode!="SinglePlayer"||string.IsNullOrEmpty(c.Playfield)||c.Playfield.Length>160||
                c.Playfield.IndexOfAny(Path.GetInvalidFileNameChars())>=0||c.Playfield=="."||c.Playfield==".."||c.Playfield.Contains("#"))return null;
            try
            {
                if(!LedgerContext.MatchesCurrentSave(c,c.SavePath))return null;
                string templates=Path.Combine(c.SavePath,"Templates"),folder=Path.Combine(templates,c.Playfield),file=Path.Combine(folder,"playfield.yaml");
                foreach(string p in new[]{c.SavePath,templates,folder,file})if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)return null;
                var bytes=new byte[4096];int count;
                using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))count=stream.Read(bytes,0,bytes.Length);
                string kind=Parse(Encoding.UTF8.GetString(bytes,0,count));
                return kind==null?null:new LedgerGeography{BodyKind=kind,Source="exact-observed-PF Templates/<PF>/playfield.yaml header PlayfieldType;no-sector-inference"};
            }
            catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}catch(ArgumentException){return null;}catch(System.Security.SecurityException){return null;}
        }
    }
    public sealed class LedgerLocationIndex
    {
        private readonly Dictionary<string,LedgerScope> scopes=new Dictionary<string,LedgerScope>();
        private readonly Dictionary<string,Dictionary<LedgerScopeKind,string>> memberships=new Dictionary<string,Dictionary<LedgerScopeKind,string>>();
        private readonly LedgerViewData data;
        private readonly Dictionary<LedgerScopeKind,List<LedgerScope>> sortedScopes=new Dictionary<LedgerScopeKind,List<LedgerScope>>();
        public LedgerLocationIndex(LedgerViewData value)
        {
            data=value;
            var records=value.Markets.Concat(value.RequestCards().Select(r=>r.Source)).Where(o=>o.Context!=null&&o.Identity!=null&&o.Identity.NpcEntityId>0&&o.Context.WorldKey==value.WorldKey).ToList();
            var systemCoordinates=records.Where(o=>!string.IsNullOrEmpty(o.Context.SolarSystem)&&o.Context.Geography!=null&&!string.IsNullOrEmpty(o.Context.Geography.SystemCoordinates))
                .GroupBy(o=>o.Context.SolarSystem).ToDictionary(g=>g.Key,g=>g.Select(o=>o.Context.Geography.SystemCoordinates).Distinct().ToList());
            var sectorLabels=records.Where(o=>o.Context.Geography!=null).GroupBy(o=>o.Context.WorldKey+"|"+o.Context.Geography.SystemCoordinates+"|"+o.Context.Geography.SectorCoordinates)
                .ToDictionary(g=>g.Key,g=>{var names=g.Select(o=>o.Context.Geography.GroupName).Where(n=>!string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).Take(2).ToArray();return names.Length==1?LedgerPresentation.Text(names[0]):"Sector unknown";});
            foreach(var group in records.GroupBy(LedgerDatabase.Key))
            {
                // Market context is preferred to older dialogue context; no merging across scoped endpoints.
                var o=group.First();var c=o.Context;var id=value.ScopeIdentity(o);
                var links=new Dictionary<LedgerScopeKind,string>();memberships.Add(group.Key,links);
                string pf=c.WorldKey+"|pf:"+c.Playfield;
                string poi=id!=null&&id.StructureEntityId>0?pf+"|structure:"+id.StructureEntityId:pf+"|unresolved-npc:"+o.Identity.NpcEntityId;
                string name=id==null?null:id.StructureName??id.CatalogDisplayName;
                Add(links,LedgerScopeKind.Poi,poi,(string.IsNullOrEmpty(name)?"Unknown POI":LedgerPresentation.Text(name))+" · "+c.Playfield+" ["+(id!=null&&id.StructureEntityId>0?id.StructureEntityId.ToString():"NPC "+o.Identity.NpcEntityId)+"]");
                var geo=c.Geography;
                if(geo!=null&&(geo.BodyKind=="Planet"||geo.BodyKind=="Moon"))Add(links,LedgerScopeKind.Body,pf+"|body",c.Playfield+" ("+geo.BodyKind.ToLowerInvariant()+")");
                if(string.IsNullOrWhiteSpace(c.SolarSystem))continue;
                string sys=c.WorldKey+"|system-name:"+c.SolarSystem;
                List<string> coords;
                if(systemCoordinates.TryGetValue(c.SolarSystem,out coords)&&coords.Count>1)
                {
                    if(geo==null||string.IsNullOrEmpty(geo.SystemCoordinates))continue;
                    sys+="|coordinates:"+geo.SystemCoordinates;
                }
                Add(links,LedgerScopeKind.System,sys,c.SolarSystem+(coords!=null&&coords.Count>1?" ["+geo.SystemCoordinates+"]":""));
                if(geo!=null&&!string.IsNullOrEmpty(geo.SystemCoordinates)&&!string.IsNullOrEmpty(geo.SectorCoordinates))
                    Add(links,LedgerScopeKind.Sector,sys+"|sector:"+geo.SectorCoordinates,sectorLabels[c.WorldKey+"|"+geo.SystemCoordinates+"|"+geo.SectorCoordinates]);
            }
            foreach(var r in value.Objects.Places){if(r.IdentityConflict)continue;string key=LedgerPlaceBrowserLocationKey(r);
                if(!scopes.ContainsKey(key))scopes.Add(key,new LedgerScope{Kind=LedgerScopeKind.Poi,Key=key,Label=value.Objects.Title(r)+" | "+r.Latest.Context.Playfield+" ["+r.Latest.DiscoveredObject.NativeId+"]"});}
        }
        private static string LedgerPlaceBrowserLocationKey(LedgerObjectRecord r){return r.Latest.Context.WorldKey+"|pf:"+r.Latest.Context.Playfield+"|structure:"+r.Latest.DiscoveredObject.NativeId;}
        private void Add(Dictionary<LedgerScopeKind,string> links,LedgerScopeKind kind,string key,string label)
        {if(!scopes.ContainsKey(key))scopes.Add(key,new LedgerScope{Kind=kind,Key=key,Label=label});links[kind]=key;}
        public List<LedgerScope> Scopes(LedgerScopeKind kind){List<LedgerScope> result;if(!sortedScopes.TryGetValue(kind,out result))sortedScopes.Add(kind,result=scopes.Values.Where(s=>s.Kind==kind).OrderBy(s=>s.Label,StringComparer.OrdinalIgnoreCase).ThenBy(s=>s.Key).ToList());return result;}
        public bool Contains(LedgerScope scope,LedgerObservation o)
        {Dictionary<LedgerScopeKind,string> links;string key;if(scope==null||o==null||o.Context==null||o.Context.WorldKey!=data.WorldKey)return false;
            if(scope.World!=null)return scope.World==data.WorldKey&&(scope.AllObserved||scope.Playfields!=null&&Array.IndexOf(scope.Playfields,o.Context.Playfield)>=0);
            return o.Identity!=null&&memberships.TryGetValue(LedgerDatabase.Key(o),out links)&&links.TryGetValue(scope.Kind,out key)&&key==scope.Key;}
        public string Unavailable(LedgerScopeKind kind)
        {return kind==LedgerScopeKind.Body?"No observed body type metadata":kind==LedgerScopeKind.Sector?"No observed native sector coordinates":kind==LedgerScopeKind.System?"No observed star-system identity":"No observed POI identity";}
    }
    public sealed partial class LedgerViewData
    {
        public LedgerIdentity ScopeIdentity(LedgerObservation o)
        {
            if(o==null||o.Context==null||o.Context.WorldKey!=WorldKey||o.Identity==null||o.Identity.OwnerConsistent==false)return null;
            var a=o.Identity;var discovered=DiscoveredOwner(o);
            if(discovered!=null&&string.IsNullOrEmpty(a.StructureName??a.CatalogDisplayName)&&Objects.Title(discovered)!="Unknown place")
                return new LedgerIdentity{NpcEntityId=a.NpcEntityId,StructureEntityId=a.StructureEntityId,StructureName=discovered.Latest.DiscoveredObject.DisplayName,
                    CatalogDisplayName=discovered.Latest.DiscoveredObject.DisplayName==null?Objects.Title(discovered):null,CatalogSource="exact observed native prefab type;not an observed instance name"};
            LedgerEndpointState known;
            if(!endpoints.TryGetValue(LedgerDatabase.Key(o),out known)||known.Identity==null)return a;
            var b=known.Identity;
            if(b.OwnerConsistent==false||a.StructureEntityId.HasValue&&b.StructureEntityId.HasValue&&a.StructureEntityId!=b.StructureEntityId||a.BlockGrid!=null&&b.BlockGrid!=null&&a.BlockGrid!=b.BlockGrid)return null;
            return new LedgerIdentity{NpcEntityId=a.NpcEntityId,StructureEntityId=a.StructureEntityId??b.StructureEntityId,
                StructureName=a.StructureName??b.StructureName,CatalogDisplayName=a.CatalogDisplayName??b.CatalogDisplayName};
        }
        public List<LedgerViewItem> ScopedItems(LedgerLocationIndex index,LedgerScope scope,string search,string category)
        {
            var items=Browse(search,category);if(scope==null)return items;if(index==null)return new List<LedgerViewItem>();
            var keys=new HashSet<string>(Markets.Where(m=>index.Contains(scope,m)).SelectMany(m=>m.Offers).Where(o=>o.BindingState=="bound-current-delegate"&&o.ItemId.HasValue)
                .Select(o=>o.ItemId+":"+(o.AuxiliaryRaw.HasValue?o.AuxiliaryRaw.Value.ToString():"unknown")));
            return items.Where(i=>keys.Contains(i.Key)).ToList();
        }
        public List<LedgerViewOffer> LocatedOffers(LedgerLocationIndex index,LedgerScope scope,bool sellers,string search)
        {
            return Markets.Where(m=>index.Contains(scope,m)).SelectMany(m=>m.Offers.Where(o=>o.BindingState=="bound-current-delegate"&&o.ItemId.HasValue&&(sellers?o.NpcSellsSupported:o.NpcBuysSupported)!=false)
                .Select(o=>new LedgerViewOffer{Market=m,Offer=o,Sellers=sellers})).Where(r=>string.IsNullOrEmpty(search)||
                    (LedgerPresentation.Text(r.Offer.Visible==null?null:r.Offer.Visible.Name)+" "+r.Offer.ItemId).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)
                .OrderBy(r=>r.Offer.Visible==null?"":r.Offer.Visible.Name,StringComparer.OrdinalIgnoreCase).ThenBy(r=>r.Market.Identity.NpcEntityId).ToList();
        }
    }
}
