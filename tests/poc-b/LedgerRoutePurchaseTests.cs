using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Eleon.Modding;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal static class LedgerRoutePurchaseTests
    {
        private static T Copy<T>(T v){return LedgerJson.Decode<T>(LedgerJson.Encode(v));}
        private static LedgerContext C(){return LedgerContext.Create(Path.Combine(Path.GetTempPath(),"owned-route-purchases"),"Planet","System","SinglePlayer");}
        private static LedgerBookmarkPlan Plan(int npc,bool buy,string quantity="27",int item=400,int aux=0)
        {
            var c=C();var evidence=new LedgerObservation{Kind="market",Context=c,FullCurrentAssortment=true,GameTicks="10000",OfferFreshness=new LedgerOfferFreshness{State="observed-current-model",Source=LedgerFreshness.Source,RestockMinutes=1,ObservationGameTick=10000,NextRefreshTick=11200}};
            return new LedgerBookmarkPlan{World=c.WorldKey,Playfield=c.Playfield,NpcId=npc,StructureId=npc+100,X=npc,Y=5,Z=20,ItemId=item,Auxiliary=aux,PlayerBuys=buy,ItemName="Mining Equipment",UnitPrice=buy?"14378":"22001",Quantity=quantity,Place="Fixture destination",ObservedUtc="2026-11-06T10:00:00Z",RefreshObservation=evidence};
        }
        private static LedgerInventoryContext Part(string name,int amount=0,int aux=0,string key="slot")
        {return new LedgerInventoryContext(name,LedgerInventoryAvailability.Available,"fixture","",amount==0?new ItemStack[0]:new[]{new ItemStack{id=400,count=amount,ammo=aux}},amount==0?new string[0]:new[]{key},name);}
        private static LedgerInventorySnapshot Snapshot(int amount,LedgerInventoryLifetime life=null,ulong tick=10001,LedgerInventoryContext active=null,LedgerInventoryContext cursor=null,int aux=0)
        {life=life??new LedgerInventoryLifetime();var c=C();return new LedgerInventorySnapshot(life,life.Invalidate(),c.LocalSaveKey+"|"+c.Playfield,1002,tick,Part("permanent",999),Part("bag",amount,aux),active??Part("active"),cursor??Part("cursor"));}
        private static LedgerRequestStock Stock(int held,int aux=0){return LedgerRequestStock.Read(Snapshot(held,aux:aux));}
        private static void Eval(LedgerMarkerModel m,LedgerRequestStock stock,ulong tick=10001)
        {var c=C();var places=new LedgerProductionPlaces(c.WorldKey,new[]{c});LedgerTradeUpdates.Evaluate(m,c,1002,tick,places);LedgerRoutePurchases.Evaluate(m,c,1002,tick,stock,places);}
        private static LedgerMarkerRoute Route(LedgerMarkerModel m,string name,params LedgerMarkerDestination[] points)
        {var r=m.AddRoute(name,C().WorldKey,1002);foreach(var d in points)m.Membership(d.Purposes.Single(),r.Id,true);return r;}
        private static LedgerRoutePurchase Detail(LedgerMarkerDestination d){return d.Purposes.Single().RoutePurchases.Single();}
        private static string Label(LedgerMarkerModel m,LedgerMarkerDestination d){return LedgerPresentation.Text(m.Project(d).Label);}
        internal static void Run(Action<bool,string> check,string root)
        {
            Action<bool,string> ok=(v,n)=>check(v,"route purchases "+n);var m=new LedgerMarkerModel();var seller=m.Ensure(Plan(1,true),1002);var a=m.Ensure(Plan(2,false,"12"),1002);var b=m.Ensure(Plan(3,false,"16"),1002);var u=m.Ensure(Plan(4,false,null),1002);var r=Route(m,"Rig route",seller,a,b,u);
            Eval(m,Stock(25));ok(Detail(seller).Demand==28&&Detail(seller).UnknownBuyers==1&&Detail(seller).Deficit==3,"fresh known subtotal28 survives third unknown demand and subtracts25");
            ok(Label(m,seller).Contains("Buy: 3 Mining Equipment 14378 (27)"),"exact compact requested format retains own seller price and stock");
            ok(seller.Purposes[0].Plan.Quantity=="27"&&a.Purposes[0].Plan.Quantity=="12"&&a.Purposes[0].Plan.UnitPrice=="22001","derived shortage never mutates seller stock or buyer facts");
            Eval(m,Stock(0));ok(Detail(seller).Deficit==28&&Label(m,seller).Contains("Buy: 28"),"confirmed held zero gives full known demand");
            Eval(m,Stock(100));ok(Detail(seller).Deficit==0&&!m.Project(seller).Visible&&m.Project(a).Visible&&m.Data.Routes.Contains(r),"nonnegative zero hides purchase only and retains buyer/route");
            Eval(m,LedgerRequestStock.Unknown("disconnected"));ok(Detail(seller).Demand==28&&Detail(seller).Deficit==null&&!Label(m,seller).Contains("Buy: 28")&&Label(m,seller).Contains("Buy: Mining Equipment"),"unknown inventory preserves known demand but never treated as zero");
            var lower=Stock(25);lower.CarryComplete=false;Eval(m,lower);ok(Detail(seller).Held==25&&Detail(seller).Deficit==null&&Detail(seller).Details.Contains("held 25+"),"partial known inventory is a lower bound, no exact number");
            var wrong=Stock(25);wrong.PhysicalScope="other-save|Planet";Eval(m,wrong);ok(Detail(seller).Held==null&&Detail(seller).Deficit==null,"physical save mismatch invalidates inventory");
            wrong=Stock(25);wrong.Player=2003;Eval(m,wrong);ok(Detail(seller).Held==null,"other player not current held inventory");
            a.Purposes[0].Plan.RefreshObservation.OfferFreshness.NextRefreshTick=10001;Eval(m,Stock(25));ok(Detail(seller).Demand==16&&Detail(seller).StaleBuyers==1&&Detail(seller).Deficit==null&&!Label(m,seller).Contains("Buy: 3"),"known stale demand omitted from subtotal and blocks exact native deficit");
            a.Purposes[0].Plan.Quantity=null;Eval(m,Stock(0));ok(Detail(seller).Demand==16&&Detail(seller).Deficit==16&&Detail(seller).UnknownBuyers==2,"unknown buyer quantity does not suppress known fresh16 even with unknown timing");
            a.Purposes[0].Plan.Quantity="-12";Eval(m,Stock(0));ok(Detail(seller).Demand==16&&Detail(seller).Deficit==16,"negative buyer quantity not invented or subtracted");
            a.Purposes[0].Plan.Quantity="0";a.Purposes[0].Plan.RefreshObservation=Copy(b.Purposes[0].Plan.RefreshObservation);Eval(m,Stock(0));ok(Detail(seller).Demand==16&&Detail(seller).KnownBuyers==2,"fresh observed zero distinct from unknown buyer");
            a.Purposes[0].Plan.Quantity="2147483647";b.Purposes[0].Plan.Quantity="2147483647";Eval(m,Stock(0));ok(Detail(seller).Demand==4294967294L&&Detail(seller).Deficit==4294967294L,"large sums use long without integer overflow");
            a.Purposes[0].Plan.Quantity=long.MaxValue.ToString();Eval(m,Stock(0));ok(Detail(seller).Overflow&&Detail(seller).Deficit==null,"out-of-range aggregate fails exactness without overflow fiction");
            seller.Purposes[0].Plan.RefreshObservation.OfferFreshness.NextRefreshTick=10001;a.Purposes[0].Plan.Quantity="12";b.Purposes[0].Plan.Quantity="16";Eval(m,Stock(25));ok(Label(m,seller).Contains("Buy (old): 3 Mining Equipment")&&!Label(m,seller).Contains("14378"),"concrete stale supplier retains green Buy intent and current deficit without old quote numbers");
            seller.Purposes[0].Plan.RefreshObservation=Copy(b.Purposes[0].Plan.RefreshObservation);seller.Purposes[0].Plan.Quantity="1";Eval(m,Stock(25));ok(Label(m,seller).Contains("Buy: 3 Mining Equipment 14378 (1)"),"seller stock never caps or reallocates shared deficit");
            m.Ensure(Plan(2,false,"12"),1002);Eval(m,Stock(0));ok(Detail(seller).Demand==28,"repeat buyer Mark never doubles designated demand");
            a.Purposes[0].Availability="ambiguous observed rows";Eval(m,Stock(0));ok(Detail(seller).Demand==16&&Detail(seller).UnknownBuyers==2,"ambiguous buyer offer rows contribute unknown not duplicate quantities");
            Variants(ok);MultipleSellers(ok);Overlap(ok);Reconciliation(ok);Persistence(ok,root);Preview(ok,root);
        }
        private static void Variants(Action<bool,string> ok)
        {
            var m=new LedgerMarkerModel();var s=m.Ensure(Plan(1,true,aux:1),1002);var b=m.Ensure(Plan(2,false,"28",aux:1),1002);var other=m.Ensure(Plan(3,false,"999",aux:0),1002);Route(m,"Variant route",s,b,other);
            Eval(m,Stock(25,0));ok(Detail(s).Demand==28&&Detail(s).Held==0&&Detail(s).Deficit==28,"same ID different auxiliary inventory not subtracted or buyer demand pooled");
            Eval(m,Stock(25,1));ok(Detail(s).Held==25&&Detail(s).Deficit==3,"exact native auxiliary/stack ammo identity matches");
            var life=new LedgerInventoryLifetime();var cursor=Part("cursor",5,1,"held");var snap=Snapshot(0,life,cursor:cursor,aux:1);ok(LedgerRequestStock.Read(snap).Variant(400,1)==5,"visible cursor variant counted through same exact source keys");
            var sampler=new LedgerRequestInventorySampler();sampler.Accept(Snapshot(25,life,aux:0));ok(!sampler.Accept(Snapshot(25,life,tick:10002,aux:1)).Current,"metadata-only variant change must settle again");
            var overlap=Snapshot(25,cursor:Part("cursor",25,0,"slot"));ok(!LedgerRequestStock.Read(overlap).Current,"equal-count bag/cursor overlap protection preserved");
            var split=Snapshot(12,cursor:Part("cursor",13,0,"split"));ok(LedgerRequestStock.Read(split).Variant(400,0)==25,"distinct split source stacks count once");
            var bar=Snapshot(0,active:Part("linked-toolbar",25,0,"linked"));ok(LedgerRequestStock.Read(bar).Variant(400,0)==25,"admitted active linked toolbar participates without permanent toolbar duplicate");
            ok(Stock(0).Variant(400,null)==null&&Stock(0).Variant(400,-1)==null,"unidentified auxiliary cannot become confirmed zero");
        }
        private static void MultipleSellers(Action<bool,string> ok)
        {
            var m=new LedgerMarkerModel();var a=m.Ensure(Plan(1,true,"20"),1002);var b=m.Ensure(Plan(2,true,"30"),1002);b.Purposes[0].Plan.UnitPrice="15000";var buyer=m.Ensure(Plan(3,false,"50"),1002);var r=Route(m,"Two sellers",a,b,buyer);LedgerRouteFlow.Mode(m,r,"auto");
            Eval(m,Stock(0));var native=new Native();var sync=new LedgerMarkerCoordinator(m);sync.Sync(native,C().WorldKey,1002);int bId=b.Native.Id;int creates=native.Creates;ok(Label(m,a).Contains("Buy: 50 Mining Equipment 14378 (20)")&&Label(m,b).Contains("Buy: 50 Mining Equipment 15000 (30)"),"both sellers show common50 independently of stock20/30");
            var trade=new LedgerTradeReceipt{TradeId="actual-buy20",Confirmed=true,Context=C(),PlayerId=1002,Identity=new LedgerIdentity{NpcEntityId=1,StructureEntityId=101},ItemId=400,Auxiliary=0,Action="Bought",Quantity=20,Utc="2026-11-06T10:01:00Z"};LedgerRouteFlow.Trade(m,trade);
            Eval(m,LedgerRequestStock.Unknown("settling after purchase"));sync.Sync(native,C().WorldKey,1002);
            ok(b.Native.Id==bId&&!native.Items[bId].Label.Contains("Buy: 50"),"partial purchase retains same second marker with uncertain quantity instead of replaying old50");
            int afterPurchaseCreates=native.Creates;Eval(m,Stock(20));sync.Sync(native,C().WorldKey,1002);ok(b.Native.Id==bId&&native.Creates==afterPurchaseCreates,"settled purchase20 changes second seller quantity in place, no allocation/recreation");
            ok(Detail(a).Deficit==30&&Detail(b).Deficit==30&&Label(m,b).Contains("Buy: 30 Mining Equipment 15000 (30)"),"actual purchase plus current inventory20 resumes common remaining30 despite old cargo phase");
            ok(!m.Project(a).Visible&&m.Project(buyer).Visible&&a.Purposes[0].Plan.Quantity=="0","exhausted first seller follows existing Auto disappearance while buyer stays useful");
            m.ShowRoute(r,true);Eval(m,Stock(20));ok(Label(m,a).Contains("Buy (check): 30 Mining Equipment")&&a.Purposes[0].Plan.Quantity=="0"&&Label(m,b).Contains("Buy: 30"),"manual exhausted route seller keeps direction/deficit with check qualifier and no fake stock");
            Eval(m,Stock(50));ok(!m.Project(a).Visible&&!m.Project(b).Visible&&m.Project(buyer).Visible,"all purchase guidance stops when covered; sale marker retained");
            Eval(m,Stock(40));ok(Label(m,b).Contains("Buy: 10")&&m.Data.Routes.Count==1,"sales/transfers reducing accessible stock rearm remaining10 without rebuilding route");
        }
        private static void Overlap(Action<bool,string> ok)
        {
            var m=new LedgerMarkerModel();var s=m.Ensure(Plan(1,true),1002);var a=m.Ensure(Plan(2,false,"28"),1002);var b=m.Ensure(Plan(3,false,"30"),1002);var x=Route(m,"Route A",s,a);var y=Route(m,"Route B",s,b);Eval(m,Stock(25));
            ok(s.Purposes[0].RoutePurchases.Select(h=>h.Deficit).OrderBy(n=>n).SequenceEqual(new long?[]{3,5}),"overlapping route goals independently consider same25 without reservation");
            string text=Label(m,s);ok(text.Contains("Buy: 3")&&text.Contains("[Route A]")&&text.Contains("Buy: 5")&&text.Contains("[Route B]"),"different deficits on shared native marker retain route attribution, never sum8");
            m.ShowRoute(y,false);Eval(m,Stock(25));ok(Label(m,s).Contains("Buy: 3")&&!Label(m,s).Contains("Buy: 5"),"hidden unrelated goal does not leak into shared label");
            m.ShowRoute(y,true);b.Purposes[0].Plan.Quantity="28";Eval(m,Stock(25));ok(Label(m,s).Split(new[]{"Buy:"},StringSplitOptions.None).Length==2,"same stock and equal route deficit deduplicates identical clause");
            m.Membership(b.Purposes[0],y.Id,false);Eval(m,Stock(25));ok(Label(m,s).Contains("[Route B]")&&DetailFor(s,x).Deficit.HasValue,"unspecified shopping goal remains separate from concrete amount");
            m.DeleteRoute(y.Id);Eval(m,Stock(25));ok(s.Purposes[0].RoutePurchases.Count==1&&Label(m,s).Contains("Buy: 3"),"route removal recalculates without touching surviving goal");
            var plain=m.Ensure(Plan(99,true),1002);Eval(m,Stock(0));ok(m.WorldSummary(plain.Purposes[0])=="Buy: Mining Equipment 14378 (27)","standalone generic Buy unchanged");
            var many=new LedgerMarkerModel();var shared=many.Ensure(Plan(50,true),1002);
            for(int i=0;i<8;i++){var point=many.Ensure(Plan(60+i,false,(i+1).ToString()),1002);Route(many,"A long distinct route name for compact label "+i,shared,point);}
            Eval(many,Stock(0));ok(Label(many,shared).Length<=480&&Label(many,shared).Contains("route goals")&&shared.Purposes[0].RoutePurchases.Count==8,"many overlapping goals keep compact HUD budget and complete journal attribution");
            ok(m.WorldSummary(a.Purposes[0])=="Sell: Mining Equipment 22001 (28)","generic Sell price/stock and formatting unchanged");
        }
        private static LedgerRoutePurchase DetailFor(LedgerMarkerDestination d,LedgerMarkerRoute r){return d.Purposes[0].RoutePurchases.Single(h=>h.Route==r.Id);}
        private sealed class Native:ILedgerMarkerBackend
        {
            internal Dictionary<int,LedgerMarkerNative> Items=new Dictionary<int,LedgerMarkerNative>();internal int Creates,Updates,Deletes;
            public IDictionary<int,LedgerMarkerNative> Read(){return Items.ToDictionary(x=>x.Key,x=>Copy(x.Value));}
            public void Create(LedgerMarkerDestination d,LedgerMarkerProjection p,Action<LedgerMarkerNative> done){var n=new LedgerMarkerNative{Id=++Creates,Player=d.Player,Type=3,Playfield=d.Playfield,X=d.X,Y=d.Y,Z=d.Z,Label=p.Label,Hud=p.Visible,Universe=p.Visible,Approach=p.Approach};Items[n.Id]=n;done(Copy(n));}
            public LedgerMarkerNative Update(LedgerMarkerNative n,LedgerMarkerProjection p){if(!n.Matches(Items[n.Id]))throw new Exception("foreign update");var v=Copy(n);v.Label=p.Label;v.Hud=v.Universe=p.Visible;v.Approach=p.Approach;Items[n.Id]=v;Updates++;return Copy(v);}
            public void Delete(LedgerMarkerNative n){if(!n.Matches(Items[n.Id]))throw new Exception("foreign delete");Items.Remove(n.Id);Deletes++;}
        }
        private static void Reconciliation(Action<bool,string> ok)
        {
            var m=new LedgerMarkerModel();var s=m.Ensure(Plan(1,true),1002);var buyer=m.Ensure(Plan(2,false,"17"),1002);var r=Route(m,"Live route",s,buyer);var native=new Native();var coordinator=new LedgerMarkerCoordinator(m);var sampler=new LedgerRequestInventorySampler();var life=new LedgerInventoryLifetime();
            Action<int> poll=held=>{Eval(m,sampler.Accept(Snapshot(held,life)));coordinator.Sync(native,C().WorldKey,1002);};
            poll(0);poll(0);int id=s.Native.Id;int created=native.Creates;ok(native.Items[id].Label.Contains("Buy: 17"),"real sampler/projection/coordinator pipeline starts active17 without UI");
            poll(7);ok(s.Native.Id==id&&!native.Items[id].Label.Contains("Buy: 17")&&!native.Items[id].Label.Contains("Buy: 10"),"first changed transfer frame removes stale exact deficit in place");
            poll(7);ok(s.Native.Id==id&&native.Items[id].Label.Contains("Buy: 10")&&native.Creates==created,"periodic storage-to-accessible7 updates SAME active marker17 to10");
            int updates=native.Updates;poll(7);poll(7);ok(native.Updates==updates,"unchanged periodic samples do not write native markers");
            Eval(m,LedgerRequestStock.Unknown("link lost"));coordinator.Sync(native,C().WorldKey,1002);ok(s.Native.Id==id&&!native.Items[id].Label.Contains("Buy: 10"),"unknown source transition promptly drops exact number without recreation");
            poll(7);poll(7);ok(native.Items[id].Label.Contains("Buy: 10"),"settled supported source restores accurate deficit");
            Action<LedgerInventorySnapshot> frame=snapshot=>{Eval(m,sampler.Accept(snapshot));coordinator.Sync(native,C().WorldKey,1002);};
            frame(Snapshot(0,life,active:Part("linked-toolbar",7,0,"linked")));frame(Snapshot(0,life,active:Part("linked-toolbar",7,0,"linked")));
            ok(s.Native.Id==id&&native.Items[id].Label.Contains("Buy: 10"),"closed-journal coordinator follows changed admitted linked toolbar on same marker");
            frame(Snapshot(0,life,active:new LedgerInventoryContext("active",LedgerInventoryAvailability.Unavailable,"fixture","range lost")));
            ok(s.Native.Id==id&&!native.Items[id].Label.Contains("Buy: 10"),"range loss removes exact quantity without assuming empty linked stock");
            poll(7);poll(7);ok(native.Items[id].Label.Contains("Buy: 10"),"normal accessible source restores periodic exact deficit after link invalidation");
            poll(17);poll(17);ok(s.Native==null&&m.Data.Destinations.Contains(s)&&m.Data.Routes.Contains(r)&&buyer.Native!=null,"zero removes temporary purchase waypoint only, retains logical route/buyer");
            poll(7);poll(7);ok(s.Native!=null&&native.Items[s.Native.Id].Label.Contains("Buy: 10")&&m.Data.Destinations.Count==2,"return of shortage reprojects logical seller without route reconstruction");
            native.Items.Remove(s.Native.Id);poll(7);ok(s.Native!=null&&native.Items.ContainsKey(s.Native.Id),"native disappearance still recreates active needed purchase");
            m.ShowRoute(r,false);s.Purposes[0].TrackUpdates=false;poll(7);ok(s.Native==null,"Hide plus no tracking does not resurrect purchase guidance");
            LedgerRouteFlow.Mode(m,r,"auto");var prefs=LedgerProductionServices.Preferences(m,C().WorldKey,1002);prefs.For("all",true).Suppress=true;poll(7);ok(s.Native==null,"explicit inherited Suppress mutes automatic guidance");
            prefs.Overrides.Clear();poll(7);ok(s.Native!=null,"unsuppress restores still-needed automatic route");
            m.RemovePurpose(s,s.Purposes[0]);poll(7);ok(s.Purposes.Count==0&&s.Native==null,"explicit purpose removal cannot be rebuilt by inventory timer");
            string host=File.ReadAllText("src/TradeLedgerProbeB/LedgerNativeBookmarks.cs"),window=File.ReadAllText("src/TradeLedgerProbeB/LedgerPrototypeWindow.cs");
            ok(host.Contains("now-refreshed<500")&&host.Contains("LedgerRoutePurchases.HasGoals(Model,context.WorldKey,Player)")&&window.Contains("if(ready&&context!=null&&context.Recordable&&NativeBookmarks!=null)NativeBookmarks.Refresh"),"host samples route goals on existing500ms cadence independently of open journal or Pending NPC requests, only in valid current context");
            ok(host.Contains("LedgerRoutePurchases.Reset(Model)")&&File.ReadAllText("src/TradeLedgerProbeB/LedgerInventoryAccess.cs").Contains("ammo=(int)F(0x2fec,item)"),"unload/error resets exact guidance and cursor retains native auxiliary field");
        }
        private static void Persistence(Action<bool,string> ok,string root)
        {
            var m=new LedgerMarkerModel();var s=m.Ensure(Plan(1,true),1002);var b=m.Ensure(Plan(2,false,"28"),1002);Route(m,"Saved route",s,b);Eval(m,Stock(25));
            string json=System.Text.Encoding.UTF8.GetString(LedgerJson.Encode(m.Data));ok(!json.Contains("RoutePurchases")&&!json.Contains("PurchaseSatisfied")&&!json.Contains("Variants"),"derived inventory/deficits are never persisted as reusable stock");
            string path=Path.Combine(root,"route-purchases-portable","Mods","TradeLedger","LedgerMarkers.json");var repo=new LedgerMarkerRepository(path);repo.Save(m.Data);ok(repo.Wait(5000),"existing atomic portable metadata path writes unchanged durable route schema");
            var restored=new LedgerMarkerModel(new LedgerMarkerRepository(path).Read());Eval(restored,LedgerRequestStock.Unknown("restart"));ok(restored.Data.Routes.Count==1&&!Label(restored,restored.Data.Destinations[0]).Contains("Buy: 3"),"restart keeps route but cannot reuse previous held25");
            Eval(restored,Stock(25));ok(Label(restored,restored.Data.Destinations[0]).Contains("Buy: 3"),"fresh admitted inventory restores derived quantity after reload");
        }
        private static void Preview(Action<bool,string> ok,string root)
        {
            foreach(int width in new[]{1280,1920,3840}){float scale=width/1280f;string path=Path.Combine(root,"route-purchases-"+width+".png");using(var image=new Bitmap(width,width*9/16))using(var g=Graphics.FromImage(image))using(var font=new Font("Arial",14*scale)){
                g.Clear(Color.FromArgb(20,28,38));g.DrawString("NON-GAME PREVIEW: existing marker projection",font,Brushes.LightBlue,20*scale,20*scale);float y=75*scale;
                foreach(var spec in new[]{new[]{"28","25","27"},new[]{"50","0","20"},new[]{"50","20","30"},new[]{"17","7","27"}}){var m=new LedgerMarkerModel();var s=m.Ensure(Plan(1,true,spec[2]),1002);var b=m.Ensure(Plan(2,false,spec[0]),1002);Route(m,"Rig route",s,b);Eval(m,Stock(int.Parse(spec[1])));g.DrawString(Label(m,s),font,Brushes.LightGreen,new RectangleF(20*scale,y,width-40*scale,70*scale));y+=85*scale;}
                g.DrawString("Unknown inventory or stale demand: Buy: Mining Equipment 14378 (27)",font,Brushes.White,20*scale,y);image.Save(path);
            }ok(File.Exists(path),"compact native-label non-game preview "+width);}
        }
    }
}
