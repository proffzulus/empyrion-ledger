using System;
using System.Globalization;

namespace TradeLedgerProbeB
{
    // Optional observed scheduling evidence, never a replacement for factual offers.
    public sealed class LedgerOfferFreshness
    {
        public int? RestockMinutes;
        public ulong? NextRefreshTick, ObservationGameTick;
        public string State, Source;
        public string StabilityKey()
        {
            // Advancing time must not produce a capture/write on every poll.
            return (State ?? "unknown") + ":" + RestockMinutes + ":" + NextRefreshTick;
        }
    }

    public enum LedgerOfferAge { Unknown, BeforeDeadline, Recheck, Disabled }

    public static class LedgerFreshness
    {
        public const string Source = "5188 current admitted BE30/ConnectionToken.0400531F UInt16 minutes +04005324 UInt64 deadline;IApplication.GameTicks/20";
        public static bool Valid(LedgerOfferFreshness f)
        {
            if (f == null || f.State != "observed-current-model" || f.Source != Source ||
                !f.RestockMinutes.HasValue || f.RestockMinutes < 0 || f.RestockMinutes > ushort.MaxValue ||
                !f.NextRefreshTick.HasValue || !f.ObservationGameTick.HasValue || f.ObservationGameTick == 0) return false;
            if (f.RestockMinutes == 0) return true; // Native explicitly disables timed rerolls after initialization.
            ulong period = (ulong)f.RestockMinutes.Value * 1200;
            return f.NextRefreshTick > 0 && (f.NextRefreshTick <= f.ObservationGameTick ||
                f.NextRefreshTick.Value - f.ObservationGameTick.Value <= period);
        }
        public static void StampObservation(LedgerObservation o, Func<ulong?> clock)
        {
            if (o == null || o.Kind != "market" || o.OfferFreshness == null) return;
            ulong? tick = null;
            try { if (clock != null) tick = clock(); } catch (Exception) { /* Optional clock failure cannot drop offers. */ }
            o.OfferFreshness.ObservationGameTick = tick;
            if (tick.HasValue) o.GameTicks = tick.Value.ToString(CultureInfo.InvariantCulture);
        }
        public static LedgerOfferAge Evaluate(LedgerObservation market, LedgerContext current, ulong? now)
        {
            var f = market == null ? null : market.OfferFreshness;
            ulong stamped;
            if (current == null || !current.Recordable || string.IsNullOrEmpty(current.WorldKey) || market == null || market.Kind != "market" ||
                market.Context == null || market.Context.WorldKey != current.WorldKey || current.Mode != "SinglePlayer" ||
                market.Context.Mode != current.Mode || market.FullCurrentAssortment != true || !Valid(f) || !now.HasValue ||
                !ulong.TryParse(market.GameTicks, NumberStyles.None, CultureInfo.InvariantCulture, out stamped) ||
                stamped != f.ObservationGameTick || now < f.ObservationGameTick) return LedgerOfferAge.Unknown;
            if (f.RestockMinutes == 0) return LedgerOfferAge.Disabled;
            // At the boundary show the conservative recheck hint; native regeneration requires strictly >.
            return now >= f.NextRefreshTick ? LedgerOfferAge.Recheck : LedgerOfferAge.BeforeDeadline;
        }
        public static string Badge(LedgerOfferAge state)
        { return state == LedgerOfferAge.Recheck ? "recheck" : ""; }
        public static int BadgeWidth(LedgerOfferAge state)
        { return state == LedgerOfferAge.Recheck ? 60 : 0; }
        public static uint Background(LedgerOfferAge state)
        { return state == LedgerOfferAge.Recheck ? 0xD94A505Au : 0u; }
        public static uint TextColor(LedgerOfferAge state)
        { return state == LedgerOfferAge.BeforeDeadline ? 0xFF8EE6A1u : 0u; }
        public static string Hint(LedgerObservation market, LedgerContext current, ulong? now)
        {
            var state = Evaluate(market, current, now);
            if (state == LedgerOfferAge.Recheck) return "Recheck on next visit";
            if (state != LedgerOfferAge.BeforeDeadline) return "";
            double minutes=Math.Ceiling((market.OfferFreshness.NextRefreshTick.Value-now.Value)/1200.0);
            return "Refresh in "+minutes.ToString(CultureInfo.InvariantCulture)+(minutes==1?" minute":" minutes");
        }
    }
}
