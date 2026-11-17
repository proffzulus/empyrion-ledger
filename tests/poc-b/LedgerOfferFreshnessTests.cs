using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal static class LedgerOfferFreshnessTests
    {
        private static T Copy<T>(T value) { return LedgerJson.Decode<T>(LedgerJson.Encode(value)); }
        private static LedgerObservation Market(string save, int npc = 3002, ulong tick = 10000, ulong deadline = 12400)
        {
            var visible = new PublishedOffer { Name = "Owned fixture ore", StockText = "2", CapacityText = "10", BuyPriceText = "155", SellPriceText = "150", SellAvailable = true, BuyAvailable = true, BuySupported = true };
            var offer = LedgerOffer.Capture(0, visible, new OfferBinding { State = "bound-current-delegate", Item = 401, Auxiliary = 0, BaseBuy = 155, BaseSell = 150 });
            return new LedgerObservation { Kind = "market", Utc = "2026-10-31T00:00:00.0000000Z", GameTicks = tick.ToString(CultureInfo.InvariantCulture), Session = "owned-freshness-fixture", FullCurrentAssortment = true,
                Context = LedgerContext.Create(save, "Observed fixture planet", "Fixture system", "SinglePlayer"), Identity = new LedgerIdentity { NpcEntityId = npc, StructureEntityId = 1065, StructureName = "Fixture station", BlockGrid = npc + ",2,3" },
                Offers = new List<LedgerOffer> { offer }, OfferFreshness = new LedgerOfferFreshness { RestockMinutes = 2, NextRefreshTick = deadline, ObservationGameTick = tick, State = "observed-current-model", Source = LedgerFreshness.Source } };
        }
        internal static void Run(Action<bool, string> check, string root)
        {
            string save = Path.Combine(root, "owned-freshness-save"); var m = Market(save); var context = m.Context;
            check(LedgerFreshness.Evaluate(m, context, 10000) == LedgerOfferAge.BeforeDeadline, "freshness uses actual observed deadline before boundary");
            check(LedgerFreshness.Evaluate(m, context, 12399) == LedgerOfferAge.BeforeDeadline && LedgerFreshness.Evaluate(m, context, 12400) == LedgerOfferAge.Recheck, "freshness conservative exact-boundary hint precedes native strict greater-than reroll");
            check(LedgerFreshness.Evaluate(m, context, 12401) == LedgerOfferAge.Recheck, "freshness eligibility does not remotely refresh offers");
            string immutable = LedgerJson.Hash(LedgerJson.Encode(m));
            for (ulong t = 10000; t <= 30000; t += 1000) LedgerFreshness.Hint(m, context, t);
            check(immutable == LedgerJson.Hash(LedgerJson.Encode(m)), "freshness time/hover evaluations never mutate observed stock price quantity or metadata");
            var sale = Copy(m); sale.Offers[0].Stock = 10; sale.Offers[0].RemainingNpcBuyingCapacity = 0; sale.Offers[0].NpcBuysAvailable = false;
            sale.Offers[0].Visible.StockText = "10"; sale.Offers[0].Visible.BuyAvailable = false;
            LedgerFreshness.StampObservation(sale, () => 11000);
            check(m.Offers[0].RemainingNpcBuyingCapacity == 8 && sale.Offers[0].RemainingNpcBuyingCapacity == 0 && sale.OfferFreshness.NextRefreshTick == 12400, "freshness own sale demand8 to0 retains actual native deadline rather than scheduling from trade");
            check(LedgerFreshness.Evaluate(sale, context, 12400) == LedgerOfferAge.Recheck && sale.Offers[0].StoredBuyUnit == "155", "freshness own trade cannot postpone eligibility or erase zero/stored price");
            var reopened = Copy(sale); reopened.ObservationId = Guid.NewGuid().ToString("N"); reopened.OfferFreshness.NextRefreshTick = 15400; LedgerFreshness.StampObservation(reopened, () => 13000);
            check(LedgerFreshness.Evaluate(reopened, context, 13000) == LedgerOfferAge.BeforeDeadline && reopened.Offers[0].RemainingNpcBuyingCapacity == 0, "freshness overdue reopen with unchanged values and new ACTUAL deadline clears recheck");
            var stable = new TraderSnapshotStability();
            string first = "unchanged-offers|" + sale.OfferFreshness.StabilityKey();
            check(stable.Sample(first, false) == TraderReadResult.WaitingStable && stable.Sample(first, false) == TraderReadResult.Captured, "freshness requires two stable timer and offer samples");
            var timeOnly = Copy(sale); LedgerFreshness.StampObservation(timeOnly, () => 11500);
            check(first == "unchanged-offers|" + timeOnly.OfferFreshness.StabilityKey() && stable.Sample(first, false) == TraderReadResult.Unchanged, "freshness advancing clock alone cannot create250ms writes or reset eligibility");
            string next = "unchanged-offers|" + reopened.OfferFreshness.StabilityKey();
            check(stable.Sample(next, false) == TraderReadResult.WaitingStable && stable.Sample(next, false) == TraderReadResult.Captured, "freshness new deadline schedules capture even when all offer values repeat");
            check(LedgerFreshness.Evaluate(m, context, 1000000) == LedgerOfferAge.Recheck && LedgerFreshness.Evaluate(m, context, 9999) == LedgerOfferAge.Unknown, "freshness game-time skip reaches eligibility while rollback is unknown without unsigned underflow");
            check(LedgerFreshness.Evaluate(m, context, null) == LedgerOfferAge.Unknown, "freshness absent current clock never uses UTC or uptime");
            var quote = Copy(m); quote.Snapshot = "different-selection-or-reputation-quote"; quote.Utc = "2026-10-31T00:10:00.0000000Z";
            check(quote.OfferFreshness.StabilityKey() == m.OfferFreshness.StabilityKey() && LedgerFreshness.Evaluate(quote, context, 12399) == LedgerOfferAge.BeforeDeadline, "freshness quote/reputation identity and UTC alone do not establish or reset a schedule");
            foreach (string fault in new[] { "legacy", "missing-interval", "missing-deadline", "negative", "range", "zero-deadline", "future-too-far", "future-overflow", "source", "state", "missing-observed", "stamp-conflict", "partial" })
            {
                var bad = Copy(m);
                switch (fault)
                {
                    case "legacy": bad.OfferFreshness = null; break;
                    case "missing-interval": bad.OfferFreshness.RestockMinutes = null; break;
                    case "missing-deadline": bad.OfferFreshness.NextRefreshTick = null; break;
                    case "negative": bad.OfferFreshness.RestockMinutes = -1; break;
                    case "range": bad.OfferFreshness.RestockMinutes = 65536; break;
                    case "zero-deadline": bad.OfferFreshness.NextRefreshTick = 0; break;
                    case "future-too-far": bad.OfferFreshness.NextRefreshTick = 12401; break;
                    case "future-overflow": bad.OfferFreshness.NextRefreshTick = ulong.MaxValue; break;
                    case "source": bad.OfferFreshness.Source = "prefab guessed120"; break;
                    case "state": bad.OfferFreshness.State = "schedule-changed-during-read"; break;
                    case "missing-observed": bad.OfferFreshness.ObservationGameTick = null; break;
                    case "stamp-conflict": bad.GameTicks = "9000"; break;
                    case "partial": bad.FullCurrentAssortment = false; break;
                }
                check(LedgerFreshness.Evaluate(bad, context, 20000) == LedgerOfferAge.Unknown && bad.Offers[0].RemainingNpcBuyingCapacity == 8, "freshness invalid/legacy evidence remains unknown without false zero or lost offers: " + fault);
            }
            var disabled = Copy(m); disabled.OfferFreshness.RestockMinutes = 0; disabled.OfferFreshness.NextRefreshTick = 0;
            check(LedgerFreshness.Evaluate(disabled, context, 1000000) == LedgerOfferAge.Disabled, "freshness observed zero interval is explicitly no scheduled refresh, not fabricated fresh/stale");
            var foreign = LedgerContext.Create(save + "-independent", context.Playfield, null, "SinglePlayer");
            check(LedgerFreshness.Evaluate(m, foreign, 10000) == LedgerOfferAge.Unknown, "freshness world isolation rejects another save clock with identical NPC and playfield");
            var failedClock = Copy(m); LedgerFreshness.StampObservation(failedClock, () => { throw new IOException("fixture clock failure"); });
            check(LedgerFreshness.Evaluate(failedClock, context, 11000) == LedgerOfferAge.Unknown && failedClock.Offers.Count == 1, "freshness optional clock failure preserves normal offers and yields unknown");
            var paired = new LedgerComparisonRow { Source = new LedgerViewOffer { Market = m }, Destination = new LedgerViewOffer { Market = reopened }, UnitDelta = 5, TotalDelta = 40 };
            check(LedgerFreshness.Evaluate(paired.Source.Market, context, 14000) == LedgerOfferAge.Recheck && LedgerFreshness.Evaluate(paired.Destination.Market, context, 14000) == LedgerOfferAge.BeforeDeadline && paired.TotalDelta == 40, "freshness comparison source/destination independently age without changing signed differences");
            var legacyJson = LedgerJson.Encode(new LedgerObservation { Kind = "market", Offers = m.Offers });
            check(LedgerJson.Decode<LedgerObservation>(legacyJson).OfferFreshness == null, "freshness legacy JSON optional member defaults unknown");
            Persistence(check, root, m, sale, reopened, foreign);
            check(LedgerFreshness.Badge(LedgerOfferAge.Recheck) == "recheck" && LedgerFreshness.Badge(LedgerOfferAge.Unknown) == "" && LedgerFreshness.BadgeWidth(LedgerOfferAge.Unknown) == 0 && LedgerFreshness.Background(LedgerOfferAge.Unknown) == 0 && LedgerFreshness.Hint(new LedgerObservation(), context, 14000) == "" && LedgerFreshness.Badge(LedgerOfferAge.BeforeDeadline) == "", "freshness known overdue badge remains while unknown timing is silent without declaring freshness");
            check(LedgerFreshness.Background(LedgerOfferAge.BeforeDeadline) == 0 && LedgerFreshness.Background(LedgerOfferAge.Recheck) != LedgerFreshness.Background(LedgerOfferAge.Unknown), "freshness neutral backplates are distinct from default and do not change margin/direction colors");
            string previews = Path.Combine(root, "offer-freshness-previews"); Directory.CreateDirectory(previews);
            foreach (int width in new[] { 1920, 3840 }) foreach (string view in new[] { "markets", "locations", "comparison" })
            { Preview(previews, width, view, m, reopened); check(File.Exists(Path.Combine(previews, view + "-" + width + ".png")), "freshness synthetic layout rendered " + width + " " + view + " mixed known/unknown"); }
        }
        private static void Persistence(Action<bool, string> check, string root, LedgerObservation m, LedgerObservation sale, LedgerObservation reopened, LedgerContext foreign)
        {
            string folder = Path.Combine(root, "freshness-store"); var key = LedgerDatabase.Key(m);
            using (var db = new LedgerDatabase(folder))
            {
                check(db.Save(m) == "committed", "freshness actual SQLite stores offers and optional metadata atomically");
                sale.Utc = "2026-10-31T00:01:00.0000000Z"; sale.ObservationId = Guid.NewGuid().ToString("N");
                check(db.Save(sale) == "committed" && db.ReadMarket(key).Offers[0].RemainingNpcBuyingCapacity == 0 && db.ReadMarket(key).OfferFreshness.NextRefreshTick == 12400, "freshness SQLite demand8 to0 update keeps zero and eligibility");
                var repeat = Copy(sale); repeat.ObservationId = Guid.NewGuid().ToString("N"); repeat.Utc = "2026-10-31T00:02:00.0000000Z"; LedgerFreshness.StampObservation(repeat, () => 12000);
                check(db.Save(repeat) == "committed" && db.ReadMarket(key).ObservationId == repeat.ObservationId && db.ReadMarket(key).Utc == repeat.Utc && db.ReadMarket(key).OfferFreshness.NextRefreshTick == 12400, "freshness repeated unchanged visit records actual observation time without resetting deadline");
            }
            string older = Path.Combine(root, "freshness-older-copy"); Directory.CreateDirectory(older);
            File.Copy(Path.Combine(folder, LedgerDatabase.FileName), Path.Combine(older, LedgerDatabase.FileName));
            using (var db = new LedgerDatabase(folder))
            {
                reopened.Utc = "2026-10-31T00:03:00.0000000Z";
                check(db.Save(reopened) == "committed" && db.ReadMarket(key).OfferFreshness.NextRefreshTick == 15400, "freshness later actual deadline replaces metadata with same offer values");
                var partial = Copy(reopened); partial.FullCurrentAssortment = false; partial.OfferFreshness.NextRefreshTick = 999999;
                check(db.Save(partial) == "partial" && db.ReadMarket(key).OfferFreshness.NextRefreshTick == 15400, "freshness partial capture cannot erase valid prior offers or schedule");
                var other = Copy(m); other.Context = foreign; check(db.Save(other) == "committed" && db.EndpointCount == 2, "freshness independent world retains separate schedule and observations");
            }
            using (var db = new LedgerDatabase(folder)) check(LedgerFreshness.Evaluate(db.ReadMarket(key), m.Context, 14000) == LedgerOfferAge.BeforeDeadline, "freshness restart restores committed actual schedule");
            using (var db = new LedgerDatabase(older)) check(db.ReadMarket(key).OfferFreshness.NextRefreshTick == 12400 && LedgerFreshness.Evaluate(db.ReadMarket(key), m.Context, 12000) == LedgerOfferAge.BeforeDeadline, "freshness relocated older copy retains own clock/schedule without future external metadata");
            bool fail = true;
            using (var db = new LedgerDatabase(folder, null, stage => { if (stage == "before-commit" && fail) throw new IOException("fixture write failure"); }))
            {
                bool threw = false; try { var next = Copy(reopened); next.Utc = "2026-10-31T00:04:00.0000000Z"; next.OfferFreshness.NextRefreshTick = 17800; db.Save(next); } catch (IOException) { threw = true; }
                check(threw && db.ReadMarket(key).OfferFreshness.NextRefreshTick == 15400 && db.ReadMarket(key).Offers[0].RemainingNpcBuyingCapacity == 0, "freshness failed write rolls metadata and offers back together"); fail = false;
            }
        }
        private static void Preview(string directory, int width, string view, LedgerObservation first, LedgerObservation second)
        {
            float scale = LedgerLayout.For(width,width*9/16).Scale; int height = width * 9 / 16;
            bool comparison=view=="comparison",location=view=="locations";
            float[] columns=comparison?new[]{.18f,.18f,.065f,.065f,.18f,.065f,.065f,.07f,.065f,.065f}:location?new[]{.29f,.09f,.09f,.22f,.23f,.08f}:new[]{.12f,.12f,.26f,.30f,.12f,.08f};
            string[] headers=comparison?new[]{"Item","Buy at A","Price","Stock","Sell at B","Price","Demand","Transfer","Delta/unit","Delta total"}:location?new[]{"Item","Unit price","Demand","Vendor","Place",""}:new[]{"Unit price","Demand","Vendor","Place","Distance",""};
            using (var bitmap = new Bitmap(width, height)) using (var g = Graphics.FromImage(bitmap)) using (var font = new Font("Segoe UI", 15, GraphicsUnit.Pixel))
            {
                g.ScaleTransform(scale, scale); g.Clear(Color.FromArgb(30, 65, 82));
                using (var panel = new SolidBrush(Color.FromArgb(222, 8, 30, 48))) g.FillRectangle(panel, 16, 16, 1888, 1048);
                g.DrawString("Trade Ledger   Markets   Locations   Comparison   NPC Requests   Production   Item Browser   Markers", font, Brushes.LightBlue, 30, 30);
                g.DrawString(comparison ? "Buy from A [POI]  Fixture station                         Sell to B [POI]  Another fixture station" : "Item search [Ore]                                  Sellers - buy here / Buyers - sell here", font, Brushes.White, 330, 105);
                float hx=330;for(int j=0;j<headers.Length;j++){g.DrawString(headers[j],font,Brushes.LightBlue,hx+5,160);hx+=columns[j]*1530;}
                for (int i = 0; i < 15; i++)
                {
                    var a = Copy(i % 3 == 0 ? first : second); if (i % 3 == 2) a.OfferFreshness = null;
                    var b = Copy(i % 2 == 0 ? second : first); int rowHeight=comparison?LedgerLayout.PairRow:LedgerLayout.TableRow;int y=193+i*(rowHeight+2);
                    using (var row = new SolidBrush(Color.FromArgb(13, 34, 51))) g.FillRectangle(row, 330, y, 1530, rowHeight);
                    float ax=330+1530*columns[0],bx=330+1530*(columns[0]+columns[1]+columns[2]+columns[3]);
                    PreviewShade(g,comparison?ax:330,y,1530*(comparison?columns[1]+columns[2]+columns[3]:location?.92f:.8f),rowHeight,a);
                    if(comparison)PreviewShade(g,bx,y,1530*(columns[4]+columns[5]+columns[6]),rowHeight,b);
                    var values=comparison?new[]{"Fixture ore","Source vendor\nStation A / Planet","150","2","Buyer vendor\nStation B / Planet","155","0","0",i%2==0?"+5":"-5","0"}:location?new[]{"Fixture ore","155","0","Fixture vendor","Fixture station / Planet","Mark"}:new[]{"155","0","Fixture vendor","Fixture station / Planet","unknown","Mark"};
                    float x=330;
                    for(int j=0;j<values.Length;j++){
                        float w=1530*columns[j];bool vendor=comparison?(j==1||j==4):j==(location?3:2);
                        var market=comparison&&j>=4&&j<=6?b:a;var state=LedgerFreshness.Evaluate(market,market.Context,14000);float reserve=vendor?LedgerFreshness.BadgeWidth(state):0;
                        float usable=w-10-(comparison&&vendor?66:0);var brush=comparison&&j==8?(i%2==0?Brushes.LightBlue:Brushes.LightCoral):Brushes.White;
                        uint textColor=LedgerFreshness.TextColor(state);using(var fresh=new SolidBrush(Color.FromArgb(unchecked((int)(textColor==0?0xFFFFFFFFu:textColor)))))using(var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=comparison&&vendor?0:StringFormatFlags.NoWrap})g.DrawString(values[j],font,textColor!=0&&(comparison?j>=1&&j<=6:j<values.Length-1)?fresh:brush,new RectangleF(x+5,y+3,Math.Max(1,usable-reserve),rowHeight-4),format);
                        if(vendor&&reserve>0)g.DrawString(LedgerFreshness.Badge(state),font,Brushes.WhiteSmoke,x+5+usable-reserve,y+3);
                        if(comparison&&vendor)g.DrawString("Mark",font,Brushes.LightBlue,x+w-56,y+3);x+=w;
                    }
                }
                g.DrawString("NON-GAME PREVIEW / synthetic data / shared freshness policy; native Unity rendering still requires owner playtest", font, Brushes.LightBlue, 330, 1025);
                bitmap.Save(Path.Combine(directory, view + "-" + width + ".png"), ImageFormat.Png);
            }
        }
        private static void PreviewShade(Graphics g, float x, int y, float width, int height, LedgerObservation market)
        {
            var state = LedgerFreshness.Evaluate(market, market.Context, 14000); uint argb = LedgerFreshness.Background(state);
            if (argb != 0) using (var brush = new SolidBrush(Color.FromArgb(unchecked((int)argb)))) g.FillRectangle(brush, x, y, width, height);
        }
    }
}
