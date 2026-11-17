using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Eleon.Modding;

namespace TradeLedgerProbeB
{
    // Save-local manager facade; all native actions stay on the API thread behind physical-save guards.
    internal sealed class LedgerNativeBookmarks
    {
        private readonly LedgerNativeMarkerBackend backend;
        private readonly LedgerBookmarkOwnership legacy;
        private readonly LedgerMarkerRepository repository;
        private readonly LedgerMarkerCoordinator coordinator;
        private readonly Action<string,string> log;
        private readonly LedgerStore store;
        private readonly string databasePath;
        private readonly LedgerViewReader snapshotReader=new LedgerViewReader();
        private readonly ConcurrentDictionary<string,LedgerObservation> observed=new ConcurrentDictionary<string,LedgerObservation>();
        private readonly ConcurrentQueue<LedgerManualCompletion> annotations=new ConcurrentQueue<LedgerManualCompletion>();
        private LedgerViewData requestData;
        private LedgerCatalog inventoryCatalog;
        private readonly LedgerRequestInventorySampler inventorySampler=new LedgerRequestInventorySampler();
        internal Dictionary<string,LedgerRequestInventoryProgress> InventoryProgress=new Dictionary<string,LedgerRequestInventoryProgress>();
        internal string InventoryStatus="Inventory unavailable",InventoryDiagnostics="";
        internal void InvalidateInventory(){inventorySampler.Reset();InventoryProgress.Clear();InventoryStatus="Inventory unavailable";InventoryDiagnostics="Source/session invalidated";LedgerRequestInventory.Suspend(Model);LedgerRoutePurchases.Reset(Model);}
        private void MatchRequestInventory(LedgerContext context,Func<LedgerInventorySnapshot> reader,ulong? tick)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.InventoryMatch))
            {
            LedgerRequestStock stock=LedgerRequestStock.Unknown("Inventory unavailable");
            try{if(reader!=null&&(LedgerRoutePurchases.HasGoals(Model,context.WorldKey,Player)||requestData!=null&&requestData.ManualStateReady&&requestData.RequestCards(true).Count>0))stock=inventorySampler.Accept(reader());else inventorySampler.Reset();}
            catch(Exception){inventorySampler.Reset();}
            // Re-evaluate flow reasons before adding this fresh inventory projection; no stale supplemental reason.
            LedgerRouteFlow.Evaluate(Model,context,Player,tick,servicePlaces??new LedgerProductionPlaces(context.WorldKey,new[]{context}));
            LedgerRoutePurchases.Evaluate(Model,context,Player,tick,stock,servicePlaces);
            InventoryStatus=stock.Coverage;InventoryDiagnostics=stock.Diagnostics??stock.Reason;
            InventoryProgress=LedgerRequestInventory.Evaluate(Model,requestData,context,Player,stock,servicePlaces);

            }
        }
        private readonly ConcurrentQueue<LedgerTradeReceipt> routeOutcomes=new ConcurrentQueue<LedgerTradeReceipt>();
        private string scope,state="",lastError;private long refreshed,savedRevision=-1;
        private int generation;private bool stopped;
        private sealed class ServiceRead {public int Generation,Revision;public LedgerViewData Data;}
        private readonly ConcurrentQueue<ServiceRead> serviceReads=new ConcurrentQueue<ServiceRead>();
        private readonly LedgerServiceSchedule serviceSchedule=new LedgerServiceSchedule();
        private List<LedgerObservation> serviceDevices=new List<LedgerObservation>();
        private LedgerProductionPlaces servicePlaces;
        private readonly LedgerNativeGeography serviceGeography;
        private string attentionPlace;
        private long productionRetryAt;private volatile string productionReadError;
        private int productionRevision,loadedProduction=-1,readingProduction,marketReloadRequested;private bool serviceDirty=true,serviceReady;
        internal bool Ready {get;private set;}
        internal bool SaveFailed {get{return repository.Error!=null;}}
        internal bool Busy {get{return false;}} // No global pending-create latch.
        internal string Status {get{return repository.Error==null?(productionReadError??coordinator.Status??state):"Marker changes not saved: "+repository.Error;}}
        internal LedgerMarkerModel Model {get{return coordinator.Model;}}
        internal int Player {get{return backend.currentPlayer;}}
        internal string World {get{return backend.currentWorld;}}
        internal LedgerNativeBookmarks(Assembly game,string ownershipPath,Action<string,string> logger,string stagingRoot=null,Action<Action> publisher=null,LedgerStore ledger=null,string viewDatabase=null)
        {
            log=logger;store=ledger;databasePath=viewDatabase;backend=new LedgerNativeMarkerBackend(game);
            try{serviceGeography=new LedgerNativeGeography(game);}catch(Exception e){LogError("Optional native geography unavailable; stored/current public geography retained: "+e.GetType().Name);}
            legacy=new LedgerBookmarkOwnership(ownershipPath,e=>LogError(e),stagingRoot,publisher);
            repository=new LedgerMarkerRepository(Path.Combine(Path.GetDirectoryName(ownershipPath),"LedgerMarkers.json"),stagingRoot,publisher);
            coordinator=new LedgerMarkerCoordinator(new LedgerMarkerModel(repository.Read()));
            LedgerRequestMarkers.Suspend(Model);
            if(store!=null){store.ObservationCommitted+=Observe;store.AnnotationCommitted+=Annotation;}
        }
        private void LogError(string message){try{log("LedgerBookmarkError",message);}catch(Exception){}}
        private void Annotation(LedgerManualCompletion value)
        {if(stopped||value==null||value.IsTracking||value.IsArchive)return;if(annotations.Count<128)annotations.Enqueue(value);Interlocked.Increment(ref productionRevision);Interlocked.Exchange(ref productionRetryAt,0);}
        private void Observe(LedgerObservation value)
        {
            if(stopped||value==null||value.Context==null||value.Kind=="place-observed")return;
            if(value.Kind=="production"||value.Kind=="production-lifecycle"||value.Kind=="dialogue-shown"){Interlocked.Increment(ref productionRevision);Interlocked.Exchange(ref productionRetryAt,0);}
            if(observed.Count>=512){if(value.Kind=="market")Interlocked.Increment(ref marketReloadRequested);LogError("marker refresh backlog bounded512; latest snapshot reload requested");return;}
            if(value.Kind=="production"){if(!LedgerProduction.Valid(value))return;observed[value.Production.Key]=value;return;}
            if(value.Kind!="market"||value.Identity==null||!(value.Identity.NpcEntityId>0))return;
            observed[value.Context.WorldKey+"|"+value.Context.Playfield+"|"+value.Identity.NpcEntityId]=value;
        }
        private void Snapshot(string world,int player)
        {
            if(databasePath==null||DateTime.UtcNow.Ticks/TimeSpan.TicksPerMillisecond<Interlocked.Read(ref productionRetryAt)||Interlocked.CompareExchange(ref readingProduction,1,0)!=0)return;
            int ticket=generation,revision=Volatile.Read(ref productionRevision),marketReload=Volatile.Read(ref marketReloadRequested);var catalog=inventoryCatalog;
            bool restoreMarkets=!serviceReady||marketReload!=0;
            ThreadPool.QueueUserWorkItem(_=>
            {
                try{
                    if(!File.Exists(databasePath))throw new FileNotFoundException("Private producer view not yet published");
                    var data=snapshotReader.Read(databasePath,world,catalog,player);
                    // Recognition is pure over detached DTOs/catalog. Warm its lazy card cache on
                    // this reader worker, not the next API tick after servicing a producer.
                    data.RequestCards();
                    if(stopped||generation!=ticket)return;
                    serviceReads.Enqueue(new ServiceRead{Generation=ticket,Revision=revision,Data=data});
                    // Committed market observations have their own queue. A producer-only reload
                    // must not replay every unchanged quote and dirty/serialize the entire marker document.
                    if(restoreMarkets){foreach(var o in data.Markets){if(o.Identity!=null&&o.Identity.NpcEntityId>0)observed[o.Context.WorldKey+"|"+o.Context.Playfield+"|"+o.Identity.NpcEntityId]=o;}
                        Interlocked.CompareExchange(ref marketReloadRequested,0,marketReload);}
                }
                catch(Exception e){productionReadError="Producer records could not be reloaded; previous service state retained. Retrying.";Interlocked.Exchange(ref productionRetryAt,DateTime.UtcNow.Ticks/TimeSpan.TicksPerMillisecond+5000);LogError("Service/marker projection read: "+e.GetType().Name+"; original metadata retained, will retry");}
                finally{Interlocked.Exchange(ref readingProduction,0);}
            });
        }
        internal void Refresh(LedgerContext context,IApplication app,bool force=false,LedgerCatalog catalog=null,Func<LedgerInventorySnapshot> inventoryReader=null)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.MarkerRefresh))
            {
            long now=DateTime.UtcNow.Ticks/TimeSpan.TicksPerMillisecond;if(!force&&now-refreshed<500)return;refreshed=now;Ready=false;
            if(store!=null&&!store.OwnedPublicationAllowed)return;
            try
            {
                if(!ReferenceEquals(catalog,inventoryCatalog)){inventoryCatalog=catalog;Interlocked.Increment(ref productionRevision);requestData=null;inventorySampler.Reset();}
                backend.Bind(context,app);string next=context.LocalSaveKey+"|"+context.WorldKey+"|"+Player;
                if(scope!=next)
                {
                    generation++;requestData=null;inventorySampler.Reset();InventoryProgress.Clear();LedgerRequestInventory.Suspend(Model);LedgerRequestMarkers.Suspend(Model);serviceReady=false;serviceDevices.Clear();Interlocked.Exchange(ref productionRetryAt,0);productionReadError=null;loadedProduction=-1;serviceDirty=true;coordinator.Model.Migrate(legacy.For(World,Player),backend.Read(),World+"|"+Player);scope=next;LedgerRequestMarkers.Suspend(Model);Snapshot(World,Player);
                }
                foreach(var pair in observed.ToArray()){LedgerObservation o;if(observed.TryRemove(pair.Key,out o)){Model.Observe(o);serviceDirty=true;}}
                LedgerManualCompletion annotation;while(annotations.TryDequeue(out annotation)){LedgerRequestMarkers.Committed(Model,annotation);LedgerRequestInventory.Committed(Model,annotation);}
                ServiceRead read;while(serviceReads.TryDequeue(out read)){
                    if(read.Generation!=generation||read.Revision!=Volatile.Read(ref productionRevision))continue;
                    requestData=read.Data;LedgerRequestMarkers.Apply(Model,requestData,World,Player);
                    try{
                        serviceDevices=LedgerProductionServices.ReadActive(read.Data,World,Player);
                        servicePlaces=new LedgerProductionPlaces(World,LedgerGeographyProjection.Contexts(read.Data));
                        serviceReady=true;productionReadError=null;
                    }catch(Exception){serviceReady=false;productionReadError="Producer records could not be reloaded; previous service state retained. Retrying.";Interlocked.Exchange(ref productionRetryAt,now+5000);}
                    loadedProduction=read.Revision;serviceDirty=true;
                }
                if(!serviceReady||loadedProduction!=Volatile.Read(ref productionRevision)||Volatile.Read(ref marketReloadRequested)!=0)Snapshot(World,Player);
                LedgerTradeReceipt outcome;while(routeOutcomes.TryDequeue(out outcome)){if(scope==outcome.Context.LocalSaveKey+"|"+outcome.Context.WorldKey+"|"+outcome.PlayerId){LedgerRouteFlow.Trade(Model,outcome);serviceDirty=true;}else LogError("Route outcome not applied: physical save/player changed; no goal state invented");}
                bool geographyDirty=requestData!=null&&serviceGeography!=null&&serviceGeography.Enrich(requestData,app,context,now);
                if(geographyDirty){servicePlaces=new LedgerProductionPlaces(World,LedgerGeographyProjection.Contexts(requestData));serviceDirty=true;}
                bool attentionDirty=geographyDirty;
                if(serviceSchedule.Due(now,app.GameTicks,serviceDirty)){
                    if(serviceReady)ApplyServicePolicy(context,app);
                    // Trade updates do not depend on producer read success/alert enablement.
                    LedgerTradeUpdates.Evaluate(Model,context,Player,app.GameTicks,servicePlaces);
                    serviceDirty=false;attentionDirty=true;
                }
                string attentionCurrent=context.LocalSaveKey+"|"+context.WorldKey+"|"+Player+"|"+context.Playfield+"|"+context.Geography?.SystemCoordinates+"|"+context.Geography?.SectorCoordinates;
                if(attentionDirty||attentionPlace!=attentionCurrent){LedgerMinerAttention.Evaluate(Model,serviceDevices,context,Player);attentionPlace=attentionCurrent;}
                try{MatchRequestInventory(context,inventoryReader,app.GameTicks);}catch(Exception){inventorySampler.Reset();InventoryProgress.Clear();InventoryStatus="Inventory unavailable";InventoryDiagnostics="Source/session invalidated";LedgerRequestInventory.Suspend(Model);LedgerRoutePurchases.Reset(Model);}
                coordinator.Sync(backend,World,Player);if(state=="Loading saved markers...")state="";Ready=true;Save();
                if(repository.Error!=lastError){lastError=repository.Error;if(lastError!=null)LogError("marker persistence: "+lastError);}
            }
            catch(LedgerMarkerEnumerationPendingException){state="Loading saved markers...";}
            catch(Exception e){state="Bookmarks unavailable: "+e.GetBaseException().Message;LogError(e.ToString());}

            }
        }
        private void ApplyServicePolicy(LedgerContext context,IApplication app)
        {
            try{LedgerProductionServices.Evaluate(Model,serviceDevices,context,Player,app.GameTicks,false,servicePlaces);if(state.StartsWith("Service alerts unavailable: ",StringComparison.Ordinal))state="";}
            catch(Exception e){state="Service alerts unavailable: "+e.GetBaseException().Message;LogError(e.ToString());}
            // A service limit/conflict never prevents unrelated owned trade-marker reconciliation.
        }
        private void Save(){if(Model.Revision==savedRevision)return;repository.Save(Model.Data);savedRevision=Model.Revision;}
        internal void Edit(Action<LedgerMarkerModel> action)
        {
            if(!Ready)return;try{action(Model);Model.Revision++;Save();serviceDirty=true;refreshed=0;state="";}catch(Exception e){state=e.GetBaseException().Message;LogError(e.ToString());}
        }
        internal void MarkServiceScope(LedgerContext context,IApplication app,LedgerProductionPlaces places,string place,IEnumerable<string> matchingKeys)
        {
            if(!Ready||!serviceReady){state="Device records are loading; try Mark again shortly";return;}
            try{
                if(store!=null&&!store.OwnedPublicationAllowed)throw new InvalidOperationException("Native save menu active; retry after closing it");
                var members=new HashSet<string>(matchingKeys??Enumerable.Empty<string>(),StringComparer.Ordinal);
                if(members.Count==0)return;
                backend.Bind(context,app);LedgerProductionServices.Evaluate(Model,serviceDevices,context,Player,app.GameTicks,true,places,place,members);
                LedgerMinerAttention.Evaluate(Model,serviceDevices,context,Player);coordinator.Sync(backend,World,Player);Save();serviceDirty=true;refreshed=0;state="";
            }catch(Exception e){state="Service markers unavailable: "+e.GetBaseException().Message;LogError(e.ToString());}
        }
        internal void ConfirmedTrade(LedgerTradeReceipt t){if(stopped||t==null||t.Context==null||t.Identity==null||!t.Confirmed)return;if(routeOutcomes.Count>=32){LogError("Route outcome queue bounded32; manual route Show remains available");return;}routeOutcomes.Enqueue(new LedgerTradeReceipt{TradeId=t.TradeId,Confirmed=true,Context=LedgerJson.Decode<LedgerContext>(LedgerJson.Encode(t.Context)),Identity=LedgerJson.Decode<LedgerIdentity>(LedgerJson.Encode(t.Identity)),PlayerId=t.PlayerId,ItemId=t.ItemId,Auxiliary=t.Auxiliary,Action=t.Action,Quantity=t.Quantity,Utc=t.Utc});serviceDirty=true;refreshed=0;}
        internal void RetrySave(){try{repository.Save(Model.Data);state="";}catch(Exception e){state=e.Message;}}
        internal LedgerMarkerDestination Destination(LedgerBookmarkPlan p){return Ready&&p!=null&&p.World==World?Model.Find(p,Player):null;}
        internal bool HasPurpose(LedgerBookmarkPlan p){var d=Destination(p);return d!=null&&d.Purposes.Any(v=>v.Key==LedgerMarkerModel.PurposeKey(p));}
        internal bool Changed(LedgerBookmarkPlan p){var d=Destination(p);return d!=null&&d.Conflict;}
        internal bool UnmanagedExists(LedgerBookmarkPlan p){return false;} // Never adopt/block by names or incidental numeric tuples.
        internal int ActiveId(LedgerBookmarkPlan p){var d=Destination(p);return d!=null&&!d.Conflict&&d.Native!=null&&HasPurpose(p)?d.Native.Id:-1;}
        internal void Submit(LedgerBookmarkPlan p,LedgerContext context,IApplication app)
        {
            try
            {
                if(store!=null&&!store.OwnedPublicationAllowed)throw new InvalidOperationException("Native save menu active; retry after closing it");
                if(!LedgerBookmarkPolicy.CurrentScope(p,context))throw new InvalidOperationException("Stale current-save scope");
                backend.Bind(context,app);Model.Ensure(p,Player,true,true);if(p.RequestKey!=null){LedgerRequestMarkers.Suspend(Model);LedgerRequestMarkers.Apply(Model,requestData,World,Player);}serviceDirty=true;LedgerTradeUpdates.Evaluate(Model,context,Player,app.GameTicks,servicePlaces);LedgerMinerAttention.Evaluate(Model,serviceDevices,context,Player);coordinator.Sync(backend,World,Player);Save();state="";refreshed=0;
            }
            catch(Exception e){state="Bookmark unavailable: "+e.GetBaseException().Message;LogError(e.ToString());}
        }
        internal void Stop()
        {
            stopped=true;generation++;if(store!=null){store.ObservationCommitted-=Observe;store.AnnotationCommitted-=Annotation;}
            try{Save();if(!repository.Wait(1000))LogError("Marker save still pending/failed; original metadata and private pending generation preserved");}catch(Exception e){LogError(e.ToString());}
        }
    }
}
