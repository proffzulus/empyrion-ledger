using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;

namespace TradeLedgerProbeB.Runtime
{
    public sealed partial class RuntimeObserver
    {
        private readonly Dictionary<string,LedgerOffer> exhaustedOffers=new Dictionary<string,LedgerOffer>();private long exhaustedInteraction;private string exhaustedWorld;private int exhaustedNpc;
        private void PersistMarket(OfferFrame frame, TraderIdentity identity, List<LedgerOffer> offers, string snapshot)
        {
            var sink = host as ILedgerHost;
            if (sink == null) return;
            try
            {
                bool currentIdentity = identity != null && identity.Current && Direct.Current(frame);
                var value = new LedgerIdentity { ProofState = currentIdentity ? identity.State : "identity-unknown-or-stale",
                    NpcSource = "currentBE30.5325;cached-not-live-NPC-proof", StructureSource = "currentBE30.524D.4A36.4A98.1B2A",
                    CatalogSource = "not-read;no-certified-discovery-filtered-catalog-reader-or-join",
                    BlockSource = "chunk.4A0F/10/11 * (16,512,16) + typed524A;structure-grid-not-global" };
                if (currentIdentity)
                {
                    value.NpcEntityId = identity.CachedNpcId; value.NpcRaw = identity.CachedNpcRaw;
                    value.StructureEntityId = identity.StructureEntityId; value.BlockGrid = identity.BlockGrid.HasValue ? P(identity.BlockGrid) : null;
                    value.ChunkClrIdx = identity.ClrIdx; value.OwnerClusterIdx = identity.ClusterIdx;
                    value.ChunkRelativeOffset = identity.Offset.HasValue ? P(identity.Offset) : null;
                    value.ChunkCoordinates = identity.ChunkCoordinates.HasValue ? P(identity.ChunkCoordinates) : null;
                    if (identity.OwnerState == "typed-direct-reference") value.OwnerConsistent = identity.OwnerConsistent;
                    // Not a catalog enumeration: a field on this admitted trader's exact direct owning structure.
                    try
                    {
                        string name = Direct.ObservedOwnerName(identity);
                        if (name != null)
                        { value.StructureName = name; value.StructureNameSource = "observed-owner StoreFactory.04001C0E -> IEntity.Name/B64B/1D11;catalog-equivalence-unverified"; }
                    }
                    catch (Exception error) { host.Trace(TraceLevel.Warn, "LedgerNameSkipped", "type=" + error.GetType().Name + " marketCaptureContinues=true"); }
                    // Optional exact owning POI origin: never convert the structure-local block offset into world coordinates.
                    try
                    {
                        object point=Direct.ObservedOwnerPosition(identity);float x,y,z;
                        string source="currentBE30.524D.4A36.4A98/ResourceSerializer.1B3A/EntityBridge.Position;POI-origin-not-NPC";
                        string text=point is UnityEngine.Vector3?Position((UnityEngine.Vector3)point):null;
                        if(LedgerBookmarkPolicy.Position(text,source,out x,out y,out z))
                        {value.StructureNativePosition=text;value.StructurePositionSource=source;}
                    }
                    catch(Exception error){host.Trace(TraceLevel.Warn,"LedgerPositionSkipped","type="+error.GetType().Name+" marketCaptureContinues=true");}
                    // Fence the new optional name/position read too; a name never rescues an invalid identity.
                    try
                    {
                        TraderIdentity final = Direct.TraderContext(frame);
                        if (!Direct.Current(frame) || !final.Current || !ReferenceEquals(identity.Structure, final.Structure) ||
                            identity.CachedNpcRaw != final.CachedNpcRaw || P(identity.BlockGrid) != P(final.BlockGrid) || identity.StructureEntityId != final.StructureEntityId)
                            value = new LedgerIdentity { ProofState = "identity-changed-during-name-read" };
                    }
                    catch (Exception) { value = new LedgerIdentity { ProofState = "identity-fence-read-failed;visible-offers-retained" }; }
                }
                if(frame.ComparisonComplete&&exhaustedOffers.Count>0&&exhaustedInteraction==traderCycle.Number&&identity!=null&&identity.CachedNpcId==exhaustedNpc&&(host as ILedgerTradeHost)!=null&&(host as ILedgerTradeHost).TradeContext!=null&&(host as ILedgerTradeHost).TradeContext.WorldKey==exhaustedWorld)foreach(var exhaustedOffer in exhaustedOffers.Values){if(offers.Exists(x=>x.ItemId==exhaustedOffer.ItemId&&x.AuxiliaryRaw==exhaustedOffer.AuxiliaryRaw))continue;var zero=LedgerJson.Decode<LedgerOffer>(LedgerJson.Encode(exhaustedOffer));zero.UiRow=offers.Count;offers.Add(zero);}

                sink.Record(new LedgerObservation { Kind = "market", Interaction = traderCycle.Number.ToString(CultureInfo.InvariantCulture),
                    Snapshot = snapshot, Source = "5229 admitted-current-TraderNPC;two-sample-stable;unchanged-eight-postfixes/250ms-poll",
                    Identity = value, Offers = offers, FullCurrentAssortment = frame.ComparisonComplete,
                    OfferFreshness = Direct.ScheduleCurrent(frame, frame.Freshness) ? frame.Freshness :
                        new LedgerOfferFreshness { State = "schedule-or-frame-changed-before-persistence", Source = LedgerFreshness.Source } });
                if(!traderAdmissionReported&&frame.ComparisonComplete&&value.NpcEntityId.HasValue){traderAdmissionReported=true;try{host.Trace(TraceLevel.Warn,"TraderCaptureAdmitted","interaction="+traderCycle.Number+" offers="+offers.Count+" complete=true source=current1.20-visible-published-trader durability=worker-pending");}catch(Exception){}}
            }
            catch (Exception error) { host.Trace(TraceLevel.Error, "LedgerCaptureError", "kind=market type=" + error.GetType().Name + " diagnosticObserverContinues=true"); }
        }
        private void PersistDialogue(DialogueIdentity identity, string title, string nativePosition)
        {
            var sink = host as ILedgerHost;
            if (sink == null) return;
            try
            {
                long interaction = dialogueCycle.Number;
                object controller = dialogue;
                if (!DialogueLease(controller) || !identity.Current) return;
                RememberHandIn(controller,identity);
                string body = Displayed(Field(0xb933, controller) as TMP_Text);
                if (body == null) return;
                var text = new LedgerDialogue { Page = dialoguePages.Page, Sample = dialoguePages.Sample,
                    State = Field<string>(0xb93c, controller), TitleVerbatim = title, BodyVerbatim = body.Substring(0, Math.Min(4096, body.Length)) };
                var bodyWidget=Field(0xb933,controller) as TMP_Text;
                text.BodyFullyRevealed=body.Length<=4096&&bodyWidget!=null&&bodyWidget.textInfo!=null&&bodyWidget.maxVisibleCharacters>=bodyWidget.textInfo.characterCount;
                if (body.Length > 4096) text.Completeness = "INCOMPLETE;visible-source-prefix-limited-to-4096;not-quest-status";
                // Options are shown output only; never a choice/acceptance/completion signal.
                var options = Field(0xb936, controller) as System.Collections.IList;
                if (options != null && options.Count <= 32)
                    for (int i = 0; i < options.Count; i++)
                    { string option = Displayed(options[i] as TMP_Text); if (option != null) text.OptionsVerbatim.Add(option.Substring(0, Math.Min(4096, option.Length))); }
                if (!DialogueLease(controller) || dialogueCycle.Number != interaction ||
                    !ReferenceEquals(identity.Npc, Field(0xb956, controller))) return;
                sink.Record(new LedgerObservation { Kind = "dialogue-shown", Interaction = interaction.ToString(CultureInfo.InvariantCulture),
                    Snapshot = dialogueHash, Source = "current-visible B731/B732 revealed output;original-rich-text;no-unseen-branches",
                    Identity = new LedgerIdentity { NpcEntityId = identity.NpcId, NpcRaw = identity.NpcRaw,
                        NpcSource = "B755 UI-declared NPC -> 1B2A;not-local-player-B754", StructureEntityId = identity.ReverseStructureId,
                        StructureSource = identity.ReverseState + ":DeviceLayout.210D", BlockGrid = identity.ReverseBlock.HasValue ? P(identity.ReverseBlock) : null,
                        BlockSource = "DeviceLayout.210E;structure-local-block", NativePosition = nativePosition,
                        PositionSource = "ResourceSerializer.1B3A/EntityBridge.Position;native-frame-map-equivalence-unverified", ProofState = identity.State }, Dialogue = text });
            }
            catch (Exception error) { host.Trace(TraceLevel.Error, "LedgerCaptureError", "kind=dialogue type=" + error.GetType().Name + " diagnosticObserverContinues=true"); }
        }
    }
}
