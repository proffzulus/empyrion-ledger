using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Eleon.Modding;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace TradeLedgerProbeB
{
    // Public local modal provides the game's cursor/input lease; IMGUI supplies only its owned visual surface.
    // No new Harmony hooks, native tab mutation, raw cursor setters or Rewired context stack operations.
    internal sealed class LedgerModalLease
    {
        private readonly object manager;
        private readonly FieldInfo stack, registry, root, controller, configField, handlerField, canvasField;
        private readonly MethodInfo close, typing,debugTyping;
        private readonly FieldInfo inputBlocked,nativeWorld,debugPanel,entryConfig,unclosable;
        private string key;
        private object windowRoot;
        private CanvasGroup ownedCanvas;
        private DialogConfig ownedConfig;
        private DialogActionHandler ownedHandler;
        internal LedgerModalLease(Assembly game)
        {
            LedgerNativeContracts.Require(game,"journal");
            var module = game.ManifestModule;
            var get = (MethodInfo)LedgerNativeContracts.Method(module,0x0600cdd0);
            close = (MethodInfo)LedgerNativeContracts.Method(module,0x0600cde5); typing = (MethodInfo)LedgerNativeContracts.Method(module,0x0600ae60);
            inputBlocked=LedgerNativeContracts.Field(module,0x0400a126);nativeWorld=LedgerNativeContracts.Field(module,0x040064e6);debugPanel=LedgerNativeContracts.Field(module,0x040064ff);debugTyping=(MethodInfo)LedgerNativeContracts.Method(module,0x06006f09);
            if(!inputBlocked.IsStatic||inputBlocked.FieldType!=typeof(bool)||!nativeWorld.IsStatic||debugTyping.ReturnType!=typeof(bool)||debugTyping.GetParameters().Length!=0)throw new InvalidDataException("UI typing contract mismatch");
            stack = LedgerNativeContracts.Field(module,0x0400c181); registry = LedgerNativeContracts.Field(module,0x0400c180); root = LedgerNativeContracts.Field(module,0x0400c185);
            canvasField=LedgerNativeContracts.Field(module,0x0400c189);
            entryConfig=LedgerNativeContracts.Field(module,0x0400c186);unclosable=LedgerNativeContracts.Field(module,0x0400c137);
            controller = LedgerNativeContracts.Field(module,0x0400c184); configField = LedgerNativeContracts.Field(module,0x0400a57e); handlerField = LedgerNativeContracts.Field(module,0x0400a57f);
            if (get.ReturnType.FullName != "Assembly-CSharp.RegistryTreeConnectionToken" || !get.IsStatic || close.IsStatic || close.GetParameters().Length != 0 || close.ReturnType != typeof(bool) || !typing.IsStatic || typing.ReturnType != typeof(bool) || stack.FieldType != typeof(Stack<string>)) throw new InvalidDataException("UI member signature mismatch");
            manager = get.Invoke(null, null);
            if (manager == null) throw new InvalidOperationException("Native UI manager unavailable");
        }
        private Stack<string> Stack { get { return (Stack<string>)stack.GetValue(manager); } }
        internal bool Empty { get { var visual=windowRoot as GameObject;return LedgerWindowPolicy.CanReopen(Stack.Count==0,OwnedEntry&&visual!=null&&visual.activeInHierarchy); } }
        internal bool Typing
        {
            get
            {
                var es=EventSystem.current;
                var ngui=UIInput.selection;
                var selected=es==null?null:es.currentSelectedGameObject;
                var input=selected==null?null:selected.GetComponent<InputField>();
                var tmp=selected==null?null:selected.GetComponent<TMP_InputField>();
                object world=nativeWorld.GetValue(null),debug=world==null?null:debugPanel.GetValue(world);
                // A selected but disabled/blurred UGUI field is not live typing. Preserve native global/debug blocks.
                return LedgerJournalLifecycle.IsTyping(es!=null,(bool)inputBlocked.GetValue(null),ngui!=null&&ngui.enabled&&ngui.gameObject.activeInHierarchy,
                    input!=null&&input.isActiveAndEnabled&&input.isFocused,tmp!=null&&tmp.isActiveAndEnabled&&tmp.isFocused,debug!=null&&(bool)debugTyping.Invoke(debug,null));
            }
        }
        private bool OwnedEntry
        {
            get
            {
                if(key==null)return false;var map=registry.GetValue(manager) as IDictionary;
                if(map==null||!map.Contains(key))return false;
                var native=controller.GetValue(map[key]);
                return native!=null&&native.GetType().FullName=="Assembly-CSharp.StreamDesignerConnectionToken"&&LedgerWindowPolicy.OwnedModal(windowRoot,Root(key),ownedConfig,configField.GetValue(native),ownedHandler,handlerField.GetValue(native));
            }
        }
        internal bool Present { get { return key!=null&&Stack.Contains(key)&&OwnedEntry; } }
        internal bool Top { get { return Present && Stack.Count > 0 && Stack.Peek() == key; } }
        internal bool ForeignPresent {get{return Stack.Count>0&&!Top;}}
        internal GameObject SwitchableNative
        {
            get{
                if(Stack.Count!=1||Present||!LedgerWindowSwitch.Switchable(Stack.Peek()))return null;
                var map=registry.GetValue(manager) as IDictionary;string name=Stack.Peek();if(map==null||!map.Contains(name))return null;
                object entry=map[name],native=controller.GetValue(entry);var visual=root.GetValue(entry) as GameObject;
                if(native==null||native.GetType().FullName=="Assembly-CSharp.StreamDesignerConnectionToken"||visual==null||!visual.activeInHierarchy||(bool)unclosable.GetValue(entryConfig.GetValue(entry)))return null;
                return visual;
            }
        }
        internal bool CloseSwitchable(GameObject expected){return expected!=null&&ReferenceEquals(SwitchableNative,expected)&&(bool)close.Invoke(manager,null);}
        internal GameObject EntryMenu
        {
            get
            {
                // Exact registered top only. Never close another panel or stack underneath it.
                if(Stack.Count!=1||Stack.Peek()!="InGameMenu")return null;
                var menu=Root("InGameMenu") as GameObject;
                return menu!=null&&menu.activeInHierarchy?menu:null;
            }
        }
        internal bool CloseEntryMenu(GameObject expected)
        {if(expected==null||!ReferenceEquals(EntryMenu,expected))return false;return (bool)close.Invoke(manager,null);}
        private object Root(string name)
        { var map = (IDictionary)registry.GetValue(manager); return map.Contains(name) ? root.GetValue(map[name]) : null; }
        internal bool Adopt(DialogConfig config, DialogActionHandler handler)
        {
            if (Stack.Count != 1) return false;
            key = Stack.Peek(); windowRoot = Root(key);
            ownedConfig = config; ownedHandler = handler;
            if(!(windowRoot is GameObject) || !Present)return false;
            var entry=((IDictionary)registry.GetValue(manager))[key];
            if(canvasField.FieldType!=typeof(CanvasGroup))throw new InvalidDataException("Modal canvas signature mismatch");
            ownedCanvas=canvasField.GetValue(entry) as CanvasGroup;
            if(ownedCanvas==null)throw new InvalidDataException("Owned native modal canvas missing");
            Conceal();return true;
        }
        // CC3C fades the native CanvasGroup over 0.25s after popping the stack. It can
        // also refuse a close while BF36 is set. Never treat a close request as completion.
        internal void Conceal() { if(ownedCanvas!=null&&OwnedEntry)ownedCanvas.alpha=0; }
        internal void CloseTop() { if (Top) close.Invoke(manager, null); Conceal(); }
    }

    internal interface ILedgerWindow { void Poll(LedgerContext context, bool ready); void Stop(); }

    public sealed partial class LedgerPrototypeWindow : MonoBehaviour, ILedgerWindow
    {
        internal IGui NativeGui;
        internal LedgerModalLease Lease;
        internal string DatabasePath;
        internal Action<string, string> Log;
        internal readonly KeyCode ToggleKey = KeyCode.RightBracket;
        internal IApplication Application;
        internal LedgerNativeCatalog NativeCatalog;
        internal LedgerNativeBookmarks NativeBookmarks;
        private LedgerBookmarkPlan bookmarkRequested;
        private LedgerContext currentContext;
        private LedgerCatalog catalog;
        private LedgerPlayerPoint playerPoint;
        private string selectedOfferKey,iconKey;
        private readonly HashSet<string> collapsedGroups=new HashSet<string>();
        private string iconHoverName;
        private LedgerIcon selectedIcon;
        private readonly LedgerJournalLifecycle lifecycle=new LedgerJournalLifecycle();
        private readonly LedgerWindowSwitch windowSwitch=new LedgerWindowSwitch();
        private LedgerNativeWindowSwitch nativeSwitch;
        private static readonly KeyCode[] switchKeys={KeyCode.F1,KeyCode.F2,KeyCode.F3,KeyCode.F4};
        private readonly LedgerMenuEntry menuEntry=new LedgerMenuEntry();
        private readonly LedgerMenuHandoff menuHandoff=new LedgerMenuHandoff();
        private GameObject retiringEntryMenu;
        private readonly LedgerOwnedGuiFocus guiFocus=new LedgerOwnedGuiFocus();
        private bool escapePending {get{return lifecycle.EscapePending;}set{lifecycle.EscapePending=value;}}
        private bool paused,focusLostPending,ownTyping;
        private long recoverAt;
        private int modalSerial;
        private LedgerPointerIsolation pointerIsolation;
        private readonly LedgerOfferSortCache marketSellCache=new LedgerOfferSortCache(),marketBuyCache=new LedgerOfferSortCache();
        private readonly LedgerHeaderSort marketSellSort=new LedgerHeaderSort(),marketBuySort=new LedgerHeaderSort();
        private static bool PointerIdle {get{return !Input.GetMouseButton(0)&&!Input.GetMouseButton(1)&&!Input.GetMouseButton(2)&&!Input.GetMouseButtonUp(0)&&!Input.GetMouseButtonUp(1)&&!Input.GetMouseButtonUp(2)&&Input.mouseScrollDelta.sqrMagnitude==0;}}
        private void SyncPointer()
        {
            bool focused=UnityEngine.Application.isFocused&&!paused&&!focusLostPending;
            if(open&&focused&&Lease.Top&&pointerIsolation==null)pointerIsolation=new LedgerPointerIsolation(new LedgerNativePointerBackend());
            if(pointerIsolation!=null){bool prior=pointerIsolation.Active;pointerIsolation.Sync(open&&focused&&Lease.Top,focused&&(!open||!Lease.Present),PointerIdle);if(prior!=pointerIsolation.Active)Log("LedgerPointerIsolation","active="+pointerIsolation.Active+" cameras="+pointerIsolation.CapturedCameras+" nativeNGUI=true ownIMGUI=true");}
        }
        private void ReleasePointer(){if(pointerIsolation!=null)pointerIsolation.Release();}
        private long positionAt;
        private string world, search = "", selectedKey;
        private bool open {get{return lifecycle.Open;}set{lifecycle.Open=value;}}
        private bool closeRequested {get{return lifecycle.CloseRequested;}set{lifecycle.CloseRequested=value;}}
        private bool failed, refreshRequested,readRetry;
        private readonly LedgerViewReadWork viewRead=new LedgerViewReadWork();
        private readonly LedgerViewReader viewReader=new LedgerViewReader();private bool forceRead;private string viewReadError;
        private bool loading {get{lock(resultGate)return viewRead.Busy;}}
        private int generation;
        private LedgerTabPreference tabPreference=new LedgerTabPreference();
        private int view {get{return tabPreference.Index;}set{tabPreference.Select(value);}}
        internal void RestoreTabPreference()
        {tabPreference=new LedgerTabPreference(()=>PlayerPrefs.GetString(LedgerTabPreference.StorageKey,LedgerTabPreference.DefaultId),id=>{PlayerPrefs.SetString(LedgerTabPreference.StorageKey,id);PlayerPrefs.Save();});}

        private Vector2 itemsScroll, sellersScroll, buyersScroll, requestsScroll;
        private LedgerViewData data = new LedgerViewData();
        private readonly object resultGate = new object();
        private LedgerViewData pending;
        private string pendingError;
        private GUIStyle text, heading, small, row, itemButton, selectedRow, rich, richHeading, pairText;
        private GUISkin ledgerSkin;private GUIStyle selectedTab;private readonly List<Texture2D> ownedSkinTextures=new List<Texture2D>();
        private LedgerLayout layout;
        private Texture2D background;
        private Texture2D selectionBackground;


        public void Poll(LedgerContext context, bool ready)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.UiPoll))
            {
            long now=(long)(Time.realtimeSinceStartup*1000);
            if(failed){if(now<recoverAt)return;failed=false;Log("LedgerViewRecovery","ready-for-new-shortcut=true autoReopen=false");}
            currentContext=context;
            try
            {
                PollOfferClock(now, ready);
                var entryMenu=Lease.EntryMenu;
                bool menuScoped=context!=null&&context.Recordable&&ManualStore!=null&&(entryMenu!=null||menuHandoff.Pending(context.LocalSaveKey+"|"+context.WorldKey,now));
                string current = (ready||menuScoped) && context != null && context.Recordable ? context.WorldKey : null;
                if (current != world)
                {
                    closeRequested = true; world = current;detailsKey=null;catalogSelection=null;SetManualPlayer(null);InvalidateViewRead(); data = new LedgerViewData(); selectedKey = null;selectedOfferKey=null;playerPoint=null;bookmarkRequested=null;ResetLocationViews();ResetPlaceBrowser();ResetItemBrowser();catalog=null;
                    if(current!=null && NativeCatalog!=null)
                    {
                        try{catalog=NativeCatalog.Read();Log("LedgerCatalogReady","definitions="+catalog.Items.Count+" source="+catalog.Source);}
                        catch(Exception e){catalog=null;Log("LedgerCatalogUnavailable",e.ToString()+" requests-use-raw-text;groups-uncategorized");}
                    }
                }
                Lease.Conceal();
                string switchScope=current==null?null:context.LocalSaveKey+"|"+current;
                bool switchFocus=UnityEngine.Application.isFocused&&!paused&&!focusLostPending&&ready&&current!=null;
                bool switchTyping=Lease.Typing||open&&ownTyping;
                if(open&&Lease.Top&&switchFocus&&!switchTyping&&!Input.GetKey(KeyCode.LeftControl)&&!Input.GetKey(KeyCode.RightControl)&&!Input.GetKey(KeyCode.LeftAlt)&&!Input.GetKey(KeyCode.RightAlt)){
                    foreach(var k in switchKeys)if(Input.GetKeyDown(k))windowSwitch.Request((int)k,switchScope,now);
                }
                int handoff=windowSwitch.Step(now,switchScope,switchFocus,switchTyping,open,Lease.Present,Lease.Top,Lease.Empty,windowSwitch.Control!=0&&Input.GetKey((KeyCode)LedgerWindowSwitch.KeyForControl(windowSwitch.Control)),Lease.ForeignPresent);
                if(handoff==1)closeRequested=true;
                if(handoff==2){int control=windowSwitch.Control;windowSwitch.Cancel();ReleasePointer();nativeSwitch=nativeSwitch??new LedgerNativeWindowSwitch(Application.GetType().Assembly);if(!nativeSwitch.Open(control))Log("LedgerWindowSwitchUnavailable","registered UI action unavailable; control="+control);}
                string menuScope=current==null?null:context.LocalSaveKey+"|"+current;
                bool entryEligible=!open&&!failed&&menuScoped&&current!=null&&UnityEngine.Application.isFocused&&!lifecycle.Retiring;
                if(menuEntry.Clicked)
                {
                    menuEntry.Clicked=false;
                    if(entryEligible&&entryMenu!=null&&Lease.CloseEntryMenu(entryMenu))
                    {retiringEntryMenu=entryMenu;menuHandoff.Request(menuScope,now);Log("LedgerEntryClicked","location=in-game-Escape-menu;deferred-until-native-close-and-running=true");}
                    menuEntry.Hide();
                }
                else menuEntry.Sync(entryMenu,entryEligible);
                if(menuHandoff.Take(menuScope,ready&&entryMenu==null,now))lifecycle.RequestOpen(now);
                var action=lifecycle.Step(new LedgerJournalFrame{Now=now,Focused=UnityEngine.Application.isFocused&&!paused,Ready=ready,Scoped=current!=null,
                    Present=Lease.Present,Top=Lease.Top,Empty=Lease.Empty&&(retiringEntryMenu==null||!retiringEntryMenu.activeInHierarchy),WorldVisible=NativeGui!=null&&NativeGui.IsWorldVisible,
                    Typing=Lease.Typing||open&&ownTyping,ShortcutHeld=Input.GetKey(ToggleKey),EscapeHeld=Input.GetKey(KeyCode.Escape),SwitchableNative=Lease.SwitchableNative!=null,ForeignNative=Lease.ForeignPresent});
                focusLostPending=false;
                if(action.OpenRejected!=null)Log("LedgerOpenBlocked","gate="+action.OpenRejected+" scope="+(context==null?"absent":context.ScopeState)+" reading="+loading+" generation="+generation+" ready="+ready+" present="+Lease.Present+" top="+Lease.Top+" retiring="+lifecycle.Retiring);
                if(action.ClosedReason!=null)FinishClose(action.ClosedReason,action.Interrupted);
                if(action.CloseForeign){var native=Lease.SwitchableNative;if(Lease.CloseSwitchable(native))retiringEntryMenu=native;}
                if(action.CloseNative)
                {
                    Lease.CloseTop();
                    if(!Lease.Present&&open)FinishClose("requested");
                }
                if(action.OpenNative)Open();
                if(playerPoint!=null&&!LedgerBrowse.SamePositionScope(playerPoint,context)){playerPoint=null;positionAt=0;}
                long positionNow=DateTime.UtcNow.Ticks/TimeSpan.TicksPerMillisecond;
                if(open && ready && current!=null && (positionNow-positionAt>=500||positionNow<positionAt))
                {
                    positionAt=positionNow;
                    try{var p=Application==null?null:Application.LocalPlayer;if(p!=null){SetManualPlayer(p.Id>0?(int?)p.Id:null);var v=p.Position;
                        var geo=LedgerBrowse.SamePositionScope(playerPoint,context)?playerPoint.Geography:null;
                        if(geo==null&&nativeGeography!=null)geo=nativeGeography.Read(context,Application);
                        playerPoint=new LedgerPlayerPoint{World=current,Playfield=context.Playfield,SaveScope=context.LocalSaveKey??context.SavePath,X=v.x,Y=v.y,Z=v.z,Geography=geo};}else{playerPoint=null;SetManualPlayer(null);}}
                    catch(Exception){playerPoint=null;SetManualPlayer(null);}
                }
                PollGraphDrag(open&&ready&&view==5&&Lease.Top);
                PollItemBrowser(ready,positionNow);
                lock (resultGate)
                {
                    if(readRetry){readRetry=false;if(open)refreshRequested=true;}
                    if (pending != null) { var projection=pending;pending=null;
                        if(pendingManualRevision!=manualRevision)refreshRequested=true;
                        else if(ReferenceEquals(data,projection))viewReadError=null;
                        else{data=projection;viewReadError=null;iconKey=null;detailsKey=null;ApplyManualPending();
                            if(data.ManualReadError!=null||data.TrackingReadError!=null){manualError="Request state couldn't be read.";Log("LedgerManualAnnotationError",data.ManualReadError??data.TrackingReadError);}OnLocationsLoaded();Log("LedgerViewLoaded",data.Status+" requests="+data.ObservedRequests.Count+" world="+world+" readOnly=true");} }
                    if (pendingError != null) { viewReadError=data.WorldKey==world&&data.ManualPlayerId==manualPlayer?"Refresh failed; previous observations retained. Retry Refresh.":"Records unavailable. Retry Refresh.";Log("LedgerViewError",pendingError);pendingError=null; }
                }
                PollManualResults();PollPlaceMoves();PollPlaceFactions(context,ready,now);
                if(open&&ready&&current!=null&&nativeGeography!=null&&nativeGeography.Enrich(data,Application,context,positionNow)){data.PrepareDistances();locations=null;locationCacheKey=null;}
                if(open&&view==7&&placeDirty&&positionNow-placeLoadedAt>=1000){placeDirty=false;placeLoadedAt=positionNow;refreshRequested=true;}
                if(open&&view==4&&productionDirty&&positionNow-productionLoadedAt>=1000){productionDirty=false;productionLoadedAt=positionNow;refreshRequested=true;}
                if(ready&&context!=null&&context.Recordable&&NativeBookmarks!=null)NativeBookmarks.Refresh(context,Application,false,catalog,InventoryReader);
                else if(NativeBookmarks!=null)NativeBookmarks.InvalidateInventory();
                if(bookmarkRequested!=null)
                {
                    var intention=bookmarkRequested;bookmarkRequested=null;
                    if(open&&Lease.Top&&ready&&NativeBookmarks!=null)NativeBookmarks.Submit(intention,context,Application);
                    else Log("LedgerBookmarkSkipped","reason=modal-or-world-no-longer-admitted");
                }
                if (refreshRequested && open) { refreshRequested = false; Load(); }
                SyncPointer();
            }
            catch (Exception e) { Fail(e); }

            }
        }
        private void Open()
        {
            menuEntry.Hide();menuHandoff.Cancel();retiringEntryMenu=null;
            // This fixed native dialog sits behind our opaque window and owns input/cursor/normal Escape handling.
            var config = new DialogConfig { TitleText = "", BodyText = "",
                ButtonTexts = new string[0], ButtonIdxForEsc = 0, ButtonIdxForEnter = -1, MaxChars = 0 };
            int ticket=++modalSerial;
            DialogActionHandler handler = (button, link, input, player, custom) => { if(open&&ticket==modalSerial)closeRequested = true; };
            bool accepted = NativeGui.ShowDialog(config, handler, 731025);
            if (!accepted) { Log("LedgerViewSkipped", "native-modal-rejected"); return; }
            if (!Lease.Adopt(config, handler)) throw new InvalidOperationException("Native modal ownership could not be proven");
            // Remove native button hit targets behind IMGUI. Escape remains a native fallback until
            // ownership is proven, then this window alone handles the close key/release lifecycle.
            // Keep the native Rewired CloseWindow (80) route enabled as a focus-independent fallback.
            config.ButtonIdxForEsc=0;
            lifecycle.Opened();ownTyping=false;guiFocus.RequestClear();
            Log("LedgerViewOpened", "entry=standalone-native-modal hotkey=] code=" + (int)ToggleKey + " world=" + world + " nativeTab=false");
            try{var local=Application==null?null:Application.LocalPlayer;SetManualPlayer(local!=null&&local.Id>0?(int?)local.Id:null);}catch(Exception){SetManualPlayer(null);}
            SyncPointer();refreshRequested=false;Load();
        }
        private void Load()
        {
            long ticket;lock(resultGate){if(!viewRead.Begin(generation,out ticket)){refreshRequested=true;return;}}
            string scope = world, path = DatabasePath;var itemCatalog=catalog;int? player=manualPlayer;int revision=manualRevision;bool force=forceRead;forceRead=false;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                LedgerViewData value = null; string error = null;
                try { value = viewReader.Read(path, scope,itemCatalog,player,force); }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
                lock (resultGate) { if (viewRead.Complete(ticket,generation)&&open) { pending = value; pendingError = error;pendingManualRevision=revision; }else readRetry=true; }
            });
        }
        private void InvalidateViewRead(){lock(resultGate){generation++;pending=null;pendingError=null;readRetry=false;}}
        private void FinishClose(string reason,bool interrupted=false)
        {
            menuEntry.Hide();menuEntry.Clicked=false;CloseUiMenu();uiMenuInput.Reset();
            CancelGraphDrag();CancelRequestGesture();open = false; closeRequested = false;escapePending=false;bookmarkRequested=null;InvalidateViewRead();modalSerial++;CloseComparisonWork(true);
            ownTyping=false;guiFocus.RequestClear();
            if(interrupted)try{ReleasePointer();}catch(Exception e){Log("LedgerPointerRestoreError",e.ToString());}
            Log("LedgerViewClosed", "reason=" + reason + " nativeFocusCleanup=true");
        }
        private void Fail(Exception e)
        {
            windowSwitch.Cancel();CancelGraphDrag();CancelRequestGesture();
            try { if (Lease != null) Lease.CloseTop(); } catch (Exception) { }
            try{ReleasePointer();}catch(Exception restore){Log("LedgerPointerRestoreError",restore.ToString());}
            FinishClose("recoverable-error",true);failed=true;recoverAt=(long)(Time.realtimeSinceStartup*1000)+1000;lifecycle.Fault(recoverAt-1000);
            Log("LedgerViewError", "recovery=one-second-cooldown;fresh-shortcut-required " + e.ToString());
        }
        public void Stop()
        { try { if (Lease != null) Lease.CloseTop(); } catch (Exception e) { Log("LedgerViewError", "close=" + e.GetType().Name); } windowSwitch.Cancel();FinishClose("shutdown"); ReleasePointer(); if(ManualStore!=null)ManualStore.ObservationCommitted-=ProductionCommitted; if(NativeBookmarks!=null)NativeBookmarks.Stop(); Destroy(gameObject); }
        private void LateUpdate() { try{PollPlaceMoves();if(Lease!=null){Lease.Conceal();if(!failed)SyncPointer();}}catch(Exception e){if(!failed)Fail(e);} }
        private void LostFocus(string source)
        {
            menuEntry.Hide();menuEntry.Clicked=false;
            windowSwitch.Cancel();lifecycle.FocusLost();focusLostPending=true;ownTyping=false;guiFocus.RequestClear();CancelGraphDrag();CancelRequestGesture();
            try{ReleasePointer();}catch(Exception e){if(Log!=null)Log("LedgerPointerRestoreError",e.ToString());}
            if(Log!=null)Log("LedgerViewFocus","focused=false source="+source+" release-required=true no-OS-focus-change=true");
        }
        private void OnApplicationFocus(bool focus) { if(!focus)LostFocus("application-focus");else if(Log!=null)Log("LedgerViewFocus","focused=true rearm=key-release;no-click-required"); }
        private void OnApplicationPause(bool value){paused=value;if(value)LostFocus("application-pause");}
        private void OnDestroy() { foreach(var texture in ownedSkinTextures)Destroy(texture);ownedSkinTextures.Clear();if(placeRelationDot!=null)Destroy(placeRelationDot);menuEntry.Dispose();try{CancelGraphDrag();CancelRequestGesture();ReleasePointer();}catch(Exception e){Log("LedgerPointerRestoreError",e.ToString());} if (background != null) Destroy(background);if(selectionBackground!=null)Destroy(selectionBackground); if (ledgerSkin != null) Destroy(ledgerSkin); }
        private void OnGUI()
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.UiRender))
            {
            ReleaseUiMenuHot();if(guiFocus.ClearKeyboard(GUIUtility.keyboardControl))GUIUtility.keyboardControl=0;
            if(guiFocus.ClearHot(GUIUtility.hotControl))GUIUtility.hotControl=0;
            guiFocus.Cleared();
            if (!open || failed || Lease == null) return;
            try
            {
                if (!UnityEngine.Application.isFocused||paused||focusLostPending||!LedgerWindowPolicy.CanRender(open, failed, Lease.Top)) return; // Never draw over somebody else's newer modal.
                if ((Event.current.type == EventType.KeyDown || Event.current.type==EventType.KeyUp) && Event.current.keyCode == KeyCode.Escape) { if(Event.current.type==EventType.KeyDown){closeRequested = true;escapePending=true;} Event.current.Use(); }
                EnsureStyle();
                int oldDepth = GUI.depth; Color old = GUI.color; Matrix4x4 oldMatrix = GUI.matrix; GUISkin oldSkin = GUI.skin; bool oldEnabled = GUI.enabled;
                try
                {
                    GUI.depth = -1000; GUI.color = Color.white;
                    layout = LedgerLayout.For(Screen.width, Screen.height);
                    GUI.matrix = oldMatrix * Matrix4x4.Scale(new Vector3(layout.Scale,layout.Scale,1));
                    GUI.skin = ledgerSkin;
                    if(view!=5)CancelGraphDrag();
                    if(uiMenuKey==null)HandleGraphDrag();else CancelGraphDrag();
                    GUI.Box(new Rect(0,0,layout.Width,layout.Height),"",row);
                    Draw(731025);
                    EndRequestGesture();
                    if(Event.current.type!=EventType.Used&&(Event.current.isMouse||Event.current.type==EventType.ScrollWheel))Event.current.Use();
                }
                finally { guiFocus.Remember(GUIUtility.keyboardControl,GUIUtility.hotControl);ownTyping=guiTextFocused;guiTextFocused=false;GUI.depth = oldDepth; GUI.color = old; GUI.matrix = oldMatrix; GUI.skin = oldSkin; GUI.enabled = oldEnabled; }
            }
            catch (Exception e) { Fail(e); }

            }
        }
        private void EnsureStyle()
        {
            if (text != null) return;
            background = new Texture2D(1, 1); background.SetPixel(0, 0, new Color(.045f, .16f, .25f, .97f)); background.Apply();
            selectionBackground=new Texture2D(1,1);selectionBackground.SetPixel(0,0,new Color(.08f,.30f,.40f,1));selectionBackground.Apply();
            ledgerSkin = Instantiate(GUI.skin);
            ledgerSkin.label = new GUIStyle(ledgerSkin.label) { fontSize=LedgerLayout.BodyFont, wordWrap=true, richText=false };
            ledgerSkin.label.normal.textColor = new Color(.88f,.94f,.95f);
            ledgerSkin.button = new GUIStyle(ledgerSkin.button) { fontSize=LedgerLayout.BodyFont, wordWrap=true, richText=false, padding=new RectOffset(6,6,3,3) };
            ledgerSkin.toggle = new GUIStyle(ledgerSkin.toggle) { fontSize=LedgerLayout.BodyFont, padding=new RectOffset(6,6,3,3) };
            ledgerSkin.textField = new GUIStyle(ledgerSkin.textField) { fontSize=LedgerLayout.BodyFont, padding=new RectOffset(6,6,3,3),richText=false };
            ledgerSkin.box = new GUIStyle(ledgerSkin.box) { padding=new RectOffset(4,4,4,4),margin=new RectOffset(0,0,0,0) };
            ledgerSkin.verticalScrollbar.fixedWidth=LedgerLayout.Scrollbar;
            ledgerSkin.verticalScrollbarThumb.fixedWidth=LedgerLayout.Scrollbar;ledgerSkin.verticalScrollbarThumb.fixedHeight=0;ledgerSkin.verticalScrollbarThumb.stretchHeight=true;
            ledgerSkin.verticalScrollbarUpButton.fixedWidth=LedgerLayout.Scrollbar;ledgerSkin.verticalScrollbarUpButton.fixedHeight=LedgerLayout.Scrollbar;
            ledgerSkin.verticalScrollbarDownButton.fixedWidth=LedgerLayout.Scrollbar;ledgerSkin.verticalScrollbarDownButton.fixedHeight=LedgerLayout.Scrollbar;
            ledgerSkin.horizontalScrollbar.fixedHeight=LedgerLayout.Scrollbar;ledgerSkin.horizontalScrollbarThumb.fixedHeight=LedgerLayout.Scrollbar;
            var normal=new Texture2D(1,1);normal.SetPixel(0,0,new Color(.10f,.30f,.45f,.96f));normal.Apply();
            var hover=new Texture2D(1,1);hover.SetPixel(0,0,new Color(.16f,.45f,.62f,1f));hover.Apply();
            var track=new Texture2D(1,1);track.SetPixel(0,0,new Color(.035f,.09f,.14f,1));track.Apply();
            var thumb=new Texture2D(1,1);thumb.SetPixel(0,0,new Color(.38f,.69f,.8f,1));thumb.Apply();ownedSkinTextures.AddRange(new[]{normal,hover,track,thumb});
            foreach(var style in new[]{ledgerSkin.button,ledgerSkin.textField,ledgerSkin.box})
            {style.normal.background=normal;style.hover.background=hover;style.focused.background=hover;style.active.background=hover;style.onNormal.background=hover;style.onHover.background=hover;style.onFocused.background=hover;style.fontSize=LedgerLayout.SmallFont;style.border=new RectOffset(0,0,0,0);style.padding=new RectOffset(5,5,2,2);style.normal.textColor=new Color(.88f,.95f,1f);}
            foreach(var style in new[]{ledgerSkin.verticalScrollbar,ledgerSkin.verticalScrollbarThumb,ledgerSkin.verticalScrollbarUpButton,ledgerSkin.verticalScrollbarDownButton,ledgerSkin.horizontalScrollbar,ledgerSkin.horizontalScrollbarThumb})
            {style.normal.background=normal;style.hover.background=hover;style.active.background=hover;style.border=new RectOffset(0,0,0,0);style.padding=new RectOffset(0,0,0,0);}
            ledgerSkin.verticalScrollbar.normal.background=track;ledgerSkin.verticalScrollbarThumb.normal.background=thumb;ledgerSkin.verticalScrollbarThumb.hover.background=hover;ledgerSkin.verticalScrollbarThumb.padding=new RectOffset(0,0,6,6);ledgerSkin.verticalScrollbarThumb.border=new RectOffset(0,0,0,0);ledgerSkin.verticalScrollbarThumb.overflow=new RectOffset(0,0,0,0);
            selectedTab=new GUIStyle(ledgerSkin.button){fontStyle=FontStyle.Bold};selectedTab.normal.background=selectionBackground;selectedTab.normal.textColor=Color.white;
            text = new GUIStyle(ledgerSkin.label);
            heading = new GUIStyle(text) {fontSize=LedgerLayout.HeadingFont};
            small = new GUIStyle(text) {fontSize=LedgerLayout.SmallFont};
            rich=new GUIStyle(text){richText=true};richHeading=new GUIStyle(heading){richText=true};
            pairText=new GUIStyle(rich){wordWrap=true,clipping=TextClipping.Clip,padding=new RectOffset(0,0,0,0),fontSize=LedgerLayout.SmallFont};
            itemButton = new GUIStyle(ledgerSkin.label) {alignment=TextAnchor.MiddleLeft,wordWrap=false,clipping=TextClipping.Clip,richText=true,padding=new RectOffset(6,4,0,0)};
            row = new GUIStyle(ledgerSkin.window);row.normal.background=background;row.onNormal.background=background;
            selectedRow=new GUIStyle(ledgerSkin.box);selectedRow.normal.background=selectionBackground;
        }
        private string SaveNotice { get { return LedgerSaveAliases.Notice(currentContext,data.Markets.Count>0||data.Requests.Count>0||data.ObservedRequests.Count>0); } }
        private Rect Content { get { float y=SaveNotice==null?96:136;return new Rect(16,y,layout.Width-32,layout.Height-y-16); } }
        private void Label(Rect rect,string value,bool emphasis=false)
        {GUI.Label(rect,LedgerPresentation.ItemRich(value),emphasis?richHeading:rich);}
        private bool Button(Rect rect,string value){return GUI.Button(rect,value);}
        private void Backplate(Rect rect,float alpha=LedgerReadability.PanelAlpha)
        {var old=GUI.color;GUI.color=new Color(.025f,.075f,.12f,alpha>=.98f?1:LedgerReadability.PanelAlpha);GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;}
        private bool guiTextFocused;
        private string Search(Rect rect,string value,string name,string placeholder=null)
        {ControlFrame(rect,GUI.GetNameOfFocusedControl()==name);GUI.SetNextControlName(name);string result=GUI.TextField(new Rect(rect.x+2,rect.y+2,rect.width-4,rect.height-4),value,80);if(GUI.GetNameOfFocusedControl()==name)guiTextFocused=true;else if(string.IsNullOrEmpty(result)&&placeholder!=null){Color old=GUI.color;GUI.color=new Color(.63f,.75f,.8f);GUI.Label(new Rect(rect.x+6,rect.y+2,rect.width-12,rect.height-4),placeholder,small);GUI.color=old;}return result;}
        private void ControlFrame(Rect r,bool active)
        {
            Color old=GUI.color;GUI.color=active?new Color(.5f,.9f,1f,1):new Color(.42f,.64f,.77f,1);GUI.DrawTexture(r,Texture2D.whiteTexture);
            GUI.color=new Color(.025f,.075f,.12f,1);GUI.DrawTexture(new Rect(r.x+2,r.y+2,r.width-4,r.height-4),Texture2D.whiteTexture);GUI.color=old;
        }
        private bool SelectRow(Rect rect,bool selected)
        {
            bool hover=rect.Contains(Event.current.mousePosition);
            if(selected||hover){Color old=GUI.color;GUI.color=selected?new Color(.42f,.70f,.78f,.55f):new Color(.55f,.63f,.65f,.18f);GUI.DrawTexture(rect,selectionBackground);GUI.color=old;}
            if(GUI.enabled&&hover&&Event.current.type==EventType.MouseDown&&Event.current.button==0){Event.current.Use();return true;}return false;
        }
        private void Draw(int id)
        {
            var ev=Event.current;PositionUiMenu();bool popup=uiMenuKey!=null;
            bool pointer=ev.isMouse||ev.type==EventType.ScrollWheel;
            bool block=uiMenuInput.Begin(popup,popup&&uiMenuRect.Contains(ev.mousePosition),pointer,ev.type==EventType.MouseDown,ev.type==EventType.MouseUp);
            if(uiMenuInput.Dismiss)CloseUiMenu();
            EventType original=ev.type;bool enabled=GUI.enabled;
            // GUI.enabled alone does not stop BeginScrollView handling wheel events.
            // Hide the event from every background control, then restore it for the overlay.
            if(block)ev.type=EventType.Ignore;GUI.enabled=enabled&&!popup&&!block;
            try{DrawPage(id);}finally{GUI.enabled=enabled;if(block)ev.type=original;}
            DrawUiMenu();ReleaseUiMenuHot();if(block&&ev.type!=EventType.Used)ev.Use();DrawHoverLabel();
        }
        private void DrawPage(int id)
        {
            iconHoverName=null;hoverLabel=null;
            Label(new Rect(18,12,270,30),"Trade Ledger",true);
            if(returnPages.Count>0){if(UiMenuButton(new Rect(300,12,210,28),"< "+LedgerJournalUi.Tabs[returnPages.Peek.View],"Return to previous view"))ReturnPageBack();}
            else if(viewReadError!=null)Label(new Rect(300,16,Math.Max(120,layout.Width-570),24),viewReadError);
            else if(loading)Label(new Rect(300,16,400,24),data.WorldKey==world?"Refreshing...":"Loading...");
            else if(view==0)Label(new Rect(300,16,400,24),data.Markets.Count+" markets / "+data.Items(null).Count+" items");
            else if(view==5)Label(new Rect(300,16,400,24),browserCatalog==null?"Loading catalog...":browserCatalog.Items.Count+" items");
            var profileRect=new Rect(layout.Width-252,12,150,28);
            bool profileControlEnabled=GUI.enabled;GUI.enabled=profileControlEnabled&&LedgerProfiler.State!="Saving";
            if(UiMenuButton(profileRect,LedgerProfiler.ButtonLabel,"Record Ledger timings for up to 10 minutes; click again to stop. Session only."))LedgerProfiler.SetEnabled(!LedgerProfiler.Enabled);
            GUI.enabled=profileControlEnabled;
            if(profileRect.Contains(Event.current.mousePosition)&&LedgerProfiler.Output!=null)hoverLabel="Profiler: "+LedgerProfiler.Output+(LedgerProfiler.StopReason==null?"":" | "+LedgerProfiler.StopReason);
            if(HeaderAction(new Rect(layout.Width-94,12,34,28),true)){forceRead=true;refreshRequested=true;browserReload=true;}
            if(HeaderAction(new Rect(layout.Width-52,12,34,28),false))closeRequested=true;
            for(int n=0;n<LedgerJournalUi.TabOrder.Length;n++){int i=LedgerJournalUi.TabOrder[n];var tab=new Rect(18+n*((layout.Width-36)/LedgerJournalUi.Tabs.Length),52,(layout.Width-36)/LedgerJournalUi.Tabs.Length-6,32);ControlFrame(tab,view==i);if(GUI.Button(new Rect(tab.x+2,tab.y+2,tab.width-4,tab.height-4),LedgerJournalUi.Tabs[i],view==i?selectedTab:GUI.skin.button))RootTab(i);if(view==i){Color prior=GUI.color;GUI.color=new Color(.35f,.9f,1f);GUI.DrawTexture(new Rect(tab.x+2,tab.yMax-4,tab.width-4,3),Texture2D.whiteTexture);GUI.color=prior;}}
            if(SaveNotice!=null){Backplate(new Rect(16,85,layout.Width-32,45),.98f);GUI.Label(new Rect(22,89,layout.Width-44,39),SaveNotice,small);}
            bool enabled=GUI.enabled;GUI.enabled=enabled&&uiMenuKey==null;
            if(view==0)DrawMarkets();else if(view==1)DrawRequests();else if(view==2)DrawLocations();else if(view==3)DrawComparison();else if(view==4)DrawProduction();else if(view==5)DrawItemBrowser();else if(view==6)DrawMarkerManager();else DrawPlaces();
            GUI.enabled=enabled;
        }

        private void DrawMarkets()
        {
            Rect c=Content;float left=layout.LeftWidth;Rect right=new Rect(c.x+left+12,c.y,c.width-left-12,c.height);
            search=Search(new Rect(c.x,c.y,left,28),search,"LedgerSearch","Search items");
            var items=data.ScopedItems(locations,itemLocation,search,"All items");
            if(catalogSelection!=null&&catalogSelection.Key==selectedKey&&(string.IsNullOrEmpty(search)||catalogSelection.Name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0))
            {
                if(catalogSelection.Canonical)items.RemoveAll(i=>i.Id==catalogSelection.Id);
                if(!items.Any(i=>i.Key==selectedKey))items.Add(catalogSelection);
            }
            bool pinsReady=NativeBookmarks!=null&&NativeBookmarks.Ready&&NativeBookmarks.World==world&&manualPlayer==NativeBookmarks.Player;
            var pins=pinsReady?LedgerItemPins.For(NativeBookmarks.Model,world,manualPlayer.Value):null;
            items=LedgerItemPins.Include(items,pins,search,i=>itemLocation==null||locations!=null&&data.Offers(i,true).Concat(data.Offers(i,false)).Any(o=>locations.Contains(itemLocation,o.Market)));
            var tree=LedgerItemPins.Tree(items,collapsedGroups,search,pins);
            if(selectedKey==null&&items.Count>0){var pinnedFirst=tree.FirstOrDefault(t=>t.Item!=null&&LedgerItemPins.Has(pins,t.Item.Key));selectedKey=pinnedFirst==null?items[0].Key:pinnedFirst.Item.Key;}
            Rect viewport=new Rect(c.x,c.y+36,left,c.height-36);Backplate(viewport);float w=left-LedgerLayout.Scrollbar-3;
            itemsScroll=GUI.BeginScrollView(viewport,itemsScroll,new Rect(0,0,w,tree.Count*LedgerLayout.TreeRow),false,true);
            int start=LedgerLayout.FirstRow(itemsScroll.y,tree.Count,LedgerLayout.TreeRow),end=LedgerLayout.EndRow(itemsScroll.y,viewport.height,tree.Count,LedgerLayout.TreeRow);
            for(int n=start;n<end;n++)
            {
                var entry=tree[n];Rect r=new Rect(0,n*LedgerLayout.TreeRow,w,LedgerLayout.TreeRow);
                if(entry.Item==null)
                {if(!entry.PinnedGroup&&SelectRow(r,false)){if(!collapsedGroups.Add(entry.Category))collapsedGroups.Remove(entry.Category);}Label(r,entry.PinnedGroup?entry.Category:(entry.Expanded?"\u25BE  ":"\u25B8  ")+entry.Category);}
                else
                {var item=entry.Item;DrawItemPin(new Rect(w-28,r.y+1,26,r.height-2),item,LedgerItemPins.Has(pins,item.Key),pinsReady);if(SelectRow(new Rect(0,r.y,w-30,r.height),selectedKey==item.Key)){selectedKey=item.Key;selectedOfferKey=null;}GUI.Label(new Rect(18,r.y,w-50,r.height),LedgerPresentation.ItemRich(item.Markup??item.Name),itemButton);}
            }
            GUI.EndScrollView();
            Backplate(new Rect(right.x,right.y,right.width,60));
            var selected=data.Items(null).FirstOrDefault(i=>i.Key==selectedKey)??(catalogSelection!=null&&catalogSelection.Key==selectedKey?catalogSelection:null)??(pins==null?null:pins.Items.FirstOrDefault(i=>i.Key==selectedKey));
            if(selected!=null)
            {
                if(iconKey!=selected.Key){iconKey=selected.Key;try{selectedIcon=NativeCatalog==null?null:NativeCatalog.Icon(selected.Sprite);}catch(Exception){selectedIcon=null;}}
                Rect icon=new Rect(right.x,right.y,48,48);
                if(selectedIcon!=null)GUI.DrawTextureWithTexCoords(icon,selectedIcon.Texture,selectedIcon.Uv);
                else GUI.Box(icon,new GUIContent("--","Item image unavailable"));
                if(selectedIcon!=null&&icon.Contains(Event.current.mousePosition))iconHoverName=selected.Markup??selected.Name;
            }
            Label(new Rect(right.x+60,right.y+2,right.width-60,28),selected==null?"Select an item":selected.Markup??selected.Name,true);
            if(itemLocation!=null){Label(new Rect(right.x+60,right.y+30,right.width-150,26),itemLocation.Label);if(Button(new Rect(right.xMax-80,right.y+30,80,26),"Clear"))itemLocation=null;}
            DrawItemInformation(new Rect(right.x,right.y+64,right.width,180),selected);
            float pane=(right.height-260)/2;
            DrawOfferTable(new Rect(right.x,right.y+252,right.width,pane),selected,true,ref sellersScroll);
            DrawOfferTable(new Rect(right.x,right.y+258+pane,right.width,pane),selected,false,ref buyersScroll);

        }
        private void DrawOfferTable(Rect area,LedgerViewItem item,bool sellers,ref Vector2 scroll)
        {
            var sort=sellers?marketSellSort:marketBuySort;
            var rows=data.Offers(item,sellers);
            Label(new Rect(area.x,area.y,area.width,26),LedgerJournalUi.Side(sellers),true);
            float[] columns={.12f,.12f,.26f,.30f,.12f,.08f};
            if(TableHeader(new Rect(area.x,area.y+29,area.width-LedgerLayout.Scrollbar-3,25),new[]{"Unit price",sellers?"Stock":"Demand","Vendor","Place","Distance",""},columns,sort))scroll=Vector2.zero;
            rows=(sellers?marketSellCache:marketBuyCache).Apply(rows,sort,new[]{LedgerOfferColumn.Price,LedgerOfferColumn.Quantity,LedgerOfferColumn.Vendor,LedgerOfferColumn.Place,LedgerOfferColumn.Distance},data,playerPoint,positionAt,itemLocation,r=>itemLocation==null||locations!=null&&locations.Contains(itemLocation,r.Market));
            Rect port=new Rect(area.x,area.y+55,area.width,area.height-55);
            DrawRows(port,ref scroll,rows.Count,LedgerLayout.TableRow,(i,r)=>
            {var offer=rows[i];
                OfferBackdrop(new Rect(r.x,r.y,r.width*.80f,r.height),offer.Market);
                DrawOfferBookmark(new Rect(r.x+r.width*.92f,r.y+2,r.width*.08f-3,r.height-4),offer);
                if(SelectRow(new Rect(r.x,r.y,r.width*.92f,r.height),selectedOfferKey==LedgerBrowse.OfferKey(offer.Market,offer.Offer,sellers)))selectedOfferKey=LedgerBrowse.OfferKey(offer.Market,offer.Offer,sellers);
                OfferCells(r,new[]{offer.Price,offer.Quantity,data.VendorFor(offer.Market),data.PlaceFor(offer.Market),data.DistanceFor(offer.Market,playerPoint),""},columns,offer.Market,2);
                if(new Rect(r.x+r.width*.80f,r.y,r.width*.12f,r.height).Contains(Event.current.mousePosition))hoverLabel=data.DistanceReasonFor(offer.Market,playerPoint);},LedgerItemDisplay.EmptyMarket(data,item,sellers,itemLocation!=null));
        }
        private bool TableHeader(Rect rect,string[] values,float[] columns,LedgerHeaderSort sort=null)
        {
            Backplate(rect);if(sort==null){TableCells(rect,values,columns);return false;}bool changed=false;
            float x=rect.x;for(int i=0;i<values.Length;i++){float w=rect.width*columns[i];if(!string.IsNullOrEmpty(values[i])&&GUI.Button(new Rect(x+4,rect.y,w-8,rect.height),new GUIContent(sort.Label(values[i],i),"Sort by "+values[i]),itemButton)){sort.Click(i);changed=true;}x+=w;}return changed;
        }
        private void TableCells(Rect rect,string[] values,float[] columns)
        {float x=rect.x;for(int i=0;i<values.Length;i++){float w=rect.width*columns[i];GUI.Label(new Rect(x+5,rect.y+3,w-10,rect.height-4),LedgerPresentation.ItemRich(values[i]??"unknown"),itemButton);x+=w;}}
        private void DrawRows(Rect port,ref Vector2 scroll,int count,int height,Action<int,Rect> draw,string emptyText="No offers in this selection.",float panelAlpha=.86f,float rowAlpha=LedgerReadability.RowAlpha)
        {
            Backplate(port,panelAlpha);float w=port.width-LedgerLayout.Scrollbar-3;
            scroll.y=Math.Max(0,Math.Min(scroll.y,Math.Max(0,count*height-port.height)));
            scroll=GUI.BeginScrollView(port,scroll,new Rect(0,0,w,LedgerLayout.ScrollContentHeight(count,height)),false,true);
            int start=LedgerLayout.FirstRow(scroll.y,count,height),end=LedgerLayout.EndRow(scroll.y,port.height,count,height);
            for(int i=start;i<end;i++){var r=new Rect(0,i*height,w,height);Backplate(r,rowAlpha);draw(i,r);}
            if(count==0)Label(new Rect(6,6,w-12,28),emptyText);GUI.EndScrollView();
        }
        private void DrawRequests(){DrawRequestBrowser();}

    }

    internal static class LedgerWindowBootstrap
    {
        internal static ILedgerWindow Create(Assembly game, IGui gui, IApplication application,string databasePath,LedgerStore store, Action<string,string> log)
        {
            string managed = Path.GetDirectoryName(game.Location);
            if (Safe.Hash(Path.Combine(managed, "UnityEngine.IMGUIModule.dll")) != "83C3C8182B2F822B253B7F28022004FE1CF9F48392AF4EA8EEF4130167DF4E12" ||
                Safe.Hash(Path.Combine(managed, "UnityEngine.InputLegacyModule.dll")) != "6D40162C06BFDBD965E616745705C2E450013A7E74CA116DC5E0AE26F8B4C7A9" ||
                Safe.Hash(Path.Combine(managed, "UnityEngine.TextRenderingModule.dll")) != "BC789553A0BDC72A1248324D0AC402EB1F412DED427BEA6DCE6073F4EF27A7B4") throw new InvalidDataException("UI Unity fingerprint mismatch");
            var lease = new LedgerModalLease(game);
            var go = new GameObject("TradeLedger-owned-prototype"); UnityEngine.Object.DontDestroyOnLoad(go);
            var window = go.AddComponent<LedgerPrototypeWindow>(); window.Lease = lease;
            window.NativeGui = gui; window.DatabasePath = databasePath; window.Log = log;
            window.RestoreTabPreference();
            window.Application=application;window.ManualStore=store;
            if(store!=null)store.ObservationCommitted+=window.ProductionCommitted;
            try{window.NativeCatalog=new LedgerNativeCatalog(game,application);}catch(Exception e){log("LedgerCatalogUnavailable",e.GetType().Name);}
            try{window.NativeBookmarks=new LedgerNativeBookmarks(game,store==null?Path.Combine(Path.GetDirectoryName(databasePath),"LedgerBookmarks.json"):store.BookmarkOwnershipPath,log,store==null?null:store.Status.Root,store==null?(Action<Action>)null:store.PublishOwnedFile,store,databasePath);}catch(Exception e){log("LedgerBookmarkUnavailable",e.ToString());}
            log("LedgerViewReady", "standalone=true key=] code=93 publicNativeModal=true nativeTab=false readOnlyDatabase=true no-F8-fallback=true");
            return window;
        }
    }
    public sealed partial class Probe
    {
        private ILedgerWindow ledgerWindow;
        private bool uiAttempted;
        private long uiRetryAt;
        private void PollLedgerWindow()
        {
            if(portableEnabled&&WorldReady&&ledgerWindow==null&&UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.RightBracket))Write("LedgerOpenBlocked","gate=window-not-created ready="+WorldReady+" scope="+(ledgerContext==null?"absent":ledgerContext.ScopeState)+" storage="+(ledger==null?"not-bound":ledger.Status.State)+" retryPending="+portableOpening);
            if(uiAttempted&&ledgerWindow==null&&ElapsedMs>=uiRetryAt)uiAttempted=false;
            if (!uiAttempted && WorldReady && ledger!=null)
            {
                uiAttempted = true;uiRetryAt=ElapsedMs+5000;
                try
                {
                    var game = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                    if (game == null) { Write("LedgerViewSkipped", "game-assembly-unavailable"); return; }
                    ledgerWindow = LedgerWindowBootstrap.Create(game, api.GUI,application, ledgerDatabasePath,ledger, Write);
                    var inventoryWindow=ledgerWindow as LedgerPrototypeWindow;if(inventoryWindow!=null)inventoryWindow.InventoryReader=ReadInventorySnapshot;
                }
                catch (Exception e) { Write("LedgerViewError", "initialization=" + e.ToString()); }
            }
            if (ledgerWindow != null) ledgerWindow.Poll(ledgerContext, WorldReady);
        }
        private void StopLedgerWindow()
        {
            try { if (ledgerWindow != null) ledgerWindow.Stop(); }
            catch (Exception e) { Write("LedgerViewError", "shutdown=" + e.ToString()); }
            finally { ledgerWindow = null; }
        }
    }
}
