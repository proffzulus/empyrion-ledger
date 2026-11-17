using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    public sealed class LedgerRoutePurchase
    {
        public string Route,Name;public long Demand;public int KnownBuyers,UnknownBuyers,StaleBuyers;
        public long? Held,Deficit;public bool CompleteInventory,Overflow,Relevant;
        public string Details {get{return KnownBuyers+UnknownBuyers+StaleBuyers==0?Name+": quantity unspecified":Name+": demand "+Demand+(UnknownBuyers+StaleBuyers>0||Overflow?"+":"")+" ("+KnownBuyers+" fresh, "+UnknownBuyers+" unknown, "+StaleBuyers+" stale); held "+(Held.HasValue?Held.Value.ToString(CultureInfo.InvariantCulture)+(CompleteInventory?"":"+"):"unknown")+"; buy "+(Deficit.HasValue?Deficit.Value.ToString(CultureInfo.InvariantCulture):"unknown");}}
    }
    // Transient derived purchase guidance on EXISTING route purposes. Not reservations or stock facts.
    public static class LedgerRoutePurchases
    {
        public static bool HasGoals(LedgerMarkerModel m,string world,int player)
        {
            foreach(var r in m.Data.Routes.Where(r=>r.World==world&&r.Player==player)){
                var purposes=m.Data.Destinations.Where(d=>d.World==world&&d.Player==player&&!d.DeleteRequested).SelectMany(d=>d.Purposes).Where(p=>p.Kind=="offer"&&p.Routes.Contains(r.Id)).ToList();
                var buyers=new HashSet<string>(purposes.Where(p=>!p.Plan.PlayerBuys).Select(LedgerRouteFlow.Item));
                if(purposes.Any(p=>p.Plan.PlayerBuys&&buyers.Contains(LedgerRouteFlow.Item(p))))return true;
            }return false;
        }
        public static void Reset(LedgerMarkerModel m){foreach(var p in m.Data.Destinations.SelectMany(d=>d.Purposes)){p.RoutePurchases=null;p.PurchaseSatisfied=false;}}
        public static void Evaluate(LedgerMarkerModel m,LedgerContext c,int player,ulong? now,LedgerRequestStock stock,LedgerProductionPlaces places=null)
        {
            Reset(m);if(c==null||!c.Recordable||c.Mode!="SinglePlayer")return;
            bool current=stock!=null&&stock.Current&&stock.Player==player&&stock.PhysicalScope==(c.LocalSaveKey??c.WorldKey)+"|"+c.Playfield;
            foreach(var r in m.Data.Routes.Where(r=>r.World==c.WorldKey&&r.Player==player)){
                var members=m.Data.Destinations.Where(d=>d.World==r.World&&d.Player==r.Player&&!d.DeleteRequested&&!d.Conflict).SelectMany(d=>d.Purposes.Where(p=>p.Kind=="offer"&&!p.Service&&p.Routes.Contains(r.Id)).Select(p=>Tuple.Create(d,p))).ToList();
                foreach(var item in members.GroupBy(x=>LedgerRouteFlow.Item(x.Item2))){
                    var buyers=item.Where(x=>!x.Item2.Plan.PlayerBuys).GroupBy(x=>x.Item1.Key).ToList();
                    // Empty buyer sets remain unspecified shopping goals; never assign them a zero target.
                    long demand=0;int known=0,unknown=0,stale=0;bool overflow=false;
                    foreach(var buyer in buyers){
                        var p=buyer.First().Item2;long? q=LedgerRouteFlow.Quantity(p);
                        if(!q.HasValue||buyer.Any(x=>LedgerRouteFlow.Quantity(x.Item2)!=q||LedgerRouteFlow.Fresh(x.Item2,c,now)!=LedgerRouteFlow.Fresh(p,c,now))){unknown++;continue;}
                        if(!LedgerRouteFlow.Fresh(p,c,now)){stale++;continue;}
                        try{demand=checked(demand+q.Value);known++;}catch(OverflowException){unknown++;overflow=true;}
                    }
                    foreach(var seller in item.Where(x=>x.Item2.Plan.PlayerBuys)){
                        var d=seller.Item1;var p=seller.Item2;
                        var h=new LedgerRoutePurchase{Route=r.Id,Name=r.Name,Demand=demand,KnownBuyers=known,UnknownBuyers=unknown,StaleBuyers=stale,Overflow=overflow,
                            Held=current?stock.Variant(p.Plan.ItemId,p.Plan.Auxiliary):null,CompleteInventory=current&&stock.CarryComplete};
                        h.Relevant=Relevant(m,d,p,r,places);
                        if(known>0&&stale==0&&!overflow&&h.CompleteInventory&&h.Held.HasValue)h.Deficit=Math.Max(0,demand-h.Held.Value);
                        if(p.RoutePurchases==null)p.RoutePurchases=new List<LedgerRoutePurchase>();p.RoutePurchases.Add(h);
                        // Partial purchases do not finish the common goal just because the old trade
                        // phase records some cargo. Use actual settled stock, never allocate suppliers.
                        if(h.Relevant&&r.Mode=="auto"&&p.TrackUpdates&&h.Deficit!=0&&known>0&&demand>0&&stale==0&&!overflow){
                            bool fresh=LedgerRouteFlow.Fresh(p,c,now);long? q=LedgerRouteFlow.Quantity(p);
                            if(fresh&&q>0||(!fresh||!q.HasValue)&&LedgerTradeUpdates.Forced(m,p)){p.FlowVisible=true;p.FlowStage=fresh&&q>0?"Buy":"Check";p.FlowToken=r.Id+":"+r.ShowRun+":inventory:"+h.Demand+":"+h.Held;}
                        }
                    }
                }
            }
            foreach(var d in m.Data.Destinations)foreach(var p in d.Purposes.Where(p=>p.RoutePurchases!=null)){
                var relevant=p.RoutePurchases.Where(h=>h.Relevant).ToList();
                bool unquantified=m.Data.Routes.Any(r=>p.Routes.Contains(r.Id)&&r.World==d.World&&r.Player==d.Player&&Relevant(m,d,p,r,places)&&!p.RoutePurchases.Any(h=>h.Route==r.Id));
                // Explicit individual Show is an independent existing manual intention.
                p.PurchaseSatisfied=p.Visibility<=0&&!unquantified&&relevant.Count>0&&relevant.All(h=>h.Deficit==0);
            }
        }
        private static bool Relevant(LedgerMarkerModel m,LedgerMarkerDestination d,LedgerMarkerPurpose p,LedgerMarkerRoute r,LedgerProductionPlaces places)
        {
            if(p.Visibility>0||p.Visibility==0&&r.Shown)return true;
            if(!(r.Mode=="auto"&&p.TrackUpdates||p.UpdateAutoVisible))return false;
            var hierarchy=places??new LedgerProductionPlaces(d.World,new[]{p.Plan.RefreshObservation==null?null:p.Plan.RefreshObservation.Context});
            var prefs=LedgerProductionServices.Preferences(m,d.World,d.Player,false);
            return prefs==null||!prefs.Effective(hierarchy.Ancestry("playfield:"+d.Playfield),LedgerTradeUpdates.RoutePolicyKey(r)).Suppressed;
        }
        public static string World(LedgerMarkerPurpose p,string name,bool historical=false)
        {
            if(p.RoutePurchases==null)return null;var list=p.RoutePurchases.Where(h=>h.Relevant&&h.Deficit!=0).OrderBy(h=>h.Route,StringComparer.Ordinal).ToList();
            if(list.Count==0||list.All(h=>h.KnownBuyers+h.UnknownBuyers+h.StaleBuyers==0))return null;
            Func<LedgerRoutePurchase,string> label=h=>(historical?"Buy (old): ":"Buy: ")+(h.Deficit.HasValue?h.Deficit.Value.ToString(CultureInfo.InvariantCulture)+" ":"")+name+(historical?"":" "+p.Plan.UnitPrice+" ("+p.Plan.Quantity+")");
            // Shared stock is evaluated independently, never summed. Different route deficits
            // get separate short attributed clauses on the one existing physical marker.
            bool same=list.All(h=>h.Deficit==list[0].Deficit);
            if(same)return label(list[0]);
            var clauses=list.Select(h=>label(h)+" ["+h.Name+"]").ToList();string text=clauses[0];
            for(int i=1;i<clauses.Count;i++){
                string suffix=", +"+(clauses.Count-i)+" route goals";
                if(text.Length+clauses[i].Length+2+suffix.Length>260)return text+suffix;
                text+=", "+clauses[i];
            }return text;
        }
    }
}
