using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeLedgerProbeB
{
    // Projection of personally observed timers on EXISTING purposes only. No native reads/creation,
    // stock refresh, logger dependency, wall-clock eligibility or financial accounting.
    public static class LedgerTradeUpdates
    {
        public static string RouteKey(string route) { return "route:" + route; }
        public static string RoutePolicyKey(LedgerMarkerRoute route) { return route.ServiceKey ?? RouteKey(route.Id); }
        public static LedgerObservation Evidence(LedgerObservation market)
        {
            if (market == null || market.Kind != "market" || market.Context == null || market.FullCurrentAssortment != true) return null;
            // Keep only bounded scheduling proof, not another full assortment or live object.
            return new LedgerObservation { Kind = "market", Utc = market.Utc, GameTicks = market.GameTicks,
                FullCurrentAssortment = true, OfferFreshness = market.OfferFreshness == null ? null :
                    LedgerJson.Decode<LedgerOfferFreshness>(LedgerJson.Encode(market.OfferFreshness)),
                Context = new LedgerContext { WorldKey = market.Context.WorldKey, Playfield = market.Context.Playfield,
                    Mode = market.Context.Mode, Recordable = market.Context.Recordable, SolarSystem = market.Context.SolarSystem,
                    Geography = market.Context.Geography == null ? null : LedgerJson.Decode<LedgerGeography>(LedgerJson.Encode(market.Context.Geography)) } };
        }
        public static bool Persistent(LedgerMarkerModel model, LedgerMarkerDestination d)
        { return d.Pinned || model.Grouped(d); }
        public static bool Forced(LedgerMarkerModel model,LedgerMarkerPurpose p)
        {return p.ForceNotify||model.Data.Routes.Any(r=>r.ForceNotify&&p.Routes.Contains(r.Id));}
        public static bool Ready(LedgerMarkerPurpose p){return !p.Consumed&&p.Kind=="offer"&&p.TrackUpdates&&p.UpdateAge==LedgerOfferAge.Recheck;}
        public static string Attention(LedgerMarkerPurpose p){return Ready(p)?"Ready":p.TrackUpdates&&p.UpdateMinutes.HasValue?p.UpdateMinutes+"m":"";}
        public static void Track(LedgerMarkerModel model, LedgerMarkerDestination d, LedgerMarkerPurpose p, bool enabled)
        {
            if (p.Kind != "offer" || p.Service || !d.Purposes.Contains(p) || enabled && !Persistent(model, d)) return;
            p.TrackUpdates = enabled; if (!enabled) p.UpdateAutoVisible = false; model.Revision++;
        }
        public static bool Suppressed(LedgerMarkerModel model, LedgerMarkerDestination d, LedgerMarkerPurpose p, LedgerProductionPlaces places)
        {
            var prefs = LedgerProductionServices.Preferences(model, d.World, d.Player, false);
            if (prefs == null) return false;
            var ancestry = places.Ancestry("playfield:" + d.Playfield).Concat(new[] { "playfield:" + d.Playfield }).Distinct().ToArray();
            var routes = model.Data.Routes.Where(r => r.World == d.World && r.Player == d.Player && p.Routes.Contains(r.Id)).ToList();
            // A shared purpose can be required by several independently unsuppressed routes.
            // Location/root suppression still wins over every child, exactly as service alerts.
            return routes.Count == 0 ? prefs.Effective(ancestry).Suppressed :
                routes.All(r => prefs.Effective(ancestry, RoutePolicyKey(r)).Suppressed);
        }
        public static void Evaluate(LedgerMarkerModel model, LedgerContext current, int player, ulong? now, LedgerProductionPlaces places = null)
        {
            var contexts = model.Data.Destinations.Where(d => current != null && d.World == current.WorldKey && d.Player == player)
                .SelectMany(d => d.Purposes).Where(p => p.Plan.RefreshObservation != null).Select(p => p.Plan.RefreshObservation.Context);
            var observedPlaces = new LedgerProductionPlaces(current == null ? "" : current.WorldKey, contexts);
            model.ReadyRoutes.Clear();
            foreach (var d in model.Data.Destinations)
            foreach (var p in d.Purposes)
            {
                p.UpdateAge = LedgerOfferAge.Unknown;p.UpdateMinutes=null;
                bool eligible = false;
                // Display age is useful for manually shown offers too; this never enables
                // tracking, changes stock, or supplies an automatic visibility reason.
                var evidence = p.Plan.RefreshObservation;
                if(current!=null&&current.Recordable&&d.World==current.WorldKey&&d.Player==player&&
                    p.Kind=="offer"&&!p.Service&&evidence!=null&&evidence.Context!=null&&evidence.Context.Playfield==d.Playfield)
                    {p.UpdateAge=LedgerFreshness.Evaluate(evidence,current,now);if(p.UpdateAge==LedgerOfferAge.BeforeDeadline)p.UpdateMinutes=(long)Math.Ceiling((evidence.OfferFreshness.NextRefreshTick.Value-now.Value)/1200d);}
                if (current != null && current.Recordable && d.World == current.WorldKey && d.Player == player &&
                    p.Kind == "offer" && !p.Service && !p.Consumed && p.TrackUpdates && Forced(model,p) && !d.DeleteRequested && Persistent(model, d))
                {
                    // New market evidence can precede the asynchronous shared-place cache reload.
                    var hierarchy = places != null && places.Resolve("playfield:" + d.Playfield) != places.All ? places : observedPlaces;
                    eligible = p.UpdateAge == LedgerOfferAge.Recheck && !Suppressed(model, d, p, hierarchy);
                }
                // Runtime-only reasons are recomputed after restart. Never arm Restore/CreateRequested.
                p.UpdateAutoVisible = eligible;
                if(Ready(p))foreach(string route in p.Routes)model.ReadyRoutes.Add(route);
            }
            LedgerRouteFlow.Evaluate(model,current,player,now,places??observedPlaces);
        }
    }
}
