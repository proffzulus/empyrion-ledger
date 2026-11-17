using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace TradeLedgerProbeB
{
    // Shared runtime reader and owned-fixture tests. Reads pinned fields only; never calls a game delegate/getter.
    public interface IPassiveFields
    {
        object Read(int tokenRow, object instance);
        bool Exact(object value, string type);
        bool Supports(object value, string type);
        bool ListType(int tokenRow, object value);
        bool Callback(Delegate value);
    }

    public sealed class OfferFrame
    {
        public bool Admitted;
        public bool ComparisonComplete;
        public object Controller, Model, ListOwner;
        public IList Rows, Raw;
        public int RowCount, RawCount;
        public Func<bool> Lease;
        public string State;
        public LedgerOfferFreshness Freshness;
    }

    public sealed class PublishedOffer
    {
        public string Name, Sprite, StockText, CapacityText, SellPriceText, BuyPriceText;
        public bool SellAvailable, BuyAvailable, BuySupported;
        public int Auxiliary;
        public string SemanticValue()
        {
            // Length-prefixed values: duplicates and null are distinct; no object IDs/selection/ordinal heuristic keys.
            var parts = new List<string>();
            foreach (string s in new[] { Name, Sprite, StockText, CapacityText, SellPriceText, BuyPriceText })
                parts.Add(s == null ? "null:" : s.Length.ToString(CultureInfo.InvariantCulture) + ":" + s.Substring(0, Math.Min(240, s.Length)));
            return string.Join("|", parts) + "|" + SellAvailable + "|" + BuyAvailable + "|" + BuySupported + "|" + Auxiliary;
        }
    }

    public sealed class OfferBinding
    {
        public string State = "unbound", Reason;
        public int? Index, Item, Auxiliary, Stock, Capacity, BaseSell, BaseBuy;
        public object Entry, Closure;
        public bool Bound { get { return State == "bound-current-delegate"; } }
    }

    public struct BlockPoint
    {
        public readonly int X, Y, Z;
        public BlockPoint(int x, int y, int z) { X = x; Y = y; Z = z; }
        public bool Same(BlockPoint p) { return X == p.X && Y == p.Y && Z == p.Z; }
        public override string ToString() { return X.ToString(CultureInfo.InvariantCulture) + "," + Y.ToString(CultureInfo.InvariantCulture) + "," + Z.ToString(CultureInfo.InvariantCulture); }
    }

    public sealed class TraderIdentity
    {
        public string State, OffsetState = "missing", ChunkState = "missing", OwnerState = "missing", StructureState = "missing", GridState = "unknown";
        public int? CachedNpcRaw, CachedNpcId, EntityRouteRaw, ClrIdx, ClusterIdx, StructureEntityId, StructureEntityRaw;
        public object Chunk, Owner, Structure;
        public BlockPoint? Offset, ChunkCoordinates, BlockGrid;
        public bool Current, OwnerConsistent;
    }

    public sealed class DialogueIdentity
    {
        public string State, ReverseState = "unsupported-actual-type";
        public bool Current;
        public object Npc;
        public int? NpcId, NpcRaw, ReverseStructureId, ReverseStructureRaw;
        public BlockPoint? ReverseBlock;
    }

    // Current1.20 native identities are independently checked against field owners,
    // row construction and selection-delegate IL by the build verifier.
    public static class LedgerTraderTypes
    {
        public const string Controller="Assembly-CSharp.PcitureEditorConnectionToken";
        public const string Bag="Assembly-CSharp.FormSiteConnectionToken";
        public const string Row=Controller+"+DeviceEventArgsConnectionToken";
        public const string Closure=Controller+"+GroupProviderConnectionToken";
        public const string Model="Assembly-CSharp.ActionEditor";
        public const string Entry=Model+"+DriveResolver";
    }
    public sealed class DirectEvidenceReader
    {
        private readonly IPassiveFields f;
        private const string Trader = LedgerTraderTypes.Controller;
        private const string Model = LedgerTraderTypes.Model;
        private const string Row = LedgerTraderTypes.Row;
        private const string Closure = LedgerTraderTypes.Closure;
        public DirectEvidenceReader(IPassiveFields fields) { f = fields; }
        private int Int(int token, object value) { return (int)f.Read(token, value); }

        public OfferFrame Begin(object controller, bool admitted, Func<bool> lease)
        {
            var frame = new OfferFrame { Controller = controller, Lease = lease, State = "scope-not-admitted" };
            if (!admitted || lease == null || !lease()) return frame;
            if (!f.Exact(controller, Trader)) { frame.State = "controller-type"; return frame; }
            frame.Model = f.Read(0xc031, controller);
            if (!f.Exact(frame.Model, Model)) { frame.State = "model-missing-or-type"; return frame; }
            frame.ListOwner = f.Read(0xc01c, controller);
            frame.Rows = frame.ListOwner == null ? null : f.Read(0x699f, frame.ListOwner) as IList;
            frame.Raw = f.Read(0x533e, frame.Model) as IList;
            if (!f.ListType(0x699f, frame.Rows) || !f.ListType(0x533e, frame.Raw)) { frame.State = "list-missing-or-type"; return frame; }
            frame.RowCount = frame.Rows.Count; frame.RawCount = frame.Raw.Count;
            if (frame.RowCount > 256 || frame.RawCount > 256) { frame.State = "list-bound"; return frame; }
            frame.Admitted = true; frame.State = "current-frame"; return frame;
        }

        public bool Current(OfferFrame frame)
        {
            return frame != null && frame.Admitted && frame.Lease() &&
                ReferenceEquals(frame.Model, f.Read(0xc031, frame.Controller)) &&
                ReferenceEquals(frame.ListOwner, f.Read(0xc01c, frame.Controller)) &&
                ReferenceEquals(frame.Rows, f.Read(0x699f, frame.ListOwner)) &&
                ReferenceEquals(frame.Raw, f.Read(0x533e, frame.Model)) &&
                frame.RowCount == frame.Rows.Count && frame.RawCount == frame.Raw.Count &&
                !(bool)f.Read(0x5341, frame.Model);
        }

        public LedgerOfferFreshness RefreshSchedule(OfferFrame frame)
        {
            var result = new LedgerOfferFreshness { State = "scope-or-frame-unknown", Source = LedgerFreshness.Source };
            if (!Current(frame)) return result;
            try
            {
                object minutes = f.Read(0x533a, frame.Model), deadline = f.Read(0x533f, frame.Model);
                if (!(minutes is ushort) || !(deadline is ulong)) { result.State = "field-type-unknown"; return result; }
                result.RestockMinutes = (ushort)minutes; result.NextRefreshTick = (ulong)deadline;
                result.State = "observed-current-model";
                if (!ScheduleCurrent(frame, result)) result.State = "schedule-changed-during-read";
            }
            catch (Exception) { result.State = "schedule-read-unavailable"; }
            return result;
        }
        public bool ScheduleCurrent(OfferFrame frame, LedgerOfferFreshness schedule)
        {
            if (!Current(frame)) return false;
            if (schedule == null || schedule.State != "observed-current-model") return true; // Unknown metadata never erases valid visible offers.
            try
            {
                object minutes = f.Read(0x533a, frame.Model), deadline = f.Read(0x533f, frame.Model);
                return minutes is ushort && deadline is ulong && (ushort)minutes == schedule.RestockMinutes &&
                    (ulong)deadline == schedule.NextRefreshTick && Current(frame);
            }
            catch (Exception) { return false; }
        }

        public PublishedOffer Published(object row)
        {
            if (!f.Exact(row, Row)) return null;
            return new PublishedOffer {
                Name = (string)f.Read(0x6977, row), Sprite = (string)f.Read(0x6978, row),
                StockText = (string)f.Read(0xc038, row), CapacityText = (string)f.Read(0xc039, row),
                SellPriceText = (string)f.Read(0xc03a, row), BuyPriceText = (string)f.Read(0xc03b, row),
                SellAvailable = (bool)f.Read(0xc03c, row), BuyAvailable = (bool)f.Read(0xc03d, row),
                BuySupported = (bool)f.Read(0xc03e, row), Auxiliary = Int(0xc03f, row)
            };
        }

        private static OfferBinding Reject(OfferBinding b, string reason)
        { b.Reason = reason; b.Item = b.Auxiliary = null; return b; }

        public OfferBinding Bind(OfferFrame frame, object row, int publishedIndex, PublishedOffer visible)
        {
            var b = new OfferBinding();
            if (!Current(frame)) return Reject(b, "frame-stale-or-processing");
            if (publishedIndex < 0 || publishedIndex >= frame.RowCount || !ReferenceEquals(frame.Rows[publishedIndex], row)) return Reject(b, "row-not-current");
            if (!f.Exact(row, Row) || visible == null) return Reject(b, "row-type");
            object callback = f.Read(0x697f, row);
            if (!f.Exact(callback, "Assembly-CSharp.PageSetConnectionToken+ConfigLayoutConnectionToken")) return Reject(b, "delegate-missing-or-type");
            var action = callback as Delegate;
            if (action == null || action.GetInvocationList().Length != 1 || !f.Callback(action)) return Reject(b, "delegate-method-or-multicast");
            b.Closure = action.Target;
            if (!f.Exact(b.Closure, Closure)) return Reject(b, "closure-type");
            if (!ReferenceEquals(f.Read(0xc047, b.Closure), frame.Controller)) return Reject(b, "foreign-controller");
            if (!ReferenceEquals(f.Read(0xc046, b.Closure), row)) return Reject(b, "stale-or-foreign-row");
            int index = Int(0xc045, b.Closure); b.Index = index;
            if (index < 0 || index >= frame.RawCount) return Reject(b, "raw-index-bound");
            object entry = frame.Raw[index]; b.Entry = entry;
            if (!f.Exact(entry, LedgerTraderTypes.Entry)) return Reject(b, "raw-entry-type");
            // Only the entry explicitly addressed by this admitted row. No scan or fallback search.
            int stock = Int(0x5348, entry), capacity = Int(0x5349, entry);
            if (stock <= 0 && capacity <= 0) return Reject(b, "entry-no-longer-published-available");
            int item = Int(0x5346, entry), auxiliary = Int(0x5347, entry);
            if (item != Int(0xc044, b.Closure)) return Reject(b, "item-conflict");
            if (auxiliary != visible.Auxiliary) return Reject(b, "auxiliary-conflict");
            int ps = Int(0x534a, entry), pb = Int(0x534b, entry);
            int shownStock, shownCapacity;
            if (!int.TryParse(visible.StockText, NumberStyles.Integer, CultureInfo.InvariantCulture, out shownStock) ||
                !int.TryParse(visible.CapacityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out shownCapacity) ||
                shownStock != stock || shownCapacity != capacity || stock < 0 || capacity < 0)
                return Reject(b, "published-quantity-conflict-or-invalid");
            if (visible.SellAvailable != (ps > 0 && stock > 0) || visible.BuySupported != (pb > 0 && capacity > 0) ||
                visible.BuyAvailable != (pb > 0 && capacity > 0 && stock < capacity)) return Reject(b, "published-flags-conflict");
            // At these integral magnitudes the pinned zero-coefficient formula is exact in Single.
            int shownSell, shownBuy;
            if (ps > 16777216 || pb > 16777216 || !int.TryParse(visible.SellPriceText, out shownSell) ||
                !int.TryParse(visible.BuyPriceText, out shownBuy) || shownSell != Math.Max(0, ps) || shownBuy != Math.Max(0, pb))
                return Reject(b, "stored-price-conflict-or-precision-unsupported");
            if (!Current(frame) || !ReferenceEquals(frame.Raw[index], entry) || !ReferenceEquals(frame.Rows[publishedIndex], row) ||
                !ReferenceEquals(f.Read(0x697f, row), callback) || Int(0xc045, b.Closure) != index ||
                !ReferenceEquals(f.Read(0xc046, b.Closure), row) || !ReferenceEquals(f.Read(0xc047, b.Closure), frame.Controller))
                return Reject(b, "frame-or-binding-changed-during-read");
            // Scalar/row fences also reject in-place mutation without relying on list/object identity alone.
            if (Int(0x5346, entry) != item || Int(0x5347, entry) != auxiliary || Int(0xc044, b.Closure) != item ||
                Int(0x5348, entry) != stock || Int(0x5349, entry) != capacity || Int(0x534a, entry) != ps || Int(0x534b, entry) != pb ||
                Published(row).SemanticValue() != visible.SemanticValue()) return Reject(b, "values-changed-during-read");
            b.State = "bound-current-delegate"; b.Reason = "direct-index-reference-item-aux-and-value-fences";
            b.Item = item; b.Auxiliary = auxiliary; b.Stock = stock; b.Capacity = capacity; b.BaseSell = ps; b.BaseBuy = pb;
            return b;
        }

        public BlockPoint? Point(object value)
        {
            if (!f.Exact(value, "Vector3i")) return null;
            return new BlockPoint(Int(0x7415, value), Int(0x7416, value), Int(0x7417, value));
        }
        public static BlockPoint? Grid(BlockPoint? chunk, BlockPoint? offset)
        {
            if (!chunk.HasValue || !offset.HasValue) return null;
            try { checked { return new BlockPoint(chunk.Value.X * 16 + offset.Value.X, chunk.Value.Y * 512 + offset.Value.Y, chunk.Value.Z * 16 + offset.Value.Z); } }
            catch (OverflowException) { return null; }
        }

        public TraderIdentity TraderContext(OfferFrame frame)
        {
            var t = new TraderIdentity { State = "frame-not-current" };
            if (!Current(frame)) return t;
            t.CachedNpcRaw = Int(0x5340, frame.Model);
            if (t.CachedNpcRaw > 0) t.CachedNpcId = t.CachedNpcRaw;
            t.EntityRouteRaw = Int(0x5262, frame.Model);
            object offset = f.Read(0x5263, frame.Model);
            t.Offset = Point(offset); t.OffsetState = offset == null ? "missing" : t.Offset.HasValue ? "typed-Vector3i-source" : "unsupported-type";
            t.Chunk = f.Read(0x5266, frame.Model);
            if (t.Chunk != null)
            {
                t.ChunkState = "unsupported-type";
                if (f.Exact(t.Chunk, "Assembly-CSharp.ActivatorFactory"))
                {
                    t.ChunkState = "typed-direct-reference";
                    t.ChunkCoordinates = new BlockPoint(Int(0x4a28, t.Chunk), Int(0x4a29, t.Chunk), Int(0x4a2a, t.Chunk));
                    t.ClrIdx = Int(0x4a51, t.Chunk);
                    t.Owner = f.Read(0x4a4f, t.Chunk);
                    if (t.Owner != null)
                    {
                        t.OwnerState = "unsupported-type";
                        if (f.Exact(t.Owner, "Assembly-CSharp.ActionLayout"))
                        {
                            t.OwnerState = "typed-direct-reference"; t.ClusterIdx = Int(0x4aa9, t.Owner);
                            t.OwnerConsistent = t.ClrIdx >= 0 && t.ClusterIdx >= 0 && t.ClrIdx == t.ClusterIdx;
                            t.Structure = f.Read(0x4ab1, t.Owner);
                            if (t.Structure != null)
                            {
                                t.StructureState = "unsupported-type";
                                if (f.Supports(t.Structure, "Assembly-CSharp.DatabaseToken"))
                                { t.StructureState = "typed-direct-reference"; int id = Int(0x1b31, t.Structure); t.StructureEntityRaw = id; if (id > 0) t.StructureEntityId = id; }
                            }
                        }
                    }
                }
            }
            t.BlockGrid = Grid(t.ChunkCoordinates, t.Offset);
            t.GridState = t.BlockGrid.HasValue ? "derived-owning-block-grid-not-global" : "missing-or-overflow";
            t.Current = Current(frame) && ReferenceEquals(t.Chunk, f.Read(0x5266, frame.Model)) &&
                t.CachedNpcRaw == Int(0x5340, frame.Model) && t.EntityRouteRaw == Int(0x5262, frame.Model) && Same(t.Offset, Point(f.Read(0x5263, frame.Model)));
            if (t.Current && t.ChunkState == "typed-direct-reference")
                t.Current = ReferenceEquals(t.Owner, f.Read(0x4a4f, t.Chunk)) && t.ClrIdx == Int(0x4a51, t.Chunk) &&
                    t.ChunkCoordinates.Value.Same(new BlockPoint(Int(0x4a28, t.Chunk), Int(0x4a29, t.Chunk), Int(0x4a2a, t.Chunk)));
            if (t.Current && t.OwnerState == "typed-direct-reference")
                t.Current = ReferenceEquals(t.Structure, f.Read(0x4ab1, t.Owner)) && t.ClusterIdx == Int(0x4aa9, t.Owner);
            if (t.Current && t.StructureState == "typed-direct-reference")
                t.Current = t.StructureEntityRaw == Int(0x1b31, t.Structure);
            t.State = t.Current ? "current-source-tuple-cached-NPC-not-live-proof" : "identity-changed-during-read";
            return t;
        }
        public object ObservedOwnerPosition(TraderIdentity identity)
        {
            if(identity==null||!identity.Current||!identity.OwnerConsistent||!(identity.StructureEntityId>0)||identity.StructureState!="typed-direct-reference"||!f.Supports(identity.Structure,"Assembly-CSharp.ReferenceHelper"))return null;
            return f.Read(0x1b41,identity.Structure);
        }
        public string ObservedOwnerName(TraderIdentity identity)
        {
            if (identity == null || !identity.Current || !f.Supports(identity.Structure, "Assembly-CSharp.MemoryHelper")) return null;
            string name = f.Read(0x1c15, identity.Structure) as string;
            return string.IsNullOrEmpty(name) || name.Length > 512 ? null : name;
        }
        private static int? Positive(int n) { return n > 0 ? (int?)n : null; }
        private static bool Same(BlockPoint? a, BlockPoint? b)
        { return a.HasValue == b.HasValue && (!a.HasValue || a.Value.Same(b.Value)); }

        public DialogueIdentity DialogueContext(object controller, bool admitted, Func<bool> lease)
        {
            var d = new DialogueIdentity { State = "dialogue-not-currently-visible" };
            if (!admitted || lease == null || !lease()) return d;
            if (!f.Exact(controller, "Assembly-CSharp.DiskTypeConnectionToken")) { d.State = "controller-type"; return d; }
            d.Npc = f.Read(0xb956, controller);
            if (!f.Supports(d.Npc, "Assembly-CSharp.ReferenceHelper")) { d.State = "NPC-null-or-unsupported-type"; return d; }
            d.NpcRaw = Int(0x1b31, d.Npc); d.NpcId = Positive(d.NpcRaw.Value);
            if (f.Supports(d.Npc, "Assembly-CSharp.ProjectOptions"))
            {
                d.ReverseState = "typed-DeviceLayout-direct-fields";
                d.ReverseStructureRaw = Int(0x2124, d.Npc); d.ReverseStructureId = Positive(d.ReverseStructureRaw.Value); d.ReverseBlock = Point(f.Read(0x2125, d.Npc));
            }
            d.Current = lease() && ReferenceEquals(d.Npc, f.Read(0xb956, controller)) && d.NpcRaw == Int(0x1b31, d.Npc);
            if (d.Current && d.ReverseState == "typed-DeviceLayout-direct-fields")
                d.Current = d.ReverseStructureRaw == Int(0x2124, d.Npc) && Same(d.ReverseBlock, Point(f.Read(0x2125, d.Npc)));
            d.State = d.Current ? "current-UI-declared-NPC" : "dialogue-reference-changed-during-read";
            return d;
        }

        public static string Compare(TraderIdentity t, DialogueIdentity d)
        {
            if (t == null || d == null || !t.Current || !d.Current) return "unknown-both-current-visibility-proofs-required";
            if (!t.OwnerConsistent || !t.CachedNpcId.HasValue || !t.StructureEntityId.HasValue || !t.BlockGrid.HasValue ||
                !d.NpcId.HasValue || !d.ReverseStructureId.HasValue || !d.ReverseBlock.HasValue) return "unknown-incomplete-source-tuple";
            return t.CachedNpcId == d.NpcId && t.StructureEntityId == d.ReverseStructureId && t.BlockGrid.Value.Same(d.ReverseBlock.Value)
                ? "matching-current-direct-reverse-tuple-not-cross-load-proof" : "conflicting-current-direct-reverse-tuple";
        }
    }
}
