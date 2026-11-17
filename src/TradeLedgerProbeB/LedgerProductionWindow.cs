using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public sealed partial class LedgerPrototypeWindow
    {
        private Vector2 productionScroll,productionPlacesScroll,productionSummaryScroll,productionPolicyScroll;
        private string servicePolicyKey="",serviceFieldsKey,serviceHours="5",serviceOwnHours="";
        private bool servicePolicyPopup,serviceSettingsExpanded;
        private GUIStyle productionQuantityStyle;
        private string productionSearch="",productionPlace="all",productionSelected,productionWorld,productionResource;private int productionKind;
        private LedgerViewData productionPlacesData;private LedgerProductionPlaces productionPlaces;private LedgerProductionSizeOrder productionSizes;
        private int productionGeographyRevision=-1;
        private ILookup<string,LedgerObservation> productionMinerLookup;
        private readonly HashSet<string> productionCollapsed=new HashSet<string>();private readonly LedgerHeaderSort productionSizeSort=new LedgerHeaderSort(),productionServiceSort=new LedgerHeaderSort();
        private string productionForgetArmed,productionForgetError;
        private readonly HashSet<string> productionForgetting=new HashSet<string>();
        private readonly Queue<KeyValuePair<string,string>> productionForgetResults=new Queue<KeyValuePair<string,string>>();
        private volatile bool productionDirty;private long productionLoadedAt;
        internal void ProductionCommitted(LedgerObservation o){if(o.Kind=="place-observed"||o.Kind=="market"||o.Kind=="dialogue-shown")placeDirty=true;if(o.Kind=="production"||o.Kind=="production-lifecycle")productionDirty=true;}
        private void DrawProduction()
        {
            lock(resultGate)while(productionForgetResults.Count>0){var result=productionForgetResults.Dequeue();productionForgetting.Remove(result.Key);productionForgetError=result.Value==null?null:"Device record wasn't forgotten. Try again.";productionDirty=true;refreshRequested=true;}
            Rect whole=Content;DateTime now=DateTime.UtcNow;ulong tick=Application==null?0:Application.GameTicks;
            if(productionWorld!=world){productionWorld=world;productionPlace="all";productionResource=null;productionCollapsed.Clear();productionSizes=null;productionPlacesData=null;productionSelected=null;productionPlacesScroll=productionScroll=Vector2.zero;}
            if(productionPlaces==null||!ReferenceEquals(productionPlacesData,data)||productionGeographyRevision!=data.GeographyRevision){
                productionGeographyRevision=data.GeographyRevision;productionPlacesData=data;productionPlaces=new LedgerProductionPlaces(world,LedgerGeographyProjection.Contexts(data));productionPlace=productionPlaces.Resolve(productionPlace).Key;
                productionMinerLookup=data.Production.Where(o=>o.Production.Kind=="producer"&&o.Production.Family=="AutoMiner"&&!string.IsNullOrEmpty(o.Production.DepositKey)).ToLookup(o=>o.Production.DepositKey);
            }
            if(productionSizes==null){string[] labels=null;try{if(NativeCatalog!=null)labels=new[]{"txtVerySmall","txtSmall","txtMedium","txtLarge","txtVeryLarge"}.Select(NativeCatalog.LocalizeRich).ToArray();}catch(Exception){/* Native English labels remain supported; unknowns sort last. */}productionSizes=new LedgerProductionSizeOrder(labels);}
            float navWidth=Math.Max(250,Math.Min(420,whole.width*.26f));var nav=new Rect(whole.x,whole.y,navWidth,whole.height);
            Label(new Rect(nav.x+6,nav.y,nav.width-12,27),"Locations",true);var navigation=productionPlaces.Visible(productionCollapsed);
            DrawRows(new Rect(nav.x,nav.y+33,nav.width,nav.height-33),ref productionPlacesScroll,navigation.Count,28,(i,r)=>{
                var place=navigation[i];float indent=place.Depth*14;
                if(place.Children.Count>0&&Button(new Rect(r.x+indent,r.y+2,23,24),productionCollapsed.Contains(place.Key)?"+":"−")){if(!productionCollapsed.Add(place.Key))productionCollapsed.Remove(place.Key);}
                var name=new Rect(r.x+indent+25,r.y,r.width-indent-25,r.height);
                if(SelectRow(name,productionPlace==place.Key)){productionPlace=place.Key;servicePolicyKey="";servicePolicyPopup=false;productionScroll=Vector2.zero;}
                Label(name,place.Label,place.Role=="system");if(name.Contains(Event.current.mousePosition))iconHoverName=place.Label;
            },"No recorded locations.",.55f,.12f);
            Rect c=new Rect(whole.x+navWidth+12,whole.y,whole.width-navWidth-12,whole.height);
            var scoped=data.Production.Where(o=>productionPlaces.Contains(productionPlace,o)).ToList();
            int devices=scoped.Count(o=>o.Production.Kind=="producer"),deposits=scoped.Count(o=>o.Production.Kind=="deposit");
            for(int kind=0;kind<2;kind++)if(Button(new Rect(c.x+kind*151,c.y,144,27),(kind==0?"Devices ("+devices:"Deposits ("+deposits)+")")){productionKind=kind;productionScroll=Vector2.zero;}
            float searchX=c.x+315,searchWidth=Math.Max(180,c.width-407);string priorSearch=productionSearch;
            productionSearch=Search(new Rect(searchX,c.y,searchWidth,27),productionSearch,"ProductionSearch","Search devices or resources");
            if(priorSearch!=productionSearch)productionScroll=Vector2.zero;
            if(Button(new Rect(c.xMax-78,c.y,78,27),"Reset")){productionSearch="";productionResource=null;productionPlace="all";servicePolicyKey="";servicePolicyPopup=false;productionSizeSort.Column=productionServiceSort.Column=-1;productionScroll=Vector2.zero;scoped=data.Production.ToList();}
            bool depositView=productionKind==1;
            var matching=LedgerDeviceSelection.Filter(scoped,productionSearch,productionResource);
            float resourceExtra=DrawProductionServiceControls(c,scoped,matching,tick);
            var rows=depositView?LedgerProductionDisplay.Filter(scoped,true,null,productionSearch):LedgerDeviceSelection.Filter(scoped,productionSearch,productionResource);
            if(depositView)rows=productionSizeSort.Column==2?productionSizes.Sort(rows,productionSizeSort):LedgerProductionServices.SortDeposits(rows,productionSizeSort,productionSizes,data.Production,tick,world);
            if(!depositView)rows=LedgerProductionDisplay.SortDevices(rows,productionServiceSort,tick,world);
            var lines=LedgerProductionDisplay.Lines(rows);
            bool terminal=LedgerProductionDisplay.Terminal(productionPlaces.Resolve(productionPlace));
            var columns=LedgerProductionDisplay.Columns(depositView,terminal);
            var headers=LedgerProductionDisplay.Headers(depositView,terminal);
            Color headerColor=GUI.color;GUI.color=new Color(.68f,.85f,1f,1f);
            var header=new Rect(c.x,c.y+139+resourceExtra+LedgerJournalUi.ProductionExtra(serviceSettingsExpanded),c.width-LedgerLayout.Scrollbar-3,26);
            int serviceColumn=terminal?6:7;
            if(!depositView)for(int i=0;i<headers.Length-1;i++)headers[i]="";
            int sizeColumn=terminal?1:2;if(depositView){headers[0]="";headers[sizeColumn]="";headers[sizeColumn+1]="";}
            TableHeader(header,headers,columns);GUI.color=headerColor;
            if(!depositView){
                var names=LedgerProductionDisplay.Headers(false,terminal);float x=header.x;
                for(int i=0;i<names.Length-1;i++){
                    int column=terminal&&i>=2?i+1:i;float width=header.width*columns[i];
                    bool enabled=GUI.enabled;GUI.enabled=enabled&&!servicePolicyPopup;
                    if(GUI.Button(new Rect(x+4,header.y,width-8,header.height),new GUIContent(productionServiceSort.Label(names[i],column),"Sort by "+names[i]),itemButton)){
                        productionServiceSort.Click(column);productionScroll=Vector2.zero;
                    }GUI.enabled=enabled;x+=width;
                }
            }
            if(depositView){
                float nameWidth=header.width*columns[0];
                ProductionSortButton(new Rect(header.x+4,header.y,nameWidth-8,header.height),"Resource",0);
                float sizeX=header.x+header.width*columns.Take(sizeColumn).Sum();
                ProductionSortButton(new Rect(sizeX+4,header.y,header.width*columns[sizeColumn]-8,header.height),"Size",2);
                float minerX=sizeX+header.width*columns[sizeColumn],minerWidth=header.width*columns[sizeColumn+1];
                ProductionSortButton(new Rect(minerX+4,header.y,minerWidth*.58f-8,header.height),"Has miner",3);
                ProductionSortButton(new Rect(minerX+minerWidth*.58f,header.y,minerWidth*.42f-4,header.height),"Fill",4);
            }
            Rect port=new Rect(c.x,c.y+LedgerJournalUi.ProductionHeader(serviceSettingsExpanded)+resourceExtra,c.width,c.height-LedgerJournalUi.ProductionHeader(serviceSettingsExpanded)-resourceExtra-LedgerProductionDisplay.FooterHeight);
            string empty=LedgerProductionDisplay.Empty(depositView,data.ProductionReadError);
            Func<int,string> definition=id=>{LedgerCatalogItem item;return catalog!=null&&catalog.Items.TryGetValue(id,out item)?item.InternalName:null;};
            bool tableEnabled=GUI.enabled;GUI.enabled=tableEnabled&&!servicePolicyPopup;
            DrawRows(port,ref productionScroll,data.ProductionReadError==null?(depositView?rows.Count:lines.Count):0,depositView?LedgerProductionDisplay.DepositRow:LedgerProductionDisplay.DeviceRow,(i,r)=>{
                var o=depositView?rows[i]:lines[i].Observation;var p=o.Production;bool first=depositView||lines[i].First;
                float actionWidth=r.width*columns.Last();
                if(SelectRow(new Rect(r.x,r.y,r.width-actionWidth,r.height),productionSelected==p.Key))productionSelected=p.Key;
                var e=LedgerProduction.Estimate(p,tick,world);
                var due=depositView?null:LedgerProductionDue.Evaluate(p,tick,world);
                var values=depositView?LedgerProductionDisplay.DepositCells(o,now,productionMinerLookup[p.Key],tick,world,definition):LedgerProductionDisplay.DeviceCells(lines[i],e,now,definition,due);
                TableCells(r,LedgerProductionDisplay.VisibleCells(values,depositView,terminal),columns);
                if(!depositView&&first){int fillColumn=terminal?3:4;float fillX=r.x+r.width*columns.Take(fillColumn).Sum();DrawProductionFill(new Rect(fillX+5,r.y+5,r.width*columns[fillColumn]-10,r.height-10),LedgerProductionServices.State(p,tick,world).Fill);}
                if(first){var plan=LedgerProduction.Bookmark(o,currentContext);DrawRowBookmark(new Rect(r.xMax-actionWidth+2,r.y+2,actionWidth-5,r.height-4),plan,plan!=null,plan==null?"Recorded position or current save is unavailable":null,"Mark");}
                var rowBody=new Rect(r.x,r.y,r.width-actionWidth,r.height);
                if(!depositView&&rowBody.Contains(Event.current.mousePosition)){
                    float dueX=r.x+r.width*columns.Take(serviceColumn).Sum();
                    iconHoverName=due.Seconds.HasValue&&Event.current.mousePosition.x>=dueX&&Event.current.mousePosition.x<dueX+r.width*columns[serviceColumn]?LedgerProductionDisplay.DeviceName(p,definition)+" / "+o.Context.Playfield+": "+due.Hint:null;
                }
            },empty,.55f,.12f);
            Rect footer=new Rect(c.x,port.yMax+7,c.width,LedgerProductionDisplay.FooterHeight-7);Backplate(footer,.9f);
            var selected=rows.FirstOrDefault(o=>o.Production.Key==productionSelected);
            if(selected!=null){Label(new Rect(footer.x+7,footer.y,footer.width-(depositView?16:132),29),(productionForgetError??LedgerProductionDisplay.Name(selected.Production))+(terminal?"":" / "+selected.Context.Playfield),true);
                if(!depositView){
                    string key=selected.Production.Key;bool waiting=productionForgetting.Contains(key);var forgetRect=new Rect(footer.xMax-116,footer.y+1,108,28);
                    if(forgetRect.Contains(Event.current.mousePosition))iconHoverName="Forget journal entry; opening the device records it again.";
                    bool enabled=GUI.enabled;GUI.enabled=enabled&&!waiting&&ManualStore!=null&&selected.Production.Player==manualPlayer;
                    if(Button(forgetRect,waiting?"Saving...":productionForgetArmed==key?"Confirm":"Forget")){
                        if(productionForgetArmed!=key)productionForgetArmed=key;
                        else{productionForgetArmed=null;var command=LedgerProductionLifecycle.Forget(selected,currentContext);
                            if(command!=null&&ManualStore.Enqueue(command,error=>{lock(resultGate)productionForgetResults.Enqueue(new KeyValuePair<string,string>(key,error));}))productionForgetting.Add(key);
                            else productionForgetError="Device record wasn't forgotten. Try again.";
                        }
                    }GUI.enabled=enabled;
                }
}
            else Label(new Rect(footer.x+7,footer.y+2,footer.width-14,28),productionForgetError??(NativeBookmarks!=null&&(NativeBookmarks.SaveFailed||NativeBookmarks.Status.Length>0)?NativeBookmarks.Status:""));
            GUI.enabled=tableEnabled;DrawProductionServiceChoices(c,scoped);
        }
        private void ProductionSortButton(Rect rect,string title,int column)
        {bool enabled=GUI.enabled;GUI.enabled=enabled&&!servicePolicyPopup;if(GUI.Button(rect,new GUIContent(productionSizeSort.Label(title,column),"Ascending / descending / default"),itemButton)){productionSizeSort.Click(column);productionScroll=Vector2.zero;}GUI.enabled=enabled;}
        private void DrawProductionFill(Rect rect,double? percent)
        {
            Backplate(rect,.8f);if(percent.HasValue){Color old=GUI.color;GUI.color=new Color(.33f,.57f,.72f,.9f);GUI.DrawTexture(new Rect(rect.x,rect.y,rect.width*(float)(percent.Value/100),rect.height),Texture2D.whiteTexture);GUI.color=old;}
            GUI.Label(rect,new GUIContent(percent.HasValue?LedgerProductionServices.DisplayedFill(percent)+"%":"unknown"),itemButton);
        }
        private float DrawProductionServiceControls(Rect c,List<LedgerObservation> scoped,List<LedgerObservation> matching,ulong tick)
        {
            bool ready=NativeBookmarks!=null&&NativeBookmarks.Ready;var prefs=ready?LedgerProductionServices.Preferences(NativeBookmarks.Model,world,manualPlayer??0,false):null;
            prefs=prefs??new LedgerServicePreferences{World=world,Player=manualPlayer??0};
            var producers=scoped.Where(o=>o.Production.Kind=="producer").ToList();
            var group=producers.FirstOrDefault(o=>LedgerProductionServices.GroupKey(o)==servicePolicyKey);
            if(servicePolicyKey.Length>0&&group==null)servicePolicyKey="";
            string policy=servicePolicyKey.Length>0?servicePolicyKey:productionPlace;
            var ancestry=productionPlaces.Ancestry(group==null?productionPlace:"playfield:"+group.Context.Playfield).ToList();
            var effective=prefs.Effective(ancestry,group==null?null:servicePolicyKey);var own=prefs.For(policy);
            string fieldsKey=world+"|"+manualPlayer+"|"+policy;
            if(serviceFieldsKey!=fieldsKey){serviceFieldsKey=fieldsKey;serviceHours=prefs.Hours.ToString(System.Globalization.CultureInfo.InvariantCulture);serviceOwnHours=(own==null?effective.Hours:own.Hours??effective.Hours).ToString(System.Globalization.CultureInfo.InvariantCulture);}
            bool enabled=GUI.enabled;GUI.enabled=enabled&&ready;
            float y=c.y+34,x=c.x+4;string policyLabel=group==null?productionPlaces.Resolve(productionPlace).Label:LedgerProductionServices.GroupName(group);
            if(MarkerCheck(new Rect(x,y,28,27),prefs.Enabled,"Automatic service alerts"))ServiceEdit(p=>p.Enabled=!p.Enabled);
            Label(new Rect(x+31,y,68,27),"Alerts");x+=110;
            bool inherited=prefs.Effective(ancestry.Where(k=>k!=policy)).Suppressed;
            if(MarkerCheck(new Rect(x,y,28,27),own!=null&&own.Suppress,"Suppress automatic alerts: "+policyLabel))ServiceEdit(p=>p.For(policy,true).Suppress=!(p.For(policy)?.Suppress??false));
            Label(new Rect(x+30,y,210,27),inherited?"Suppress (inherited)":group==null?"Suppress":"Suppress group");
            if(UiMenuButton(new Rect(c.xMax-306,y,116,27),serviceSettingsExpanded?"Settings -":"Settings +","Alert thresholds and group overrides")){serviceSettingsExpanded=!serviceSettingsExpanded;servicePolicyPopup=false;}
            GUI.enabled=enabled&&ready&&matching.Count>0;
            if(Button(new Rect(c.xMax-136,y,132,27),"Mark group"))NativeBookmarks.MarkServiceScope(currentContext,Application,productionPlaces,productionPlace,matching.Select(o=>o.Production.Key));
            GUI.enabled=enabled&&ready;
            var routes=ready?NativeBookmarks.Model.Data.Routes.Where(r=>r.World==world&&r.Player==manualPlayer&&r.ServiceKey!=null&&(group!=null?r.ServiceKey==servicePolicyKey:producers.Any(o=>LedgerProductionServices.GroupKey(o)==r.ServiceKey))).ToList():new List<LedgerMarkerRoute>();
            if(routes.Count>0){bool shown=routes.Any(r=>r.Shown);if(MarkerEye(new Rect(c.xMax-180,y,34,27),shown,shown?"Hide manual group display":"Show group manually"))MarkerEdit(m=>{foreach(var route in routes)m.ShowRoute(route,!shown);});}
            if(serviceSettingsExpanded){
                y+=34;x=c.x+4;Label(new Rect(x,y,84,27),"Defaults");x+=92;
                Label(new Rect(x,y,100,27),"Service hours");ServiceNumber(new Rect(x+104,y,68,27),ref serviceHours,false,prefs.Hours,n=>ServiceEdit(p=>p.Hours=n),"Hours until storage or fuel stops production");
                y+=34;x=c.x+4;
                if(Button(new Rect(x,y,260,27),policyLabel+LedgerJournalUi.Dropdown)){servicePolicyPopup=!servicePolicyPopup;productionPolicyScroll=Vector2.zero;}x+=269;
                bool hoursOwn=own!=null&&own.Hours.HasValue;
                if(MarkerCheck(new Rect(x,y,28,27),hoursOwn,"Override service hours; unchecked inherits"))ServiceEdit(p=>p.For(policy,true).Hours=hoursOwn?(double?)null:effective.Hours);
                Label(new Rect(x+30,y,48,27),"Hours");GUI.enabled=enabled&&ready&&hoursOwn;ServiceNumber(new Rect(x+81,y,68,27),ref serviceOwnHours,false,own==null?effective.Hours:own.Hours??effective.Hours,n=>ServiceEdit(p=>p.For(policy,true).Hours=n),"Service lead time in hours");
            }
            GUI.enabled=enabled;
            var overview=LedgerProductionServices.Overview(matching,tick,world,prefs,productionPlaces);
            y=c.y+68+LedgerJournalUi.ProductionExtra(serviceSettingsExpanded);GUI.Label(new Rect(c.x+5,y,c.width-350,27),new GUIContent(overview.FillText+"    "+(producers.Select(o=>LedgerProductionServices.Threshold(prefs,productionPlaces,o).Hours).Distinct().Take(2).Count()==1?"Service horizon "+LedgerProductionServices.Threshold(prefs,productionPlaces,producers[0]).Hours.ToString(System.Globalization.CultureInfo.InvariantCulture)+"h: ":"Service by group: ")+overview.ReserveKnown+" sufficient | "+overview.ReserveLow+" soon | "+overview.ReserveUnknown+" unknown","Warning below "+effective.Hours+"h"),itemButton);
            var dueScope=LedgerProductionDue.Scope(producers,tick,world);
            GUI.Label(new Rect(c.xMax-338,y,333,27),new GUIContent(dueScope.Text,dueScope.Hint),itemButton);
            // Choices retain the location/search totals when a resource is active, so switching
            // resource never requires clearing it first. All rows of a multi-output device match.
            var choices=LedgerProductionServices.Overview(LedgerDeviceSelection.Filter(scoped,productionSearch,null),tick,world,prefs,productionPlaces).Resources;
            var totals=overview.Resources.ToDictionary(r=>LedgerDeviceSelection.Resource(r.Item,r.Auxiliary));
            float natural=LedgerDeviceSelection.Height(choices.Count,c.width),height=Math.Min(natural,Math.Max(28,Math.Min(168,c.height*.28f)));
            var port=new Rect(c.x,c.y+99+LedgerJournalUi.ProductionExtra(serviceSettingsExpanded),c.width,height);
            bool scroll=natural>height;float gridWidth=c.width-(scroll?LedgerLayout.Scrollbar:0);
            natural=LedgerDeviceSelection.Height(choices.Count,gridWidth);
            productionSummaryScroll.x=0;
            productionSummaryScroll=GUI.BeginScrollView(port,productionSummaryScroll,new Rect(0,0,gridWidth,natural),false,scroll);
            for(int i=0;i<choices.Count;i++){
                var resource=choices[i];string key=LedgerDeviceSelection.Resource(resource.Item,resource.Auxiliary);var cell=LedgerDeviceSelection.Cell(i,gridWidth);var rect=new Rect(cell[0],cell[1],cell[2],cell[3]);
                bool active=productionResource==key;Backplate(rect,active?.98f:.8f);
                LedgerServiceResource total;if(!totals.TryGetValue(key,out total))total=new LedgerServiceResource();
                if(active){Color old=GUI.color;GUI.color=new Color(.24f,.55f,.72f,.9f);GUI.DrawTexture(new Rect(rect.x,rect.y,3,rect.height),Texture2D.whiteTexture);GUI.color=old;}
                bool can=GUI.enabled;GUI.enabled=can&&productionKind==0;
                if(GUI.Button(rect,new GUIContent("",resource.Name+": "+total.Stock+" stored / "+total.Devices+" devices / "+total.RateText+" observed rate"),active?selectedRow:itemButton)){
                    productionResource=LedgerDeviceSelection.Toggle(productionResource,key);productionScroll=Vector2.zero;
                }
                if(productionQuantityStyle==null)productionQuantityStyle=new GUIStyle(itemButton){alignment=TextAnchor.MiddleRight,fontStyle=FontStyle.Bold};
                string quantity=total.Stock.ToString(System.Globalization.CultureInfo.InvariantCulture);
                float quantityWidth=Math.Min(rect.width*.45f,Math.Max(48,productionQuantityStyle.CalcSize(new GUIContent(quantity)).x+6));
                GUI.Label(new Rect(rect.x+3,rect.y,rect.width-quantityWidth-5,23),(active?"- ":"")+resource.Name,itemButton);
                Color priorColor=GUI.contentColor;GUI.contentColor=new Color(1f,.84f,.34f);GUI.Label(new Rect(rect.xMax-quantityWidth,rect.y,quantityWidth,23),quantity,productionQuantityStyle);
                GUI.contentColor=new Color(.4f,.88f,1f);GUI.Label(new Rect(rect.x+3,rect.y+23,rect.width-6,21),total.RateText+"  ("+total.Devices+")",itemButton);GUI.contentColor=priorColor;
                GUI.enabled=can;
            }
            if(choices.Count==0)Label(new Rect(5,0,gridWidth-10,24),"No matching resources");GUI.EndScrollView();
            return height-28;

        }
        private void ServiceEdit(Action<LedgerServicePreferences> action)
        {MarkerEdit(m=>action(LedgerProductionServices.Preferences(m,world,manualPlayer??0)));}
        private void ServiceNumber(Rect rect,ref string text,bool percent,double existing,Action<double> changed,string hint)
        {
            string prior=text;text=Search(rect,text,"Service"+rect.x+":"+rect.y);double value;
            bool valid=LedgerServicePreferences.Number(text,percent,out value);
            if(!valid){Color old=GUI.color;GUI.color=new Color(1,.45f,.4f,1);GUI.DrawTexture(new Rect(rect.x,rect.yMax-2,rect.width,2),Texture2D.whiteTexture);GUI.color=old;}
            if(prior!=text&&valid&&value!=existing)changed(value);
            if(rect.Contains(Event.current.mousePosition))iconHoverName=hint+(valid?"":"; enter "+(percent?"0-100":"nonnegative hours"));
        }
        private void DrawProductionServiceChoices(Rect c,List<LedgerObservation> scoped)
        {
            if(!servicePolicyPopup||!serviceSettingsExpanded)return;
            var choices=new List<KeyValuePair<string,string>>{new KeyValuePair<string,string>("",productionPlaces.Resolve(productionPlace).Label+" (place)")};
            choices.AddRange(scoped.Where(o=>o.Production.Kind=="producer").GroupBy(LedgerProductionServices.GroupKey).Select(g=>new KeyValuePair<string,string>(g.Key,LedgerProductionServices.GroupName(g.First()))).OrderBy(x=>x.Value,StringComparer.OrdinalIgnoreCase));
            var popup=new Rect(c.x+4,c.y+134,400,Math.Min(280,choices.Count*28));Backplate(popup,.98f);
            DrawRows(popup,ref productionPolicyScroll,choices.Count,28,(i,r)=>{if(SelectRow(r,servicePolicyKey==choices[i].Key)){servicePolicyKey=choices[i].Key;servicePolicyPopup=false;}Label(r,choices[i].Value);});
        }
    }
}
