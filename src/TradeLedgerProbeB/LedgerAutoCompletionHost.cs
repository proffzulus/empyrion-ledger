using System;
using System.Linq;

namespace TradeLedgerProbeB
{
    public sealed partial class Probe : ILedgerHandInHost
    {
        private readonly LedgerHandInCorrelation handIn=new LedgerHandInCorrelation();
        private LedgerNativeCatalog handInCatalogSource;
        private LedgerCatalog handInCatalog;
        private int handInDiagnostics;
        private string unresolvedHandInBody;
        private void HandInShown(LedgerObservation observation)
        {
            if(observation.Kind!="dialogue-shown")return;
            try
            {
                if(handInCatalog==null)
                {
                    handInCatalogSource=new LedgerNativeCatalog(application.GetType().Assembly,application);
                    handInCatalog=handInCatalogSource.Read();
                }
                var fact=LedgerRequestRecognition.Recognize(observation,handInCatalog);
                if(observation.Dialogue.BodyFullyRevealed==true&&fact!=null&&fact.Items.Any(i=>!i.ItemId.HasValue)&&unresolvedHandInBody!=observation.Dialogue.BodyVerbatim)
                {
                    unresolvedHandInBody=observation.Dialogue.BodyVerbatim;
                    HandInDiagnostic("request-item-identity-unresolved npc="+observation.Identity.NpcEntityId+" items="+Safe.Text(string.Join(";",fact.Items.Where(i=>!i.ItemId.HasValue).Select(i=>i.Name)),240)+" catalogDefinitions="+handInCatalog.Items.Count+" rule="+fact.Rule);
                }
                handIn.Shown(observation,fact,localId);
            }
            catch(Exception e){handIn.Reset();HandInDiagnostic("request-or-catalog-unavailable type="+e.GetType().Name);}
        }
        private void HandInDiagnostic(string message)
        {
            if(handInDiagnostics<128)Write("LedgerHandInEvidence",message);
            else if(handInDiagnostics==128)Write("LedgerHandInEvidence","diagnostics-truncated limit=128 until-world-reset");
            if(handInDiagnostics<=128)handInDiagnostics++;
        }
        public void HandInClosed(string interaction){if(OnApiThread)handIn.Close(interaction,ElapsedMs);}
        public void HandInCancelled(string reason){if(OnApiThread){bool active=handIn.HasShown;handIn.Reset();if(active)HandInDiagnostic("cancelled reason="+Safe.Text(reason));}}
        public void HandInRemoved(string interaction,int npc,int item,int meta,int count,string evidence)
        {
            if(!OnApiThread||!WorldReady||ledger==null||ledgerContext==null||!ledgerContext.Recordable||ledgerContext.Mode!="SinglePlayer")return;
            try
            {
                if(!handIn.Current(ledgerContext,localId)){handIn.Reset();HandInDiagnostic("rejected reason=current-save-player-or-playfield-changed");return;}
                string reason;var value=handIn.Removal(interaction,npc,item,meta,count,evidence,ElapsedMs,out reason);
                HandInDiagnostic("interaction="+interaction+" npc="+npc+" item="+item+" meta="+meta+" actualRemoved="+count+" nativeCall="+evidence+" result="+reason);
                if(value==null)return;
                bool queued=ledger.EnqueueManualCompletion(value,error=>
                {
                    Write(error==null?"LedgerAutoDoneStored":"LedgerAutoDoneError","request="+value.RequestKey+" evidence="+value.AutomaticEvidence+" result="+(error??value.AutomaticResult)+" "+value.EvidenceDetail+" reversible=true");
                });
                HandInDiagnostic("request="+value.RequestKey+" autoDoneQueued="+queued+" observation="+value.ObservedUtc+" "+value.EvidenceDetail);
            }
            catch(Exception e){HandInDiagnostic("automatic-write-skipped type="+e.GetType().Name);}
        }
        private void ResetHandIn(){handIn.Reset();handInCatalog=null;handInCatalogSource=null;handInDiagnostics=0;unresolvedHandInBody=null;}
    }
}
