using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeLedgerProbeB
{
    public enum LedgerOfferColumn { Item,Price,Quantity,Vendor,Place,Distance }
    public sealed class LedgerHeaderSort
    {
        public int Column=-1;public bool Descending;
        public void Click(int column)
        {
            if(Column!=column){Column=column;Descending=false;}
            else if(!Descending)Descending=true;
            else{Column=-1;Descending=false;}
        }
        public string Label(string text,int column){return text+(Column==column?(Descending?" \u25BC":" \u25B2"):"");}
    }
    // UI DTOs are immutable between projection reloads. Sort once per table/input change,
    // or position sample for Distance, rather than once per OnGUI event.
    public sealed class LedgerOfferSortCache
    {
        private List<LedgerViewOffer> source,value;private LedgerViewData owner;private object scope;
        private int column;private bool descending;private long position;
        public List<LedgerViewOffer> Apply(List<LedgerViewOffer> rows,LedgerHeaderSort sort,LedgerOfferColumn[] columns,LedgerViewData data,LedgerPlayerPoint player,long positionSample=0,object location=null,Func<LedgerViewOffer,bool> filter=null)
        {
            long sample=sort.Column>=0&&sort.Column<columns.Length&&columns[sort.Column]==LedgerOfferColumn.Distance?positionSample:0;
            if(value!=null&&ReferenceEquals(source,rows)&&ReferenceEquals(owner,data)&&ReferenceEquals(scope,location)&&column==sort.Column&&descending==sort.Descending&&position==sample)return value;
            source=rows;owner=data;scope=location;column=sort.Column;descending=sort.Descending;position=sample;
            value=LedgerTableSorting.Offers(filter==null?rows:rows.Where(filter),sort,columns,data,player);return value;
        }
    }
    public static class LedgerTableSorting
    {
        // Unknowns stay last in BOTH directions. Ties always use stable identity, never row ordinal.
        private static List<T> Ordered<T>(IEnumerable<T> rows,bool descending,Func<T,decimal?> number,Func<T,string> text,Func<T,string> key)
        {
            if(number!=null)
            {var keys=rows.Select(r=>new{Row=r,Number=number(r),Key=key(r)}).ToList();var known=keys.OrderBy(r=>r.Number.HasValue?0:1);
                return (descending?known.ThenByDescending(r=>r.Number):known.ThenBy(r=>r.Number)).ThenBy(r=>r.Key,StringComparer.Ordinal).Select(r=>r.Row).ToList();}
            return (descending?rows.OrderByDescending(text,StringComparer.OrdinalIgnoreCase):rows.OrderBy(text,StringComparer.OrdinalIgnoreCase)).ThenBy(key,StringComparer.Ordinal).ToList();
        }
        public static List<LedgerViewOffer> Offers(IEnumerable<LedgerViewOffer> rows,LedgerHeaderSort sort,LedgerOfferColumn[] columns,LedgerViewData data,LedgerPlayerPoint player)
        {
            if(sort.Column<0||sort.Column>=columns.Length)return rows.ToList();var column=columns[sort.Column];Func<LedgerViewOffer,decimal?> number=null;Func<LedgerViewOffer,string> text=null;
            if(column==LedgerOfferColumn.Price)number=r=>LedgerComparison.Price(r.Price);
            else if(column==LedgerOfferColumn.Quantity)number=r=>{long? n=r.Sellers?r.Offer.Stock:r.Offer.RemainingNpcBuyingCapacity;return n>=0?(decimal?)n:null;};
            else if(column==LedgerOfferColumn.Distance)number=r=>data.DistanceSortFor(r.Market,player);
            else text=r=>column==LedgerOfferColumn.Item?LedgerPresentation.Text(r.Offer.Visible==null?null:r.Offer.Visible.Name):column==LedgerOfferColumn.Vendor?data.VendorFor(r.Market):data.PlaceFor(r.Market);
            return Ordered(rows,sort.Descending,number,text,r=>LedgerBrowse.OfferKey(r.Market,r.Offer,r.Sellers));
        }
        public static List<LedgerComparisonRow> Comparison(IEnumerable<LedgerComparisonRow> rows,LedgerHeaderSort sort,LedgerViewData data)
        {
            // The comparison producer already established total-difference order and stable ties.
            if(sort.Column<0||sort.Column>9)return rows.ToList();
            Func<LedgerComparisonRow,decimal?> number=null;Func<LedgerComparisonRow,string> text=null;
            switch(sort.Column)
            {
                case 2:number=r=>r.SourcePrice;break;case 3:number=r=>r.SourceStock;break;
                case 5:number=r=>r.DestinationPrice;break;case 6:number=r=>r.DestinationDemand;break;
                case 7:number=r=>r.Quantity;break;case 8:number=r=>r.UnitDelta;break;case 9:number=r=>r.TotalDelta;break;
                default:text=r=>sort.Column==1?data.VendorFor(r.Source.Market)+" / "+data.PlaceFor(r.Source.Market):sort.Column==4?data.VendorFor(r.Destination.Market)+" / "+data.PlaceFor(r.Destination.Market):LedgerPresentation.Text(r.Name);break;
            }
            return Ordered(rows,sort.Descending,number,text,r=>r.Key);
        }
    }
    public static class LedgerReadability
    {
        public const float PanelAlpha=.94f,RowAlpha=.92f;
        // WCAG luminance of local backing composited over a bright scene and the existing blue overlay.
        public static double WhiteSceneContrast()
        {
            Func<double,double> linear=v=>v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);
            double[] overlay={.045,.16,.25},plate={.025,.075,.12},foreground={.88,.94,.95};
            double back=0,front=0;double[] weights={.2126,.7152,.0722};
            for(int i=0;i<3;i++){double v=plate[i]*RowAlpha+(overlay[i]*.72+.28)*(1-RowAlpha);back+=linear(v)*weights[i];front+=linear(foreground[i])*weights[i];}
            return (front+.05)/(back+.05);
        }
    }
}
