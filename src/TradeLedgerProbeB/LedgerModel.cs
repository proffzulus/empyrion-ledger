using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace TradeLedgerProbeB
{
    // Plain owned values only. These objects must never contain a Unity/game object or delegate.
    public sealed class LedgerContext
    {
        public string WorldKey, LocalSaveKey, SavePath, Playfield, SolarSystem, Mode, ScopeState;
        public bool Recordable;
        public LedgerGeography Geography;
        public static bool MatchesCurrentSave(LedgerContext context,string save)
        {
            if(context==null||!context.Recordable)return false;
            var current=Create(save,context.Playfield,null,context.Mode);
            var claimed=Create(context.SavePath,context.Playfield,null,context.Mode);
            string local=context.LocalSaveKey??context.WorldKey;
            return current.Recordable&&claimed.Recordable&&current.WorldKey==local&&claimed.WorldKey==local;
        }
        public static LedgerContext Create(string save, string playfield, string system, string mode)
        {
            var c = new LedgerContext { SavePath = save, Playfield = playfield, SolarSystem = system, Mode = mode, ScopeState = "unknown-save-or-playfield" };
            if (mode != "SinglePlayer") { c.ScopeState = "multiplayer-server-world-scope-unverified;capture-disabled"; return c; }
            if (string.IsNullOrWhiteSpace(save) || string.IsNullOrWhiteSpace(playfield) || save.StartsWith("<error:", StringComparison.Ordinal) || playfield.StartsWith("<error:", StringComparison.Ordinal)) return c;
            try
            {
                if (!Path.IsPathRooted(save)) return c;
                c.SavePath = Path.GetFullPath(save).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                c.WorldKey = LedgerJson.Hash("single-player-path-v1|" + c.SavePath.ToUpperInvariant());
                c.LocalSaveKey = c.WorldKey;
                c.ScopeState = "local-save-path;not-authoritative-world-GUID;replaced-save-needs-new-ledger-scope";
                c.Recordable = true;
            }
            catch (Exception) { c.ScopeState = "invalid-save-path"; }
            return c;
        }
    }
    public sealed class LedgerIdentity
    {
        public int? NpcEntityId, NpcRaw, StructureEntityId, ChunkClrIdx, OwnerClusterIdx;
        public string NpcSource, StructureSource, BlockGrid, BlockSource, NativePosition, PositionSource;
        public string StructureNativePosition, StructurePositionSource;
        public string ChunkCoordinates, ChunkRelativeOffset, StructureName, StructureNameSource;
        public string CatalogDisplayName, CatalogSource, ProofState;
        public bool? OwnerConsistent;
    }
    public sealed class LedgerOffer
    {
        public LedgerOffer Copy(){return (LedgerOffer)MemberwiseClone();} // Seller projection changes scalar stock only; nested evidence stays read-only.
        public int UiRow;
        public bool RetainedRelation;public string LastEvidenceUtc;
        public static bool Certified(LedgerOffer o){return o!=null&&(o.BindingState=="bound-current-delegate"||o.BindingState=="confirmed-native-exhaustion"||o.RetainedRelation)&&o.ItemId>0&&o.AuxiliaryRaw.HasValue;}
        public PublishedOffer Visible;
        public string BindingState, BindingReason, BindingSource;
        public int? RawIndex, ItemId, AuxiliaryRaw, Stock, Capacity, BaseSellRaw, BaseBuyRaw;
        public bool? NpcSellsSupported, NpcBuysSupported, NpcSellsAvailable, NpcBuysAvailable;
        public long? RemainingNpcBuyingCapacity;
        public string DemandState, StoredSellUnit, StoredBuyUnit, RenderedSellNumeric, RenderedBuyNumeric;
        public string RenderedSellState, RenderedBuyState;
        public static LedgerOffer Capture(int row, PublishedOffer visible, OfferBinding binding)
        {
            var o = new LedgerOffer { UiRow = row, Visible = visible, BindingState = binding.State, BindingReason = binding.Reason,
                BindingSource = "6940.Target/BE44/BE43/BE45/BE46;passive-reference-and-value-fences", RawIndex = binding.Index };
            if (visible != null)
            {
                int n;
                if (int.TryParse(visible.StockText, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 0) o.Stock = n;
                if (int.TryParse(visible.CapacityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 0) o.Capacity = n;
                o.NpcBuysSupported = visible.BuySupported; o.NpcBuysAvailable = visible.BuyAvailable; o.NpcSellsAvailable = visible.SellAvailable;
                o.StoredSellUnit = visible.SellPriceText; o.StoredBuyUnit = visible.BuyPriceText;
                o.RenderedSellNumeric = visible.SellAvailable ? visible.SellPriceText : null;
                o.RenderedBuyNumeric = visible.BuyAvailable ? visible.BuyPriceText : null;
                o.RenderedSellState = visible.SellAvailable ? "numeric-column" : "unavailable-placeholder";
                o.RenderedBuyState = visible.BuyAvailable ? "numeric-column" : "unavailable-placeholder";
            }
            if (binding.Bound)
            {
                o.ItemId = binding.Item; o.AuxiliaryRaw = binding.Auxiliary; o.BaseSellRaw = binding.BaseSell; o.BaseBuyRaw = binding.BaseBuy;
                o.NpcSellsSupported = binding.BaseSell > 0;
            }
            o.DemandState = !o.NpcBuysSupported.HasValue ? "unknown" : !o.NpcBuysSupported.Value ? "unsupported" :
                o.Stock.HasValue && o.Capacity.HasValue ? "observed-remaining-capacity" : "unknown-quantity";
            if (o.DemandState == "observed-remaining-capacity") o.RemainingNpcBuyingCapacity = Math.Max(0L, (long)o.Capacity.Value - o.Stock.Value);
            return o;
        }
    }
    public sealed class LedgerDialogue
    {
        public bool? BodyFullyRevealed;
        public int Page, Sample;
        public string TitleVerbatim, BodyVerbatim, State, Completeness = "unknown;shown-prefix-and-page-samples;not-quest-status";
        public List<string> OptionsVerbatim = new List<string>();
        public string Acceptance = "not-inferred", Completion = "not-inferred";
    }
    public sealed class LedgerObservation
    {
        public int FormatVersion = 1;
        public string ObservationId = Guid.NewGuid().ToString("N"), Utc, GameTicks, Session, Kind, Interaction, Snapshot, Source;
        public long Sequence;public int ObserverPlayer;public string RequestInstance;
        public LedgerContext Context;
        public LedgerIdentity Identity;
        public List<LedgerOffer> Offers;
        public bool? FullCurrentAssortment;
        public LedgerOfferFreshness OfferFreshness;
        public LedgerDialogue Dialogue;
        public LedgerRequestFact Request;
        public LedgerObjectFact DiscoveredObject;
        public LedgerProductionSnapshot Production;
        public LedgerProductionRetirement ProductionRetirement;
    }
    public interface ILedgerHost
    {
        void Record(LedgerObservation observation);
    }
    public static class LedgerJson
    {
        public static byte[] Encode<T>(T value)
        {
            using (var stream = new MemoryStream())
            { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); return stream.ToArray(); }
        }
        public static T Decode<T>(byte[] bytes)
        { using (var stream = new MemoryStream(bytes)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream); }
        public static string Hash(string text) { return Hash(Encoding.UTF8.GetBytes(text)); }
        public static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    }
    // Latest known endpoint evidence; timestamps belong to individual retained fields.
    public sealed class LedgerEndpointState
    {
        public LedgerIdentity Identity;
        // One last actually shown NPC body, independent of retained full pages or prefix ranking.
        // Additive owned JSON field; no schema change or destructive page migration.
        public LedgerObservation LastDialogue;
        public LedgerObservation LastRequest;
        public Dictionary<string,string> EvidenceUtc = new Dictionary<string,string>();
    }
}
