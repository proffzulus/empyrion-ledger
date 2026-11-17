using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Data.Sqlite;

namespace TradeLedgerProbeB
{
    // Read-only, owned SQLite DTOs. Never opens a game database or resolves a live entity.
    public sealed partial class LedgerViewData
    {
        public readonly List<LedgerObservation> Markets = new List<LedgerObservation>();
        public List<LedgerObservation> Production=new List<LedgerObservation>();
        public string ProductionReadError;
        public bool RetryableProjection;
        public List<LedgerObjectRecord> DiscoveredObjects=new List<LedgerObjectRecord>();
        public string ObjectReadError;
        private LedgerObjectIndex objectIndex;
        public int GeographyRevision {get;private set;}
        public void GeographyChanged(){GeographyRevision++;if(objectIndex!=null)objectIndex.RefreshHierarchy();}
        private LedgerPlaceBrowser placeBrowser;public LedgerPlaceBrowser PlaceBrowser {get{return placeBrowser??(placeBrowser=new LedgerPlaceBrowser(this));}}
        public LedgerObjectIndex Objects {get{return objectIndex??(objectIndex=new LedgerObjectIndex(this));}}
        public readonly List<LedgerObservation> Requests = new List<LedgerObservation>();
        public readonly List<LedgerObservation> DialoguePages = new List<LedgerObservation>();
        public readonly HashSet<string> AmbiguousDialogue = new HashSet<string>();
        public readonly List<LedgerRequestRecord> ObservedRequests = new List<LedgerRequestRecord>();
        public LedgerCatalog Catalog;
        public LedgerTradePage Trades=new LedgerTradePage();
        public string TradeReadError;
        public int? ManualPlayerId;
        public bool ManualStateReady;
        public string ManualReadError;
        public Dictionary<string,bool> ManualCompletions=new Dictionary<string,bool>();
        public Dictionary<string,string> CompletionStamps=new Dictionary<string,string>();
        public Dictionary<string,bool> ManualTracking=new Dictionary<string,bool>();
        public Dictionary<string,bool> ConversationArchives=new Dictionary<string,bool>();
        public bool ArchiveStateReady;public string ArchiveReadError;
        public bool TrackingStateReady;public string TrackingReadError;
        private readonly Dictionary<string,LedgerEndpointState> endpoints = new Dictionary<string,LedgerEndpointState>();
        public string WorldKey, Status = "Not loaded", LoadedUtc;
        public int UnboundRows;
        private List<LedgerViewItem> knownItems;
        private List<LedgerRequestCard> requestCards;
        private LedgerRequestMemo requestMemo=new LedgerRequestMemo();
        private LedgerProductionPlaces requestPlaces;private int requestGeography=-1;
        private LedgerLocationIndex locationIndex;private int locationGeography=-1;
        public LedgerLocationIndex Locations {get{if(locationIndex==null||locationGeography!=GeographyRevision){locationIndex=new LedgerLocationIndex(this);locationGeography=GeographyRevision;}return locationIndex;}}
        public LedgerProductionPlaces RequestPlaces {get{if(requestPlaces==null||requestGeography!=GeographyRevision){requestPlaces=new LedgerProductionPlaces(WorldKey,RequestCards().Select(c=>c.Source.Context));requestGeography=GeographyRevision;}return requestPlaces;}}
        private Dictionary<string,string> observedStructureNames;
        private readonly Dictionary<string,List<LedgerViewOffer>> offerCache = new Dictionary<string,List<LedgerViewOffer>>();
        public static LedgerViewData Read(string path, string world,LedgerCatalog catalog=null,int? playerId=null)
        {return ReadPrepared(path,world,catalog,playerId,null);}
        internal static LedgerViewData ReadPrepared(string path,string world,LedgerCatalog catalog,int? playerId,LedgerRequestMemo memo)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.SnapshotRead))
            {
            var result = new LedgerViewData { WorldKey = world,Catalog=catalog,ManualPlayerId=playerId,requestMemo=memo??new LedgerRequestMemo() };
            result.requestMemo.Begin();
            if (string.IsNullOrEmpty(world)) { result.Status = "Current save scope unknown; no records displayed"; return result; }
            if (!File.Exists(path)) { result.Status = "No local Ledger database yet"; return result; }
            LedgerSqliteRuntime.Prepare();
            using (LedgerPrivateMirror.Acquire(path))
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Version = 3, ReadOnly = true, FailIfMissing = true, Pooling = false, DefaultTimeout = 1 }.ToString()))
            {
                db.Open();
                using (var c = db.CreateCommand()) { c.CommandText = "PRAGMA query_only=ON"; c.ExecuteNonQuery(); }
                int version;
                using (var c = db.CreateCommand())
                {
                    c.CommandText = "PRAGMA application_id";
                    if (Convert.ToInt64(c.ExecuteScalar()) != 1414288177) throw new InvalidDataException("Not an owned Ledger database");
                    c.CommandText = "PRAGMA user_version";
                    version = Convert.ToInt32(c.ExecuteScalar());
                    if (version != 1 && version != 2) throw new InvalidDataException("Unsupported Ledger schema");
                }
                // A consistent read transaction, bounded rows and bounded individual JSON blobs.
                using (var tx = db.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                {
                    ReadRows(db, tx, "SELECT m.snapshot_json FROM markets m JOIN endpoints e ON e.endpoint_key=m.endpoint_key WHERE e.world_key=@world ORDER BY e.playfield,e.npc_id LIMIT 513", world, result.Markets, 512);
                    var pages = new List<LedgerObservation>();
                    ReadRows(db, tx, "SELECT d.capture_json FROM dialogue_pages d JOIN endpoints e ON e.endpoint_key=d.endpoint_key WHERE e.world_key=@world ORDER BY d.last_observed_utc DESC LIMIT 2049", world, pages, 2048);
                    if(version==2) ReadRows(db, tx,"SELECT d.encounter_json FROM dialogue_pages d JOIN endpoints e ON e.endpoint_key=d.endpoint_key WHERE e.world_key=@world AND d.encounter_json IS NOT NULL LIMIT 2049",world,pages,4096);
                    result.ReadEndpoints(db,tx,world,pages);
                    using(var ic=db.CreateCommand()){ic.Transaction=tx;ic.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='request_instances_v1'";if(Convert.ToInt32(ic.ExecuteScalar())>0)ReadRows(db,tx,"SELECT i.capture_json FROM request_instances_v1 i JOIN endpoints e ON e.endpoint_key=i.endpoint_key WHERE e.world_key=@world LIMIT 8193",world,pages,12289);}
                    if(pages.Any(o=>o.Context==null || o.Context.WorldKey!=world || o.Identity==null || !(o.Identity.NpcEntityId>0))) throw new InvalidDataException("Dialogue scope mismatch");
                    // Retain raw texts, ordinary latest dialogue and the independent latest recognized request.
                    foreach (var group in pages.Where(o => o.Dialogue != null).GroupBy(o => LedgerDatabase.Key(o) + "|" + LedgerJson.Hash(o.Dialogue.BodyVerbatim ?? "")+"|"+o.RequestInstance))
                        result.DialoguePages.Add(group.Aggregate((a,b)=>LedgerPresentation.CompareDialogue(a,b)>=0?a:b));
                    result.SelectLastFragments(pages);
                    result.SelectRequests(pages);
                    try{result.ManualCompletions=LedgerManualCompletion.Read(db,tx,world,playerId,false,result.CompletionStamps);result.ManualStateReady=playerId>0;}
                    catch(Exception e){result.ManualReadError=e.GetType().Name+":"+e.Message;result.ManualStateReady=false;}
                    try{result.ManualTracking=LedgerManualCompletion.Read(db,tx,world,playerId,true);result.TrackingStateReady=playerId>0;}
                    catch(Exception e){result.TrackingReadError=e.GetType().Name+":"+e.Message;result.TrackingStateReady=false;}
                    try{result.ConversationArchives=LedgerManualCompletion.Read(db,tx,world,playerId,false,null,true);result.ArchiveStateReady=playerId>0;}
                    catch(Exception e){result.ArchiveReadError=e.GetType().Name+":"+e.Message;result.ArchiveStateReady=false;}
                    try{result.DiscoveredObjects=LedgerDiscoveredObjects.Read(db,tx,world,playerId);}catch(Exception e){result.ObjectReadError=e.GetType().Name+":"+e.Message;}
                    try{result.Production=LedgerProduction.Read(db,tx,world,playerId);}catch(Exception e){result.ProductionReadError=e.GetType().Name;}
                    tx.Commit();
                }
            }
            foreach (var m in result.Markets)
            {
                if (m.Context == null || m.Context.WorldKey != world || m.FullCurrentAssortment != true || m.Offers == null || m.Offers.Count > 256) throw new InvalidDataException("Invalid complete market projection");
                result.UnboundRows += m.Offers.Count(o => !o.ItemId.HasValue || !LedgerOffer.Certified(o));
            }
            if (result.Requests.Any(o => o.Context == null || o.Context.WorldKey != world)) throw new InvalidDataException("Dialogue scope mismatch");
            // Legacy snapshots lack geography; read only the exact already-observed template header.
            var geoRecords=result.Markets.Concat(result.ObservedRequests.Select(r=>r.Source)).Concat(result.Requests).Where(o=>o.Context!=null).ToList();
            foreach(var group in geoRecords.GroupBy(o=>o.Context.Playfield))
            {
                var header=group.Any(o=>o.Context.Geography==null||string.IsNullOrEmpty(o.Context.Geography.BodyKind))?LedgerGeographyHeader.ReadKnown(group.First().Context):null;
                foreach(var o in group)if(header!=null&&(o.Context.Geography==null||string.IsNullOrEmpty(o.Context.Geography.BodyKind)))
                {
                    var old=o.Context.Geography;o.Context.Geography=new LedgerGeography{BodyKind=header.BodyKind,GroupName=old==null?null:old.GroupName,SystemCoordinates=old==null?null:old.SystemCoordinates,SectorCoordinates=old==null?null:old.SectorCoordinates,Source=(old==null?"":old.Source+";")+header.Source};
                }
            }
            LedgerGeographyProjection.FillStored(result);
            result.objectIndex=new LedgerObjectIndex(result);
            result.placeBrowser=new LedgerPlaceBrowser(result);
            // Finish all fact-backed cards on this worker, then detach the consumer's mutable
            // memo before publishing the DTO to the UI. A later read cannot race an old view.
            result.RequestCards();result.RetryableProjection=result.requestMemo.Incomplete;result.requestMemo.End();result.requestMemo=new LedgerRequestMemo();
            result.LoadedUtc = DateTime.UtcNow.ToString("o");
            result.Status = "Real local SQLite | " + result.Markets.Count + " markets / " + result.Markets.Sum(m => m.Offers.Count) + " observed rows | " + result.UnboundRows + " unbound";
            return result;

            }
        }
        private void ReadEndpoints(SqliteConnection db,SqliteTransaction tx,string world,List<LedgerObservation> pages)
        {
            using(var c=db.CreateCommand())
            {
                c.Transaction=tx;c.CommandText="SELECT endpoint_key,identity_json FROM endpoints WHERE world_key=@world LIMIT 513";c.Parameters.AddWithValue("@world",world);
                long total=0;
                using(var r=c.ExecuteReader())while(r.Read())
                {
                    if(endpoints.Count>=512)throw new InvalidDataException("Endpoint view limit");
                    byte[] bytes=(byte[])r[1];total+=bytes.Length;
                    if(bytes.Length>512*1024 || total>16*1024*1024)throw new InvalidDataException("Endpoint view size limit");
                    string key=(string)r[0];var state=LedgerJson.Decode<LedgerEndpointState>(bytes);endpoints.Add(key,state);
                    if(state.LastDialogue!=null)
                    {
                        var o=state.LastDialogue;
                        if(o.Context==null || o.Context.WorldKey!=world || o.Identity==null || !(o.Identity.NpcEntityId>0) || LedgerDatabase.Key(o)!=key || o.Dialogue==null)throw new InvalidDataException("Last dialogue endpoint mismatch");
                        pages.Add(o);
                    }
                    if(state.LastRequest!=null)
                    {
                        var o=state.LastRequest;
                        if(o.Context==null||o.Context.WorldKey!=world||o.Identity==null||!(o.Identity.NpcEntityId>0)||LedgerDatabase.Key(o)!=key||o.Dialogue==null)throw new InvalidDataException("Request endpoint mismatch");
                        pages.Add(o);
                    }
                }
            }
        }
        public void SelectRequests(IEnumerable<LedgerObservation> pages)
        {
            ObservedRequests.Clear();requestCards=null;requestPlaces=null;
            foreach(var group in pages.Where(o=>o.Kind=="dialogue-shown"&&o.Dialogue!=null).GroupBy(LedgerDatabase.Key))
            {
                LedgerRequestRecord latest=null;
                foreach(var o in group)
                {
                    var fact=requestMemo.Recognize(o,Catalog);if(fact==null)continue;
                    if(latest==null || LedgerPresentation.CompareDialogue(o,latest.Source)>0)latest=new LedgerRequestRecord{Source=o,Fact=fact};
                }
                if(latest!=null)ObservedRequests.Add(latest);
            }
            ObservedRequests.Sort((a,b)=>a.Source.Identity.NpcEntityId.Value.CompareTo(b.Source.Identity.NpcEntityId.Value));
        }
        public bool ManuallyCompleted(LedgerRequestCard card)
        {bool done;return card.Request!=null&&ManualStateReady&&ManualCompletions.TryGetValue(card.CompletionKey??LedgerManualCompletion.Key(card.Source),out done)&&done;}
        public bool ManuallyTracked(LedgerRequestCard card)
        {bool tracked;return card.Request!=null&&TrackingStateReady&&ManualTracking.TryGetValue(card.CompletionKey??LedgerManualCompletion.Key(card.Source),out tracked)&&tracked;}
        public bool ConversationArchived(LedgerObservation page)
        {bool archived;return ArchiveStateReady&&ConversationArchives.TryGetValue(LedgerManualCompletion.Key(page),out archived)&&archived;}
        public List<LedgerRequestCard> RequestCards(bool unfinishedOnly=false)
        {
            if(requestCards!=null)return requestCards.Where(r=>!unfinishedOnly||!ManuallyCompleted(r)).ToList();
            // Retained distinct request bodies remain individually reversible even after a new request.
            // Exact text, not page/visit/time, supplies identity; recognition metadata is not identity.
            var result=new List<LedgerRequestCard>();
            var unrecognized=new List<LedgerObservation>();
            foreach(var group in DialoguePages.Concat(ObservedRequests.Select(r=>r.Source)).Where(o=>o.Dialogue!=null&&!string.IsNullOrEmpty(o.Dialogue.BodyVerbatim)).GroupBy(LedgerManualCompletion.Key))
            {
                var source=group.Aggregate((a,b)=>LedgerPresentation.CompareDialogue(a,b)>=0?a:b);
                var fact=requestMemo.Recognize(source,Catalog);if(fact==null){unrecognized.Add(source);continue;}
                result.Add(new LedgerRequestCard{Source=source,CompletionKey=LedgerManualCompletion.Key(source),Request=new LedgerRequestRecord{Source=source,Fact=fact},Pages=new List<LedgerObservation>{source}});
            }
            var known=result.ToLookup(r=>LedgerDatabase.Key(r.Source));
            foreach(var group in unrecognized.GroupBy(LedgerDatabase.Key))
            {
                var recognized=known[group.Key].Select(r=>r.Source).ToList();
                var latest=recognized.Count==0?null:recognized.Aggregate((a,b)=>LedgerPresentation.CompareDialogue(a,b)>=0?a:b);
                // A known request does not cover a later unparsed page. Earlier setup/replies and
                // unrevealed samples must not clutter that NPC's cards. No inferred objective/Done.
                var candidates=group.Where(o=>latest==null||o.Dialogue.BodyFullyRevealed==true&&LedgerPresentation.CompareDialogue(o,latest)>0&&
                    !LedgerRequestLanguage.CompletedNarrative(LedgerPresentation.Text(o.Dialogue.BodyVerbatim))).ToList();
                var bodies=candidates.Concat(recognized).ToDictionary(o=>o,o=>LedgerPresentation.Text(o.Dialogue.BodyVerbatim));
                var pages=candidates.Where(o=>!string.IsNullOrWhiteSpace(bodies[o]))
                    .GroupBy(o=>bodies[o],StringComparer.Ordinal).Select(g=>g.Aggregate((a,b)=>LedgerPresentation.CompareDialogue(a,b)>=0?a:b)).ToList();
                // Collapse repeated rich-text forms and witnessed prefixes only in this projection.
                // Original pages remain in storage; never concatenate them into a parsed request.
                pages=pages.Where(o=>!bodies.Any(b=>b.Key!=o&&b.Value.Length>bodies[o].Length&&b.Value.StartsWith(bodies[o],StringComparison.Ordinal))).ToList();
                if(pages.Count==0)continue;
                pages.Sort(LedgerPresentation.CompareDialogue);result.Add(new LedgerRequestCard{Source=pages.Last(),Pages=pages});
            }
            requestCards=result.OrderBy(r=>PlaceFor(r.Source),StringComparer.OrdinalIgnoreCase).ThenBy(r=>VendorFor(r.Source),StringComparer.OrdinalIgnoreCase).ThenBy(r=>LedgerDatabase.Key(r.Source)).ToList();
            return requestCards.Where(r=>!unfinishedOnly||!ManuallyCompleted(r)).ToList();
        }
        public void SelectLastFragments(IEnumerable<LedgerObservation> pages)
        {
            Requests.Clear();AmbiguousDialogue.Clear();
            foreach(var group in pages.Where(o=>o.Dialogue!=null && !string.IsNullOrEmpty(o.Dialogue.BodyVerbatim)).GroupBy(LedgerDatabase.Key))
            {
                LedgerObservation latest=null;bool ambiguous=false;
                foreach(var o in group)
                {
                    int comparison=latest==null?1:LedgerPresentation.CompareDialogue(o,latest);
                    if(comparison>0){latest=o;ambiguous=false;}
                    else if(comparison==0 && o.Dialogue.BodyVerbatim!=latest.Dialogue.BodyVerbatim)ambiguous=true;
                }
                if(latest!=null){Requests.Add(latest);if(ambiguous)AmbiguousDialogue.Add(group.Key);}
            }
            Requests.Sort((a,b)=>a.Identity.NpcEntityId.Value.CompareTo(b.Identity.NpcEntityId.Value));
        }
        private static void ReadRows(SqliteConnection db, SqliteTransaction tx, string sql, string world, List<LedgerObservation> target, int limit)
        {
            long totalBytes = 0;
            using (var c = db.CreateCommand())
            {
                c.Transaction = tx; c.CommandText = sql; c.Parameters.AddWithValue("@world", world);
                using (var r = c.ExecuteReader()) while (r.Read())
                {
                    if (target.Count >= limit) throw new InvalidDataException("View row limit exceeded; display refused rather than silently incomplete");
                    long bytes = r.GetBytes(0, 0, null, 0, 0);
                    totalBytes += bytes;
                    if (bytes <= 0 || bytes > 512 * 1024 || totalBytes > 16 * 1024 * 1024) throw new InvalidDataException("View record/aggregate size limit");
                    target.Add(LedgerJson.Decode<LedgerObservation>((byte[])r.GetValue(0)));
                }
            }
        }
        public static LedgerObservation OfferMarket(LedgerObservation m,LedgerOffer o){return !o.RetainedRelation?m:new LedgerObservation{Kind=m.Kind,Utc=o.LastEvidenceUtc??m.Utc,GameTicks=m.GameTicks,Context=m.Context,Identity=m.Identity,FullCurrentAssortment=m.FullCurrentAssortment,Offers=m.Offers,OfferFreshness=null};}
        public List<LedgerViewItem> Items(string search)
        {
            if (knownItems == null) knownItems = Markets.SelectMany(m => m.Offers).Where(o => o.ItemId.HasValue && LedgerOffer.Certified(o))
                .GroupBy(o => o.ItemId.Value.ToString() + ":" + (o.AuxiliaryRaw.HasValue ? o.AuxiliaryRaw.Value.ToString() : "unknown"))
                .Select(g => new LedgerViewItem { Key = g.Key, Id = g.First().ItemId.Value, Auxiliary = g.First().AuxiliaryRaw,
                    Markup = g.Select(o=>o.Visible==null?null:o.Visible.Name).FirstOrDefault(n=>!string.IsNullOrEmpty(n)),
                    Name = LedgerPresentation.Text(g.Select(o => o.Visible == null ? null : o.Visible.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "Unknown name"),
                    Sprite=g.Select(o=>o.Visible==null?null:o.Visible.Sprite).FirstOrDefault(s=>!string.IsNullOrEmpty(s)),
                    Category=Category(g.First().ItemId.Value) })
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Key).ToList();
            return string.IsNullOrEmpty(search) ? knownItems : knownItems.Where(i => (i.Name + " " + i.Key).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
        private string Category(int id){LedgerCatalogItem item;return Catalog!=null&&Catalog.Items.TryGetValue(id,out item)&&!string.IsNullOrWhiteSpace(item.Category)?item.Category:"Uncategorized";}
        public List<LedgerViewItem> Browse(string search,string category)
        {return Items(search).Where(i=>!string.IsNullOrEmpty(search)||string.IsNullOrEmpty(category)||category=="All items"||i.Category==category).ToList();}
        public List<string> Groups(){return new[]{"All items"}.Concat(Items(null).Select(i=>i.Category).Distinct().OrderBy(s=>s,StringComparer.OrdinalIgnoreCase)).ToList();}
        public List<LedgerViewOffer> Offers(LedgerViewItem item, bool sellers)
        {
            if (item == null) return new List<LedgerViewOffer>();
            string key = item.Key + ":" + sellers;
            List<LedgerViewOffer> cached;
            if (offerCache.TryGetValue(key, out cached)) return cached;
            cached = Markets.SelectMany(m => m.Offers.Where(o => LedgerOffer.Certified(o) && o.ItemId == item.Id && (item.Canonical || o.AuxiliaryRaw == item.Auxiliary))
                .Select(o => new LedgerViewOffer { Market = LedgerViewData.OfferMarket(m,o), Offer = o, Sellers = sellers }))
                .Where(r => (sellers ? r.Offer.NpcSellsSupported : r.Offer.NpcBuysSupported) != false)
                .OrderBy(r => r.Market.Context.Playfield).ThenBy(r => r.Market.Identity.NpcEntityId).ToList();
            offerCache[key] = cached; return cached;
        }
        public static string Place(LedgerObservation o)
        {
            var id = o.Identity;
            return LedgerPresentation.Text(id == null ? "Unknown POI" : id.StructureName ?? id.CatalogDisplayName ?? "Unknown POI") + " / " +
                (o.Context == null ? "Unknown place" : o.Context.Playfield ?? "Unknown place");
        }
        internal LedgerObjectRecord DiscoveredOwner(LedgerObservation o)
        {
            if(o==null||o.Context==null||o.Identity==null||!(o.Identity.NpcEntityId>0))return null;
            LedgerEndpointState state;if(endpoints.TryGetValue(LedgerDatabase.Key(o),out state)&&state.Identity!=null){var a=o.Identity;var b=state.Identity;
                if(b.OwnerConsistent==false||a.StructureEntityId.HasValue&&b.StructureEntityId.HasValue&&a.StructureEntityId!=b.StructureEntityId||a.BlockGrid!=null&&b.BlockGrid!=null&&a.BlockGrid!=b.BlockGrid)return null;}
            return Objects.Owner(o);
        }
        public string PlaceFor(LedgerObservation o)
        {
            var discovered=DiscoveredOwner(o);if(o!=null&&o.Identity!=null&&string.IsNullOrEmpty(o.Identity.StructureName??o.Identity.CatalogDisplayName)&&discovered!=null&&Objects.Title(discovered)!="Unknown place")return LedgerPresentation.Text(Objects.Title(discovered))+" / "+o.Context.Playfield;
            if(o.Identity==null || !(o.Identity.NpcEntityId>0) || o.Context==null || o.Context.WorldKey!=WorldKey)return Place(o);
            LedgerEndpointState known;
            if(!endpoints.TryGetValue(LedgerDatabase.Key(o),out known) || known.Identity==null)
                return DialoguePages.Any(p=>p.Context!=null&&p.Identity!=null&&LedgerDatabase.Key(p)==LedgerDatabase.Key(o)&&p.Identity.StructureEntityId==o.Identity.StructureEntityId&&p.Identity.BlockGrid==o.Identity.BlockGrid)?NamedPlace(o,o.Identity):Place(o);
            var a=o.Identity;var b=known.Identity;
            if(a.StructureEntityId.HasValue && b.StructureEntityId.HasValue && a.StructureEntityId!=b.StructureEntityId ||
                a.BlockGrid!=null && b.BlockGrid!=null && a.BlockGrid!=b.BlockGrid || a.OwnerConsistent==false || b.OwnerConsistent==false)return Place(o);
            // Endpoint merge already certified this scoped NPC's owner; do not guess by title/proximity.
            string name=a.StructureName??a.CatalogDisplayName;
            if(string.IsNullOrEmpty(name) && b.StructureEntityId.HasValue)name=b.StructureName??b.CatalogDisplayName;
            if(string.IsNullOrEmpty(name))return NamedPlace(o,b.StructureEntityId.HasValue?b:a);
            return LedgerPresentation.Text(name)+" / "+(o.Context.Playfield??"Unknown place");
        }
        // Presentation-only join on an observed, exact world/playfield/owner ID. Never a
        // name/proximity lookup and never a source for coordinates, discovery or new endpoints.
        private string NamedPlace(LedgerObservation o,LedgerIdentity id)
        {
            if(id==null||!(id.StructureEntityId>0)||id.OwnerConsistent==false||!string.IsNullOrEmpty(o.Identity.StructureName??o.Identity.CatalogDisplayName))return Place(o);
            if(observedStructureNames==null)
            {
                observedStructureNames=new Dictionary<string,string>(StringComparer.Ordinal);
                var seen=Markets.Select(m=>new{Context=m.Context,Identity=m.Identity}).Concat(endpoints.Values.Where(e=>e.LastDialogue!=null).Select(e=>new{Context=e.LastDialogue.Context,Identity=e.Identity}));
                foreach(var source in seen)
                {
                    var a=source.Identity;var c=source.Context;
                    if(c==null||c.WorldKey!=WorldKey||string.IsNullOrEmpty(c.Playfield)||a==null||!(a.StructureEntityId>0)||a.OwnerConsistent!=true)continue;
                    string name=LedgerPresentation.Text(a.StructureName??a.CatalogDisplayName);if(string.IsNullOrWhiteSpace(name))continue;
                    string k=c.Playfield+"|"+a.StructureEntityId.Value;string prior;
                    if(observedStructureNames.TryGetValue(k,out prior)&&prior!=name)observedStructureNames[k]=null;else if(!observedStructureNames.ContainsKey(k))observedStructureNames.Add(k,name);
                }
            }
            string found;return observedStructureNames.TryGetValue(o.Context.Playfield+"|"+id.StructureEntityId.Value,out found)&&found!=null?found+" / "+o.Context.Playfield:Place(o);
        }
        // Same scoped endpoint only; never resolve by name, proximity, current controller or another NPC.
        public LedgerIdentity ObservedPositionIdentity(LedgerObservation o)
        {
            if(o==null||o.Context==null||o.Context.WorldKey!=WorldKey||o.Identity==null||!(o.Identity.NpcEntityId>0)||o.Identity.OwnerConsistent==false)return null;
            var identity=o.Identity;LedgerEndpointState known;
            if(endpoints.TryGetValue(LedgerDatabase.Key(o),out known)&&known.Identity!=null)
            {
                var b=known.Identity;
                if(b.OwnerConsistent==false || identity.StructureEntityId.HasValue&&b.StructureEntityId.HasValue&&identity.StructureEntityId!=b.StructureEntityId || identity.BlockGrid!=null&&b.BlockGrid!=null&&identity.BlockGrid!=b.BlockGrid)return null;
                if(string.IsNullOrEmpty(identity.NativePosition)&&(!string.IsNullOrEmpty(b.NativePosition)||string.IsNullOrEmpty(identity.StructureNativePosition)))identity=b;
            }
            return identity;
        }
        public string DistanceFor(LedgerObservation o,LedgerPlayerPoint player)
        {
            if(o==null||o.Context==null)return "unknown";
            if(player!=null&&player.Playfield!=o.Context.Playfield){var cross=CrossDistance(o,player);return cross==null?"unknown":cross.Text;}
            var identity=ObservedPositionIdentity(o);
            string position,source;LedgerBookmarkPolicy.NavigationPosition(identity,out position,out source);
            return LedgerBrowse.Distance(player,o.Context.WorldKey,o.Context.Playfield,position,source);
        }
        public double? DistanceMetersFor(LedgerObservation o,LedgerPlayerPoint player)
        {var id=ObservedPositionIdentity(o);string position,source;LedgerBookmarkPolicy.NavigationPosition(id,out position,out source);return LedgerBrowse.DistanceMeters(player,o.Context.WorldKey,o.Context.Playfield,position,source);}
        public string DistanceReasonFor(LedgerObservation o,LedgerPlayerPoint player)
        {
            if(o==null||o.Context==null)return "Location unavailable.";
            if(player!=null&&player.World==o.Context.WorldKey&&player.Playfield!=o.Context.Playfield){var cross=CrossDistance(o,player);return cross==null?"Cross-playfield distance unavailable; missing/conflicting native geography or shared planetary sector.":cross.Reason;}
            var identity=ObservedPositionIdentity(o);
            string position,source;LedgerBookmarkPolicy.NavigationPosition(identity,out position,out source);
            return LedgerBrowse.DistanceReason(player,o.Context.WorldKey,o.Context.Playfield,position,source);
        }
        public static string Vendor(LedgerObservation o)
        {
            string title = o.Dialogue == null ? null : o.Dialogue.TitleVerbatim;
            return LedgerPresentation.Text(string.IsNullOrEmpty(title) ? "Vendor" : title);
        }
        public string VendorFor(LedgerObservation o)
        {
            var d = Requests.FirstOrDefault(r => r.Identity != null && o.Identity != null && r.Identity.NpcEntityId == o.Identity.NpcEntityId && r.Context.Playfield == o.Context.Playfield);
            return Vendor(d ?? o);
        }
    }
    public static class LedgerWindowPolicy
    {
        public static bool OwnedModal(object expectedRoot, object root, object expectedConfig, object config, object expectedHandler, object handler)
        { return expectedRoot != null && expectedConfig != null && expectedHandler != null && ReferenceEquals(expectedRoot,root) && ReferenceEquals(expectedConfig,config) && ReferenceEquals(expectedHandler,handler); }
        public static bool CanReopen(bool emptyNativeStack,bool ownedVisualStillActive){return emptyNativeStack&&!ownedVisualStillActive;}
        // A leftover global IMGUI control ID while closed is not evidence of live text entry.
        public static bool CanToggle(bool pressed, bool typing, int keyboardControl) { return pressed && !typing; }
        public static bool CanOpen(bool ready, bool scoped, bool emptyNativeStack, bool worldVisible) { return ready && scoped && emptyNativeStack && worldVisible; }
        public static bool CanRender(bool open, bool failed, bool ownedTop) { return open && !failed && ownedTop; }
    }
    public sealed class LedgerRequestCard { public string CompletionKey; public LedgerObservation Source; public LedgerRequestRecord Request; public List<LedgerObservation> Pages; }
    public sealed class LedgerViewItem { public string Key, Name, Markup, Category, Sprite; public int Id; public int? Auxiliary; public bool Canonical; }
    public sealed class LedgerViewOffer
    {
        public LedgerObservation Market; public LedgerOffer Offer; public bool Sellers;
        public string Price { get { return (Sellers ? Offer.StoredSellUnit : Offer.StoredBuyUnit) ?? "unknown"; } }
        public string Quantity { get { return Sellers ? (Offer.Stock.HasValue ? Offer.Stock.Value.ToString() : "unknown") : (Offer.RemainingNpcBuyingCapacity.HasValue ? Offer.RemainingNpcBuyingCapacity.Value.ToString() : "unknown"); } }
        public string Availability { get { bool? supported = Sellers ? Offer.NpcSellsSupported : Offer.NpcBuysSupported; bool? available = Sellers ? Offer.NpcSellsAvailable : Offer.NpcBuysAvailable;
            return supported == false ? "unsupported" : !supported.HasValue ? "support unknown" : available == false ? "unavailable" : available == true ? "available" : "availability unknown"; } }
    }
}
