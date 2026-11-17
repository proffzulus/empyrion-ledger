using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using Mono.Data.Sqlite;

namespace TradeLedgerProbeB
{
    // Reversible annotation. Automatic hand-ins use a separate evidence receipt and never clear Done.
    public sealed class LedgerManualCompletion
    {
        public readonly string World, Endpoint, Body, Utc, Block;public string RequestKey;public int ObservedPage;
        public readonly int PlayerId;
        public readonly int? Structure;
        public readonly bool Completed, IsTracking, Tracked, IsArchive, Archived;
        public bool? StoredCompleted; // Actual committed value, including ignored automatic replays.
        public string AutomaticEvidence,ObservedUtc,EvidenceDetail,AutomaticResult,ObservedSession,ObservedInteraction;
        private LedgerManualCompletion(LedgerObservation source,int playerId,bool completed,bool tracking=false,bool archive=false)
        {
            World=source.Context.WorldKey;Endpoint=LedgerDatabase.Key(source);Body=source.Dialogue.BodyVerbatim;
            RequestKey=Key(source);PlayerId=playerId;IsTracking=tracking;IsArchive=archive;Archived=archive&&completed;Completed=!tracking&&!archive&&completed;Tracked=tracking&&completed;Utc=DateTime.UtcNow.ToString("o");
            Structure=source.Identity.StructureEntityId;Block=source.Identity.BlockGrid;
        }
        public string PendingKey { get { return PendingFor(World,PlayerId,RequestKey)+(IsArchive?":archive":IsTracking?":track":""); } }
        public static string Key(LedgerObservation source)
        {
            if(source==null||source.Context==null||!source.Context.Recordable||string.IsNullOrEmpty(source.Context.WorldKey)||string.IsNullOrEmpty(source.Context.Playfield)||source.Identity==null||!(source.Identity.NpcEntityId>0)||source.Dialogue==null||string.IsNullOrEmpty(source.Dialogue.BodyVerbatim)||source.Dialogue.BodyVerbatim.Length>4096)
                throw new ArgumentException("manual-request-identity-unresolved");
            return InstanceKey(LedgerDatabase.Key(source),source.Dialogue.BodyVerbatim,source.RequestInstance);
        }
        public static string InstanceKey(string endpoint,string body,string instance){if(string.IsNullOrEmpty(instance))return KeyFor(endpoint,body);Guid g;if(!Guid.TryParseExact(instance,"N",out g))throw new InvalidDataException("invalid-request-instance");return LedgerJson.Hash(KeyFor(endpoint,body)+"|occurrence:"+instance);}
        public static string KeyFor(string endpoint,string body)
        {return LedgerJson.Hash("manual-request-exact-body-v1|"+endpoint+"|"+LedgerJson.Hash(body));}
        public static bool CanCreate(LedgerRequestCard card,int? player)
        {
            var source=card==null?null:card.Source;
            return card!=null&&card.Request!=null&&card.Pages!=null&&card.Pages.Count==1&&ReferenceEquals(card.Pages[0],source)&&ReferenceEquals(card.Request.Source,source)&&player>0&&source!=null&&source.Kind=="dialogue-shown"&&source.Context!=null&&source.Context.Recordable&&!string.IsNullOrEmpty(source.Context.WorldKey)&&!string.IsNullOrEmpty(source.Context.Playfield)&&source.Identity!=null&&source.Identity.NpcEntityId>0&&source.Identity.OwnerConsistent!=false&&source.Dialogue!=null&&!string.IsNullOrEmpty(source.Dialogue.BodyVerbatim)&&source.Dialogue.BodyVerbatim.Length<=4096;
        }
        public static string PendingFor(string world,int player,string requestKey)
        {return world+":"+player.ToString(CultureInfo.InvariantCulture)+":"+requestKey;}
        public static bool TryCreate(LedgerRequestCard card,int? player,bool completed,out LedgerManualCompletion value)
        {
            value=null;
            // A multi-page unresolved conversation has no single certified request identity.
            if(!CanCreate(card,player))return false;
            value=new LedgerManualCompletion(card.Source,player.Value,completed);return true;
        }
        public static bool TryTrack(LedgerRequestCard card,int? player,bool tracked,out LedgerManualCompletion value)
        {value=null;if(!CanCreate(card,player))return false;value=new LedgerManualCompletion(card.Source,player.Value,tracked,true);return true;}
        public static bool TryArchive(LedgerObservation source,int? player,bool archived,out LedgerManualCompletion value)
        {
            value=null;
            if(!(player>0)||source==null||source.Kind!="dialogue-shown"||source.Identity==null||source.Identity.OwnerConsistent==false)return false;
            try{Key(source);}catch(ArgumentException){return false;}
            value=new LedgerManualCompletion(source,player.Value,archived,false,true);return true;
        }
        private static bool ValidStoredKey(SqliteConnection db,SqliteTransaction tx,string key,string endpoint,string body){if(key==KeyFor(endpoint,body))return true;using(var c=db.CreateCommand()){c.Transaction=tx;c.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='request_instances_v1'";if(Convert.ToInt32(c.ExecuteScalar())==0)return false;c.CommandText="SELECT capture_json FROM request_instances_v1 WHERE request_key=@key AND endpoint_key=@ep";c.Parameters.AddWithValue("@key",key);c.Parameters.AddWithValue("@ep",endpoint);var b=c.ExecuteScalar() as byte[];if(b==null)return false;var o=LedgerJson.Decode<LedgerObservation>(b);return o.Dialogue.BodyVerbatim==body&&Key(o)==key;}}
        internal static Dictionary<string,bool> Read(SqliteConnection db,SqliteTransaction tx,string world,int? player,bool tracking=false,Dictionary<string,string> stamps=null,bool archive=false)
        {
            string table=archive?"conversation_disposition_v1":tracking?"request_tracking_v1":"request_completion_v1",column=archive?"archived":tracking?"tracked":"completed";
            var result=new Dictionary<string,bool>();if(!(player>0))return result;
            using(var c=db.CreateCommand())
            {
                c.Transaction=tx;c.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='"+table+"'";
                if(Convert.ToInt32(c.ExecuteScalar(),CultureInfo.InvariantCulture)==0)return result;
                c.CommandText="SELECT request_key,endpoint_key,body_verbatim,"+column+",updated_utc FROM "+table+" WHERE world_key=@world AND player_id=@player LIMIT 4097";
                c.Parameters.AddWithValue("@world",world);c.Parameters.AddWithValue("@player",player.Value);
                using(var r=c.ExecuteReader())while(r.Read())
                {
                    string key=(string)r[0],endpoint=(string)r[1],body=(string)r[2];int done=Convert.ToInt32(r[3],CultureInfo.InvariantCulture);
                    if(result.Count>=4096||endpoint.Length>512||!endpoint.StartsWith(world+":",StringComparison.Ordinal)||body.Length==0||body.Length>4096||!ValidStoredKey(db,tx,key,endpoint,body)||(done!=0&&done!=1))throw new InvalidDataException("manual-completion-row-invalid-or-limit");
                    result.Add(key,done==1);if(stamps!=null)stamps.Add(key,(string)r[4]);
                }
            }
            return result;
        }
    }
    public sealed partial class LedgerDatabase
    {
        public void SaveManualCompletion(LedgerManualCompletion value)
        {
            if(value==null)throw new ArgumentNullException("value");
            try
            {
                using(transaction=connection.BeginTransaction())
                {
                    if(value.AutomaticEvidence!=null&&Convert.ToInt64(Scalar("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='request_instances_v1'"))>0){var matched=Scalar("SELECT capture_json FROM request_instances_v1 WHERE endpoint_key=@p0 AND player_id=@p1 ORDER BY seen_utc DESC LIMIT 1",value.Endpoint,value.PlayerId) as byte[];if(matched!=null){var source=LedgerJson.Decode<LedgerObservation>(matched);if(source.Dialogue.BodyVerbatim==value.Body&&source.Session==value.ObservedSession&&source.Interaction==value.ObservedInteraction&&source.Dialogue.Page==value.ObservedPage)value.RequestKey=LedgerManualCompletion.Key(source);}}
                    var bytes=Scalar("SELECT identity_json FROM endpoints WHERE endpoint_key=@p0 AND world_key=@p1",value.Endpoint,value.World) as byte[];
                    if(bytes==null)throw new InvalidDataException("manual-completion-endpoint-not-observed");
                    if(value.RequestKey!=LedgerManualCompletion.KeyFor(value.Endpoint,value.Body)){var instance=Scalar("SELECT capture_json FROM request_instances_v1 WHERE request_key=@p0 AND endpoint_key=@p1 AND player_id=@p2",value.RequestKey,value.Endpoint,value.PlayerId) as byte[];if(instance==null||LedgerManualCompletion.Key(LedgerJson.Decode<LedgerObservation>(instance))!=value.RequestKey)throw new InvalidDataException("unobserved-request-instance");}
                    var known=LedgerJson.Decode<LedgerEndpointState>(bytes);
                    if(known.Identity==null||known.Identity.OwnerConsistent==false||Different(known.Identity.StructureEntityId,value.Structure)||Different(known.Identity.BlockGrid,value.Block))throw new InvalidDataException("manual-completion-owner-conflict");
                    var page=Scalar("SELECT capture_json FROM dialogue_pages WHERE endpoint_key=@p0 AND content_key=@p1",value.Endpoint,LedgerJson.Hash(value.Body)) as byte[];
                    var capture=page==null?null:LedgerJson.Decode<LedgerObservation>(page);
                    bool shown=capture!=null&&capture.Dialogue!=null&&capture.Dialogue.BodyVerbatim==value.Body;
                    shown=shown||known.LastRequest!=null&&known.LastRequest.Dialogue.BodyVerbatim==value.Body||known.LastDialogue!=null&&known.LastDialogue.Dialogue.BodyVerbatim==value.Body;
                    if(!shown)throw new InvalidDataException("manual-completion-body-not-observed");
                    // A separate optional table keeps old Done values independent of tracking writes.
                    string table=value.IsArchive?"conversation_disposition_v1":value.IsTracking?"request_tracking_v1":"request_completion_v1",column=value.IsArchive?"archived":value.IsTracking?"tracked":"completed";
                    Exec("CREATE TABLE IF NOT EXISTS "+table+"(world_key TEXT NOT NULL,player_id INTEGER NOT NULL,request_key TEXT NOT NULL,endpoint_key TEXT NOT NULL,body_verbatim TEXT NOT NULL,"+column+" INTEGER NOT NULL CHECK("+column+" IN (0,1)),updated_utc TEXT NOT NULL,PRIMARY KEY(world_key,player_id,request_key));");
                    object prior=Scalar("SELECT body_verbatim FROM "+table+" WHERE world_key=@p0 AND player_id=@p1 AND request_key=@p2",value.World,value.PlayerId,value.RequestKey);
                    if(prior!=null&&(string)prior!=value.Body)throw new InvalidDataException("manual-annotation-key-collision");
                    long count=Convert.ToInt64(Scalar("SELECT count(*) FROM "+table+" WHERE world_key=@p0 AND player_id=@p1",value.World,value.PlayerId),CultureInfo.InvariantCulture);
                    if(prior==null&&count>=4096)throw new InvalidDataException("manual-annotation-limit;prior-state-preserved");
                    bool active=value.IsArchive?value.Archived:value.IsTracking?value.Tracked:value.Completed;
                    if(value.AutomaticEvidence!=null)
                    {
                        if(value.IsArchive||value.IsTracking||!value.Completed||value.AutomaticEvidence.Length>256||string.IsNullOrEmpty(value.ObservedUtc)||string.IsNullOrEmpty(value.EvidenceDetail)||value.EvidenceDetail.Length>2048)throw new InvalidDataException("automatic-evidence-invalid");
                        Exec("CREATE TABLE IF NOT EXISTS request_auto_evidence_v1(event_key TEXT PRIMARY KEY,world_key TEXT NOT NULL,player_id INTEGER NOT NULL,request_key TEXT NOT NULL,evidence TEXT NOT NULL,result TEXT NOT NULL,updated_utc TEXT NOT NULL)");
                        if(Scalar("SELECT event_key FROM request_auto_evidence_v1 WHERE event_key=@p0",value.AutomaticEvidence)!=null)
                        {value.AutomaticResult="duplicate-evidence;unchanged";var actual=Scalar("SELECT completed FROM request_completion_v1 WHERE world_key=@p0 AND player_id=@p1 AND request_key=@p2",value.World,value.PlayerId,value.RequestKey);value.StoredCompleted=actual!=null&&Convert.ToInt32(actual)==1;transaction.Commit();return;}
                        if(Convert.ToInt64(Scalar("SELECT count(*) FROM request_auto_evidence_v1"),CultureInfo.InvariantCulture)>=65536)throw new InvalidDataException("automatic-evidence-limit;manual-controls-remain-available");
                        string priorUtc=Scalar("SELECT updated_utc FROM request_completion_v1 WHERE world_key=@p0 AND player_id=@p1 AND request_key=@p2",value.World,value.PlayerId,value.RequestKey) as string;
                        object previous=Scalar("SELECT completed FROM request_completion_v1 WHERE world_key=@p0 AND player_id=@p1 AND request_key=@p2",value.World,value.PlayerId,value.RequestKey);
                        value.StoredCompleted=previous!=null&&Convert.ToInt32(previous)==1;
                        value.AutomaticResult=previous!=null&&Convert.ToInt32(previous,CultureInfo.InvariantCulture)==1?"already-Done;unchanged":previous!=null&&string.CompareOrdinal(priorUtc,value.ObservedUtc)>=0?"newer-manual-correction;unchanged":"Pending-to-Done";
                        Exec("INSERT INTO request_auto_evidence_v1 VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",value.AutomaticEvidence,value.World,value.PlayerId,value.RequestKey,value.EvidenceDetail,value.AutomaticResult,value.Utc);
                        if(value.AutomaticResult!="Pending-to-Done"){transaction.Commit();return;}
                    }
                    Exec("INSERT OR REPLACE INTO "+table+"(world_key,player_id,request_key,endpoint_key,body_verbatim,"+column+",updated_utc) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",value.World,value.PlayerId,value.RequestKey,value.Endpoint,value.Body,active?1:0,value.Utc);
                    if(!value.IsTracking&&!value.IsArchive)value.StoredCompleted=active;
                    Inject(value.IsArchive?"conversation-archive-before-commit":value.IsTracking?"manual-tracking-before-commit":"manual-completion-before-commit");transaction.Commit();
                }
            }
            finally{transaction=null;}
        }
    }
}
