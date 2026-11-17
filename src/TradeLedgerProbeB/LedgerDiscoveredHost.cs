using System;
namespace TradeLedgerProbeB
{
    public sealed partial class Probe
    {
        private LedgerDiscoveredNative discoveredSource;private long discoveredNext,discoveredRetry;private string discoveredError;
        private bool ObjectCaptureReady {get{return runtimeEnabled&&OnApiThread&&WorldReady&&ledger!=null&&ledgerContext!=null&&ledgerContext.Recordable&&ledger.OwnedPublicationAllowed;}}
        private bool SubmitDiscovered(LedgerObservation o)
        {
            if(!ObjectCaptureReady)return false;
            o.Session=session;o.Sequence=++ledgerSequence;o.ObserverPlayer=localId;
            o.GameTicks=application.GameTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return ledger.Enqueue(o); // No JSON, hashing, recognition, markers or DB on this thread.
        }
        private bool ObjectReader()
        {
            if(discoveredSource!=null)return true;if(ElapsedMs<discoveredRetry)return false;
            discoveredRetry=ElapsedMs+10000;discoveredSource=new LedgerDiscoveredNative(application.GetType().Assembly);return true;
        }
        private void ObjectFailure(Exception e)
        {string message=e.GetBaseException().GetType().Name+":"+e.GetBaseException().Message;if(message==discoveredError)return;discoveredError=message;Write("DiscoveredObjectCaptureError",message);}
        private void PollDiscoveredObjects()
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.ObjectReconcile))
            {
            if(!ObjectCaptureReady||ElapsedMs<discoveredNext)return;discoveredNext=ElapsedMs+1000;
            try{if(ObjectReader())discoveredSource.Poll(ledgerContext,application,localId,ElapsedMs,SubmitDiscovered);}
            catch(Exception e){ObjectFailure(e);}

            }
        }
        private void DiscoveredObjectEvent(int type,object name,object actor,object raw)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.ObjectDiscovery))
            {
            if(type!=24||!ObjectCaptureReady)return;
            try{if(ObjectReader())discoveredSource.Discover(ledgerContext,application,localId,name,actor,raw,SubmitDiscovered);}
            catch(Exception e){ObjectFailure(e);}

            }
        }
    }
}
