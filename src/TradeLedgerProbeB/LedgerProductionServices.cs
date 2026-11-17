using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    // Optional portable marker metadata. Missing fields in older documents are normalized, not reset.
    public sealed class LedgerServiceOverride
    {public string Key;public double? Fill,Hours;public bool Suppress;} // Fill is inert legacy round-trip data, never an alert threshold.
    public sealed class LedgerServicePreferences
    {
        // Non-attributed JSON construction applies this default; an explicit saved false wins.
        public string World;public int Player;public bool Enabled=true;
        public double Fill=75,Hours=5; // Retain old Fill bytes logically; only Hours is effective for both downtime causes.
        public List<LedgerServiceOverride> Overrides=new List<LedgerServiceOverride>();
        public List<string> Deleted=new List<string>();
        public LedgerServiceOverride For(string key,bool create=false)
        {var found=Overrides.FirstOrDefault(x=>x.Key==key);if(found==null&&create){if(Overrides.Count>=512)throw new InvalidOperationException("Service place limit512");found=new LedgerServiceOverride{Key=key};Overrides.Add(found);}return found;}
        public LedgerServiceThreshold Effective(IEnumerable<string> ancestry,string group=null)
        {
            var result=new LedgerServiceThreshold{Hours=Hours};
            foreach(string key in ancestry.Concat(group==null?new string[0]:new[]{group}).Distinct()){
                var item=For(key);if(item==null)continue;
                if(item.Hours.HasValue)result.Hours=item.Hours.Value;
                result.Suppressed|=item.Suppress;
            }return result;
        }
        public static bool Number(string text,bool percent,out double value)
        {return double.TryParse((text??"").Trim().Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value)&&!double.IsNaN(value)&&!double.IsInfinity(value)&&value>=0&&value<=(percent?100:1000000);}
        public static void Validate(LedgerServicePreferences p)
        {
            if(p==null||string.IsNullOrEmpty(p.World)||p.Player<=0||!Range(p.Fill,true)||!Range(p.Hours,false)||p.Overrides==null||p.Deleted==null||p.Overrides.Count>512||p.Deleted.Count>1024||p.Overrides.Any(x=>x==null||string.IsNullOrEmpty(x.Key)||x.Key.Length>1024||x.Fill.HasValue&&!Range(x.Fill.Value,true)||x.Hours.HasValue&&!Range(x.Hours.Value,false))||p.Overrides.Select(x=>x.Key).Distinct().Count()!=p.Overrides.Count||p.Deleted.Any(x=>x==null||x.Length>1024))throw new InvalidOperationException("Invalid service preferences; original preserved");
        }
        private static bool Range(double n,bool percent){return !double.IsNaN(n)&&!double.IsInfinity(n)&&n>=0&&n<=(percent?100:1000000);}
    }
    public sealed class LedgerServiceThreshold {public double Hours;public bool Suppressed;}
    public sealed class LedgerServiceState
    {
        public LedgerProductionEstimate Estimate;public double? Fill,Seconds;
        public LedgerProductionDueValue Due;
        public bool Offline,ClockValid;
        public bool BufferAlert(LedgerServiceThreshold t){return Due!=null&&Due.StorageSeconds.HasValue&&Due.StorageSeconds<t.Hours*3600;}
        public bool FuelAlert(LedgerServiceThreshold t){return Due!=null&&Due.FuelSeconds.HasValue&&Due.FuelSeconds<t.Hours*3600;}
        public bool Alert(LedgerServiceThreshold t){return Due!=null&&Due.Seconds.HasValue&&Due.Seconds<t.Hours*3600;}
    }
    public sealed class LedgerServiceResource {public int Item;public int? Auxiliary;public string Name;public long Stock;public int Devices,KnownRates;public double Rate;
        public string RateText {get{return Devices>0&&KnownRates==0?"rate unknown":Rate.ToString("0.#",CultureInfo.InvariantCulture)+"/h"+(KnownRates<Devices?" + ?":"");}}
    }
    public sealed class LedgerServiceOverview
    {
        public int Devices,KnownFill,ReserveKnown,ReserveLow,ReserveUnknown;public double? MeanFill;
        public List<LedgerServiceResource> Resources=new List<LedgerServiceResource>();
        public string FillText {get{return "Fill "+(MeanFill.HasValue?MeanFill.Value.ToString("0.#",CultureInfo.InvariantCulture)+"%":"unknown")+" ("+KnownFill+"/"+Devices+")";}}
        public string ReserveText {get{return "Service reserve: "+ReserveKnown+" sufficient · "+ReserveLow+" soon · "+ReserveUnknown+" unknown";}}
    }
    // Scheduling uses wall time only to limit work. All quantities/durations are derived from GAME ticks.
    public sealed class LedgerServiceSchedule
    {
        private long last;private ulong tick;private bool initialized;
        public bool Due(long milliseconds,ulong gameTick,bool dirty)
        {if(!initialized||dirty||milliseconds-last>=60000||gameTick<tick||gameTick-tick>=1200){initialized=true;last=milliseconds;tick=gameTick;return true;}return false;}
    }
    public static class LedgerProductionServices
    {
        public const string Color="C9A1FF";
        public static LedgerServiceState State(LedgerProductionSnapshot p,ulong now,string epoch)
        {
            var e=LedgerProduction.Estimate(p,now,epoch);var s=new LedgerServiceState{Estimate=e,Due=LedgerProductionDue.Evaluate(p,now,epoch),ClockValid=now>=p.Tick&&epoch==p.Epoch};
            // 581A sums BOTH published output stacks before comparing shared capacity52E4.
            if(p.Capacity>0&&p.Outputs.Count>0)s.Fill=Math.Min(100,100d*e.Stocks.Values.Sum()/p.Capacity.Value);
            string basis;double? fuel=LedgerProduction.FuelAtObservation(p,out basis);
            if(s.ClockValid){
                if(e.State.StartsWith("GAME-time",StringComparison.Ordinal)&&!e.Full)s.Seconds=e.WorkingSeconds;
                // 5819/581A return before consuming fuel when already full or switched off.
                else if(now==p.Tick||p.Running==false||p.Capacity>0&&p.Outputs.Sum(x=>x.Stock)>=p.Capacity)s.Seconds=fuel;
                s.Offline=p.Running==false||s.Seconds==0&&(p.FuelTick==p.Tick&&p.FuelSeconds==0||e.State.StartsWith("GAME-time",StringComparison.Ordinal)&&fuel>0);
            }
            // An older/future baseline cannot trigger a new alert or certify fuel reserve.
            else{s.Fill=null;s.Seconds=null;}
            return s;
        }
        public static List<LedgerObservation> ReadActive(LedgerViewData data,string world,int player)
        {
            if(data==null||data.WorldKey!=world||data.ProductionReadError!=null||data.Production==null||data.Production.Any(o=>!LedgerProduction.Valid(o)||o.Context.WorldKey!=world||o.Production.Player!=player)||data.Production.Select(o=>o.Production.Key).Distinct().Count()!=data.Production.Count)throw new System.IO.InvalidDataException("Producer snapshot unavailable/inconsistent; previous service records retained");
            return data.Production.Where(o=>o.Production.Kind=="producer").ToList();
        }
        public static string Prefix(LedgerProductionSnapshot p)
        {return p.Family=="AutoMiner"?"Autominer":LedgerPresentation.Text(p.Device??"Producer");}
        public static string GroupKey(LedgerObservation o)
        {return "service:"+LedgerJson.Hash(o.Context.Playfield)+":"+LedgerJson.Hash(o.Production.Family=="AutoMiner"?"AutoMiner":o.Production.DefinitionName??o.Production.Family+":"+o.Production.Definition);}
        public static string GroupName(LedgerObservation o){return o.Context.Playfield+" · "+(o.Production.Family=="AutoMiner"?"Autominers":Prefix(o.Production));}
        public static LedgerServicePreferences Preferences(LedgerMarkerModel model,string world,int player,bool create=true)
        {var found=model.Data.Services.FirstOrDefault(x=>x.World==world&&x.Player==player);if(found==null&&create){if(model.Data.Services.Count>=64)throw new InvalidOperationException("Service scope limit64");found=new LedgerServicePreferences{World=world,Player=player};model.Data.Services.Add(found);model.Revision++;}return found;}
        public static LedgerServiceThreshold Threshold(LedgerServicePreferences preferences,LedgerProductionPlaces places,LedgerObservation o)
        {return preferences.Effective(places.Ancestry("playfield:"+o.Context.Playfield),GroupKey(o));}
        public static LedgerServiceOverview Overview(IEnumerable<LedgerObservation> observations,ulong now,string epoch,LedgerServicePreferences preferences,LedgerProductionPlaces places)
        {
            var result=new LedgerServiceOverview();double fill=0;var resources=new Dictionary<string,LedgerServiceResource>();
            foreach(var o in observations.Where(o=>o.Production.Kind=="producer")){
                var s=State(o.Production,now,epoch);result.Devices++;
                if(s.Fill.HasValue){result.KnownFill++;fill+=s.Fill.Value;}
                var threshold=Threshold(preferences,places,o);
                if(s.Alert(threshold))result.ReserveLow++;else if(!s.Due.Seconds.HasValue||s.Due.Partial)result.ReserveUnknown++;else result.ReserveKnown++;
                var rate=LedgerProductionDisplay.ProductRate(o.Production);
                foreach(var x in o.Production.Outputs.GroupBy(x=>new{x.Item,x.Auxiliary}).Select(g=>new LedgerProductionOutput{Item=g.Key.Item,Auxiliary=g.Key.Auxiliary,Name=g.First().Name,Stock=g.Sum(v=>v.Stock)})){
                    string key=LedgerDeviceSelection.Resource(x.Item,x.Auxiliary);LedgerServiceResource found;if(!resources.TryGetValue(key,out found)){found=new LedgerServiceResource{Item=x.Item,Auxiliary=x.Auxiliary,Name=LedgerPresentation.Text(x.Name)};resources.Add(key,found);result.Resources.Add(found);}
                    found.Devices++;if(rate.HasValue){found.KnownRates++;found.Rate+=rate.Value;}
                    found.Stock+=s.Estimate.State.StartsWith("GAME-time",StringComparison.Ordinal)?s.Estimate.Stocks[x.Item]:x.Stock;
                }
            }
            result.MeanFill=result.KnownFill>0?(double?)(fill/result.KnownFill):null;
            result.Resources=result.Resources.OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ThenBy(x=>x.Item).ToList();return result;
        }
        public static string Label(LedgerObservation o,LedgerServiceState s)
        {
            string resource=o.Production.Family=="AutoMiner"?LedgerPresentation.Text(o.Production.Resource??o.Production.Outputs.FirstOrDefault()?.Name??"Ore"):string.Join(" / ",o.Production.Outputs.Select(x=>LedgerPresentation.Text(x.Name)));
            string fill=s.Fill.HasValue?DisplayedFill(s.Fill)+"%":"fill unknown";
            return "["+Prefix(o.Production)+"] "+o.Context.Playfield+": "+resource+" "+fill+" ("+LedgerProductionDisplay.Duration(s.Seconds).Replace("h 0m","h")+")"+(s.Offline?" [c][FF7777]OFFLINE[-][/c]":"");
        }
        public static string DisplayedFill(double? fill)
        {return fill.HasValue?fill.Value.ToString("0",CultureInfo.InvariantCulture):"unknown";}
        private static string ShownKey(LedgerObservation o)
        {return LedgerJson.Hash((o.ObservationId??"")+"|"+(o.Utc??"")+"|"+LedgerJson.Hash(LedgerJson.Encode(o.Production)));}
        private static bool Serviced(LedgerObservation o,LedgerServiceThreshold threshold)
        {
            var p=o.Production;var shown=State(p,p.Tick,p.Epoch);
            return p.Epoch==o.Context.WorldKey&&shown.ClockValid&&DisplayedFill(shown.Fill)=="0"&&shown.Seconds>0&&shown.Seconds.Value>=threshold.Hours*3600;
        }
        public static void Evaluate(LedgerMarkerModel model,IEnumerable<LedgerObservation> active,LedgerContext current,int player,ulong now,bool markScope=false,LedgerProductionPlaces places=null,string scope="all",ISet<string> markMembers=null)
        {
            // The authoritative full cache remains the retirement input. A filtered command
            // selects explicit members only; it must never make hidden devices look retired.
            if(markScope&&markMembers!=null&&markMembers.Count==0)return;
            var rows=active.Where(o=>LedgerProduction.Valid(o)&&o.Production.Kind=="producer"&&o.Production.Player==player&&o.Context.WorldKey==current.WorldKey).ToList();
            places=places??new LedgerProductionPlaces(current.WorldKey,rows.Select(x=>x.Context));
            var prefs=Preferences(model,current.WorldKey,player);var keys=new HashSet<string>(rows.Select(o=>o.Production.Key));
            foreach(var d in model.Data.Destinations.Where(d=>d.World==current.WorldKey&&d.Player==player))foreach(var purpose in d.Purposes.Where(p=>p.Service)){
                bool retired=!keys.Contains(d.TargetKey);if(purpose.ServiceRetired!=retired){purpose.ServiceRetired=retired;model.Revision++;}
            }
            foreach(var o in rows){
                bool explicitMark=markScope&&places.Contains(scope,o)&&(markMembers==null||markMembers.Contains(o.Production.Key));var plan=LedgerProduction.Bookmark(o,current);if(plan==null)continue;
                string key=LedgerMarkerModel.DestinationKey(plan,player);
                if(explicitMark&&prefs.Deleted.Remove(key))model.Revision++;
                if(prefs.Deleted.Contains(key))continue;
                var d=model.Find(plan,player);var purpose=d==null?null:d.Purposes.FirstOrDefault(p=>p.Key==LedgerMarkerModel.PurposeKey(plan));
                var state=State(o.Production,now,current.WorldKey);var t=Threshold(prefs,places,o);bool alert=prefs.Enabled&&state.Alert(t)&&!t.Suppressed;
                string groupKey=GroupKey(o);var route=model.Data.Routes.FirstOrDefault(r=>r.World==current.WorldKey&&r.Player==player&&r.ServiceKey==groupKey);
                if(purpose==null&&!explicitMark&&!alert&&!(route!=null&&route.Shown))continue;
                bool priorManual=purpose!=null&&!purpose.Service&&model.Active(purpose);
                // Existing relevance/threshold policy survives native disappearance; shared reconciliation recreates only an active projection.
                if(purpose==null||explicitMark){d=model.Ensure(plan,player,d==null||explicitMark);purpose=d.Purposes.Single(p=>p.Key==LedgerMarkerModel.PurposeKey(plan));}
                if(!purpose.Service){purpose.Service=true;purpose.UngroupedShown=false;purpose.Visibility=priorManual?1:0;model.Revision++;}
                if(route==null){route=model.AddRoute(GroupName(o),current.WorldKey,player);route.ServiceKey=groupKey;route.Shown=explicitMark&&markMembers==null||priorManual;}
                foreach(var old in model.Data.Routes.Where(r=>r.World==current.WorldKey&&r.Player==player&&r.ServiceKey!=null&&r.ServiceKey!=groupKey&&purpose.Routes.Contains(r.Id)).ToArray())model.Membership(purpose,old.Id,false);
                if(!purpose.Routes.Contains(route.Id))model.Membership(purpose,route.Id,true);
                // Filtered Mark explicitly shows just these purposes, preserving every route's
                // manual visibility and unrelated members. The group eye remains group-wide.
                if(explicitMark){if(markMembers==null)model.ShowRoute(route,true);else purpose.Visibility=1;purpose.ServiceServiced=false;}
                string shownKey=ShownKey(o);
                // Only a NEW legitimately displayed baseline can complete a service run. Forecasts,
                // native approach/reconciliation and reloading the same baseline cannot do so.
                if(purpose.ServiceObservation!=shownKey){
                    bool fresh=purpose.ServiceObservation!=null;
                    purpose.ServiceObservation=shownKey;
                    if(fresh&&!explicitMark&&state.ClockValid&&Serviced(o,t)){purpose.ServiceServiced=true;purpose.Visibility=0;}
                    model.Revision++;
                }
                // A renewed real service condition re-arms a run; mere first-unit/rounded-zero
                // growth does not. Suppression controls automatic visibility, not this evidence.
                if(purpose.ServiceServiced&&state.Alert(t)){purpose.ServiceServiced=false;model.Revision++;}
                string label=Label(o,state);
                if(!d.Pinned||purpose.ServiceAutoVisible!=alert||purpose.ServiceRetired||purpose.Plan.Label!=label||purpose.Plan.ObservedUtc!=o.Utc||d.Vendor!=plan.Vendor){d.Vendor=plan.Vendor;purpose.Plan.Vendor=plan.Vendor;d.Pinned=true;purpose.ServiceAutoVisible=alert;purpose.ServiceRetired=false;purpose.Plan.Label=label;purpose.Plan.ColorHex=Color;purpose.Plan.ObservedUtc=o.Utc;model.Revision++;}
            }
        }
        public static List<LedgerObservation> SortDeposits(IEnumerable<LedgerObservation> source,LedgerHeaderSort sort,LedgerProductionSizeOrder sizes,IEnumerable<LedgerObservation> producers,ulong now,string epoch)
        {
            var rows=source.ToList();if(sort.Column<0)return rows;if(sort.Column==2)return sizes.Sort(rows,sort);
            if(sort.Column==0)return (sort.Descending?rows.OrderByDescending(o=>LedgerProductionDisplay.Name(o.Production),StringComparer.OrdinalIgnoreCase):rows.OrderBy(o=>LedgerProductionDisplay.Name(o.Production),StringComparer.OrdinalIgnoreCase)).ThenBy(o=>o.Production.Key,StringComparer.Ordinal).ToList();
            var linked=producers.Where(o=>o.Production.Kind=="producer"&&o.Production.Family=="AutoMiner").ToLookup(o=>o.Production.DepositKey);
            var ranked=rows.Select(o=>{var miners=LedgerProductionDisplay.LinkedMiners(o,linked[o.Production.Key]);return new{Row=o,Rank=sort.Column==3?(double?)(miners.Count>0?1:0):miners.Count==1?State(miners[0].Production,now,epoch).Fill:null};});
            var ordered=ranked.OrderBy(x=>x.Rank.HasValue?0:1);return (sort.Descending?ordered.ThenByDescending(x=>x.Rank):ordered.ThenBy(x=>x.Rank)).ThenBy(x=>x.Row.Production.Key,StringComparer.Ordinal).Select(x=>x.Row).ToList();
        }
    }
}
