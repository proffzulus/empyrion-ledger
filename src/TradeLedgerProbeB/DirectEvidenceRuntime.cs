using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace TradeLedgerProbeB.Runtime
{
    public sealed partial class RuntimeObserver
    {
        private sealed class PinnedPassiveFields : IPassiveFields
        {
            private readonly RuntimeObserver observer;
            internal PinnedPassiveFields(RuntimeObserver value) { observer = value; }
            public object Read(int token, object instance) { return observer.Field(token, instance); }
            public bool Exact(object value, string name)
            { return value != null && value.GetType().Module == observer.module && value.GetType().FullName == name; }
            public bool Supports(object value, string name)
            {
                if (value == null || value.GetType().Module != observer.module) return false;
                Type expected = observer.module.GetType(name, true, false);
                return expected.IsInstanceOfType(value);
            }
            public bool ListType(int token, object value)
            { return value != null && value.GetType() == observer.fields[0x04000000 + token].FieldType; }
            public bool Callback(Delegate value)
            {
                MethodInfo m = value.Method;
                return m.Module == observer.module && LedgerNativeContracts.Token(m) == 0x0600cd33 && !m.IsStatic && m.ReturnType == typeof(void) &&
                    m.DeclaringType.FullName == "Assembly-CSharp.PcitureEditorConnectionToken+GroupProviderConnectionToken" &&
                    m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(bool);
            }
        }
        private DirectEvidenceReader directReader;
        private long directBound, directUnbound, directIdentity, directDialogue, directFailures;
        private string directSemantic, directDialogueStamp;
        private DirectEvidenceReader Direct
        { get { return directReader ?? (directReader = new DirectEvidenceReader(new PinnedPassiveFields(this))); } }
        private bool TraderLease(object controller)
        { return enabled && host.OnApiThread && host.WorldReady && traderCycle.Active && traderCycle.NativeOpenSeen && traderRetry.Active && ReferenceEquals(trader, controller); }
        private bool DialogueLease(object controller)
        { return enabled && host.OnApiThread && host.WorldReady && dialogueCycle.Active && ReferenceEquals(dialogue, controller); }
        private static string N(int? n) { return n.HasValue ? n.Value.ToString(CultureInfo.InvariantCulture) : "unknown"; }
        private static string P(BlockPoint? p) { return p.HasValue ? p.Value.ToString() : "unknown"; }
        private static string B(bool b) { return b ? "true" : "false"; }
        private void DirectDump(bool isTrader, string kind, string record)
        {
            // Keep detail valid JSON even when escaped user-visible strings exceed the existing 1800-char detail bound.
            if (record.Length > 1700)
            {
                truncations++;
                record = DumpJson.Record("snapshot", isTrader ? traderStability.Current : dialogueHash,
                    "state", "bounded-record-omitted", "originalKind", kind, "payloadCompleteness", "INCOMPLETE", "limit", "1700-json-detail-chars");
            }
            Dump(isTrader, kind, record);
        }

        private string DirectFrameStamp(OfferFrame frame, List<OfferBinding> bindings)
        {
            // Readiness includes proof/identity changes so an initially unknown binding can settle without a new hook.
            var pieces = new List<string>();
            foreach (OfferBinding b in bindings) pieces.Add(b.State + ":" + b.Reason + ":" + N(b.Index) + ":" + N(b.Item) + ":" + N(b.Auxiliary) + ":" + Id(b.Entry) + ":" + Id(b.Closure));
            try
            {
                TraderIdentity t = Direct.TraderContext(frame);
                pieces.Add(t.State + ":" + N(t.CachedNpcRaw) + ":" + Id(t.Chunk) + ":" + Id(t.Owner) + ":" + Id(t.Structure) +
                    ":" + P(t.Offset) + ":" + P(t.ChunkCoordinates) + ":" + N(t.ClrIdx) + ":" + N(t.ClusterIdx) + ":" + N(t.StructureEntityRaw) + ":" + t.OffsetState + ":" + t.GridState);
            }
            catch (Exception error) { directFailures++; pieces.Add("unknown-identity-" + error.GetType().Name); }
            return Digest(string.Join("|", pieces));
        }

        private void EmitDirectTrader(OfferFrame frame, List<PublishedOffer> visible, List<OfferBinding> bindings, string snapshot)
        {
            // Called only at the existing two-sample stable boundary, inside the admitted trader poll.
            var semantics = new List<string>();
            foreach (PublishedOffer row in visible) semantics.Add(row == null ? "unknown:row" : row.SemanticValue());
            semantics.Sort(StringComparer.Ordinal);
            string semantic = Digest(string.Join("\n", semantics));
            string change = !frame.ComparisonComplete ? "unknown-filtered-or-incomplete-scope" : directSemantic == null ? "first-visible-observation" : directSemantic == semantic ? "unchanged-visible-offer-values" : "visible-offer-values-changed";
            DirectDump(true, "TraderSemanticObservation", DumpJson.Record("snapshot", snapshot, "semanticSnapshot", semantic,
                "previousSemanticSnapshot", directSemantic ?? "unknown:first", "change", change,
                "comparison", "published-multiset;duplicates-preserved;object-IDs-selection-and-order-excluded", "transaction", "not-required-not-inferred"));
            if (frame.ComparisonComplete) directSemantic = semantic;
            TraderIdentity identity = null;
            try { identity = Direct.TraderContext(frame); EmitTraderIdentity(identity, snapshot); }
            catch (Exception error) { directFailures++; DirectDump(true, "TraderIdentity", DumpJson.Record("snapshot", snapshot, "state", "unknown-read-failed", "errorType", error.GetType().Name)); }
            // No stored dialogue object/identity crosses closing. Read only if its independent current gate passes now.
            DialogueIdentity npc = null;
            bool dialogueAdmitted = dialogueCycle.Active && VisibleScope(dialogue, false);
            if (dialogueAdmitted)
            {
                object controller = dialogue;
                long interaction = dialogueCycle.Number;
                npc = Direct.DialogueContext(controller, true, () => DialogueLease(controller) && dialogueCycle.Number == interaction);
            }
            DirectDump(true, "CurrentIdentityComparison", DumpJson.Record("snapshot", snapshot, "traderGate", "passed",
                "dialogueGate", dialogueAdmitted ? "passed" : "not-currently-admitted;no-dialogue-payload-read",
                "result", DirectEvidenceReader.Compare(identity, npc), "cachedOrClosedDialogueUsed", "false"));
            var ledgerRows = new List<LedgerOffer>();
            for (int i = 0; i < bindings.Count; i++)
            {
                OfferBinding binding = bindings[i]; PublishedOffer row = visible[i];
                if (!Direct.Current(frame)) { binding = new OfferBinding { Reason = "frame-changed-before-emission" }; }
                else if (binding.Bound)
                {
                    try { binding = Direct.Bind(frame, frame.Rows[i], i, row); }
                    catch (Exception error) { directFailures++; binding = new OfferBinding { Reason = "contained-emission-" + error.GetType().Name }; }
                }
                if (binding.Bound) directBound++; else directUnbound++;
                ledgerRows.Add(LedgerOffer.Capture(i, row, binding));
                var values = new List<string> { "snapshot", snapshot, "uiRow", i.ToString(CultureInfo.InvariantCulture),
                    "state", binding.State, "reason", binding.Reason, "rawIndex", N(binding.Index),
                    "rowObject", Id(frame.Rows == null || i >= frame.Rows.Count ? null : frame.Rows[i]).ToString(CultureInfo.InvariantCulture),
                    "closureObject", Id(binding.Closure).ToString(CultureInfo.InvariantCulture), "entryObject", Id(binding.Entry).ToString(CultureInfo.InvariantCulture),
                    "bindingSource", "6940.Target/BE44/BE43/BE45/BE46;no-invocation-no-heuristic",
                    "itemId", binding.Bound ? N(binding.Item) : "unknown", "auxiliaryRaw", binding.Bound ? N(binding.Auxiliary) : "unknown",
                    "displayName", row == null ? "unknown" : Safe.Text(row.Name, 180), "spriteKey", row == null ? "unknown" : Safe.Text(row.Sprite, 100),
                    "visibleObservationRetained", "true", "crossLoadIdStability", "unknown" };
                DirectDump(true, "TraderRowBinding", DumpJson.Record(values.ToArray()));
                if (binding.Bound)
                {
                    bool sellSupported = binding.BaseSell > 0, buySupported = binding.BaseBuy > 0 && binding.Capacity > 0;
                    DirectDump(true, "TraderBoundOffer", DumpJson.Record("snapshot", snapshot, "uiRow", i.ToString(CultureInfo.InvariantCulture),
                        "itemId", N(binding.Item), "auxiliaryRaw", N(binding.Auxiliary), "stock", N(binding.Stock), "capacity", N(binding.Capacity),
                        "remainingNpcBuyingCapacity", buySupported ? Math.Max(0L, (long)binding.Capacity.Value - binding.Stock.Value).ToString(CultureInfo.InvariantCulture) : "unsupported",
                        "npcSellsSupported", B(sellSupported), "npcBuysSupported", B(buySupported),
                        "npcSellsAvailable", B(row.SellAvailable), "npcBuysAvailable", B(row.BuyAvailable),
                        "storedSellUnit", row.SellPriceText, "storedBuyUnit", row.BuyPriceText,
                        "renderedSellNumeric", row.SellAvailable ? row.SellPriceText : "unavailable-placeholder",
                        "renderedBuyNumeric", row.BuyAvailable ? row.BuyPriceText : "unavailable-placeholder",
                        "rawBaseSell532F", N(binding.BaseSell), "rawBaseBuy5330", N(binding.BaseBuy),
                        "semantics", "5188-static-certified-row-units;aux-encoding-unknown;selected-totals-separate"));
                }
            }
            PersistMarket(frame, identity, ledgerRows, snapshot);
        }

        private void EmitTraderIdentity(TraderIdentity t, string snapshot)
        {
            directIdentity++;
            DirectDump(true, "TraderIdentity", DumpJson.Record("snapshot", snapshot, "state", t.State,
                "cachedNpc5325Raw", N(t.CachedNpcRaw), "cachedNpcId", N(t.CachedNpcId),
                "cachedNpcProof", !t.Current ? "unknown-stale-source" : t.CachedNpcId.HasValue ? "positive-cache-not-live-NPC-proof" : "invalid-or-unset",
                "entityRoute5249Raw", N(t.EntityRouteRaw), "chunkObject", Id(t.Chunk).ToString(CultureInfo.InvariantCulture), "chunkProof", t.ChunkState,
                "offset524A", P(t.Offset), "chunkCoordinates4A0F_10_11", P(t.ChunkCoordinates), "blockGrid", P(t.BlockGrid), "gridProof", t.GridState,
                "chunkClrIdx4A38", N(t.ClrIdx), "ownerObject", Id(t.Owner).ToString(CultureInfo.InvariantCulture), "ownerProof", t.OwnerState,
                "ownerClusterIdx4A90", N(t.ClusterIdx), "indexConsistency", t.OwnerState != "typed-direct-reference" ? "unknown" : t.OwnerConsistent ? "equal-distinct-namespaces" : "mismatch",
                "structureObject", Id(t.Structure).ToString(CultureInfo.InvariantCulture), "structureProof", t.StructureState, "structureEntity1B2A", N(t.StructureEntityId),
                "source", "currentBE30/5325/524D/4A36/4A98;typed-Vector3i.721E_1F_20", "currentReferenceFence", B(t.Current),
                "globalMapPosition", "unknown;block-grid-not-global", "POIIdentityAndCrossLoadStability", "unknown"));
            DirectDump(true, "TraderIdentityFieldProofs", DumpJson.Record("snapshot", snapshot, "currentReferenceFence", B(t.Current),
                "offset524AProof", t.Current ? t.OffsetState : "unknown-stale-source",
                "chunk524DProof", t.Current ? t.ChunkState : "unknown-stale-source",
                "owner4A36Proof", t.Current ? t.OwnerState : "unknown-stale-source",
                "structure4A98Proof", t.Current ? t.StructureState : "unknown-stale-source",
                "chunkActualType", t.Chunk == null ? "unknown:missing" : t.Chunk.GetType().FullName,
                "ownerActualType", t.Owner == null ? "unknown:missing" : t.Owner.GetType().FullName,
                "structureActualType", t.Structure == null ? "unknown:missing" : t.Structure.GetType().FullName,
                "structureEntity1B2ARaw", N(t.StructureEntityRaw),
                "entityIdProof", !t.Current ? "unknown-stale-source" : t.StructureEntityId.HasValue ? "positive-direct-entity-ID-not-IStructure.Id" : "unknown-missing-unsupported-or-invalid",
                "chunkClrIdxProof", !t.Current ? "unknown-stale-source" : t.ClrIdx >= 0 ? "observed-chunk-index" : "unknown-missing-or-negative",
                "ownerClusterIdxProof", !t.Current ? "unknown-stale-source" : t.ClusterIdx >= 0 ? "observed-owner-index-native-IStructure.Id" : "unknown-missing-or-negative",
                "gridProof", t.Current ? t.GridState : "unknown-stale-source", "POIAndCrossLoadProof", "unknown"));
        }

        private void ObserveDirectDialogue()
        {
            // Additional metadata only; the established text/page/choice capture code is unchanged.
            if (dialogueHash == null) return;
            object controller = dialogue;
            long interaction = dialogueCycle.Number;
            try
            {
                DialogueIdentity d = Direct.DialogueContext(controller, true, () => DialogueLease(controller) && dialogueCycle.Number == interaction);
                string stamp = dialogueCycle.Number.ToString(CultureInfo.InvariantCulture) + ":" + dialogueHash + ":" +
                    d.State + ":" + Id(d.Npc) + ":" + N(d.NpcRaw) + ":" + N(d.ReverseStructureRaw) + ":" + P(d.ReverseBlock);
                if (stamp == directDialogueStamp) return;
                directDialogueStamp = stamp;
                directDialogue++;
                string nativePosition = d.Current && d.Npc != null ? Position(Field<Vector3>(0x1b41, d.Npc)) : "unknown";
                string title = Displayed(Field(0xb932, controller) as TMP_Text);
                DirectDump(false, "DialogueIdentity", DumpJson.Record("snapshot", dialogueHash, "page", dialoguePages.Page.ToString(CultureInfo.InvariantCulture),
                    "sample", dialoguePages.Sample.ToString(CultureInfo.InvariantCulture), "state", d.State, "npcEntityId", N(d.NpcId), "npcEntityIdRaw", N(d.NpcRaw),
                    "roleSource", "B755->DialogueSystem.Start(_curNPCEntity)", "actualNpcType", d.Npc == null ? "unknown" : d.Npc.GetType().FullName,
                    "visibleTitle", Safe.Text(title, 180), "nativePosition1B3A", nativePosition, "positionFrame", "native-EntityBridge.Position;map-equivalence-unknown",
                    "reverseProof", d.ReverseState, "reverseStructureEntity210D", N(d.ReverseStructureId), "reverseStructure210DRaw", N(d.ReverseStructureRaw), "reverseBlock210E", P(d.ReverseBlock),
                    "crossLoadIdStability", "unknown", "acceptanceRequired", "false", "comparison", "only-independent-current-gates;closed-dialogue-never-carried"));
                PersistDialogue(d, title, nativePosition);
            }
            catch (Exception error) { directFailures++; DirectDump(false, "DialogueIdentity", DumpJson.Record("snapshot", dialogueHash, "state", "unknown-read-failed", "errorType", error.GetType().Name)); }
        }

        private void DirectCounters()
        {
            host.Trace(TraceLevel.Info, "DirectEvidenceCounters", "boundRows=" + directBound + " unboundRows=" + directUnbound +
                " traderIdentityDumps=" + directIdentity + " dialogueIdentityDumps=" + directDialogue + " containedReadFailures=" + directFailures + " writtenDumpLoss=see-ObserverCounters-and-LoggerCounters");
        }
    }
}
