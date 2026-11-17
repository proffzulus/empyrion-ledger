using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    // Plain action evidence only. No game objects, disappearance scans or destruction events.
    public sealed class LedgerProductionRetirement
    {
        public string Key,Block,Reason,ExpectedObservationId;
        public int Player,ClrIdx,OldDefinition,NewDefinition;
    }
    public static class LedgerProductionLifecycle
    {
        private static string ActionKey(LedgerContext c,int player,int clr,string block,int definition)
        {return LedgerJson.Hash(System.Text.Encoding.UTF8.GetBytes(c.LocalSaveKey+"|"+c.WorldKey+"|"+c.Playfield+"|"+player+"|"+clr+"|"+block+"|"+definition));}
        public static string PickupBeginning(LedgerContext c,int player,int clr,string block,int requested,int before)
        {return c!=null&&c.Recordable&&c.Mode=="SinglePlayer"&&player>0&&requested>0&&before==requested?ActionKey(c,player,clr,block,requested):null;}
        public static bool PickupCommitted(string proof,LedgerContext c,int player,int clr,string block,int requested,int after)
        {return proof!=null&&c!=null&&after==0&&proof==ActionKey(c,player,clr,block,requested);}
        public static bool Valid(LedgerObservation o)
        {
            var a=o==null?null:o.ProductionRetirement;
            return a!=null&&o.Context!=null&&o.Context.Recordable&&o.Context.Mode=="SinglePlayer"&&a.Player>0&&
                (a.Reason=="manual-forget"&&!string.IsNullOrEmpty(a.Key)&&!string.IsNullOrEmpty(a.ExpectedObservationId)||
                 (a.Reason=="player-pickup"||a.Reason=="player-upgrade")&&a.OldDefinition>0&&!string.IsNullOrEmpty(a.Block))&&
                (a.Key??"").Length<=512&&(a.Block??"").Length<=128&&(a.ExpectedObservationId??"").Length<=64;
        }
        public static LedgerObservation Forget(LedgerObservation shown,LedgerContext current)
        {
            if(!LedgerProduction.Valid(shown)||shown.Production.Kind!="producer"||current==null||!current.Recordable||
                current.Mode!="SinglePlayer"||current.WorldKey!=shown.Context.WorldKey)return null;
            return new LedgerObservation{Kind="production-lifecycle",Context=current,Identity=new LedgerIdentity(),Utc=DateTime.UtcNow.ToString("o"),Source="explicit journal-only Forget",
                ProductionRetirement=new LedgerProductionRetirement{Key=shown.Production.Key,Player=shown.Production.Player,ExpectedObservationId=shown.ObservationId,Reason="manual-forget"}};
        }
        public static bool Matches(LedgerObservation o,LedgerObservation command)
        {
            if(!Valid(command)||!LedgerProduction.Valid(o)||o.Production.Kind!="producer")return false;
            var a=command.ProductionRetirement;var p=o.Production;
            if(o.Context.WorldKey!=command.Context.WorldKey||p.Player!=a.Player)return false;
            if(a.Reason=="manual-forget")return a.Key==p.Key&&a.ExpectedObservationId==o.ObservationId;
            return o.Context.Playfield==command.Context.Playfield&&p.Owner=="player:"+a.Player.ToString(CultureInfo.InvariantCulture)&&p.ClrIdx==a.ClrIdx&&p.Block==a.Block&&p.Definition==a.OldDefinition;
        }
    }
    // Explicit committed local actions only; unload, missing scans and destruction are not inputs.
    public sealed class LedgerProductionActions
    {
        public LedgerObservation Accept(LedgerContext c,int player,int clr,string block,int oldDefinition,int newDefinition,bool pickup,bool committed,long now)
        {
            if(c==null||!c.Recordable||c.Mode!="SinglePlayer"||player<=0||oldDefinition<=0||string.IsNullOrEmpty(block)||!committed||oldDefinition==newDefinition||newDefinition==0&&!pickup)return null;
            return new LedgerObservation{Kind="production-lifecycle",Context=c,Identity=new LedgerIdentity(),Utc=DateTime.UtcNow.ToString("o"),Source=pickup?"native local pickup SaveOutline returned after committed terrain removal":"native committed player definition change",
                ProductionRetirement=new LedgerProductionRetirement{Player=player,ClrIdx=clr,Block=block,OldDefinition=oldDefinition,NewDefinition=newDefinition,Reason=pickup?"player-pickup":"player-upgrade"}};
        }
    }
    public sealed partial class LedgerDatabase
    {
        private void ProductionLifecycleSchema()
        {
            Exec("CREATE TABLE IF NOT EXISTS production_inactive_v1(record_key TEXT PRIMARY KEY,retired_utc TEXT NOT NULL,reason TEXT NOT NULL)");
            Exec("CREATE TABLE IF NOT EXISTS production_retirements_v1(retirement_id TEXT PRIMARY KEY,record_key TEXT NOT NULL,world_key TEXT NOT NULL,player_id INTEGER NOT NULL,retired_utc TEXT NOT NULL,reason TEXT NOT NULL,capture_json BLOB NOT NULL)");
        }
        private void RetireProduction(LedgerObservation old,string at,string reason,string id)
        {
            Exec("INSERT OR IGNORE INTO production_retirements_v1 VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",id+"|"+old.Production.Key,old.Production.Key,old.Context.WorldKey,old.Production.Player,at,reason,LedgerJson.Encode(old));
            Exec("INSERT OR REPLACE INTO production_inactive_v1 VALUES(@p0,@p1,@p2)",old.Production.Key,at,reason);
        }
        private string SaveProductionRetirement(LedgerObservation command)
        {
            if(!LedgerProductionLifecycle.Valid(command))return "identity-unknown";
            DateTime at;if(!DateTime.TryParse(command.Utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out at)||at.Kind!=DateTimeKind.Utc)throw new System.IO.InvalidDataException("production-retirement-UTC");
            try{using(transaction=connection.BeginTransaction()){
                ProductionLifecycleSchema();var rows=LedgerProduction.Read(connection,transaction,command.Context.WorldKey,command.ProductionRetirement.Player);
                var matched=rows.Where(o=>LedgerProductionLifecycle.Matches(o,command)&&string.CompareOrdinal(o.Utc,command.Utc)<=0).ToList();
                if(command.ProductionRetirement.Reason=="manual-forget"&&matched.Count==0&&Scalar("SELECT retired_utc FROM production_inactive_v1 WHERE record_key=@p0",command.ProductionRetirement.Key)==null)return "stale";
                foreach(var old in matched)RetireProduction(old,command.Utc,command.ProductionRetirement.Reason,command.ObservationId);
                Inject("production-retirement-before-commit");transaction.Commit();return "committed";
            }}finally{transaction=null;}
        }
    }
}
