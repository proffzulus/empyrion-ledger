using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public sealed partial class LedgerPrototypeWindow
    {
        private int placePanelTab;private bool placePanelDone;
        private string placeMarketSearch="",placeRequestSearch="",placePanelKey;
        private LedgerScope placePanelScope;private LedgerViewData placePanelData;private int placePanelGeography=-1;
        private List<LedgerViewOffer> placePanelOffers=new List<LedgerViewOffer>();private string placePanelOfferSearch;
        private readonly LedgerRequestListProjection placePanelRequests=new LedgerRequestListProjection();
        private void OpenPlaceContext(LedgerScope scope,bool requests)
        {
            if(scope==null)return;RememberPage();
            if(requests){requestFilters=new LedgerRequestFilters{Location=scope,Search=placeRequestSearch,Done=placePanelDone};requestPopup=0;RequestFilterChanged();view=1;}
            else{locationPicker=new LedgerScopePicker(scope.Kind){Key=scope.Key,Context=scope};locationSearch=placeMarketSearch;locationCacheKey=null;locationBuyScroll=locationSellScroll=Vector2.zero;view=2;}
        }
        private void ComparePlaceContext(LedgerScope scope,bool buy)
        {
            if(scope==null)return;RememberPage();var picker=new LedgerScopePicker(scope.Kind){Key=scope.Key,Context=scope};
            if(buy)sourcePicker=picker;else destinationPicker=picker;comparisonCacheKey=null;view=3;
        }
        private void DrawPlaceContext(Rect d,LedgerPlaceRow target)
        {
            string key=target==null?placeScope:target.Record.Key;
            if(!ReferenceEquals(placePanelData,data)||placePanelGeography!=data.GeographyRevision||placePanelKey!=key){
                bool changed=placePanelKey!=key;placePanelData=data;placePanelGeography=data.GeographyRevision;placePanelKey=key;
                placePanelScope=LedgerPlaceContext.Scope(placeProjection,placeScope,target);placePanelOfferSearch=null;
                if(changed)placeDetailScroll=Vector2.zero;
            }
            var scope=placePanelScope;float x=d.x+8,w=d.width-16;
            if(scope==null){Label(new Rect(x,d.y+8,w,50),"Place identity unavailable for scoped observations.");return;}
            float heading=Math.Max(30,Math.Min(78,richHeading.CalcHeight(new GUIContent(LedgerPresentation.ItemRich(scope.Label)),w)+6));
            var title=new Rect(x,d.y+6,w,heading);Color prior=GUI.contentColor;if(target!=null)GUI.contentColor=PlaceColor(target.FactionInfo.Color);Label(title,scope.Label,true);GUI.contentColor=prior;PlaceOverflow(title,scope.Label,richHeading);
            float top=d.y+heading+10;string[] tabs={"Overview","Market","Requests"};
            for(int i=0;i<3;i++)if(PlaceButton(new Rect(x+i*w/3,top,w/3-4,28),tabs[i],placePanelTab==i)){placePanelTab=i;placeDetailScroll=Vector2.zero;}
            top+=34;
            if(PlaceButton(new Rect(x,top,w*.48f,26),placePanelTab==2?"Full Requests":"Full Market"))OpenPlaceContext(scope,placePanelTab==2);
            if(UiMenuButton(new Rect(x+w*.5f,top,w*.5f,26),"Compare"+LedgerJournalUi.Dropdown,"Use this exact scope on Buy A or Sell B"))UiMenu(new Rect(x+w*.5f,top,w*.5f,26),"place-compare",new[]{"Buy from A","Sell to B"},-1,i=>ComparePlaceContext(scope,i==0));
            top+=34;
            if(locations==null)locations=data.Locations;
            string query=placePanelTab==1?placeMarketSearch:"";
            if(placePanelOfferSearch!=query){placePanelOfferSearch=query;placePanelOffers=data.LocatedOffers(locations,scope,true,query).Concat(data.LocatedOffers(locations,scope,false,query)).ToList();}
            if(placePanelTab==1){
                string search=Search(new Rect(x,top,w,28),placeMarketSearch,"PlaceMarketSearch","Search observed items");if(search!=placeMarketSearch){placeMarketSearch=search;placeDetailScroll=Vector2.zero;}
                top+=34;Label(new Rect(x,top,w*.56f,26),"Item",true);Label(new Rect(x+w*.56f,top,w*.23f,26),"Qty",true);Label(new Rect(x+w*.79f,top,w*.21f,26),"Price",true);top+=28;
                DrawRows(new Rect(x,top,w,Math.Max(30,d.yMax-top-8)),ref placeDetailScroll,placePanelOffers.Count,LedgerPlaceBrowserLayout.MarketRow,(i,r)=>{
                    var offer=placePanelOffers[i];OfferBackdrop(r,offer.Market);float width=r.width;
                    if(SelectRow(r,false))OpenItem(offer.Offer.ItemId+":"+(offer.Offer.AuxiliaryRaw.HasValue?offer.Offer.AuxiliaryRaw.Value.ToString():"unknown"),scope,offer);
                    Color color=GUI.contentColor;GUI.contentColor=OfferTextColor(offer.Market,color);
                    string name=(offer.Sellers?"Buy: ":"Sell: ")+(offer.Offer.Visible==null?"Item "+offer.Offer.ItemId:offer.Offer.Visible.Name);
                    GUI.Label(new Rect(r.x+4,r.y+3,width*.56f-8,26),LedgerPresentation.ItemRich(name),itemButton);
                    GUI.Label(new Rect(r.x+width*.56f,r.y+3,width*.23f-4,26),offer.Quantity,itemButton);
                    GUI.Label(new Rect(r.x+width*.79f,r.y+3,width*.21f-4,26),offer.Price,itemButton);GUI.contentColor=color;
                    if(r.Contains(Event.current.mousePosition))hoverLabel=LedgerPresentation.Text(name)+"\n"+data.VendorFor(offer.Market)+" / "+data.PlaceFor(offer.Market)+"\n"+LedgerFreshness.Hint(offer.Market,currentContext,offerGameTick)+"\n"+(offer.Sellers?"Stock: ":"Demand: ")+offer.Quantity+"; click for item details";
                },"No matching market observations.");return;
            }
            var filters=new LedgerRequestFilters{Location=scope,Done=placePanelDone,Search=placePanelTab==2?placeRequestSearch:""};
            var cards=placePanelRequests.Query(data,filters,RequestInventoryProgress,manualRevision);
            if(placePanelTab==2){
                for(int i=0;i<2;i++)if(PlaceButton(new Rect(x+i*86,top,80,27),i==0?"Pending":"Done",placePanelDone==(i==1))){placePanelDone=i==1;placeDetailScroll=Vector2.zero;}
                top+=33;placeRequestSearch=Search(new Rect(x,top,w,28),placeRequestSearch,"PlaceRequestSearch","Search requests");top+=34;
                DrawRows(new Rect(x,top,w,Math.Max(30,d.yMax-top-8)),ref placeDetailScroll,cards.Count,100,(i,r)=>{
                    var card=cards[i];if(SelectRow(r,false))OpenPlaceContext(scope,true);
                    Label(new Rect(r.x+4,r.y+2,r.width-8,25),data.VendorFor(card.Source),true);
                    GUI.Label(new Rect(r.x+4,r.y+29,r.width-8,44),LedgerPresentation.ItemRich(LedgerBookmarkPolicy.DeliveryLabel(card.Request.Fact,data.Catalog)),rich);
                    LedgerRequestInventoryProgress progress;var inventory=RequestInventoryProgress;string status=inventory!=null&&inventory.TryGetValue(LedgerRequestBrowser.StableKey(card),out progress)?progress.Status:"Stock unknown";
                    Label(new Rect(r.x+4,r.y+76,r.width-8,22),(placePanelDone?"Done":status)+"  "+LedgerRequestCardPresentation.Reward(card.Request.Fact));
                    if(r.Contains(Event.current.mousePosition))hoverLabel=LedgerBookmarkPolicy.DeliveryLabel(card.Request.Fact,data.Catalog)+"\nOpen full Requests for progress, seller evidence, Track / Deliver / Done and original dialogue";
                },data.ManualStateReady?"No matching request observations.":"Request state unavailable.");return;
            }
            var text=new List<string>();
            if(target!=null){text.Add(target.Record.Latest.Context.Playfield+" / "+(target.Record.Latest.Context.SolarSystem??"System unknown"));text.Add(target.FactionLabel);text.Add(target.PotentialSummary);text.Add(target.MarketSummary);text.Add(target.RequestSummary);}
            else{text.Add("Observed offers and demand: "+placePanelOffers.Count);text.Add("Select Market or Requests to inspect this location scope.");text.Add("No observations means unknown, not no traders or requests.");}
            float bodyWidth=w-24;var heights=text.Select(t=>Math.Max(28,rich.CalcHeight(new GUIContent(LedgerPresentation.Rich(t)),bodyWidth)+8)).ToArray();
            placeDetailScroll=GUI.BeginScrollView(new Rect(x,top,w,Math.Max(30,d.yMax-top-8)),placeDetailScroll,new Rect(0,0,bodyWidth,Math.Max(30,heights.Sum())),false,true);
            float y=0;for(int i=0;i<text.Count;i++){GUI.Label(new Rect(0,y,bodyWidth,heights[i]),LedgerPresentation.Rich(text[i]),rich);y+=heights[i];}GUI.EndScrollView();
        }
    }
}
