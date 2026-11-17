using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeLedgerProbeB
{
    public enum LedgerPlaceTraderFilter { Any, WithTraders, WithoutTraders, Unknown }
    public static class LedgerPlaceContext
    {
        public static LedgerObjectOrganization HideTarget(LedgerObjectOrganization list){return list==LedgerObjectOrganization.Hidden?LedgerObjectOrganization.Unsorted:LedgerObjectOrganization.Hidden;}
        public static LedgerObjectOrganization FavoriteTarget(LedgerObjectOrganization list){return list==LedgerObjectOrganization.Favorites?LedgerObjectOrganization.Unsorted:LedgerObjectOrganization.Favorites;}
        public static LedgerScope Scope(LedgerPlaceBrowser browser,string key,LedgerPlaceRow selected=null)
        {
            if(selected!=null)return selected.Record.IdentityConflict?null:new LedgerScope{Kind=LedgerScopeKind.Poi,Key=selected.LocationKey,Label=selected.Name};
            var node=browser.Index.Hierarchy.Resolve(key);if(node.Key!=key)return null;
            return new LedgerScope{Kind=node.Role=="system"||node.Role=="all"?LedgerScopeKind.System:node.Role=="group"?LedgerScopeKind.Sector:LedgerScopeKind.Body,
                Key="places:"+key,Label=node.Label,World=browser.Index.WorldKey,Playfields=node.Playfields.OrderBy(p=>p,StringComparer.Ordinal).ToArray(),AllObserved=key=="all"};
        }
    }
    public sealed class LedgerPlaceRow
    {
        public LedgerObjectRecord Record;public string Name,Type,Faction,SearchText,AliasHint,VariantHint,FactionLabel;
        public readonly List<string> KnownTraders=new List<string>(),KnownRequests=new List<string>();
        public int Markets,Pending,Done,Conversations;public bool RequestStateKnown;
        public LedgerPlaceTemplate TemplatePotential;
        public LedgerFactionPresentation FactionInfo;internal LedgerFactionPresentation BaseFactionInfo;
        public int? PotentialTraders {get{return TemplatePotential==null?null:TemplatePotential.PotentialTraders;}}
        public string PotentialSummary {get{return PotentialTraders.HasValue?"Up to "+PotentialTraders.Value+" traders":"Potential traders unknown";}}
        public bool MatchesTraders(LedgerPlaceTraderFilter filter){return filter==LedgerPlaceTraderFilter.Any||filter==LedgerPlaceTraderFilter.WithTraders&&PotentialTraders>0||filter==LedgerPlaceTraderFilter.WithoutTraders&&PotentialTraders==0||filter==LedgerPlaceTraderFilter.Unknown&&!PotentialTraders.HasValue;}
        public string LocationKey {get{return Record.Latest.Context.WorldKey+"|pf:"+Record.Latest.Context.Playfield+"|structure:"+Record.Latest.DiscoveredObject.NativeId;}}
        public string RequestKey {get{return Record.Latest.Context.WorldKey+"|"+Record.Latest.Context.Playfield+"|structure:"+Record.Latest.DiscoveredObject.NativeId;}}
        public string MarketSummary {get{return Markets==0?"No market observations":Markets+" observed trader"+(Markets==1?"":"s");}}
        public string RequestSummary {get{return !RequestStateKnown?"Request state unavailable":Pending+Done+Conversations==0?"No request observations":Pending+" pending / "+Done+" done"+(Conversations>0?" / "+Conversations+" conversations":"");}}
    }
    // Built on the reader worker. No native object, IO, catalogue parsing or full-store
    // evidence joins are performed by a browser row draw or a discovery callback.
    public sealed class LedgerPlaceBrowser
    {
        public readonly LedgerObjectIndex Index;public readonly List<LedgerPlaceRow> Rows;public readonly string[] Types,Factions;public readonly int[] FactionGroups;
        private readonly Dictionary<string,LedgerPlaceRow> byKey=new Dictionary<string,LedgerPlaceRow>();
        private readonly LedgerDistanceIndex distances;
        public LedgerPlaceRow Find(string key){LedgerPlaceRow row;return key!=null&&byKey.TryGetValue(key,out row)?row:null;}
        public LedgerPlaceBrowser(LedgerViewData data)
        {
            Index=data.Objects;Rows=new List<LedgerPlaceRow>();
            distances=new LedgerDistanceIndex(data.WorldKey,Index.Places.Select(r=>r.Latest.Context));
            foreach(var r in Index.Places){var f=r.Latest.DiscoveredObject;var t=Index.TypeFor(r);var aliases=t==null?new string[0]:t.Aliases??new string[0];
                var naming=r.IdentityConflict?new LedgerPlaceName{Display=LedgerPresentation.Text(Index.Title(r)),Type="Type unknown",Hint="Variant unavailable: conflicting instance identity."}:LedgerPlaceNames.Current.Resolve(Index.Title(r),f.NativeName,f.Template,f.ContentIdentity);
                string type=naming.Certified?naming.Type:t==null?(f.Category??"Type unknown"):t.DisplayName;
                string faction=Faction(f);var row=new LedgerPlaceRow{Record=r,Name=naming.Display,Type=type,Faction=faction,FactionLabel=FactionDisplay(f),VariantHint=naming.Hint,AliasHint=aliases.Length==0?null:"Type aliases: "+string.Join(", ",aliases),RequestStateKnown=data.ManualStateReady};
                row.TemplatePotential=r.IdentityConflict?null:LedgerPlaceNames.Current.PotentialFor(f.Template,f.ContentIdentity);
                row.FactionInfo=row.BaseFactionInfo=LedgerPlaceFactions.Current.Resolve(f,r.IdentityConflict);row.FactionLabel=row.FactionInfo.Name;
                row.SearchText=row.Name+" "+type+" "+(t==null?"":t.VariantLabel)+" "+f.Template+" "+f.Group+" "+faction+" "+row.FactionLabel+" "+row.FactionInfo.InternalName+" "+r.Latest.Context.Playfield+" "+string.Join(" ",r.PriorNames??new List<string>())+" "+string.Join(" ",aliases);Rows.Add(row);byKey.Add(r.Key,row);
            }
            Rows.Sort((a,b)=>{int n=StringComparer.OrdinalIgnoreCase.Compare(a.Name,b.Name);if(n==0)n=StringComparer.OrdinalIgnoreCase.Compare(a.Record.Latest.Context.Playfield,b.Record.Latest.Context.Playfield);return n==0?StringComparer.Ordinal.Compare(a.Record.Key,b.Record.Key):n;});
            Types=new[]{"All types"}.Concat(Rows.Select(r=>r.Type).Distinct().OrderBy(s=>s,StringComparer.OrdinalIgnoreCase)).ToArray();
            Factions=new[]{"All factions"}.Concat(Rows.Select(r=>r.FactionLabel).Distinct().OrderBy(s=>s,StringComparer.OrdinalIgnoreCase)).ToArray();
            FactionGroups=Rows.Where(r=>r.FactionInfo.InternalName!=null).Select(r=>r.Record.Latest.DiscoveredObject.FactionGroup.Value).Distinct().Take(128).ToArray();
            foreach(var o in data.Markets){var owner=data.DiscoveredOwner(o);LedgerPlaceRow r;if(owner!=null&&byKey.TryGetValue(owner.Key,out r)){r.Markets++;r.KnownTraders.Add(data.VendorFor(o));}}
            foreach(var card in data.RequestCards()){var owner=data.DiscoveredOwner(card.Source);LedgerPlaceRow r;if(owner==null||!byKey.TryGetValue(owner.Key,out r))continue;if(card.Request==null)r.Conversations++;else if(data.ManualStateReady){bool done=data.ManuallyCompleted(card);if(done)r.Done++;else r.Pending++;r.KnownRequests.Add((done?"Done: ":"Pending: ")+data.VendorFor(card.Source)+" - "+LedgerBookmarkPolicy.DeliveryLabel(card.Request.Fact,data.Catalog));}}
        }
        public static string FactionDisplay(LedgerObjectFact f){return LedgerPlaceFactions.Current.Resolve(f).Name;}
        public double? Distance(LedgerPlaceRow r,LedgerPlayerPoint player)
        {
            if(r==null||r.Record.IdentityConflict||player==null)return null;var o=r.Record.Latest;var f=o.DiscoveredObject;
            if(player.World!=o.Context.WorldKey)return null;
            if(player.Playfield!=o.Context.Playfield){var d=distances.Resolve(player,o.Context);return d==null?null:(double?)(d.Value*(d.Unit=="ly"?10000:1)*150000000);}
            if(!LedgerBrowse.SamePositionScope(player,o.Context)||!f.X.HasValue||!f.Y.HasValue||!f.Z.HasValue||f.PositionSource==null||!(f.PositionSource.Contains("1B3A")||f.PositionSource.Contains("1E77")))return null;
            double x=player.X-f.X.Value,y=player.Y-f.Y.Value,z=player.Z-f.Z.Value;
            double value=Math.Sqrt(x*x+y*y+z*z);return double.IsNaN(value)||double.IsInfinity(value)?null:(double?)value;
        }
        public string DistanceText(LedgerPlaceRow r,LedgerPlayerPoint player)
        {
            if(r==null||r.Record.IdentityConflict)return "unknown";
            if(player!=null&&player.Playfield!=r.Record.Latest.Context.Playfield){var d=distances.Resolve(player,r.Record.Latest.Context);return d==null?"unknown":d.Text;}
            var n=Distance(r,player);return !n.HasValue?"unknown":n<1000?n.Value.ToString("0",System.Globalization.CultureInfo.InvariantCulture)+" m":(n.Value/1000).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" km";
        }
        public List<LedgerPlaceRow> Sort(IEnumerable<LedgerPlaceRow> rows,LedgerHeaderSort sort,LedgerPlayerPoint player)
        {
            if(sort.Column<0)return rows.ToList();
            if(sort.Column==0)return (sort.Descending?rows.OrderByDescending(r=>r.Name,StringComparer.OrdinalIgnoreCase):rows.OrderBy(r=>r.Name,StringComparer.OrdinalIgnoreCase)).ThenBy(r=>r.Record.Key,StringComparer.Ordinal).ToList();
            var values=rows.Select(r=>new{Row=r,Value=sort.Column==1?Distance(r,player):(double?)r.PotentialTraders}).ToList();
            var known=values.OrderBy(r=>r.Value.HasValue?0:1);return (sort.Descending?known.ThenByDescending(r=>r.Value):known.ThenBy(r=>r.Value)).ThenBy(r=>r.Row.Record.Key,StringComparer.Ordinal).Select(r=>r.Row).ToList();
        }
        public bool ApplyFactions(IDictionary<int,LedgerFactionLive> live){bool changed=false;var projected=new Dictionary<int,LedgerFactionPresentation>();
            foreach(var r in Rows){var basis=r.BaseFactionInfo;LedgerFactionPresentation original;
                if(basis.InternalName==null)continue;
                int group=r.Record.Latest.DiscoveredObject.FactionGroup.Value;
                if(!projected.TryGetValue(group,out original)){
                    original=new LedgerFactionPresentation{Name=basis.Name,Color=basis.Color,InternalName=basis.InternalName,Source=basis.Source};LedgerFactionLive value;
                    if(live.TryGetValue(group,out value)&&value.InternalName==original.InternalName){original.Level(value.Level,value.Source);if(value.Color!=null&&System.Text.RegularExpressions.Regex.IsMatch(value.Color,@"\A[0-9A-F]{6}\z"))original.Color=value.Color;}
                    projected.Add(group,original);
                }
                var old=r.FactionInfo;if(old.Color!=original.Color||old.Relation!=original.Relation||old.Standing!=original.Standing||old.RelationSource!=original.RelationSource)changed=true;r.FactionInfo=original;
            }return changed;
        }
        public static string Faction(LedgerObjectFact f)
        {
            if(!f.FactionGroup.HasValue)return "Faction unknown";
            string group=f.FactionGroup.Value>=0&&f.FactionGroup.Value<=255&&Enum.IsDefined(typeof(FactionGroup),(byte)f.FactionGroup.Value)?((FactionGroup)f.FactionGroup.Value).ToString():"Group "+f.FactionGroup.Value;
            return group+" ("+f.FactionId+")";
        }
        public List<LedgerPlaceRow> Query(LedgerObjectOrganization list,string place="all",string search="",string type=null,string faction=null,LedgerPlaceTraderFilter traders=LedgerPlaceTraderFilter.Any,LedgerPlaceRelation? relation=null)
        {return Rows.Where(r=>r.Record.Organization==list&&(!relation.HasValue||r.FactionInfo.Relation==relation.Value)&&r.MatchesTraders(traders)&&Index.Hierarchy.Contains(place,r.Record.Latest)&&(type==null||r.Type==type)&&(faction==null||r.FactionLabel==faction||r.FactionInfo.InternalName==faction||r.Faction==faction)&&(string.IsNullOrWhiteSpace(search)||r.SearchText.IndexOf(search.Trim(),StringComparison.OrdinalIgnoreCase)>=0)).ToList();}
        public static bool Current(LedgerViewData data,string world,int? player)
        {return data!=null&&player>0&&data.WorldKey==world&&data.ManualPlayerId==player;}
        public static LedgerRequestFilters RequestFilter(LedgerPlaceRow row,bool done)
        {return new LedgerRequestFilters{Done=done,Playfield=row.Record.Latest.Context.Playfield,Poi=row.RequestKey};}
    }
    public sealed class LedgerPlaceSortCache
    {
        private List<LedgerPlaceRow> source,result;private LedgerPlaceBrowser owner;private int column,revision;private bool descending;private LedgerPlayerPoint origin;private long at;
        public int Rebuilds {get;private set;}
        public List<LedgerPlaceRow> Apply(List<LedgerPlaceRow> rows,LedgerPlaceBrowser browser,LedgerHeaderSort sort,LedgerPlayerPoint player,int geographyRevision,long now)
        {
            bool scope=origin==null?player!=null:player==null||origin.World!=player.World||origin.Playfield!=player.Playfield||origin.SaveScope!=player.SaveScope;
            bool moved=origin!=null&&player!=null&&(origin.X!=player.X||origin.Y!=player.Y||origin.Z!=player.Z);
            if(result==null||!ReferenceEquals(source,rows)||!ReferenceEquals(owner,browser)||column!=sort.Column||descending!=sort.Descending||revision!=geographyRevision||sort.Column==1&&(scope||moved&&(now-at>=1000||now<at))){
                source=rows;owner=browser;column=sort.Column;descending=sort.Descending;revision=geographyRevision;at=now;origin=player;result=browser.Sort(rows,sort,player);Rebuilds++;
            }return result;
        }
    }
    // Selection is scoped to the current visible result, not a hidden queue of old checks.
    public sealed class LedgerPlaceSelection
    {
        public readonly HashSet<string> Selected=new HashSet<string>();private string signature;
        public void Scope(string next,IEnumerable<LedgerPlaceRow> rows){if(signature!=next){Selected.Clear();signature=next;}else Selected.IntersectWith(rows.Select(r=>r.Record.Key));}
        public void Toggle(string key){if(!Selected.Add(key))Selected.Remove(key);}
        // Checkbox owns membership. The return value requests detail focus only for a row-body click.
        public bool Activate(string key,bool checkboxPressed,bool rowPressed)
        {if(checkboxPressed){Toggle(key);return false;}return rowPressed;}
        public void All(IEnumerable<LedgerPlaceRow> rows){Selected.Clear();foreach(var r in rows)Selected.Add(r.Record.Key);}
        public LedgerObjectDisposition[] Move(IEnumerable<LedgerPlaceRow> visible,LedgerObjectOrganization next,string cursor=null)
        {bool checkedOnly=Selected.Count>0;return visible.Where(r=>(checkedOnly?Selected.Contains(r.Record.Key):r.Record.Key==cursor)&&r.Record.Organization!=next).Select(r=>LedgerDiscoveredObjects.Organize(r.Record,next)).ToArray();}
    }
    public sealed class LedgerPlaceBrowserLayout
    {
        public float Nav,Main,List,Detail,Height;public const int Row=48,MarketRow=32;
        public static float[] Columns(float width){return new[]{32f,width-208,width-176,width-90};}
        public LedgerPlaceBrowserLayout(float width,float height){Nav=Math.Max(200,Math.Min(280,width*.19f));Main=width-Nav-12;Detail=Math.Min(540,Math.Max(320,Main*.45f));List=Main-Detail-12;Height=height-104;}
    }
}
