using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TradeLedgerProbeB
{
    public sealed class LedgerDialogueEntry
    {
        public string State, ContentKey, LastObservedUtc, LastEncounteredUtc, EncounterKind;
        public LedgerObservation Capture, Encounter;
    }
    public sealed partial class LedgerDatabase
    {
        private static bool DifferentRequestVisit(LedgerObservation a,LedgerObservation b)
        {
            // Page/state/options may advance before the old widget text is replaced. They
            // are not occurrence IDs. Missing visit metadata cannot prove another visit.
            return a!=null&&!string.IsNullOrEmpty(a.Session)&&!string.IsNullOrEmpty(b.Session)&&
                (a.Session!=b.Session||!string.IsNullOrEmpty(a.Interaction)&&!string.IsNullOrEmpty(b.Interaction)&&a.Interaction!=b.Interaction);
        }
        private void RequestInstance(LedgerObservation o,string endpoint)
        {
            if(o.ObserverPlayer<=0||o.Request==null||o.Dialogue==null||o.Dialogue.BodyFullyRevealed!=true)return;
            Exec("CREATE TABLE IF NOT EXISTS request_instances_v1(request_key TEXT PRIMARY KEY,endpoint_key TEXT NOT NULL,player_id INTEGER NOT NULL,capture_json BLOB NOT NULL,seen_utc TEXT NOT NULL,body_hash TEXT NOT NULL)");
            string body=o.Dialogue.BodyVerbatim,legacy=LedgerManualCompletion.KeyFor(endpoint,body);
            var bytes=Scalar("SELECT capture_json FROM request_instances_v1 WHERE endpoint_key=@p0 AND player_id=@p1 AND body_hash=@p2 ORDER BY seen_utc DESC LIMIT 1",endpoint,o.ObserverPlayer,LedgerJson.Hash(body)) as byte[];
            var previous=bytes==null?null:LedgerJson.Decode<LedgerObservation>(bytes);
            if(previous!=null&&previous.Dialogue.BodyVerbatim!=body)throw new InvalidDataException("request-body-hash-collision");
            if(previous!=null&&string.CompareOrdinal(o.Utc,previous.Utc)<0)return;
            string key=previous==null?legacy:LedgerManualCompletion.Key(previous);
            bool hasTable=Convert.ToInt64(Scalar("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='request_completion_v1'"))>0;
            object done=hasTable?Scalar("SELECT completed FROM request_completion_v1 WHERE world_key=@p0 AND player_id=@p1 AND request_key=@p2",o.Context.WorldKey,o.ObserverPlayer,key):null;
            if(previous==null){var old=Scalar("SELECT capture_json FROM dialogue_pages WHERE endpoint_key=@p0 AND content_key=@p1",endpoint,LedgerJson.Hash(body)) as byte[];if(old!=null)previous=LedgerJson.Decode<LedgerObservation>(old);}
            bool newVisit=DifferentRequestVisit(previous,o);
            if(done!=null&&Convert.ToInt32(done)==1&&previous!=null&&newVisit&&string.CompareOrdinal(o.Utc,previous.Utc)>0)
            {
                // Completion does not propagate to a later observed occurrence of the same formulated request.
                Exec("INSERT OR IGNORE INTO request_instances_v1 VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",key,endpoint,o.ObserverPlayer,LedgerJson.Encode(previous),previous.Utc,LedgerJson.Hash(body));
                o.RequestInstance=Guid.NewGuid().ToString("N");
            }
            else if(previous!=null)o.RequestInstance=previous.RequestInstance;
            if(Convert.ToInt64(Scalar("SELECT count(*) FROM request_instances_v1"))>=8192)throw new InvalidDataException("request-instance-limit;history-preserved");
            // Keep the assignment origin throughout this visit so a queued hand-in receipt
            // still binds its shown page. SaveDialogue separately retains distinct raw dialogue bodies.
            Exec("INSERT OR REPLACE INTO request_instances_v1 VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",LedgerManualCompletion.Key(o),endpoint,o.ObserverPlayer,LedgerJson.Encode(previous!=null&&!newVisit&&previous.RequestInstance==o.RequestInstance?previous:o),o.Utc,LedgerJson.Hash(body));
        }
        public long DialogueMigrated { get; private set; }
        public long DialogueDuplicatesCollapsed { get; private set; }
        private static string DialogueState(LedgerObservation o)
        { return string.IsNullOrEmpty(o.Dialogue.State) ? "<unknown-state>" : o.Dialogue.State; }
        private static DateTime Stamp(string value)
        {
            DateTime t;
            if (!DateTime.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out t) || t.Kind != DateTimeKind.Utc)
                throw new InvalidDataException("invalid-dialogue-UTC");
            return t;
        }
        private static bool SameLease(LedgerObservation a, LedgerObservation b)
        {
            return !string.IsNullOrEmpty(a.Session) && !string.IsNullOrEmpty(a.Interaction) &&
                a.Session == b.Session && a.Interaction == b.Interaction && a.Dialogue.Page == b.Dialogue.Page && DialogueState(a)==DialogueState(b);
        }
        private static string Body(LedgerObservation o) { return o.Dialogue.BodyVerbatim ?? ""; }
        private void PutDialogue(string endpoint, LedgerDialogueEntry e)
        {
            if (e.ContentKey != LedgerJson.Hash(Body(e.Capture))) throw new InvalidDataException("dialogue-content-key-mismatch");
            Exec("INSERT OR REPLACE INTO dialogue_pages(endpoint_key,state_key,content_key,last_observed_utc,last_encountered_utc,capture_json,encounter_json,encounter_kind) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                endpoint,e.State,e.ContentKey,e.LastObservedUtc,e.LastEncounteredUtc,LedgerJson.Encode(e.Capture),e.Encounter == null ? null : LedgerJson.Encode(e.Encounter),e.EncounterKind);
        }
        public List<LedgerDialogueEntry> ReadDialogueEntries(string endpoint, string state = null)
        {
            var result=new List<LedgerDialogueEntry>();
            using(var command=state == null ? Command("SELECT state_key,content_key,last_observed_utc,last_encountered_utc,capture_json,encounter_json,encounter_kind FROM dialogue_pages WHERE endpoint_key=@p0 ORDER BY state_key,last_observed_utc,content_key",endpoint) :
                Command("SELECT state_key,content_key,last_observed_utc,last_encountered_utc,capture_json,encounter_json,encounter_kind FROM dialogue_pages WHERE endpoint_key=@p0 AND state_key=@p1 ORDER BY last_observed_utc,content_key",endpoint,state))
            using(var reader=command.ExecuteReader())
                while(reader.Read())
                {
                    if(result.Count >= limits.DialoguePagesPerEndpoint) throw new InvalidDataException("dialogue-content-limit;prior-data-preserved");
                    result.Add(new LedgerDialogueEntry {State=(string)reader[0],ContentKey=(string)reader[1],LastObservedUtc=(string)reader[2],LastEncounteredUtc=(string)reader[3],
                        Capture=LedgerJson.Decode<LedgerObservation>((byte[])reader[4]),Encounter=reader.IsDBNull(5) ? null : LedgerJson.Decode<LedgerObservation>((byte[])reader[5]),EncounterKind=(string)reader[6]});
                }
            return result;
        }
        private string SaveDialogue(LedgerObservation o, string endpoint, string state)
        {
            string body=Body(o);
            if(body.Length==0) return "partial"; // Empty widgets carry no new heard content and cannot erase memory.
            var entries=ReadDialogueEntries(endpoint);
            string content=LedgerJson.Hash(body);
            var exact=entries.Find(e=>e.ContentKey==content);
            if(exact!=null && Body(exact.Capture)!=body) throw new InvalidDataException("dialogue-hash-collision;not-merged");
            if(exact!=null && Stamp(o.Utc)<Stamp(exact.LastObservedUtc)) return "stale";
            var grown=new List<LedgerDialogueEntry>();
            foreach(var e in entries)
                if(Body(e.Capture).Length < body.Length && body.StartsWith(Body(e.Capture),StringComparison.Ordinal) && SameLease(e.Capture,o))
                    grown.Add(e); // Only witnessed growth in the same transient widget page; never an ordinal join across visits.
            if(exact==null)
            {
                var longer=entries.FindAll(e=>e.State==state && Body(e.Capture).Length>body.Length && Body(e.Capture).StartsWith(body,StringComparison.Ordinal));
                if(longer.Count!=0)
                {
                    // The prefix adds no new verbatim content. It does NOT prove that a whole prior request was shown again.
                    if(longer.Count==1 && Stamp(o.Utc)>=Stamp(longer[0].LastEncounteredUtc))
                    {
                        var e=longer[0];e.LastEncounteredUtc=o.Utc;e.Encounter=o;e.EncounterKind="exact-prefix-only;full-text-not-reobserved";PutDialogue(endpoint,e);
                        return "committed";
                    }
                    return "partial"; // Ambiguous shared prefix: retain existing distinct pages; raw sample remains in diagnostics.
                }
            }
            long count=Convert.ToInt64(Scalar("SELECT count(*) FROM dialogue_pages WHERE endpoint_key=@p0",endpoint),CultureInfo.InvariantCulture);
            if(exact==null && count-grown.Count >= limits.DialoguePagesPerEndpoint) throw new InvalidDataException("dialogue-content-limit;prior-data-preserved");
            foreach(var e in grown) Exec("DELETE FROM dialogue_pages WHERE endpoint_key=@p0 AND state_key=@p1 AND content_key=@p2",endpoint,state,e.ContentKey);
            var value=exact ?? new LedgerDialogueEntry {State=state,ContentKey=content};
            value.State=state;value.Capture=o;value.LastObservedUtc=o.Utc;
            if(value.LastEncounteredUtc==null || Stamp(o.Utc)>=Stamp(value.LastEncounteredUtc))
            {value.LastEncounteredUtc=o.Utc;value.Encounter=o;value.EncounterKind="exact-text";}
            PutDialogue(endpoint,value);
            return "committed";
        }
        private void MigrateDialogue()
        {
            long read=0,duplicates=0;
            try
            {
                using(transaction=connection.BeginTransaction())
                {
                    Exec(@"ALTER TABLE dialogue_pages RENAME TO dialogue_pages_v1;
CREATE TABLE dialogue_pages(endpoint_key TEXT NOT NULL REFERENCES endpoints(endpoint_key), state_key TEXT NOT NULL, content_key TEXT NOT NULL, last_observed_utc TEXT NOT NULL, last_encountered_utc TEXT NOT NULL, capture_json BLOB NOT NULL, encounter_json BLOB, encounter_kind TEXT NOT NULL, PRIMARY KEY(endpoint_key,content_key));");
                    using(var command=Command("SELECT endpoint_key,state_key,page,last_observed_utc,capture_json FROM dialogue_pages_v1"))
                    using(var reader=command.ExecuteReader())
                        while(reader.Read())
                        {
                            string endpoint=(string)reader[0],state=(string)reader[1],seen=(string)reader[3];byte[] bytes=(byte[])reader[4];
                            if(bytes.Length>limits.RecordBytes) throw new InvalidDataException("legacy-dialogue-size-limit;not-migrated");
                            var o=LedgerJson.Decode<LedgerObservation>(bytes);
                            if(o==null || o.Kind!="dialogue-shown" || o.Dialogue==null || o.Dialogue.BodyVerbatim==null || o.Context==null || !o.Context.Recordable || o.Identity==null || !(o.Identity.NpcEntityId>0) ||
                                Key(o)!=endpoint || DialogueState(o)!=state || o.Dialogue.Page!=Convert.ToInt32(reader[2],CultureInfo.InvariantCulture))
                                throw new InvalidDataException("legacy-dialogue-identity-inconsistent;not-migrated");
                            var ownerBytes=Scalar("SELECT identity_json FROM endpoints WHERE endpoint_key=@p0",endpoint) as byte[];
                            if(ownerBytes==null || Conflict(LedgerJson.Decode<LedgerEndpointState>(ownerBytes).Identity,o.Identity))
                                throw new InvalidDataException("legacy-dialogue-conflicting-owner;not-migrated");
                            Stamp(o.Utc);Stamp(seen);
                            string key=LedgerJson.Hash(Body(o));
                            var e=ReadDialogueEntries(endpoint).Find(x=>x.ContentKey==key);
                            bool exactEncounter=seen==o.Utc;
                            if(e==null)
                                e=new LedgerDialogueEntry {State=state,ContentKey=key,Capture=o,LastObservedUtc=o.Utc,LastEncounteredUtc=seen,
                                    Encounter=exactEncounter ? o : null,EncounterKind=exactEncounter ? "exact-text" : "legacy-v1-last-seen-only;prefix-payload-unavailable"};
                            else
                            {
                                if(Body(e.Capture)!=Body(o)) throw new InvalidDataException("legacy-dialogue-hash-collision;not-merged");
                                duplicates++;
                                if(Stamp(o.Utc)>Stamp(e.LastObservedUtc)) {e.Capture=o;e.State=state;e.LastObservedUtc=o.Utc;}
                                if(Stamp(seen)>Stamp(e.LastEncounteredUtc))
                                {e.LastEncounteredUtc=seen;e.Encounter=exactEncounter ? o : null;e.EncounterKind=exactEncounter ? "exact-text" : "legacy-v1-last-seen-only;prefix-payload-unavailable";}
                            }
                            if(Convert.ToInt64(Scalar("SELECT count(*) FROM dialogue_pages WHERE endpoint_key=@p0",endpoint)) >= limits.DialoguePagesPerEndpoint &&
                                Scalar("SELECT content_key FROM dialogue_pages WHERE endpoint_key=@p0 AND content_key=@p1",endpoint,key)==null)
                                throw new InvalidDataException("migration-dialogue-limit;prior-data-preserved");
                            PutDialogue(endpoint,e);read++;
                        }
                    Inject("dialogue-migration-after-copy");
                    Exec("DROP TABLE dialogue_pages_v1; PRAGMA user_version=2");
                    Inject("dialogue-migration-before-commit");
                    transaction.Commit();
                }
                DialogueMigrated=read;DialogueDuplicatesCollapsed=duplicates;
            }
            finally {transaction=null;}
        }
    }
}
