using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public sealed partial class LedgerPrototypeWindow
    {
        private LedgerLocationIndex locations;
        private LedgerNativeGeography nativeGeography;
        private bool nativeGeographyAttempted;
        private LedgerScope itemLocation;
        private LedgerScopePicker locationPicker=new LedgerScopePicker(LedgerScopeKind.Poi),sourcePicker=new LedgerScopePicker(LedgerScopeKind.System),destinationPicker=new LedgerScopePicker(LedgerScopeKind.System);
        private Vector2 locationChoices,sourceChoices,destinationChoices,locationBuyScroll,locationSellScroll,comparisonScroll;
        private string locationSearch="",comparisonSearch="",locationCacheKey,locationFilterKey,comparisonCacheKey,selectedPair;
        private List<LedgerViewOffer> locationBuys=new List<LedgerViewOffer>(),locationSells=new List<LedgerViewOffer>();
        private int comparisonGeneration;private readonly LedgerViewReadWork comparisonRead=new LedgerViewReadWork();private LedgerViewData comparisonRequestedData,comparisonResultData,comparisonPendingData;private int comparisonRequestedGeo,comparisonResultGeo,comparisonPendingGeo;private string comparisonWantedKey,comparisonDisplayKey,comparisonPendingKey;
        private LedgerComparisonSort comparisonSort=LedgerComparisonSort.TotalDelta;
        private bool comparisonDescending=true,comparisonLoading;
        private readonly LedgerOfferSortCache locationBuyCache=new LedgerOfferSortCache(),locationSellCache=new LedgerOfferSortCache();
        private readonly LedgerHeaderSort locationBuySort=new LedgerHeaderSort(),locationSellSort=new LedgerHeaderSort(),comparisonHeaderSort=new LedgerHeaderSort();
        private List<LedgerComparisonRow> sortedPairSource,sortedPairs;
        private LedgerViewData sortedPairData;
        private int sortedPairColumn=-1;private bool sortedPairDescending;
        private LedgerComparisonResult comparisonResult=new LedgerComparisonResult(),comparisonPending;
        private string comparisonError;
        private void ResetLocationViews()
        {
            CloseUiMenu();returnPages.Clear();locations=null;itemLocation=null;locationPicker=new LedgerScopePicker(LedgerScopeKind.Poi);sourcePicker=new LedgerScopePicker(LedgerScopeKind.System);destinationPicker=new LedgerScopePicker(LedgerScopeKind.System);
            locationCacheKey=locationFilterKey=null;selectedPair=null;CloseComparisonWork();
        }
        private void CloseComparisonWork(bool preserve=false)
        {lock(resultGate){comparisonGeneration++;comparisonPending=null;comparisonLoading=false;comparisonCacheKey=null;comparisonError=null;if(!preserve){comparisonResult=new LedgerComparisonResult();comparisonResultData=null;comparisonDisplayKey=null;}}}
        private void OnLocationsLoaded()
        {
            try
            {
                if(!nativeGeographyAttempted&&Application!=null){nativeGeographyAttempted=true;nativeGeography=new LedgerNativeGeography(Application.GetType().Assembly);}
                if(nativeGeography!=null)nativeGeography.Enrich(data,Application,currentContext,DateTime.UtcNow.Ticks/TimeSpan.TicksPerMillisecond);
            }
            catch(Exception e){Log("LedgerGeographyUnavailable",e.GetType().Name+";stored/header metadata retained");}
            data.PrepareDistances();
            playerPoint=LedgerDistanceRefresh.Replace(playerPoint,currentContext,data.ManualPlayerId,manualPlayer,()=>nativeGeography==null?null:nativeGeography.Read(currentContext,Application));
            locations=null;locationCacheKey=null;CloseComparisonWork(true);
            Log("LedgerGeographyReady","observedOnly=true index=deferred-until-location-view");
        }
        private LedgerScope DrawScopePicker(Rect rect,string title,LedgerScopePicker picker,ref Vector2 scroll,bool levels=false)
        {
            Label(new Rect(rect.x,rect.y,130,26),title);
            string[] art={"IconWaypoint","IconPlanet","IconMap","IconSolarSystem"};int kind=(int)picker.Kind;
            var kindRect=new Rect(rect.x+130,rect.y,148,26);
            if(levels){for(int n=0;n<4;n++){var button=new Rect(rect.x+130+n*46,rect.y,44,28);ControlFrame(button,n==kind);if(ArtButton(new Rect(button.x+2,button.y+2,button.width-4,button.height-4),art[n],n==0?"POI":n==1?"Body":n==2?"Sec":"Sys",LedgerJournalUi.Scopes[n],n==kind))picker.Change((LedgerScopeKind)n);if(n==kind){Color color=GUI.color;GUI.color=new Color(.35f,.9f,1f);GUI.DrawTexture(new Rect(button.x+2,button.yMax-3,button.width-4,2),Texture2D.whiteTexture);GUI.color=color;}}}
            else if(UiMenuButton(kindRect,LedgerJournalUi.Scopes[kind]+LedgerJournalUi.Dropdown,"Location scope"))UiMenu(kindRect,"scope:"+title,LedgerJournalUi.Scopes,kind,i=>picker.Change((LedgerScopeKind)i));
            // Borrow only an actually available native atlas asset; readable text remains the fallback.
            if(!levels&&HasUiArt(art[kind])){var icon=nativeArt.Get(art[kind]);GUI.DrawTextureWithTexCoords(new Rect(rect.x+282,rect.y+3,20,20),icon.Texture,icon.Uv);}
            var selected=picker.Resolve(locations);var choices=locations.Scopes(picker.Kind);
            Rect choiceRect=new Rect(rect.x,rect.y+32,rect.width,28);
            if(Button(choiceRect,selected==null?"Choose location":LedgerUi.ScopeLabel(selected)+"  \u25BE"))picker.Expanded=!picker.Expanded;
            return selected;
        }
        private void DrawScopeChoices(Rect rect,LedgerScopePicker picker,ref Vector2 scroll)
        {
            if(!picker.Expanded)return;
            GUI.Box(rect,"",row);var choices=locations.Scopes(picker.Kind);
            DrawRows(rect,ref scroll,choices.Count,LedgerLayout.ControlHeight,(i,r)=>{if(SelectRow(r,picker.Key==choices[i].Key)){picker.Key=choices[i].Key;picker.Expanded=false;}Label(r,LedgerUi.ScopeLabel(choices[i]));});
        }
        private void OpenItem(string key,LedgerScope scope,LedgerViewOffer offer)
        {RememberItemEntry(key);selectedKey=key;itemLocation=scope;selectedOfferKey=offer==null?null:LedgerBrowse.OfferKey(offer.Market,offer.Offer,offer.Sellers);search="";view=0;}
        private void DrawOfferBookmark(Rect rect,LedgerViewOffer offer)
        {
            LedgerBookmarkPlan plan;string reason;bool can=LedgerBookmarkPolicy.TryCreate(data,offer,currentContext,out plan,out reason);
            DrawRowBookmark(rect,plan,can,reason,"Mark");
        }
        private void DrawRowBookmark(Rect rect,LedgerBookmarkPlan plan,bool can,string reason,string label)
        {
            var destination=can&&NativeBookmarks!=null?NativeBookmarks.Destination(plan):null;
            bool known=can&&NativeBookmarks!=null&&NativeBookmarks.HasPurpose(plan);
            string hint=LedgerJournalUi.BookmarkHint(can,reason,NativeBookmarks!=null&&NativeBookmarks.Ready,destination!=null&&destination.Conflict,destination!=null&&destination.Conflict?destination.Status:NativeBookmarks!=null&&NativeBookmarks.SaveFailed?NativeBookmarks.Status:null);
            if(rect.Contains(Event.current.mousePosition))iconHoverName=hint;
            bool enabled=GUI.enabled;GUI.enabled=enabled&&can&&NativeBookmarks!=null&&NativeBookmarks.Ready&&!loading&&bookmarkRequested==null;
            Color color=GUI.color;if(known)GUI.color=new Color(.5f,.9f,1f);
            if(GUI.Button(rect,new GUIContent(label,hint)))bookmarkRequested=plan;
            GUI.color=color;GUI.enabled=enabled;
        }
        private void DrawLocations()
        {
            if(locations==null)locations=data.Locations;Rect c=Content;bool contentEnabled=GUI.enabled;
            float pickerWidth=Math.Min(640,c.width*.46f);
            var scope=DrawScopePicker(new Rect(c.x,c.y,pickerWidth,62),"Location",locationPicker,ref locationChoices,true);

            locationSearch=Search(new Rect(c.x+pickerWidth+24,c.y+32,c.width-pickerWidth-24,28),locationSearch,"LocationSearch","Search items");
            string signature=(scope==null?"none":scope.Key)+"|"+locationSearch;
            if(signature!=locationCacheKey){bool changed=locationFilterKey!=signature;locationCacheKey=locationFilterKey=signature;locationBuys=data.LocatedOffers(locations,scope,true,locationSearch);locationSells=data.LocatedOffers(locations,scope,false,locationSearch);if(changed)locationBuyScroll=locationSellScroll=Vector2.zero;}
            GUI.enabled=contentEnabled&&!locationPicker.Expanded;
            float h=(c.height-84)/2;
            DrawLocationOffers(new Rect(c.x,c.y+72,c.width,h),LedgerJournalUi.Side(true),locationBuys,scope,ref locationBuyScroll);
            DrawLocationOffers(new Rect(c.x,c.y+78+h,c.width,h),LedgerJournalUi.Side(false),locationSells,scope,ref locationSellScroll);
            GUI.enabled=contentEnabled;
            // Draw popup last so it overlays only the Ledger surface, without pushing rows offscreen.
            DrawScopeChoices(new Rect(c.x,c.y+64,pickerWidth,Math.Min(260,c.height-80)),locationPicker,ref locationChoices);
        }
        private void DrawLocationOffers(Rect area,string title,List<LedgerViewOffer> rows,LedgerScope scope,ref Vector2 scroll)
        {
            Label(new Rect(area.x,area.y,area.width,26),title,true);float[] columns={.29f,.09f,.09f,.22f,.23f,.08f};
            if(TableHeader(new Rect(area.x,area.y+29,area.width-LedgerLayout.Scrollbar-3,25),new[]{"Item","Unit price",title.StartsWith("Buy",StringComparison.Ordinal)?"Stock":"Demand","Vendor","Place",""},columns,title.StartsWith("Buy",StringComparison.Ordinal)?locationBuySort:locationSellSort))scroll=Vector2.zero;
            rows=(title.StartsWith("Buy",StringComparison.Ordinal)?locationBuyCache:locationSellCache).Apply(rows,title.StartsWith("Buy",StringComparison.Ordinal)?locationBuySort:locationSellSort,new[]{LedgerOfferColumn.Item,LedgerOfferColumn.Price,LedgerOfferColumn.Quantity,LedgerOfferColumn.Vendor,LedgerOfferColumn.Place},data,playerPoint);
            DrawRows(new Rect(area.x,area.y+55,area.width,area.height-55),ref scroll,rows.Count,LedgerLayout.TableRow,(i,r)=>
            {
                var offer=rows[i];string key=LedgerBrowse.OfferKey(offer.Market,offer.Offer,offer.Sellers);
                OfferBackdrop(new Rect(r.x,r.y,r.width*.92f,r.height),offer.Market);
                // A normal row selects; clicking its item cell also opens item browsing with scope retained.
                DrawOfferBookmark(new Rect(r.x+r.width*.92f,r.y+2,r.width*.08f-3,r.height-4),offer);
                bool clicked=SelectRow(new Rect(r.x,r.y,r.width*.92f,r.height),selectedOfferKey==key);
                if(clicked){selectedOfferKey=key;if(Event.current.mousePosition.x<r.width*columns[0])OpenItem(offer.Offer.ItemId+":"+(offer.Offer.AuxiliaryRaw.HasValue?offer.Offer.AuxiliaryRaw.Value.ToString():"unknown"),scope,offer);}
                OfferCells(r,new[]{offer.Offer.Visible==null?null:offer.Offer.Visible.Name,offer.Price,offer.Quantity,data.VendorFor(offer.Market),data.PlaceFor(offer.Market),""},columns,offer.Market,3);
            });
        }
        private void RequestComparison(LedgerScope a,LedgerScope b)
        {
            string signature=(a==null?"none":a.Key)+" -> "+(b==null?"none":b.Key)+"|"+comparisonSearch+"|"+comparisonSort+"|"+comparisonDescending;
            comparisonWantedKey=signature;int ticket;long flight;
            lock(resultGate){
                if(comparisonCacheKey==signature&&ReferenceEquals(comparisonRequestedData,data)&&comparisonRequestedGeo==data.GeographyRevision)return;
                if(comparisonRead.Busy){if(comparisonCacheKey!=null){comparisonGeneration++;comparisonCacheKey=null;}comparisonLoading=true;return;}
                comparisonCacheKey=signature;comparisonRequestedData=data;comparisonRequestedGeo=data.GeographyRevision;
                if(comparisonDisplayKey==signature&&ReferenceEquals(comparisonResultData,data)&&comparisonResultGeo==data.GeographyRevision){comparisonLoading=false;return;}
                ticket=++comparisonGeneration;comparisonRead.Begin(ticket,out flight);comparisonPending=null;comparisonLoading=true;comparisonError=null;
            }
            var snapshot=data;var index=locations;string query=comparisonSearch;var sort=comparisonSort;bool descending=comparisonDescending;int geo=data.GeographyRevision;
            ThreadPool.QueueUserWorkItem(_=>
            {
                LedgerComparisonResult result=null;string error=null;
                try{result=LedgerComparison.Query(snapshot,index,a,b,query,sort,descending);}catch(Exception e){error=e.GetType().Name;}
                lock(resultGate)if(comparisonRead.Complete(flight,comparisonGeneration)&&ticket==comparisonGeneration&&open){comparisonPending=result;comparisonPendingKey=signature;comparisonPendingData=snapshot;comparisonPendingGeo=geo;comparisonError=error;}
            });
        }
        private void ReadComparisonResult()
        {
            lock(resultGate)
            {
                if(comparisonPending!=null){bool changed=comparisonDisplayKey!=comparisonPendingKey;comparisonDisplayKey=comparisonPendingKey;comparisonResultData=comparisonPendingData;comparisonResultGeo=comparisonPendingGeo;comparisonResult=comparisonPending;comparisonPending=null;comparisonLoading=false;if(changed)comparisonScroll=Vector2.zero;}
                if(comparisonError!=null)comparisonLoading=false;
            }
        }
        private void DrawComparison()
        {
            if(locations==null)locations=data.Locations;Rect c=Content;bool contentEnabled=GUI.enabled;float side=(c.width-64)/2;
            Border(new Rect(c.x-3,c.y-3,side+6,70));Border(new Rect(c.x+side+61,c.y-3,side+6,70));
            var a=DrawScopePicker(new Rect(c.x,c.y,side,62),"Buy from A",sourcePicker,ref sourceChoices,true);
            var b=DrawScopePicker(new Rect(c.x+side+64,c.y,side,62),"Sell to B",destinationPicker,ref destinationChoices,true);
            if(ArtButton(new Rect(c.x+side+13,c.y+14,38,34),"SwapArrows","\u21C4","Swap buy and sell sides"))
            {LedgerUi.Swap(sourcePicker,destinationPicker);sourcePicker.Expanded=destinationPicker.Expanded=false;selectedPair=null;comparisonCacheKey=null;a=sourcePicker.Resolve(locations);b=destinationPicker.Resolve(locations);}
            comparisonSearch=Search(new Rect(c.x,c.y+74,300,28),comparisonSearch,"ComparisonSearch","Search items");
            RequestComparison(a,b);ReadComparisonResult();
            Rect header=new Rect(c.x,c.y+114,c.width-LedgerLayout.Scrollbar-3,26);float[] columns={.18f,.18f,.065f,.065f,.18f,.065f,.065f,.07f,.065f,.065f};
            GUI.enabled=contentEnabled&&!sourcePicker.Expanded&&!destinationPicker.Expanded;
            if(TableHeader(header,new[]{"Item","Trader A","Price","Stock","Trader B","Price","Demand","Transfer","\u0394 / unit","\u0394 total"},columns,comparisonHeaderSort))comparisonScroll=Vector2.zero;
            if(!ReferenceEquals(sortedPairSource,comparisonResult.Rows)||!ReferenceEquals(sortedPairData,data)||sortedPairColumn!=comparisonHeaderSort.Column||sortedPairDescending!=comparisonHeaderSort.Descending)
            {sortedPairSource=comparisonResult.Rows;sortedPairData=data;sortedPairColumn=comparisonHeaderSort.Column;sortedPairDescending=comparisonHeaderSort.Descending;sortedPairs=LedgerTableSorting.Comparison(sortedPairSource,comparisonHeaderSort,data);}
            var rows=sortedPairs;GUI.enabled=contentEnabled&&!sourcePicker.Expanded&&!destinationPicker.Expanded;
            GUI.enabled=GUI.enabled&&!comparisonLoading&&comparisonError==null;
            if(comparisonDisplayKey==comparisonWantedKey)
            DrawRows(new Rect(c.x,c.y+144,c.width,c.height-(comparisonResult.LimitReached||comparisonLoading||comparisonError!=null?174:148)),ref comparisonScroll,rows.Count,LedgerLayout.PairRow,(i,r)=>
            {
                var pair=rows[i];
                float ax=r.width*columns[0],bx=r.width*(columns[0]+columns[1]+columns[2]+columns[3]);
                OfferBackdrop(new Rect(r.x+ax,r.y,bx-ax,r.height),pair.Source.Market);
                OfferBackdrop(new Rect(r.x+bx,r.y,r.width*(columns[4]+columns[5]+columns[6]),r.height),pair.Destination.Market);
                bool actionArea=new Rect(ax+r.width*columns[1]-62,r.y+2,58,24).Contains(Event.current.mousePosition)||new Rect(bx+r.width*columns[4]-62,r.y+2,58,24).Contains(Event.current.mousePosition);
                if(!actionArea&&SelectRow(r,selectedPair==pair.Key)){selectedPair=pair.Key;if(Event.current.mousePosition.x<r.width*columns[0])OpenItem(pair.ItemKey,a,pair.Source);}
                DrawOfferBookmark(new Rect(ax+r.width*columns[1]-62,r.y+2,58,24),pair.Source);
                DrawOfferBookmark(new Rect(bx+r.width*columns[4]-62,r.y+2,58,24),pair.Destination);
                string[] cells={pair.Name,data.VendorFor(pair.Source.Market)+"\n"+data.PlaceFor(pair.Source.Market),LedgerComparison.Number(pair.SourcePrice),LedgerComparison.Quantity(pair.SourceStock),data.VendorFor(pair.Destination.Market)+"\n"+data.PlaceFor(pair.Destination.Market),LedgerComparison.Number(pair.DestinationPrice),LedgerComparison.Quantity(pair.DestinationDemand),LedgerComparison.Quantity(pair.Quantity),LedgerComparison.Number(pair.UnitDelta,true),LedgerComparison.Number(pair.TotalDelta,true)};
                float x=r.x;for(int j=0;j<cells.Length;j++)
                {float w=r.width*columns[j];Color old=GUI.color,content=GUI.contentColor;if(j>=1&&j<=6)GUI.contentColor=OfferTextColor(j<=3?pair.Source.Market:pair.Destination.Market,content);if(j>=8){decimal? v=j==8?pair.UnitDelta:pair.TotalDelta;if(v>0)GUI.color=new Color(.4f,.85f,1f);else if(v<0)GUI.color=new Color(1f,.4f,.4f);}
                    var cell=new Rect(x+4,r.y+3,w-8-(j==1||j==4?66:0),r.height-4);
                    if(j==1||j==4)OfferVendor(cell,cells[j],j==1?pair.Source.Market:pair.Destination.Market,pairText);
                    else GUI.Label(cell,LedgerPresentation.ItemRich(cells[j]),itemButton);GUI.color=old;GUI.contentColor=content;x+=w;}
            });
            else Label(new Rect(c.x,c.y+150,c.width,28),comparisonLoading?"Comparing...":"Comparison unavailable");
            if(comparisonDisplayKey==comparisonWantedKey&&(comparisonLoading||comparisonError!=null))Label(new Rect(c.x,c.yMax-26,c.width,26),comparisonLoading?"Updating comparison; previous observations shown.":"Refresh failed; previous comparison retained. Retry Refresh.");
            else if(comparisonResult.LimitReached)Label(new Rect(c.x,c.yMax-26,c.width,26),"Narrow the selection to display more pairs.");
            GUI.enabled=contentEnabled;
            DrawScopeChoices(new Rect(c.x,c.y+64,side,260),sourcePicker,ref sourceChoices);
            DrawScopeChoices(new Rect(c.x+side+64,c.y+64,side,260),destinationPicker,ref destinationChoices);
        }
    }
}
