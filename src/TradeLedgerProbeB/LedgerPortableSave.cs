using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mono.Data.Sqlite;

namespace TradeLedgerProbeB
{
    // Only private Ledger mirrors, never native save files. Reader SQL and atomic
    // replacement must not compete for Windows delete-sharing. No API thread reads.
    public static class LedgerPrivateMirror
    {
        private sealed class Entry { internal readonly object Gate=new object();internal int Users,Owners;internal object Stamp=new object(); }
        private sealed class Owner : IDisposable {private string path;internal Owner(string p){path=p;}public void Dispose(){lock(entries){if(path==null)return;var e=entries[path];e.Owners--;if(--e.Users==0)entries.Remove(path);path=null;}}}
        private sealed class Access : IDisposable
        {
            private readonly string path;private Entry entry;
            internal Access(string p,Entry e){path=p;entry=e;System.Threading.Monitor.Enter(e.Gate);}
            public void Dispose(){var e=entry;if(e==null)return;entry=null;System.Threading.Monitor.Exit(e.Gate);lock(entries){if(--e.Users==0)entries.Remove(path);}}
        }
        private static readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        // Lifetime metadata only: no held monitor, OS file handle, native save lease or disk IO.
        public static IDisposable Track(string path){string p=Path.GetFullPath(path);lock(entries){Entry e;if(!entries.TryGetValue(p,out e))entries.Add(p,e=new Entry());e.Users++;e.Owners++;return new Owner(p);}}
        public static object Version(string path){lock(entries){Entry e;return entries.TryGetValue(Path.GetFullPath(path),out e)&&e.Owners>0?e.Stamp:null;}}
        public static void Published(string path){lock(entries){Entry e;if(entries.TryGetValue(Path.GetFullPath(path),out e)&&e.Owners>0)e.Stamp=new object();}}
        public static IDisposable Acquire(string path)
        {
            string p=Path.GetFullPath(path);Entry e;
            lock(entries){if(!entries.TryGetValue(p,out e))entries.Add(p,e=new Entry());e.Users++;}
            return new Access(p,e);
        }
    }
    public sealed class LedgerPortableIdentity
    {
        public int Format=1;
        public string Campaign,WorldKey,Branch,Snapshot,ParentSnapshot,Utc,Origin,AliasesJson,LegacyDigest,NativeHeaderEvidence;
        public long Revision;
        public string CampaignName,Scenario,BookmarkSeedJson;public int? NativeSeedField;public uint? NativeHeaderVersion;
    }
    public sealed class LedgerPortableLineage {public int Format=1;public string Campaign,WorldKey;}
    public sealed class LedgerPublicationPausedException : IOException {public LedgerPublicationPausedException():base("native-save-menu;publication-deferred") { }}
    // Native HideServer closes InGameMenu before its synchronous exclusive-file preflight
    // and recursive copy. A close EVENT must not resume publication inside that native call.
    public sealed class LedgerSaveMenuSchedule
    {
        public bool Suspended {get;private set;}
        private bool resumeOnUpdate;
        public bool Window(int type,object name)
        {
            if(!(name is string)||(string)name!="InGameMenu"||(type!=10&&type!=11))return false;
            Suspended=true;resumeOnUpdate=type==11;return true;
        }
        public bool Update(){if(!resumeOnUpdate)return false;resumeOnUpdate=false;Suspended=false;return true;}
    }
    // A published DB is immutable. Working/journal/temp files live OUTSIDE the save-copy tree,
    // on the same volume. Only an atomic complete replacement changes authoritative save state.
    public sealed class LedgerPortableSave : IDisposable
    {
        public readonly string SavePath,Root,DatabasePath,WorkRoot,ViewDatabasePath,LeasePath;
        public LedgerPortableIdentity Identity {get;private set;}
        private FileStream lease;
        private IDisposable mirrorTracking;
        private readonly object publicationGate=new object();
        private readonly object publicationWorkGate=new object(); // worker-only preparation/mirror serialization; never acquired by native-menu API callback
        private volatile bool nativeSaveSuspended;
        public bool PublicationAllowed {get{return !nativeSaveSuspended;}}
        public void SuspendForNativeSave(){nativeSaveSuspended=true;var profileWait=LedgerProfiler.Measure(LedgerPerfPath.PublicationGateWait);lock(publicationGate) {profileWait.Dispose(); /* drain prior publication; no save-tree handle remains */ }}
        public void ResumeAfterNativeSave(){nativeSaveSuspended=false;}
        private readonly string branch=Guid.NewGuid().ToString("N");
        private readonly Action<string> fault;
        private bool disposed,dirty,viewPending;
        private string unversionedLocal;
        private const string Table="ledger_portable_v1";
        private LedgerPortableSave(string save,Action<string> inject)
        {
            SavePath=Path.GetFullPath(save).TrimEnd('\\','/');fault=inject;
            if(!Directory.Exists(SavePath)||!File.Exists(Path.Combine(SavePath,"game.dat")))throw new InvalidDataException("save-folder-or-game.dat-unavailable;portable-storage-paused");
            NoLinks(SavePath);
            Root=Path.Combine(SavePath,"Mods","TradeLedger");DatabasePath=Path.Combine(Root,LedgerDatabase.FileName);
            string parent=Path.GetDirectoryName(SavePath),ancestor=Path.GetDirectoryName(parent);
            WorkRoot=Path.Combine(ancestor??parent,"TradeLedgerCache",Guid.NewGuid().ToString("N"));
            ViewDatabasePath=Path.Combine(WorkRoot,"LedgerView.sqlite");
            LeasePath=Path.Combine(ancestor??parent,"TradeLedgerCache","WriterLocks",LedgerJson.Hash(SavePath.ToUpperInvariant())+".lock");
            NoLinks(Root);NoLinks(WorkRoot);Directory.CreateDirectory(Root);
            // Native backup first opens EVERY save file with FileShare.None (06007D15),
            // even though File.Copy itself accepts the former shared-read lease. Keep OS
            // ownership OUTSIDE the copied tree, keyed by physical path, not campaign.
            NoLinks(LeasePath);Directory.CreateDirectory(Path.GetDirectoryName(LeasePath));
            lease=new FileStream(LeasePath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            try{Directory.CreateDirectory(WorkRoot);mirrorTracking=LedgerPrivateMirror.Track(ViewDatabasePath);}catch{lease.Dispose();lease=null;throw;}
        }
        public static LedgerPortableSave Open(string save,string legacyRoot,Action<string> inject=null)
        {
            LedgerSqliteRuntime.Prepare();LedgerPortableSave p=null;
            try {p=new LedgerPortableSave(save,inject);p.Initialize(legacyRoot);if(!File.Exists(p.ViewDatabasePath))File.Copy(Path.Combine(p.WorkRoot,LedgerDatabase.FileName),p.ViewDatabasePath);LedgerPrivateMirror.Published(p.ViewDatabasePath);return p;}
            catch {if(p!=null)p.Dispose();throw;}
        }
        public static void NoLinks(string path)
        {
            for(string p=Path.GetFullPath(path);!string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))
                if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("portable-reparse-path-refused");
        }
        internal static SqliteConnection Connect(string file,bool readOnly)
        {
            NoLinks(file);
            // Provider Open defaults to PRAGMA journal_mode=DELETE even for a read-only
            // connection. Its inspected "Default" escape suppresses that pragma and leaves
            // a live legacy WAL source untouched (the builder enum omits this valid value).
            var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Version=3,ReadOnly=readOnly,Pooling=false,DefaultTimeout=1}.ToString()+";Journal Mode=Default");
            c.Open();return c;
        }
        internal static object Scalar(SqliteConnection c,string sql,params object[] args)
        {using(var q=c.CreateCommand()){q.CommandText=sql;for(int i=0;i<args.Length;i++)q.Parameters.AddWithValue("@p"+i,args[i]??DBNull.Value);return q.ExecuteScalar();}}
        internal static void Exec(SqliteConnection c,string sql,params object[] args)
        {using(var q=c.CreateCommand()){q.CommandText=sql;for(int i=0;i<args.Length;i++)q.Parameters.AddWithValue("@p"+i,args[i]??DBNull.Value);q.ExecuteNonQuery();}}
        public static void Clone(string source,string target)
        {
            if(new FileInfo(source).Length>512L*1024*1024)throw new InvalidDataException("portable-size-limit;source-preserved");
            if(File.Exists(target))throw new IOException("snapshot-target-already-exists");
            using(var c=Connect(source,true))
            {
                if(Convert.ToInt32(Scalar(c,"PRAGMA application_id"),CultureInfo.InvariantCulture)!=1414288177)throw new InvalidDataException("not-owned-ledger-database");
                if((string)Scalar(c,"PRAGMA quick_check(1)")!="ok")throw new InvalidDataException("source-ledger-integrity;preserved");
                // SQLite 3.39.3 supplied by the guarded client: consistent committed read including
                // WAL, no checkpoint or mutation of source. Query_only forbids VACUUM INTO, so
                // source is instead opened SQLITE_OPEN_READONLY at the provider level.
                Exec(c,"VACUUM INTO @p0",target);
            }
            using(var c=Connect(target,true))if((string)Scalar(c,"PRAGMA integrity_check")!="ok")throw new InvalidDataException("snapshot-integrity");
        }
        public static LedgerPortableIdentity ReadIdentity(string file)
        {
            if(!File.Exists(file))return null;
            if(new FileInfo(file).Length>512L*1024*1024)throw new InvalidDataException("portable-size-limit;source-preserved");
            using(var c=Connect(file,true))
            {
                if(Convert.ToInt32(Scalar(c,"PRAGMA application_id"),CultureInfo.InvariantCulture)!=1414288177||(string)Scalar(c,"PRAGMA quick_check(1)")!="ok")throw new InvalidDataException("portable-database-invalid;no-legacy-fallback");
                if(Scalar(c,"SELECT name FROM sqlite_master WHERE type='table' AND name=@p0",Table)==null)return null;
                var b=Scalar(c,"SELECT identity_json FROM "+Table+" WHERE id=1") as byte[];
                if(b!=null&&b.Length>1048576)throw new InvalidDataException("portable-identity-bound");
                var id=b==null?null:LedgerJson.Decode<LedgerPortableIdentity>(b);
                if(id==null||id.Format!=1||string.IsNullOrEmpty(id.Campaign)||string.IsNullOrEmpty(id.WorldKey)||string.IsNullOrEmpty(id.Snapshot)||string.IsNullOrEmpty(id.Branch)||id.Revision<1)throw new InvalidDataException("portable-identity-invalid;no-legacy-fallback");
                ValidateWorld(c,id.WorldKey);
                return id;
            }
        }
        private static void ValidateWorld(SqliteConnection c,string world)
        {
            foreach(string table in new[]{"endpoints","stations","trades_v1","request_completion_v1","request_tracking_v1"})
                if(Scalar(c,"SELECT name FROM sqlite_master WHERE type='table' AND name=@p0",table)!=null&&Convert.ToInt64(Scalar(c,"SELECT count(*) FROM "+table+" WHERE world_key<>@p0",world),CultureInfo.InvariantCulture)>0)
                    throw new InvalidDataException("portable-independent-scope-in-"+table+";sources-preserved");
        }
        private void Initialize(string legacyRoot)
        {
            string work=Path.Combine(WorkRoot,LedgerDatabase.FileName);
            if(File.Exists(DatabasePath))
            {
                // Incomplete hot/WAL copies of a supposedly immutable authority must not be
                // replayed or silently repaired into a different state.
                foreach(string suffix in new[]{"-wal","-shm","-journal"})if(File.Exists(DatabasePath+suffix))throw new InvalidDataException("incomplete-portable-copy-sidecar;restore-complete-save-copy");
                Identity=ReadIdentity(DatabasePath);
                if(Identity!=null){ValidateNativeHeader();EnsureLineage();Clone(DatabasePath,work);RestoreBookmarkSeed();return;}
                unversionedLocal=DatabasePath;
            }
            // A directory without its authoritative file is not a new empty campaign if another
            // payload hints at a partial copy. Never resurrect later external observations.
            if(unversionedLocal==null&&Directory.GetFiles(Root).Any(p=>Path.GetFileName(p)!="Writer.lock"))throw new InvalidDataException("incomplete-portable-copy;missing-authority;restore-complete-save");
            var context=LedgerContext.Create(SavePath,"<metadata-only>",null,"SinglePlayer");
            var aliases=new LedgerSaveAliases();aliases.Apply(context,legacyRoot);
            if(!context.Recordable)throw new InvalidDataException(context.ScopeState);
            string legacy=Path.Combine(legacyRoot,LedgerDatabase.FileName),aliasesFile=Path.Combine(legacyRoot,"SaveAliases.json");
            string aliasesJson=File.Exists(aliasesFile)?System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(aliasesFile)):null;
            long own=0,all=0;
            if(File.Exists(legacy))using(var c=Connect(legacy,true))
            {
                own=Convert.ToInt64(Scalar(c,"SELECT count(*) FROM endpoints WHERE world_key=@p0",context.WorldKey),CultureInfo.InvariantCulture);
                all=Convert.ToInt64(Scalar(c,"SELECT count(*) FROM endpoints"),CultureInfo.InvariantCulture);
            }
            bool registered=context.ScopeState=="explicit-verified-save-alias;memory-not-rollback-snapshot";
            if(own>0&&!registered)throw new InvalidDataException("legacy-path-records-without-lineage-proof;reviewed-migration-required;external-data-preserved");
            if(own==0&&all>0&&PossibleLegacyCopy(SavePath,aliasesFile))throw new InvalidDataException("possible-unmarked-legacy-copy;no-historical-snapshot;reviewed-recovery-required");
            Identity=new LedgerPortableIdentity{Campaign=registered?LedgerJson.Hash("verified-legacy-campaign-v1|"+context.WorldKey):Guid.NewGuid().ToString("N"),
                WorldKey=registered?context.WorldKey:LedgerJson.Hash("portable-campaign-v1|"+Guid.NewGuid().ToString("N")),
                Origin=own>0?"legacy-recovery-at-migration;NOT-exact-historical-save-snapshot":"new-isolated-lineage;no-prior-ledger-evidence;no-name-or-seed-link",
                AliasesJson=aliasesJson,NativeHeaderEvidence=LedgerJson.Hash(File.ReadAllBytes(Path.Combine(SavePath,"game.dat")))};
            if(unversionedLocal!=null&&!registered)throw new InvalidDataException("unversioned-save-local-ledger-without-reviewed-lineage;both-sources-preserved");
            if(unversionedLocal!=null)
            {
                Clone(unversionedLocal,work);
                using(var c=Connect(work,true))ValidateWorld(c,context.WorldKey);
            }
            if(own>0)
            {
                string sourceCopy=unversionedLocal==null?work:Path.Combine(WorkRoot,"legacy-import.sqlite");
                Clone(legacy,sourceCopy);Identity.LegacyDigest=LedgerJson.Hash(File.ReadAllBytes(sourceCopy));
                using(var c=Connect(sourceCopy,false))
                {
                    // Partition an owned consistent clone; original legacy database/aliases remain.
                    Exec(c,"PRAGMA foreign_keys=OFF; BEGIN IMMEDIATE");
                    foreach(string table in new[]{"offers","markets","dialogue_pages"})Exec(c,"DELETE FROM "+table+" WHERE endpoint_key NOT IN (SELECT endpoint_key FROM endpoints WHERE world_key=@p0)",context.WorldKey);
                    foreach(string table in new[]{"stations","trades_v1","request_completion_v1","request_tracking_v1"})
                        if(Scalar(c,"SELECT name FROM sqlite_master WHERE type='table' AND name=@p0",table)!=null)Exec(c,"DELETE FROM "+table+" WHERE world_key<>@p0",context.WorldKey);
                    Exec(c,"DELETE FROM endpoints WHERE world_key<>@p0; COMMIT",context.WorldKey);
                }
            }
            Identity.CampaignName=Path.GetFileName(SavePath);ValidateNativeHeader();
            using(var db=new LedgerDatabase(WorkRoot)) { }
            if(unversionedLocal!=null&&own>0)MergeLegacy(Path.Combine(WorkRoot,"legacy-import.sqlite"),WorkRoot,context.WorldKey);
            string bookmarks=Path.Combine(legacyRoot,"LedgerBookmarks.json");
            if(registered&&File.Exists(bookmarks))
            {
                if(new FileInfo(bookmarks).Length>1024*1024)throw new InvalidDataException("legacy-bookmark-bound");
                var doc=LedgerJson.Decode<LedgerBookmarkOwnership.Document>(File.ReadAllBytes(bookmarks));
                doc.Records=doc.Records.Where(v=>v.Plan!=null&&v.Plan.World==Identity.WorldKey).ToList();
                Identity.BookmarkSeedJson=System.Text.Encoding.UTF8.GetString(LedgerJson.Encode(doc));
            }
            EnsureLineage();Publish();RestoreBookmarkSeed();
        }
        private void ValidateNativeHeader()
        {
            string path=Path.Combine(SavePath,"game.dat");if(new FileInfo(path).Length>65536)throw new InvalidDataException("native-header-bound");
            using(var reader=new BinaryReader(File.OpenRead(path)))
            {
                if(reader.ReadUInt32()!=2022986310)throw new InvalidDataException("native-save-header-mismatch;no-lineage-inference");
                uint format=reader.ReadUInt32();reader.ReadInt32();reader.ReadInt32();if(format>=19)reader.ReadByte();
                string scenario=format>7?reader.ReadString():"";int seed=reader.ReadInt32();
                if(Identity.NativeSeedField.HasValue&&(Identity.NativeSeedField.Value!=seed||Identity.Scenario!=scenario))throw new InvalidDataException("portable-save-header-conflict;do-not-merge-campaigns");
                Identity.NativeHeaderVersion=format;Identity.Scenario=scenario;Identity.NativeSeedField=seed;
            }
        }
        private void EnsureLineage()
        {
            string path=Path.Combine(Root,"Lineage.json");NoLinks(path);
            if(File.Exists(path))
            {
                if(new FileInfo(path).Length>4096)throw new InvalidDataException("lineage-witness-bound");
                var v=LedgerJson.Decode<LedgerPortableLineage>(File.ReadAllBytes(path));
                if(v==null||v.Format!=1||v.Campaign!=Identity.Campaign||v.WorldKey!=Identity.WorldKey)throw new InvalidDataException("lineage-witness-conflict;no-fallback");
                return;
            }
            string temp=Path.Combine(WorkRoot,"lineage.pending");
            using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {byte[] bytes=LedgerJson.Encode(new LedgerPortableLineage{Campaign=Identity.Campaign,WorldKey=Identity.WorldKey});f.Write(bytes,0,bytes.Length);f.Flush(true);}
            File.Move(temp,path);
        }
        private void RestoreBookmarkSeed()
        {
            string owned=Path.Combine(Root,"LedgerBookmarks.json");
            if(Identity.BookmarkSeedJson==null||File.Exists(owned))return;
            string temp=Path.Combine(WorkRoot,"bookmarks-migration.json");
            File.WriteAllText(temp,Identity.BookmarkSeedJson,new System.Text.UTF8Encoding(false));File.Move(temp,owned);
        }
        // Existing target and legacy source are both retained as immutable migration evidence.
        // Only already-proven, same-world records can participate. Existing Ledger save methods
        // enforce identity conflicts, exact page dedup and complete-market timestamp semantics.
        public static void MergeLegacy(string source,string targetRoot,string world)
        {
            string target=Path.Combine(targetRoot,LedgerDatabase.FileName);
            byte[] old=File.ReadAllBytes(target),import=File.ReadAllBytes(source);
            using(var c=Connect(target,false))
            {
                ValidateWorld(c,world);
                if(Scalar(c,"SELECT name FROM sqlite_master WHERE name='ledger_migration_sources_v1'")!=null&&Scalar(c,"SELECT digest FROM ledger_migration_sources_v1 WHERE digest=@p0 AND origin='legacy-import'",LedgerJson.Hash(import))!=null)return;
                Exec(c,"CREATE TABLE IF NOT EXISTS ledger_migration_sources_v1(digest TEXT PRIMARY KEY,world_key TEXT NOT NULL,origin TEXT NOT NULL,database_blob BLOB NOT NULL)");
                Exec(c,"INSERT OR IGNORE INTO ledger_migration_sources_v1 VALUES(@p0,@p1,'target-before-merge',@p2)",LedgerJson.Hash(old),world,old);
            }
            using(var incoming=Connect(source,true))using(var targetDb=new LedgerDatabase(targetRoot))
            {
                foreach(string query in new[]{"SELECT snapshot_json FROM markets JOIN endpoints USING(endpoint_key) WHERE world_key=@p0", "SELECT capture_json FROM dialogue_pages JOIN endpoints USING(endpoint_key) WHERE world_key=@p0"})
                using(var command=incoming.CreateCommand())
                {
                    command.CommandText=query;command.Parameters.AddWithValue("@p0",world);
                    using(var rows=command.ExecuteReader())while(rows.Read())
                    {
                        var o=LedgerJson.Decode<LedgerObservation>((byte[])rows[0]);string result=targetDb.Save(o);
                        if(result!="committed"&&result!="stale"&&result!="duplicate"&&result!="prefix"&&result!="partial")
                            throw new InvalidDataException("migration-observation-conflict-"+result+";originals-preserved");
                    }
                }
                if(Scalar(incoming,"SELECT name FROM sqlite_master WHERE name='trades_v1'")!=null)
                using(var command=incoming.CreateCommand())
                {
                    command.CommandText="SELECT receipt_json FROM trades_v1 WHERE world_key=@p0 ORDER BY sequence";command.Parameters.AddWithValue("@p0",world);
                    using(var rows=command.ExecuteReader())while(rows.Read())
                    {
                        byte[] bytes=(byte[])rows[0];var receipt=LedgerJson.Decode<LedgerTradeReceipt>(bytes);
                        byte[] existing=targetDb.Scalar("SELECT receipt_json FROM trades_v1 WHERE trade_id=@p0",receipt.TradeId) as byte[];
                        if(existing!=null)
                        {
                            if(LedgerJson.Hash(LedgerJson.Encode(LedgerJson.Decode<LedgerTradeReceipt>(existing)))!=LedgerJson.Hash(LedgerJson.Encode(receipt)))throw new InvalidDataException("migration-trade-id-conflict;both-sources-preserved");
                        }
                        else targetDb.SaveTrade(receipt);
                    }
                }
            }
            using(var c=Connect(target,false))
            {
                Exec(c,"ATTACH DATABASE @p0 AS incoming",source);
                using(var tx=c.BeginTransaction())
                {
                    foreach(string table in new[]{"stations","request_completion_v1","request_tracking_v1"})
                    {
                        string schema=Convert.ToString(Scalar(c,"SELECT sql FROM incoming.sqlite_master WHERE type='table' AND name=@p0",table),CultureInfo.InvariantCulture);
                        if(string.IsNullOrEmpty(schema))continue;
                        if(Scalar(c,"SELECT name FROM main.sqlite_master WHERE name=@p0",table)==null)Exec(c,schema);
                        // The target's existing explicit annotations win exact-key conflicts; both
                        // pre-merge databases remain recoverable in the migration evidence table.
                        Exec(c,"INSERT OR IGNORE INTO main."+table+" SELECT * FROM incoming."+table+" WHERE world_key=@p0",world);
                    }
                    tx.Commit();
                }
                // Only a completed merge is idempotently acknowledged. A failed merge is never
                // published; the prior authority and this exact source remain recoverable.
                Exec(c,"INSERT OR IGNORE INTO ledger_migration_sources_v1 VALUES(@p0,@p1,'legacy-import',@p2)",LedgerJson.Hash(import),world,import);
            }
        }
        private static bool PossibleLegacyCopy(string save,string aliasesFile)
        {
            if(!File.Exists(aliasesFile))return false;
            if(new FileInfo(aliasesFile).Length>65536)throw new InvalidDataException("legacy-alias-file-bound");
            var aliases=LedgerJson.Decode<LedgerSaveAlias[]>(File.ReadAllBytes(aliasesFile));
            return aliases.Any(a=>a.Guards!=null&&a.Guards.Length>=2&&a.Guards.All(g=>
            {
                if(Path.IsPathRooted(g.RelativePath))return false;string p=Path.GetFullPath(Path.Combine(save,g.RelativePath));
                return p.StartsWith(save+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.Exists(p)&&new FileInfo(p).Length<=2097152&&LedgerJson.Hash(File.ReadAllBytes(p))==g.Sha256;
            }));
        }
        public void MarkDirty(){dirty=true;}
        internal void MarkUnchanged(){if(viewPending)throw new InvalidOperationException("unpublished mirror cannot be cleared as unchanged");dirty=false;}
        public void Publish()
        { lock(publicationWorkGate)PublishCore(); }
        public void PublishOwnedFile(Action action)
        {
            var profileWait=LedgerProfiler.Measure(LedgerPerfPath.PublicationGateWait);
            lock(publicationGate)
            {
                profileWait.Dispose();
                if(nativeSaveSuspended)throw new LedgerPublicationPausedException();
                if(disposed)throw new ObjectDisposedException("portable-session");
                action();
            }
        }
        private void PublishCore()
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.Publication))
            {
            if(disposed)throw new ObjectDisposedException("portable-session");
            if(nativeSaveSuspended)throw new LedgerPublicationPausedException();
            // Authority may already have advanced when a private mirror replacement failed.
            // Retry that exact mirror, not another authority revision for the same queue head.
            if(viewPending){AdvanceView();return;}
            string next=Path.Combine(WorkRoot,"publish-"+Guid.NewGuid().ToString("N")+".sqlite");
            string viewNext=Path.Combine(WorkRoot,"view.pending");
            var id=LedgerJson.Decode<LedgerPortableIdentity>(LedgerJson.Encode(Identity));
            id.Branch=branch;id.ParentSnapshot=id.Snapshot;id.Snapshot=Guid.NewGuid().ToString("N");id.Revision++;id.Utc=DateTime.UtcNow.ToString("o");
            try
            {
                Clone(Path.Combine(WorkRoot,LedgerDatabase.FileName),next);
                using(var c=Connect(next,false))
                {
                    Exec(c,"PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS "+Table+"(id INTEGER PRIMARY KEY CHECK(id=1),identity_json BLOB NOT NULL)");
                    Exec(c,"INSERT OR REPLACE INTO "+Table+"(id,identity_json) VALUES(1,@p0)",LedgerJson.Encode(id));
                }
                using(var f=new FileStream(next,FileMode.Open,FileAccess.ReadWrite,FileShare.Read))f.Flush(true);
                File.Copy(next,viewNext,true);
                if(fault!=null)fault("publication-prepared");
                // Only the save-tree authority check/swap belongs in the native-backup barrier.
                // SQLite clone/fsync and a potentially blocked private reader are outside it.
                PublishOwnedFile(()=>{
                    if(fault!=null)fault("before-publish");
                    var prior=ReadIdentity(DatabasePath);
                    if(Identity.Revision>0&&(prior==null||prior.Campaign!=Identity.Campaign||prior.Snapshot!=Identity.Snapshot))
                        throw new InvalidDataException("portable-authority-changed-during-session;reload-save;unpublished-work-preserved");
                    if(File.Exists(DatabasePath))File.Replace(next,DatabasePath,Path.Combine(Root,"TradeLedger.previous.sqlite"));else File.Move(next,DatabasePath);
                    Identity=id;viewPending=true;
                });
                AdvanceView();
            }
            finally {if(File.Exists(next))File.Delete(next);if(!viewPending&&File.Exists(viewNext))File.Delete(viewNext);}

            }
        }
        private void AdvanceView()
        {
            using(LedgerPrivateMirror.Acquire(ViewDatabasePath))
            {
                if(fault!=null)fault("before-view-replace");
                string pending=Path.Combine(WorkRoot,"view.pending");
                try{if(File.Exists(ViewDatabasePath))File.Replace(pending,ViewDatabasePath,null);else File.Move(pending,ViewDatabasePath);}
                catch(IOException e){throw new IOException("private-view-publication: "+ViewDatabasePath+"; authority retained; retrying same generation",e);}
                LedgerPrivateMirror.Published(ViewDatabasePath);viewPending=false;dirty=false;
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;if(mirrorTracking!=null){mirrorTracking.Dispose();mirrorTracking=null;}if(lease!=null){lease.Dispose();lease=null;}
            // Only our uniquely created session cache is cleaned, after its SQLite connections
            // close. Unpublished committed work remains recoverable and is reported by Root.
            if(dirty||!Directory.Exists(WorkRoot))return;
            try
            {
                NoLinks(WorkRoot);
                if(Path.GetFileName(Path.GetDirectoryName(WorkRoot))!="TradeLedgerCache"||Path.GetFileName(WorkRoot).Length!=32||Directory.GetDirectories(WorkRoot).Length!=0)return;
                var files=Directory.GetFiles(WorkRoot);
                if(files.Any(p=>!new[]{LedgerDatabase.FileName,LedgerDatabase.FileName+"-journal",LedgerDatabase.FileName+"-wal",LedgerDatabase.FileName+"-shm","LedgerView.sqlite","view.pending","legacy-import.sqlite","lineage.pending","bookmarks.pending","bookmarks-migration.json"}.Contains(Path.GetFileName(p))&&!Path.GetFileName(p).StartsWith("publish-",StringComparison.Ordinal)))return;
                foreach(string p in files){NoLinks(p);File.Delete(p);}Directory.Delete(WorkRoot);
            }
            catch(Exception){/* Owned cache can remain; authority is independent of it. */}
        }
    }
}
