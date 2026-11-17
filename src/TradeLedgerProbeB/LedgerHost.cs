using System;
using System.IO;
using System.Threading;
using Eleon.Modding;

namespace TradeLedgerProbeB
{
    public sealed partial class Probe : ILedgerTradeHost
    {
        private readonly string ledgerRoot;
        private LedgerStore ledger;
        private LedgerContext ledgerContext;
        private string lastLedgerStatus, lastLedgerSkip;
        private long reportedStorageProblems;
        private string reportedStorageState;
        private long storageAnnouncementAt=-60000;
        private readonly object portableGate=new object();
        private int portableGeneration;
        private string portablePath,portableError,ledgerDatabasePath;
        private LedgerPortableSave portablePending,portableActive;
        private bool portableEnabled;
        private bool portableOpening;
        private long portableRetryAt;
        private readonly LedgerSaveMenuSchedule nativeSaveMenu=new LedgerSaveMenuSchedule();
        private void LedgerNativeWindow(int type,object name)
        {
            if(!nativeSaveMenu.Window(type,name))return;
            lock(portableGate){if(portableActive!=null)portableActive.SuspendForNativeSave();}
            Write("LedgerNativeSaveBarrier","suspended=true source=public-InGameMenu-event resume=next-API-update-after-close native-result-unchanged=true");
        }
        private void LedgerNativeUpdate()
        {
            if(!nativeSaveMenu.Update())return;
            lock(portableGate){if(portableActive!=null)portableActive.ResumeAfterNativeSave();}
            Write("LedgerNativeSaveBarrier","suspended=false source=API-update native-result-not-inferred=true");
        }
        private long ledgerSequence;
        private LedgerNativeGeography geographySource;
        private bool geographyAttempted;
        private string geographyScope;
        private long geographyReadAt;
        private LedgerGeography geographyCached;
        private void StartLedger()
        {
            portableEnabled=runtimeEnabled;
            if(portableEnabled)Write("LedgerStorage","format=save-contained-immutable-SQLite legacyExternal=read-only-recovery current-save-authority=true");
        }
        private void BindPortable(LedgerContext context)
        {
            // Missing playfield/UI context during a transition is NOT a save unload. Keep
            // the physical-save worker/lease, but never admit capture or native commands.
            if(worldLeft){context.Recordable=false;context.ScopeState="world-left";return;}
            if(!portableEnabled||!context.Recordable)return;
            if(portablePath!=context.SavePath)
            {
                StopLedgerWindow();uiAttempted=false;StopLedger();
                lock(portableGate){portablePath=context.SavePath;portableError=null;portableRetryAt=0;}
            }
            lock(portableGate)
            if(portableActive==null&&portablePending==null&&!portableOpening&&ElapsedMs>=portableRetryAt)
            {
                string save=portablePath;int ticket=++portableGeneration;portableOpening=true;
                ThreadPool.QueueUserWorkItem(_=>
                {
                    LedgerPortableSave opened=null;string error=null;
                    for(int attempt=0;attempt<5&&opened==null;attempt++)
                    {
                        try{opened=LedgerPortableSave.Open(save,ledgerRoot);}
                        catch(Exception e){error=e.GetType().Name+":"+e.Message;if(!(e is IOException)||e is InvalidDataException)break;Thread.Sleep(250);}
                        lock(portableGate)if(ticket!=portableGeneration)break;
                    }
                    if(opened!=null)error=null;
                    lock(portableGate)
                    {
                        if(ticket==portableGeneration){portablePending=opened;portableError=error;portableOpening=false;portableRetryAt=ElapsedMs+5000;opened=null;}
                    }
                    if(opened!=null)opened.Dispose();
                });
            }
            lock(portableGate)
            {
                if(portablePending!=null)
                {
                    portableActive=portablePending;portablePending=null;ledgerDatabasePath=portableActive.ViewDatabasePath;
                    if(nativeSaveMenu.Suspended)portableActive.SuspendForNativeSave();
                    ledger=new LedgerStore(portableActive.WorkRoot,null,null,portableActive);
                    Write("LedgerPortableLoaded","path="+Safe.Text(portableActive.DatabasePath,800)+" UIReadPath="+Safe.Text(ledgerDatabasePath,800)+" leaseOutsideSaveTree=true campaign="+portableActive.Identity.Campaign+" snapshot="+portableActive.Identity.Snapshot+" origin="+portableActive.Identity.Origin);
                    if(portableActive.Identity.Revision==1&&portableActive.Identity.Origin.StartsWith("new-isolated-lineage;",StringComparison.Ordinal))
                        Announce("This save has no prior portable Ledger or verified legacy link. Using isolated records. If restoring a copy, include its complete Mods/TradeLedger folder; names/seeds cannot recover missing records.");
                }
                if(portableActive==null)
                {
                    context.Recordable=false;context.ScopeState=portableError==null?"portable-opening;capture-paused":"portable-blocked;retry-in-5s;"+portableError;
                    if(context.ScopeState!=lastLedgerSkip){lastLedgerSkip=context.ScopeState;Write(portableError==null?"LedgerStorageOpening":"LedgerStorageError",context.ScopeState);if(portableError!=null)Announce("Trade Ledger paused: "+portableError+". See portable migration/recovery instructions; no data was merged or replaced.");}
                    return;
                }
                context.WorldKey=portableActive.Identity.WorldKey;
                context.ScopeState="portable-save-authority;"+portableActive.Identity.Origin;
            }
        }
        private void UpdateLedgerContext(string save, string mode)
        {
            string playfield = null, system = null, bodyKind=null;
            try
            {
                IPlayfield pf = api == null ? null : api.ClientPlayfield;
                if (pf != null) { playfield = pf.Name; system = pf.SolarSystemName; bodyKind=pf.PlayfieldType; }
            }
            catch (Exception) { /* Missing context disables persistence; diagnostics retain their own evidence. */ }
            ledgerContext = LedgerContext.Create(save, playfield, system, mode);
            BindPortable(ledgerContext);
            if(!ledgerContext.Recordable)return;
            if(!geographyAttempted&&application!=null)
            {
                geographyAttempted=true;
                try{geographySource=new LedgerNativeGeography(application.GetType().Assembly);}catch(Exception){/* Optional metadata; public/current body context remains useful. */}
            }
            string geoScope=ledgerContext.LocalSaveKey+"|"+ledgerContext.WorldKey+"|"+playfield;long now=DateTime.UtcNow.Ticks/TimeSpan.TicksPerMillisecond;
            if(geoScope!=geographyScope||(geographyCached==null||string.IsNullOrWhiteSpace(geographyCached.GroupName))&&now-geographyReadAt>=5000)
            {
                geographyScope=geoScope;geographyReadAt=now;
                try{geographyCached=geographySource==null?null:geographySource.Read(ledgerContext,application);}catch(Exception){geographyCached=null;}
            }
            ledgerContext.Geography=geographyCached??(!playfield.Contains("#")&&(bodyKind=="Planet"||bodyKind=="Moon"||bodyKind=="Space")?new LedgerGeography{BodyKind=bodyKind,Source="IPlayfield.PlayfieldType current API;sector unknown"}:null);
        }
        public void Record(LedgerObservation observation)
        {
            // The adapter submits an owned plain DTO only after its existing current-UI payload gate.
            if (!OnApiThread || ledger == null || ledgerContext == null || !ledgerContext.Recordable)
            {
                string reason = ledgerContext == null ? "storage-or-world-context-unavailable" : ledgerContext.ScopeState;
                if (reason != lastLedgerSkip) { lastLedgerSkip = reason; Write("LedgerCaptureSkipped", "reason=" + reason); }
                return;
            }
            lastLedgerSkip = null;
            observation.Context = ledgerContext; observation.Session = session; observation.GameTicks = observation.Production==null?ticks:observation.Production.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture);
            LedgerFreshness.StampObservation(observation, () => application == null ? (ulong?)null : application.GameTicks);
            observation.Sequence = ++ledgerSequence;observation.ObserverPlayer=localId;
            // English cue/quantity recognition is bounded and uses only this already-shown body.
            // The read projection later resolves IDs against the active item/localization catalog.
            observation.Request = LedgerRequestRecognition.Recognize(observation);
            if(observation.Production==null||observation.Utc==null)observation.Utc = DateTime.UtcNow.ToString("o");
            bool accepted = ledger.Enqueue(observation);
            if(accepted)HandInShown(observation);
            Write(accepted ? "LedgerQueued" : "LedgerStorageError", "observation=" + observation.ObservationId + " kind=" + observation.Kind +
                " interaction=" + observation.Interaction + " queued=" + accepted + " committedOnlyAfterBackgroundFlush=true");
        }
        LedgerContext ILedgerTradeHost.TradeContext {get{return ledgerContext;}}
        public void RecordTrade(LedgerTradeReceipt receipt)
        {
            // Confirmed outcomes feed remembered route steps only. No financial journal writes.
            var w=ledgerWindow as LedgerPrototypeWindow;if(OnApiThread&&w!=null&&w.NativeBookmarks!=null)w.NativeBookmarks.ConfirmedTrade(receipt);
        }
        private void PollLedgerStatus(bool force = false)
        {
            if (ledger == null) return;
            var s = ledger.Status;
            string summary = "state=" + s.State + " queued=" + s.Queued + " committed=" + s.Committed + " recovered=" + s.Recovered +
                " pending=" + s.Pending + " rejected=" + s.Rejected + " failures=" + s.Failures + " skippedPartial=" + s.SkippedPartial +
                " identityRejected=" + s.IdentityRejected + " unchanged="+s.Unchanged+" staleRejected=" + s.StaleRejected + " dialogueMigrated=" + s.DialogueMigrated + " dialogueDuplicatesCollapsed=" + s.DialogueDuplicatesCollapsed + " recoveryWorkRoot=" + Safe.Text(s.Root,800) + " error=" + Safe.Text(s.Error, 300);
            if (force || summary != lastLedgerStatus)
            {
                lastLedgerStatus = summary;
                long problems=s.Rejected+s.Failures+s.IdentityRejected+s.StaleRejected;
                bool bad = s.State == "blocked" || s.State == "retrying" || problems > reportedStorageProblems ||
                    s.State == "shutdown-flush-pending" || s.State == "closed-with-unwritten-queue" || (force && s.Pending != 0);
                Write(bad ? "LedgerStorageError" : "LedgerStatus", summary);
                if (bad && (problems != reportedStorageProblems || s.State != reportedStorageState) && ElapsedMs-storageAnnouncementAt>=60000)
                {storageAnnouncementAt=ElapsedMs;Announce("Ledger storage needs attention: " + summary);}
                reportedStorageProblems=problems;reportedStorageState=s.State;
            }
        }
        private void StopLedger()
        {
            ResetHandIn();
            // Flush the last owned shown DTO, never a closing/destroyed native UI object.
            // Preserve its original scope even if binding a new save triggered this shutdown.
            FlushShownProduction();
            productionSource=null;productionError=null;
            LedgerPortableSave pending;
            lock(portableGate){portableGeneration++;portableOpening=false;pending=portablePending;portablePending=null;if(portableActive!=null)portableActive.ResumeAfterNativeSave();portableActive=null;portablePath=null;portableError=null;}
            if(pending!=null)pending.Dispose();
            if(ledger!=null){ledger.Dispose();PollLedgerStatus(true);ledger=null;}
            ledgerDatabasePath=null;
        }
    }
}
