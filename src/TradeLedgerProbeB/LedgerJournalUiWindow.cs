using System;
using System.Linq;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public sealed partial class LedgerPrototypeWindow
    {
        private sealed class ReturnPage
        {
            internal LedgerRequestFilters Requests;internal LedgerScopeKind LocationKind;internal string LocationKey,LocationSearch;internal Vector2 LocationScroll,LocationBuy,LocationSell,RequestScroll;
            internal int View;internal string ItemKey,Offer,Search,IconKey,DetailsKey,Text;
            internal LedgerScope Scope;internal LedgerViewItem Item;internal LedgerIcon Icon;internal LedgerItemDetails Details;
            internal bool Recipe;internal float DetailWidth,DetailHeight;internal Vector2 Items,Sellers,Buyers,Detail,Ingredients;
            internal LedgerScope LocationContext;internal int PlaceTab;internal bool PlaceDone;internal string PlaceMarketQuery,PlaceRequestQuery;internal Vector2 PlaceDetail;
        }
        private readonly LedgerUiTrail<ReturnPage> returnPages=new LedgerUiTrail<ReturnPage>();
        private string uiMenuKey;private Rect uiMenuRect,uiMenuAnchor;private string[] uiMenuChoices;private string[] uiMenuColors;private Action<int> uiMenuAction;private int uiMenuSelected,uiMenuHot,uiMenuRetiredHot;private float uiMenuWidth;
        private readonly LedgerPopupInput uiMenuInput=new LedgerPopupInput();
        private Vector2 uiTipScroll,uiMenuScroll;
        private void CloseUiMenu(){if(uiMenuHot!=0)uiMenuRetiredHot=uiMenuHot;uiMenuHot=0;uiMenuKey=null;uiMenuAction=null;uiMenuChoices=null;}
        private void ReleaseUiMenuHot(){if(uiMenuRetiredHot!=0&&GUIUtility.hotControl==uiMenuRetiredHot)GUIUtility.hotControl=0;uiMenuRetiredHot=0;}
        private void RootTab(int next)
        {if(view==next)return;CloseUiMenu();CancelRequestGesture();CancelGraphDrag();returnPages.Clear();view=next;}
        private void RememberItemEntry(string next)
        {
            if(view==0&&selectedKey==next)return;
            RememberPage();
        }
        private void RememberPage()
        {
            returnPages.Push(new ReturnPage{Requests=requestFilters.Copy(),LocationKind=locationPicker.Kind,LocationKey=locationPicker.Key,LocationSearch=locationSearch,LocationScroll=locationChoices,LocationBuy=locationBuyScroll,LocationSell=locationSellScroll,RequestScroll=requestsScroll,View=view,ItemKey=selectedKey,Offer=selectedOfferKey,Search=search,Scope=itemLocation,Item=catalogSelection,Recipe=recipeExpanded,
                IconKey=iconKey,Icon=selectedIcon,DetailsKey=detailsKey,Details=selectedDetails,Text=selectedDetailText,DetailWidth=detailWidth,DetailHeight=detailHeight,
                Items=itemsScroll,Sellers=sellersScroll,Buyers=buyersScroll,Detail=detailScroll,Ingredients=recipeScroll,LocationContext=locationPicker.Context,PlaceTab=placePanelTab,PlaceDone=placePanelDone,PlaceMarketQuery=placeMarketSearch,PlaceRequestQuery=placeRequestSearch,PlaceDetail=placeDetailScroll});
            CloseUiMenu();CancelRequestGesture();CancelGraphDrag();
        }
        private void ReturnPageBack()
        {
            ReturnPage p;if(!returnPages.Back(out p))return;CloseUiMenu();CancelRequestGesture();CancelGraphDrag();view=p.View;
            requestFilters=p.Requests??new LedgerRequestFilters();requestPopup=0;requestsScroll=p.RequestScroll;
            locationPicker=new LedgerScopePicker(p.LocationKind){Key=p.LocationKey,Context=p.LocationContext};locationSearch=p.LocationSearch??"";locationChoices=p.LocationScroll;locationBuyScroll=p.LocationBuy;locationSellScroll=p.LocationSell;locationCacheKey=null;
            placePanelTab=p.PlaceTab;placePanelDone=p.PlaceDone;placeMarketSearch=p.PlaceMarketQuery??"";placeRequestSearch=p.PlaceRequestQuery??"";placeDetailScroll=p.PlaceDetail;
            selectedKey=p.ItemKey;selectedOfferKey=p.Offer;search=p.Search;itemLocation=p.Scope;catalogSelection=p.Item;recipeExpanded=p.Recipe;
            iconKey=p.IconKey;selectedIcon=p.Icon;detailsKey=p.DetailsKey;selectedDetails=p.Details;selectedDetailText=p.Text;detailWidth=p.DetailWidth;detailHeight=p.DetailHeight;
            itemsScroll=p.Items;sellersScroll=p.Sellers;buyersScroll=p.Buyers;detailScroll=p.Detail;recipeScroll=p.Ingredients;
        }
        private void UiMenu(Rect anchor,string key,string[] choices,int selected,Action<int> action,string[] colors=null)
        {
            if(choices==null||choices.Length==0)return;if(uiMenuKey==key){CloseUiMenu();return;}
            uiMenuKey=key;uiMenuChoices=choices;uiMenuSelected=selected;uiMenuAction=action;uiMenuColors=colors;
            uiMenuWidth=Math.Min(420,choices.Max(s=>rich.CalcSize(new GUIContent(s)).x)+32);uiMenuAnchor=anchor;uiMenuScroll=Vector2.zero;PositionUiMenu();
        }
        private void PositionUiMenu(){if(uiMenuKey==null)return;var b=LedgerPopupLayout.Bounds(uiMenuAnchor.x,uiMenuAnchor.y,uiMenuAnchor.width,uiMenuAnchor.height,uiMenuWidth,uiMenuChoices.Length,layout.Width,layout.Height);uiMenuRect=new Rect(b[0],b[1],b[2],b[3]);uiMenuScroll.y=LedgerPopupLayout.Scroll(uiMenuScroll.y,0,uiMenuChoices.Length,uiMenuRect.height);}
        private void DrawUiMenu()
        {
            if(uiMenuKey==null)return;PositionUiMenu();Backplate(uiMenuRect,1);Border(uiMenuRect);
            var choices=uiMenuChoices;var action=uiMenuAction;int selected=uiMenuSelected,clicked=-1;
            var e=Event.current;bool inside=uiMenuRect.Contains(e.mousePosition);
            // Route wheel explicitly before nested scroll views; outside wheels are swallowed by Draw.
            if(inside&&e.type==EventType.ScrollWheel){uiMenuScroll.y=LedgerPopupLayout.Scroll(uiMenuScroll.y,e.delta.y,choices.Length,uiMenuRect.height);e.Use();}
            if(inside&&e.type==EventType.MouseDown&&e.button==0&&e.mousePosition.x<uiMenuRect.xMax-20){clicked=LedgerPopupLayout.Hit(e.mousePosition.y-uiMenuRect.y,uiMenuScroll.y,choices.Length,uiMenuRect.height);e.Use();}
            int priorHot=GUIUtility.hotControl;
            uiMenuScroll=GUI.BeginScrollView(uiMenuRect,uiMenuScroll,new Rect(0,0,uiMenuRect.width-20,choices.Length*30+8),false,true);
            int first=Math.Max(0,(int)(uiMenuScroll.y/30)),last=Math.Min(choices.Length,first+(int)(uiMenuRect.height/30)+2);
            for(int i=first;i<last;i++){
                var r=new Rect(3,i*30+4,uiMenuRect.width-26,28);
                if(inside)SelectRow(r,i==selected);
                Color prior=GUI.contentColor;if(uiMenuColors!=null&&i<uiMenuColors.Length)GUI.contentColor=PlaceColor(uiMenuColors[i]);GUI.Label(r,choices[i],itemButton);GUI.contentColor=prior;
            }GUI.EndScrollView();if(GUIUtility.hotControl==0)uiMenuHot=0;else if(GUIUtility.hotControl!=priorHot)uiMenuHot=GUIUtility.hotControl;
            if(clicked>=0){CloseUiMenu();action(clicked);}
        }

        private bool UiMenuButton(Rect rect,string text,string hint)
        {ControlFrame(rect,GUI.enabled&&rect.Contains(Event.current.mousePosition));bool pressed=GUI.Button(new Rect(rect.x+1,rect.y+1,rect.width-2,rect.height-2),new GUIContent(text,hint));if(!string.IsNullOrEmpty(hint)&&rect.Contains(Event.current.mousePosition))hoverLabel=hint;return pressed;}
        private bool HeaderAction(Rect rect,bool refresh)
        {
            bool clicked=GUI.Button(rect,new GUIContent("",refresh?"Refresh records":"Close (Esc / ])"));
            Color old=GUI.color;GUI.color=new Color(.8f,.92f,1f);float x=rect.center.x,y=rect.center.y;
            if(refresh){for(int i=0;i<18;i++){float a=i*Mathf.PI/11;GUI.DrawTexture(new Rect(x+7*Mathf.Cos(a)-1,y+7*Mathf.Sin(a)-1,2,2),Texture2D.whiteTexture);}GUI.DrawTexture(new Rect(x+5,y-1,6,2),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(x+9,y-5,2,5),Texture2D.whiteTexture);}
            else for(int i=-6;i<=6;i++){GUI.DrawTexture(new Rect(x+i,y+i,2,2),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(x+i,y-i,2,2),Texture2D.whiteTexture);}
            GUI.color=old;return clicked;
        }
    }
}
