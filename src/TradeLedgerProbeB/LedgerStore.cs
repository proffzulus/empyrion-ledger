using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Mono.Data.Sqlite;
using System.Threading;

namespace TradeLedgerProbeB
{
    public sealed class LedgerLimits
    {
        public int QueueCount = 256, QueueBytes = 8 * 1024 * 1024, RecordBytes = 512 * 1024;
        public int DialoguePagesPerEndpoint = 128;
        public long DiskBytes = 512L * 1024 * 1024;
    }
    public sealed class LedgerStatus
    {
        public string State, Error, Root;
        public long Queued, Committed, Recovered, Rejected, Failures, SkippedPartial, IdentityRejected, StaleRejected, DialogueMigrated, DialogueDuplicatesCollapsed,Unchanged;
        public int Pending;
    }
    // All SQLite transactions and serialization run on the bounded worker, never on the gameplay thread.
    public sealed class LedgerStore : IDisposable
    {
        private sealed class Pending { internal LedgerObservation Value; internal LedgerManualCompletion Manual; internal LedgerObjectDisposition ObjectDisposition;internal LedgerObjectDisposition[] ObjectDispositions; internal Action<string> Done; internal int Estimate; internal bool Saved,Attempted,Publish; internal string Outcome; }
        private readonly object gate = new object();
        private readonly Queue<Pending> queue = new Queue<Pending>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Thread worker;
        public event Action<LedgerObservation> ObservationCommitted;
        public event Action<LedgerManualCompletion> AnnotationCommitted;
        public bool OwnedPublicationAllowed {get{return portable==null||portable.PublicationAllowed;}}
        private readonly LedgerLimits limits;
        private readonly string root;
        private readonly Action<string> fault;
        private readonly LedgerPortableSave portable;
        private LedgerStatus status;
        private bool closing;
        private int queuedBytes;
        public string BookmarkOwnershipPath {get{return Path.Combine(portable==null?root:portable.Root,"LedgerBookmarks.json");}}
        public void PublishOwnedFile(Action action){if(portable==null)action();else portable.PublishOwnedFile(action);}
        public LedgerStore(string path, LedgerLimits bounds = null, Action<string> injectFault = null, LedgerPortableSave publisher = null)
        {
            root = Path.GetFullPath(path); limits = bounds ?? new LedgerLimits(); fault = injectFault; portable=publisher;
            status = new LedgerStatus { State = "starting", Root = root };
            worker = new Thread(Work) { IsBackground = true, Name = "TradeLedger-local-store" }; worker.Start();
        }
        public LedgerStatus Status
        {
            get { lock (gate) { var s = LedgerStatusCopy.Copy(status); s.Pending = queue.Count; return s; } }
        }
        public bool Enqueue(LedgerObservation value,Action<string> done=null)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.StoreEnqueue))
            {
            // A transferred, exclusively owned DTO: no serialization, disk, waiting or game object inspection here.
            int estimate = Estimate(value);
            lock (gate)
            {
                if (closing || estimate <= 0 || queue.Count >= limits.QueueCount || queuedBytes + estimate > limits.QueueBytes || status.State == "blocked")
                { status.Rejected++; return false; }
                queue.Enqueue(new Pending { Value = value, Estimate = estimate,Done=done }); queuedBytes += estimate; status.Queued++;
                LedgerProfiler.QueueDepth(queue.Count);
                wake.Set(); return true;
            }

            }
        }
        // Retired financial accumulation. Kept as a source-compatible rejected endpoint.
        public bool EnqueueTrade(LedgerTradeReceipt value){return false;}
        public bool EnqueueManualCompletion(LedgerManualCompletion value,Action<string> done)
        {
            if(value==null||done==null)return false;int estimate=8192+value.Body.Length*6;
            lock(gate)
            {
                if(closing||queue.Count>=limits.QueueCount||queuedBytes+estimate>limits.QueueBytes||status.State=="blocked")return false;
                queue.Enqueue(new Pending{Manual=value,Done=done,Estimate=estimate});queuedBytes+=estimate;status.Queued++;wake.Set();return true;
            }
        }
        public bool EnqueueObjectDisposition(LedgerObjectDisposition value,Action<string> done)
        {
            if(value==null||!value.Valid()||done==null)return false;
            lock(gate){if(closing||queue.Count>=limits.QueueCount||queuedBytes+8192>limits.QueueBytes||status.State=="blocked")return false;
                queue.Enqueue(new Pending{ObjectDisposition=value,Done=done,Estimate=8192});queuedBytes+=8192;status.Queued++;wake.Set();return true;}
        }
        public bool EnqueueObjectDispositions(LedgerObjectDisposition[] values,Action<string> done)
        {
            if(values==null||values.Length==0||values.Length>LedgerDiscoveredObjects.Limit||values.Any(v=>v==null||!v.Valid())||done==null)return false;
            long estimate=values.Sum(v=>2048L+(v.Observed==null?0:(v.Observed.DiscoveredObject.RawLabel??"").Length*6L+4096));
            lock(gate){if(closing||queue.Count>=limits.QueueCount||queuedBytes+estimate>limits.QueueBytes||status.State=="blocked")return false;
                queue.Enqueue(new Pending{ObjectDispositions=values,Done=done,Estimate=(int)estimate});queuedBytes+=(int)estimate;status.Queued++;wake.Set();return true;}
        }
        private static void Complete(Pending p,string error)
        {var done=Interlocked.Exchange(ref p.Done,null);if(done!=null)try{done(error);}catch(Exception){/* UI notification cannot abort durable capture. */}}
        private static int Estimate(LedgerObservation o)
        {
            if (o == null || o.Context == null || !o.Context.Recordable || o.Identity == null || string.IsNullOrEmpty(o.ObservationId)) return -1;
            if (o.Offers != null && o.Offers.Count > 256) return -1;
            if (o.Dialogue != null && ((o.Dialogue.BodyVerbatim ?? "").Length > 4096 || o.Dialogue.OptionsVerbatim.Count > 32)) return -1;
            int size = 8192;
            if(o.DiscoveredObject!=null){if(!LedgerDiscoveredObjects.Valid(o))return -1;size=32768;}
            if(o.ProductionRetirement!=null){if(!LedgerProductionLifecycle.Valid(o))return -1;size=8192;}
            if(o.Production!=null){if(!LedgerProduction.Valid(o))return -1;size=65536;}
            if (o.Offers != null) foreach (var r in o.Offers)
            {
                if (r == null) return -1;
                size += 2048;
                if (r.Visible != null)
                    foreach (string s in new[] { r.Visible.Name, r.Visible.Sprite, r.Visible.StockText, r.Visible.CapacityText, r.Visible.SellPriceText, r.Visible.BuyPriceText })
                    { if (s != null && s.Length > 1024) return -1; size += (s ?? "").Length * 6; }
            }
            if (o.Dialogue != null)
            {
                size += (o.Dialogue.BodyVerbatim ?? "").Length * 6 + (o.Dialogue.TitleVerbatim ?? "").Length * 6;
                foreach (string s in o.Dialogue.OptionsVerbatim) { if (s != null && s.Length > 4096) return -1; size += (s ?? "").Length * 6; }
            }
            return size;
        }
        public bool WaitForIdle(int milliseconds)
        {
            var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < end)
            {
                var s = Status;
                if (s.Pending == 0 && s.State != "starting" && s.State != "recovering") return true;
                Thread.Sleep(10);
            }
            return false;
        }
        private void Work()
        {
            LedgerDatabase database = null;
            try
            {
                while (true)
                {
                    Pending p;
                    lock (gate) { p = queue.Count == 0 ? null : queue.Peek(); if (closing && p == null) break; }
                    try
                    {
                        if (database == null)
                        using(LedgerProfiler.Measure(LedgerPerfPath.StoreOpen))
                        {
                            lock (gate) status.State = "recovering";
                            database = new LedgerDatabase(root, limits, fault);
                            // SQLite may wait for a file lock. Never hold the gameplay enqueue/status
                            // gate across a query, including portable reopen after each publication.
                            if(fault!=null)fault("before-endpoint-count");
                            long recovered=database.EndpointCount;
                            lock (gate) { status.Recovered = recovered; status.DialogueMigrated += database.DialogueMigrated; status.DialogueDuplicatesCollapsed += database.DialogueDuplicatesCollapsed; status.State = "ready"; status.Error = null; }
                        }
                        if (p == null)
                        {
                            wake.WaitOne(500); continue;
                        }
                        if(portable!=null&&!portable.PublicationAllowed)
                        { lock(gate)status.State="paused-native-save-menu";wake.WaitOne(100);continue; }
                        string outcome=p.Outcome;
                        if(!p.Saved)
                        {
                            bool retry=p.Attempted;p.Attempted=true;
                            if(portable!=null)portable.MarkDirty();
                            if(p.ObjectDispositions!=null){database.SaveObjectDispositions(p.ObjectDispositions);outcome="committed";}else if(p.ObjectDisposition!=null){database.SaveObjectDisposition(p.ObjectDisposition);outcome="committed";}else if(p.Manual!=null){database.SaveManualCompletion(p.Manual);outcome="committed";}else outcome=database.Save(p.Value);
                            if(portable!=null){
                                // A completed non-committing Save rolls back row changes (or is an
                                // exact object duplicate). Do not clone/fsync an unchanged authority.
                                // Any uncertain prior attempt forces publication, even if retry is duplicate.
                                p.Saved=true;p.Outcome=outcome;p.Publish=outcome=="committed"||retry;
                                if(!p.Publish)portable.MarkUnchanged();
                            }
                        }
                        if(portable!=null&&p.Publish){database.Dispose();database=null;portable.Publish();}
                        lock (gate)
                        {
                            queue.Dequeue(); queuedBytes -= p.Estimate;
                            LedgerProfiler.QueueDepth(queue.Count);
                            if (outcome == "committed") status.Committed++;
                            else if (outcome == "partial") status.SkippedPartial++;
                            else if (outcome == "stale") status.StaleRejected++;
                            else if (outcome == "duplicate" || outcome == "prefix") status.Unchanged++;
                            else status.IdentityRejected++;
                            status.State = "ready"; status.Error = null;
                        }
                        Complete(p,p.Value!=null&&p.Value.Kind=="production-lifecycle"&&outcome!="committed"?"record-changed;refresh-and-retry":null);
                        // Owned DTO notification only, after successful publication; failures cannot affect capture.
                        if(outcome=="committed"&&p.Value!=null){var notification=ObservationCommitted;if(notification!=null)foreach(Action<LedgerObservation> listener in notification.GetInvocationList())try{listener(p.Value);}catch(Exception){}}
                        if(outcome=="committed"&&p.Manual!=null&&!p.Manual.IsTracking&&!p.Manual.IsArchive){var notification=AnnotationCommitted;if(notification!=null)foreach(Action<LedgerManualCompletion> listener in notification.GetInvocationList())try{listener(p.Manual);}catch(Exception){}}
                        // Limit disk churn independently of the API's capture rate; no gameplay thread waits here.
                        if(portable==null||p.Publish)Thread.Sleep(portable==null?50:250);
                    }
                    catch (Exception error)
                    {
                        if(error is LedgerPublicationPausedException)
                        {lock(gate)status.State="paused-native-save-menu";wake.WaitOne(100);continue;}
                        bool manual=p!=null&&(p.ObjectDispositions!=null||p.ObjectDisposition!=null||p.Manual!=null||p.Value!=null&&p.Value.ProductionRetirement!=null&&p.Value.ProductionRetirement.Reason=="manual-forget");
                        bool permanent = (!manual || p.Saved) && error is InvalidDataException;
                        lock (gate) { status.Failures++; status.State = permanent ? "blocked" : "retrying"; status.Error = error.GetType().Name + ":" + error.Message; }
                        if(manual&&!p.Saved)
                        {
                            lock(gate){queue.Dequeue();queuedBytes-=p.Estimate;}
                            Complete(p,error.GetType().Name+":"+error.Message);
                        }
                        if (database != null) { database.Dispose(); database = null; }
                        if (permanent) break;
                        lock (gate) if (closing) break;
                        wake.WaitOne(1000);
                    }
                }
            }
            finally
            {
                try{if(database!=null)database.Dispose();}
                finally
                {
                    try{if(portable!=null)portable.Dispose();}
                    finally
                    {
                        Pending[] unfinished;
                        lock(gate){if(status.State!="blocked")status.State=queue.Count==0?"closed":"closed-with-unwritten-queue";unfinished=queue.ToArray();}
                        // Preserve queued work/status/recovery evidence. A terminal worker cannot
                        // promise publication, but must release waiting UI with an honest error.
                        foreach(var item in unfinished)Complete(item,"store-closed-before-ack;refresh-before-retry");
                    }
                }
            }
        }
        public void Dispose()
        {
            lock (gate) closing = true;
            wake.Set();
            if (!worker.Join(1500)) lock (gate) { status.State = "shutdown-flush-pending"; status.Error = "worker exceeded 1500ms; queued data not yet guaranteed durable"; }
            // Do not dispose the signal while an outstanding OS I/O may still own the background worker.
        }
    }
    public static class LedgerStatusCopy
    {
        public static LedgerStatus Copy(LedgerStatus s)
        {
            return new LedgerStatus { State=s.State, Error=s.Error, Root=s.Root, Queued=s.Queued, Committed=s.Committed, Recovered=s.Recovered,
                Rejected=s.Rejected, Failures=s.Failures, Pending=s.Pending, SkippedPartial=s.SkippedPartial, IdentityRejected=s.IdentityRejected, StaleRejected=s.StaleRejected, DialogueMigrated=s.DialogueMigrated, DialogueDuplicatesCollapsed=s.DialogueDuplicatesCollapsed,Unchanged=s.Unchanged };
        }
    }

    // Uses only the SQLite provider and native library shipped with this exact Windows client.
    // No game methods execute and no game-owned database is ever opened.
    public static class LedgerSqliteRuntime
    {
        private static readonly object Gate = new object();
        private static IntPtr native;
        public const string ProviderHash = "91914602F682E0D4278DEC0C1F29E3A781FC63C8C3FD7DC864E6AD3F9B95FD38";
        public const string NativeHash = "17A3DB82B949EBC604D613F8EBB8371A874100F4E293D841AE16AEA2C616B644";
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);
        public static void Prepare()
        {
            lock (Gate)
            {
                if (native != IntPtr.Zero) return;
                string client = Path.GetDirectoryName(Path.GetDirectoryName(typeof(SqliteConnection).Assembly.Location));
                string provider = Path.Combine(client, "Managed", "Mono.Data.Sqlite.dll");
                string dll = Path.Combine(client, "Plugins", "x86_64", "sqlite3.dll");
                if (IntPtr.Size != 8 || Safe.Hash(provider) != ProviderHash || Safe.Hash(dll) != NativeHash)
                    throw new InvalidDataException("SQLite-runtime-fingerprint-or-bitness-mismatch;storage-disabled");
                // Keep the verified shipped native dependency loaded for the process lifetime.
                native = LoadLibraryW(dll);
                if (native == IntPtr.Zero) throw new IOException("SQLite-native-load-failed:" + Marshal.GetLastWin32Error());
            }
        }
    }
    public sealed partial class LedgerDatabase : IDisposable
    {
        public const string FileName = "TradeLedger.sqlite";
        private const int ApplicationId = 1414288177; // TLS1, owned latest-snapshot schema
        private SqliteConnection connection;
        private SqliteTransaction transaction;
        private readonly LedgerLimits limits;
        private readonly Action<string> fault;
        public readonly string DatabasePath;
        public long EndpointCount { get { return Convert.ToInt64(Scalar("SELECT count(*) FROM endpoints"), CultureInfo.InvariantCulture); } }
        public string SQLiteVersion { get { return Convert.ToString(Scalar("SELECT sqlite_version()"), CultureInfo.InvariantCulture); } }
        public LedgerDatabase(string root, LedgerLimits bounds = null, Action<string> injectFault = null)
        {
            limits = bounds ?? new LedgerLimits(); fault = injectFault;
            DatabasePath = Path.Combine(Path.GetFullPath(root), FileName);
            try
            {
                LedgerSqliteRuntime.Prepare();
                if (Safe.Hash(typeof(SqliteConnection).Assembly.Location) != LedgerSqliteRuntime.ProviderHash)
                    throw new InvalidDataException("SQLite-loaded-provider-mismatch;storage-disabled");
                NoLinks(DatabasePath); NoLinks(DatabasePath + "-journal");
                Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));
                bool existed = File.Exists(DatabasePath);
                if (existed) ValidateHeader(DatabasePath);
                connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Version = 3, Pooling = false, DefaultTimeout = 1 }.ToString());
                connection.Open();
                int app = Convert.ToInt32(Scalar("PRAGMA application_id"), CultureInfo.InvariantCulture);
                int version = Convert.ToInt32(Scalar("PRAGMA user_version"), CultureInfo.InvariantCulture);
                if (existed && (app != ApplicationId || (version != 1 && version != 2))) throw new InvalidDataException("unknown-SQLite-owner-or-schema;not-modified");
                Exec("PRAGMA busy_timeout=500; PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL; PRAGMA journal_mode=DELETE");
                long page = Convert.ToInt64(Scalar("PRAGMA page_size"), CultureInfo.InvariantCulture);
                if (limits.DiskBytes < page || (existed && new FileInfo(DatabasePath).Length > limits.DiskBytes)) throw new InvalidDataException("SQLite-size-limit;preserved");
                Exec("PRAGMA max_page_count=" + (limits.DiskBytes / page).ToString(CultureInfo.InvariantCulture));
                if (!existed)
                {
                    using (transaction = connection.BeginTransaction())
                    {
                        Exec(@"CREATE TABLE endpoints(endpoint_key TEXT PRIMARY KEY, world_key TEXT NOT NULL, playfield TEXT NOT NULL, npc_id INTEGER NOT NULL, context_json BLOB NOT NULL, identity_json BLOB NOT NULL, UNIQUE(world_key,playfield,npc_id));
CREATE TABLE markets(endpoint_key TEXT PRIMARY KEY REFERENCES endpoints(endpoint_key), last_observed_utc TEXT NOT NULL, capture_id TEXT NOT NULL, snapshot_json BLOB NOT NULL);
CREATE TABLE offers(endpoint_key TEXT NOT NULL REFERENCES markets(endpoint_key), ui_row INTEGER NOT NULL, item_id INTEGER, stock INTEGER, capacity INTEGER, remaining_demand INTEGER, sells_supported INTEGER, buys_supported INTEGER, stored_sell_unit TEXT, stored_buy_unit TEXT, offer_json BLOB NOT NULL, PRIMARY KEY(endpoint_key,ui_row));
CREATE INDEX offers_item ON offers(item_id);
CREATE TABLE dialogue_pages(endpoint_key TEXT NOT NULL REFERENCES endpoints(endpoint_key), state_key TEXT NOT NULL, content_key TEXT NOT NULL, last_observed_utc TEXT NOT NULL, last_encountered_utc TEXT NOT NULL, capture_json BLOB NOT NULL, encounter_json BLOB, encounter_kind TEXT NOT NULL, PRIMARY KEY(endpoint_key,content_key));
CREATE TABLE stations(world_key TEXT NOT NULL, playfield TEXT NOT NULL, structure_id INTEGER NOT NULL, name TEXT NOT NULL, source TEXT, last_observed_utc TEXT NOT NULL, PRIMARY KEY(world_key,playfield,structure_id));");
                        Exec("PRAGMA application_id=" + ApplicationId + "; PRAGMA user_version=2");
                        transaction.Commit();
                    }
                    transaction = null;
                }
                if (Convert.ToString(Scalar("PRAGMA quick_check(1)"), CultureInfo.InvariantCulture) != "ok") throw new InvalidDataException("SQLite-integrity-check-failed;no-repair");
                if (existed && version == 1) MigrateDialogue();
            }
            catch { Dispose(); throw; }
        }
        private static void ValidateHeader(string path)
        {
            // Check ownership before the provider can apply any connection-open PRAGMAs.
            // Committed schema ownership survives ordinary SQLite hot-journal recovery.
            byte[] header = new byte[100];
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                if (file.Read(header,0,header.Length) != header.Length) throw new InvalidDataException("unowned-or-incomplete-SQLite-header");
            if (System.Text.Encoding.ASCII.GetString(header,0,16) != "SQLite format 3\0" || BigEndian(header,68) != ApplicationId || (BigEndian(header,60) != 1 && BigEndian(header,60) != 2))
                throw new InvalidDataException("unknown-SQLite-owner-or-schema;not-opened-for-write");
        }
        private static int BigEndian(byte[] b, int offset)
        { return (b[offset]<<24) | (b[offset+1]<<16) | (b[offset+2]<<8) | b[offset+3]; }
        private static void NoLinks(string path)
        {
            for (string p = Path.GetFullPath(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
                if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("ledger-reparse-path-refused");
        }
        private SqliteCommand Command(string sql, params object[] args)
        {
            var c = connection.CreateCommand(); c.CommandText = sql; c.Transaction = transaction; c.CommandTimeout = 1;
            for (int i = 0; i < args.Length; i++) c.Parameters.AddWithValue("@p" + i.ToString(CultureInfo.InvariantCulture), args[i] ?? DBNull.Value);
            return c;
        }
        public object Scalar(string sql, params object[] args) { using (var c = Command(sql, args)) return c.ExecuteScalar(); }
        private void Exec(string sql, params object[] args) { using (var c = Command(sql, args)) c.ExecuteNonQuery(); }
        public static string Key(LedgerObservation o)
        { return o.Context.WorldKey + ":" + LedgerJson.Hash(o.Context.Playfield) + ":npc:" + o.Identity.NpcEntityId.Value.ToString(CultureInfo.InvariantCulture); }
        private static bool Different<T>(T? a, T? b) where T : struct { return a.HasValue && b.HasValue && !a.Value.Equals(b.Value); }
        private static bool Different(string a, string b) { return a != null && b != null && a != b; }
        private static bool AnchoredPositionDrift(LedgerIdentity a,LedgerIdentity b)
        {
            // Live 5188 reloads: DeviceLayout Y81.5 ->81.51001. Position is evidence, not an ID.
            // Accept only <=5cm on an independently identical positive structure + exact block + source.
            if(!(a.StructureEntityId>0)||a.StructureEntityId!=b.StructureEntityId||string.IsNullOrEmpty(a.BlockGrid)||a.BlockGrid!=b.BlockGrid||a.PositionSource==null||a.PositionSource!=b.PositionSource)return false;
            var x=a.NativePosition.Split(',');var y=b.NativePosition.Split(',');if(x.Length!=3||y.Length!=3)return false;
            double squared=0;
            for(int i=0;i<3;i++){double av,bv;if(!double.TryParse(x[i],NumberStyles.Float,CultureInfo.InvariantCulture,out av)||!double.TryParse(y[i],NumberStyles.Float,CultureInfo.InvariantCulture,out bv)||double.IsNaN(av)||double.IsNaN(bv)||double.IsInfinity(av)||double.IsInfinity(bv))return false;squared+=(av-bv)*(av-bv);}
            return squared<=.05*.05;
        }
        private static bool Conflict(LedgerIdentity a, LedgerIdentity b)
        { return b.OwnerConsistent == false || (a != null && (Different(a.StructureEntityId,b.StructureEntityId) || Different(a.BlockGrid,b.BlockGrid) || Different(a.NativePosition,b.NativePosition)&&!AnchoredPositionDrift(a,b))); }
        private static LedgerEndpointState Merge(LedgerEndpointState old, LedgerObservation o)
        {
            var r = old ?? new LedgerEndpointState { Identity = new LedgerIdentity() };
            var a = r.Identity; var b = o.Identity;
            if (b.NpcEntityId.HasValue) { a.NpcEntityId=b.NpcEntityId; a.NpcRaw=b.NpcRaw; a.NpcSource=b.NpcSource; r.EvidenceUtc["NPC"]=o.Utc; }
            if (b.StructureEntityId.HasValue) { a.StructureEntityId=b.StructureEntityId; a.StructureSource=b.StructureSource; r.EvidenceUtc["Structure"]=o.Utc; }
            if (b.BlockGrid!=null) { a.BlockGrid=b.BlockGrid; a.BlockSource=b.BlockSource; r.EvidenceUtc["Block"]=o.Utc; }
            if (b.NativePosition!=null) { a.NativePosition=b.NativePosition; a.PositionSource=b.PositionSource; r.EvidenceUtc["NativePosition"]=o.Utc; }
            if (b.StructureNativePosition!=null && b.StructureEntityId>0 && b.OwnerConsistent==true) { a.StructureNativePosition=b.StructureNativePosition; a.StructurePositionSource=b.StructurePositionSource; r.EvidenceUtc["StructureNativePosition"]=o.Utc; }
            if (b.StructureName!=null) { a.StructureName=b.StructureName; a.StructureNameSource=b.StructureNameSource; r.EvidenceUtc["StructureName"]=o.Utc; }
            if (b.CatalogDisplayName!=null) { a.CatalogDisplayName=b.CatalogDisplayName; a.CatalogSource=b.CatalogSource; r.EvidenceUtc["CatalogName"]=o.Utc; }
            if (b.ChunkCoordinates!=null) { a.ChunkCoordinates=b.ChunkCoordinates; r.EvidenceUtc["ChunkCoordinates"]=o.Utc; }
            if (b.ChunkRelativeOffset!=null) { a.ChunkRelativeOffset=b.ChunkRelativeOffset; r.EvidenceUtc["ChunkRelativeOffset"]=o.Utc; }
            if (b.ChunkClrIdx.HasValue) { a.ChunkClrIdx=b.ChunkClrIdx; r.EvidenceUtc["ClrIdx"]=o.Utc; }
            if (b.OwnerClusterIdx.HasValue) { a.OwnerClusterIdx=b.OwnerClusterIdx; r.EvidenceUtc["ClusterIdx"]=o.Utc; }
            if (b.OwnerConsistent.HasValue) a.OwnerConsistent=b.OwnerConsistent;
            a.ProofState="latest-known-per-field;see-EvidenceUtc;not-current-interface-proof";
            return r;
        }
        public LedgerObservation ReadMarket(string key)
        { var b = Scalar("SELECT snapshot_json FROM markets WHERE endpoint_key=@p0", key) as byte[]; return b == null ? null : LedgerJson.Decode<LedgerObservation>(b); }
        public List<LedgerObservation> ReadDialogue(string key)
        {
            var list = new List<LedgerObservation>();
            using (var c=Command("SELECT capture_json FROM dialogue_pages WHERE endpoint_key=@p0 ORDER BY state_key,last_observed_utc,content_key",key))
            using (var reader=c.ExecuteReader()) while(reader.Read()) list.Add(LedgerJson.Decode<LedgerObservation>((byte[])reader[0]));
            return list;
        }
        private void Inject(string stage) { if (fault != null) fault(stage); }
        public string Save(LedgerObservation o)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.SqliteSave))
            {
            if(o!=null&&o.Kind=="place-observed")return SaveObject(o);
            if(o!=null&&o.Kind=="production-lifecycle")return SaveProductionRetirement(o);
            if(o!=null&&o.Kind=="production")return SaveProduction(o);
            if (o == null || o.Context == null || !o.Context.Recordable || o.Identity == null || !(o.Identity.NpcEntityId > 0)) return "identity-unknown";
            DateTime utc;
            if (!DateTime.TryParse(o.Utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out utc) || utc.Kind != DateTimeKind.Utc) throw new InvalidDataException("invalid-capture-UTC");
            if (o.Kind == "market" && (o.FullCurrentAssortment != true || o.Offers == null)) return "partial";
            if (o.Kind != "market" && (o.Kind != "dialogue-shown" || o.Dialogue == null)) throw new InvalidDataException("invalid-capture-kind");
            if (LedgerJson.Encode(o).Length > limits.RecordBytes) throw new InvalidDataException("capture-size-limit");
            string key = Key(o);
            try
            {
                using (transaction=connection.BeginTransaction())
                {
                    byte[] prior = Scalar("SELECT identity_json FROM endpoints WHERE endpoint_key=@p0",key) as byte[];
                    var known = prior == null ? null : LedgerJson.Decode<LedgerEndpointState>(prior);
                    if (Conflict(known == null ? null : known.Identity,o.Identity)) return "identity-conflict";
                    bool lastDialogueAdvanced = o.Kind == "dialogue-shown" && !string.IsNullOrEmpty(o.Dialogue.BodyVerbatim) &&
                        (known == null || known.LastDialogue == null || LedgerPresentation.CompareDialogue(o,known.LastDialogue)>0);
                    if(o.Kind=="dialogue-shown" && o.Request==null)o.Request=LedgerRequestRecognition.Recognize(o);
                    bool lastRequestAdvanced=o.Kind=="dialogue-shown" && o.Request!=null && (known==null || known.LastRequest==null || LedgerPresentation.CompareDialogue(o,known.LastRequest)>0);
                    string state = o.Dialogue == null ? null : string.IsNullOrEmpty(o.Dialogue.State) ? "<unknown-state>" : o.Dialogue.State;
                    object last = o.Kind == "market" ? Scalar("SELECT last_observed_utc FROM markets WHERE endpoint_key=@p0",key) : null;
                    if (last != null && utc < DateTime.Parse((string)last,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind)) return "stale";
                    if(o.Kind=="dialogue-shown")RequestInstance(o,key);
                    known = Merge(known,o);
                    if (lastDialogueAdvanced) known.LastDialogue = o;
                    if (lastRequestAdvanced) known.LastRequest = o;
                    Exec("INSERT OR REPLACE INTO endpoints(endpoint_key,world_key,playfield,npc_id,context_json,identity_json) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",key,o.Context.WorldKey,o.Context.Playfield,o.Identity.NpcEntityId.Value,LedgerJson.Encode(o.Context),LedgerJson.Encode(known));
                    if (o.Kind == "market")
                    {
                        var previousMarket=Scalar("SELECT snapshot_json FROM markets WHERE endpoint_key=@p0",key) as byte[];
                        if(previousMarket!=null){var previousObservation=LedgerJson.Decode<LedgerObservation>(previousMarket);foreach(var old in previousObservation.Offers.Where(LedgerOffer.Certified)){if(o.Offers.Any(x=>x.ItemId==old.ItemId&&x.AuxiliaryRaw==old.AuxiliaryRaw))continue;var remembered=LedgerJson.Decode<LedgerOffer>(LedgerJson.Encode(old));if(o.Offers.Count>=256)throw new InvalidDataException("retained-relation-limit;prior-market-preserved");remembered.RetainedRelation=true;remembered.NpcSellsAvailable=false;remembered.NpcBuysAvailable=false;remembered.LastEvidenceUtc=old.LastEvidenceUtc??previousObservation.Utc;remembered.UiRow=o.Offers.Count;if(old.Stock!=0){remembered.Stock=null;remembered.RemainingNpcBuyingCapacity=null;}o.Offers.Add(remembered);}}
                        // Child rows and snapshot change in one transaction. A failed delete/insert never publishes an empty market.
                        Exec("DELETE FROM offers WHERE endpoint_key=@p0",key); Inject("after-delete");
                        Exec("INSERT OR REPLACE INTO markets(endpoint_key,last_observed_utc,capture_id,snapshot_json) VALUES(@p0,@p1,@p2,@p3)",key,o.Utc,o.ObservationId,LedgerJson.Encode(o));
                        foreach (var offer in o.Offers)
                            Exec("INSERT INTO offers(endpoint_key,ui_row,item_id,stock,capacity,remaining_demand,sells_supported,buys_supported,stored_sell_unit,stored_buy_unit,offer_json) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10)",
                                key,offer.UiRow,offer.ItemId,offer.Stock,offer.Capacity,offer.RemainingNpcBuyingCapacity,offer.NpcSellsSupported,offer.NpcBuysSupported,offer.StoredSellUnit,offer.StoredBuyUnit,LedgerJson.Encode(offer));
                    }
                    else
                    {
                        string dialogueResult = SaveDialogue(o,key,state);
                        // A valid last fragment is useful even when full-page retention classifies it as a
                        // shared/short prefix. Commit that exact pointer atomically, without ranking text.
                        if (dialogueResult != "committed" && !lastDialogueAdvanced && !lastRequestAdvanced) return dialogueResult;
                    }
                    if (o.Identity.StructureEntityId.HasValue && o.Identity.StructureName != null)
                    {
                        object stationTime = Scalar("SELECT last_observed_utc FROM stations WHERE world_key=@p0 AND playfield=@p1 AND structure_id=@p2",o.Context.WorldKey,o.Context.Playfield,o.Identity.StructureEntityId.Value);
                        if (stationTime == null || utc >= DateTime.Parse((string)stationTime,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind))
                            Exec("INSERT OR REPLACE INTO stations(world_key,playfield,structure_id,name,source,last_observed_utc) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",o.Context.WorldKey,o.Context.Playfield,o.Identity.StructureEntityId.Value,o.Identity.StructureName,o.Identity.StructureNameSource,o.Utc);
                    }
                    Inject("before-commit"); transaction.Commit();
                }
                Inject("after-commit"); return "committed";
            }
            finally { transaction = null; }

            }
        }
        public void Dispose() { if (connection != null) { connection.Dispose(); connection = null; } }
    }
}
