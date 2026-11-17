using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public sealed partial class LedgerPrototypeWindow
    {
        private LedgerRequestFilters requestFilters=new LedgerRequestFilters();
        private int requestPopup;
        private readonly HashSet<string> requestExpanded=new HashSet<string>();
        private Vector2 requestPlaceScroll;private System.Collections.Generic.HashSet<string> requestCollapsed=new System.Collections.Generic.HashSet<string>();
        private Vector2 requestChoiceScroll;
        private readonly LedgerRequestListProjection requestList=new LedgerRequestListProjection();
        private readonly LedgerRequestLayoutGate requestLayoutGate=new LedgerRequestLayoutGate();private int requestExpansion;
        private LedgerViewData requestChoiceData;private int requestChoiceGeography=-1,requestChoiceRevision=-1,requestConversations;private string requestChoicePlayfield;
        private List<LedgerRequestChoice> requestPois,requestItems,requestPlayfields,requestPopupChoices,requestPopupSource;private int requestPopupKind;
        private sealed class RequestLayout {
            internal readonly List<List<LedgerRequestCompactItem>> Rows=new List<List<LedgerRequestCompactItem>>();internal readonly List<List<float>> RowHeights=new List<List<float>>();
            internal readonly List<float> Heights=new List<float>(),Titles=new List<float>();internal readonly List<string> Bodies=new List<string>();internal float[] Starts;internal float Total;
        }
        private RequestLayout requestLayout;
        private void RequestFilterChanged(){requestsScroll=Vector2.zero;CancelRequestGesture();}
        private void RequestFilterButton(Rect rect,string title,int popup)
        {if(Button(rect,title)){requestPopup=requestPopup==popup?0:popup;requestChoiceScroll=Vector2.zero;CancelRequestGesture();}}
        private void DrawRequestBrowser()
        {
            Rect whole=Content;float navWidth=Math.Max(220,Math.Min(310,whole.width*.23f));
            var places=data.RequestPlaces;var nodes=places.Visible(requestCollapsed);
            DrawRows(new Rect(whole.x,whole.y,navWidth,whole.height),ref requestPlaceScroll,nodes.Count,28,(i,r)=>{var p=nodes[i];float indent=p.Depth*12;if(p.Children.Count>0&&Button(new Rect(r.x+indent,r.y,22,26),requestCollapsed.Contains(p.Key)?"+":"-")){if(!requestCollapsed.Add(p.Key))requestCollapsed.Remove(p.Key);}var text=new Rect(r.x+indent+24,r.y,r.width-indent-24,r.height);if(SelectRow(text,requestFilters.Place==p.Key)){requestFilters.Location=null;requestFilters.Place=p.Key;RequestFilterChanged();}Label(text,p.Label);});
            Rect c=new Rect(whole.x+navWidth+12,whole.y,whole.width-navWidth-12,whole.height);float x=c.x;bool enabled=GUI.enabled;
            if(!ReferenceEquals(requestChoiceData,data)||requestChoiceGeography!=data.GeographyRevision||requestChoiceRevision!=manualRevision){
                requestChoiceData=data;requestChoiceGeography=data.GeographyRevision;requestChoiceRevision=manualRevision;requestChoicePlayfield=null;requestPois=null;
                requestItems=LedgerRequestBrowser.Items(data);requestPlayfields=LedgerRequestBrowser.Playfields(data);
                requestConversations=data.ArchiveStateReady?data.RequestCards().Count(card=>card.Request==null&&card.Pages.Any(p=>!data.ConversationArchived(p))):0;
            }
            int conversations=requestConversations;
            var modes=new[]{"Pending","Done","Conversations ("+conversations+")","Archived"};int mode=requestFilters.Archived?3:requestFilters.Conversations?2:requestFilters.Done?1:0;
            var modeRect=new Rect(x,c.y,172,28);
            if(UiMenuButton(modeRect,modes[mode],"Request status or informational conversations"))UiMenu(modeRect,"request-status",modes,mode,i=>{requestFilters.Done=i==1;requestFilters.Conversations=i==2;requestFilters.Archived=i==3;requestPopup=0;RequestFilterChanged();});x+=178;
            var header=new LedgerRequestHeaderLayout(c.width);float searchWidth=header.SearchWidth,pfWidth=header.PlayfieldWidth,poiWidth=header.PoiWidth,itemWidth=header.ItemWidth;
            Rect pfRect=new Rect(x,c.y,pfWidth,28);x+=pfWidth+6;
            Rect poiRect=new Rect(x,c.y,poiWidth,28);x+=poiWidth+6;
            Rect itemRect=new Rect(x,c.y,itemWidth,28);x+=itemWidth+6;
            if(requestPois==null||requestChoicePlayfield!=requestFilters.Playfield){requestChoicePlayfield=requestFilters.Playfield;requestPois=LedgerRequestBrowser.Pois(data,requestFilters.Playfield);}var pois=requestPois;var items=requestItems;
            var poi=pois.FirstOrDefault(o=>o.Key==requestFilters.Poi);var item=items.FirstOrDefault(o=>o.ItemId==requestFilters.ItemId);
            RequestFilterButton(pfRect,requestFilters.Location==null?requestFilters.Playfield??"Playfield":requestFilters.Location.Label,1);
            RequestFilterButton(poiRect,poi==null?"POI":poi.Label,2);
            RequestFilterButton(itemRect,requestFilters.Other?"Other":item==null?"Required item":item.Label,3);
            string query=Search(new Rect(x,c.y,searchWidth,28),requestFilters.Search,"RequestSearch","Search requests");x+=searchWidth+6;
            if(query!=requestFilters.Search){requestFilters.Search=query;RequestFilterChanged();}
            if(GUI.Button(new Rect(x,c.y,68,28),new GUIContent("Reset","Clear filters and search"))){requestFilters.ResetRestrictions();requestPopup=0;RequestFilterChanged();}
            var entries=requestList.Query(data,requestFilters,RequestInventoryProgress,manualRevision);
            float width=c.width-LedgerLayout.Scrollbar-19,total=0;var controls=new LedgerRequestRowLayout(width);var compact=new LedgerRequestCompactLayout(width-16);
            if(requestLayoutGate.Changed(entries,RequestInventoryProgress,width,RequestSellerLayoutVersion(),manualRevision,requestExpansion)){
            requestLayout=new RequestLayout{Starts=new float[entries.Count]};int rowIndex=0;
            var allRowsBuild=requestLayout.Rows;var rowHeightsBuild=requestLayout.RowHeights;var heightsBuild=requestLayout.Heights;var titlesBuild=requestLayout.Titles;var bodiesBuild=requestLayout.Bodies;
            foreach(var card in entries){requestLayout.Starts[rowIndex++]=total;
                LedgerRequestInventoryProgress progress;var inventory=RequestInventoryProgress;
                if(inventory==null||!inventory.TryGetValue(LedgerRequestBrowser.StableKey(card),out progress))progress=null;
                var rows=LedgerRequestCardPresentation.Rows(card.Request==null?null:card.Request.Fact,catalog,progress,RequestSellerHints(card));allRowsBuild.Add(rows);
                var rh=rows.Select(r=>compact.RowHeight(rich.CalcHeight(new GUIContent(LedgerPresentation.Rich(r.Requirement)),compact.Name),rich.CalcHeight(new GUIContent(r.Progress),compact.Progress),rich.CalcHeight(new GUIContent(r.Seller),compact.Seller))).ToList();rowHeightsBuild.Add(rh);
                float th=Math.Max(26,richHeading.CalcHeight(new GUIContent(LedgerPresentation.Rich(data.VendorFor(card.Source))),width-150));titlesBuild.Add(th);
                bool expanded=requestExpanded.Contains(LedgerRequestBrowser.StableKey(card));
                string body=card.Request==null?string.Join("\n\n",card.Pages.Select(o=>LedgerPresentation.Rich(o.Dialogue.BodyVerbatim)).ToArray()):expanded?LedgerPresentation.RequestBody(card.Request):"";bodiesBuild.Add(body);
                int groups=rows.Select(r=>r.Branch).Distinct().Count();float h=th+28+rh.Sum()+(groups>1?groups*24:0)+36+(string.IsNullOrEmpty(body)?0:rich.CalcHeight(new GUIContent(body),width-16)+12);heightsBuild.Add(h);total+=h+8;
            }
            requestLayout.Total=total;
            }
            var allRows=requestLayout.Rows;var rowHeights=requestLayout.RowHeights;var heights=requestLayout.Heights;var titles=requestLayout.Titles;var bodies=requestLayout.Bodies;total=requestLayout.Total;
            string stateError=manualError??(mode>=2&&!data.ArchiveStateReady?"Conversation state unavailable. Refresh to retry.":null);
            float offset=(mode>=2?32:62)+(stateError==null?0:28);
            if(mode<2)GUI.Label(new Rect(c.x,c.y+32,c.width,24),new GUIContent(RequestInventoryCoverage,RequestInventoryDiagnostics));if(stateError!=null)Label(new Rect(c.x,c.y+offset-28,c.width,26),stateError);
            Rect port=new Rect(c.x,c.y+offset,c.width,c.height-offset);GUI.enabled=enabled&&requestPopup==0;
            requestsScroll.y=Math.Max(0,Math.Min(requestsScroll.y,Math.Max(0,total-port.height)));
            requestsScroll=GUI.BeginScrollView(port,requestsScroll,new Rect(0,0,width,Math.Max(34,total)),false,true);
            float y=0;for(int i=LedgerRequestLayoutGate.First(requestLayout.Starts,requestsScroll.y);i<entries.Count;i++)
            {
                y=requestLayout.Starts[i];if(y>requestsScroll.y+port.height)break;var card=entries[i];float h=heights[i];string key=LedgerRequestBrowser.StableKey(card);
                if(y+h>=requestsScroll.y&&y<=requestsScroll.y+port.height)
                {
                    // One quiet card surface. Helpers are aligned text, not full-width button bands.
                    Backplate(new Rect(0,y,width,h));
                    var request=card.Request;LedgerRequestInventoryProgress progress;var inventory=RequestInventoryProgress;
                    if(inventory==null||!inventory.TryGetValue(key,out progress))progress=null;
                    Label(new Rect(8,y+2,width-158,titles[i]),data.VendorFor(card.Source),true);
                    string status=request==null?"Conversation":requestFilters.Done?"Done":LedgerRequestCardPresentation.Status(progress);Color prior=GUI.contentColor;
                    GUI.contentColor=progress!=null&&progress.Ready?new Color(.55f,.95f,.6f):progress!=null&&(progress.Partial||progress.Ambiguous)?new Color(1f,.82f,.45f):new Color(.65f,.85f,.95f);
                    Label(new Rect(width-146,y+4,138,26),status);GUI.contentColor=prior;
                    string reward=request==null?"":LedgerRequestCardPresentation.Reward(request.Fact);
                    GUI.contentColor=new Color(.68f,.74f,.8f);Label(new Rect(8,y+titles[i],width-16,26),data.PlaceFor(card.Source)+(string.IsNullOrEmpty(reward)?"":" | "+reward));GUI.contentColor=prior;
                    float rowY=y+titles[i]+28;int branch=-1;bool alternatives=allRows[i].Select(r=>r.Branch).Distinct().Count()>1;
                    for(int n=0;n<allRows[i].Count;n++){
                        var itemRow=allRows[i][n];float rh=rowHeights[i][n];
                        if(alternatives&&itemRow.Branch!=branch){Label(new Rect(8,rowY,width-16,24),"Option "+(itemRow.Branch+1),true);rowY+=24;branch=itemRow.Branch;}
                        GUI.Label(new Rect(8,rowY,compact.Name,rh),LedgerPresentation.Rich(itemRow.Requirement),rich);
                        GUI.contentColor=new Color(.78f,.84f,.88f);if(!requestFilters.Done)GUI.Label(new Rect(14+compact.Name,rowY,compact.Progress,rh),itemRow.Progress,rich);GUI.contentColor=prior;
                        if(!requestFilters.Done)DrawRequestSeller(new Rect(20+compact.Name+compact.Progress,rowY,compact.Seller+compact.Market,rh),card,itemRow.Hint,itemRow.Seller);
                        rowY+=rh;
                    }
                    float actions=y+h-32;
                    if(request!=null&&RequestInventoryAction(new Rect(8,actions,78,28),"details:"+key,requestExpanded.Contains(key)?"Less":"Details","Original dialogue and request details",true)){if(!requestExpanded.Add(key))requestExpanded.Remove(key);requestExpansion++;CancelRequestGesture();}
                    var required=request!=null&&request.Fact.Items.Count==1?request.Fact.Items[0]:null;var target=LedgerItemDisplay.Request(data,catalog,required);
                    if(target!=null&&RequestInventoryAction(new Rect(controls.Item,actions,74,28),"item:"+key,"Item","Item details and offers",true))SelectCatalogItem(target,true);
                    LedgerBookmarkPlan destination;string destinationReason;bool destinationOk=LedgerBookmarkPolicy.TryRequest(data,card.Source,currentContext,out destination,out destinationReason,request==null?null:request.Fact);
                    if(request==null)DrawConversationArchive(new Rect(width-106,actions,98,28),card);
                    else{
                        if(RequestInventoryAction(new Rect(controls.Deliver,actions,90,28),"deliver:"+key,"Deliver",destinationOk?"Delivery destination":destinationReason,destinationOk))bookmarkRequested=destination;
                        DrawTracking(new Rect(controls.Track,actions,86,28),card);DrawCompletion(new Rect(controls.Complete,actions-2,LedgerRequestRowLayout.CompletionWidth,LedgerRequestRowLayout.CompletionHeight),card);
                    }
                    if(!string.IsNullOrEmpty(bodies[i]))GUI.Label(new Rect(8,rowY+6,width-16,Math.Max(0,actions-rowY-8)),bodies[i],rich);
                }
                y+=h+8;
            }
            if(entries.Count==0)Label(new Rect(8,4,width-16,28),mode==0&&conversations>0?"No matching requests. Unrecognized pages are under Conversations.":"No matching entries.");GUI.EndScrollView();GUI.enabled=enabled;
            if(requestPopup!=0)
            {
                var sourceChoices=requestPopup==1?requestPlayfields:requestPopup==2?pois:items;
                if(requestPopupChoices==null||requestPopupKind!=requestPopup||!ReferenceEquals(requestPopupSource,sourceChoices)){requestPopupKind=requestPopup;requestPopupSource=sourceChoices;requestPopupChoices=new[]{new LedgerRequestChoice{Label="Any"}}.Concat(sourceChoices).ToList();if(requestPopup==3)requestPopupChoices.Insert(1,new LedgerRequestChoice{Key="other",Label="Other"});}var choices=requestPopupChoices;
                var anchor=requestPopup==1?pfRect:requestPopup==2?poiRect:itemRect;
                Rect popup=new Rect(anchor.x,c.y+32,anchor.width,Math.Min(280,choices.Count*28));Backplate(popup);GUI.Box(popup,"",row);
                DrawRows(popup,ref requestChoiceScroll,choices.Count,28,(i,r)=>
                {
                    var choice=choices[i];bool selected=requestPopup==1?choice.Key==requestFilters.Playfield:requestPopup==2?choice.Key==requestFilters.Poi:requestFilters.Other?choice.Key=="other":choice.ItemId==requestFilters.ItemId&&choice.Key!="other";
                    if(SelectRow(r,selected))
                    {
                        if(requestPopup==1){requestFilters.Location=null;requestFilters.Playfield=choice.Key;requestFilters.Poi=null;}
                        else if(requestPopup==2){requestFilters.Location=null;requestFilters.Poi=choice.Key;}
                        else{requestFilters.Other=choice.Key=="other";requestFilters.ItemId=choice.ItemId;}
                        requestPopup=0;RequestFilterChanged();
                    }
                    Label(r,choice.Label);
                });
            }
        }
    }
}
