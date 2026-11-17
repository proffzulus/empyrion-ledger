using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Data.Sqlite;

namespace TradeLedgerProbeB
{
    public sealed class LedgerProductionOutput
    {public int Item;public int? Auxiliary;public string Name;public long Stock;}
    public sealed class LedgerProductionSnapshot
    {
        public string Key,Kind,Device,Family,Resource,Block,Owner,PositionSource,DepositKey,Size,PercentageText,RawUi,Proof,Epoch,VisitUtc,CollectionUtc,Change,DetailsUtc;
        public string ShownOperating,ShownNext,ShownQuality,ShownStatus,DefinitionName;
        public int Player,Definition,ClrIdx;public int? DepositId;
        public float X,Y,Z;public bool NavigationPosition,Complete;
        public ulong Tick;public uint? Period;public ulong? NextTick;
        public ulong? DetailsTick,SizeTick,PercentageTick,FuelTick;public string FuelUtc,DetailsSource;
        public long? Capacity;public int? CapacityRaw;public double? FuelSeconds,RemainingPercent;
        public bool? Running;public bool Alternating;public int NextItem;
        public List<LedgerProductionOutput> Outputs=new List<LedgerProductionOutput>();
    }
    public sealed class LedgerProductionEstimate
    {public string State,FuelBasis;public readonly Dictionary<int,long> Stocks=new Dictionary<int,long>();public double? WorkingSeconds;public bool Full,NeedsFuel;}
    public sealed class LedgerProductionVisit
    {
        private readonly object gate=new object();
        private LedgerObservation latest;
        public void Shown(LedgerObservation o,bool admitted){if(admitted&&LedgerProduction.Valid(o)&&o.Production.Kind=="producer")lock(gate){var next=LedgerJson.Decode<LedgerObservation>(LedgerJson.Encode(o));if(latest!=null)LedgerProduction.PreserveFuel(next.Production,latest.Production);latest=next;}}
        public LedgerObservation Close(){lock(gate){var o=latest;latest=null;return o;}}
        public void Cancel(){lock(gate)latest=null;}
    }
    public static class LedgerProduction
    {
        public static string ScopeRejection(LedgerContext context,Eleon.Modding.IApplication app,int player)
        {
            if(context==null)return "context-unavailable";
            if(!context.Recordable)return "context-not-recordable";
            if(context.Mode!="SinglePlayer")return "context-mode";
            if(app==null)return "application-unavailable";
            if(app.State.ToString()!="Running")return "application-state";
            if(app.Mode.ToString()!="SinglePlayer")return "application-mode";
            var local=app.LocalPlayer;
            if(local==null)return "local-player-unavailable";
            if(player<=0||local.Id!=player)return "local-player-mismatch";
            if(!LedgerContext.MatchesCurrentSave(context,app.GetPathFor(Eleon.Modding.AppFolder.SaveGame)))return "physical-save-mismatch";
            return null;
        }

        // CC2B copies controller Props, then replaces BF34/BF35 from THIS root's WindowStub.
        // Do not weaken the other 11 fields or accept arbitrary below-top windows.
        public static string ConfigurationMismatch(Func<int,object> registered,Func<int,object> controller,Func<int,object> stub)
        {
            if(registered==null||controller==null||stub==null)return "configuration-or-window-stub-missing";
            for(int row=0xc132;row<=0xc13e;row++){
                int source=row==0xc135?0xc198:row==0xc136?0xc199:row;
                object a=registered(row),b=row==0xc135||row==0xc136?stub(source):controller(source);
                bool equal=a==null&&b==null||a is bool&&b is bool&&(bool)a==(bool)b||a is string&&b is string&&string.Equals((string)a,(string)b,StringComparison.Ordinal);
                if(!equal)return "field=0400"+row.ToString("X4")+" source="+(source==row?"controller.":"WindowStub.")+"0400"+source.ToString("X4");
            }
            return null;
        }
        // ConfigManager.52DD -> PathCollection.1304 is the INSTANCE ownership tuple.
        // 52DE is an unused STATIC int, never a player/device owner.
        public static bool LocalOwner(FactionData owner,int player){return player>0&&owner.Group==FactionGroup.Player&&owner.Id==player;}
        public static FactionData ReadOwner(Func<int,object,object> read,object model)
        {object tuple=read(0x52f6,model);if(tuple==null)throw new InvalidDataException("producer-owner-missing");object value=read(0x1305,tuple);if(!(value is FactionData))throw new InvalidDataException("producer-owner-shape");return (FactionData)value;}
        public static bool PublishedMatches(Array visible,Array model,Func<object,int> item,Func<object,int> count)
        {
            if(visible==null||model==null||visible.Length!=model.Length||visible.Length>64)return false;
            for(int i=0;i<visible.Length;i++){object a=visible.GetValue(i),b=model.GetValue(i);if(a==null||b==null){if(a!=b)return false;continue;}int n=count(a);if(n<0||n!=count(b)||n>0&&item(a)!=item(b))return false;}return true;
        }
        public static List<LedgerProductionOutput> ReadPublishedOutputs(Array published,Func<object,int> item,Func<object,int> count,Func<int,string> name)
        {
            if(published==null||published.Length>64)throw new InvalidDataException("producer-output-bound");var result=new List<LedgerProductionOutput>();
            foreach(var slot in published){if(slot==null)continue;int n=count(slot);if(n<0)throw new InvalidDataException("producer-negative-output");if(n==0)continue;int id=item(slot);if(id<=0)throw new InvalidDataException("producer-output-item");var found=result.FirstOrDefault(x=>x.Item==id);if(found==null){if(result.Count>=2)throw new InvalidDataException("producer-output-types");found=new LedgerProductionOutput{Item=id,Name=name(id)};result.Add(found);}found.Stock+=n;}return result;
        }
        public static long? KnownCapacity(int native){return native>0?(long?)native:null;}
        // Native C2CC uses separately truncated reservoir and the current published UI countdown.
        // This is fuel validation only. Production phase/elapsed time always use GameTicks/52E7.
        public static int? RenderedFuelSeconds(int reservoirTicks,int supplySeconds,float uiCycleSeconds)
        {
            if(reservoirTicks<0||supplySeconds<0||float.IsNaN(uiCycleSeconds)||float.IsInfinity(uiCycleSeconds)||uiCycleSeconds>int.MaxValue)return null;
            long seconds=reservoirTicks/20+(long)supplySeconds+(uiCycleSeconds>0?(long)uiCycleSeconds:0);
            return seconds<=int.MaxValue?(int?)seconds:null;
        }
        public static double? FuelAtObservation(LedgerProductionSnapshot p,out string basis)
        {
            basis="coherent native fuel";
            if(p.FuelTick==p.Tick&&p.FuelSeconds.HasValue)return p.FuelSeconds;
            // The native C2CD formatter floors hours/minutes. A valid SHOWN label therefore
            // supplies a conservative whole-minute bound, never a rebased older raw fuel value.
            basis="shown operating time, whole-minute lower bound";
            var m=Regex.Match(LedgerPresentation.Text(p.ShownOperating??"").Trim(),@"^(\d{1,6}):([0-5]\d)$");
            if(m.Success)return long.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)*3600d+int.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture)*60d;
            basis="operating time unknown";return null;
        }
        public static void PreserveFuel(LedgerProductionSnapshot next,LedgerProductionSnapshot old)
        {
            if(next.Key!=old.Key||next.Definition!=old.Definition||next.Tick<old.Tick||next.FuelSeconds.HasValue||!old.FuelSeconds.HasValue)return;
            next.FuelSeconds=old.FuelSeconds;next.FuelTick=old.FuelTick??old.Tick;next.FuelUtc=old.FuelUtc;
        }
        public static string ContentKey(LedgerProductionSnapshot p)
        {
            var c=LedgerJson.Decode<LedgerProductionSnapshot>(LedgerJson.Encode(p));c.Tick=0;c.DetailsTick=null;c.DetailsUtc=null;c.SizeTick=null;c.PercentageTick=null;
            // Countdown-only changes stay in the owned last-shown DTO and close flush, not disk writes every frame.
            if(c.Kind=="producer"){c.FuelSeconds=null;c.FuelTick=null;c.FuelUtc=null;c.NextTick=null;c.RawUi=null;c.ShownNext=null;c.ShownOperating=null;}
            return LedgerJson.Hash(LedgerJson.Encode(c));
        }
        public static string ShownPercentage(string raw)
        {
            if(string.IsNullOrEmpty(raw)||raw.Length>4096)return null;
            var matches=Regex.Matches(LedgerPresentation.Text(raw),@"(?<![\d.,+\-])(?:[<>]\s*)?\d{1,3}(?:[.,]\d+)?\s*%");
            return matches.Count==1?matches[0].Value:null;
        }
        public static string Key(LedgerContext c,LedgerProductionSnapshot p)
        {return c.WorldKey+"|"+p.Player+"|"+LedgerJson.Hash(c.Playfield)+"|"+p.Kind+"|"+(p.Kind=="deposit"?p.DepositId+"|"+p.Definition:p.ClrIdx+"|"+p.Block+"|"+p.Owner)+"|"+p.X.ToString("R",CultureInfo.InvariantCulture)+","+p.Y.ToString("R",CultureInfo.InvariantCulture)+","+p.Z.ToString("R",CultureInfo.InvariantCulture);}
        public static bool Valid(LedgerObservation o)
        {
            var p=o==null?null:o.Production;
            return p!=null&&o.Context!=null&&o.Context.Recordable&&o.Context.Mode=="SinglePlayer"&&p.Player>0&&p.Complete&&p.Key==Key(o.Context,p)&&
                (p.Kind=="deposit"&&p.DepositId>=0&&!string.IsNullOrEmpty(p.Resource)||p.Kind=="producer"&&!string.IsNullOrEmpty(p.Block)&&!string.IsNullOrEmpty(p.Owner))&&
                Finite(p.X)&&Finite(p.Y)&&Finite(p.Z)&&p.Outputs!=null&&p.Outputs.Count<=16&&p.Outputs.All(x=>x!=null&&x.Item>0&&x.Stock>=0&&x.Stock<=int.MaxValue&&(x.Name??"").Length<=512)&&
                (!p.Capacity.HasValue||p.Capacity>=0)&&(!p.FuelSeconds.HasValue||!double.IsNaN(p.FuelSeconds.Value)&&p.FuelSeconds>=0&&p.FuelSeconds<1e12)&&
                (p.RawUi??"").Length<=4096&&new[]{p.ShownOperating,p.ShownNext,p.ShownQuality,p.ShownStatus,p.DefinitionName}.All(s=>(s??"").Length<=512)&&(p.Device??"").Length<=512&&(p.Proof??"").Length<=2048&&(p.Key??"").Length<=512&&(p.Block??"").Length<=128&&(p.Owner??"").Length<=128;
        }
        private static bool Finite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v)&&Math.Abs(v)<1e9;}
        public static LedgerProductionEstimate Estimate(LedgerProductionSnapshot p,ulong now,string epoch)
        {
            var e=new LedgerProductionEstimate{State="Observed only"};foreach(var x in p.Outputs)e.Stocks[x.Item]=e.Stocks.ContainsKey(x.Item)?e.Stocks[x.Item]+x.Stock:x.Stock;
            string basis;double? fuel=FuelAtObservation(p,out basis);e.FuelBasis=basis;
            long total=p.Outputs.Sum(x=>x.Stock);e.Full=p.Capacity.HasValue&&total>=p.Capacity;e.NeedsFuel=fuel.HasValue&&fuel<=0;
            if(p.Kind!="producer")return e;
            if(now<p.Tick||epoch!=p.Epoch){e.State="Estimate unavailable: clock/save changed";return e;}
            if(e.Full){e.State="Observed full";e.WorkingSeconds=fuel;return e;}
            if(!p.Capacity.HasValue||!fuel.HasValue||!(p.Period>0)||p.Running!=true||p.Outputs.Count==0||p.NextItem<=0||!p.NextTick.HasValue||p.Outputs.GroupBy(x=>x.Item).Any(g=>g.Select(x=>x.Auxiliary).Distinct().Count()>1))
            {e.State="Estimate unavailable: capacity, fuel, cycle or operation unknown";return e;}
            if(now-p.Tick>uint.MaxValue||p.NextTick<p.Tick){e.State="Estimate unavailable: cycle discontinuity";return e;}
            double seconds=(now-p.Tick)/20.0,work=Math.Min(seconds,fuel.Value);
            ulong budget=(ulong)Math.Floor(work*20);ulong first=p.NextTick.Value-p.Tick;
            long amount=work<=0||budget<first?0:1+(long)((budget-first)/p.Period.Value);amount=Math.Min(amount,Math.Max(0,p.Capacity.Value-total));
            var ids=p.Outputs.Select(x=>x.Item).Distinct().ToArray();
            if(p.Alternating&&ids.Length>2||!p.Alternating&&ids.Length!=1||!ids.Contains(p.NextItem)){e.State="Estimate unavailable: output sequence unknown";return e;}
            if(p.Alternating&&ids.Length==2){e.Stocks[p.NextItem]+=(amount+1)/2;e.Stocks[ids.First(x=>x!=p.NextItem)]+=amount/2;}else e.Stocks[p.NextItem]+=amount;
            e.Full=total+amount>=p.Capacity;e.WorkingSeconds=e.Full?(double?)null:Math.Max(0,fuel.Value-work);e.NeedsFuel=e.WorkingSeconds<=0;
            e.State="GAME-time projection (unchanged operation; sleep/unloaded/depletion unverified)";return e;
        }
        public static LedgerBookmarkPlan Bookmark(LedgerObservation o,LedgerContext current)
        {
            if(!Valid(o)||current==null||!current.Recordable||current.Mode!="SinglePlayer"||o.Context.WorldKey!=current.WorldKey||!NavigationKnown(o.Production))return null;
            var p=o.Production;return new LedgerBookmarkPlan{World=current.WorldKey,Playfield=o.Context.Playfield,TargetKey=p.Key,TargetKind=p.Kind,
                X=p.X,Y=p.Y,Z=p.Z,PositionSource=p.PositionSource,Block=p.Block,Label="Visit "+LedgerProductionDisplay.Name(p),Place=o.Context.Playfield,Vendor=LedgerProductionDisplay.Name(p),
                OfferKey=p.Key,ColorHex="80E5FF",ObservedUtc=o.Utc,RequestBody=null};
        }
        public static bool NavigationKnown(LedgerProductionSnapshot p)
        {
            // Native Map marker ctor377D passes StoreQueue.5047 through Vector3i.7D9D
            // directly to MemoryDictionary.3709. Historical exact-source observations need no rewrite.
            return p.NavigationPosition||p.Kind=="deposit"&&p.DepositId>=0&&p.PositionSource!=null&&p.PositionSource.StartsWith("StoreQueue.5047 native deposit coordinates",StringComparison.Ordinal);
        }
        public static List<LedgerObservation> Read(SqliteConnection db,SqliteTransaction tx,string world,int? player)
        {
            var values=new List<LedgerObservation>();using(var cmd=db.CreateCommand())
            {cmd.Transaction=tx;cmd.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='production_latest_v1'";if(Convert.ToInt32(cmd.ExecuteScalar())==0)return values;
                cmd.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='production_inactive_v1'";bool lifecycle=Convert.ToInt32(cmd.ExecuteScalar())>0;
                cmd.CommandText="SELECT p.capture_json FROM production_latest_v1 p WHERE world_key=@w AND player_id=@p "+(lifecycle?"AND NOT EXISTS(SELECT 1 FROM production_inactive_v1 s WHERE s.record_key=p.record_key) ":"")+"ORDER BY observed_utc DESC LIMIT 1025";cmd.Parameters.AddWithValue("@w",world);cmd.Parameters.AddWithValue("@p",player??-1);
                using(var r=cmd.ExecuteReader())while(r.Read()){if(values.Count>=1024)throw new InvalidDataException("Production view limit1024");byte[] b=(byte[])r[0];if(b.Length>65536)throw new InvalidDataException("Production capture bound");var o=LedgerJson.Decode<LedgerObservation>(b);if(!Valid(o)||o.Context.WorldKey!=world||o.Production.Player!=player)throw new InvalidDataException("Production scope mismatch");values.Add(o);}}
            return values;
        }
    }
    public sealed partial class LedgerDatabase
    {
        private string SaveProduction(LedgerObservation o)
        {
            if(!LedgerProduction.Valid(o))return "identity-unknown";if(LedgerJson.Encode(o).Length>65536)throw new InvalidDataException("Production capture size");
            DateTime at;if(!DateTime.TryParse(o.Utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out at)||at.Kind!=DateTimeKind.Utc)throw new InvalidDataException("Production UTC");
            try{using(transaction=connection.BeginTransaction()){
                Exec("CREATE TABLE IF NOT EXISTS production_latest_v1(record_key TEXT PRIMARY KEY,world_key TEXT NOT NULL,player_id INTEGER NOT NULL,observed_utc TEXT NOT NULL,capture_json BLOB NOT NULL)");
                ProductionLifecycleSchema();bool revived=false;
                if(o.Production.Kind=="producer"){
                    string retired=Scalar("SELECT retired_utc FROM production_inactive_v1 WHERE record_key=@p0",o.Production.Key) as string;
                    // Only an independently begun normal UI visit can undo Forget/removal.
                    revived=retired!=null;
                    if(retired!=null&&(o.Production.VisitUtc==null||string.CompareOrdinal(o.Production.VisitUtc,retired)<=0))return "stale";
                    foreach(var replaced in LedgerProduction.Read(connection,transaction,o.Context.WorldKey,o.Production.Player).Where(x=>x.Production.Kind=="producer"&&x.Production.Key!=o.Production.Key&&x.Context.Playfield==o.Context.Playfield&&x.Production.Family=="AutoMiner"&&o.Production.Family=="AutoMiner"&&!string.IsNullOrEmpty(o.Production.DepositKey)&&x.Production.DepositKey==o.Production.DepositKey)){
                        if(string.CompareOrdinal(replaced.Utc,o.Utc)>0)return "stale";
                        RetireProduction(replaced,o.Utc,"new observed miner at same directly linked deposit",o.ObservationId);
                    }
                    Exec("DELETE FROM production_inactive_v1 WHERE record_key=@p0",o.Production.Key);
                }
                byte[] prior=Scalar("SELECT capture_json FROM production_latest_v1 WHERE record_key=@p0",o.Production.Key) as byte[];
                if(prior!=null){var old=LedgerJson.Decode<LedgerObservation>(prior);if(string.CompareOrdinal(old.Utc,o.Utc)>0)return "stale";
                    if(!revived)LedgerProduction.PreserveFuel(o.Production,old.Production);
                    LedgerDepositDetails.Preserve(o.Production,old.Production);
                    if(!revived&&old.Production.Definition==o.Production.Definition&&o.Production.CollectionUtc==null)o.Production.CollectionUtc=old.Production.CollectionUtc;
                    if(o.Production.VisitUtc==null)o.Production.VisitUtc=old.Production.VisitUtc;
                    if(old.Production.Definition!=o.Production.Definition||old.Production.Tick>o.Production.Tick)o.Production.Change="Definition/clock changed; baseline reset; replacement/upgrade continuity unverified";
                    else if(old.Production.Outputs.Sum(x=>x.Stock)>o.Production.Outputs.Sum(x=>x.Stock))o.Production.Change="Observed stock decrease; successful collection not independently evidenced";
                }
                Exec("INSERT OR REPLACE INTO production_latest_v1 VALUES(@p0,@p1,@p2,@p3,@p4)",o.Production.Key,o.Context.WorldKey,o.Production.Player,o.Utc,LedgerJson.Encode(o));Inject("production-before-commit");transaction.Commit();return "committed";
            }}finally{transaction=null;}
        }
    }
}
