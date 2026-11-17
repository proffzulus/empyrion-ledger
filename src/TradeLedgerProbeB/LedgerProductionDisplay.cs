using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TradeLedgerProbeB
{
    // Shared by the actual IMGUI view and non-game layout/record regressions.
    public sealed class LedgerProducerLine
    {public LedgerObservation Observation;public LedgerProductionOutput Output;public bool First;}
    public sealed class LedgerProductionSizeOrder
    {
        // Native five-band selector; English txtVerySmall/Small/Medium/Large/VeryLarge labels.
        private readonly Dictionary<string,int> labels=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> ambiguous=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public LedgerProductionSizeOrder(IEnumerable<string> localized=null){string[] english={"Tiny","Small","Medium","Large","Huge"};for(int i=0;i<english.Length;i++)labels[english[i]]=i;if(localized!=null){int i=0;foreach(string s in localized.Take(5)){if(!string.IsNullOrWhiteSpace(s)){int old;if(labels.TryGetValue(s,out old)&&old!=i){labels.Remove(s);ambiguous.Add(s);}else if(!ambiguous.Contains(s))labels[s]=i;}i++;}}}
        public int? Rank(string value){int rank;return value!=null&&labels.TryGetValue(LedgerPresentation.Text(value).Trim(),out rank)?(int?)rank:null;}
        public List<LedgerObservation> Sort(IEnumerable<LedgerObservation> rows,LedgerHeaderSort sort)
        {if(sort.Column!=2)return rows.ToList();var ranked=rows.Select(o=>new{Row=o,Rank=Rank(o.Production.Size)});var known=ranked.OrderBy(x=>x.Rank.HasValue?0:1);return (sort.Descending?known.ThenByDescending(x=>x.Rank):known.ThenBy(x=>x.Rank)).ThenBy(x=>x.Row.Production.Key,StringComparer.Ordinal).Select(x=>x.Row).ToList();}
    }
    // Resource selection is item/variant identity, never a localized-name substring.
    public static class LedgerDeviceSelection
    {
        public static string Resource(int item,int? auxiliary){return item.ToString(CultureInfo.InvariantCulture)+":"+(auxiliary.HasValue?auxiliary.Value.ToString(CultureInfo.InvariantCulture):"unknown");}
        public static string Toggle(string selected,string clicked){return selected==clicked?null:clicked;}
        public static List<LedgerObservation> Filter(IEnumerable<LedgerObservation> scoped,string search,string resource)
        {return LedgerProductionDisplay.Filter(scoped,false,null,search).Where(o=>resource==null||o.Production.Outputs.Any(p=>Resource(p.Item,p.Auxiliary)==resource)).ToList();}
        public static int Columns(float width){return Math.Max(1,Math.Min(6,(int)((width+6)/180)));}
        public static int Rows(int count,float width){return Math.Max(1,(count+Columns(width)-1)/Columns(width));}
        public const int CardPitch=50,CardHeight=46;
        public static float Height(int count,float width){return Rows(count,width)*CardPitch;}
        public static float[] Cell(int index,float width)
        {int columns=Columns(width);float cell=(width-6*(columns-1))/columns;return new[]{(index%columns)*(cell+6),(index/columns)*CardPitch,cell,(float)CardHeight};}
    }
    public static class LedgerProductionDisplay
    {
        public const int DepositRow=32,DeviceRow=34,HeaderHeight=207,FooterHeight=44;
        public static readonly float[] DepositColumns={.26f,.18f,.09f,.41f,.06f};
        public static readonly float[] DeviceColumns={.15f,.16f,.12f,.12f,.08f,.08f,.12f,.11f,.06f};
        public static string Empty(bool deposits,string error=null){return error!=null?"Production records could not be read. Try Refresh.":deposits?"No deposits match. Shown scanner details are recorded; native Map hover is also supported.":"No devices match. Normally open a personal miner, extractor or atmospheric condenser to record its shown state.";}
        public static string Name(LedgerProductionSnapshot p){return LedgerPresentation.Text(p.Kind=="deposit"?(p.Resource??p.Device):p.Device)??"unknown";}
        public static List<LedgerObservation> Filter(IEnumerable<LedgerObservation> all,bool deposits,string place,string search)
        {
            return all.Where(o=>o.Production.Kind==(deposits?"deposit":"producer")&&(place==null||o.Context.Playfield==place))
                .Where(o=>string.IsNullOrWhiteSpace(search)||LedgerPresentation.Text(Name(o.Production)+" "+o.Context.Playfield+" "+string.Join(" ",o.Production.Outputs.Select(v=>v.Name))).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0)
                .OrderBy(o=>o.Context.Playfield,StringComparer.OrdinalIgnoreCase).ThenBy(o=>Name(o.Production),StringComparer.OrdinalIgnoreCase).ThenBy(o=>o.Production.Key,StringComparer.Ordinal).ToList();
        }
        public static List<LedgerProducerLine> Lines(IEnumerable<LedgerObservation> observations)
        {
            var lines=new List<LedgerProducerLine>();foreach(var o in observations){if(o.Production.Outputs.Count==0)lines.Add(new LedgerProducerLine{Observation=o,First=true});else for(int i=0;i<o.Production.Outputs.Count;i++)lines.Add(new LedgerProducerLine{Observation=o,Output=o.Production.Outputs[i],First=i==0});}return lines;
        }
        public static string Duration(double? seconds)
        {
            if(!seconds.HasValue||seconds<0||double.IsNaN(seconds.Value)||double.IsInfinity(seconds.Value))return "unknown";
            long minutes=(long)Math.Floor(seconds.Value/60);return minutes>=60?minutes/60+"h "+minutes%60+"m":minutes>0?minutes+"m":((int)seconds.Value)+"s";
        }
        public static string Age(string utc,DateTime now)
        {
            DateTime seen;if(!DateTime.TryParse(utc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out seen))return "unknown";
            double seconds=(now.ToUniversalTime()-seen.ToUniversalTime()).TotalSeconds;
            if(seconds< -2)return "clock changed";if(seconds<60)return "just now";if(seconds<3600)return (int)(seconds/60)+"m ago";if(seconds<86400)return (int)(seconds/3600)+"h ago";return (int)(seconds/86400)+"d ago";
        }
        public static string Fuel(LedgerProductionSnapshot p)
        {string shown=LedgerPresentation.Text(p.ShownOperating??"");return shown.Length>0?shown+" h:mm":Duration(p.FuelSeconds);}
        public static string DepositName(LedgerProductionSnapshot p)
        {
            string literal=LedgerPresentation.Text(p.PercentageText??"").Trim();var m=Regex.Match(literal,@"^(?<op>[<>]?)\s*(?<n>\d{1,3}(?:[.,]\d+)?)\s*%$");double number;
            if(!m.Success||!double.TryParse(m.Groups["n"].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out number)||number>100)return Name(p)+" · remaining unknown";
            return Name(p)+(number==100&&m.Groups["op"].Value==""?"":" · "+literal);
        }
        public static List<LedgerObservation> LinkedMiners(LedgerObservation deposit,IEnumerable<LedgerObservation> all)
        {return all==null||!LedgerProduction.Valid(deposit)?new List<LedgerObservation>():all.Where(o=>LedgerProduction.Valid(o)&&o.Production.Kind=="producer"&&o.Production.Family=="AutoMiner"&&o.Production.DepositKey==deposit.Production.Key&&o.Production.Player==deposit.Production.Player&&o.Context.WorldKey==deposit.Context.WorldKey&&o.Context.Playfield==deposit.Context.Playfield).ToList();}
        public static bool Terminal(LedgerProductionPlace place){return place!=null&&place.Key.StartsWith("playfield:",StringComparison.Ordinal)&&place.Children.Count==0;}
        public static float[] Columns(bool deposit,bool terminal)
        {var source=deposit?DepositColumns:DeviceColumns;if(!terminal)return source;float remaining=1-source[deposit?1:2];return source.Where((v,i)=>i!=(deposit?1:2)).Select(v=>v/remaining).ToArray();}
        public static string[] VisibleCells(string[] cells,bool deposit,bool terminal)
        {return terminal?cells.Where((v,i)=>i!=(deposit?1:2)).ToArray():cells;}
        public static string[] Headers(bool deposit,bool terminal)
        {return VisibleCells(deposit?new[]{"Resource","Location","Size","Miner",""}:new[]{"Device","Product","Location","Storage / cap","Fill","Rate","Operation","Service in",""},deposit,terminal);}
        public static string MinerType(LedgerProductionSnapshot p,string internalName=null)
        {var t=Regex.Match(p.DefinitionName??internalName??"",@"^AutoMiningDeviceT([123])$");return t.Success?"T"+t.Groups[1].Value:"tier unknown";}
        public static string DeviceName(LedgerProductionSnapshot p,Func<int,string> definition=null)
        {return p.Family=="AutoMiner"?"Auto Miner "+MinerType(p,definition==null?null:definition(p.Definition)):Name(p);}
        public static string MinerRate(LedgerProductionSnapshot p)
        {var rate=Rate(p);return rate.HasValue?rate.Value.ToString("0.#",CultureInfo.InvariantCulture)+"/h":"unknown";}
        public static double? Rate(LedgerProductionSnapshot p)
        {
            var m=Regex.Match(LedgerPresentation.Text(p.ShownQuality??""),@"(?<![\d.])(\d+(?:[.,]\d+)?)\s*(?:/\s*(?:hour|h)\b|per\s+hour\b)",RegexOptions.IgnoreCase);
            double rate;if(m.Success&&double.TryParse(m.Groups[1].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out rate))return rate;
            return p.Period>0&&p.Period<uint.MaxValue?(double?)(72000d/p.Period.Value):null;
        }
        public static double? ProductRate(LedgerProductionSnapshot p)
        {
            var rate=Rate(p);if(!rate.HasValue||double.IsNaN(rate.Value)||double.IsInfinity(rate.Value)||rate<0)return null;
            int products=p.Outputs.Select(x=>LedgerDeviceSelection.Resource(x.Item,x.Auxiliary)).Distinct().Count();
            if(products==1)return rate;
            return p.Alternating&&products==2&&p.Outputs.Select(x=>x.Item).Distinct().Count()==2?rate/2:null;
        }
        public static string Operating(LedgerProductionSnapshot p,LedgerProductionEstimate e)
        {string basis;double? fuel=e.State.StartsWith("GAME-time",StringComparison.Ordinal)?e.WorkingSeconds:LedgerProduction.FuelAtObservation(p,out basis);return Duration(fuel).Replace("h 0m","h");}
        public static string Stock(LedgerProducerLine line,LedgerProductionEstimate e)
        {long amount;if(line.Output==null)return "unknown";return e.State.StartsWith("GAME-time",StringComparison.Ordinal)&&e.Stocks.TryGetValue(line.Output.Item,out amount)?amount.ToString(CultureInfo.InvariantCulture):line.Output.Stock.ToString(CultureInfo.InvariantCulture);}
        public static string EstimateHint(LedgerProductionSnapshot p,LedgerProductionEstimate e)
        {return "";}
        // Sort physical devices before expanding multi-product lines, so actions and identity stay together.
        public static List<LedgerObservation> SortDevices(IEnumerable<LedgerObservation> rows,LedgerHeaderSort sort,ulong tick,string epoch)
        {
            if(sort.Column<0||sort.Column>7)return rows.ToList();
            if(sort.Column==7)return LedgerProductionDue.Sort(rows,sort,tick,epoch);
            if(sort.Column<=2){
                Func<LedgerObservation,string> text=o=>sort.Column==0?DeviceName(o.Production):sort.Column==2?o.Context.Playfield:
                    o.Production.Outputs.Count==0?null:string.Join(" / ",o.Production.Outputs.Select(x=>LedgerPresentation.Text(x.Name)).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase));
                var strings=rows.Select(o=>new{Row=o,Text=text(o)}).OrderBy(x=>string.IsNullOrWhiteSpace(x.Text)?1:0);
                return (sort.Descending?strings.ThenByDescending(x=>x.Text,StringComparer.OrdinalIgnoreCase):strings.ThenBy(x=>x.Text,StringComparer.OrdinalIgnoreCase)).ThenBy(x=>x.Row.Production.Key,StringComparer.Ordinal).Select(x=>x.Row).ToList();
            }
            var numbers=rows.Select(o=>{var p=o.Production;var state=LedgerProductionServices.State(p,tick,epoch);
                double? value=sort.Column==3?(p.Outputs.Count>0?(double?)state.Estimate.Stocks.Values.Sum():null):sort.Column==4?state.Fill:sort.Column==5?Rate(p):state.Seconds;
                return new{Row=o,Value=value};}).OrderBy(x=>x.Value.HasValue?0:1);
            return (sort.Descending?numbers.ThenByDescending(x=>x.Value):numbers.ThenBy(x=>x.Value)).ThenBy(x=>x.Row.Production.Key,StringComparer.Ordinal).Select(x=>x.Row).ToList();
        }
        public static string MinerSummary(LedgerObservation deposit,IEnumerable<LedgerObservation> all,ulong now,string epoch,Func<int,string> internalName=null)
        {
            var linked=LinkedMiners(deposit,all);if(linked.Count==0)return "—";if(linked.Count!=1)return "Miner link uncertain";
            var o=linked[0];var p=o.Production;var e=LedgerProduction.Estimate(p,now,epoch);var line=new LedgerProducerLine{Observation=o,Output=p.Outputs.Count==1?p.Outputs[0]:null,First=true};
            return MinerType(p,internalName==null?null:internalName(p.Definition))+" · "+Stock(line,e)+"/"+(p.Capacity.HasValue?p.Capacity.Value.ToString(CultureInfo.InvariantCulture):"unknown")+" · "+MinerRate(p)+" · "+Operating(p,e);
        }
        public static string[] DepositCells(LedgerObservation o,DateTime now,IEnumerable<LedgerObservation> all=null,ulong tick=0,string epoch=null,Func<int,string> internalName=null)
        {var p=o.Production;return new[]{DepositName(p),o.Context.Playfield,p.Size??"not observed",MinerSummary(o,all,tick,epoch,internalName),""};}
        public static string[] DeviceCells(LedgerProducerLine line,LedgerProductionEstimate e,DateTime now,Func<int,string> definition=null,LedgerProductionDueValue service=null)
        {
            var p=line.Observation.Production;string stock=Stock(line,e);if(line.First&&p.Capacity.HasValue)stock+=" / "+(p.Outputs.Select(x=>x.Item).Distinct().Count()>1?"shared ":"")+p.Capacity.Value;
            string operation=line.First?(e.Full?"Full · ":e.NeedsFuel?"Fuel due · ":p.Running==false?"Stopped · ":"")+Operating(p,e):"";
            return new[]{line.First?DeviceName(p,definition):"",line.Output==null?"not identified":line.Output.Name,line.First?line.Observation.Context.Playfield:"",stock,"",line.First?MinerRate(p):"",operation,line.First?(service==null?"unknown":service.Text):"",""};
        }
        // Retained for old non-game harnesses; overview no longer exposes countdown metadata.
        public static string DetailLine(LedgerProductionSnapshot p){return "";}
    }
}
