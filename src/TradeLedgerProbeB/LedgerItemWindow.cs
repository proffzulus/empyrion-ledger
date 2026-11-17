using System;
using System.Linq;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public sealed partial class LedgerPrototypeWindow
    {
        private LedgerNativeItemDetails nativeDetails;
        private LedgerNativeUiArt nativeArt;
        private bool artAttempted,recipeExpanded;
        private string detailsKey,hoverLabel;
        private string selectedDetailText;
        private float detailWidth=-1,detailHeight;
        private LedgerItemDetails selectedDetails;
        private LedgerViewItem catalogSelection;
        private Vector2 detailScroll,recipeScroll;
        private void RefreshItemDetails(LedgerViewItem item)
        {
            string key=world+"|"+(item==null?"none":item.Key);
            if(key==detailsKey)return;detailsKey=key;selectedDetails=null;selectedDetailText=null;detailWidth=-1;detailScroll=recipeScroll=Vector2.zero;
            if(item==null||NativeCatalog==null)return;
            try
            {
                if(nativeDetails==null)nativeDetails=new LedgerNativeItemDetails(Application.GetType().Assembly,NativeCatalog);
                selectedDetails=nativeDetails.Read(item);
                selectedDetailText=LedgerItemDisplay.Details(selectedDetails);
            }
            catch(Exception e){Log("LedgerItemDetailsUnavailable",e.GetType().Name);}
        }
        private void SelectIngredient(LedgerIngredient ingredient)
        {
            SelectCatalogItem(LedgerItemDisplay.Ingredient(data,catalog,ingredient),false);
        }
        private void DrawItemPin(Rect rect,LedgerViewItem item,bool pinned,bool ready)
        {
            bool enabled=GUI.enabled;GUI.enabled=enabled&&ready;
            string hint=NativeBookmarks!=null&&NativeBookmarks.SaveFailed?"Item pins not saved; retry save in Markers":pinned?"Unpin item":"Pin item";
            if(GUI.Button(rect,new GUIContent("",hint),GUIStyle.none))MarkerEdit(m=>LedgerItemPins.Toggle(m,world,manualPlayer.Value,item));
            Color old=GUI.color;GUI.color=pinned?new Color(1f,.81f,.34f,1):new Color(.48f,.61f,.68f,.8f);
            float x=rect.center.x,y=rect.center.y;
            GUI.DrawTexture(new Rect(x-5,y-7,10,2),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(x-3,y-5,6,6),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x-6,y+1,12,2),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(x-1,y+3,2,6),Texture2D.whiteTexture);
            GUI.color=old;GUI.enabled=enabled;if(rect.Contains(Event.current.mousePosition))iconHoverName=hint;
        }
        private void SelectCatalogItem(LedgerViewItem item,bool clearLocation)
        {
            if(item==null)return;
            RememberItemEntry(item.Key);catalogSelection=item;selectedKey=item.Key;selectedOfferKey=null;search="";iconKey=null;detailsKey=null;view=0;
            if(clearLocation)itemLocation=null;
        }
        private void DrawItemInformation(Rect rect,LedgerViewItem item)
        {
            RefreshItemDetails(item);Backplate(rect);Border(rect);
            var details=selectedDetails;
            if(details==null){Label(new Rect(rect.x+8,rect.y+8,rect.width-16,28),"Item details unavailable");return;}
            float recipeWidth=recipeExpanded?Math.Min(390,rect.width*.36f):0;
            Rect recipeButton=new Rect(rect.xMax-176,rect.y+5,168,26);
            if(Button(recipeButton,recipeExpanded?"Recipe \u25BE":"Recipe \u25B8"))recipeExpanded=!recipeExpanded;
            Label(new Rect(rect.x+8,rect.y+6,rect.width-196,25),"Details");
            Rect port=new Rect(rect.x+6,rect.y+36,rect.width-recipeWidth-12,rect.height-42);
            string formatted=selectedDetailText;float width=port.width-LedgerLayout.Scrollbar-8;
            if(detailWidth!=width){detailWidth=width;detailHeight=rich.CalcHeight(new GUIContent(formatted),width)+8;}
            float height=Math.Max(port.height,detailHeight);
            detailScroll=GUI.BeginScrollView(port,detailScroll,new Rect(0,0,width,height),false,true);
            GUI.Label(new Rect(3,3,width-6,height-6),formatted,rich);GUI.EndScrollView();
            if(recipeExpanded)
            {
                Rect right=new Rect(rect.xMax-recipeWidth,rect.y+36,recipeWidth-8,rect.height-42);
                Label(new Rect(right.x+6,right.y,right.width-12,25),details.OutputCount.HasValue?"Output: "+details.OutputCount:details.RecipeState);
                if(details.Ingredients.Count>0)
                    DrawRows(new Rect(right.x,right.y+28,right.width,right.height-28),ref recipeScroll,details.Ingredients.Count,LedgerLayout.TreeRow,(i,r)=>
                    {var part=details.Ingredients[i];if(SelectRow(r,false))SelectIngredient(part);GUI.Label(new Rect(r.x+5,r.y,r.width-10,r.height),(part.Quantity.HasValue?part.Quantity.Value.ToString():"?")+" \u00D7 "+LedgerItemDisplay.NativeRich(part.Name),itemButton);});
            }
        }
        private void Border(Rect rect)
        {
            Color old=GUI.color;GUI.color=new Color(.30f,.62f,.82f,.65f);
            GUI.DrawTexture(new Rect(rect.x,rect.y,rect.width,1),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(rect.x,rect.yMax-1,rect.width,1),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x,rect.y,1,rect.height),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(rect.xMax-1,rect.y,1,rect.height),Texture2D.whiteTexture);GUI.color=old;
        }
        private bool ArtButton(Rect rect,string key,string fallback,string tooltip,bool selected=false)
        {
            EnsureUiArt();
            var icon=nativeArt==null?null:nativeArt.Get(key=="SwapArrows"?"ArrowNavigationRight":key);
            var opposite=key=="SwapArrows"&&nativeArt!=null?nativeArt.Get("ArrowNavigationLeft"):null;
            bool pressed=GUI.Toggle(rect,selected,icon==null?fallback:"",GUI.skin.button)!=selected;
            if(icon!=null&&opposite!=null){GUI.DrawTextureWithTexCoords(new Rect(rect.x+7,rect.y+4,rect.width-14,(rect.height-8)/2),icon.Texture,icon.Uv);GUI.DrawTextureWithTexCoords(new Rect(rect.x+7,rect.center.y,rect.width-14,(rect.height-8)/2),opposite.Texture,opposite.Uv);}
            else if(icon!=null){float side=Math.Min(rect.width-8,rect.height-6);GUI.DrawTextureWithTexCoords(new Rect(rect.x+(rect.width-side)/2,rect.y+3,side,side),icon.Texture,icon.Uv);}
            if(rect.Contains(Event.current.mousePosition))hoverLabel=tooltip;
            return pressed;
        }
        private void EnsureUiArt()
        {if(!artAttempted){artAttempted=true;try{nativeArt=new LedgerNativeUiArt();}catch(Exception e){Log("LedgerUiArtUnavailable",e.GetType().Name);}}}
        private bool HasUiArt(string key){EnsureUiArt();return nativeArt!=null&&nativeArt.Get(key)!=null;}
        private void DrawHoverLabel()
        {
            if(uiMenuKey!=null)return;string value=iconHoverName??hoverLabel??GUI.tooltip;if(string.IsNullOrEmpty(value))return;
            value=LedgerPresentation.TooltipText(value);
            float w=Math.Min(420,text.CalcSize(new GUIContent(value)).x+20);w=Math.Max(120,w);
            float h=text.CalcHeight(new GUIContent(value),w-16)+12;
            var box=LedgerJournalUi.Tooltip(Event.current.mousePosition.x,Event.current.mousePosition.y,w,h,layout.Width,layout.Height);var rect=new Rect(box[0],box[1],box[2],box[3]);
            Backplate(rect,.98f);GUI.Box(rect,"",row);
            if(h>rect.height){uiTipScroll=GUI.BeginScrollView(rect,uiTipScroll,new Rect(0,0,w-16,h));GUI.Label(new Rect(6,6,w-28,h-12),value,text);GUI.EndScrollView();}
            else GUI.Label(new Rect(rect.x+8,rect.y+6,rect.width-16,rect.height-12),value,text);

        }
    }
}
