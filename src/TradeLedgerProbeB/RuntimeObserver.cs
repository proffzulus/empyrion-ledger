using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace TradeLedgerProbeB.Runtime
{
    // This assembly is loaded only after opt-in and the loader's strict hash checks.
    public sealed partial class RuntimeObserver : IRuntimeObserver
    {
        private const string Owner = "TradeLedgerProbeB.experimental.5229";
        private static RuntimeObserver current;
        private IProbeHost host;
        private Module module;
        private Harmony harmony;
        private HookPatchSession patchSession;
        private readonly HashSet<int> installedTokens = new HashSet<int>();
        private readonly Dictionary<int, FieldInfo> fields = new Dictionary<int, FieldInfo>();
        private readonly ConditionalWeakTable<object, Tag> ids = new ConditionalWeakTable<object, Tag>();
        private int nextId;
        private bool enabled, traderWasVisible, dialogueWasVisible;
        private object trader, dialogue;
        private long traderAt, dialogueAt, lastPoll = -1000, lastChoiceAt;
        private long lastUnchangedDump = -5000;
        private long choice;
        private string dialogueHash, lastReadiness;
        private readonly Dictionary<int, long> hits = new Dictionary<int, long>();
        private readonly Dictionary<int, long> offThreadHits = new Dictionary<int, long>();
        private readonly Dictionary<int, long> lastHitLog = new Dictionary<int, long>();
        private sealed class Tag { internal int Value; }
        private int Id(object value)
        {
            if (value == null) return 0;
            return ids.GetValue(value, key => { int n = Interlocked.Increment(ref nextId); if (n > 16384) { truncations++; if (n == 16385) host.Trace(TraceLevel.Warn, "ObjectIdLimit", "bound=16384 furtherObjectIds=unknown-zero interactionIdsRemainAvailable=true"); return new Tag(); } return new Tag { Value = n }; }).Value;
        }

        public void Start(IProbeHost owner, Assembly game)
        {
            host = owner; module = game.ManifestModule;
            if (!host.OnApiThread || current != null)
            { host.Trace(TraceLevel.Error, "HookSkipped", "reason=adapter-thread-or-owner-conflict"); throw new InvalidOperationException("adapter-thread-or-owner"); }
            LedgerNativeContracts.Require(game,"runtime-core");
            if (Harmony.DEBUG)
            { host.Trace(TraceLevel.Error, "HookSkipped", "reason=global-harmony-debug-already-enabled"); throw new InvalidOperationException("global-harmony-debug-enabled"); }
            foreach (var spec in BuildContract.Fields)
            {
                FieldInfo field = LedgerNativeContracts.Field(module,spec.Token);
                if (field.DeclaringType.FullName != spec.Type || field.Name != spec.Name || field.FieldType.Name != spec.FieldType)
                {
                    host.Trace(TraceLevel.Error, "HookSkipped", "reason=field-contract token=0x" + spec.Token.ToString("X8"));
                    throw new InvalidDataException("field-contract");
                }
                fields.Add(spec.Token, field);
            }
            host.Trace(TraceLevel.Info, "HookRuntime", "runtime=" + Safe.Text(Environment.Version.ToString()) +
                " mono=" + (Type.GetType("Mono.Runtime") != null) + " pointerBytes=" + IntPtr.Size +
                " harmony=" + Safe.Text(typeof(Harmony).Assembly.FullName) + " adapter=" + Safe.Text(typeof(RuntimeObserver).Assembly.FullName) + " patchTiming=world-ready-update");
            var targets = new List<MethodInfo>();
            foreach (var spec in BuildContract.Methods)
            {
                MethodInfo target = LedgerNativeContracts.Method(module,spec.Token) as MethodInfo;
                if (target == null || (!HookContract.Matches(target, spec) && !LedgerNativeContracts.Bound(target,spec.Token)))
                { host.Trace(TraceLevel.Error, "HookSkipped", "reason=method-contract token=0x" + spec.Token.ToString("X8")); throw new InvalidDataException("method-contract"); }
                targets.Add(target);
                host.Trace(TraceLevel.Debug, "HookValidated", "token=0x" + LedgerNativeContracts.Token(target).ToString("X8") + " type=" + Safe.Text(spec.Type) + " method=" + Safe.Text(spec.Name));
            }
            harmony = new Harmony(Owner);
            // Optional diagnostics compile/execute only owned copies and install no detours.
            // Every actual target must independently pass metadata and copy-compilation checks.
            HarmonyCanary.Verify(host, harmony);
            patchSession = HarmonyCanary.CreateSession(host, harmony);
            foreach (MethodInfo target in targets)
            {
                Patches existing = Harmony.GetPatchInfo(target);
                if (existing != null && existing.Owners.Count != 0)
                { host.Trace(TraceLevel.Warn, "HookSkipped", "reason=target-already-patched token=0x" + LedgerNativeContracts.Token(target).ToString("X8")); continue; }
                if (patchSession.TryInstall(target, PostfixFor(LedgerNativeContracts.Token(target)))) installedTokens.Add(LedgerNativeContracts.Token(target));
                if (patchSession.UnsafeState) throw new InvalidOperationException("individual-patch-cleanup-uncertain");
            }
            enabled = installedTokens.Count != 0;
            current = enabled ? this : null;
            // Accounting-only hooks retired. Original eight quote/model/refresh hooks and
            // independent trader polling remain; hand-in hooks are separate.
            if(enabled)StartTradeHooks();
            if(enabled)StartHandInHooks();
            if(enabled)StartProductionVisible();
            host.Trace(TraceLevel.Warn, "HookInstallSummary", "installed=" + installedTokens.Count + " targets=8 failedOrSkipped=" +
                (8 - installedTokens.Count) + " retainedIndependentSuccesses=" + enabled + " perTargetCallbacksGuarded=true");
            host.Trace(TraceLevel.Warn, enabled ? "RuntimeObserverEnabled" : "HookSkipped", "experimental=true installed=" + installedTokens.Count +
                " correctness=UNVERIFIED limits=256-rows/32-options/4096-text-chars requestCriterion=shown/heard acceptanceNotRequired=true nativeDiagnosticsContinue=true");
        }

        public static MethodInfo PostfixFor(int token)
        {
            string name;
            switch (token)
            {
                case 0x0600cd1c: name = "TraderOpened"; break;
                case 0x0600cd1f: name = "TraderRows"; break;
                case 0x0600cd1e: name = "TraderModel"; break;
                case 0x0600cd26: name = "TraderQuote"; break;
                case 0x0600cd1d: name = "TraderLifecycle"; break;
                case 0x0600c7ba: name = "DialogueDisplay"; break;
                case 0x0600c7b1: name = "DialogueLifecycle"; break;
                case 0x06006a76: name = "DialogueAnswer"; break;
                default: throw new InvalidOperationException("postfix-token-not-allowlisted");
            }
            return typeof(RuntimeObserver).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        }
        // Minimal void signatures: no __args array, __originalMethod injection, ref/result writes or game calls.
        private static void TraderOpened(object __instance) { Dispatch(__instance, 0x0600cd1c, null); }
        private static void TraderRows(object __instance) { Dispatch(__instance, 0x0600cd1f, null); }
        private static void TraderModel(object __instance) { Dispatch(__instance, 0x0600cd1e, null); }
        private static void TraderQuote(object __instance) { Dispatch(__instance, 0x0600cd26, null); }
        private static void TraderLifecycle(object __instance, bool __0) { Dispatch(__instance, 0x0600cd1d, __0); }
        private static void DialogueDisplay(object __instance) { Dispatch(__instance, 0x0600c7ba, null); }
        private static void DialogueLifecycle(object __instance, bool __0) { Dispatch(__instance, 0x0600c7b1, __0); }
        private static void DialogueAnswer(object __instance, int __0) { Dispatch(__instance, 0x06006a76, __0); }
        private static void Dispatch(object instance, int token, object firstArgument)
        {
            RuntimeObserver observer = current;
            if (observer == null || !observer.enabled || !observer.installedTokens.Contains(token)) return;
            try { observer.After(instance, token, firstArgument); }
            catch (Exception error) { observer.Disable("postfix", error); }
        }

        private void After(object instance, int token, object firstArgument)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.ObserverHook))
            {
            long count;
            lock (hits) { hits.TryGetValue(token, out count); hits[token] = ++count; }
            if (!host.OnApiThread) { lock (hits) { long off; offThreadHits.TryGetValue(token, out off); offThreadHits[token] = off + 1; } host.Trace(TraceLevel.Warn, "HookSkipped", "reason=off-api-thread token=0x" + token.ToString("X8")); return; }
            lastHookToken = token;
            if (!host.WorldReady) { worldNotReadyHooks++; return; }
            long last; lastHitLog.TryGetValue(token, out last);
            if (host.ElapsedMs - last >= 500 || count == 1)
            {
                lastHitLog[token] = host.ElapsedMs;
                host.Trace(TraceLevel.Debug, "HookFired", "token=0x" + token.ToString("X8") + " cumulative=" + count + " payloadDeferredUntilVisible=true");
            }
            if (token == 0x06006a76)
            {
                if (choice != 0 && host.ElapsedMs - lastChoiceAt <= 5000)
                    host.Trace(TraceLevel.Info, "DialogueTransitionReturned", "choiceCorrelation=" + choice +
                        " zeroBasedDisplayedIndex=" + Safe.Value(firstArgument) +
                        " methodReturnPostfixObserved=true transitionCompletion=UNVERIFIED accepted=UNKNOWN stateAndFutureBranchesNotRead=true");
                return;
            }
            bool close = (token == 0x0600cd1d || token == 0x0600c7b1) && firstArgument is bool && !(bool)firstArgument;
            if (token >= 0x0600cd1c && token <= 0x0600cd26)
            {
                if (close) { CloseTrader("native-close-callback"); return; }
                traderLastHookAt = host.ElapsedMs; traderLastHookToken = token;
                Acquire(instance, true);
            }
            else
            {
                if (close) { CloseDialogue("native-close-callback"); return; }
                Acquire(instance, false);
            }

            }
        }

        private object Field(int row, object instance)
        { int token=0x04000000+row;FieldInfo field;if(!fields.TryGetValue(token,out field)){field=LedgerNativeContracts.Field(module,token);fields.Add(token,field);}return field.GetValue(instance); }
        private T Field<T>(int row, object instance) { return (T)Field(row, instance); }

        private bool VisibleScope(object controller, bool isTrader)
        { return DiagnoseScope(controller, isTrader); }

        public void Poll()
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.ObserverPoll))
            {
            if (!enabled || !host.OnApiThread) return;
            if (!host.WorldReady) { ResetWorld(); return; }
            if (host.ElapsedMs - lastPoll < 250) return;
            lastPoll = host.ElapsedMs;
            try
            {
                PollTrader();
                bool dialogueScope = VisibleScope(dialogue, false);
                if (dialogueScope) { ObserveDialogue(); ObserveDirectDialogue(); }
                else if (dialogueCycle.Active && host.ElapsedMs - dialogueAt > 30000) CloseDialogue("scope-unproved-timeout-30s");
            }
            catch (Exception error) { Disable("poll", error); }

            }
        }

        private static bool VisibleText(Behaviour widget)
        {
            return widget != null && widget.enabled && widget.gameObject.activeInHierarchy;
        }
        private static string Label(UILabel label)
        { return VisibleText(label) && label.isVisible ? label.text : null; }
        private static string Displayed(TMP_Text text)
        { string status; return Displayed(text, out status); }
        private static string Displayed(TMP_Text text, out string status)
        {
            status = "widget-null-disabled-or-inactive";
            if (!VisibleText(text)) return null;
            status = "widget-alpha-not-ready";
            if (text.color.a < 0.99f) return null;
            var info = text.textInfo;
            status = "text-info-empty-or-typewriter-zero";
            if (info == null || info.characterCount == 0 || text.maxVisibleCharacters <= 0) return null;
            status = "page-line-word-or-first-character-limits-unsupported";
            if (text.firstVisibleCharacter != 0 || text.maxVisibleLines < info.lineCount ||
                text.maxVisibleWords < info.wordCount || text.overflowMode == TextOverflowModes.Page) return null;
            status = "glyph-info-unavailable";
            if (info.characterInfo == null) return null;
            int visible = Math.Min(info.characterCount, text.maxVisibleCharacters);
            // Only map already revealed glyphs. New .text with stale mesh metadata is rejected.
            return RevealedText.Read(text.text, visible, info.characterInfo.Length, i => {
                var g = info.characterInfo[i]; return new VisibleGlyph(g.index, g.stringLength, g.character);
            }, out status);
        }
        private static string Digest(string text)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }

        private TraderReadResult ObserveTrader()
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.TraderCapture))
            {
            // This method is reached only after VisibleScope passed; no world-wide model acquisition exists.
            object model = Field(0xc031, trader);
            traderReadDetail = "modelObject=" + Id(model) + " modelType=" + (model == null ? "null" : model.GetType().FullName) + " modelNull=" + (model == null);
            if (model == null) return TraderReadResult.ModelMissing;
            bool processing = Field<bool>(0x5341, model);
            object listComponent = Field(0xc01c, trader);
            var rows = listComponent == null ? null : Field(0x699f, listComponent) as IList;
            traderReadDetail += " rowListOwnerObject=" + Id(listComponent) + " rowListSource=0400BE1B/04006960 rowsNull=" + (rows == null) + " rowCount=" + (rows == null ? -1 : rows.Count) + " bound=256 processing=" + processing;
            if (rows == null || rows.Count > 256) { if (rows != null) truncations++; return TraderReadResult.RowsMissing; }
            object currentController = trader;
            long directInteraction = traderCycle.Number;
            OfferFrame directFrame = Direct.Begin(currentController, true, () => TraderLease(currentController) && traderCycle.Number == directInteraction);
            if (!ReferenceEquals(directFrame.Model, model) || !ReferenceEquals(directFrame.Rows, rows))
            { directFrame.Admitted = false; directFrame.State = "model-or-list-changed-before-direct-frame"; }
            directFrame.Freshness = Direct.RefreshSchedule(directFrame);
            var directVisible = new List<PublishedOffer>();
            var directBindings = new List<OfferBinding>();
            if (!traderWasVisible)
            {
                traderWasVisible = true;
                host.Trace(TraceLevel.Info, "TraderOpenedVisible", "windowObject=" + Id(trader) + " modelObject=" + Id(model) + " owner=local scope=" + traderScopeMode + "/active-root/alpha-ready");
                Dump(true, "TraderOpenedVisible", "payloadGate=passed modelObject=" + Id(model));
            }
            var rowLines = new List<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                object row = rows[i];
                if (row == null || row.GetType().FullName != LedgerTraderTypes.Row)
                    throw new InvalidDataException("unexpected-published-row-type");
                PublishedOffer published = Direct.Published(row);
                directVisible.Add(published);
                try { directBindings.Add(Direct.Bind(directFrame, row, i, published)); }
                catch (Exception error) { directFailures++; directBindings.Add(new OfferBinding { Reason = "contained-read-" + error.GetType().Name }); }
                rowLines.Add("uiRow=" + i + " rowObject=" + Id(row) +
                    " name6938=" + BoundText(Field<string>(0x6977, row)) + " name6939=" + BoundText(Field<string>(0x6978, row)) +
                    " columnBE37=" + BoundText(Field<string>(0xc038, row)) + " columnBE38=" + BoundText(Field<string>(0xc039, row)) +
                    " priceColumnBE39=" + BoundText(Field<string>(0xc03a, row)) + " priceColumnBE3A=" + BoundText(Field<string>(0xc03b, row)) +
                    " flagBE3B=" + Field<bool>(0xc03c, row) + " flagBE3C=" + Field<bool>(0xc03d, row) + " flagBE3D=" + Field<bool>(0xc03e, row) +
                    " variantBE3E=" + Field<int>(0xc03f, row) + " interpretation=UNVERIFIED directionA/directionB/current/capacity-not-certified");
            }
            var rawLines = new List<string>();
            var raw = Field(0x533e, model) as IList;
            traderReadDetail += " rawModelNull=" + (raw == null) + " rawModelCount=" + (raw == null ? -1 : raw.Count);
            var search = Field(0xc021, trader) as UIInput;
            bool searchEmpty = search != null && string.IsNullOrEmpty(search.value);
            int eligible = 0;
            if (raw != null && raw.Count <= 256 && searchEmpty && !processing)
            {
                for (int i = 0; i < raw.Count; i++)
                {
                    object entry = raw[i];
                    RawOfferCandidate candidate = RawOfferCandidate.ReadAvailable(token => Field<int>(token, entry));
                    if (candidate == null) continue;
                    eligible++;
                    rawLines.Add("availableModelIndex=" + i + " entryObject=" + Id(entry) + " " + candidate.Describe());
                }
            }
            bool safeModel = raw != null && raw.Count <= 256 && ObservationRules.SafeModel(rows.Count, eligible, processing, searchEmpty);
            directFrame.ComparisonComplete = safeModel && searchEmpty;
            if (!safeModel) rawLines.Clear();
            string quoteA = Label(Field(0xc027, trader) as UILabel), quoteB = Label(Field(0xc028, trader) as UILabel);
            var quantityInput = Field(0xc022, trader) as UIInput;
            string header = "windowObject=" + Id(trader) + " modelObject=" + Id(model) + " publishedRows=" + rows.Count +
                " processing=" + processing + " rawModelGate=" + safeModel + " searchEmpty=" + searchEmpty +
                " rawCount=" + (raw == null ? -1 : raw.Count) + " rawEligibilityEvaluated=" + (raw != null && raw.Count <= 256 && searchEmpty && !processing) + " eligibleCountCandidate=" + eligible +
                " rawContext5249=" + Safe.Value(Field(0x5262, model)) + " rawContext524A=" + Safe.Value(Field(0x5263, model)) +
                " rawContext524E=" + Safe.Value(Field(0x5267, model)) + " factorBE2F=" + Field<float>(0xc030, trader).ToString("R", CultureInfo.InvariantCulture) +
                " visibleQuoteA=" + Safe.Text(quoteA) + " visibleQuoteB=" + Safe.Text(quoteB) +
                " quantityInputCandidate=" + Safe.Text(!VisibleText(quantityInput) ? null : quantityInput.value) +
                " POI/traderIdentity=UNVERIFIED quantities/prices/directions=UNVERIFIED viewport=available-current-window-rows-not-global";
            string readiness = "modelObject=" + Id(model) + " publishedRows=" + rows.Count + " processing=" + processing + " rawGate=" + safeModel;
            if (readiness != lastReadiness) { lastReadiness = readiness; host.Trace(TraceLevel.Debug, "ModelReadiness", readiness); Dump(true, "ModelReadiness", readiness); }
            if (!processing && directFrame.Admitted && !Direct.ScheduleCurrent(directFrame, directFrame.Freshness))
            { traderStability.LoseScope(); return TraderReadResult.WaitingStable; }
            string digest = Digest(header + string.Join("|", rowLines) + string.Join("|", rawLines) + DirectFrameStamp(directFrame, directBindings) + directFrame.Freshness.StabilityKey());
            string previousSnapshot = traderStability.Current;
            TraderReadResult result = traderStability.Sample(digest, processing);
            if (result == TraderReadResult.Processing || result == TraderReadResult.WaitingStable) return result;
            if (result == TraderReadResult.Unchanged) { unchangedModels++; if (host.ElapsedMs - lastUnchangedDump >= 5000) { lastUnchangedDump = host.ElapsedMs; Dump(true, "ModelUnchanged", "snapshot=" + digest + " cumulativeUnchangedPolls=" + unchangedModels + " transaction=UNKNOWN"); } return result; }
            bool first = previousSnapshot == null; modelCaptures++;
            if (rows.Count == 0) traderEmptySnapshots++;
            if (raw != null && raw.Count == 0) traderEmptyModels++;
            Dump(true, first ? "ModelReadyCandidate" : "ModelRefreshStableCandidate", header + " previousSnapshot=" + (previousSnapshot ?? "unknown:first") + " snapshot=" + digest + " settlement=UNKNOWN emptyList=" + (rows.Count == 0));
            host.Trace(TraceLevel.Info, first ? "ModelReadyCandidate" : "ModelRefreshStableCandidate", header + " stableSamples=2 serverSettlement=UNKNOWN snapshot=" + digest);
            foreach (string line in rowLines) { publishedRowsCaptured++; Dump(true, "TraderPublishedRow", line); }
            foreach (string line in rawLines) { rawEntriesCaptured++; Dump(true, "TraderRawAvailableEntry", line); }
            EmitDirectTrader(directFrame, directVisible, directBindings, digest);
            if (!safeModel) host.Trace(TraceLevel.Warn, "RawModelSkipped", "reason=processing/search/bound/published-count-mismatch no-hidden-model-entries-emitted=true");
            return TraderReadResult.Captured;

            }
        }

        private void ObserveDialogue()
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.DialogueCapture))
            {
            if (!dialogueWasVisible)
            {
                dialogueWasVisible = true;
                host.Trace(TraceLevel.Info, "DialogueOpenedVisible", "windowObject=" + Id(dialogue) + " owner=local");
            }
            string state = Field<string>(0xb93c, dialogue);
            string title = Displayed(Field(0xb932, dialogue) as TMP_Text);
            string body = Displayed(Field(0xb933, dialogue) as TMP_Text);
            var options = Field(0xb936, dialogue) as IList;
            if (options != null && options.Count > 32) throw new InvalidDataException("options-bound");
            var displayed = new List<string>();
            if (options != null)
                for (int i = 0; i < options.Count; i++)
                {
                    string text = Displayed(options[i] as TMP_Text);
                    if (!string.IsNullOrEmpty(text)) displayed.Add("displayedIndex=" + (i + 1) + " text=" + BoundText(text, 512));
                }
            string boundedTitle = title == null ? null : title.Substring(0, Math.Min(title.Length, 4096));
            string boundedBody = body == null ? null : body.Substring(0, Math.Min(body.Length, 4096));
            string digest = Digest((state ?? "") + "|" + boundedTitle + "|" + boundedBody + string.Join("|", displayed));
            if (digest == dialogueHash) { if (boundedBody != null) { dialoguePages.Add(state, boundedBody, host.ElapsedMs); if (dialoguePages.StableSamples == 4) Dump(false, "DialogueStabilitySample", "page=" + dialoguePages.Page + " stableSamples=4 completeness=UNKNOWN pauseIsNotCompletion=true"); } return; }
            if (title == null && body == null && displayed.Count == 0) return;
            dialogueHash = digest; dialogueCaptures++;
            bool pageBoundary = boundedBody != null && dialoguePages.Add(state, boundedBody, host.ElapsedMs);
            Dump(false, "DialoguePageSample", "page=" + dialoguePages.Page + " sample=" + dialoguePages.Sample + " pageBoundaryCandidate=" + pageBoundary + " state=" + Safe.Text(state) + " stableSamples=" + dialoguePages.StableSamples + " titleAvailable=" + (boundedTitle != null) + " bodyAvailable=" + (boundedBody != null) + " requestSuccessCriterion=shown-not-accepted completeness=UNKNOWN typewriterPauseDoesNotCompletePage=true optionWidgets=" + displayed.Count + " " + DialogueLocation());
            Dump(false, "DialogueWidgetMetadata", WidgetMetadata(Field(0xb932, dialogue) as TMP_Text, "title") + " " + WidgetMetadata(Field(0xb933, dialogue) as TMP_Text, "body"));
            object npcA = Field(0xb955, dialogue), npcB = Field(0xb956, dialogue);
            host.Trace(TraceLevel.Info, "DialogueDisplaySample", "windowObject=" + Id(dialogue) + " state=" + Safe.Text(state) +
                " contextEntityA=" + (npcA == null ? -1 : Field<int>(0x1b31, npcA)) + " contextEntityB=" + (npcB == null ? -1 : Field<int>(0x1b31, npcB)) +
                " identityAndTitleBodyRoles=UNVERIFIED heard=display-sample-only accepted=UNKNOWN choiceCorrelation=" +
                (host.ElapsedMs - lastChoiceAt <= 5000 ? choice : 0) + " correlation=temporal-not-proof");
            TextChunks("DialogueVisibleTitle", boundedTitle);
            TextChunks("DialogueVisibleBody", boundedBody);
            foreach (string option in displayed) Dump(false, "DialogueVisibleOption", "page=" + dialoguePages.Page + " " + option);
            foreach (string span in ObservationRules.CompoundTextCandidates(boundedBody))
                host.Trace(TraceLevel.Warn, "CompoundRequirementTextCandidate", "literalSpan=" + Safe.Text(span) + " itemId=UNKNOWN quantityMeaning=UNVERIFIED conjunction=UNVERIFIED accepted=UNKNOWN source=current-visible-body");
            if ((title != null && title.Length > 4096) || (body != null && body.Length > 4096))
            { truncations++; host.Trace(TraceLevel.Warn, "DialogueTextTruncated", "limit=4096 completeness=UNKNOWN"); }

            }
        }

        private void TextChunks(string kind, string text)
        {
            if (text == null) return;
            for (int offset = 0; offset < text.Length; offset += 512)
                Dump(false, kind, "page=" + dialoguePages.Page + " sample=" + dialoguePages.Sample + " offset=" + offset + " capturedChars=" + text.Length + " text=" + text.Substring(offset, Math.Min(512, text.Length - offset)));
        }

        public void NativeEvent(int type, object arg1, object arg2)
        {
            if (!enabled || !host.OnApiThread) return;
            try
            {
                if ((type == 10 || type == 11) && arg1 is string) NativeWindow(type, (string)arg1);
                if (type == 72 && VisibleScope(dialogue, false))
                {
                    ObserveDialogue(); // Capture current shown fragment before choice execution; capture does not require a choice.
                    if (!ObservationRules.MatchesChoiceState(Field<string>(0xb93c, dialogue), arg1, arg2))
                    {
                        choice = 0; lastChoiceAt = 0;
                        host.Trace(TraceLevel.Warn, "DialogueChoiceCorrelationSkipped", "reason=event-state-or-index-does-not-match-current-local-display accepted=UNKNOWN");
                        return;
                    }
                    int index = (int)arg2;
                    string selectedText = null;
                    var options = Field(0xb936, dialogue) as IList;
                    if (index > 0 && options != null && options.Count <= 32 && index <= options.Count)
                        selectedText = Displayed(options[index - 1] as TMP_Text);
                    choice++; lastChoiceAt = host.ElapsedMs;
                    host.Trace(TraceLevel.Info, "DialogueChoicePreExecution", "choiceCorrelation=" + choice + " state=" + Safe.Value(arg1) +
                        " displayedIndex=" + Safe.Value(arg2) + " selectedMaterializedText=" + Safe.Text(selectedText, 512) +
                        " classification=" + (index == 0 ? "cancel-candidate" : "selection-intent-not-acceptance") + " accepted=UNKNOWN");
                }
            }
            catch (Exception error) { Disable("native-correlation", error); }
        }
        private void CloseTrader(string reason)
        {
            if(traderCycle.Active&&traderStability.Current==null)try{host.Trace(TraceLevel.Warn,"TraderCaptureSkipped","interaction="+traderCycle.Number+" reason="+(traderCycle.LastReason??"no-complete-stable-market")+" payloadAttempts="+traderInteractionPayloadAttempts+" result="+traderLastRead+" detail="+traderReadDetail+" close="+reason+" noJournalWrite=true");}catch(Exception){}
            if (traderCycle.Active) Dump(true, "TraderClosed", "windowObject=" + Id(trader) + " wasVisible=" + traderWasVisible + " lastSnapshot=" + (traderStability.Current ?? "unknown:none") + " reason=" + reason);
            traderRetry.Stop(); traderCycle.End(); traderScopeDigest = traderProofDigest = null; traderLastRead = null;
            trader = null; traderWasVisible = false; lastReadiness = null; traderStability = new TraderSnapshotStability();
            directSemantic = null;
        }
        private void CloseDialogue(string reason)
        {
            CloseHandIn();
            if (dialogueCycle.Active) { Dump(false, "DialoguePageEnd", "page=" + dialoguePages.Page + " sample=" + dialoguePages.Sample + " stableSamples=" + dialoguePages.StableSamples + " completeness=UNKNOWN retainedPriorPages=in-log reason=" + reason); Dump(false, "DialogueClosed", "windowObject=" + Id(dialogue) + " wasVisible=" + dialogueWasVisible + " reason=" + reason); }
            dialogueCycle.End(); dialogueScopeDigest = null; dialoguePages = new DialoguePages();
            dialogue = null; dialogueWasVisible = false; dialogueHash = null;
        }
        public void ResetWorld()
        {
            handInSkipCount=0;
            ForgetHandIn("world-context-cleared");
            activeTrade=null; CloseTrader("world-context-cleared"); CloseDialogue("world-context-cleared"); choice = 0; lastChoiceAt = 0; }
        private void Disable(string where, Exception error)
        {
            enabled = false; current = null;
            try { host.Trace(TraceLevel.Error, "AdapterAutoDisabled", "where=" + where + " errorType=" + error.GetType().Name + " nativeDiagnosticsContinue=true"); ExceptionTrace.Write(host, where, error, "callbackIsNoOp=true"); ResetWorld(); }
            catch (Exception) { }
            // Keep no-op postfixes until orderly shutdown rather than detouring a method from inside its own callback.
        }
        public void Dispose()
        {
            productionLifecycleEnabled=false;if(productionLifecyclePatch!=null)productionLifecyclePatch.Dispose();
            productionVisibleEnabled=false;if(productionPatch!=null)productionPatch.Dispose();
            StopHandInHooks();
            StopTradeHooks();
            enabled = false; if (ReferenceEquals(current, this)) current = null;
            ResetWorld();
            FinalCounters();
            installedTokens.Clear();
            if (patchSession != null) patchSession.Dispose();
        }
    }
}
