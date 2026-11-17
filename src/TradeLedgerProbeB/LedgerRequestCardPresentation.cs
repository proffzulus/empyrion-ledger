using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    public sealed class LedgerRequestCompactItem
    {public int Branch;public string Requirement,Progress,Seller;public LedgerRequestSellerHint Hint;}
    // One requirement row owns name/quantity, stock progress and optional Market evidence.
    public static class LedgerRequestCardPresentation
    {
        public static List<LedgerRequestCompactItem> Rows(LedgerRequestFact fact,LedgerCatalog catalog,LedgerRequestInventoryProgress progress,IList<LedgerRequestSellerHint> hints)
        {
            var rows=new List<LedgerRequestCompactItem>();if(fact==null)return rows;
            var structure=progress??LedgerRequestInventory.Match(fact,LedgerRequestStock.Unknown("Inventory unavailable"));
            for(int b=0;b<structure.Branches.Count;b++)foreach(var item in structure.Branches[b].Items){
                LedgerCatalogItem canonical;string name=item.Item>0&&catalog!=null&&catalog.Items.TryGetValue(item.Item.Value,out canonical)?LedgerPresentation.Text(canonical.Name):item.Name;
                var hint=(hints??new List<LedgerRequestSellerHint>()).FirstOrDefault(h=>h.Branch==b&&h.Item==item.Item&&h.Needed==item.Needed&&(h.Name==item.Name||h.Name=="Option "+(b+1)+": "+item.Name));
                string seller=hint==null?"":hint.Target==null?"Unresolved item":hint.Seller==null?"No seller seen":hint.Seller.Offer.RetainedRelation?"Seen before | stock "+N(hint.Stock):"Seller "+N(hint.Stock)+" | "+(hint.Age==LedgerOfferAge.BeforeDeadline?"fresh":hint.Age==LedgerOfferAge.Recheck?"historical":"last seen");
                rows.Add(new LedgerRequestCompactItem{Branch=b,Requirement=N(item.Needed)+" "+name,
                    Progress="Pack "+N(item.Backpack)+" | Carry "+N(item.Carry)+(item.Carry.HasValue&&!item.CarryComplete?"+":""),Seller=seller,Hint=hint});
            }return rows;
        }
        private static string N(long? n){return n.HasValue?n.Value.ToString(CultureInfo.InvariantCulture):"?";}
        public static uint SellerColor(LedgerRequestSellerHint hint){return hint==null||hint.Seller==null?0:LedgerFreshness.TextColor(hint.Age);}
        public static string Status(LedgerRequestInventoryProgress p)
        {return p==null?"":p.Ambiguous?"Check requirements":p.Ready?"Ready":p.Prepared?"Move to pack":p.Partial?"Partial":"";}
        public static string Reward(LedgerRequestFact f)
        {return f==null?"":f.RewardCredits.HasValue?f.RewardCredits.Value.ToString("N0",CultureInfo.InvariantCulture)+" cr":string.IsNullOrEmpty(f.RewardText)?"":LedgerPresentation.Text(f.RewardText).Length>64?"Reward: details":LedgerPresentation.Text(f.RewardText);}
    }
    public sealed class LedgerRequestCompactLayout
    {
        public readonly float Name,Progress,Seller,Market;
        public LedgerRequestCompactLayout(float width){Market=70;Progress=Math.Min(190,Math.Max(140,width*.21f));Seller=Math.Min(280,Math.Max(155,width*.29f));Name=Math.Max(100,width-Progress-Seller-Market-24);}
        public float RowHeight(float nameHeight,float progressHeight,float sellerHeight){return Math.Max(30,Math.Max(nameHeight,Math.Max(progressHeight,sellerHeight))+6);}
    }
}
