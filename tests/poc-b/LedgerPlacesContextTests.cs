using System;using System.IO;using System.Linq;using System.Drawing;using System.Drawing.Imaging;using System.Collections.Generic;using TradeLedgerProbeB;
namespace BOfflineTests {internal static class LedgerPlacesContextTests {
 static LedgerObservation Place(string root,int id,string pf,string sector="4,5,6",string sys="1,2,3"){
  var c=LedgerContext.Create(Path.Combine(root,"context-save"),pf,"Observed star","SinglePlayer");c.Geography=new LedgerGeography{BodyKind="Moon",GroupName="Observed group",SystemCoordinates=sys,SectorCoordinates=sector};
  return new LedgerObservation{Context=c,Kind="place-observed",ObserverPlayer=1002,Utc="2026-11-17T10:00:00Z",DiscoveredObject=new LedgerObjectFact{Player=1002,NativeId=id,DisplayName="Colony",Template="R2BA_ColonistColonyLrg01",Evidence="local-poi-discovery",ContentIdentity="RE2 Build 46"}};
 }
 static LedgerObservation Market(LedgerObservation place,int npc,string price,int stock){return new LedgerObservation{Context=place.Context,Kind="market",ObserverPlayer=1002,Identity=new LedgerIdentity{NpcEntityId=npc,StructureEntityId=place.DiscoveredObject.NativeId,StructureName="Colony",OwnerConsistent=true},Offers=new List<LedgerOffer>{new LedgerOffer{ItemId=100,AuxiliaryRaw=0,BindingState="bound-current-delegate",NpcSellsSupported=true,NpcBuysSupported=false,Stock=stock,StoredSellUnit=price,Visible=new PublishedOffer{Name="Mining Equipment"}}}};}
 internal static void Run(Action<bool,string> check,string root){int count=0;Action<bool,string> ok=(v,s)=>{count++;check(v,"places-context: "+s);};
  var a=Place(root,1,"Moon A");var b=Place(root,2,"Moon A");var c=Place(root,3,"Moon B");var d=Place(root,4,"Moon C","7,8,9");var e=Place(root,5,"Far moon","1,2,3","9,9,9");
  var data=new LedgerViewData{WorldKey=a.Context.WorldKey,ManualPlayerId=1002,ManualStateReady=true,ArchiveStateReady=true};
  foreach(var p in new[]{a,b,c,d,e})data.DiscoveredObjects.Add(LedgerDiscoveredObjects.Merge(null,p));
  foreach(var m in new[]{Market(a,11,"781",213),Market(b,12,"800",0),Market(c,13,"900",30),Market(d,14,"1000",5),Market(e,15,"1100",1)})data.Markets.Add(m);
  var browser=data.PlaceBrowser;var index=new LedgerLocationIndex(data);var hierarchy=browser.Index.Hierarchy;
  var row=browser.Rows.Single(r=>r.Record.Latest.DiscoveredObject.NativeId==1);var scope=LedgerPlaceContext.Scope(browser,"all",row);
  ok(data.LocatedOffers(index,scope,true,"").Single().Price=="781","exact selected POI excludes same-name same-body instance");
  var body=LedgerPlaceContext.Scope(browser,"playfield:Moon A");var group=hierarchy.Resolve("playfield:Moon A").Parent;
  var sector=LedgerPlaceContext.Scope(browser,group.Key);var system=LedgerPlaceContext.Scope(browser,group.Parent.Key);var all=LedgerPlaceContext.Scope(browser,"all");
  ok(body.Kind==LedgerScopeKind.Body&&data.LocatedOffers(index,body,true,"").Count==2,"body includes both observed endpoints only");
  ok(sector.Kind==LedgerScopeKind.Sector&&data.LocatedOffers(index,sector,true,"").Count==3,"moon-only sector includes exact native sibling playfields");
  ok(system.Kind==LedgerScopeKind.System&&data.LocatedOffers(index,system,true,"").Count==4,"system excludes another same-name star by coordinates");
  ok(data.LocatedOffers(index,all,true,"").Count==5,"all aggregates known observations without supply allocation");
  ok(data.LocatedOffers(index,sector,true,"equipment").Select(o=>o.Price).Distinct().Count()==3,"multiple prices remain separate, case-insensitive item search");
  ok(data.LocatedOffers(index,body,true,"").Any(o=>o.Quantity=="0"),"observed zero retained, never hidden as unavailable");
  ok(data.LocatedOffers(index,scope,false,"").Count==0,"unsupported buying side not invented");
  ok(data.LocatedOffers(index,sector,true,"no such item").Count==0,"empty search is not fabricated supply");
  ok(LedgerPlaceContext.Scope(browser,"invalid-key")==null,"missing navigator identity fails closed rather than All fallback");
  var foreign=LedgerJson.Decode<LedgerObservation>(LedgerJson.Encode(data.Markets[0]));foreign.Context.WorldKey="other-save";ok(!index.Contains(all,foreign),"transient aggregate cannot cross save identity");
  var picker=new LedgerScopePicker(sector.Kind){Key=sector.Key,Context=sector};ok(ReferenceEquals(picker.Resolve(index),sector),"aggregate full-view scope survives picker resolve");
  var second=new LedgerScopePicker(scope.Kind){Key=scope.Key,Context=scope};LedgerUi.Swap(picker,second);ok(ReferenceEquals(picker.Resolve(index),scope)&&ReferenceEquals(second.Resolve(index),sector),"Comparison swap preserves full scoped payload on both sides");
  picker.Change(LedgerScopeKind.System);ok(picker.Context==null&&picker.Key==null,"explicit different level drops obsolete contextual override");
  foreach(var list in new[]{LedgerObjectOrganization.Unsorted,LedgerObjectOrganization.Hidden,LedgerObjectOrganization.Favorites}){
   var selected=new LedgerPlaceSelection();row.Record.Organization=list;var visible=browser.Query(list,search:"Colony");selected.Scope("current:"+list,visible);selected.Toggle(row.Record.Key);var snapshot=LedgerJson.Hash(LedgerJson.Encode(row.Record.Latest));
   var hidden=selected.Move(visible,LedgerPlaceContext.HideTarget(list));ok(hidden.Length==1&&hidden[0].Value==(list==LedgerObjectOrganization.Hidden?LedgerObjectOrganization.Unsorted:LedgerObjectOrganization.Hidden),"Hide/Unhide target "+list);
   var favorite=selected.Move(visible,LedgerPlaceContext.FavoriteTarget(list));ok(favorite.Length==1&&favorite[0].Value==(list==LedgerObjectOrganization.Favorites?LedgerObjectOrganization.Unsorted:LedgerObjectOrganization.Favorites),"Favorite/Unfavorite target "+list);
   row.Record.Organization=hidden[0].Value;ok(row.Record.Latest.Context.WorldKey==data.WorldKey&&snapshot==LedgerJson.Hash(LedgerJson.Encode(row.Record.Latest)),"membership changes no evidence, world or marker "+list);
   row.Record.Organization=list;selected.Scope("changed",new LedgerPlaceRow[0]);ok(selected.Selected.Count==0,"changed filter cannot move invisible members "+list);
  }
  row.Record.Organization=LedgerObjectOrganization.Unsorted;
  var npc=new LedgerObservation{Context=a.Context,Kind="dialogue-shown",ObserverPlayer=1002,Session="fixture",Sequence=1,Utc=a.Utc,Identity=new LedgerIdentity{NpcEntityId=20,StructureEntityId=1,StructureName="Colony",OwnerConsistent=true},Dialogue=new LedgerDialogue{BodyVerbatim="Bring me 5 Iron Ingots.",BodyFullyRevealed=true,TitleVerbatim="Observed NPC"}};
  data.DialoguePages.Add(npc); // Build a fresh projection after captured input, like an actual snapshot publication.
  var requests=new LedgerViewData{WorldKey=data.WorldKey,ManualPlayerId=1002,ManualStateReady=true,ArchiveStateReady=true};requests.DialoguePages.Add(npc);requests.Markets.AddRange(data.Markets);requests.DiscoveredObjects.AddRange(data.DiscoveredObjects);
  var f=new LedgerRequestFilters{Location=scope};var projection=new LedgerRequestListProjection();var cards=projection.Query(requests,f,null,0);
  ok(cards.Count==1,"Pending actual request in exact selected place");for(int i=0;i<50;i++)ok(ReferenceEquals(cards,projection.Query(requests,f,null,0)),"unchanged scoped request query reuse "+i);
  ok(projection.Builds==1,"scroll/repaint does not rebuild scoped request projection");
  var copy=f.Copy();f.Search="unmatched";ok(copy.Location==scope&&copy.Search=="","Back stores independent filters and immutable scope");
  ok(projection.Query(requests,f,null,0).Count==0,"context query search no stale rows");
  f.Search="";requests.ManualCompletions[cards.Single().CompletionKey]=true;f.Done=true;ok(projection.Query(requests,f,null,1).Count==1,"Done access retained without changing lifecycle");
  f.Done=false;ok(projection.Query(requests,f,null,1).Count==0,"Done not duplicated into Pending");
  f.ResetRestrictions();ok(f.Location==null,"Reset leaves exact contextual restriction explicitly");
  var handoff=new LedgerMenuHandoff();handoff.Request("save|world",100);ok(handoff.Pending("save|world",101)&&!handoff.Take("save|world",false,101),"paused menu click keeps intent without capture-ready permission");
  ok(handoff.Take("save|world",true,500)&&!handoff.Take("save|world",true,501),"menu opens once only after Running resumes");
  handoff.Request("save|world",1000);ok(!handoff.Take("other|world",true,1001)&&!handoff.Pending("save|world",1002),"save change cancels old menu intent");
  handoff.Request("save|world",2000);ok(!handoff.Take("save|world",true,4501),"timeout does not open later unrelated window");
  handoff.Request("save|world",5000);handoff.Cancel();ok(!handoff.Take("save|world",true,5001),"close/shutdown cancellation");
  string workspace=Environment.CurrentDirectory;Func<string,string> source=n=>File.ReadAllText(Path.Combine(workspace,"src/TradeLedgerProbeB",n));var ui=source("LedgerPlaceBrowserWindow.cs");var panel=source("LedgerPlaceContextWindow.cs");
  ok(!ui.Contains("Move selected")&&!ui.Contains("place-single")&&!panel.Contains("DrawRowBookmark"),"no redundant membership dropdown or Mark in Overview");
  ok(ui.Contains("LedgerPlaceContext.HideTarget(placeList)")&&ui.Contains("LedgerPlaceContext.FavoriteTarget(placeList)")&&!ui.Contains("placeList=placeLists[i]"),"membership commands preserve current list");
  ok(!ui.Contains("refreshRequested=true")&&ui.Contains("placeSignature=null"),"committed disposition invalidates local results without full DB reload");
  ok(!ui.Contains("place-traders\"")&&!ui.Contains("place-relation\"")&&ui.Contains("i<4")&&ui.Contains("placeRelationValues.Length"),"four trader and seven relation options are compact direct controls");
  ok(source("LedgerLocationWindow.cs").Contains("locationPicker,ref locationChoices,true"),"Locations uses actual four Comparison icons");
  ok(panel.Contains("placePanelGeography!=data.GeographyRevision")&&panel.Contains("placePanelOfferSearch!=query")&&!panel.Contains("LedgerViewData.Read"),"context data cached by snapshot/scope/geography/query, never IO in paint");
  ok(panel.Contains("RememberPage()")&&panel.Contains("Context=scope")&&source("LedgerJournalUiWindow.cs").Contains("PlaceDetail=placeDetailScroll"),"scoped full view and intuitive Back retain detail tab/query/scroll");
  ok(source("LedgerPrototypeWindow.cs").Contains("menuHandoff.Take(menuScope,ready&&entryMenu==null,now)")&&source("ProbeB.cs").Contains('"'+"Running"+'"'),"menu-only eligibility does not widen observer WorldReady");
  var picks=new LedgerPlaceSelection();picks.Scope("same",browser.Rows);
  string keyA=row.Record.Key,keyB=browser.Rows.First(r=>r.Record.Key!=keyA).Record.Key,cursor=null;
  if(picks.Activate(keyA,false,true))cursor=keyA;
  ok(cursor==keyA&&picks.Selected.Count==0,"ordinary row focuses details without checking");
  if(picks.Activate(keyB,false,true))cursor=keyB;
  ok(cursor==keyB&&picks.Selected.Count==0,"cursor A to B leaves empty checked set unchanged");
  ok(!picks.Activate(keyA,true,false)&&picks.Selected.SetEquals(new[]{keyA}),"checkbox toggles once without moving cursor");
  if(picks.Activate(keyA,false,true))cursor=keyA;if(picks.Activate(keyB,false,true))cursor=keyB;
  ok(cursor==keyB&&picks.Selected.SetEquals(new[]{keyA}),"browsing checked A then unchecked B leaves checks unchanged");
  ok(!picks.Activate(keyA,false,false)&&picks.Selected.SetEquals(new[]{keyA}),"mouse-up/consumed/action events never toggle");
  var moves=picks.Move(browser.Rows,LedgerObjectOrganization.Hidden,cursor);
  ok(moves.Length==1&&moves[0].Key==keyA,"checked A plus cursor B moves only A");
  ok(picks.Move(browser.Rows,LedgerObjectOrganization.Favorites,cursor).Single().Key==keyA,"Favorite uses same checked-only target contract");
  picks.Activate(keyB,true,false);ok(picks.Selected.SetEquals(new[]{keyA,keyB}),"second checkbox adds without clearing A");
  ok(picks.Move(browser.Rows,LedgerObjectOrganization.Hidden,cursor).Length==2,"explicit multiple checks supply bulk targets");
  picks.Activate(keyA,true,true);ok(picks.Selected.SetEquals(new[]{keyB}),"checkbox owns a gesture even if both hit notifications arrive");
  picks.Activate(keyB,true,false);ok(picks.Selected.Count==0,"checked cell toggles off once");
  ok(picks.Move(browser.Rows,LedgerObjectOrganization.Hidden,cursor).Single().Key==keyB,"empty checked set acts on cursor");
  ok(picks.Move(browser.Rows,LedgerObjectOrganization.Hidden,"not visible").Length==0,"invisible cursor never acts outside filtered scope");
  ok(picks.Move(browser.Rows,LedgerObjectOrganization.Hidden).Length==0,"no cursor and no checks has no target");
  picks.Selected.Add("hidden or filtered stale key");
  ok(picks.Move(browser.Rows,LedgerObjectOrganization.Hidden,cursor).Length==0,"nonempty invisible checks never fall back to an unchecked cursor");
  picks.Selected.Clear();picks.Toggle(keyA);picks.Scope("same",browser.Rows);
  ok(picks.Selected.SetEquals(new[]{keyA}),"same-filter refresh retains checked identity");
  picks.Scope("same",browser.Rows.Where(r=>r.Record.Key!=keyA));ok(picks.Selected.Count==0,"same-filter refresh drops a vanished checked member");
  picks.Toggle(keyA);picks.Scope("different search",browser.Rows);ok(picks.Selected.Count==0,"changed search clears old check set");
  picks.Toggle(keyA);picks.Scope("Hidden",browser.Rows.Where(r=>r.Record.Organization==LedgerObjectOrganization.Hidden));ok(picks.Selected.Count==0,"switching list cannot retain invisible checked targets");
  picks.Scope("same",browser.Rows);picks.Toggle(keyA);cursor=keyB;
  var before=row.Record.Organization;var untouched=browser.Rows.First(r=>r.Record.Key==keyB).Record.Organization;
  moves=picks.Move(browser.Rows,LedgerObjectOrganization.Hidden,cursor);row.Record.Organization=moves.Single().Value;
  ok(browser.Query(LedgerObjectOrganization.Hidden).Contains(row)&&browser.Rows.First(r=>r.Record.Key==keyB).Record.Organization==untouched,"Hide changes checked A only and leaves cursor B intact");
  row.Record.Organization=before;ok(browser.Query(before).Contains(row)&&picks.Selected.SetEquals(new[]{keyA}),"Undo restores exact former disposition without changing checked identity or evidence");
  picks.Selected.Clear();moves=picks.Move(browser.Rows,LedgerObjectOrganization.Favorites,keyA);row.Record.Organization=moves.Single().Value;
  ok(row.Record.Organization==LedgerObjectOrganization.Favorites,"cursor fallback Favorite changes one row");
  ok(picks.Move(browser.Rows,LedgerObjectOrganization.Favorites,keyA).Length==0,"already-in-target cursor action is a no-op");
  row.Record.Organization=before;ok(browser.Query(before).Contains(row),"cursor fallback Undo restores original membership");
  const string native="native exact observed PF 5D4B;system5D49/sector5D4A;fixture";
  Func<string,LedgerGeography> geo=sectorText=>new LedgerGeography{Source=native,SystemCoordinates="0,0,0",SectorCoordinates=sectorText};
  var current=LedgerContext.Create(Path.Combine(root,"distance-refresh"),"Home","Observed star","SinglePlayer");
  var prior=new LedgerPlayerPoint{World=current.WorldKey,SaveScope=current.LocalSaveKey??current.SavePath,Playfield="Home",Geography=geo("0,0,0"),X=0,Y=0,Z=0};
  var destination=LedgerContext.Create(current.SavePath,"Destination","Observed star","SinglePlayer");destination.Geography=geo("300,400,0");
  var offer=new LedgerObservation{Context=destination,Identity=new LedgerIdentity{NpcEntityId=44}};var distances=new LedgerViewData{WorldKey=current.WorldKey};distances.Markets.Add(offer);distances.PrepareDistances();
  ok(distances.DistanceFor(offer,prior)=="50.0 AU","trusted refresh fixture begins with native 3-4-5 distance");int reads=0;
  var next=LedgerDistanceRefresh.Replace(prior,current,1002,1002,()=>{reads++;for(int frame=0;frame<6;frame++)ok(distances.DistanceFor(offer,prior)=="50.0 AU","pending read retains last trustworthy result "+frame);return geo("100,0,0");});
  ok(reads==1&&!ReferenceEquals(next,prior)&&prior.Geography.SectorCoordinates=="0,0,0"&&distances.DistanceFor(offer,next)=="44.7 AU","one settled read atomically replaces immutable point and updates geometry");
  for(int i=0;i<20;i++)ok(distances.DistanceFor(offer,next)=="44.7 AU"&&reads==1,"repaint uses settled data without new native read "+i);
  var missing=LedgerDistanceRefresh.Replace(next,current,1002,1002,()=>null);ok(missing.Geography==null&&distances.DistanceFor(offer,missing)=="unknown","completed unavailable geography invalidates old distance honestly");
  var failedRead=LedgerDistanceRefresh.Replace(next,current,1002,1002,()=>{throw new IOException("fixture");});ok(failedRead.Geography==null,"failed read never masks genuine Unknown with stale coordinates");
  var restored=LedgerDistanceRefresh.Replace(missing,current,1002,1002,()=>geo("0,0,0"));ok(distances.DistanceFor(offer,restored)=="50.0 AU","settled supported observation restores distance after Unknown");
  foreach(string change in new[]{"world","save","playfield","unrecordable","player","unknown-player"}){
   var context=LedgerJson.Decode<LedgerContext>(LedgerJson.Encode(current));if(change=="world")context.WorldKey="foreign";if(change=="save")context.LocalSaveKey="foreign physical save";if(change=="playfield")context.Playfield="Other";if(change=="unrecordable")context.Recordable=false;
   int called=0;var rejected=LedgerDistanceRefresh.Replace(next,context,1002,change=="player"?1003:change=="unknown-player"?(int?)null:1002,()=>{called++;return geo("0,0,0");});ok(rejected==null&&called==0,"identity/scope loss invalidates before read: "+change);
  }
  var during=LedgerJson.Decode<LedgerContext>(LedgerJson.Encode(current));ok(LedgerDistanceRefresh.Replace(next,during,1002,1002,()=>{during.Playfield="Changed during read";return geo("0,0,0");})==null,"scope revalidated after coordinate read");
  var unavailableTarget=new LedgerObservation{Context=LedgerContext.Create(current.SavePath,"Unresolved target","Observed star","SinglePlayer"),Identity=new LedgerIdentity{NpcEntityId=45}};var unknownDistances=new LedgerViewData{WorldKey=current.WorldKey};unknownDistances.Markets.Add(unavailableTarget);unknownDistances.PrepareDistances();ok(unknownDistances.DistanceFor(unavailableTarget,restored)=="unknown","target/evidence change does not reuse previous destination distance");
  ok(!source("LedgerLocationWindow.cs").Contains("playerPoint.Geography=null")&&source("LedgerLocationWindow.cs").Contains("LedgerDistanceRefresh.Replace"),"actual snapshot publication uses atomic refresh instead of artificial Unknown frame");
  ok(ui.Contains("placeSelection.Activate(key,checkboxPressed,rowPressed)")&&ui.Contains("SelectRow(new Rect(r.x,r.y,32,r.height),selected)")&&ui.Contains("r.x+32,r.y,r.width-32,r.height),placeFocus==key")&&ui.Contains("Event.current.type==EventType.Repaint)GUI.skin.toggle.Draw")&&!ui.Contains("GUI.Toggle("),"full checkbox cell consumes one gesture; row body only focuses; checkbox paint has no input handler");
  ok(ui.Contains("placeSelection.Move(placeRows,LedgerPlaceContext.HideTarget(placeList),placeFocus)")&&ui.Contains("placeSelection.Move(placeRows,LedgerPlaceContext.FavoriteTarget(placeList),placeFocus)")&&ui.Contains("checkedOnly||cursorVisible"),"both enabled actions share checked-first/cursor-fallback routing");
  ok(ui.Contains("checkedOnly?placeSelection.Selected.Count+")&&ui.Contains("Cursor row")&&ui.Contains("No target"),"action target mode remains visible even with operation status");
  ok(source("LedgerPrototypeWindow.cs").Contains("Event.current.Use();return true;"),"row hit helper consumes left mouse-down before drawing controls");
  ok(LedgerPlaceBrowserLayout.MarketRow==32&&panel.Contains("placePanelOffers.Count,LedgerPlaceBrowserLayout.MarketRow")&&!panel.Contains("LedgerFreshness.Badge("),"contextual Market removes redundant badge and halves virtual row height");
  ok(panel.Contains("OfferBackdrop(r,offer.Market)")&&panel.Contains("LedgerFreshness.Hint(offer.Market")&&panel.Contains("r.Contains(Event.current.mousePosition)"),"freshness background and full-row hover retained");
  var separate=data.LocatedOffers(index,body,true,"");ok(separate.Count==2&&separate.All(o=>o.Offer.ItemId==100)&&separate.Select(o=>o.Price).Distinct().Count()==2,"same item remains two distinct offers/prices in compact rows");
  foreach(int height in new[]{160,320,640})foreach(float scroll in new[]{0f,31f,32f,5000f}){int first=LedgerLayout.FirstRow(scroll,500,LedgerPlaceBrowserLayout.MarketRow),end=LedgerLayout.EndRow(scroll,height,500,LedgerPlaceBrowserLayout.MarketRow);ok(first>=0&&end<=500&&end-first<=height/32+3,"compact virtualization stays viewport-bounded "+height+"/"+scroll);}
  string dir=Path.Combine(root,"places-context");Directory.CreateDirectory(dir);
  foreach(int width in new[]{1280,1920,2560,3840}){var l=LedgerLayout.For(width,width*9/16);var layout=new LedgerPlaceBrowserLayout(l.Width-32,l.Height-112);ok(layout.List>=300&&layout.Detail>=320,"three panes bounded "+width);foreach(int tab in new[]{0,1,2})Render(Path.Combine(dir,"places-"+width+"-"+tab+".png"),width,tab);}
  Console.WriteLine("Places contextual checks passed: "+count+"; "+dir);File.WriteAllText(Path.Combine(dir,"checks.txt"),count+" checks; synthetic/model/source wiring/GDI; not Unity");
 }
 static void Render(string file,int width,int tab){var l=LedgerLayout.For(width,width*9/16);var bounds=new LedgerPlaceBrowserLayout(l.Width-32,l.Height-112);using(var bmp=new Bitmap(width,width*9/16))using(var g=Graphics.FromImage(bmp))using(var font=new Font("Segoe UI",16,GraphicsUnit.Pixel))using(var bold=new Font("Segoe UI",16,FontStyle.Bold,GraphicsUnit.Pixel))using(var small=new Font("Segoe UI",13,GraphicsUnit.Pixel)){
  g.Clear(Color.FromArgb(12,34,51));g.ScaleTransform(l.Scale,l.Scale);Action<float,float,float,string,bool> button=(bx,by,bw,t,active)=>{g.FillRectangle(active?Brushes.DarkSlateGray:Brushes.Transparent,bx,by,bw,28);g.DrawRectangle(active?Pens.Cyan:Pens.SteelBlue,bx,by,bw,28);g.DrawString(t,font,Brushes.White,bx+5,by+3);};
  g.DrawString("Trade Ledger    Market   Requests   Production   Destinations   Locations   Comparison   Item Browser   Places",bold,Brushes.White,16,16);
  float x=16+bounds.Nav+12;g.DrawString("Locations",bold,Brushes.White,20,96);g.DrawString("All places\n  Deltalan\n    Deltalan IV group\n      Moon A\n      Moon B",font,Brushes.LightGray,new RectangleF(20,132,bounds.Nav-20,250));
  for(int i=0;i<3;i++)button(x+i*112,96,106,new[]{"Favorites","Unsorted","Hidden"}[i],i==1);button(x+342,96,bounds.Main-414,"Search discovered places",false);button(x+bounds.Main-66,96,66,"Reset",false);
  float fw=Math.Max(140,bounds.Main-450);button(x,130,fw,"All factions  \u25BE",false);Color[] colors={Color.White,Color.Red,Color.Orange,Color.Yellow,Color.LimeGreen,Color.Gray,Color.SlateGray};for(int i=0;i<7;i++){float px=x+fw+10+i*28;button(px,130,26,i==0?"*":i==6?"/":"",i==2);if(i>0&&i<6)using(var brush=new SolidBrush(colors[i]))g.FillEllipse(brush,px+9,140,8,8);}g.DrawString("Traders",small,Brushes.White,x+fw+214,136);for(int i=0;i<4;i++)button(x+fw+274+i*28,130,26,new[]{"*","+","-","?"}[i],i==0);
  button(x,164,100,"Select all",false);button(x+106,164,62,"Clear",false);button(x+176,164,112,"Hide checked",false);button(x+296,164,112,"Favorite",false);button(x+416,164,64,"Undo",false);button(x+bounds.Main-80,164,80,"Visit",false);
  g.DrawString("2 checked",small,Brushes.LightBlue,x+490,171);
  g.DrawString("Place                         Distance  Max traders",small,Brushes.LightBlue,x+32,204);for(int i=0;i<18;i++){float y=232+i*48;if(y>l.Height-35)break;if(i==2)g.FillRectangle(Brushes.DarkSlateGray,x+32,y,bounds.List-32,48);g.DrawRectangle(Pens.SlateGray,x+5,y+14,18,18);if(i<2){g.DrawLine(Pens.Cyan,x+8,y+23,x+12,y+28);g.DrawLine(Pens.Cyan,x+12,y+28,x+21,y+17);}g.DrawString(i==0?"Bank of Farr B":"Colony "+(char)('A'+i%4),bold,Brushes.LightCyan,x+38,y+1);g.DrawString("Moon A | Observed faction",small,Brushes.Gray,x+38,y+26);g.FillEllipse(Brushes.Orange,x+bounds.List-197,y+19,8,8);g.DrawString("1.6 km     5",small,Brushes.White,x+bounds.List-163,y+10);}g.FillRectangle(Brushes.SteelBlue,x+bounds.List-12,234,10,bounds.Height-40);g.FillRectangle(Brushes.LightBlue,x+bounds.List-12,250,10,70);
  float dx=x+bounds.List+12,dw=bounds.Detail;g.DrawRectangle(Pens.SteelBlue,dx,200,dw,bounds.Height);g.DrawString("Colony C",bold,Brushes.Gold,dx+8,210);for(int i=0;i<3;i++)button(dx+8+i*(dw-16)/3,244,(dw-16)/3-4,new[]{"Overview","Market","Requests"}[i],i==tab);button(dx+8,278,(dw-16)*.48f,tab==2?"Full Requests":"Full Market",false);button(dx+8+(dw-16)*.5f,278,(dw-16)*.5f,"Compare \u25BE",false);
  if(tab==0)g.DrawString("Deltalan I / Deltalan\n\nBrotherhood of Farr\n\nUp to 5 traders\n\n2 observed traders\n\n1 pending / 0 done / 3 conversations",font,Brushes.White,new RectangleF(dx+8,320,dw-32,400));
  else if(tab==1){button(dx+8,312,dw-16,"Search observed items",false);g.DrawString("Item                       Qty       Price",small,Brushes.LightBlue,dx+8,350);g.SetClip(new RectangleF(dx+8,378,dw-16,Math.Max(30,l.Height-394)));for(int i=0;i<28;i++){float y=380+i*LedgerPlaceBrowserLayout.MarketRow;var name=i<2?"100MW Laser Rifle Charge":i%3==0?"Mining Equipment with a long observed item name":"Luxury Clothing";g.DrawString((i%2==0?"Buy: ":"Sell: ")+name,small,i%3==0?Brushes.Khaki:Brushes.White,new RectangleF(dx+12,y,(dw-36)*.56f-8,26));g.DrawString((i%2==0?"213":"38"),small,Brushes.White,dx+8+(dw-36)*.56f,y);g.DrawString((i%2==0?"781":"800"),small,Brushes.White,dx+8+(dw-36)*.79f,y);}g.ResetClip();}
  else{button(dx+8,312,80,"Pending",true);button(dx+94,312,80,"Done",false);button(dx+8,346,dw-16,"Search requests",false);g.DrawString("Gold Standard Employee\n5 Mining Equipment, 15 Computers\nPartial   24,000 cr\n\nSupervisor\n25 Civilian Furniture\nReady    15,000 cr",font,Brushes.White,new RectangleF(dx+12,392,dw-35,360));}bmp.Save(file,ImageFormat.Png);
 }}
}}
