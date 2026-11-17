using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public sealed partial class LedgerPrototypeWindow
    {
        private LedgerObjectOrganization placeList=LedgerObjectOrganization.Unsorted;
        private LedgerPlaceTraderFilter placeTraders;
        private LedgerPlaceRelation? placeRelation;private LedgerNativePlaceFactions nativePlaceFactions;private bool factionAttempted;
        private static readonly string[] placeRelationFilters={"Any","Hostile","Unfriendly","Neutral","Friendly","Unknown","N/A"};
        private static readonly LedgerPlaceRelation?[] placeRelationValues={null,LedgerPlaceRelation.Hostile,LedgerPlaceRelation.Unfriendly,LedgerPlaceRelation.Neutral,LedgerPlaceRelation.Friendly,LedgerPlaceRelation.Unknown,LedgerPlaceRelation.NotApplicable};
        private static readonly string[] placeRelationColors={"FFFFFF","FF5555","FFAA55","FFFF55","55DD77","A9B4BE","A9B4BE"};
        private readonly LedgerHeaderSort placeSort=new LedgerHeaderSort();private readonly LedgerPlaceSortCache placeSortCache=new LedgerPlaceSortCache();
        private Texture2D placeRelationDot;private readonly Dictionary<string,Color> placeColors=new Dictionary<string,Color>();
        private Color PlaceColor(string hex){if(hex==null)return new Color(.82f,.86f,.89f);Color v;if(!placeColors.TryGetValue(hex,out v)){v=new Color(Convert.ToInt32(hex.Substring(0,2),16)/255f,Convert.ToInt32(hex.Substring(2,2),16)/255f,Convert.ToInt32(hex.Substring(4,2),16)/255f);if(placeColors.Count<256)placeColors.Add(hex,v);}return v;}
        private void RelationDot(Rect r,LedgerFactionPresentation value){
            if(placeRelationDot==null){placeRelationDot=new Texture2D(8,8);for(int y=0;y<8;y++)for(int x=0;x<8;x++)placeRelationDot.SetPixel(x,y,(x-3.5f)*(x-3.5f)+(y-3.5f)*(y-3.5f)<=12?Color.white:Color.clear);placeRelationDot.Apply();}
            if(value.Relation==LedgerPlaceRelation.NotApplicable)GUI.Label(new Rect(r.x-5,r.y-6,30,24),"N/A",small);
            else{Color old=GUI.color;GUI.color=PlaceColor(value.RelationColor);GUI.DrawTexture(r,placeRelationDot);GUI.color=old;}
            if(new Rect(r.x-6,r.y-7,30,26).Contains(Event.current.mousePosition))hoverLabel=value.RelationLabel;
        }
        private void PollPlaceFactions(LedgerContext context,bool ready,long now){
            if(!open||view!=7||!ready)return;
            if(!factionAttempted){factionAttempted=true;try{nativePlaceFactions=new LedgerNativePlaceFactions(Application.GetType().Assembly);}catch(Exception e){Log("LedgerPlaceFactionUnavailable",e.GetType().Name+": "+e.Message);}}
            if(nativePlaceFactions!=null&&nativePlaceFactions.Refresh(data,Application,context,now))placeSignature=null;
        }
        private static readonly string[] placeTraderFilters={"Any traders","With traders","Without traders","Unknown traders"};
        private string placeScope="all",placeSearch="",placeFaction,placeFocus,placeSignature,placeFilterSignature,placeMessage;
        private readonly LedgerPlaceSelection placeSelection=new LedgerPlaceSelection();
        private readonly HashSet<string> placeCollapsed=new HashSet<string>();
        private Vector2 placeNavScroll,placeRowsScroll,placeDetailScroll;
        private List<LedgerPlaceRow> placeRows=new List<LedgerPlaceRow>();private LedgerPlaceBrowser placeProjection;
        private readonly HashSet<string> placeVisibleKeys=new HashSet<string>();
        private string[] placeFactions;private volatile bool placeDirty;private long placeLoadedAt;
        private LedgerObjectDisposition[] placeUndo;private bool placeMoving;
        private sealed class PlaceMoveResult {public string World,Error;public int Player;public LedgerObjectDisposition[] Values,Prior;public bool Undo;}
        private readonly Queue<PlaceMoveResult> placeMoveResults=new Queue<PlaceMoveResult>();
        private static readonly LedgerObjectOrganization[] placeLists={LedgerObjectOrganization.Favorites,LedgerObjectOrganization.Unsorted,LedgerObjectOrganization.Hidden};
        private static readonly string[] placeListNames={"Favorites","Unsorted","Hidden"};
        private void ResetPlaceBrowser(){placeRelation=null;placeTraders=LedgerPlaceTraderFilter.Any;placeScope="all";placeSearch="";placeFaction=placeFocus=placeSignature=placeFilterSignature=null;placeUndo=null;placeMoving=false;placeMessage=null;placeProjection=null;placeCollapsed.Clear();placeSelection.Selected.Clear();placeRows.Clear();}
        private bool PlaceButton(Rect r,string value,bool active=false){ControlFrame(r,GUI.enabled&&(active||r.Contains(Event.current.mousePosition)));bool click=GUI.Button(new Rect(r.x+1,r.y+1,r.width-2,r.height-2),value);if(active){Color old=GUI.color;GUI.color=new Color(.35f,.9f,1f);GUI.DrawTexture(new Rect(r.x+3,r.yMax-3,r.width-6,2),Texture2D.whiteTexture);GUI.color=old;}return click;}
        private void PlaceOverflow(Rect r,string value,GUIStyle style){if(r.Contains(Event.current.mousePosition)&&style.CalcSize(new GUIContent(LedgerPresentation.ItemRich(value))).x>r.width)iconHoverName=value;}
        private void MovePlaces(LedgerObjectDisposition[] values,bool undo=false)
        {
            if(placeMoving||values==null||values.Length==0)return;
            values=values.Where(v=>data.PlaceBrowser.Find(v.Key)==null||data.PlaceBrowser.Find(v.Key).Record.Organization!=v.Value).ToArray();if(values.Length==0)return;
            var prior=values.Select(v=>data.PlaceBrowser.Find(v.Key)).Where(r=>r!=null).Select(r=>LedgerDiscoveredObjects.Organize(r.Record,r.Record.Organization)).ToArray();
            if(prior.Length!=values.Length){placeMessage="Selection changed. Select again.";return;}
            var result=new PlaceMoveResult{World=world,Player=data.ManualPlayerId??0,Values=values,Prior=prior,Undo=undo};
            if(ManualStore==null||!ManualStore.EnqueueObjectDispositions(values,error=>{result.Error=error;lock(resultGate)placeMoveResults.Enqueue(result);}))
            {placeMessage="Move not queued. Try a smaller selection or refresh.";return;}
            placeMoving=true;manualRevision++;placeMessage="Saving list changes...";
        }
        private void PollPlaceMoves()
        {
            lock(resultGate)while(placeMoveResults.Count>0){var result=placeMoveResults.Dequeue();if(result.World!=world||result.Player!=manualPlayer)continue;
                placeMoving=false;manualRevision++;if(result.Error!=null){placeMessage="List changes weren't saved. Refresh and retry.";continue;}
                foreach(var v in result.Values){var r=data.PlaceBrowser.Find(v.Key);if(r!=null)r.Record.Organization=v.Value;}
                placeUndo=result.Undo?null:result.Prior;placeSelection.Selected.Clear();placeSignature=null;placeMessage=result.Undo?"List changes undone.":result.Values.Length+" place(s) moved.";
            }
        }
        private void OpenPlaceMarkets(LedgerPlaceRow target)
        {RememberPage();locationPicker=new LedgerScopePicker(LedgerScopeKind.Poi){Key=target.LocationKey};locationSearch="";locationCacheKey=null;locationBuyScroll=locationSellScroll=Vector2.zero;view=2;}
        private void OpenPlaceRequests(LedgerPlaceRow target,bool done)
        {RememberPage();requestFilters=LedgerPlaceBrowser.RequestFilter(target,done);requestPopup=0;RequestFilterChanged();view=1;}
        private void DrawPlaces()
        {
            Rect whole=Content;
            if(!LedgerPlaceBrowser.Current(data,world,manualPlayer)){Label(new Rect(whole.x+8,whole.y+8,whole.width-16,30),loading?"Loading current save/player places...":"Current save/player places unavailable. Refresh to retry.");return;}
            var bounds=new LedgerPlaceBrowserLayout(whole.width,whole.height);float navWidth=bounds.Nav;
            if(!ReferenceEquals(placeProjection,data.PlaceBrowser)){
                placeProjection=data.PlaceBrowser;placeSignature=null;placeFactions=placeProjection.Factions;
            }
            var hierarchy=placeProjection.Index.Hierarchy;var nodes=hierarchy.Visible(placeCollapsed);
            Label(new Rect(whole.x+6,whole.y,navWidth-12,27),"Locations",true);
            DrawRows(new Rect(whole.x,whole.y+33,navWidth,whole.height-33),ref placeNavScroll,nodes.Count,28,(i,r)=>{
                var n=nodes[i];float indent=n.Depth*14;if(n.Children.Count>0&&PlaceButton(new Rect(r.x+indent,r.y+2,23,24),placeCollapsed.Contains(n.Key)?"+":"-")){if(!placeCollapsed.Add(n.Key))placeCollapsed.Remove(n.Key);}
                var text=new Rect(r.x+indent+25,r.y,r.width-indent-25,r.height);if(SelectRow(text,placeScope==n.Key)){placeScope=n.Key;placeFocus=null;placeDetailScroll=Vector2.zero;}Label(text,n.Label,n.Role=="system");PlaceOverflow(text,n.Label,rich);
            },"No discovered locations.",.55f,.12f);
            Rect c=new Rect(whole.x+navWidth+12,whole.y,bounds.Main,whole.height);
            for(int i=0;i<placeLists.Length;i++){var value=placeLists[i];if(PlaceButton(new Rect(c.x+i*112,c.y,106,28),placeListNames[i],placeList==value))placeList=value;}
            placeSearch=Search(new Rect(c.x+342,c.y,Math.Max(160,c.width-414),28),placeSearch,"PlacesSearch","Search discovered places");
            if(PlaceButton(new Rect(c.xMax-66,c.y,66,28),"Reset")){placeRelation=null;placeTraders=LedgerPlaceTraderFilter.Any;placeScope="all";placeFaction=null;placeSearch="";placeSort.Column=-1;}
            Rect fr=new Rect(c.x,c.y+34,Math.Max(140,c.width-450),28);
            if(UiMenuButton(fr,(placeFaction??"All factions")+LedgerJournalUi.Dropdown,null)){var factions=placeFactions;UiMenu(fr,"place-faction",factions,Math.Max(0,Array.IndexOf(factions,placeFaction)),i=>placeFaction=i==0?null:factions[i]);}
            float fx=fr.xMax+10;
            for(int i=0;i<placeRelationValues.Length;i++)if(PlaceFilterIcon(new Rect(fx+i*28,c.y+34,26,28),i==0?"*":i==6?"/":"",placeRelationFilters[i],placeRelationColors[i],placeRelation==placeRelationValues[i],i>0&&i<6))placeRelation=placeRelationValues[i];
            fx+=204;Label(new Rect(fx,c.y+34,58,28),"Traders");
            for(int i=0;i<4;i++)if(PlaceFilterIcon(new Rect(fx+60+i*28,c.y+34,26,28),i==0?"*":i==1?"+":i==2?"-":"?",placeTraderFilters[i]+". Template potential; unknown is not zero.","FFFFFF",(int)placeTraders==i,false))placeTraders=(LedgerPlaceTraderFilter)i;
            string signature=placeList+"|"+placeScope+"|"+placeSearch+"|"+placeFaction+"|"+placeTraders+"|"+placeRelation;
            if(signature!=placeSignature){bool filterChanged=placeFilterSignature!=signature;placeSignature=placeFilterSignature=signature;placeRows=placeProjection.Query(placeList,placeScope,placeSearch,null,placeFaction,placeTraders,placeRelation);placeVisibleKeys.Clear();foreach(var r in placeRows)placeVisibleKeys.Add(r.Record.Key);placeSelection.Scope(signature,placeRows);if(filterChanged)placeRowsScroll=Vector2.zero;}
            bool enabled=GUI.enabled;GUI.enabled=enabled&&!placeMoving&&placeProjection.Index.Available;
            if(PlaceButton(new Rect(c.x,c.y+68,100,27),"Select all"))placeSelection.All(placeRows);
            if(PlaceButton(new Rect(c.x+106,c.y+68,62,27),"Clear"))placeSelection.Selected.Clear();
            bool checkedOnly=placeSelection.Selected.Count>0;bool cursorVisible=placeFocus!=null&&placeVisibleKeys.Contains(placeFocus);
            GUI.enabled=enabled&&!placeMoving&&(checkedOnly||cursorVisible);
            if(PlaceButton(new Rect(c.x+176,c.y+68,112,27),(placeList==LedgerObjectOrganization.Hidden?"Unhide":"Hide")+(checkedOnly?" checked":" row")))MovePlaces(placeSelection.Move(placeRows,LedgerPlaceContext.HideTarget(placeList),placeFocus));
            if(PlaceButton(new Rect(c.x+296,c.y+68,112,27),placeList==LedgerObjectOrganization.Favorites?"Unfavorite":"Favorite"))MovePlaces(placeSelection.Move(placeRows,LedgerPlaceContext.FavoriteTarget(placeList),placeFocus));
            GUI.enabled=enabled&&!placeMoving&&placeUndo!=null;
            if(PlaceButton(new Rect(c.x+416,c.y+68,64,27),"Undo"))MovePlaces(placeUndo,true);
            GUI.enabled=enabled;Label(new Rect(c.x+490,c.y+68,c.width-580,27),(checkedOnly?placeSelection.Selected.Count+" checked":cursorVisible?"Cursor row":"No target")+(placeMessage==null?"":" | "+placeMessage));
            var visit=placeProjection.Find(placeFocus);if(visit!=null&&!placeVisibleKeys.Contains(visit.Record.Key))visit=null;LedgerBookmarkPlan visitPlan=null;string visitReason="Select a place";
            bool canVisit=visit!=null&&LedgerBookmarkPolicy.TryPlace(data,visit.Record,currentContext,out visitPlan,out visitReason);
            DrawRowBookmark(new Rect(c.xMax-80,c.y+68,80,27),visitPlan,canVisit,visitReason,"Visit");
            float tableWidth=bounds.List-LedgerLayout.Scrollbar-3;
            var columnX=LedgerPlaceBrowserLayout.Columns(tableWidth);
            var tableHeader=new Rect(c.x,c.y+104,tableWidth,30);Backplate(tableHeader);
            if(GUI.Button(new Rect(tableHeader.x+32,tableHeader.y,Math.Max(40,columnX[1]-32),28),placeSort.Label("Place",0),itemButton))placeSort.Click(0);
            if(GUI.Button(new Rect(tableHeader.x+columnX[2],tableHeader.y,86,28),placeSort.Label("Distance",1),small))placeSort.Click(1);
            if(GUI.Button(new Rect(tableHeader.x+columnX[3],tableHeader.y,90,28),placeSort.Label("Max traders",2),small))placeSort.Click(2);
            var displayed=placeSortCache.Apply(placeRows,placeProjection,placeSort,playerPoint,data.GeographyRevision,positionAt);
            Rect port=new Rect(c.x,c.y+136,bounds.List,bounds.Height-32);
            DrawRows(port,ref placeRowsScroll,displayed.Count,LedgerPlaceBrowserLayout.Row,(i,r)=>{
                var row=displayed[i];string key=row.Record.Key;bool old=GUI.enabled;GUI.enabled=old&&!placeMoving;
                bool selected=placeSelection.Selected.Contains(key);
                bool checkboxPressed=SelectRow(new Rect(r.x,r.y,32,r.height),selected);
                if(Event.current.type==EventType.Repaint)GUI.skin.toggle.Draw(new Rect(r.x+5,r.y+14,22,22),GUIContent.none,false,false,selected,false);
                bool rowPressed=SelectRow(new Rect(r.x+32,r.y,r.width-32,r.height),placeFocus==key);
                if(placeSelection.Activate(key,checkboxPressed,rowPressed)){placeFocus=key;placeDetailScroll=Vector2.zero;}GUI.enabled=old;
                Color nameColor=GUI.contentColor;GUI.contentColor=PlaceColor(row.FactionInfo.Color);Label(new Rect(r.x+38,r.y+2,columnX[1]-44,25),row.Name,true);GUI.contentColor=nameColor;
                Color prior=GUI.contentColor;GUI.contentColor=new Color(.69f,.77f,.82f);GUI.Label(new Rect(r.x+38,r.y+26,columnX[1]-44,20),row.Record.Latest.Context.Playfield+" | "+row.FactionLabel,itemButton);GUI.contentColor=prior;
                RelationDot(new Rect(r.x+columnX[1]+10,r.y+20,8,8),row.FactionInfo);
                GUI.Label(new Rect(r.x+columnX[2],r.y+10,86,28),placeProjection.DistanceText(row,playerPoint),itemButton);
                GUI.Label(new Rect(r.x+columnX[3],r.y+10,90,28),row.PotentialTraders.HasValue?row.PotentialTraders.Value.ToString():"?",itemButton);
                PlaceOverflow(new Rect(r.x+38,r.y+2,columnX[1]-44,25),row.Name,richHeading);
            },!placeProjection.Index.Available?"Discovered-place records unavailable. Refresh to retry.":"No places in this list and filter.");
            Rect d=new Rect(c.x+bounds.List+12,c.y+104,bounds.Detail,bounds.Height);Backplate(d);Border(d);
            var focused=placeProjection.Find(placeFocus);
            if(focused!=null&&!placeVisibleKeys.Contains(focused.Record.Key))focused=null;
            DrawPlaceContext(d,focused);
        }
        private bool PlaceFilterIcon(Rect r,string value,string hint,string color,bool active,bool dot)
        {
            bool click=PlaceButton(r,value=="*"?"":value,active);
            if(value=="*"){Color old=GUI.color;GUI.color=Color.white;for(int dx=-1;dx<=1;dx+=2)for(int dy=-1;dy<=1;dy+=2){for(int j=2;j<=7;j++)GUI.DrawTexture(new Rect(r.center.x+dx*j,r.center.y+dy*j,1,1),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(r.center.x+(dx<0?-7:4),r.center.y+dy*7,4,1),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(r.center.x+dx*7,r.center.y+(dy<0?-7:4),1,4),Texture2D.whiteTexture);}GUI.color=old;}
            if(dot){if(placeRelationDot==null)RelationDot(new Rect(-50,-50,8,8),new LedgerFactionPresentation());Color old=GUI.color;GUI.color=PlaceColor(color);GUI.DrawTexture(new Rect(r.center.x-4,r.center.y-4,8,8),placeRelationDot);GUI.color=old;}
            if(active){Color old=GUI.color;GUI.color=new Color(.35f,.9f,1f);GUI.DrawTexture(new Rect(r.x+3,r.yMax-3,r.width-6,2),Texture2D.whiteTexture);GUI.color=old;}
            if(r.Contains(Event.current.mousePosition))hoverLabel=hint+(active?" (selected)":"");
            return click;
        }
    }
}
