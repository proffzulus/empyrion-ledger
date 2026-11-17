using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using Eleon.Modding;
using TradeLedgerProbe;

namespace TradeLedgerProbeB
{
    public sealed partial class Probe : IMod, IProbeHost, IObservationContext, ILedgerHost
    {
        private readonly object gate = new object();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly string session = Guid.NewGuid().ToString("N");
        private IRuntimeObserver observer;
        private bool observerAttempted, runtimeEnabled, worldReady, worldLeft;
        private int localId = -1;
        private IModApi api;
        private IApplication application;
        private IDiagnosticLog log;
        private readonly LedgerDiagnostics diagnostics = new LedgerDiagnostics();
        private bool initialized, stopped, entered;
        private bool eventBound, enteredBound, loadedBound, unloadingBound, updateBound;
        private int apiThread;
        private long lastPoll = -1000, lastHeartbeat = -30000, contextAt;
        private string ticks = "unavailable", context = "unavailable", lastContext, lastPlayfield;
        private string lastScopeKey;
        private long suppressedFieldMappings, suppressedReplacementOperands;
        private readonly string primaryLogRoot;
        private readonly string fallbackLogRoot;

        private bool gameLoaderInstance;private IDisposable loaderLease;
        public Probe() : this(LedgerInstallPaths.Current.Logs, null, LedgerInstallPaths.Current.LegacyRecovery) { gameLoaderInstance=true; }

        // Used only by the offline harness; the game loader uses the public parameterless constructor.
        internal Probe(string primary, string fallback)
            : this(primary, fallback, Path.Combine(Path.GetDirectoryName(primary), "ledger-fixture")) { }
        internal Probe(string primary, string fallback, string dataRoot)
        { primaryLogRoot = primary; fallbackLogRoot = fallback; ledgerRoot = dataRoot; }

        public void Init(IModApi modApi)
        {
            lock (gate)
            {
                if (initialized) return;
                initialized = true; api = modApi; apiThread = Thread.CurrentThread.ManagedThreadId;
                if(gameLoaderInstance&&LedgerInstallPaths.Current.PublicRelease){
                    if(LedgerInstallPaths.Current.LegacyPackagePresent||LedgerLoaderLease.LegacyLoaded()||(loaderLease=LedgerLoaderLease.Acquire())==null){stopped=true;Announce("Duplicate Trade Ledger loader detected. Close the game and remove the older mod DLL through the checked installer. This copy is disabled.");return;}
                }
                LedgerNativeContracts.Report=Write;
                try
                {
                    string selection = "installed-mod-operational-v1";
                    diagnostics.Minimum = LedgerDiagnosticSettings.Read().Level;
                    try { log = LedgerDiagnostics.Open(primaryLogRoot, session); }
                    catch (LogBudgetException) { Announce("Diagnostic log budget reached; Ledger remains available. Existing logs are preserved."); }
                    catch (Exception error)
                    {
                        selection = "local-app-data primaryFailure=" + error.GetType().Name;
                        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        string fallback=fallbackLogRoot??(string.IsNullOrEmpty(local)?null:Path.Combine(local,"TradeLedger","logs"));
                        try{if(fallback!=null)log = LedgerDiagnostics.Open(fallback, session);else Announce("Diagnostic log unavailable; Ledger remains available.");}
                        catch(Exception fallbackError){Announce("Diagnostic log unavailable: "+fallbackError.GetType().Name+"; Ledger remains available.");}
                    }
                    Write("Init", "TradeLedger=1.0.0 target=1.20.1/5232 dumpFormat=prefixed-flat-JSON-in-same-log");
                    Write("LogSelected", "location=" + Compact.Text(log == null ? null : log.Path, 800) +
                        " selection=" + selection + " sessionLimit=2097152 folderLimit=16777216 fileLimit=8 threshold=" + diagnostics.Minimum + " legacyEvidencePreserved=true");
                    Write("BuildFingerprint", "id=" + ProbeBuild.Fingerprint + " expectedModApi=" + ProbeBuild.ModApiHash +
                        " expectedMif=" + ProbeBuild.MifHash + " expectedRuntime=" + ProbeBuild.RuntimeHash);
                    Write("LoadedFingerprint", "Probe=" + AssemblyHash(typeof(Probe).Assembly) + " ModApi=" + AssemblyHash(typeof(IMod).Assembly) +
                        " Mif=" + AssemblyHash(typeof(CmdId).Assembly) +
                        " UnityCore=" + AssemblyHash(typeof(UnityEngine.Vector3).Assembly));
                    Write("BuildNumber", "value=" + Compact.Read(() => LedgerInstallPaths.Current.GameRoot==null?"install-root-unavailable":File.ReadAllText(Path.Combine(LedgerInstallPaths.Current.GameRoot, "BuildNumber.txt"))));
                    application = api == null ? null : api.Application;
                    Capture("Init", true);
                    runtimeEnabled = RuntimeBootstrap.OptedIn();
                    LedgerProfiler.Configure(log==null?primaryLogRoot:Path.GetDirectoryName(Path.GetDirectoryName(log.Path)),ProbeBuild.Fingerprint,ProbeBuild.RuntimeHash,apiThread,runtimeEnabled,diagnostics.Minimum);
                    StartLedger();
                    Write("HookSkipped", "reason=" + (runtimeEnabled ? "waiting-for-world-ready-update" : RuntimeBootstrap.OptInReason));
                    Bind("GameEvent", () => { api.GameEvent += OnGameEvent; eventBound = true; });
                    if (application != null)
                    {
                        Bind("GameEntered", () => { application.GameEntered += OnGameEntered; enteredBound = true; });
                        Bind("OnPlayfieldLoaded", () => { application.OnPlayfieldLoaded += OnPlayfieldLoaded; loadedBound = true; });
                        Bind("OnPlayfieldUnloading", () => { application.OnPlayfieldUnloading += OnPlayfieldUnloading; unloadingBound = true; });
                        Bind("Update", () => { application.Update += OnUpdate; updateBound = true; });
                    }
                    else Write("ApplicationUnavailable", "cannotSubscribeLifecycle=true");
                    Announce("Trade Ledger session=" + session + " log=" + (log == null ? "DISABLED" : log.Path));
                }
                catch (Exception error) { Write("InitError", "type=" + error.GetType().Name); Announce("Init failed: " + error.GetType().Name); }
            }
        }

        private static string AssemblyHash(Assembly assembly)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var file = File.OpenRead(assembly.Location))
                    return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
            }
            catch (Exception error) { return "unavailable:" + error.GetType().Name; }
        }

        private void Bind(string name, Action action)
        {
            try { action(); Write(name.StartsWith("remove.", StringComparison.Ordinal) ? "Unsubscribed" : "Subscribed", "event=" + name); }
            catch (Exception error) { Write("SubscriptionError", "event=" + name + " type=" + error.GetType().Name); }
        }

        private void Guard(Action action)
        {
            var profileWait=LedgerProfiler.Measure(LedgerPerfPath.CallbackGateWait);
            lock(gate)
            {
                profileWait.Dispose();
                if (stopped) return;
                try { action(); }
                catch (Exception error) { Write("CallbackError", "type=" + error.GetType().Name); }
            }
        }

        private void OnGameEntered(bool hasEntered)
        {
            Guard(() =>
            {
                inventoryLifetime.Invalidate();
                inventoryUnloading=!hasEntered;
                if(!hasEntered)FlushShownProduction();
                entered = hasEntered;
                worldLeft = !hasEntered;
                if (!hasEntered) { worldReady = false; localId = -1; }
                if (observer != null) observer.ResetWorld();
                Capture("GameEntered", false);
                if(!hasEntered){StopLedgerWindow();uiAttempted=false;StopLedger();}
                Write("GameEntered", "entered=" + hasEntered + " " + ContextForLine());
            });
        }

        private void OnPlayfieldLoaded(IPlayfield playfield)
        {
            Guard(() => { inventoryLifetime.Invalidate(); inventoryUnloading=false; Capture("OnPlayfieldLoaded", false); Write("OnPlayfieldLoaded", "argument=" + PlayfieldArgument(playfield) + " " + ContextForLine()); });
        }

        private void OnPlayfieldUnloading(IPlayfield playfield)
        {
            Guard(() => { inventoryLifetime.Invalidate(); inventoryUnloading=true; FlushShownProduction(); Capture("OnPlayfieldUnloading", false); Write("OnPlayfieldUnloading", "argument=" + PlayfieldArgument(playfield) + " " + ContextForLine()); });
        }

        private string PlayfieldArgument(IPlayfield playfield)
        {
            return Thread.CurrentThread.ManagedThreadId == apiThread ? PlayfieldName(playfield) : Compact.Value(playfield);
        }

        private static string PlayfieldName(IPlayfield playfield)
        {
            return playfield == null ? "null" : Compact.Read(() => playfield.Name);
        }

        private void OnUpdate()
        {
            LedgerProfiler.Tick();
            using(LedgerProfiler.Measure(LedgerPerfPath.Update))
            {
            Guard(() =>
            {
                apiThread = Thread.CurrentThread.ManagedThreadId;
                inventoryLifetime.Invalidate();
                LedgerNativeUpdate();
                long now = clock.ElapsedMilliseconds;
                if (now - lastPoll >= 500) { lastPoll = now; Capture("Update", false); }
                if (runtimeEnabled && !observerAttempted && WorldReady)
                {
                    observerAttempted = true;
                    observer = RuntimeBootstrap.Start(this);
                }
                if (observer != null) observer.Poll();
                PollProduction();
                PollDiscoveredObjects();
                PollLedgerWindow();
                if (now - lastPoll < 25) PollLedgerStatus();
                if (now - lastHeartbeat >= 30000)
                { lastHeartbeat = now; Write("Heartbeat", ContextForLine()); }
            });

            }
        }

        private void OnGameEvent(GameEventType type, object arg1, object arg2, object arg3, object arg4, object arg5)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.GameEvent))
            {
            if (type != GameEventType.WindowOpened && type != GameEventType.WindowClosed &&
                type != GameEventType.DialogOption && type != GameEventType.PoiDiscovered && type!=GameEventType.ResourceDiscovered) return;
            Guard(() =>
            {
                inventoryLifetime.Invalidate();
                LedgerNativeWindow((int)type,arg1);
                Capture("GameEvent", false);
                ProductionEvent((int)type,arg1,arg2,arg3);
                DiscoveredObjectEvent((int)type,arg1,arg2,arg3);
                Write("GameEvent", "type=" + type + "(" + (int)type + ")" +
                    " arg1=" + Compact.Value(arg1) + " arg2=" + Compact.Value(arg2) +
                    " arg3=" + Compact.Value(arg3) + " arg4=" + Compact.Value(arg4) +
                    " arg5=" + Compact.Value(arg5) + " " + ContextForLine());
                if (observer != null) observer.NativeEvent((int)type, arg1, arg2);
            });

            }
        }

        private void Capture(string source, bool force)
        {
            // Public game getters are read only on the Init/Update thread. Other callbacks use a labelled cache.
            if (Thread.CurrentThread.ManagedThreadId != apiThread) return;
            ticks = application == null ? "unavailable" : ReadRaw(() => application.GameTicks.ToString(CultureInfo.InvariantCulture));
            string pf = api == null ? "null" : ReadRaw(() => PlayfieldName(api.ClientPlayfield));
            localId = -1;
            string player = application == null ? "null" : ReadRaw(() =>
            {
                IPlayer local = application.LocalPlayer;
                if (local == null) { localId = -1; return "null"; }
                localId = local.Id;
                return "{id=" + Compact.Read(() => local.Id.ToString(CultureInfo.InvariantCulture)) +
                    " name=" + Compact.Read(() => local.Name) + "}";
            });
            string gui = api == null ? "null" : ReadRaw(() =>
            {
                IGui value = api.GUI;
                return value == null ? "null" : "{present=true worldVisible=" + Compact.Read(() => value.IsWorldVisible.ToString()) + "}";
            });
            string save = application == null ? null : ReadRaw(() => application.GetPathFor(AppFolder.SaveGame));
            string scopeKey = pf + "|" + save + "|" + localId;
            if (scopeKey != lastScopeKey)
            {
                lastScopeKey = scopeKey;
                LedgerProfiler.ScopeChanged();
                if (observer != null) observer.ResetWorld();
            }
            context = "mode=" + (application == null ? "null" : Compact.Read(() => application.Mode.ToString())) +
                " state=" + (application == null ? "null" : Compact.Read(() => application.State.ToString())) +
                " enteredSignal=" + entered + " playfield=" + pf + " localPlayer=" + player +
                " savePath=" + Compact.Text(save, 800) +
                " GUI=" + gui;
            string mode = application == null ? "" : ReadRaw(() => application.Mode.ToString());
            UpdateLedgerContext(save, mode);
            worldReady = !worldLeft && application != null && ReadRaw(() => application.State.ToString()) == "Running" &&
                (mode == "SinglePlayer" || mode == "Client") && localId >= 0;
            contextAt = clock.ElapsedMilliseconds;
            if (force || pf != lastPlayfield)
            { Write("PlayfieldChanged", "source=" + source + " previous=" + (lastPlayfield ?? "unavailable") + " current=" + pf); lastPlayfield = pf; }
            if (force || context != lastContext)
            { Write("ContextChanged", "source=" + source + " " + context); lastContext = context; }
        }

        private static string ReadRaw(Func<string> getter)
        {
            try { return getter(); }
            catch (Exception error) { return "<error:" + error.GetType().Name + ">"; }
        }

        private string ContextForLine()
        {
            return "contextSource=" + (Thread.CurrentThread.ManagedThreadId == apiThread ? "callback" : "cached-off-thread") +
                " contextAgeMs=" + (clock.ElapsedMilliseconds - contextAt) + " " + context;
        }

        private void Write(string kind, string detail)
        {
            diagnostics.Sink = log;
            diagnostics.Write(Level(kind), kind, ticks, detail, ElapsedMs);
            CheckDiagnosticDelivery(kind,detail,Level(kind)=="ERROR");
        }
        private int essentialLogs;
        private bool diagnosticFailureAnnounced;
        private void CheckDiagnosticDelivery(string kind,string detail,bool error)
        {
            if(OnApiThread&&diagnostics.Failures>0&&!diagnosticFailureAnnounced){diagnosticFailureAnnounced=true;Announce("Diagnostic output unavailable; observation, persistence and journal remain active. Existing evidence preserved.");}
            if(log==null||diagnostics.Failures>0)WriteEssential(kind,detail,error);
        }
        private void WriteEssential(string kind,string detail,bool error=false)
        {
            // Bounded lifecycle/error evidence in the existing native log, without evading diagnostic disk limits.
            if(!OnApiThread)return;
            if(!error&&kind!="LedgerOpenBlocked"&&kind!="LedgerViewReady"&&kind!="LedgerViewOpened"&&kind!="LedgerViewClosed"&&kind!="LedgerViewFocus"&&kind!="LedgerAutoDoneStored"&&kind!="HandInRecordingReady"&&kind.IndexOf("Error",StringComparison.Ordinal)<0)return;
            if(essentialLogs<32)Announce(kind+" "+Compact.Text(detail,600)+" [diagnostic-file-unavailable]");
            else if(essentialLogs==32)Announce("Ledger essential log limit reached; Ledger remains available.");
            if(essentialLogs<=32)essentialLogs++;
        }

        private void Announce(string text)
        {
            // Avoid calling game logging from an unexpected background callback.
            if (api == null || Thread.CurrentThread.ManagedThreadId != apiThread) return;
            try { api.Log("[TradeLedger] " + text); } catch (Exception) { }
        }


        private static string Level(string kind)
        {
            if (kind.IndexOf("Error", StringComparison.Ordinal) >= 0) return "ERROR";
            if(kind=="LedgerHandInEvidence"||kind=="LedgerAutoDoneStored"||kind=="LedgerOpenBlocked"||kind.StartsWith("LedgerCapability-",StringComparison.Ordinal))return "WARN";
            if (kind.IndexOf("Skipped", StringComparison.Ordinal) >= 0 || kind.IndexOf("Unavailable", StringComparison.Ordinal) >= 0) return "WARN";
            return kind == "ContextChanged" || kind == "Heartbeat" ? "DEBUG" : "INFO";
        }
        public bool OnApiThread { get { return Thread.CurrentThread.ManagedThreadId == apiThread; } }
        public bool WorldReady { get { return worldReady && !stopped; } }
        public int LocalPlayerId { get { return localId; } }
        public long ElapsedMs { get { return clock.ElapsedMilliseconds; } }
        public string ContextEvidence { get { return ContextForLine(); } }
        public void Trace(TraceLevel level, string kind, string detail)
        {
            lock (gate)
            {
                if (stopped) return;
                // Diagnostics are optional delivery. No return/exception from a sink participates in observation.
                if (level == TraceLevel.Debug && kind == "FieldMappingVerified") { suppressedFieldMappings++; return; }
                if (level == TraceLevel.Debug && kind == "ReplacementOperand") { suppressedReplacementOperands++; return; }
                if (kind == "HookInstallSummary")
                    Write("BootstrapMetadataSummary", "fieldMappingsVerifiedSuppressed=" + suppressedFieldMappings + " replacementOperandsSuppressed=" + suppressedReplacementOperands + " allChecksStillExecuted=true");
                diagnostics.Sink = log;
                diagnostics.Write(level.ToString().ToUpperInvariant(), kind, ticks, detail, ElapsedMs);
                CheckDiagnosticDelivery(kind,detail,level==TraceLevel.Error);
            }
        }

        public void Shutdown()
        {
            LedgerProfiler.SetEnabled(false); // asynchronous drain overlaps existing shutdown; no new join.
            lock (gate)
            {
                if (stopped) return;
                try
                {
                    inventoryLifetime.Invalidate();
                    Capture("Shutdown", false);
                    Write("Shutdown", ContextForLine());
                    if (observer != null) { observer.Dispose(); observer = null; }
                    StopLedgerWindow();
                    StopLedger();
                    if (eventBound) Bind("remove.GameEvent", () => api.GameEvent -= OnGameEvent);
                    if (enteredBound) Bind("remove.GameEntered", () => application.GameEntered -= OnGameEntered);
                    if (loadedBound) Bind("remove.OnPlayfieldLoaded", () => application.OnPlayfieldLoaded -= OnPlayfieldLoaded);
                    if (unloadingBound) Bind("remove.OnPlayfieldUnloading", () => application.OnPlayfieldUnloading -= OnPlayfieldUnloading);
                    if (updateBound) Bind("remove.Update", () => application.Update -= OnUpdate);
                    Write("LoggerCounters", ReadRaw(() => log == null ? "unavailable" : log.Counters) + " filtered=" + diagnostics.Filtered + " repeatedSuppressed=" + diagnostics.Suppressed + " deliveryFailures=" + diagnostics.Failures);
                    Write("LogClosed", "cleanShutdown=true");
                }
                catch (Exception error) { Write("ShutdownError", "type=" + error.GetType().Name); }
                finally { LedgerProfiler.SetEnabled(false);stopped = true; if(loaderLease!=null){loaderLease.Dispose();loaderLease=null;}if (log != null) { try { log.Dispose(); } catch (Exception) { } } }
            }
        }
    }
}
