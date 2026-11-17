using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TradeLedgerProbeB
{
    // UI-level text only, never hidden precise counters. Native callers establish either
    // visible publication or the separately verified synchronous discovery-event permission.
    public sealed class LedgerDepositDetails
    {
        public string Size,PercentageText,Body;
        public double? RemainingPercent;
        public static LedgerDepositDetails Read(string body,string rendered,string localizedSize,bool visible)
        {
            if(!visible||string.IsNullOrWhiteSpace(body)||body.Length>4096||string.IsNullOrEmpty(rendered)||rendered.Length>8192)return null;
            string plain=LedgerPresentation.Text(body),shown=LedgerPresentation.Text(rendered);
            if(plain.Length==0||shown.IndexOf(plain,StringComparison.Ordinal)<0)return null;
            return ParseAdmitted(body,localizedSize);
        }
        // Pure parsing is NOT an admission gate. Offscreen callers require the exact event proof.
        public static LedgerDepositDetails ParseAdmitted(string body,string localizedSize)
        {
            if(string.IsNullOrWhiteSpace(body)||body.Length>4096)return null;
            string plain=LedgerPresentation.Text(body);
            var d=new LedgerDepositDetails{Body=body};
            if(!string.IsNullOrWhiteSpace(localizedSize)&&localizedSize.Length<=256){string size=LedgerPresentation.Text(localizedSize);if(size.Length>0&&Regex.IsMatch(plain,@"(?<!\p{L})"+Regex.Escape(size)+@"(?!\p{L})"))d.Size=size;}
            string literal=LedgerProduction.ShownPercentage(body);double number;
            if(literal!=null){var m=Regex.Match(literal,@"^(?<op>[<>]?)\s*(?<n>\d{1,3}(?:[.,]\d+)?)\s*%$");
                if(m.Success&&double.TryParse(m.Groups["n"].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out number)&&number>=0&&number<=100){d.PercentageText=literal;if(m.Groups["op"].Value.Length==0)d.RemainingPercent=number;}}
            return d.Size==null&&d.PercentageText==null?null:d;
        }
        public void Apply(LedgerProductionSnapshot p,ulong tick,string source)
        {p.Size=Size;p.PercentageText=PercentageText;p.RemainingPercent=RemainingPercent;p.RawUi=Body;p.DetailsTick=tick;p.DetailsUtc=DateTime.UtcNow.ToString("o");p.DetailsSource=source;p.SizeTick=Size==null?(ulong?)null:tick;p.PercentageTick=PercentageText==null?(ulong?)null:tick;}
        public static void Preserve(LedgerProductionSnapshot next,LedgerProductionSnapshot old)
        {
            if(next.Kind!="deposit"||old.Kind!="deposit"||next.Key!=old.Key)return;
            bool all=!next.DetailsTick.HasValue||old.DetailsTick.HasValue&&next.DetailsTick<old.DetailsTick;
            if(all||string.IsNullOrEmpty(next.Size)){next.Size=old.Size;next.SizeTick=string.IsNullOrEmpty(old.Size)?null:old.SizeTick??old.DetailsTick;}
            if(all||string.IsNullOrEmpty(next.PercentageText)){next.PercentageText=old.PercentageText;next.RemainingPercent=old.RemainingPercent;next.PercentageTick=string.IsNullOrEmpty(old.PercentageText)?null:old.PercentageTick??old.DetailsTick;}
            if(all){next.RawUi=old.RawUi;next.DetailsTick=old.DetailsTick;next.DetailsUtc=old.DetailsUtc;next.DetailsSource=old.DetailsSource;}
        }
    }
}
