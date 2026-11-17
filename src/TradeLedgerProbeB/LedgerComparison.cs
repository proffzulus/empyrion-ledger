using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    public enum LedgerComparisonSort { TotalDelta, UnitDelta, Quantity }
    public sealed class LedgerComparisonRow
    {
        public LedgerViewOffer Source,Destination;
        public string ItemKey,Name,Key;
        public long? SourceStock,DestinationDemand,Quantity;
        public decimal? SourcePrice,DestinationPrice,UnitDelta,TotalDelta;
    }
    public sealed class LedgerComparisonResult
    {
        public List<LedgerComparisonRow> Rows=new List<LedgerComparisonRow>();
        public bool LimitReached;
    }
    public static class LedgerComparison
    {
        public static string ItemKey(LedgerOffer o)
        {return LedgerOffer.Certified(o)&&o.ItemId>0&&o.AuxiliaryRaw.HasValue?o.ItemId.Value.ToString(CultureInfo.InvariantCulture)+":"+o.AuxiliaryRaw.Value.ToString(CultureInfo.InvariantCulture):null;}
        public static decimal? Price(string value)
        {decimal n;return value!=null&&value.Length<=40&&decimal.TryParse(value,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out n)&&n>=0?(decimal?)n:null;}
        public static string Number(decimal? value,bool signed=false)
        {return !value.HasValue?"unknown":(signed&&value.Value>0?"+":"")+value.Value.ToString("0.############################",CultureInfo.InvariantCulture);}
        public static string Quantity(long? value){return value.HasValue?value.Value.ToString(CultureInfo.InvariantCulture):"unknown";}
        public static LedgerComparisonResult Query(LedgerViewData data,LedgerLocationIndex index,LedgerScope a,LedgerScope b,string search,LedgerComparisonSort sort,bool descending,int maxPairs=100000)
        {
            var result=new LedgerComparisonResult();if(a==null||b==null)return result;
            var sellers=data.Markets.Where(m=>index.Contains(a,m)&&m.FullCurrentAssortment==true).SelectMany(m=>m.Offers.Where(o=>o.NpcSellsSupported==true&&ItemKey(o)!=null).Select(o=>new LedgerViewOffer{Market=LedgerViewData.OfferMarket(m,o),Offer=o,Sellers=true}));
            var buyers=data.Markets.Where(m=>index.Contains(b,m)&&m.FullCurrentAssortment==true).SelectMany(m=>m.Offers.Where(o=>o.NpcBuysSupported==true&&ItemKey(o)!=null).Select(o=>new LedgerViewOffer{Market=LedgerViewData.OfferMarket(m,o),Offer=o,Sellers=false})).ToLookup(r=>ItemKey(r.Offer));
            foreach(var src in sellers)
            {
                string item=ItemKey(src.Offer),name=LedgerPresentation.Text(src.Offer.Visible==null?null:src.Offer.Visible.Name);
                if(!string.IsNullOrEmpty(search)&&(name+" "+item).IndexOf(search,StringComparison.OrdinalIgnoreCase)<0)continue;
                foreach(var dest in buyers[item])
                {
                    if(result.Rows.Count>=maxPairs){result.LimitReached=true;return Ordered(result,sort,descending);}
                    var row=new LedgerComparisonRow{Source=src,Destination=dest,Name=name,ItemKey=item,
                        Key=LedgerBrowse.OfferKey(src.Market,src.Offer,true)+"|row:"+src.Offer.UiRow+" -> "+LedgerBrowse.OfferKey(dest.Market,dest.Offer,false)+"|row:"+dest.Offer.UiRow,
                        SourcePrice=Price(src.Price),DestinationPrice=Price(dest.Price),
                        SourceStock=src.Offer.Stock>=0?(long?)src.Offer.Stock:null,
                        DestinationDemand=dest.Offer.RemainingNpcBuyingCapacity>=0?dest.Offer.RemainingNpcBuyingCapacity:null};
                    if(row.SourceStock.HasValue&&row.DestinationDemand.HasValue)row.Quantity=Math.Min(row.SourceStock.Value,row.DestinationDemand.Value);
                    try{if(row.SourcePrice.HasValue&&row.DestinationPrice.HasValue)row.UnitDelta=row.DestinationPrice-row.SourcePrice;}catch(OverflowException){row.UnitDelta=null;}
                    try{if(row.UnitDelta.HasValue&&row.Quantity.HasValue)row.TotalDelta=row.UnitDelta*row.Quantity.Value;}catch(OverflowException){row.TotalDelta=null;}
                    result.Rows.Add(row);
                }
            }
            return Ordered(result,sort,descending);
        }
        private static LedgerComparisonResult Ordered(LedgerComparisonResult r,LedgerComparisonSort sort,bool descending)
        {
            Func<LedgerComparisonRow,decimal?> metric=row=>sort==LedgerComparisonSort.UnitDelta?row.UnitDelta:sort==LedgerComparisonSort.Quantity?(decimal?)row.Quantity:row.TotalDelta;
            var known=r.Rows.OrderBy(row=>metric(row).HasValue?0:1);
            r.Rows=(descending?known.ThenByDescending(metric):known.ThenBy(metric)).ThenBy(row=>row.Key,StringComparer.Ordinal).ToList();return r;
        }
    }
}
