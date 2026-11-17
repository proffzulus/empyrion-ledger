using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    // Remembered intentions, not inventory/accounting. Only confirmed native outcomes move cargo steps.
    public sealed class LedgerRouteLoad { public string Item; public long Quantity; }
    public static class LedgerRouteFlow
    {
        public static string Item(LedgerMarkerPurpose p){return p.Plan.ItemId+":"+p.Plan.Auxiliary;}
        public static string Evidence(LedgerMarkerPurpose p){var o=p.Plan.RefreshObservation;return o==null?"unknown":o.OfferFreshness==null?"unknown":o.OfferFreshness.NextRefreshTick.ToString();}
        public static long? Quantity(LedgerMarkerPurpose p){long n;return p.Availability==null&&long.TryParse(p.Plan.Quantity,NumberStyles.None,CultureInfo.InvariantCulture,out n)&&n>=0?(long?)n:null;}
        public static string CheckToken(LedgerMarkerPurpose p,LedgerContext c,ulong? now){return (LedgerFreshness.Evaluate(p.Plan.RefreshObservation,c,now)==LedgerOfferAge.Recheck?"due:":"unknown:")+Evidence(p);}
        public static bool Fresh(LedgerMarkerPurpose p,LedgerContext c,ulong? now){var a=LedgerFreshness.Evaluate(p.Plan.RefreshObservation,c,now);return a==LedgerOfferAge.BeforeDeadline||a==LedgerOfferAge.Disabled;}
        public static string ManualActivation(LedgerMarkerModel m,LedgerMarkerPurpose p)
        {return !p.Service&&m.ManualActive(p)?"manual:"+p.ManualRun+":"+string.Join(",",m.Data.Routes.Where(r=>r.Shown&&p.Routes.Contains(r.Id)).OrderBy(r=>r.Id).Select(r=>r.Id+":"+r.ShowRun)):null;}
        public static string AutomaticActivation(LedgerMarkerPurpose p)
        {return p.Service?null:p.FlowVisible?"flow:"+p.FlowStage+":"+p.FlowToken:p.UpdateAutoVisible?"check:"+Evidence(p):null;}
        public static bool ManualNeeded(LedgerMarkerModel m,LedgerMarkerPurpose p)
        {var a=ManualActivation(m,p);return a!=null&&a!=p.AcknowledgedManual;}
        public static bool Needed(LedgerMarkerModel m,LedgerMarkerPurpose p)
        {var a=AutomaticActivation(p);return ManualNeeded(m,p)||a!=null&&a!=p.AcknowledgedAuto;}
        public static void Acknowledge(LedgerMarkerModel m,LedgerMarkerDestination d)
        {foreach(var p in d.Purposes.Where(p=>!p.Service)){AcknowledgePurpose(m,p);}m.Revision++;}
        public static void AcknowledgePurpose(LedgerMarkerModel m,LedgerMarkerPurpose p)
        {var a=ManualActivation(m,p);if(a!=null)p.AcknowledgedManual=a;a=AutomaticActivation(p);if(a!=null)p.AcknowledgedAuto=a;}
        public static void Mode(LedgerMarkerModel m,LedgerMarkerRoute r,string mode)
        {
            if(mode!="show"&&mode!="hide"&&mode!="auto")throw new ArgumentException("route mode");
            if(r.Mode==mode)return;r.Mode=mode;r.Shown=mode=="show";r.ShowRun++;
            if(mode=="auto")foreach(var p in m.Data.Destinations.SelectMany(d=>d.Purposes).Where(p=>p.Kind=="offer"&&p.Routes.Contains(r.Id))){p.TrackUpdates=true;p.Visibility=0;}
            m.Revision++;
        }
        public static void Evaluate(LedgerMarkerModel m,LedgerContext c,int player,ulong? now,LedgerProductionPlaces places)
        {
            foreach(var p in m.Data.Destinations.SelectMany(d=>d.Purposes)){p.FlowVisible=false;p.FlowStage=p.FlowToken=null;}
            foreach(var p in m.Data.Destinations.SelectMany(d=>d.Purposes))if(m.Data.Routes.Any(r=>r.Mode=="auto"&&p.Routes.Contains(r.Id)))p.UpdateAutoVisible=false;
            foreach(var r in m.Data.Routes.Where(r=>r.Mode=="auto"&&c!=null&&r.World==c.WorldKey&&r.Player==player))
            {
                var pairs=m.Data.Destinations.Where(d=>d.World==r.World&&d.Player==player&&!d.DeleteRequested).SelectMany(d=>d.Purposes.Where(p=>p.Kind=="offer"&&p.Routes.Contains(r.Id)&&p.TrackUpdates).Select(p=>Tuple.Create(d,p))).ToList();
                foreach(var group in pairs.GroupBy(x=>Item(x.Item2)))
                {
                    var buys=group.Where(x=>x.Item2.Plan.PlayerBuys).ToList();var sells=group.Where(x=>!x.Item2.Plan.PlayerBuys).ToList();
                    var load=r.Loads.FirstOrDefault(x=>x.Item==group.Key);long cargo=load==null?0:load.Quantity;
                    if(cargo>0){foreach(var x in sells)if(!Fresh(x.Item2,c,now)||Quantity(x.Item2)!=0)Set(m,x,r,Fresh(x.Item2,c,now)&&Quantity(x.Item2)>0?"Sell":"Check",cargo.ToString(),places);continue;}
                    // A standalone shopping route has no artificial sale prerequisite.
                    bool demand=sells.Count==0||sells.Any(x=>Fresh(x.Item2,c,now)&&Quantity(x.Item2)>0);
                    if(!demand){foreach(var x in sells)if(!Fresh(x.Item2,c,now)||!Quantity(x.Item2).HasValue)Set(m,x,r,"Check",CheckToken(x.Item2,c,now),places);continue;}
                    foreach(var x in buys){var p=x.Item2;if(Fresh(p,c,now)&&Quantity(p)>0)Set(m,x,r,"Buy",Evidence(p),places);else if(!Fresh(p,c,now)||!Quantity(p).HasValue)Set(m,x,r,"Check",CheckToken(p,c,now),places);}
                }
            }
        }
        private static void Set(LedgerMarkerModel m,Tuple<LedgerMarkerDestination,LedgerMarkerPurpose> x,LedgerMarkerRoute r,string stage,string token,LedgerProductionPlaces places)
        {var p=x.Item2;if(p.Consumed||LedgerTradeUpdates.Suppressed(m,x.Item1,p,places)||stage=="Check"&&!LedgerTradeUpdates.Forced(m,p))return;p.FlowVisible=true;if(p.FlowStage==null||stage=="Sell"){p.FlowStage=stage;p.FlowToken=r.Id+":"+r.ShowRun+":"+token;}}
        public static void Trade(LedgerMarkerModel m,LedgerTradeReceipt t)
        {
            if(t==null||!t.Confirmed||t.Quantity<=0||t.Context==null||!t.Context.Recordable||t.Identity==null||!(t.Identity.NpcEntityId>0)||t.TradeId==null)return;
            if(m.Data.RouteEvents.Contains(t.TradeId))return;
            var d=m.Data.Destinations.FirstOrDefault(x=>x.World==t.Context.WorldKey&&x.Player==t.PlayerId&&x.Playfield==t.Context.Playfield&&x.Npc==t.Identity.NpcEntityId&&(!x.Structure.HasValue||!t.Identity.StructureEntityId.HasValue||x.Structure==t.Identity.StructureEntityId));if(d==null)return;
            bool buy=t.Action=="Bought";if(!buy&&t.Action!="Sold")return;
            var p=d.Purposes.FirstOrDefault(x=>x.Kind=="offer"&&x.Plan.ItemId==t.ItemId&&x.Plan.Auxiliary==t.Auxiliary&&x.Plan.PlayerBuys==buy);if(p==null)return;
            // Overlapping Auto goals do not each claim the same purchase. Explicit deterministic owner.
            var routes=m.Data.Routes.Where(r=>r.Mode=="auto"&&r.World==d.World&&r.Player==d.Player&&p.Routes.Contains(r.Id)).OrderBy(r=>r.Id).ToList();
            long left=t.Quantity;foreach(var r in routes){var l=r.Loads.FirstOrDefault(x=>x.Item==Item(p));if(l==null){l=new LedgerRouteLoad{Item=Item(p)};r.Loads.Add(l);}long amount=buy?left:Math.Min(left,l.Quantity);l.Quantity=buy?Math.Min(1000000000L,l.Quantity+amount):l.Quantity-amount;left-=amount;if(left==0)break;}
            long? quantity=Quantity(p);if(quantity.HasValue&&t.Utc!=null&&string.CompareOrdinal(p.Plan.ObservedUtc,t.Utc)<=0)p.Plan.Quantity=Math.Max(0,quantity.Value-t.Quantity).ToString(CultureInfo.InvariantCulture);
            AcknowledgePurpose(m,p);if(!m.Routed(p)&&!p.Service)m.Consume(p);if(m.Data.RouteEvents.Count>=256)m.Data.RouteEvents.RemoveAt(0);m.Data.RouteEvents.Add(t.TradeId);m.Revision++;
        }
    }
}
