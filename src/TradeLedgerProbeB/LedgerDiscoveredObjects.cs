using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mono.Data.Sqlite;

namespace TradeLedgerProbeB
{
    public static class LedgerObjectDiscoveryAdmission
    {
        public static bool LocalEvent(int player,int actorId,object actor,object localActor,object world,object currentWorld,bool concreteId,bool personalMember,bool matchingGroup)
        {return player>0&&player==actorId&&actor!=null&&ReferenceEquals(actor,localActor)&&world!=null&&ReferenceEquals(world,currentWorld)&&concreteId&&personalMember&&matchingGroup;}
    }
    public sealed class LedgerObjectCaptureMemo<T> where T:IEquatable<T>
    {
        private readonly Dictionary<int,T> samples=new Dictionary<int,T>();
        public bool Matches(int id,T sample){T old;return samples.TryGetValue(id,out old)&&old.Equals(sample);}
        public void Clear(){samples.Clear();}
        public void Remember(int id,T sample,bool accepted){if(accepted&&(samples.Count<LedgerDiscoveredObjects.Limit||samples.ContainsKey(id)))samples[id]=sample;}
    }
    public enum LedgerObjectOrganization { Unsorted=0, Favorites=1, Hidden=2 }
    // Discovery facts contain no offers, quests, loot, threat assessment or native references.
    public sealed class LedgerObjectFact
    {
        public int Player,NativeId;public string NativeName,DisplayName,Template,Group,Category;
        public string Evidence,NameSource,TemplateSource,FactionSource,PositionSource,RawLabel,ContentIdentity;
        public int? FactionGroup,FactionId;public double? X,Y,Z;
        public bool? IsPoi,CurrentInstanceState;
    }
    public sealed class LedgerObjectRecord
    {
        public string Key,FirstUtc,LastUtc,IdentityState="native-saved-ID;ordinary-reload;regenerated-ID-link-unproven";
        public LedgerObservation Latest;
        public LedgerObjectOrganization Organization;
        public Dictionary<string,string> FieldUtc=new Dictionary<string,string>();
        public bool IdentityConflict;
        public LedgerObservation ConflictObservation;
        public List<string> PriorNames=new List<string>();
        public List<string> Evidence=new List<string>();
    }
    public sealed class LedgerObjectDisposition
    {
        public string World,Key;public LedgerObservation Observed;public int Player;public LedgerObjectOrganization Value;
        public bool Valid(){return (Observed==null||LedgerDiscoveredObjects.Valid(Observed)&&LedgerDiscoveredObjects.Key(Observed)==Key&&Observed.Context.WorldKey==World&&Observed.DiscoveredObject.Player==Player)&&!string.IsNullOrEmpty(World)&&World.Length<=128&&!string.IsNullOrEmpty(Key)&&Key.Length==64&&Player>0&&Enum.IsDefined(typeof(LedgerObjectOrganization),Value);}
    }
    public static class LedgerDiscoveredObjects
    {
        public const int Limit=8192;
        public static LedgerObjectDisposition Organize(LedgerObjectRecord record,LedgerObjectOrganization value)
        {if(record==null||!Valid(record.Latest)||record.Key!=Key(record.Latest))return null;return new LedgerObjectDisposition{World=record.Latest.Context.WorldKey,Player=record.Latest.DiscoveredObject.Player,Key=record.Key,Value=value,Observed=record.Latest};}
        public static int CompareUtc(string a,string b)
        {return DateTime.Parse(a,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).CompareTo(DateTime.Parse(b,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind));}
        public static string Key(LedgerObservation o)
        {return LedgerJson.Hash("observed-object-v1|"+o.Context.WorldKey+"|"+o.DiscoveredObject.Player+"|"+o.Context.Playfield+"|entity:"+o.DiscoveredObject.NativeId);}
        private static bool Position(string value,out double x,out double y,out double z)
        {x=y=z=0;var a=(value??"").Split(',');return a.Length==3&&double.TryParse(a[0],NumberStyles.Float,CultureInfo.InvariantCulture,out x)&&double.TryParse(a[1],NumberStyles.Float,CultureInfo.InvariantCulture,out y)&&double.TryParse(a[2],NumberStyles.Float,CultureInfo.InvariantCulture,out z)&&Coordinate(x)&&Coordinate(y)&&Coordinate(z);}
        private static bool Text(string s,int max){return s==null||s.Length<=max;}
        private static bool Coordinate(double? n){return !n.HasValue||!double.IsNaN(n.Value)&&!double.IsInfinity(n.Value)&&Math.Abs(n.Value)<1e9;}
        public static bool Valid(LedgerObservation o)
        {
            var f=o==null?null:o.DiscoveredObject;DateTime utc;
            return o!=null&&o.Kind=="place-observed"&&o.Context!=null&&o.Context.Recordable&&o.Context.Mode=="SinglePlayer"&&!string.IsNullOrEmpty(o.Context.WorldKey)&&!string.IsNullOrEmpty(o.Context.Playfield)&&
                f!=null&&f.Player>0&&f.Player==o.ObserverPlayer&&f.NativeId>0&&new[]{"local-poi-discovery","personal-discovery-map","visible-map","observed-owner"}.Contains(f.Evidence)&&
                Text(f.NativeName,512)&&Text(f.ContentIdentity,128)&&Text(f.DisplayName,512)&&Text(f.Template,256)&&Text(f.Group,256)&&Text(f.RawLabel,4096)&&Text(f.Category,64)&&new[]{f.NameSource,f.TemplateSource,f.FactionSource,f.PositionSource}.All(s=>Text(s,512))&&
                Coordinate(f.X)&&Coordinate(f.Y)&&Coordinate(f.Z)&&f.X.HasValue==f.Y.HasValue&&f.Y.HasValue==f.Z.HasValue&&f.FactionGroup.HasValue==f.FactionId.HasValue&&
                DateTime.TryParse(o.Utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out utc)&&utc.Kind==DateTimeKind.Utc;
        }
        public static LedgerObservation Owner(LedgerObservation o)
        {
            var i=o==null?null:o.Identity;
            if(o==null||o.Context==null||i==null||!(i.NpcEntityId>0)||!(i.StructureEntityId>0)||i.OwnerConsistent!=true||o.ObserverPlayer<=0||o.Kind!="market"&&o.Kind!="dialogue-shown")return null;
            // Existing direct owner IDs are sufficient for an observed-owner record, not a scan claim.
            var f=new LedgerObjectFact{Player=o.ObserverPlayer,NativeId=i.StructureEntityId.Value,DisplayName=i.StructureName,NameSource=i.StructureNameSource,CurrentInstanceState=true,Evidence="observed-owner",Category="observed structure"};
            double x,y,z;if(i.OwnerConsistent==true&&Position(i.StructureNativePosition,out x,out y,out z)){f.X=x;f.Y=y;f.Z=z;f.PositionSource=i.StructurePositionSource;}
            return new LedgerObservation{Kind="place-observed",Context=o.Context,ObserverPlayer=o.ObserverPlayer,Utc=o.Utc,Session=o.Session,Sequence=o.Sequence,GameTicks=o.GameTicks,Identity=new LedgerIdentity(),DiscoveredObject=f,Source="already observed NPC/trader direct owner;not discovery backfill"};
        }
        public static LedgerObjectRecord Merge(LedgerObjectRecord old,LedgerObservation o)
        {
            if(!Valid(o))throw new InvalidDataException("object-observation-invalid");
            var key=Key(o);if(old!=null&&old.Key!=key)throw new InvalidDataException("object-key-conflict");
            if(old!=null&&CompareUtc(o.Utc,old.LastUtc)<0)return old;
            var r=old==null?new LedgerObjectRecord{Key=key,FirstUtc=o.Utc}:LedgerJson.Decode<LedgerObjectRecord>(LedgerJson.Encode(old));
            var next=LedgerJson.Decode<LedgerObservation>(LedgerJson.Encode(o));var a=r.Latest==null?null:r.Latest.DiscoveredObject;var b=next.DiscoveredObject;
            // A different known blueprint under a reused native ID is unresolved identity, not a rename.
            if(a!=null&&!string.IsNullOrEmpty(a.Template)&&!string.IsNullOrEmpty(b.Template)&&!string.Equals(LedgerObjectTypes.TemplateKey(a.Template),LedgerObjectTypes.TemplateKey(b.Template),StringComparison.OrdinalIgnoreCase))
            {r.IdentityConflict=true;r.ConflictObservation=next;r.LastUtc=o.Utc;r.IdentityState="conflicting-template-under-native-ID;manual/observed-relink-required";return r;}
            // A saved spawn/discovery profile is not a fresh rename/ownership measurement.
            // Preserve stronger actually bound instance state; keep the raw profile in history.
            if(a!=null&&a.CurrentInstanceState==true&&b.CurrentInstanceState==false){b.NativeName=null;b.DisplayName=null;b.NameSource=null;b.FactionGroup=b.FactionId=null;b.FactionSource=null;b.X=b.Y=b.Z=null;b.PositionSource=null;b.IsPoi=null;b.CurrentInstanceState=true;}
            if(r.FieldUtc==null)r.FieldUtc=new Dictionary<string,string>();
            if(!string.IsNullOrEmpty(b.DisplayName))r.FieldUtc["name"]=o.Utc;
            if(!string.IsNullOrEmpty(b.Template))r.FieldUtc["template"]=o.Utc;
            if(!string.IsNullOrEmpty(b.Group))r.FieldUtc["group"]=o.Utc;
            if(b.FactionGroup.HasValue)r.FieldUtc["faction"]=o.Utc;
            if(b.X.HasValue)r.FieldUtc["position"]=o.Utc;
            if(b.IsPoi.HasValue)r.FieldUtc["native-poi-flag"]=o.Utc;
            if(!string.IsNullOrEmpty(b.ContentIdentity))r.FieldUtc["content"]=o.Utc;
            if(!string.IsNullOrEmpty(next.Context.SolarSystem))r.FieldUtc["system-name"]=o.Utc;
            var geo=next.Context.Geography;
            if(geo!=null){if(!string.IsNullOrEmpty(geo.BodyKind))r.FieldUtc["body-kind"]=o.Utc;if(!string.IsNullOrEmpty(geo.SystemCoordinates))r.FieldUtc["system-coordinates"]=o.Utc;if(!string.IsNullOrEmpty(geo.SectorCoordinates))r.FieldUtc["sector-coordinates"]=o.Utc;if(!string.IsNullOrEmpty(geo.GroupName))r.FieldUtc["group-name"]=o.Utc;}
            if(a!=null){
                if(string.IsNullOrEmpty(next.Context.SolarSystem))next.Context.SolarSystem=r.Latest.Context.SolarSystem;
                var priorGeo=r.Latest.Context.Geography;
                if(priorGeo!=null){if(geo==null)next.Context.Geography=priorGeo;
                    else{bool retained=string.IsNullOrEmpty(geo.BodyKind)&&!string.IsNullOrEmpty(priorGeo.BodyKind)||string.IsNullOrEmpty(geo.SystemCoordinates)&&!string.IsNullOrEmpty(priorGeo.SystemCoordinates)||string.IsNullOrEmpty(geo.SectorCoordinates)&&!string.IsNullOrEmpty(priorGeo.SectorCoordinates)||string.IsNullOrEmpty(geo.GroupName)&&!string.IsNullOrEmpty(priorGeo.GroupName);
                        geo.BodyKind=string.IsNullOrEmpty(geo.BodyKind)?priorGeo.BodyKind:geo.BodyKind;geo.SystemCoordinates=string.IsNullOrEmpty(geo.SystemCoordinates)?priorGeo.SystemCoordinates:geo.SystemCoordinates;geo.SectorCoordinates=string.IsNullOrEmpty(geo.SectorCoordinates)?priorGeo.SectorCoordinates:geo.SectorCoordinates;geo.GroupName=string.IsNullOrEmpty(geo.GroupName)?priorGeo.GroupName:geo.GroupName;
                        if(retained)geo.Source=(geo.Source??"unknown current source")+"; earlier known geography retained where absent; see FieldUtc";}}
                if(string.IsNullOrEmpty(b.DisplayName)){b.DisplayName=a.DisplayName;b.NameSource=a.NameSource;}
                else if(a.DisplayName!=null&&a.DisplayName!=b.DisplayName&&!r.PriorNames.Contains(a.DisplayName)&&r.PriorNames.Count<16)r.PriorNames.Add(a.DisplayName);
                if(string.IsNullOrEmpty(b.NativeName))b.NativeName=a.NativeName;
                if(string.IsNullOrEmpty(b.ContentIdentity))b.ContentIdentity=a.ContentIdentity;
                if(string.IsNullOrEmpty(b.Template)){b.Template=a.Template;b.TemplateSource=a.TemplateSource;}if(string.IsNullOrEmpty(b.Group))b.Group=a.Group;
                if(!b.FactionGroup.HasValue){b.FactionGroup=a.FactionGroup;b.FactionId=a.FactionId;b.FactionSource=a.FactionSource;}
                if(!b.X.HasValue){b.X=a.X;b.Y=a.Y;b.Z=a.Z;b.PositionSource=a.PositionSource;}
                if(!b.IsPoi.HasValue)b.IsPoi=a.IsPoi;if(!b.CurrentInstanceState.HasValue)b.CurrentInstanceState=a.CurrentInstanceState;
            }
            if(!r.Evidence.Contains(b.Evidence)&&r.Evidence.Count<8)r.Evidence.Add(b.Evidence);
            r.Latest=next;r.LastUtc=o.Utc;return r;
        }
        internal static List<LedgerObjectRecord> Read(SqliteConnection db,SqliteTransaction tx,string world,int? player)
        {
            var rows=new List<LedgerObjectRecord>();if(!(player>0))return rows;
            using(var c=db.CreateCommand()){
                c.Transaction=tx;c.CommandText="SELECT count(*) FROM sqlite_master WHERE name='discovered_objects_v1'";if(Convert.ToInt32(c.ExecuteScalar())==0)return rows;
                c.CommandText="SELECT object_key,record_json FROM discovered_objects_v1 WHERE world_key=@world AND player_id=@player ORDER BY object_key LIMIT 8193";c.Parameters.AddWithValue("@world",world);c.Parameters.AddWithValue("@player",player.Value);
                long bytes=0;using(var rd=c.ExecuteReader())while(rd.Read()){
                    var data=(byte[])rd[1];bytes+=data.Length;if(rows.Count>=Limit||data.Length>32768||bytes>32*1024*1024)throw new InvalidDataException("object-view-bound");
                    var r=LedgerJson.Decode<LedgerObjectRecord>(data);if(r==null||r.Key!=(string)rd[0]||!Valid(r.Latest)||Key(r.Latest)!=r.Key||r.Latest.Context.WorldKey!=world||r.Latest.DiscoveredObject.Player!=player||!Enum.IsDefined(typeof(LedgerObjectOrganization),r.Organization))throw new InvalidDataException("object-view-identity");rows.Add(r);
                }
            }return rows;
        }
    }
    public sealed class LedgerObjectIndex
    {
        private readonly Dictionary<string,LedgerObjectRecord> records=new Dictionary<string,LedgerObjectRecord>();
        private readonly int? rPlayer;
        private readonly Dictionary<string,string> titles=new Dictionary<string,string>();
        private readonly Dictionary<string,LedgerObjectRecord> owners=new Dictionary<string,LedgerObjectRecord>();
        public readonly List<LedgerObjectRecord> Places;
        public readonly bool Available;
        public LedgerProductionPlaces Hierarchy {get;private set;}
        public string WorldKey {get{return world;}}
        public void RefreshHierarchy(){Hierarchy=new LedgerProductionPlaces(world,Places.Select(r=>r.Latest.Context));}
        private readonly string world;
        public LedgerObjectIndex(LedgerViewData data)
        {
            world=data.WorldKey;
            rPlayer=data.ManualPlayerId;Available=data.ManualPlayerId>0&&data.ObjectReadError==null;
            if(!Available){Places=new List<LedgerObjectRecord>();Hierarchy=new LedgerProductionPlaces(data.WorldKey,new LedgerContext[0]);return;}
            foreach(var r in data.DiscoveredObjects)if(r!=null&&LedgerDiscoveredObjects.Valid(r.Latest)&&r.Latest.Context.WorldKey==data.WorldKey&&r.Latest.DiscoveredObject.Player==data.ManualPlayerId&&r.Key==LedgerDiscoveredObjects.Key(r.Latest))records[r.Key]=r;
            foreach(var o in data.Markets.Concat(data.DialoguePages)){
                var owner=LedgerDiscoveredObjects.Owner(o);if(owner==null||!LedgerDiscoveredObjects.Valid(owner)||owner.Context.WorldKey!=data.WorldKey||owner.DiscoveredObject.Player!=data.ManualPlayerId)continue;
                string k=LedgerDiscoveredObjects.Key(owner);LedgerObjectRecord old;if(records.TryGetValue(k,out old)&&old.LastUtc!=null&&LedgerDiscoveredObjects.CompareUtc(old.LastUtc,owner.Utc)>=0)continue;records[k]=LedgerDiscoveredObjects.Merge(old,owner);
            }
            // This index is warmed by LedgerViewData.Read on its reader worker. Type JSON
            // and identity enrichment are never parsed from a card draw or discovery callback.
            foreach(var r in records.Values){var f=r.Latest.DiscoveredObject;var type=TypeFor(r);titles[r.Key]=!string.IsNullOrEmpty(f.DisplayName)?f.DisplayName:type!=null&&!string.IsNullOrEmpty(type.DisplayName)?type.DisplayName+" (type)":"Unknown place";}
            Places=records.Values.OrderBy(r=>r.Latest.Context.Playfield,StringComparer.OrdinalIgnoreCase).ThenBy(r=>Title(r),StringComparer.OrdinalIgnoreCase).ThenBy(r=>r.Key,StringComparer.Ordinal).ToList();
            foreach(var r in Places)if(!r.IdentityConflict)owners[r.Latest.Context.Playfield+"|"+r.Latest.DiscoveredObject.NativeId]=r;
            Hierarchy=new LedgerProductionPlaces(data.WorldKey,Places.Select(r=>r.Latest.Context));
        }
        public string Title(LedgerObjectRecord r){string text;return r!=null&&titles.TryGetValue(r.Key,out text)?text:"Unknown place";}
        public LedgerObjectType TypeFor(LedgerObjectRecord r)
        {return r!=null&&!r.IdentityConflict&&System.Text.RegularExpressions.Regex.IsMatch(r.Latest.DiscoveredObject.ContentIdentity??"",@"\bRE2 Build 46\b",System.Text.RegularExpressions.RegexOptions.CultureInvariant)?LedgerObjectTypes.Lookup(r.Latest.DiscoveredObject.Template):null;}
        public LedgerObjectRecord Owner(LedgerObservation o)
        {LedgerObjectRecord r;if(o==null||o.Context==null||o.Identity==null||o.Identity.OwnerConsistent==false||o.ObserverPlayer!=rPlayer||!(o.Identity.StructureEntityId>0))return null;return owners.TryGetValue(o.Context.Playfield+"|"+o.Identity.StructureEntityId.Value,out r)&&r.Latest.Context.WorldKey==o.Context.WorldKey?r:null;}
        public IEnumerable<LedgerObjectRecord> Query(LedgerObjectOrganization? organization=null,string place="all",string search=null)
        {return Places.Where(r=>(!organization.HasValue||r.Organization==organization)&&Hierarchy.Contains(place,r.Latest)&&(string.IsNullOrEmpty(search)||(Title(r)+" "+r.Latest.DiscoveredObject.Template+" "+r.Latest.Context.Playfield).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0));}
    }
    public sealed partial class LedgerDatabase
    {
        private string StoreObject(LedgerObservation o)
        {
            if(!LedgerDiscoveredObjects.Valid(o))throw new InvalidDataException("object-observation-invalid");string key=LedgerDiscoveredObjects.Key(o);
            Exec("CREATE TABLE IF NOT EXISTS discovered_objects_v1(world_key TEXT NOT NULL,player_id INTEGER NOT NULL,object_key TEXT NOT NULL,native_id INTEGER NOT NULL,playfield TEXT NOT NULL,record_json BLOB NOT NULL,PRIMARY KEY(world_key,player_id,object_key))");
            var prior=Scalar("SELECT record_json FROM discovered_objects_v1 WHERE world_key=@p0 AND player_id=@p1 AND object_key=@p2",o.Context.WorldKey,o.DiscoveredObject.Player,key) as byte[];
            if(prior==null&&Convert.ToInt32(Scalar("SELECT count(*) FROM discovered_objects_v1 WHERE world_key=@p0 AND player_id=@p1",o.Context.WorldKey,o.DiscoveredObject.Player))>=LedgerDiscoveredObjects.Limit)return "partial";
            var old=prior==null?null:LedgerJson.Decode<LedgerObjectRecord>(prior);
            if(old!=null&&LedgerDiscoveredObjects.CompareUtc(o.Utc,old.LastUtc)<0)return "stale";
            if(old!=null&&LedgerJson.Hash(LedgerJson.Encode(old.Latest.DiscoveredObject))==LedgerJson.Hash(LedgerJson.Encode(o.DiscoveredObject))&&LedgerJson.Hash(LedgerJson.Encode(old.Latest.Context.Geography))==LedgerJson.Hash(LedgerJson.Encode(o.Context.Geography)))return "duplicate";
            var record=LedgerDiscoveredObjects.Merge(old,o);var bytes=LedgerJson.Encode(record);if(bytes.Length>32768)return "partial";
            Exec("INSERT OR REPLACE INTO discovered_objects_v1 VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",o.Context.WorldKey,o.DiscoveredObject.Player,key,o.DiscoveredObject.NativeId,o.Context.Playfield,bytes);
            Exec("CREATE TABLE IF NOT EXISTS discovered_object_observations_v1(world_key TEXT NOT NULL,player_id INTEGER NOT NULL,object_key TEXT NOT NULL,observation_id TEXT NOT NULL PRIMARY KEY,observed_utc TEXT NOT NULL,observation_json BLOB NOT NULL)");
            Exec("CREATE INDEX IF NOT EXISTS discovered_object_observations_scope_v1 ON discovered_object_observations_v1(world_key,player_id,object_key,observed_utc)");
            Exec("INSERT OR IGNORE INTO discovered_object_observations_v1 VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",o.Context.WorldKey,o.DiscoveredObject.Player,key,o.ObservationId,o.Utc,LedgerJson.Encode(o));return "committed";
        }
        private string SaveObject(LedgerObservation o)
        {if(!LedgerDiscoveredObjects.Valid(o))return "identity-unknown";try{using(transaction=connection.BeginTransaction()){string result=StoreObject(o);Inject("object-before-commit");transaction.Commit();Inject("object-after-commit");return result;}}finally{transaction=null;}}
        public void SaveObjectDisposition(LedgerObjectDisposition value){SaveObjectDispositions(new[]{value});}
        public void SaveObjectDispositions(LedgerObjectDisposition[] values)
        {
            if(values==null||values.Length==0||values.Length>LedgerDiscoveredObjects.Limit||values.Any(v=>v==null||!v.Valid())||values.Any(v=>v.World!=values[0].World||v.Player!=values[0].Player)||values.Select(v=>v.Key).Distinct().Count()!=values.Length)throw new InvalidDataException("object-disposition-invalid");
            try{using(transaction=connection.BeginTransaction()){
                foreach(var value in values){
                if(Convert.ToInt32(Scalar("SELECT count(*) FROM sqlite_master WHERE name='discovered_objects_v1'"))==0){if(value.Observed==null)throw new InvalidDataException("object-not-observed");StoreObject(value.Observed);}
                var bytes=Scalar("SELECT record_json FROM discovered_objects_v1 WHERE world_key=@p0 AND player_id=@p1 AND object_key=@p2",value.World,value.Player,value.Key) as byte[];
                if(bytes==null&&value.Observed!=null){StoreObject(value.Observed);bytes=Scalar("SELECT record_json FROM discovered_objects_v1 WHERE world_key=@p0 AND player_id=@p1 AND object_key=@p2",value.World,value.Player,value.Key) as byte[];}
                if(bytes==null)throw new InvalidDataException("object-not-observed");var r=LedgerJson.Decode<LedgerObjectRecord>(bytes);
                if(r.Key!=value.Key||r.Latest.Context.WorldKey!=value.World||r.Latest.DiscoveredObject.Player!=value.Player)throw new InvalidDataException("object-disposition-scope");
                r.Organization=value.Value;Exec("UPDATE discovered_objects_v1 SET record_json=@p0 WHERE world_key=@p1 AND player_id=@p2 AND object_key=@p3",LedgerJson.Encode(r),value.World,value.Player,value.Key);}
                Inject("object-disposition-before-commit");transaction.Commit();
            }}finally{transaction=null;}
        }
    }
}
