using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace TradeLedgerProbeB.Runtime
{
    public sealed partial class RuntimeObserver
    {
        private readonly InteractionCycle traderCycle = new InteractionCycle(), dialogueCycle = new InteractionCycle();
        private readonly DumpBudget dumpBudget = new DumpBudget();
        private DialoguePages dialoguePages = new DialoguePages();
        private string traderScopeDigest, dialogueScopeDigest, lastLimit;
        private int lastHookToken;
        private long scopeChecks, scopeSkips, scopePassed, truncations, modelCaptures, dialogueCaptures;
        private long publishedRowsCaptured, rawEntriesCaptured, unchangedModels, worldNotReadyHooks;
        private long dumpSequence;
        private InteractionCycle Cycle(bool traderTarget) { return traderTarget ? traderCycle : dialogueCycle; }

        private void Dump(bool isTrader, string kind, string detail)
        {
            var cycle = Cycle(isTrader);
            long key = cycle.Number * 2 + (isTrader ? 0 : 1);
            if (!dumpBudget.Take(host.ElapsedMs, key))
            {
                if (lastLimit != dumpBudget.LastLimit)
                {
                    lastLimit = dumpBudget.LastLimit;
                    host.Trace(TraceLevel.Warn, "DumpLimit", "reason=" + lastLimit + " dropped=" + dumpBudget.Dropped + " payloadCompleteness=INCOMPLETE");
                }
                return;
            }
            lastLimit = null;
            var context = host as IObservationContext;
            string evidence = context == null ? "unknown:no-context-host" : context.ContextEvidence;
            if (detail != null && detail.Length > 1800) { truncations++; detail = detail.Substring(0, 1800) + "[value-truncated]"; }
            if (evidence.Length > 1200) { truncations++; evidence = evidence.Substring(0, 1200) + "[context-truncated]"; }
            string record = DumpJson.Record("format", "TradeLedgerProbeB.dump.v1", "dumpSeq", (++dumpSequence).ToString(CultureInfo.InvariantCulture),
                "target", isTrader ? "TraderNPC" : "NpcDialogue", "interaction", cycle.Number.ToString(CultureInfo.InvariantCulture),
                "windowObject", Id(isTrader ? trader : dialogue).ToString(CultureInfo.InvariantCulture), "kind", kind,
                "triggerHookToken", "0x" + lastHookToken.ToString("X8"), "triggerMeaning", "last-observed-hook-not-causal-proof",
                "contextSource", "cached-public-API-local-world", "context", evidence, "detail", detail);
            if (record.Length > 5600)
            {
                truncations++;
                record = DumpJson.Record("kind", "DumpSerializedLimit", "originalKind", kind, "interaction", cycle.Number.ToString(),
                    "dumpSeq", dumpSequence.ToString(), "reason", "escaped-size-over-5600", "payloadCompleteness", "INCOMPLETE");
            }
            host.Trace(TraceLevel.Debug, "DiagnosticDump", record);
        }

        private void Acquire(object instance, bool isTrader)
        {
            object previous = isTrader ? trader : dialogue;
            if (previous != null && !ReferenceEquals(previous, instance))
            { if (isTrader) CloseTrader("controller-instance-changed"); else CloseDialogue("controller-instance-changed"); }
            var cycle = Cycle(isTrader);
            if (!cycle.Active) cycle.Begin();
            if (isTrader) { trader = instance; traderAt = host.ElapsedMs; ArmTrader(); }
            else { dialogue = instance; dialogueAt = host.ElapsedMs; }
            if (previous == null || !ReferenceEquals(previous, instance))
                Dump(isTrader, "ControllerCandidate", "source=own-postfix payloadRead=false controllerType=" + instance.GetType().FullName);
        }

        private void NativeWindow(int type, string key)
        {
            if(type==10&&key!="NpcDialogue"&&key.IndexOf("tooltip",StringComparison.OrdinalIgnoreCase)<0)ForgetHandIn("other-window-open:"+key);
            if(type==10&&key=="NpcDialogue")ForgetHandIn("new-dialogue-open");
            bool ownTrader = key == "TraderNPC", ownDialogue = key == "NpcDialogue";
            bool companion = key == "Bag" || key.IndexOf("tooltip", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("inventory", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!ownTrader && !ownDialogue && !companion) return;
            if (ownTrader || ownDialogue)
            {
                var cycle = Cycle(ownTrader);
                if (type == 10)
                {
                    if (cycle.Active && cycle.NativeOpenSeen)
                    { if (ownTrader) CloseTrader("new-native-open"); else CloseDialogue("new-native-open"); }
                    if (!cycle.Active) cycle.Begin();
                    cycle.MarkOpen();
                    if (ownTrader) { traderAt = host.ElapsedMs; ArmTrader(); } else dialogueAt = host.ElapsedMs;
                }
                Dump(ownTrader, type == 10 ? "NativeWindowOpened" : "NativeWindowClosed", "key=" + key + " payloadRead=false nativeEvent=" + type);
                // Native close already invalidates scope. Do not read a closing model or text widget.
                if (type == 11) { if (ownTrader) traderRetry.Stop(); DiagnoseScope(ownTrader ? trader : dialogue, ownTrader); if (ownTrader) CloseTrader("native-window-close"); else CloseDialogue("native-window-close"); return; }
            }
            if (traderCycle.Active) { traderScopeDigest = null; DiagnoseScope(trader, true); }
            if (dialogueCycle.Active) { dialogueScopeDigest = null; DiagnoseScope(dialogue, false); }
            if (companion)
            {
                if (traderCycle.Active) Dump(true, "CompanionWindowChanged", "key=" + key + " event=" + type + " companionOwnership=UNKNOWN payloadRead=false");
                if (dialogueCycle.Active) Dump(false, "CompanionWindowChanged", "key=" + key + " event=" + type + " companionOwnership=UNKNOWN payloadRead=false");
            }
        }

        private bool MarkSkip(string reason, bool isTrader)
        {
            scopeSkips++; if (isTrader) traderScopeSkips++;
            if (Cycle(isTrader).NewReason(reason))
            {
                host.Trace(TraceLevel.Debug, "ScopeSkipped", "target=" + (isTrader ? "trader" : "dialogue") + " interaction=" + Cycle(isTrader).Number + " reason=" + reason + " payloadRead=false");
                Dump(isTrader, "ScopeSkipped", "reason=" + reason + " payloadRead=false");
            }
            return false;
        }

        // Enumerate only keys already on the open UI stack, never the windows dictionary.
        // Metadata is permitted before the payload gate. Companion payloads are never inspected.
        private bool DiagnoseScope(object controller, bool isTrader, string purpose = "metadata")
        {
            if (!enabled || !host.OnApiThread || !host.WorldReady || !Cycle(isTrader).Active) return false;
            scopeChecks++; if (isTrader) traderScopeChecks++; else dialogueScopeChecks++;
            object manager = Field(0xc16c, null);
            if (manager == null) return MarkSkip("stage=manager-null", isTrader);
            var stack = Field(0xc181, manager) as Stack<string>;
            if (stack == null || stack.Count == 0 || stack.Count > 64) return MarkSkip("stage=stack-null-empty-or-over-64", isTrader);
            var windows = Field(0xc180, manager) as IDictionary;
            if (windows == null) return MarkSkip("stage=window-entry-map-null", isTrader);
            var descriptions = new List<string>();
            object candidateEntry = null, topEntry = null;
            string candidateKey = null, topKey = null; int candidateDepth = -1;
            bool atTop = false, inStack = false;
            int ordinal = 0;
            foreach (string key in stack)
            {
                if (ordinal == 16) { truncations++; descriptions.Add("stackMetadataTruncated=true total=" + stack.Count + " bound=16 remainingEntriesNotRead=true"); break; }
                object entry = key != null && windows.Contains(key) ? windows[key] : null;
                if (ordinal == 0) { topEntry = entry; topKey = key; }
                object entryController = entry == null ? null : Field(0xc184, entry);
                var root = entry == null ? null : Field(0xc185, entry) as GameObject;
                bool match = controller != null && ReferenceEquals(entryController, controller);
                if (match) { inStack = true; if (ordinal == 0) atTop = true; candidateEntry = entry; candidateKey = key; candidateDepth = ordinal; }
                var known = controller == null ? null : Field(isTrader ? 0xc02e : 0xb950, controller) as Transform;
                descriptions.Add("topDown=" + ordinal + " key=" + Safe.Text(key, 100) + " entryType=" + (entry == null ? "unknown" : entry.GetType().FullName) +
                    " entryObject=" + (entry == null || entry.GetType().IsValueType ? "unknown:value-type-box" : Id(entry).ToString()) +
                    " controllerType=" + (entryController == null ? "unknown" : entryController.GetType().FullName) + " controllerObject=" + Id(entryController) +
                    " controllerMatch=" + match + " rootObject=" + Id(root) + " rootTransformObject=" + Id(root == null ? null : root.transform) +
                    " candidateTransformObject=" + Id(known) + " rootTransformMatch=" + (root != null && known != null && root.transform == known) +
                    " activeSelf=" + (root != null && root.activeSelf) + " activeInHierarchy=" + (root != null && root.activeInHierarchy) +
                    " " + PanelDescription(entry, root) + " payloadRead=false localOwnerProof=" + (match ? "candidate-checked-separately" : "unknown:not-read"));
                ordinal++;
            }
            string pointer = PointerMetadata(controller, isTrader);
            descriptions.Add(pointer);
            string stamp = Digest(string.Join("|", descriptions));
            string previous = isTrader ? traderScopeDigest : dialogueScopeDigest;
            if (stamp != previous)
            {
                if (isTrader) traderScopeDigest = stamp; else dialogueScopeDigest = stamp;
                Dump(isTrader, "WindowStackSnapshot", "order=top-down stackCount=" + stack.Count + " stackObject=" + Id(stack) + " candidateInStack=" + inStack + " candidateAtTop=" + atTop + " metadataOnly=true fingerprint=" + stamp);
                foreach (string description in descriptions) Dump(isTrader, "WindowMetadata", description);
            }
            if (controller == null) return MarkSkip("stage=candidate-null-await-own-callback", isTrader);
            var candidateRoot = candidateEntry == null ? null : Field(0xc185, candidateEntry) as GameObject;
            var transform = Field(isTrader ? 0xc02e : 0xb950, controller) as Transform;
            object owner = Field(isTrader ? 0xc02f : 0xb94e, controller);
            int ownerId = owner == null ? -1 : Field<int>(0x1b31, owner);
            string reason = isTrader ? TraderGate(controller, candidateEntry, topEntry, candidateKey, topKey, candidateDepth, stack.Count, atTop, ownerId, candidateRoot, transform, purpose) : WindowScopePolicy.Reject(inStack, atTop,
                candidateRoot != null && transform != null && candidateRoot.transform == transform,
                candidateRoot != null && candidateRoot.activeSelf, candidateRoot != null && candidateRoot.activeInHierarchy,
                PanelReady(candidateEntry, candidateRoot), ownerId, host.LocalPlayerId);
            if (Cycle(isTrader).LastReason != reason)
                Dump(isTrader, "LocalOwnerProof", "source=controller-field-" + (isTrader ? "0400BE2E" : "0400B74D") + "/ResourceSerializer.04001B2A ownerEntityId=" + ownerId + " nativeLocalPlayerId=" + host.LocalPlayerId + " equals=" + (ownerId >= 0 && ownerId == host.LocalPlayerId) + " stageResult=" + (reason ?? "passed") + " payloadRead=false" + (isTrader ? " purpose=" + purpose + " captureInvoked=false" : ""));
            if (reason != null) return MarkSkip(reason, isTrader);
            Cycle(isTrader).NewReason(null); scopePassed++;
            if (isTrader) { if (purpose == "capture-poll") traderPollPasses++; else traderMetadataPasses++; } else dialogueScopePasses++;
            return true;
        }

        private bool PanelReady(object entry, GameObject root)
        {
            if (entry == null || root == null) return false;
            var panels = Field(0xc188, entry) as IList;
            if (panels != null && panels.Count > 32) return false;
            bool ready = false;
            if (panels != null)
                for (int i = 0; i < panels.Count; i++)
                { var p = panels[i] as UIPanel; if (p != null && p.enabled && p.gameObject.activeInHierarchy && p.alpha >= .99f && p.finalAlpha >= .99f) ready = true; }
            if (!ready) { var p = root.GetComponent<UIPanel>(); ready = p != null && p.enabled && p.gameObject.activeInHierarchy && p.alpha >= .99f && p.finalAlpha >= .99f; }
            var canvas = Field(0xc189, entry) as CanvasGroup;
            return ready && (canvas == null || (canvas.enabled && canvas.gameObject.activeInHierarchy && canvas.alpha >= .99f));
        }

        private string PanelDescription(object entry, GameObject root)
        {
            if (entry == null) return "panels=unknown";
            var b = new StringBuilder();
            var panels = Field(0xc188, entry) as IList;
            b.Append("panelCount=").Append(panels == null ? -1 : panels.Count);
            if (panels != null)
            {
                if (panels.Count > 4) { truncations++; b.Append(" panelMetadataTruncated=true bound=4 omittedPanelCount=").Append(panels.Count - 4); }
                for (int i = 0; i < Math.Min(4, panels.Count); i++)
                { var p = panels[i] as UIPanel; if (p != null) b.Append(" panel[").Append(i).Append("]Object=").Append(Id(p)).Append(" enabled=").Append(p.enabled).Append(" active=").Append(p.gameObject.activeInHierarchy).Append(" alpha=").Append(p.alpha.ToString("R", CultureInfo.InvariantCulture)).Append(" finalAlpha=").Append(p.finalAlpha.ToString("R", CultureInfo.InvariantCulture)); }
            }
            if (root != null)
            { var p = root.GetComponent<UIPanel>(); if (p != null) b.Append(" rootPanelObject=").Append(Id(p)).Append(" enabled=").Append(p.enabled).Append(" alpha=").Append(p.alpha.ToString("R", CultureInfo.InvariantCulture)).Append(" finalAlpha=").Append(p.finalAlpha.ToString("R", CultureInfo.InvariantCulture)); }
            var canvas = Field(0xc189, entry) as CanvasGroup;
            if (canvas != null) b.Append(" canvasObject=").Append(Id(canvas)).Append(" enabled=").Append(canvas.enabled).Append(" alpha=").Append(canvas.alpha.ToString("R", CultureInfo.InvariantCulture));
            return b.ToString();
        }

        private static object NguiField(Type type, string name, int token, Type fieldType, object instance)
        {
            FieldInfo f = type.GetField(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null || f.MetadataToken != token || f.FieldType != fieldType || f.DeclaringType.Assembly != typeof(UIPanel).Assembly)
                throw new InvalidOperationException("pinned-ngui-field-contract:" + name);
            return f.GetValue(instance);
        }
        private string PointerMetadata(object controller, bool isTrader)
        {
            // UICamera.hoveredObject getter can clear mHover: read its pinned backing field instead.
            var hovered = NguiField(typeof(UICamera), "mHover", 0x04000456, typeof(GameObject), null) as GameObject;
            var tooltip = NguiField(typeof(UITooltip), "mInstance", 0x0400056c, typeof(UITooltip), null) as UITooltip;
            Transform candidate = controller == null ? null : Field(isTrader ? 0xc02e : 0xb950, controller) as Transform;
            bool child = false; int depth = 0;
            Transform cursor = hovered == null ? null : hovered.transform;
            for (; cursor != null && depth < 8; depth++, cursor = cursor.parent) if (cursor == candidate) { child = true; break; }
            return "pointerSource=NGUIAssembly.04000456 hoveredObject=" + Id(hovered) + " hoveredType=" + (hovered == null ? "unknown" : hovered.GetType().FullName) +
                " hoveredActive=" + (hovered != null && hovered.activeInHierarchy) + " candidateAncestorWithin8=" + child + " ancestryBoundHit=" + (cursor != null && depth == 8) +
                " tooltipSource=NGUIAssembly.0400056C tooltipObject=" + Id(tooltip) + " tooltipActive=" + (tooltip != null && tooltip.gameObject.activeInHierarchy) +
                " tooltipCurrentRaw=" + (tooltip == null ? "unknown" : Safe.Value(NguiField(typeof(UITooltip), "mCurrent", 0x04000576, typeof(float), tooltip))) +
                " tooltipTargetRaw=" + (tooltip == null ? "unknown" : Safe.Value(NguiField(typeof(UITooltip), "mTarget", 0x04000575, typeof(float), tooltip))) +
                " tooltipOwnership=UNKNOWN tooltipTextRead=false inventoryContentsRead=false payloadRead=false";
        }

        private string DialogueLocation()
        {
            object a = Field(0xb955, dialogue), b = Field(0xb956, dialogue);
            int aId = a == null ? -1 : Field<int>(0x1b31, a), bId = b == null ? -1 : Field<int>(0x1b31, b);
            string positionA = a == null ? "unknown" : Position(Field<Vector3>(0x1b41, a));
            string positionB = b == null ? "unknown" : Position(Field<Vector3>(0x1b41, b));
            return "source=active-dialogue-fields contextAObject=" + Id(a) + " contextAEntityId=" + aId + " contextAIsLocalPlayer=" + (aId >= 0 && aId == host.LocalPlayerId) +
                " contextAPosition=" + positionA + " contextBObject=" + Id(b) + " contextBEntityId=" + bId + " contextBIsLocalPlayer=" + (bId >= 0 && bId == host.LocalPlayerId) +
                " contextBPosition=" + positionB + " positionSource=ResourceSerializer.04001B3A/EntityBridge.get_Position entityPositionFieldMapping=static-verified coordinateFrame=UNVERIFIED" +
                " contextEntityRoles=UNVERIFIED-not-assumed-NPC-or-POI npcDisplayNameSource=current-visible-title npcEntityAssociation=UNVERIFIED POI/name=unknown crossLoadIdStability=unknown";
        }
        private static string Position(Vector3 p)
        { return p.x.ToString("R", CultureInfo.InvariantCulture) + "," + p.y.ToString("R", CultureInfo.InvariantCulture) + "," + p.z.ToString("R", CultureInfo.InvariantCulture); }

        private string WidgetMetadata(TMPro.TMP_Text text, string role)
        {
            string readStatus; Displayed(text, out readStatus);
            if (readStatus == "revealed-prefix-glyph-bound-4096") truncations++;
            return role + "ReadStatus=" + readStatus + " " + role + "WidgetObject=" + Id(text) + " enabled=" + (text != null && text.enabled) + " active=" + (text != null && text.gameObject.activeInHierarchy) +
                " maxVisibleCharacters=" + (text == null ? -1 : text.maxVisibleCharacters) + " materializedCharacterCount=" + (text == null || text.textInfo == null ? -1 : text.textInfo.characterCount) +
                " source=known-current-dialogue-widget roleMeaning=UNVERIFIED allPagesComplete=UNKNOWN";
        }

        private string BoundText(string value, int limit = 240)
        { if (value != null && value.Length > limit) truncations++; return Safe.Text(value, limit); }

        private void FinalCounters()
        {
            DirectCounters();
            TraderCounters();
            foreach (MethodSpec spec in BuildContract.Methods)
            { long count, off; lock (hits) { hits.TryGetValue(spec.Token, out count); offThreadHits.TryGetValue(spec.Token, out off); } host.Trace(TraceLevel.Info, "HookCounterFinal", "token=0x" + spec.Token.ToString("X8") + " callbacks=" + count + " offApiThreadRejected=" + off + " installed=" + installedTokens.Contains(spec.Token) + " exact=true rateLimitedHookLinesAreNotCallCounts=true"); }
            host.Trace(TraceLevel.Info, "ObserverCounters", "scopeChecks=" + scopeChecks + " scopePassed=" + scopePassed + " scopeSkips=" + scopeSkips +
                " modelCaptures=" + modelCaptures + " publishedRowCandidates=" + publishedRowsCaptured + " rawEntryCandidates=" + rawEntriesCaptured +
                " unchangedModelPolls=" + unchangedModels + " dialogueSamples=" + dialogueCaptures + " dumpEmitted=" + dumpBudget.Captured + " dumpDropped=" + dumpBudget.Dropped +
                " truncations=" + truncations + " hooksWorldNotReady=" + worldNotReadyHooks + " loggerMayIndependentlyDrop=true loggerCountersSeparate=true" +
                " dumpLimits=1024-per-second/4096-per-interaction/24000-per-session/512-interactions/16-stack-entries/4-panel-metadata/32-panel-gate/8-ancestors/5600-json-chars");
        }
    }
}
