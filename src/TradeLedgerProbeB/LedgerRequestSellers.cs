using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    public enum LedgerRequestSellerStock { None, Unknown, Zero, Partial, Sufficient }
    public sealed class LedgerRequestSellerHint
    {
        public int Branch;public int? Item;public string Name;public long? Needed,Stock;
        public LedgerViewOffer Seller;public LedgerViewItem Target;public LedgerOfferAge Age;
        public LedgerRequestSellerStock State;
        public string Text
        {
            get{
                string item=Name+(Seller!=null&&Seller.Offer.AuxiliaryRaw!=0?" [variant "+Seller.Offer.AuxiliaryRaw+"]":"");
                string amount=Stock.HasValue?Stock.Value.ToString(CultureInfo.InvariantCulture):"unknown";
                string need=Needed.HasValue?Needed.Value.ToString(CultureInfo.InvariantCulture):"unknown";
                return (State==LedgerRequestSellerStock.None?"Sellers: ":"Seller seen: ")+item+" - "+(State==LedgerRequestSellerStock.None?"no matching observation":amount+" / "+need+" required"+
                    (State==LedgerRequestSellerStock.Sufficient?" (enough seen)":State==LedgerRequestSellerStock.Partial?" (short)":""))+" | "+
                    (Seller==null?"":Seller.Offer.RetainedRelation?"seen before | ":Age==LedgerOfferAge.BeforeDeadline?"fresh | ":Age==LedgerOfferAge.Recheck?"historical; recheck | ":"last seen | ")+
                    (Target==null?"item unresolved":"Market");
            }
        }
        public string Tooltip
        {get{return Seller==null?"Inspect recorded Market offers":LedgerViewData.Place(Seller.Market)+(string.IsNullOrEmpty(Seller.Market.Utc)?"":"\nSeen "+Seller.Market.Utc)+"\n"+(Seller.Offer.RetainedRelation?"Not in latest assortment":LedgerFreshness.Hint(Seller.Market,Seller.Market.Context,Clock));}}
        internal ulong? Clock;
    }
    // Pure projection of existing latest market snapshots. Never inventory/goal/observation writes.
    public sealed class LedgerRequestSellers
    {
        private readonly LedgerViewData data;
        private readonly Dictionary<int,List<LedgerViewOffer>> sellers=new Dictionary<int,List<LedgerViewOffer>>();
        private readonly List<LedgerViewOffer> ageOffers=new List<LedgerViewOffer>();private readonly List<LedgerOfferAge> ages=new List<LedgerOfferAge>();
        private ulong? clock;private string clockWorld,clockMode;private bool clockReady,clockRead;private ulong ageRevision;
        // Layout depends on freshness categories, not every advancing game tick. Tooltip
        // countdowns are evaluated for visible rows using the current clock at draw time.
        public ulong PresentationVersion(LedgerContext current,ulong? now){
            string world=current==null?null:current.WorldKey,mode=current==null?null:current.Mode;bool ready=current!=null&&current.Recordable;
            if(clockRead&&clock==now&&clockWorld==world&&clockMode==mode&&clockReady==ready)return ageRevision;
            bool changed=!clockRead||clockWorld!=world||clockMode!=mode||clockReady!=ready;clockRead=true;clock=now;clockWorld=world;clockMode=mode;clockReady=ready;
            for(int i=0;i<ageOffers.Count;i++){var age=LedgerFreshness.Evaluate(ageOffers[i].Market,current,now);if(ages[i]!=age){ages[i]=age;changed=true;}}
            if(changed)ageRevision++;return ageRevision;
        }
        public LedgerRequestSellers(LedgerViewData view)
        {
            data=view;if(data==null)return;
            var markets=data.Markets.Where(m=>m!=null&&m.Context!=null&&m.Context.WorldKey==data.WorldKey&&m.Context.Recordable&&m.Context.Mode=="SinglePlayer"&&m.Identity!=null&&m.Identity.NpcEntityId>0&&m.FullCurrentAssortment==true&&m.Offers!=null);
            foreach(var endpoint in markets.GroupBy(LedgerDatabase.Key)){
                var latest=endpoint.Aggregate((a,b)=>LedgerPresentation.CompareDialogue(a,b)>=0?a:b);
                // Normally one DB row per endpoint. Never choose between conflicting equal-order snapshots.
                var tied=endpoint.Where(m=>LedgerPresentation.CompareDialogue(m,latest)==0).ToList();
                if(tied.Any(m=>!ReferenceEquals(m,latest)&&!LedgerJson.Encode(m.Offers).SequenceEqual(LedgerJson.Encode(latest.Offers))))continue;
                foreach(var group in latest.Offers.Where(o=>LedgerOffer.Certified(o)&&o.NpcSellsSupported==true).GroupBy(LedgerComparison.ItemKey)){
                    var first=group.First();var offer=first.Copy();
                    // Same trader/variant duplicate rows do not prove independent physical stock.
                    if(group.Any(o=>o.Stock!=first.Stock||o.RetainedRelation!=first.RetainedRelation))offer.Stock=null;
                    if(offer.Stock<0||offer.RetainedRelation&&offer.Stock!=0)offer.Stock=null;
                    List<LedgerViewOffer> list;if(!sellers.TryGetValue(offer.ItemId.Value,out list))sellers.Add(offer.ItemId.Value,list=new List<LedgerViewOffer>());
                    var row=new LedgerViewOffer{Market=LedgerViewData.OfferMarket(latest,offer),Offer=offer,Sellers=true};list.Add(row);ageOffers.Add(row);ages.Add(LedgerOfferAge.Unknown);
                }
            }
        }
        public List<LedgerRequestSellerHint> Query(LedgerRequestFact fact,LedgerContext current,ulong? now)
        {
            var result=new List<LedgerRequestSellerHint>();if(data==null||current==null||current.WorldKey!=data.WorldKey||!current.Recordable||current.Mode!="SinglePlayer")return result;
            // Reuse only the pure branch/quantity normalizer, never the inventory consumer or marker activation.
            var requirements=LedgerRequestInventory.Match(fact,LedgerRequestStock.Unknown("seller evidence only"));
            for(int b=0;b<requirements.Branches.Count;b++)foreach(var item in requirements.Branches[b].Items){
                var hint=new LedgerRequestSellerHint{Branch=b,Item=item.Item,Name=item.Name,Needed=item.Needed,State=LedgerRequestSellerStock.None,Clock=now};
                List<LedgerViewOffer> choices;
                if(item.Item>0&&sellers.TryGetValue(item.Item.Value,out choices)){
                    hint.Seller=choices.OrderByDescending(o=>!requirements.Ambiguous&&item.Needed.HasValue&&o.Offer.Stock>=item.Needed&& !o.Offer.RetainedRelation)
                        .ThenByDescending(o=>LedgerFreshness.Evaluate(o.Market,current,now)==LedgerOfferAge.BeforeDeadline).ThenByDescending(o=>o.Offer.Stock.HasValue).ThenByDescending(o=>o.Offer.Stock)
                        .ThenBy(o=>LedgerDatabase.Key(o.Market),StringComparer.Ordinal).ThenBy(o=>o.Offer.AuxiliaryRaw).First();
                    hint.Stock=hint.Seller.Offer.Stock;hint.Age=LedgerFreshness.Evaluate(hint.Seller.Market,current,now);
                    hint.State=!hint.Stock.HasValue?LedgerRequestSellerStock.Unknown:hint.Stock==0?LedgerRequestSellerStock.Zero:requirements.Ambiguous||!item.Needed.HasValue?LedgerRequestSellerStock.Unknown:
                        !requirements.Ambiguous&&item.Needed.HasValue&&hint.Stock>=item.Needed?LedgerRequestSellerStock.Sufficient:LedgerRequestSellerStock.Partial;
                    hint.Target=data.Items(null).FirstOrDefault(i=>i.Id==item.Item&&i.Auxiliary==hint.Seller.Offer.AuxiliaryRaw);
                }
                if(hint.Target==null&&item.Item>0)hint.Target=LedgerItemDisplay.Request(data,data.Catalog,new LedgerRequestedItem{ItemId=item.Item,Name=item.Name});
                if(requirements.Branches.Count>1)hint.Name="Option "+(b+1)+": "+hint.Name;
                result.Add(hint);
            }
            return result;
        }
    }
}
