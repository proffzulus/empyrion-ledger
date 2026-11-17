using System;
using System.Linq;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public sealed partial class LedgerPrototypeWindow
    {
        private Vector2 markerScroll,routeScroll;
        private string markerUiScope,routeName="",newRoute="",markerAliasKey,markerAlias="";
        private LedgerMarkerSelection markerSelection=new LedgerMarkerSelection();
        private void MarkerEdit(Action<LedgerMarkerModel> action){if(NativeBookmarks!=null)NativeBookmarks.Edit(action);}
        private bool MarkerEye(Rect rect,bool shown,string hint)
        {
            bool clicked=GUI.Button(rect,new GUIContent("",hint));Color old=GUI.color;GUI.color=shown?new Color(.65f,.9f,1f,1):new Color(.45f,.56f,.64f,1);
            float unit=Math.Min(rect.width/26,rect.height/26);float x=rect.center.x-10*unit,y=rect.center.y;
            for(int i=0;i<20;i++){float f=(i-9.5f)/10;float dy=5*(float)Math.Sqrt(Math.Max(0,1-f*f));GUI.DrawTexture(new Rect(x+i*unit,y-dy*unit,unit,1.5f*unit),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(x+i*unit,y+dy*unit,unit,1.5f*unit),Texture2D.whiteTexture);}
            GUI.DrawTexture(new Rect(rect.center.x-2*unit,y-3*unit,4*unit,6*unit),Texture2D.whiteTexture);
            if(!shown)for(int i=0;i<18;i++)GUI.DrawTexture(new Rect(x+(i+1)*unit,y+(7-i*.8f)*unit,2*unit,2*unit),Texture2D.whiteTexture);
            GUI.color=old;if(rect.Contains(Event.current.mousePosition))iconHoverName=hint;return clicked;
        }
        private bool MarkerCheck(Rect rect,bool active,string hint)
        {
            bool click=GUI.Button(rect,new GUIContent("",hint),GUIStyle.none);var box=new Rect(rect.center.x-10,rect.center.y-10,20,20);ControlFrame(box,active);
            if(active){Color old=GUI.color;GUI.color=new Color(.6f,.95f,1f,1);for(int i=0;i<6;i++)GUI.DrawTexture(new Rect(box.x+3+i,box.y+10+i*.7f,2,2),Texture2D.whiteTexture);for(int i=0;i<10;i++)GUI.DrawTexture(new Rect(box.x+8+i*.7f,box.y+14-i,2,2),Texture2D.whiteTexture);GUI.color=old;}
            if(rect.Contains(Event.current.mousePosition))iconHoverName=hint;return click;
        }
        private void DrawMarkerManager()
        {
            Rect c=Content;
            if(NativeBookmarks==null||!NativeBookmarks.Ready){Label(new Rect(c.x,c.y,c.width,50),NativeBookmarks==null?"Bookmarks unavailable":NativeBookmarks.Status);return;}
            var model=NativeBookmarks.Model;string scope=NativeBookmarks.World+"|"+NativeBookmarks.Player;
            if(markerUiScope!=scope){markerUiScope=scope;markerSelection=new LedgerMarkerSelection();markerAliasKey=null;markerAlias=routeName=newRoute="";markerScroll=routeScroll=Vector2.zero;}
            float left=260;Rect side=new Rect(c.x,c.y,left,c.height),body=new Rect(c.x+left+14,c.y,c.width-left-14,c.height);
            Backplate(side);Label(new Rect(side.x+8,side.y+4,left-16,26),"Routes",true);
            if(Button(new Rect(side.x+8,side.y+34,112,26),"All destinations")){markerSelection.All();markerScroll=Vector2.zero;}
            if(Button(new Rect(side.x+128,side.y+34,124,26),"Ungrouped")){markerSelection.Ungrouped();markerScroll=Vector2.zero;}
            newRoute=Search(new Rect(side.x+8,side.y+70,left-70,27),newRoute,"MarkerNewRoute");
            if(Button(new Rect(side.xMax-56,side.y+70,48,27),"Add")&&!string.IsNullOrWhiteSpace(newRoute))MarkerEdit(m=>{var route=m.AddRoute(newRoute,NativeBookmarks.World,NativeBookmarks.Player);markerSelection.Route(route.Id);routeName=route.Name;newRoute="";markerScroll=Vector2.zero;});
            var policies=LedgerProductionServices.Preferences(model,NativeBookmarks.World,NativeBookmarks.Player,false);
            bool suppressAll=policies!=null&&policies.Effective(new[]{"all"}).Suppressed;
            if(MarkerCheck(new Rect(side.x+8,side.y+102,28,27),suppressAll,"Suppress automatic alerts everywhere"))
                MarkerEdit(m=>LedgerProductionServices.Preferences(m,NativeBookmarks.World,NativeBookmarks.Player).For("all",true).Suppress=!suppressAll);
            Label(new Rect(side.x+40,side.y+102,left-48,27),"Suppress automatic alerts");
            var routes=model.Data.Routes.Where(r=>r.World==NativeBookmarks.World&&r.Player==NativeBookmarks.Player).ToList();
            DrawRows(new Rect(side.x+4,side.y+136,left-8,Math.Max(80,side.height-372)),ref routeScroll,routes.Count,32,(i,r)=>{
                var route=routes[i];if(MarkerEye(new Rect(r.x+2,r.y+2,30,26),route.Shown,route.Shown?"Hide route navigation":"Show route navigation"))MarkerEdit(m=>m.RouteEye(route));
                var name=new Rect(r.x+38,r.y,r.width-40,r.height);
                if(SelectRow(name,markerSelection.Scope==route.Id)){markerSelection.Route(route.Id);routeName=route.Name;markerScroll=Vector2.zero;}
                bool attention=model.ReadyRoutes.Contains(route.Id);
                GUI.Label(name,LedgerPresentation.ItemRich(attention?"[c][80E680]"+route.Name+" · Ready[-][/c]":route.Name),itemButton);
            });
            var editing=routes.FirstOrDefault(r=>r.Id==markerSelection.TargetRoute);
            if(editing!=null){float y=side.yMax-124;
                if(editing.ServiceKey==null){var goals=model.Data.Destinations.SelectMany(d=>d.Purposes).Where(p=>p.Kind=="offer"&&p.Routes.Contains(editing.Id)).ToList();bool tracked=goals.Count>0&&goals.All(p=>p.TrackUpdates);if(MarkerCheck(new Rect(side.x+8,y-95,28,27),tracked,"Track all buyers and sellers in this route"))MarkerEdit(m=>{foreach(var d in m.Data.Destinations)foreach(var p in d.Purposes.Where(p=>p.Kind=="offer"&&p.Routes.Contains(editing.Id)))LedgerTradeUpdates.Track(m,d,p,!tracked);});Label(new Rect(side.x+42,y-95,left-48,27),"Track route updates");var modeRect=new Rect(side.x+8,y-64,left-16,26);string mode=LedgerJournalUi.RouteMode(editing);if(UiMenuButton(modeRect,"Mode: "+mode+LedgerJournalUi.Dropdown,"Route display mode"))UiMenu(modeRect,"route-mode",LedgerJournalUi.RouteModes.Concat(new[]{editing.ForceNotify?"Force notify: on":"Force notify: off"}).ToArray(),Array.IndexOf(new[]{"show","hide","auto"},mode),i=>MarkerEdit(m=>{if(i<3)LedgerRouteFlow.Mode(m,editing,LedgerJournalUi.RouteModes[i].ToLowerInvariant());else editing.ForceNotify=!editing.ForceNotify;}));}
                string policy=LedgerTradeUpdates.RoutePolicyKey(editing);bool suppressed=policies!=null&&policies.For(policy)!=null&&policies.For(policy).Suppress;
                if(MarkerCheck(new Rect(side.x+8,y-31,28,27),suppressed,"Suppress automatic alerts for this route"))
                    MarkerEdit(m=>LedgerProductionServices.Preferences(m,NativeBookmarks.World,NativeBookmarks.Player).For(policy,true).Suppress=!suppressed);
                Label(new Rect(side.x+40,y-31,left-48,27),suppressAll?"Suppress (inherited)":"Suppress automatic alerts");
                Label(new Rect(side.x+8,y,left-16,23),"Add to: "+editing.Name);
                routeName=Search(new Rect(side.x+8,y+27,left-70,27),routeName,"MarkerRouteName");
                if(Button(new Rect(side.xMax-56,y+27,48,27),"Save"))MarkerEdit(m=>editing.Name=LedgerMarkerModel.Name(routeName,64));
                if(Button(new Rect(side.x+8,y+68,left-16,27),"Delete route (keep purposes)"))MarkerEdit(m=>{m.DeleteRoute(editing.Id);markerSelection.Deleted(editing.Id);});}
            string viewName=markerSelection.Scope=="all"?"All destinations":markerSelection.Scope=="ungrouped"?"Ungrouped":routes.Where(r=>r.Id==markerSelection.Scope).Select(r=>r.Name).FirstOrDefault()??"All destinations";
            Backplate(new Rect(body.x,body.y,body.width,35));Label(new Rect(body.x+8,body.y+3,body.width-16,28),viewName,true);
            var destinations=model.Data.Destinations.Where(d=>d.World==NativeBookmarks.World&&d.Player==NativeBookmarks.Player&&!d.DeleteRequested&&d.Purposes.Any(p=>model.Listed(p)&&markerSelection.IncludesPurpose(p))).OrderBy(d=>d.Alias.Length>0?d.Alias:d.Place??d.Vendor).ThenBy(d=>d.Key,StringComparer.Ordinal).ToList();
            Rect area=new Rect(body.x,body.y+43,body.width,body.height-82);float width=area.width-LedgerLayout.Scrollbar;
            Func<LedgerMarkerDestination,float> cardHeight=d=>58+d.Purposes.Count(p=>model.Listed(p)&&markerSelection.IncludesPurpose(p))*33+(markerAliasKey==d.Key?32:0)+(LedgerJournalUi.MarkerIssue(d)?30:0);
            float height=destinations.Sum(d=>cardHeight(d)+6);markerScroll=GUI.BeginScrollView(area,markerScroll,new Rect(0,0,width,Math.Max(area.height,height)));float at=0;
            foreach(var d in destinations){
                float h=cardHeight(d);Backplate(new Rect(0,at,width,h));string name=LedgerMarkerSelection.Title(d);
                GUI.Label(new Rect(8,at+3,width-136,26),LedgerPresentation.ItemRich(name),itemButton);
                var moreRect=new Rect(width-50,at+3,42,26);var moreAnchor=new Rect(body.x+moreRect.x,area.y+moreRect.y-markerScroll.y,moreRect.width,moreRect.height);
                bool service=d.Purposes.Any(p=>p.Service);
                if(service||d.Pinned)GUI.Label(new Rect(width-128,at+3,74,26),"Pinned",itemButton);
                var actions=new System.Collections.Generic.List<string>();if(!service)actions.Add(d.Pinned?"Unpin":"Pin");if(d.Native==null&&!d.Conflict)actions.Add("Mark destination");actions.Add("Rename");actions.Add("Delete...");
                if(UiMenuButton(moreRect,"...","Destination actions"))UiMenu(moreAnchor,"destination:"+d.Key,actions.ToArray(),-1,i=>{
                    string action=actions[i];
                    if(action=="Pin"||action=="Unpin")MarkerEdit(m=>m.TogglePin(d));
                    else if(action=="Mark destination")MarkerEdit(m=>m.Restore(d));
                    else if(action=="Rename"){markerAliasKey=d.Key;markerAlias=d.Alias;}
                    else UiMenu(moreAnchor,"delete:"+d.Key,new[]{"Delete destination","Cancel"},-1,j=>{if(j==0)MarkerEdit(m=>m.DeleteDestination(d));});
                });
                string subtitle=LedgerMarkerSelection.Subtitle(d);
                GUI.Label(new Rect(8,at+30,width-16,24),LedgerPresentation.ItemRich(subtitle),itemButton);float y=at+58;
                if(markerAliasKey==d.Key){markerAlias=Search(new Rect(8,y,width-110,27),markerAlias,"MarkerAlias");if(Button(new Rect(width-94,y,86,27),"Save alias"))MarkerEdit(m=>{d.Alias=string.IsNullOrWhiteSpace(markerAlias)?"":LedgerMarkerModel.Name(markerAlias,100);markerAliasKey=null;});y+=32;}
                if(LedgerJournalUi.MarkerIssue(d)){GUI.Label(new Rect(8,y,width-116,27),new GUIContent(d.Conflict?"Native marker changed":"Marker unavailable",d.Status),itemButton);
                    if(d.Conflict){if(Button(new Rect(width-100,y,92,25),"Detach"))MarkerEdit(m=>m.Detach(d));}
                    else if(Button(new Rect(width-100,y,92,25),"Mark"))MarkerEdit(m=>m.Restore(d));y+=30;}
                foreach(var purpose in d.Purposes.Where(p=>model.Listed(p)&&markerSelection.IncludesPurpose(p)).ToArray()){
                    if(MarkerEye(new Rect(8,y,30,27),purpose.Service?model.Active(purpose):model.ManualActive(purpose),(purpose.Visibility==0?"Follow route: ":"Manual: ")+(model.ManualActive(purpose)?"shown":"hidden")))MarkerEdit(m=>m.Eye(purpose));
                    float labelX=44;
                    if(editing!=null){bool member=purpose.Routes.Contains(editing.Id);if(MarkerCheck(new Rect(44,y,30,27),member,(member?"Remove from ":"Add to ")+editing.Name))MarkerEdit(m=>m.Membership(purpose,editing.Id,!member));labelX=80;}
                    string membership=string.Join(", ",routes.Where(r=>purpose.Routes.Contains(r.Id)).Select(r=>r.Name));string summary=model.Summary(purpose);
                    float trackWidth=purpose.Kind=="offer"?100:0;
                    if(trackWidth>0){
                        bool oldEnabled=GUI.enabled;GUI.enabled=oldEnabled&&(purpose.TrackUpdates||LedgerTradeUpdates.Persistent(model,d));
                        if(MarkerCheck(new Rect(width-132,y,28,27),purpose.TrackUpdates,"Track trader refresh updates"))
                            MarkerEdit(m=>LedgerTradeUpdates.Track(m,d,purpose,!purpose.TrackUpdates));
                        Label(new Rect(width-100,y,64,27),"Updates");GUI.enabled=oldEnabled;
                    }
                    string attention=LedgerTradeUpdates.Attention(purpose);string shown=summary+(attention.Length>0?" · "+attention:"");
                    if(LedgerTradeUpdates.Ready(purpose))shown="[c][80E680]"+shown+"[-][/c]";
                    GUI.Label(new Rect(labelX,y,width-labelX-40-trackWidth,29),new GUIContent(LedgerPresentation.ItemRich(shown),summary+(membership.Length==0?"":" / "+membership)),itemButton);
                    var purposeRect=new Rect(width-32,y,24,27);var purposeAnchor=new Rect(body.x+purposeRect.x,area.y+purposeRect.y-markerScroll.y,24,27);
                    string[] purposeActions=purpose.Kind=="offer"?new[]{"Follow route display",purpose.ForceNotify?"Force notify: on":"Force notify: off","Remove purpose"}:new[]{"Follow route display","Remove purpose"};
                    if(UiMenuButton(purposeRect,"...","Purpose actions"))UiMenu(purposeAnchor,"purpose:"+d.Key+purpose.Key,purposeActions,-1,i=>MarkerEdit(m=>{if(i==0)m.Auto(purpose);else if(purposeActions[i].StartsWith("Force notify",StringComparison.Ordinal))purpose.ForceNotify=!purpose.ForceNotify;else m.RemovePurpose(d,purpose);}));y+=33;
                }
                at+=h+6;
            }
            if(destinations.Count==0)Label(new Rect(8,4,width-16,30),"No destinations in this view");GUI.EndScrollView();
            if(NativeBookmarks.SaveFailed){Label(new Rect(body.x+8,body.yMax-33,body.width-134,30),NativeBookmarks.Status);if(Button(new Rect(body.xMax-118,body.yMax-33,110,27),"Retry save"))NativeBookmarks.RetrySave();}
        }
    }
}
