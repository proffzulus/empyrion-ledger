using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal static class LedgerRequestSellerTests
    {
        private static T Copy<T>(T v){return LedgerJson.Decode<T>(LedgerJson.Encode(v));}
        private static LedgerContext Context(){return LedgerContext.Create(Path.Combine(Path.GetTempPath(),"owned-seller-hints"),"Planet","System","SinglePlayer");}
        private static LedgerRequestFact Fact(int id=40,long? n=25){return new LedgerRequestFact{Relation="all",Items=new List<LedgerRequestedItem>{new LedgerRequestedItem{ItemId=id,Quantity=n,Name="Copper"}}};}
        private static LedgerObservation Market(LedgerContext c,int? stock,int npc=101,int id=40,int aux=0)
        {
            return new LedgerObservation{Kind="market",Context=c,FullCurrentAssortment=true,Utc="2026-11-06T00:00:00.0000000Z",GameTicks="10000",Identity=new LedgerIdentity{NpcEntityId=npc,StructureEntityId=npc+100,StructureName="Observed seller"},Offers=new List<LedgerOffer>{new LedgerOffer{BindingState="bound-current-delegate",ItemId=id,AuxiliaryRaw=aux,Stock=stock,NpcSellsSupported=true,NpcBuysSupported=true,NpcSellsAvailable=stock>0,RemainingNpcBuyingCapacity=999,StoredSellUnit="200",Visible=new PublishedOffer{Name="Copper"}}},OfferFreshness=new LedgerOfferFreshness{Source=LedgerFreshness.Source,State="observed-current-model",ObservationGameTick=10000,RestockMinutes=1,NextRefreshTick=11200}};
        }
        private static LedgerViewData View(LedgerContext c,params LedgerObservation[] markets)
        {var v=new LedgerViewData{WorldKey=c.WorldKey,Catalog=new LedgerCatalog()};v.Catalog.Add(new LedgerCatalogItem{Id=40,Name="Copper"});v.Catalog.Add(new LedgerCatalogItem{Id=41,Name="Optics"});v.Markets.AddRange(markets);return v;}
        private static LedgerRequestSellerHint Hint(LedgerViewData v,LedgerRequestFact f=null,ulong? tick=10001){return new LedgerRequestSellers(v).Query(f??Fact(),Context(),tick).Single();}
        internal static void Run(Action<bool,string> check,string root)
        {
            Action<bool,string> ok=(v,n)=>check(v,"request sellers "+n);var c=Context();var m=Market(c,30);var data=View(c,m);
            var h=Hint(data);ok(h.State==LedgerRequestSellerStock.Sufficient&&h.Stock==30&&h.Needed==25,"single observed seller quantity compared with full request amount");
            ok(h.Text.Contains("30 / 25 required (enough seen)")&&h.Text.Contains("fresh")&&h.Target.Id==40,"compact observed sufficiency and exact Market target");
            ok(h.Age==LedgerOfferAge.BeforeDeadline&&LedgerFreshness.TextColor(h.Age)!=0,"known unexpired scheduling uses existing fresh text color");
            ok(Hint(data,tick:11200).Text.Contains("historical; recheck")&&Hint(data,tick:11200).State==LedgerRequestSellerStock.Sufficient,"expiry changes age indication not factual stock amount");
            ok(Hint(data,tick:9999).Age==LedgerOfferAge.Unknown&&!Hint(data,tick:9999).Text.Contains("fresh"),"rollback does not label future observation fresh");
            foreach(int amount in new[]{0,10,24,25,26}){var v=View(c,Market(c,amount));var x=Hint(v);ok(x.Stock==amount&&x.State==(amount==0?LedgerRequestSellerStock.Zero:amount<25?LedgerRequestSellerStock.Partial:LedgerRequestSellerStock.Sufficient),"zero/partial/equality/sufficient stock "+amount);}
            h=Hint(View(c,Market(c,null)));ok(h.State==LedgerRequestSellerStock.Unknown&&h.Text.Contains("unknown / 25"),"unknown seller quantity not zero or enough");
            ok(Hint(data,Fact(n:null)).State==LedgerRequestSellerStock.Unknown&&!Hint(data,Fact(n:null)).Text.Contains("enough seen"),"unknown requested quantity cannot establish sufficiency");
            var conflict=Fact();conflict.Conflict="observed text/script disagreement";ok(Hint(data,conflict).State!=LedgerRequestSellerStock.Sufficient,"conflicted requirements not complete sourcing claims");
            var truncated=Fact();truncated.Truncated=true;ok(Hint(data,truncated).State!=LedgerRequestSellerStock.Sufficient,"truncated requirement not sufficient");
            var mixed=Fact();mixed.Relation="mixed-unknown";ok(Hint(data,mixed).State!=LedgerRequestSellerStock.Sufficient,"unknown relation cannot be a full sourcing claim");
            var demand=Market(c,99);demand.Offers[0].NpcSellsSupported=false;ok(Hint(View(c,demand)).State==LedgerRequestSellerStock.None,"large demand is never a seller");
            demand.Offers[0].NpcSellsSupported=null;ok(Hint(View(c,demand)).State==LedgerRequestSellerStock.None,"unproven sale support cannot invent seller evidence");
            var other=Market(c,99,id:41);other.Offers[0].Visible.Name="Copper";ok(Hint(View(c,other)).State==LedgerRequestSellerStock.None,"similar or identical visible names cannot cross canonical IDs");
            var invalid=Market(c,99);invalid.Offers[0].AuxiliaryRaw=null;ok(Hint(View(c,invalid)).State==LedgerRequestSellerStock.None,"uncertified variant identity rejected");
            invalid.Offers[0].AuxiliaryRaw=0;invalid.Offers[0].BindingState="unbound";ok(Hint(View(c,invalid)).State==LedgerRequestSellerStock.None,"unbound visible row cannot create an exact seller");
            h=Hint(View(c));ok(h.Text.Contains("no matching observation")&&!h.Text.Contains("cannot buy")&&h.Target!=null,"no evidence is not impossibility and canonical Market inspection remains available");
            var unidentified=Fact();unidentified.Items[0].ItemId=null;h=Hint(data,unidentified);ok(h.Target==null&&h.State==LedgerRequestSellerStock.None,"unresolved request item cannot guess a Market identity");
            var wrongWorld=Copy(m);wrongWorld.Context=LedgerContext.Create(Path.Combine(Path.GetTempPath(),"different-owned-seller-hints"),"Planet","System","SinglePlayer");ok(Hint(View(c,wrongWorld)).State==LedgerRequestSellerStock.None,"independent save evidence excluded");
            var partial=Copy(m);partial.FullCurrentAssortment=false;ok(Hint(View(c,partial)).State==LedgerRequestSellerStock.None,"partial capture not admitted as complete seller snapshot");
            var duplicate=Copy(m);data=View(c,m,duplicate);ok(Hint(data).Stock==30,"duplicate complete observations not summed");
            var two=View(c,Market(c,15),Market(c,15,npc:102));ok(Hint(two).State==LedgerRequestSellerStock.Partial&&Hint(two).Stock==15,"two different sellers not added into fictional sufficient stock");
            var variants=Market(c,15);variants.Offers.Add(Copy(variants.Offers[0]));variants.Offers[1].AuxiliaryRaw=1;ok(Hint(View(c,variants)).State==LedgerRequestSellerStock.Partial,"separate auxiliary variants never pooled");
            variants.Offers[1].Stock=30;h=Hint(View(c,variants));ok(h.Target.Auxiliary==1&&!h.Target.Canonical&&h.Text.Contains("variant 1"),"one sufficient variant has separate explicit identity and exact Market filter");
            var repeated=Market(c,15);repeated.Offers.Add(Copy(repeated.Offers[0]));ok(Hint(View(c,repeated)).Stock==15,"same trader repeated same-variant row not summed");
            repeated.Offers[1].Stock=30;ok(Hint(View(c,repeated)).State==LedgerRequestSellerStock.Unknown,"conflicting repeated same-trader stock stays unknown");
            var latest=Copy(m);latest.Utc="2026-11-06T00:01:00Z";latest.Offers[0].Stock=0;ok(Hint(View(c,m,latest)).Stock==0,"new zero supersedes old abundant same-trader stock");
            latest.Offers[0].Stock=10;ok(Hint(View(c,m,latest)).Stock==10,"new reduction supersedes old abundant stock");
            latest.Offers.Clear();ok(Hint(View(c,m,latest)).State==LedgerRequestSellerStock.None,"new complete empty snapshot cannot resurrect older positive row");
            var tie=Copy(m);tie.Offers[0].Stock=0;ok(Hint(View(c,m,tie)).State==LedgerRequestSellerStock.None,"equal-order conflicting snapshot not chosen opportunistically");
            var legacy=Copy(m);legacy.OfferFreshness=null;h=Hint(View(c,legacy));ok(h.Age==LedgerOfferAge.Unknown&&h.Text.Contains("last seen")&&!h.Text.Contains("?")&&!h.Text.Contains("fresh"),"legacy timer stays silent and last-seen evidence not called fresh");
            var retained=Copy(m);retained.Offers[0].RetainedRelation=true;retained.Offers[0].LastEvidenceUtc="2026-11-01T00:00:00Z";h=Hint(View(c,retained));ok(h.Stock==null&&h.State==LedgerRequestSellerStock.Unknown&&h.Age==LedgerOfferAge.Unknown&&h.Text.Contains("seen before"),"missing retained relation cannot advertise historical positive stock as current");
            retained.Offers[0].Stock=0;h=Hint(View(c,retained));ok(h.Stock==0&&h.Text.Contains("0 / 25")&&h.Seller.Market.Utc=="2026-11-01T00:00:00Z","retained observed zero and original evidence time stay meaningful");
            var f=Fact();f.Items.Add(new LedgerRequestedItem{ItemId=41,Quantity=5,Name="Optics"});var lines=new LedgerRequestSellers(View(c,m)).Query(f,c,10001);
            ok(lines.Count==2&&lines[0].State==LedgerRequestSellerStock.Sufficient&&lines[1].State==LedgerRequestSellerStock.None,"multi-item requirement keeps unsourced blocker visible");
            var repeat=Fact(n:15);repeat.Items.Add(new LedgerRequestedItem{ItemId=40,Quantity=20,Name="Copper"});h=Hint(View(c,m),repeat);ok(h.Needed==35&&h.State==LedgerRequestSellerStock.Partial,"repeated required IDs sum within branch rather than reuse same stock twice");
            var alt=Copy(f);alt.Items.Add(new LedgerRequestedItem{ItemId=42,Quantity=2,Name="Common input"});alt.Alternatives.Add(new LedgerRequestAlternative{Inputs=new[]{0}});alt.Alternatives.Add(new LedgerRequestAlternative{Inputs=new[]{1}});
            lines=new LedgerRequestSellers(View(c,m)).Query(alt,c,10001);ok(lines.Count==4&&lines.Count(l=>l.Item==42)==2&&lines.All(l=>l.Text.Contains("Option")),"alternatives and each common-input blocker remain explicit");
            var before=LedgerJson.Encode(data);new LedgerRequestSellers(data).Query(f,c,12000);ok(before.SequenceEqual(LedgerJson.Encode(data)),"projection does not mutate market observations or request data");
            Storage(ok,root,c);Boundary(ok,c,m);Preview(ok,root,c,m);
        }
        private static void Storage(Action<bool,string> ok,string root,LedgerContext c)
        {
            string folder=Path.Combine(root,"request-sellers-latest");var m=Market(c,30);string key=LedgerDatabase.Key(m);
            using(var db=new LedgerDatabase(folder)){
                db.Save(m);var partial=Copy(m);partial.FullCurrentAssortment=false;partial.Offers.Clear();ok(db.Save(partial)=="partial","real storage rejects partial disappearance without erasing stock");
                var absent=Copy(m);absent.Utc="2026-11-06T00:01:00Z";absent.Offers.Clear();db.Save(absent);
                var retained=db.ReadMarket(key).Offers.Single();ok(retained.RetainedRelation&&retained.Stock==null,"real storage retains missing item relationship but clears unproven quantity");
            }
            var view=LedgerViewData.Read(Path.Combine(folder,LedgerDatabase.FileName),c.WorldKey);var hint=Hint(view);ok(hint.State==LedgerRequestSellerStock.Unknown&&view.Offers(view.Items(null).Single(),true).Count==1,"existing Market still exposes retained relation while hint refuses old sufficiency");
            using(var db=new LedgerDatabase(folder)){var zero=Market(c,0);zero.Utc="2026-11-06T00:02:00Z";zero.Offers[0].BindingState="confirmed-native-exhaustion";db.Save(zero);}
            view=LedgerViewData.Read(Path.Combine(folder,LedgerDatabase.FileName),c.WorldKey);ok(Hint(view).Stock==0,"real latest read preserves exhausted supply zero");
            using(var db=new LedgerDatabase(folder)){var fresh=Market(c,40);fresh.Utc="2026-11-06T00:03:00Z";db.Save(fresh);}
            view=LedgerViewData.Read(Path.Combine(folder,LedgerDatabase.FileName),c.WorldKey);ok(Hint(view).State==LedgerRequestSellerStock.Sufficient,"actual newly recorded row restores sufficient observed seller normally");
        }
        private static void Boundary(Action<bool,string> ok,LedgerContext c,LedgerObservation m)
        {
            var f=Fact();var view=View(c,m);var model=new LedgerMarkerModel();string before=System.Text.Encoding.UTF8.GetString(LedgerJson.Encode(model.Data));
            new LedgerRequestSellers(view).Query(f,c,10001);
            ok(!LedgerRequestInventory.Match(f,LedgerRequestStock.Unknown("no actual inventory")).Ready&&model.Data.Destinations.Count==0&&before==System.Text.Encoding.UTF8.GetString(LedgerJson.Encode(model.Data)),"seller stock alone neither marks inventory ready nor creates delivery goal");
            string source=File.ReadAllText("src/TradeLedgerProbeB/LedgerRequestSellers.cs"),window=File.ReadAllText("src/TradeLedgerProbeB/LedgerRequestSellersWindow.cs");
            ok(!source.Contains("LedgerRequestInventory.Evaluate")&&!source.Contains(".Ensure(")&&!source.Contains("ReadInventorySnapshot")&&!source.Contains("Save(")&&!source.Contains(".Sum("),"pure seller hints cannot buy, reserve, activate markers, persist or sum sources");
            ok(window.Contains("SelectCatalogItem(hint.Target,true)")&&window.Contains("RequestInventoryAction(new Rect(")&&File.ReadAllText("src/TradeLedgerProbeB/LedgerRequestInventoryWindow.cs").Contains("RequestAnnotationClick(rect,key")&&!window.Contains("bookmarkRequested"),"existing Market and keyed press/release navigation only");
            var trail=new LedgerUiTrail<string>();trail.Push("requests:place+search+type");string state;ok(trail.Back(out state)&&state=="requests:place+search+type","existing Back trail retains request navigation state");
        }
        private static void Preview(Action<bool,string> ok,string root,LedgerContext c,LedgerObservation m)
        {
            foreach(int width in new[]{1280,1920,3840}){
                float scale=width/1280f;string path=Path.Combine(root,"request-sellers-"+width+".png");
                using(var bitmap=new Bitmap(width,width*9/16))using(var g=Graphics.FromImage(bitmap))using(var font=new Font("Arial",12*scale)){
                    g.Clear(Color.FromArgb(20,28,38));g.DrawString("NPC Requests - seller hints (NON-GAME PREVIEW)",font,Brushes.LightBlue,20*scale,18*scale);
                    var states=new[]{Hint(View(c,m)),Hint(View(c,m),tick:11200),Hint(View(c,Market(c,10))),Hint(View(c,Market(c,null))),Hint(View(c)),Hint(View(c,Market(c,0)))};
                    float y=66*scale;foreach(var h in states){var rect=new RectangleF(20*scale,y,width-40*scale,60*scale);g.FillRectangle(Brushes.DarkSlateGray,rect);g.DrawString(h.Text,font,h.Age==LedgerOfferAge.BeforeDeadline?Brushes.LightGreen:Brushes.White,new RectangleF(rect.X+8*scale,rect.Y+6*scale,rect.Width-16*scale,rect.Height-12*scale));y+=70*scale;}
                    g.DrawString("Backpack readiness and delivery markers still depend on actual inventory.",font,Brushes.LightGray,20*scale,y+14*scale);bitmap.Save(path);
                }ok(File.Exists(path),"fresh/historical/partial/unknown/zero non-game preview "+width);
            }
        }
    }
}
