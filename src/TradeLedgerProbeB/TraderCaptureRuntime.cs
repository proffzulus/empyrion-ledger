using System;
using System.Collections;
using UnityEngine;

namespace TradeLedgerProbeB.Runtime
{
    public sealed partial class RuntimeObserver
    {
        private readonly TraderRetryLoop traderRetry = new TraderRetryLoop();
        private TraderSnapshotStability traderStability = new TraderSnapshotStability();
        private long traderScopeChecks, traderScopeSkips, traderMetadataPasses, traderPollPasses;
        private long dialogueScopeChecks, dialogueScopePasses, traderPayloadAttempts, traderEmptySnapshots, traderTimeouts;
        private long traderInteractionPayloadAttempts, traderEmptyModels;
        private bool traderAdmissionReported;
        private string traderReadDetail = "payload-not-read";
        private readonly long[] traderReadResults = new long[7];
        private string traderProofDigest, traderScopeMode = "unknown";
        private long traderLastHookAt, traderLastReadLogAt = -5000;
        private int traderLastHookToken;
        private TraderReadResult? traderLastRead;

        private void ArmTrader()
        {
            if (!traderRetry.Active || traderRetry.Interaction != traderCycle.Number)
            { traderRetry.Begin(host.ElapsedMs, traderCycle.Number); traderInteractionPayloadAttempts = 0; traderAdmissionReported=false; traderReadDetail = "payload-not-read"; }
        }

        private void PollTrader()
        {
            if (!traderCycle.Active) return;
            TraderReadResult result;
            if (!traderRetry.Poll(host.ElapsedMs, () => DiagnoseScope(trader, true, "capture-poll"), () => {
                traderPayloadAttempts++;
                traderInteractionPayloadAttempts++;
                if (traderInteractionPayloadAttempts == 1 || host.ElapsedMs - traderLastReadLogAt >= 5000)
                    host.Trace(TraceLevel.Debug, "TraderPayloadReadBegin", "interaction=" + traderCycle.Number + " attempt=" + traderPayloadAttempts +
                        " interactionAttempt=" + traderInteractionPayloadAttempts + " source=update-poll scope=" + traderScopeMode + " modelField=0x0400C031 lastTraderHook=0x" + traderLastHookToken.ToString("X8") +
                        " lastHookAgeMs=" + (host.ElapsedMs - traderLastHookAt) + " callbackNotRequired=true");
                return ObserveTrader();
            }, out result)) return;
            if (result == TraderReadResult.ScopeRejected || result == TraderReadResult.ModelMissing || result == TraderReadResult.RowsMissing) traderStability.LoseScope();
            traderReadResults[(int)result]++;
            if (traderLastRead != result || host.ElapsedMs - traderLastReadLogAt >= 5000)
            {
                traderLastRead = result; traderLastReadLogAt = host.ElapsedMs;
                Dump(true, "TraderCaptureState", "source=update-poll result=" + result + " poll=" + traderRetry.Polls +
                    " payloadRead=" + (result != TraderReadResult.ScopeRejected) + " attempts=" + traderPayloadAttempts + " interactionAttempts=" + traderInteractionPayloadAttempts +
                    " modelField=0x0400C031 retryIntervalMs=250 unreadyTimeoutMs=30000 " + (result == TraderReadResult.ScopeRejected ? "payloadFieldsNotRead=true" : traderReadDetail));
            }
            if (traderRetry.Expired)
            {
                traderTimeouts++;
                Dump(true, "TraderCaptureTimeout", "lastResult=" + result + " polls=" + traderRetry.Polls + " timeoutMs=30000 nativeWindowClosed=UNKNOWN observationLeaseExpired=true");
                CloseTrader("capture-unready-timeout-30s");
            }
        }

        private string TraderGate(object controller, object entry, object topEntry, string candidateKey, string topKey,
            int depth, int stackCount, bool atTop, int ownerId, GameObject root, Transform transform, string purpose)
        {
            object config = entry == null ? null : Field(0xc186, entry);
            object owner = controller == null ? null : Field(0xc02f, controller);
            var p = new TraderVisibilityProof {
                NativeSessionOpen = traderCycle.Active && traderCycle.NativeOpenSeen && traderRetry.Active,
                InStack = entry != null, AtTop = atTop, CandidateKey = candidateKey, TopKey = topKey,
                EntryOpen = entry != null && Field<bool>(0xc187, entry),
                ControllerConfigMatches = controller != null && ConfigCopiesMatch(config, Field(0xc01b, controller),root),
                RootMatches = root != null && transform != null && root.transform == transform,
                RootActive = root != null && root.activeSelf && root.activeInHierarchy,
                AlphaReady = PanelReady(entry, root), Owner = ownerId, Local = host.LocalPlayerId,
                StackCount = stackCount, CandidateDepth = depth,
                LinkedWindowKey = config == null ? null : Field<string>(0xc134, config)
            };
            string bagMetadata = "BagProof=not-applicable";
            if (!atTop && stackCount == 2 && depth == 1 && candidateKey == "TraderNPC" && topKey == "Bag" && topEntry != null)
            {
                // Metadata only: never read AB24 item grid, inventory stacks, or other window payloads.
                object bag = Field(0xc184, topEntry);
                var bagRoot = Field(0xc185, topEntry) as GameObject;
                object bagConfig = Field(0xc186, topEntry);
                p.BagTypeMatches = bag != null && bag.GetType().FullName == LedgerTraderTypes.Bag;
                p.BagEntryOpen = Field<bool>(0xc187, topEntry);
                p.BagRootActive = bagRoot != null && bagRoot.activeSelf && bagRoot.activeInHierarchy;
                if (p.BagTypeMatches)
                {
                    var bagTransform = Field(0xad27, bag) as Transform;
                    object bagOwner = Field(0xad25, bag);
                    p.BagRootMatches = bagRoot != null && bagTransform != null && bagRoot.transform == bagTransform;
                    p.BagConfigMatches = ConfigCopiesMatch(bagConfig, Field(0xad20, bag),bagRoot);
                    p.BagOwner = bagOwner == null ? -1 : Field<int>(0x1b31, bagOwner);
                    p.SameOwnerObject = owner != null && ReferenceEquals(owner, bagOwner);
                }
                UIPanel traderPanel = SingleOpaquePanel(entry, root), bagPanel = SingleOpaquePanel(topEntry, bagRoot);
                p.BagAlphaReady = PanelReady(topEntry, bagRoot);
                p.DistinctOpaquePanels = traderPanel != null && bagPanel != null && traderPanel != bagPanel && root != bagRoot;
                bagMetadata = "bagControllerObject=" + Id(bag) + " bagRootObject=" + Id(bagRoot) + " bagTypeMatches=" + p.BagTypeMatches +
                    " bagEntryOpen=" + p.BagEntryOpen + " bagConfigMatches=" + p.BagConfigMatches + " bagRootMatches=" + p.BagRootMatches +
                    " bagRootActive=" + p.BagRootActive + " bagAlphaReady=" + p.BagAlphaReady + " bagOwnerEntityId=" + p.BagOwner +
                    " sameOwnerReference=" + p.SameOwnerObject + " distinctSingleOpaquePanels=" + p.DistinctOpaquePanels;
            }
            string reason = TraderScopePolicy.Reject(p);
            traderScopeMode = reason != null ? "rejected" : atTop ? "top-TraderNPC" : "verified-direct-Bag-companion";
            string proof = "purpose=" + purpose + " nativeSessionOpen=" + p.NativeSessionOpen + " entryOpen=" + p.EntryOpen +
                " traderConfigMatches=" + p.ControllerConfigMatches + " linkedKey=" + Safe.Text(p.LinkedWindowKey, 80) +
                " linkedKeySource=entry.0400C186/config.0400C134 configMatch=11-controller+same-root-WindowStub-C198/C199 ownerSource=0400C02F/0400AD25/04001B31 rootSource=0400C02E/0400AD27" +
                " " + bagMetadata + " result=" + (reason ?? traderScopeMode) + " payloadRead=false captureOnlyFromUpdate=true";
            // Purpose remains on the record, but does not create redundant snapshots every poll.
            string stamp = Digest(proof.Replace("purpose=" + purpose, "purpose=scope"));
            if (stamp != traderProofDigest)
            { traderProofDigest = stamp; Dump(true, "TraderCompanionProof", proof); }
            return reason;
        }

        private bool ConfigCopiesMatch(object a, object b,GameObject root)
        {
            if (a == null || b == null || a.GetType() != b.GetType() || !a.GetType().IsValueType ||
                a.GetType().FullName != "Assembly-CSharp.FileTreeConnectionToken") return false;
            if(root==null)return false;
            var stub=root.GetComponent(module.GetType("Eleon.Ngui.WindowStub")) as Component;
            return stub!=null&&stub.gameObject==root&&!Field<bool>(0xc197,stub)&&
                LedgerProduction.ConfigurationMismatch(row=>Field(row,a),row=>Field(row,b),row=>Field(row,stub))==null;
        }

        private UIPanel SingleOpaquePanel(object entry, GameObject root)
        {
            if (entry == null || root == null || !PanelReady(entry, root)) return null;
            var panels = Field(0xc188, entry) as IList;
            if (panels == null || panels.Count != 1) return null;
            var p = panels[0] as UIPanel;
            return p != null && p.enabled && p.gameObject.activeSelf && p.gameObject.activeInHierarchy &&
                TraderScopePolicy.IsOpaque(p.alpha, p.finalAlpha) ? p : null;
        }

        private void TraderCounters()
        {
            host.Trace(TraceLevel.Info, "TraderCounters", "scopeChecks=" + traderScopeChecks + " scopeSkips=" + traderScopeSkips +
                " pollScopePassed=" + traderPollPasses + " metadataScopePassed=" + traderMetadataPasses + " payloadReadAttempts=" + traderPayloadAttempts +
                " modelMissing=" + traderReadResults[(int)TraderReadResult.ModelMissing] + " rowsMissingOrBound=" + traderReadResults[(int)TraderReadResult.RowsMissing] +
                " processing=" + traderReadResults[(int)TraderReadResult.Processing] + " waitingStable=" + traderReadResults[(int)TraderReadResult.WaitingStable] +
                " captured=" + traderReadResults[(int)TraderReadResult.Captured] + " unchanged=" + traderReadResults[(int)TraderReadResult.Unchanged] +
                " scopeRejectedPolls=" + traderReadResults[(int)TraderReadResult.ScopeRejected] + " emptyPublishedSnapshots=" + traderEmptySnapshots + " emptyRawModels=" + traderEmptyModels + " timeouts=" + traderTimeouts +
                " publishedRowCandidates=" + publishedRowsCaptured + " rawEntryCandidates=" + rawEntriesCaptured +
                " dialogueScopeChecks=" + dialogueScopeChecks + " dialogueScopePassed=" + dialogueScopePasses + " settlement=UNKNOWN");
        }
    }
}
