using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeLedgerProbeB
{
    public sealed class LedgerProductionDueValue
    {
        public double? Seconds;
        public double? StorageSeconds,FuelSeconds;
        public bool Partial;
        public string Reason;
        public string Text {get{return !Seconds.HasValue?"unknown":Seconds<=0?"Now":(Partial?"\u2264":"")+LedgerProductionDisplay.Duration(Seconds<60?Math.Ceiling(Seconds.Value):Partial?Math.Ceiling(Seconds.Value/60)*60:Seconds);}}
        public string Hint {get{return !Seconds.HasValue?"":Reason!=null&&Reason.StartsWith("Storage and operating",StringComparison.Ordinal)?"Storage and fuel":Reason!=null&&Reason.IndexOf("storage",StringComparison.OrdinalIgnoreCase)>=0&&Reason.IndexOf("; storage",StringComparison.OrdinalIgnoreCase)<0?"Storage":"Fuel";}}
    }
    public sealed class LedgerProductionDueScope
    {
        public int Devices,Bounded,Partial;
        public LedgerObservation Limiting;
        public LedgerProductionDueValue Earliest;
        public string Text {get{return "Service in "+(Devices==0?"\u2014":Earliest==null?"unknown":
            new LedgerProductionDueValue{Seconds=Earliest.Seconds,Partial=Bounded<Devices||Partial>0}.Text)+
            (Devices>0&&(Bounded<Devices||Partial>0)?" ("+Bounded+"/"+Devices+" bounded)":"");}}
        public string Hint {get{return Limiting==null?"":LedgerProductionDisplay.DeviceName(Limiting.Production)+" / "+Limiting.Context.Playfield+": "+Earliest.Hint;}}
    }
    // Shared first-blocking limit for display and service lead-time alerts. Never changes observations.
    public static class LedgerProductionDue
    {
        private static bool Number(double? value){return value.HasValue&&!double.IsNaN(value.Value)&&!double.IsInfinity(value.Value);}
        public static double? BufferSeconds(long? capacity,long? stock,double? unitsPerHour)
        {
            if(!capacity.HasValue||capacity<=0||!stock.HasValue||stock<0)return null;
            if(stock>=capacity)return 0;
            if(!Number(unitsPerHour)||unitsPerHour<=0)return null;
            double seconds=(capacity.Value-stock.Value)/unitsPerHour.Value*3600;
            return Number(seconds)?(double?)seconds:null;
        }
        public static LedgerProductionDueValue Limits(double? storage,double? fuel,bool fuelRequired=true)
        {
            if(!Number(storage))storage=null;if(!Number(fuel))fuel=null;
            double? seconds=storage.HasValue&&fuelRequired&&fuel.HasValue?Math.Min(storage.Value,fuel.Value):storage??(fuelRequired?fuel:null);
            string reason=storage.HasValue&&(!fuelRequired||!fuel.HasValue||storage<fuel)?"Storage limit":fuel.HasValue&&(!storage.HasValue||fuel<storage)?"Operating/fuel limit":"Storage and operating/fuel limits coincide";
            return new LedgerProductionDueValue{Seconds=seconds.HasValue?(double?)Math.Max(0,seconds.Value):null,
                StorageSeconds=storage.HasValue?(double?)Math.Max(0,storage.Value):null,FuelSeconds=fuelRequired&&fuel.HasValue?(double?)Math.Max(0,fuel.Value):null,
                Partial=!storage.HasValue||fuelRequired&&!fuel.HasValue,
                Reason=!seconds.HasValue?"Storage and operating limits unknown":reason+(!storage.HasValue?"; storage limit unknown":fuelRequired&&!fuel.HasValue?"; operating limit unknown":"")};
        }
        public static LedgerProductionDueValue Evaluate(LedgerProductionSnapshot p,ulong now,string epoch)
        {
            var unknown=new LedgerProductionDueValue{Reason="Service timing unknown"};
            if(p==null||!p.Complete||p.Kind!="producer"||p.Outputs==null||p.Family!="AutoMiner"&&p.Family!="Generator mode 0"&&p.Family!="Generator mode 1"&&p.Family!="Generator mode 2"&&p.Family!="Generator mode 3")return unknown;
            if(now<p.Tick||string.IsNullOrEmpty(epoch)||epoch!=p.Epoch){unknown.Reason="Game clock/save differs from the observation";return unknown;}
            long stock=p.Outputs.Sum(x=>x.Stock);
            bool knownStock=p.Outputs.Count>0&&p.Outputs.All(x=>x.Stock>=0);
            if(knownStock&&p.Capacity>0&&stock>=p.Capacity)return new LedgerProductionDueValue{Seconds=0,StorageSeconds=0,Reason="Last shown storage is full"};
            string basis;double? fuel=LedgerProduction.FuelAtObservation(p,out basis);
            if(!Number(fuel)||fuel<0)fuel=null;
            bool exactFuel=fuel.HasValue&&p.FuelTick==p.Tick&&p.FuelSeconds.HasValue;
            if(exactFuel&&fuel==0)return new LedgerProductionDueValue{Seconds=0,FuelSeconds=0,Reason="Last shown operating fuel is empty"};
            if(p.Running!=true){unknown.Reason=p.Running==false?"Not operating at the last observation":"Operating state unknown";return unknown;}
            if(p.Period==0||p.Period==uint.MaxValue){unknown.Reason="No supported positive production rate";return unknown;}
            bool phase=p.Period>0&&p.NextTick.HasValue&&p.NextTick>=p.Tick&&now-p.Tick<=uint.MaxValue;
            if(p.NextTick.HasValue&&p.NextTick<p.Tick){unknown.Reason="Production cycle discontinuity";return unknown;}
            // Without a valid cycle, only fuel actually shown at this very baseline is usable.
            // Never silently advance fuel or production with a missing/zero rate.
            double elapsed=(now-p.Tick)/20.0;
            double? fuelBound=fuel.HasValue&&(phase||now==p.Tick)?(double?)(fuel.Value+(exactFuel?0:60)-elapsed):null;
            double? fuelLower=fuelBound.HasValue?(double?)(fuel.Value-elapsed):null;
            double? storage=null;
            var ids=p.Outputs.Select(x=>x.Item).Distinct().ToArray();
            bool sequence=knownStock&&p.NextItem>0&&ids.Contains(p.NextItem)&&
                (p.Alternating?ids.Length<=2:ids.Length==1)&&
                !p.Outputs.GroupBy(x=>x.Item).Any(g=>g.Select(x=>x.Auxiliary).Distinct().Count()>1);
            if(phase&&sequence&&p.Capacity>stock&&p.Capacity>0)
            {
                try{
                    // 581A: one UNIT per cycle, alternating products share the SAME capacity.
                    // Use the baseline phase once; do not subtract elapsed again from projected stock.
                    ulong ticks=checked(p.NextTick.Value-p.Tick+checked((ulong)(p.Capacity.Value-stock-1)*p.Period.Value));
                    if(ticks==0)ticks=1; // Baseline shown stock wins over a cycle still due at that exact tick.
                    // Individual native period defines the actual rate; the shown hourly rate
                    // may be rounded. Apply this device's phase once, never elapsed twice.
                    var buffer=BufferSeconds(p.Capacity,stock,72000d/p.Period.Value);
                    storage=ticks==1&&p.NextTick==p.Tick?1/20d-elapsed:
                        buffer+(p.NextTick.Value-p.Tick)/20d-p.Period.Value/20d-elapsed;
                }catch(OverflowException){/* Unknown upper horizon, never wrap into Now. */}
            }
            var value=Limits(storage,fuelBound);
            // h:mm is floored by native C2CD. Its upper bound is the NEXT minute, not an
            // exact empty-fuel deadline. Storage before its lower bound is still exact.
            if(fuelBound.HasValue&&!exactFuel&&!(storage.HasValue&&storage<=fuelLower)){
                value.Partial=true;value.Reason+="; shown operating time is rounded to whole minutes";
            }
            return value;
        }
        public static LedgerProductionDueScope Scope(IEnumerable<LedgerObservation> rows,ulong now,string epoch)
        {
            var scope=new LedgerProductionDueScope();
            foreach(var o in rows.Where(o=>o.Production.Kind=="producer").GroupBy(o=>o.Production.Key).Select(g=>g.First()).OrderBy(o=>o.Production.Key,StringComparer.Ordinal)){
                scope.Devices++;var value=Evaluate(o.Production,now,epoch);
                if(!value.Seconds.HasValue)continue;scope.Bounded++;if(value.Partial)scope.Partial++;
                if(scope.Earliest==null||value.Seconds<scope.Earliest.Seconds){scope.Earliest=value;scope.Limiting=o;}
            }
            return scope;
        }
        public static List<LedgerObservation> Sort(IEnumerable<LedgerObservation> rows,LedgerHeaderSort sort,ulong now,string epoch)
        {
            if(sort.Column!=7)return rows.ToList();
            var scored=rows.Select(o=>new{Row=o,Value=Evaluate(o.Production,now,epoch)});
            var known=scored.OrderBy(o=>o.Value.Seconds.HasValue?0:1);
            // Finite values and partial upper bounds use their numeric time; exact before partial
            // on a tie. Unknown stays LAST in both directions. Default preserves existing order.
            return (sort.Descending?known.ThenByDescending(o=>o.Value.Seconds):known.ThenBy(o=>o.Value.Seconds))
                .ThenBy(o=>o.Value.Partial?1:0).ThenBy(o=>o.Row.Production.Key,StringComparer.Ordinal).Select(o=>o.Row).ToList();
        }
    }
}
