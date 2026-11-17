using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    public sealed class LedgerMarkerNative
    {
        public int Id,Player,Type;
        public string Playfield,Label;
        public float X,Y,Z;
        public bool Shared,Universe,Hud,Approach;
        public bool Core(LedgerMarkerNative n)
        {return n!=null&&Id==n.Id&&Player==n.Player&&Type==n.Type&&Playfield==n.Playfield&&!n.Shared&&!Shared&&Near(X,n.X)&&Near(Y,n.Y)&&Near(Z,n.Z);}
        private static bool Near(float a,float b){return !float.IsNaN(a)&&!float.IsNaN(b)&&!float.IsInfinity(a)&&!float.IsInfinity(b)&&Math.Abs(a-b)<=.05f;}
        public bool Matches(LedgerMarkerNative n){return Core(n)&&Label==n.Label&&Universe==n.Universe&&Hud==n.Hud&&Approach==n.Approach;}
        public LedgerMarkerNative ChangedProjection(LedgerMarkerProjection p)
        {
            if(Core(this)&&Label==p.Label&&Universe==p.Visible&&Hud==p.Visible&&Approach==p.Approach)return null;
            // Plain owned values; no JSON roundtrip just to compare an unchanged native marker.
            return new LedgerMarkerNative{Id=Id,Player=Player,Type=Type,Playfield=Playfield,Label=p.Label,X=X,Y=Y,Z=Z,
                Shared=Shared,Universe=p.Visible,Hud=p.Visible,Approach=p.Approach};
        }
        public static LedgerMarkerNative Legacy(LedgerOwnedBookmark r)
        {return new LedgerMarkerNative{Id=r.NativeId,Player=r.PlayerId,Type=r.NativeType,Playfield=r.Plan.Playfield,Label=r.Plan.NativeLabel,X=r.Plan.X,Y=r.Plan.Y,Z=r.Plan.Z,Universe=r.Plan.VisibleInUniverse,Hud=r.Plan.VisibleInPlayfield,Approach=r.Plan.RemoveOnApproach};}
    }
    public sealed class LedgerMarkerPurpose
    {
        public string Key,Kind;
        public LedgerBookmarkPlan Plan;
        public List<string> Routes=new List<string>();
        // 0 inherits routes/ungrouped, 1 explicitly shown, -1 explicitly hidden.
        public int Visibility;
        public bool UngroupedShown=true;
        public bool Service,ServiceAutoVisible,ServiceRetired;
        // A shown empty/refuelled state completes this service run, not the device/route itself.
        public bool ServiceServiced;
        public string ServiceObservation;
        public bool RequestCompleted;
        public string RequestStateUtc;
        [System.Runtime.Serialization.IgnoreDataMember] public bool RequestBlocked;
        [System.Runtime.Serialization.IgnoreDataMember] public bool InventoryAutoVisible;
        [System.Runtime.Serialization.IgnoreDataMember] public string InventoryLabel;
        [System.Runtime.Serialization.IgnoreDataMember] public List<LedgerRoutePurchase> RoutePurchases;
        [System.Runtime.Serialization.IgnoreDataMember] public bool PurchaseSatisfied;
        public bool TrackUpdates; // Optional, defaults off for existing save-local documents.
        public bool ForceNotify,Consumed; // Quiet tracking by default; consumed one-shots are archival only.
        [System.Runtime.Serialization.IgnoreDataMember] public bool ServiceGeoHidden;
        [System.Runtime.Serialization.IgnoreDataMember] public string ServiceGeoLabel;
        [System.Runtime.Serialization.IgnoreDataMember] public long? UpdateMinutes;
        [System.Runtime.Serialization.IgnoreDataMember] public bool UpdateAutoVisible;
        [System.Runtime.Serialization.IgnoreDataMember] public LedgerOfferAge UpdateAge;
        public string Availability;
        public string Acknowledged,AcknowledgedManual,AcknowledgedAuto;public int ManualRun;
        [System.Runtime.Serialization.IgnoreDataMember] public bool FlowVisible;
        [System.Runtime.Serialization.IgnoreDataMember] public string FlowStage,FlowToken;
    }
    public sealed class LedgerMarkerRoute
    {public string Id=Guid.NewGuid().ToString("N"),Name,World,ServiceKey;public int Player;public bool Shown=true,ForceNotify;public string Mode;public int ShowRun;public List<LedgerRouteLoad> Loads=new List<LedgerRouteLoad>();}
    public sealed class LedgerMarkerDestination
    {
        public string IdentityId;
        public string Key,World,Playfield,Alias="",Place,Vendor,Block,PositionSource,Status;
        public string TargetKey,TargetKind;
        public int Player,Npc;public int? Structure;
        public float X,Y,Z;
        public bool Pinned,CreateRequested,DeleteRequested,Conflict;
        public LedgerMarkerNative Native,PendingNative;
        public List<LedgerMarkerNative> Duplicates=new List<LedgerMarkerNative>();
        public List<LedgerMarkerNative> LegacyCandidates=new List<LedgerMarkerNative>();
        public List<LedgerMarkerPurpose> Purposes=new List<LedgerMarkerPurpose>();
    }
    public sealed class LedgerMarkerDocument
    {
        public string IdentityScope;
        public List<string> DetachedIdentities=new List<string>();
        public string Format="TradeLedger.marker-manager.v1";
        public bool Migrated,LogicalWaypoints;
        public int NativeRecreationPolicy; // 1: disappearance is not suppression.
        public List<string> MigrationScopes=new List<string>();
        public List<LedgerOwnedBookmark> UnresolvedLegacy=new List<LedgerOwnedBookmark>();
        public List<LedgerMarkerDestination> Destinations=new List<LedgerMarkerDestination>();
        public List<LedgerMarkerRoute> Routes=new List<LedgerMarkerRoute>();
        public List<LedgerServicePreferences> Services=new List<LedgerServicePreferences>();
        public List<string> RouteEvents=new List<string>();
        public List<LedgerItemPinScope> ItemPins=new List<LedgerItemPinScope>();
        public List<LedgerRequestInventoryIntent> RequestInventory=new List<LedgerRequestInventoryIntent>();
    }
    public sealed class LedgerMarkerProjection
    {public string Label;public bool Visible,Approach;public List<string> ActiveKeys=new List<string>();}
    // Display scope is independent from the route selected for membership editing.
    public sealed class LedgerMarkerSelection
    {
        public string Scope="all",TargetRoute;
        public void All(){Scope="all";}
        public void Ungrouped(){Scope="ungrouped";}
        public void Route(string id){Scope=TargetRoute=id;}
        public void Deleted(string id){if(Scope==id)Scope="all";if(TargetRoute==id)TargetRoute=null;}
        public bool Includes(LedgerMarkerDestination d){return Scope=="all"||Scope=="ungrouped"?Scope=="all"||d.Purposes.Any(p=>p.Routes.Count==0):d.Purposes.Any(p=>p.Routes.Contains(Scope));}
        public bool IncludesPurpose(LedgerMarkerPurpose p){return Scope=="all"||Scope=="ungrouped"&&p.Routes.Count==0||p.Routes.Contains(Scope);}
        public static string Title(LedgerMarkerDestination d)
        {if(!string.IsNullOrEmpty(d.Alias))return d.Alias;string name=LedgerPresentation.Text(d.Place??d.Vendor??"Destination");string suffix=" / "+d.Playfield;return name.EndsWith(suffix,StringComparison.Ordinal)?name.Substring(0,name.Length-suffix.Length):name;}
        public static string Subtitle(LedgerMarkerDestination d)
        {string title=Title(d);return (d.Vendor!=null&&d.Vendor!=title&&d.Vendor!=d.Place?d.Vendor+" / ":"")+d.Playfield;}
    }
    // Pure owned metadata. No native actions, game queries, logger or storage dependency.
    public sealed class LedgerMarkerModel
    {
        public const string UpdateColor="FFFFFF"; // Native NGUI six-digit RGB: neutral white.
        public readonly LedgerMarkerDocument Data;
        public long Revision;
        public readonly HashSet<string> ReadyRoutes=new HashSet<string>(StringComparer.Ordinal);
        public LedgerMarkerModel(LedgerMarkerDocument document=null)
        {
            Data=document??new LedgerMarkerDocument();Validate(Data);
            if(Data.IdentityScope==null){Data.IdentityScope=Guid.NewGuid().ToString("N");Revision++;}
            foreach(var d in Data.Destinations)if(d.IdentityId==null){d.IdentityId=Guid.NewGuid().ToString("N");Revision++;}
            foreach(var p in Data.Destinations.SelectMany(d=>d.Purposes).Where(p=>p.Kind=="request"&&p.RequestCompleted&&!p.Consumed)){p.Consumed=true;Revision++;}
            if(Data.NativeRecreationPolicy==0){
                // Old acknowledgements did not record whether absence or actual observation caused them.
                // Discard that ambiguous latch once; explicit visibility/suppression and factual route state survive.
                foreach(var p in Data.Destinations.SelectMany(d=>d.Purposes).Where(p=>!p.Service))p.Acknowledged=p.AcknowledgedManual=p.AcknowledgedAuto=null;
                Data.NativeRecreationPolicy=1;Revision++;
            }
        }
        public static string DestinationKey(LedgerBookmarkPlan p,int player)
        {return p.World+"|"+player.ToString(CultureInfo.InvariantCulture)+"|"+LedgerJson.Hash(p.Playfield)+(p.TargetKey==null?"|npc:"+p.NpcId:"|observed:"+LedgerJson.Hash(p.TargetKey));}
        public static string PurposeKey(LedgerBookmarkPlan p)
        {return p.TargetKey!=null?"visit:"+LedgerJson.Hash(p.TargetKey):p.ItemId>0?"item:"+p.ItemId+":"+(p.Auxiliary.HasValue?p.Auxiliary.Value.ToString(CultureInfo.InvariantCulture):"unknown")+":"+(p.PlayerBuys?"buy":"sell"):"request:"+(p.RequestKey!=null&&p.RequestBody!=null&&p.RequestKey!=LedgerManualCompletion.KeyFor(LedgerRequestMarkers.Endpoint(p),p.RequestBody)?p.RequestKey:LedgerJson.Hash(p.RequestBody??p.OfferKey));}
        public LedgerMarkerDestination Find(LedgerBookmarkPlan p,int player){string key=DestinationKey(p,player);return Data.Destinations.FirstOrDefault(d=>d.Key==key);}
        public static bool SameTarget(LedgerMarkerDestination d,LedgerBookmarkPlan p)
        {return d.World==p.World&&d.Playfield==p.Playfield&&d.Npc==p.NpcId&&d.TargetKey==p.TargetKey&&!(d.Structure.HasValue&&p.StructureId.HasValue&&d.Structure!=p.StructureId)&&!(d.Block!=null&&p.Block!=null&&d.Block!=p.Block)&&Distance(d.X,d.Y,d.Z,p.X,p.Y,p.Z)<=1;}
        private static double Distance(float x,float y,float z,float a,float b,float c){return Math.Sqrt((x-a)*(double)(x-a)+(y-b)*(double)(y-b)+(z-c)*(double)(z-c));}
        public LedgerMarkerDestination Ensure(LedgerBookmarkPlan p,int player,bool arm=true,bool showService=false)
        {
            if(p==null||player<=0||p.NpcId<=0&&string.IsNullOrEmpty(p.TargetKey)||p.TargetKey!=null&&(p.NpcId!=0||p.TargetKey.Length>512||(p.TargetKind!="producer"&&p.TargetKind!="deposit"&&p.TargetKind!="place"))||string.IsNullOrEmpty(p.World)||string.IsNullOrEmpty(p.Playfield)||!Finite(p.X)||!Finite(p.Y)||!Finite(p.Z))throw new InvalidOperationException("Observed destination identity unavailable");
            var d=Find(p,player);
            if(d==null)
            {
                if(Data.Destinations.Count>=512)throw new InvalidOperationException("Marker destination limit512");
                d=new LedgerMarkerDestination{IdentityId=Guid.NewGuid().ToString("N"),Key=DestinationKey(p,player),World=p.World,Player=player,Playfield=p.Playfield,Npc=p.NpcId,TargetKey=p.TargetKey,TargetKind=p.TargetKind,Structure=p.StructureId,Block=p.Block,PositionSource=p.PositionSource,X=p.X,Y=p.Y,Z=p.Z,Place=p.Place,Vendor=p.Vendor};Data.Destinations.Add(d);
            }
            else if(!SameTarget(d,p))throw new InvalidOperationException("This NPC has conflicting recorded location evidence; existing marker left untouched");
            if(!d.Structure.HasValue)d.Structure=p.StructureId;if(d.Block==null)d.Block=p.Block;if(d.PositionSource==null)d.PositionSource=p.PositionSource;if(p.Place!=null)d.Place=p.Place;if(p.Vendor!=null)d.Vendor=p.Vendor;
            string key=PurposeKey(p);var purpose=d.Purposes.FirstOrDefault(v=>v.Key==key);
            if(purpose==null){if(d.Purposes.Count>=32)throw new InvalidOperationException("Destination purpose limit32");purpose=new LedgerMarkerPurpose{Key=key,Kind=p.TargetKey!=null?"visit":p.ItemId>0?"offer":"request"};d.Purposes.Add(purpose);}
            if(purpose.Plan==null||string.CompareOrdinal(p.ObservedUtc,purpose.Plan.ObservedUtc)>=0){purpose.Plan=Clone(p);purpose.Availability=null;}
            if(arm){if(p.RequestKey!=null)LedgerRequestInventory.Resume(this,p,player);if(showService&&p.TargetKind=="producer"){purpose.Service=true;purpose.ServiceServiced=false;purpose.Visibility=1;d.Pinned=true;LedgerProductionServices.Preferences(this,p.World,player).Deleted.Remove(d.Key);}purpose.Consumed=false;purpose.PurchaseSatisfied=false;if(!purpose.Service&&(Routed(purpose)||!ManualActive(purpose)))purpose.Visibility=1;purpose.ManualRun++;purpose.Acknowledged=purpose.AcknowledgedManual=purpose.AcknowledgedAuto=null;d.CreateRequested=d.Native==null;d.DeleteRequested=false;}
            Revision++;return d;
        }
        private static LedgerBookmarkPlan Clone(LedgerBookmarkPlan p){return LedgerJson.Decode<LedgerBookmarkPlan>(LedgerJson.Encode(p));}
        public LedgerMarkerRoute AddRoute(string name,string world=null,int player=0)
        {if(Data.Routes.Count>=64)throw new InvalidOperationException("Route limit64");name=Name(name,64);var r=new LedgerMarkerRoute{Name=name,World=world,Player=player};Data.Routes.Add(r);Revision++;return r;}
        public static string Name(string name,int bound)
        {name=LedgerPresentation.Text(name??"").Replace("\r"," ").Replace("\n"," ").Trim();if(name.Length==0||name.Length>bound||name.IndexOfAny(new[]{'[',']','<','>'})>=0)throw new InvalidOperationException("Enter a name of1-"+bound+" characters");return name;}
        public void Membership(LedgerMarkerPurpose p,string route,bool member)
        {
            if(!Data.Routes.Any(r=>r.Id==route))throw new InvalidOperationException("Route unavailable");
            if(member&&!p.Routes.Contains(route)){p.Routes.Add(route);p.UngroupedShown=false;}else if(!member)p.Routes.Remove(route);
            Revision++;
        }
        public bool Active(LedgerMarkerPurpose p)
        {if(p.Consumed||p.PurchaseSatisfied)return false;if(p.Kind=="request"&&(p.RequestCompleted||p.RequestBlocked))return false;if(p.Service)return !p.ServiceRetired&&(!p.ServiceServiced&&(p.Visibility>0||p.Routes.Count==0&&p.UngroupedShown||Data.Routes.Any(r=>r.Shown&&p.Routes.Contains(r.Id)))||p.ServiceAutoVisible);return LedgerRouteFlow.Needed(this,p)||p.Kind=="request"&&p.InventoryAutoVisible;}
        public bool Routed(LedgerMarkerPurpose p){return Data.Routes.Any(r=>r.ServiceKey==null&&p.Routes.Contains(r.Id));}
        public bool OneShotOffer(LedgerMarkerPurpose p){return p.Kind=="offer"&&!p.Service&&!p.TrackUpdates&&!Routed(p);}
        public bool Listed(LedgerMarkerPurpose p){return !p.Consumed&&!(p.Kind=="request"&&p.RequestCompleted);}
        public void Consume(LedgerMarkerPurpose p){if(!p.Consumed){p.Consumed=true;p.InventoryAutoVisible=p.UpdateAutoVisible=p.FlowVisible=false;Revision++;}}
        public bool ManualActive(LedgerMarkerPurpose p)
        {if(p.Visibility!=0)return p.Visibility>0;return p.Routes.Count==0?p.UngroupedShown:Data.Routes.Any(r=>r.Shown&&p.Routes.Contains(r.Id));}
        public void Eye(LedgerMarkerPurpose p){p.ManualRun++;p.Acknowledged=null;p.Visibility=p.Service?(p.Visibility>0&&!p.ServiceServiced?-1:1):(ManualActive(p)?-1:1);if(p.Service&&p.Visibility>0)p.ServiceServiced=false;Revision++;}
        public void Auto(LedgerMarkerPurpose p){p.Visibility=0;p.ServiceServiced=false;Revision++;}
        public void TogglePin(LedgerMarkerDestination d){d.Pinned=!d.Pinned;if(d.Pinned)foreach(var p in d.Purposes.Where(p=>!p.Service)){p.ManualRun++;p.Acknowledged=null;}if(d.Pinned&&d.Native==null&&!d.Conflict)d.CreateRequested=true;Revision++;}
        public void RouteEye(LedgerMarkerRoute route){ShowRoute(route,!route.Shown);}
        public void ShowRoute(LedgerMarkerRoute route,bool shown){route.Shown=shown;route.Mode=shown?"show":"hide";route.ShowRun++;if(shown)foreach(var p in Data.Destinations.SelectMany(d=>d.Purposes).Where(p=>p.Service&&p.Routes.Contains(route.Id)))p.ServiceServiced=false;Revision++;}
        public void Restore(LedgerMarkerDestination d){if(d!=null&&d.Native==null&&!d.Conflict&&!d.DeleteRequested){foreach(var p in d.Purposes.Where(p=>!p.Service)){p.ManualRun++;p.Acknowledged=p.AcknowledgedManual=p.AcknowledgedAuto=null;p.Visibility=1;}d.CreateRequested=true;Revision++;}}
        public bool Grouped(LedgerMarkerDestination d){return d.Purposes.Any(p=>Data.Routes.Any(r=>r.World==d.World&&r.Player==d.Player&&p.Routes.Contains(r.Id)));}
        public void RemovePurpose(LedgerMarkerDestination d,LedgerMarkerPurpose p){LedgerRequestInventory.Remove(this,d,p);if(p.Service)RememberServiceDelete(d);d.Purposes.Remove(p);Revision++;}
        public void DeleteDestination(LedgerMarkerDestination d){foreach(var p in d.Purposes)LedgerRequestInventory.Remove(this,d,p);d.DeleteRequested=true;Revision++;}
        public void DeleteRoute(string id){foreach(var prefs in Data.Services)prefs.Overrides.RemoveAll(o=>o.Key==LedgerTradeUpdates.RouteKey(id));Data.Routes.RemoveAll(r=>r.Id==id);foreach(var p in Data.Destinations.SelectMany(d=>d.Purposes))p.Routes.Remove(id);Revision++;}
        public string Summary(LedgerMarkerPurpose p,bool color=false)
        {
            string text;
            if(p.Kind=="visit")text=p.Plan.Label;
            else if(p.Kind=="request")text=p.InventoryLabel??(p.Plan.DeliveryText??"Delivery - items unknown").Replace("Delivery ","Delivery: ");
            else if(p.Plan.ItemName==null)text=p.Availability==null?p.Plan.Label:(p.Plan.PlayerBuys?"Buy ":"Sell ")+"previously marked item: unknown (unknown) \u2014 "+p.Availability; // Exact legacy text until an observed snapshot supplies fields.
            else text=(p.Plan.PlayerBuys?"Buy ":"Sell ")+p.Plan.ItemName+": "+(p.Plan.UnitPrice??"unknown")+" ("+(p.Plan.Quantity??"unknown")+")"+(p.Availability==null?"":" — "+p.Availability);
            if(p.RoutePurchases!=null)text+=" | "+string.Join(" | ",p.RoutePurchases.Select(h=>h.Details))+"; shared stock, not reserved";
            if(p.Kind=="offer"&&p.FlowVisible&&p.FlowStage=="Check")text="Check "+p.Plan.ItemName+": "+(p.Plan.UnitPrice??"unknown")+" (last "+(p.Plan.Quantity??"unknown")+")";
            if(p.Kind=="offer"&&p.TrackUpdates&&p.UpdateAge==LedgerOfferAge.Recheck&&p.FlowStage!="Check")text+=" \u00b7 recheck";
            return color?"[c]["+(p.Service?LedgerProductionServices.Color:p.Plan.PlayerBuys&&p.Kind=="offer"?"80E680":"80E5FF")+"]"+text+"[-][/c]":text;
        }
        public LedgerMarkerProjection Project(LedgerMarkerDestination d)
        {
            var active=d.Purposes.Where(p=>Active(p)&&!p.ServiceGeoHidden).OrderBy(p=>p.Key,StringComparer.Ordinal).ToList();
            string title=string.IsNullOrWhiteSpace(d.Alias)?(d.Place??d.Vendor??"Trading destination"):d.Alias;
            title=LedgerPresentation.Text(title);if(title.Length>180)title=title.Substring(0,180);
            var result=new LedgerMarkerProjection{Visible=active.Count>0,Approach=!d.Purposes.Any(p=>p.Service)&&active.Count>0&&(!d.Pinned||active.All(p=>!LedgerRouteFlow.ManualNeeded(this,p))),Label=title};
            bool compactService=active.Count>0&&active.All(p=>p.Service)&&string.IsNullOrWhiteSpace(d.Alias);if(compactService)result.Label="";
            // World labels are actions, not the journal's factual/diagnostic summary. Keep
            // every active identity even when a large shared destination needs a count suffix.
            bool tradeOnly=active.Count>0&&active.All(p=>p.Kind=="offer");
            int budget=tradeOnly?480:1800,shown=0;string previousOldPrefix=null;
            result.ActiveKeys.AddRange(active.Select(p=>p.Key));
            foreach(var p in active)
            {
                string next=p.ServiceGeoLabel??WorldSummary(p,true),separator=shown==0?(result.Label.Length>0?" — ":""):", ";
                string oldPrefix=next.StartsWith("[c][80E680]Buy (old): ",StringComparison.Ordinal)?"[c][80E680]Buy (old): ":next.StartsWith("[c][80E5FF]Sell (old): ",StringComparison.Ordinal)?"[c][80E5FF]Sell (old): ":null;
                if(oldPrefix!=null&&oldPrefix==previousOldPrefix)next=oldPrefix.Substring(0,11)+next.Substring(oldPrefix.Length);
                previousOldPrefix=oldPrefix;
                string suffix=shown+1<active.Count?", +"+(active.Count-shown-1)+" more":"";
                string candidate=result.Label+separator+next+suffix;
                if((shown>0||!tradeOnly)&&(tradeOnly?LedgerPresentation.Text(candidate).Length:candidate.Length)>budget){result.Label+=", +"+(active.Count-shown)+" more";break;}
                result.Label+=separator+next;shown++;
            }
            // Hidden bookmarks keep map type/existence and temporarily disable approach deletion.
            return result;
        }
        public string WorldSummary(LedgerMarkerPurpose p,bool color=false)
        {
            if(p.Kind!="offer")return Summary(p,color); // Delivery/service labels have their own compact contracts.
            bool identified=!string.IsNullOrWhiteSpace(p.Plan.ItemName);
            string name=!identified?"item unknown":
                System.Text.RegularExpressions.Regex.Replace(LedgerPresentation.Text(p.Plan.ItemName),@"\s+"," ");
            long? quantity=LedgerRouteFlow.Quantity(p);
            decimal price;
            bool quoted=decimal.TryParse(p.Plan.UnitPrice,NumberStyles.Float,CultureInfo.InvariantCulture,out price)&&price>=0;
            bool recheck=!identified||!quoted||p.Availability!=null||!quantity.HasValue||quantity==0||
                p.UpdateAge==LedgerOfferAge.Recheck||p.FlowVisible&&p.FlowStage=="Check";
            string text=(recheck?"Update: ":p.Plan.PlayerBuys?"Buy: ":"Sell: ")+name+
                (recheck?"":" "+p.Plan.UnitPrice+" ("+p.Plan.Quantity+")");
            if(!recheck&&p.Plan.PlayerBuys){string purchase=LedgerRoutePurchases.World(p,name);if(purchase!=null)text=purchase;}
            bool retained=p.Availability=="not in latest observed assortment"||p.Availability=="last seen; absent from shown rows";
            bool historicalRow=p.Availability==null&&quoted&&quantity.HasValue||retained&&!string.IsNullOrEmpty(p.Plan.OfferKey);
            bool oldRole=recheck&&identified&&historicalRow&&(HistoricalRouteRole(p)||OneShotOffer(p)&&Active(p)&&ManualActive(p)&&p.Plan.Auxiliary.HasValue);
            if(oldRole){
                string qualifier=p.UpdateAge==LedgerOfferAge.Recheck?"old":"check";
                text=(p.Plan.PlayerBuys?"Buy (":"Sell (")+qualifier+"): "+name;
                if(p.Plan.PlayerBuys){string purchase=LedgerRoutePurchases.World(p,name,true);if(purchase!=null)text=purchase.Replace("Buy (old): ","Buy ("+qualifier+"): ");}
            }
            return color?"[c]["+(recheck&&!oldRole?UpdateColor:p.Plan.PlayerBuys?"80E680":"80E5FF")+"]"+text+"[-][/c]":text;
        }
        // Route action and quote confidence are independent. This only formats an
        // ALREADY active purpose; it never creates a goal, changes age or arms visibility.
        private bool HistoricalRouteRole(LedgerMarkerPurpose p)
        {
            if(p.Service||!p.Plan.Auxiliary.HasValue||!Active(p))return false;
            var d=Data.Destinations.FirstOrDefault(x=>!x.DeleteRequested&&!x.Conflict&&x.Purposes.Contains(p));if(d==null)return false;
            var places=new LedgerProductionPlaces(d.World,Data.Destinations.Where(x=>x.World==d.World&&x.Player==d.Player).SelectMany(x=>x.Purposes).Where(x=>x.Plan.RefreshObservation!=null).Select(x=>x.Plan.RefreshObservation.Context));
            var prefs=LedgerProductionServices.Preferences(this,d.World,d.Player,false);
            foreach(var r in Data.Routes.Where(r=>r.ServiceKey==null&&r.World==d.World&&r.Player==d.Player&&p.Routes.Contains(r.Id)&&(r.Shown||p.Visibility>0||r.ForceNotify||p.ForceNotify&&p.TrackUpdates||r.Mode=="auto"&&p.TrackUpdates))){
                if(!r.Shown&&p.Visibility<=0&&prefs!=null&&prefs.Effective(places.Ancestry("playfield:"+d.Playfield),LedgerTradeUpdates.RoutePolicyKey(r)).Suppressed)continue;
                var opposite=Data.Destinations.Where(x=>x.World==d.World&&x.Player==d.Player&&!x.DeleteRequested&&!x.Conflict).SelectMany(x=>x.Purposes).Where(x=>x.Kind=="offer"&&!x.Service&&x.Routes.Contains(r.Id)&&x.Plan.ItemId==p.Plan.ItemId&&x.Plan.Auxiliary==p.Plan.Auxiliary&&x.Plan.PlayerBuys!=p.Plan.PlayerBuys);
                // Buying needs some personally recorded positive route demand. Unknown
                // demand never invents a quantity. Selling keeps its intended buyer even
                // after the supplier was exhausted by a real purchase.
                if(p.Plan.PlayerBuys?opposite.Any(x=>LedgerRouteFlow.Quantity(x)>0):opposite.Any())return true;
            }return false;
        }
        public void Observe(LedgerObservation o)
        {
            if(o!=null&&o.Kind=="production"&&LedgerProduction.Valid(o)){
                foreach(var d in Data.Destinations.Where(d=>d.World==o.Context.WorldKey&&d.Player==o.Production.Player&&d.TargetKey==o.Production.Key))foreach(var p in d.Purposes.Where(p=>p.Kind=="visit"&&!p.Service&&string.CompareOrdinal(o.Utc,p.Plan.ObservedUtc)>=0)){
                    p.Plan.ObservedUtc=o.Utc;p.Plan.Label="Visit "+LedgerPresentation.Text(o.Production.Device??o.Production.Resource)+": "+string.Join(", ",o.Production.Outputs.Select(x=>LedgerPresentation.Text(x.Name)+" "+x.Stock));Revision++;
                }return;
            }
            if(o==null||o.Kind!="market"||o.FullCurrentAssortment!=true||o.Identity==null||!o.Identity.NpcEntityId.HasValue||o.Context==null||o.Offers==null)return;
            foreach(var d in Data.Destinations.Where(d=>d.World==o.Context.WorldKey&&d.Playfield==o.Context.Playfield&&d.Npc==o.Identity.NpcEntityId&&!d.DeleteRequested))
            {
                if(o.Identity.OwnerConsistent==false||d.Structure.HasValue&&o.Identity.StructureEntityId.HasValue&&d.Structure!=o.Identity.StructureEntityId||d.Block!=null&&o.Identity.BlockGrid!=null&&d.Block!=o.Identity.BlockGrid){d.Status="Conflicting observed owner/location; values not updated";continue;}
                if(!d.Structure.HasValue)d.Structure=o.Identity.StructureEntityId;if(d.Block==null)d.Block=o.Identity.BlockGrid;if(o.Identity.StructureName!=null)d.Place=LedgerViewData.Place(o);
                foreach(var p in d.Purposes.Where(p=>p.Kind=="offer"&&string.CompareOrdinal(o.Utc,p.Plan.ObservedUtc)>=0))
                {
                    ulong observedTick;
                    if(p.TrackUpdates&&!Routed(p)&&string.CompareOrdinal(o.Utc,p.Plan.ObservedUtc)>0&&ulong.TryParse(o.GameTicks,NumberStyles.None,CultureInfo.InvariantCulture,out observedTick)&&LedgerFreshness.Evaluate(p.Plan.RefreshObservation,o.Context,observedTick)==LedgerOfferAge.Recheck)Consume(p);
                    if(p.TrackUpdates&&string.CompareOrdinal(o.Utc,p.Plan.ObservedUtc)>0)LedgerRouteFlow.AcknowledgePurpose(this,p);
                    var rows=o.Offers.Where(r=>r.ItemId==p.Plan.ItemId&&r.AuxiliaryRaw==p.Plan.Auxiliary&&LedgerOffer.Certified(r)).ToList();
                    p.Plan.ObservedUtc=o.Utc;
                    p.Plan.RefreshObservation=LedgerTradeUpdates.Evidence(o);
                    if(rows.Count!=1){p.Plan.UnitPrice=p.Plan.Quantity=null;p.Availability=rows.Count==0?"not in latest observed assortment":"ambiguous observed rows";continue;}
                    var row=rows[0];bool? supported=p.Plan.PlayerBuys?row.NpcSellsSupported:row.NpcBuysSupported;
                    if(row.Visible!=null)p.Plan.ItemName=LedgerPresentation.Text(row.Visible.Name);
                    p.Plan.UnitPrice=p.Plan.PlayerBuys?row.StoredSellUnit:row.StoredBuyUnit;
                    p.Plan.Quantity=p.Plan.PlayerBuys?(row.Stock.HasValue?row.Stock.Value.ToString(CultureInfo.InvariantCulture):null):(row.RemainingNpcBuyingCapacity.HasValue?row.RemainingNpcBuyingCapacity.Value.ToString(CultureInfo.InvariantCulture):null);
                    p.Availability=row.RetainedRelation?"last seen; absent from shown rows":supported==false?"direction unsupported":supported==null?"direction unknown":null;
                    if(supported!=true)p.Plan.UnitPrice=p.Plan.Quantity=null;
                }
                Revision++;
            }
        }
        public void Reconcile(LedgerMarkerDestination d,IDictionary<int,LedgerMarkerNative> native)
        {
            if(d.Native==null)return;LedgerMarkerNative now;
            if(!native.TryGetValue(d.Native.Id,out now))
            {if(d.Native.Approach)foreach(var p in d.Purposes.Where(p=>((p.Kind=="visit"&&(p.Plan.TargetKind=="deposit"||p.Plan.TargetKind=="place")&&!p.Service&&!Routed(p))||OneShotOffer(p))&&Active(p)))Consume(p);d.Native=d.PendingNative=null;d.CreateRequested=false;d.Conflict=false;d.Status="Native waypoint absent; current visibility conditions apply";Revision++;return;}
            if(!d.Native.Core(now))
            {d.Native=d.PendingNative=null;d.CreateRequested=false;d.Conflict=false;d.Status="Native ID reused or moved; other marker untouched; current visibility conditions apply";Revision++;return;}
            if(d.Native.Matches(now)||d.PendingNative!=null&&d.PendingNative.Matches(now))
            {if(d.PendingNative!=null&&d.PendingNative.Matches(now)){d.Native=now;d.PendingNative=null;Revision++;}if(d.Conflict)d.Status="";d.Conflict=false;return;}
            d.Conflict=true;d.Status="Native text/options edited. Detach leaves it untouched; Mark can then create a separate managed marker";
        }
        public void RememberServiceDelete(LedgerMarkerDestination d)
        {if(d.Purposes.Any(p=>p.Service)){var prefs=LedgerProductionServices.Preferences(this,d.World,d.Player);if(!prefs.Deleted.Contains(d.Key)){if(prefs.Deleted.Count>=1024)throw new InvalidOperationException("Service deletion limit1024");prefs.Deleted.Add(d.Key);Revision++;}}}
        public void Detach(LedgerMarkerDestination d){if(Data.DetachedIdentities.Count>=1024)throw new InvalidOperationException("Detached identity limit1024");Data.DetachedIdentities.Add(d.IdentityId);d.IdentityId=Guid.NewGuid().ToString("N");d.Native=d.PendingNative=null;d.Duplicates.Clear();d.LegacyCandidates.Clear();d.CreateRequested=false;d.Conflict=false;d.Status="Detached; native marker unchanged";Revision++;}
        public void Migrate(IEnumerable<LedgerOwnedBookmark> legacy,IDictionary<int,LedgerMarkerNative> native,string scope="all")
        {
            if(Data.MigrationScopes.Contains(scope))return;
            foreach(var r in legacy)
            {
                try{
                var d=Ensure(r.Plan,r.PlayerId,false);var proof=LedgerMarkerNative.Legacy(r);LedgerMarkerNative current;
                d.LegacyCandidates.Add(proof);var purpose=d.Purposes.Single(p=>p.Key==PurposeKey(r.Plan));
                if(native.TryGetValue(r.NativeId,out current)&&proof.Matches(current))
                {d.Pinned|=!r.Plan.RemoveOnApproach;purpose.UngroupedShown=true;d.Status="Owned legacy marker migrated";}
                else{purpose.UngroupedShown=false;d.Status=d.Native!=null?d.Status:"Legacy purpose retained; old native ID no longer proves ownership";}
                }
                catch(InvalidOperationException){Data.UnresolvedLegacy.Add(r);}
            }
            Data.Migrated=true;Data.MigrationScopes.Add(scope);Revision++;
        }
        public static void Validate(LedgerMarkerDocument data)
        {
            if(data!=null)LedgerRequestInventory.Validate(data);
            if(data!=null&&data.Services==null)data.Services=new List<LedgerServicePreferences>();
            if(data!=null){if(data.ItemPins==null)data.ItemPins=new List<LedgerItemPinScope>();LedgerItemPins.Validate(data.ItemPins);}
            if(data!=null){if(data.Services.Count>64)throw new InvalidOperationException("Service scope limit64");foreach(var prefs in data.Services)LedgerServicePreferences.Validate(prefs);if(data.Services.Select(p=>p.World+"|"+p.Player).Distinct().Count()!=data.Services.Count)throw new InvalidOperationException("Duplicate service scope");}
            if(data==null||data.Format!="TradeLedger.marker-manager.v1"||data.Destinations==null||data.Routes==null||data.Destinations.Count>512||data.Routes.Count>64)throw new InvalidOperationException("Invalid marker manager data; no reset performed");
            if(data.DetachedIdentities==null)data.DetachedIdentities=new List<string>();
            if(data.IdentityScope!=null&&!LedgerMarkerIdentity.ValidId(data.IdentityScope)||data.DetachedIdentities.Count>1024||data.DetachedIdentities.Any(id=>!LedgerMarkerIdentity.ValidId(id))||data.Destinations.Any(d=>d!=null&&d.IdentityId!=null&&!LedgerMarkerIdentity.ValidId(d.IdentityId))||data.Destinations.Where(d=>d!=null&&d.IdentityId!=null).Select(d=>d.IdentityId).Distinct().Count()!=data.Destinations.Count(d=>d!=null&&d.IdentityId!=null))throw new InvalidOperationException("Invalid persistent native marker identity");
            if(data.NativeRecreationPolicy<0||data.NativeRecreationPolicy>1)throw new InvalidOperationException("Unknown waypoint recreation policy; original preserved");
            if(data.RouteEvents==null||data.RouteEvents.Count>256||data.RouteEvents.Any(x=>x==null||x.Length>128)||data.Routes.Any(r=>r.Loads==null||r.Loads.Count>64||r.Loads.Any(l=>l.Quantity<0||l.Quantity>1000000000L)||(r.Mode!=null&&r.Mode!="auto"&&r.Mode!="show"&&r.Mode!="hide")))throw new InvalidOperationException("Invalid route flow state");
            if(data.Destinations.Select(d=>d.Key).Distinct().Count()!=data.Destinations.Count||data.Routes.Select(r=>r.Id).Distinct().Count()!=data.Routes.Count)throw new InvalidOperationException("Duplicate marker identities");
            foreach(var d in data.Destinations)
            {
                if(d!=null&&d.TargetKey!=null&&(d.Npc!=0||d.TargetKey.Length>512||(d.TargetKind!="producer"&&d.TargetKind!="deposit"&&d.TargetKind!="place")))throw new InvalidOperationException("Invalid observed destination type");
                if(d==null||d.Player<=0||d.Npc<=0&&string.IsNullOrEmpty(d.TargetKey)||string.IsNullOrEmpty(d.World)||string.IsNullOrEmpty(d.Playfield)||d.Playfield.Length>160||d.Purposes==null||d.Purposes.Count>32||d.LegacyCandidates==null||d.Duplicates==null||d.LegacyCandidates.Count>128||d.Duplicates.Count>128||d.Alias==null||d.Alias.Length>100||!Finite(d.X)||!Finite(d.Y)||!Finite(d.Z))throw new InvalidOperationException("Invalid destination data");
                if(d.Key!=DestinationKey(new LedgerBookmarkPlan{World=d.World,Playfield=d.Playfield,NpcId=d.Npc,TargetKey=d.TargetKey},d.Player))throw new InvalidOperationException("Destination scope/key mismatch");
                if(d.Purposes.Select(p=>p.Key).Distinct().Count()!=d.Purposes.Count)throw new InvalidOperationException("Duplicate purposes");
                foreach(var p in d.Purposes)
                    if(p!=null&&p.Plan!=null&&p.Plan.DeliveryText!=null&&(p.Kind!="request"||p.Plan.DeliveryText.Length>1300||!p.Plan.DeliveryText.StartsWith("Delivery ",StringComparison.Ordinal)))throw new InvalidOperationException("Invalid delivery summary");
                foreach(var p in d.Purposes)
                    if(p==null||p.Plan==null||p.Routes==null||p.Routes.Count>64||p.Visibility < -1||p.Visibility>1||(p.ServiceObservation??"").Length>128||p.Plan.World!=d.World||p.Plan.Playfield!=d.Playfield||p.Plan.NpcId!=d.Npc||p.Key!=PurposeKey(p.Plan)||p.Plan.TargetKey!=d.TargetKey||p.Kind!=(p.Plan.TargetKey!=null?"visit":p.Plan.ItemId>0?"offer":"request"))throw new InvalidOperationException("Invalid scoped purpose");
                foreach(var n in d.LegacyCandidates.Concat(d.Duplicates).Concat(new[]{d.Native,d.PendingNative}).Where(n=>n!=null))
                    if(n.Id<0||n.Player!=d.Player||n.Playfield!=d.Playfield||n.Type!=2&&n.Type!=3||n.Shared||n.Label==null||n.Label.Length>2048||!Finite(n.X)||!Finite(n.Y)||!Finite(n.Z))throw new InvalidOperationException("Invalid native ownership receipt");
            }
        }
        private static bool Finite(float n){return !float.IsNaN(n)&&!float.IsInfinity(n)&&Math.Abs(n)<1000000000;}
    }
}
