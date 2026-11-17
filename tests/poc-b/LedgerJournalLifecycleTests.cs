using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Eleon.Modding;
using TradeLedgerProbe;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal static class LedgerJournalLifecycleTests
    {
        private static LedgerJournalFrame Frame(){return new LedgerJournalFrame{Focused=true,Ready=true,Scoped=true,Empty=true,WorldVisible=true};}
        private static void Open(LedgerJournalLifecycle flow,LedgerJournalFrame f)
        {
            f.ShortcutHeld=false;flow.Step(f);f.ShortcutHeld=true;
            if(!flow.Step(f).OpenNative)throw new Exception("fixture failed to open");flow.Opened();f.Present=f.Top=true;f.Empty=false;f.ShortcutHeld=false;flow.Step(f);
        }
        internal static void Run(Action<bool,string> check,string root)
        {
            check(LedgerJournalLifecycle.ShortcutCode==93&&LedgerJournalLifecycle.ShortcutLabel=="]","shortcut is RightBracket code93, independent of text character and without an F8 fallback");
            check(!LedgerJournalLifecycle.IsTyping(true,false,false,false,false,false),"inactive or blurred selected UI fields do not require a mouse click to stop blocking shortcut");
            for(int i=0;i<6;i++){var flags=new[]{true,false,false,false,false,false};flags[i]=i!=0;check(LedgerJournalLifecycle.IsTyping(flags[0],flags[1],flags[2],flags[3],flags[4],flags[5]),"real typing/native/debug readiness block preserved "+i);}
            var flow=new LedgerJournalLifecycle();var f=Frame();Open(flow,f);f.ShortcutHeld=true;
            check(flow.Step(f).CloseNative&&flow.Open,"shortcut requests native owned close without pretending it already disappeared");
            f.Present=f.Top=false;f.Empty=false;check(flow.Step(f).ClosedReason=="native-dismissed"&&!flow.Open,"native removal finalizes managed visibility");
            f.ShortcutHeld=false;flow.Step(f);f.ShortcutHeld=true;check(!flow.Step(f).OpenNative,"reopen waits for native fade/root retirement");
            f.ShortcutHeld=false;f.Empty=true;flow.Step(f);f.ShortcutHeld=true;check(flow.Step(f).OpenNative,"fresh released shortcut reopens without a mouse click after fade");
            for(int pass=0;pass<20;pass++)
            {
                flow=new LedgerJournalLifecycle();f=Frame();Open(flow,f);f.Top=false;
                var a=flow.Step(f);check(a.ClosedReason=="native-panel-superseded"&&a.Interrupted&&!a.CloseNative&&!flow.Open,"F2/F4 interruption hides owned visuals and leaves newer native panel untouched cycle"+pass);
                f.ShortcutHeld=true;check(!flow.Step(f).OpenNative&&!flow.Step(f).CloseNative,"shortcut cannot pop or replace the foreign panel cycle"+pass);
                f.ShortcutHeld=false;f.Top=true;check(flow.Step(f).CloseNative,"only resurfaced exact owned modal is retired cycle"+pass);
                f.Present=f.Top=false;f.Empty=true;check(!flow.Step(f).OpenNative,"retirement frame never auto-reopens cycle"+pass);
                f.ShortcutHeld=true;check(flow.Step(f).OpenNative,"normal next shortcut reopens after foreign panel without clicking cycle"+pass);
            }
            flow=new LedgerJournalLifecycle();f=Frame();Open(flow,f);flow.FocusLost();f.Focused=false;
            check(flow.Step(f).ClosedReason=="focus-lost"&&!flow.Open,"Alt-Tab immediately invalidates owned rendering/input state");
            f.Present=f.Top=false;f.Empty=true;f.Focused=true;f.ShortcutHeld=true;
            check(!flow.Step(f).OpenNative&&!flow.Step(f).OpenNative,"holding shortcut through foreground return cannot reopen or duplicate-toggle");
            f.ShortcutHeld=false;flow.Step(f);f.ShortcutHeld=true;check(flow.Step(f).OpenNative,"focus returns by OS and release/new key press, never an extra mouse click");
            flow=new LedgerJournalLifecycle();f=Frame();Open(flow,f);flow.FocusLost();
            check(flow.Step(f).ClosedReason=="focus-lost","focus loss is remembered even if application Update paused until after focus returned");
            flow=new LedgerJournalLifecycle();f=Frame();f.Typing=true;flow.Step(f);f.ShortcutHeld=true;
            check(!flow.Step(f).OpenNative,"typing bracket in native input never opens Ledger");
            f.Typing=false;check(!flow.Step(f).OpenNative,"ending typing does not replay an already held shortcut");
            f.ShortcutHeld=false;flow.Step(f);f.ShortcutHeld=true;check(flow.Step(f).OpenNative,"new bracket press after editing opens normally");
            flow=new LedgerJournalLifecycle();f=Frame();Open(flow,f);f.Typing=true;f.ShortcutHeld=true;check(!flow.Step(f).CloseNative,"Ledger search accepts bracket as text rather than closing");
            f.EscapeHeld=true;check(!flow.Step(f).CloseNative&&flow.CloseRequested,"Escape while typing waits for release while modal retains ownership");
            f.EscapeHeld=false;check(flow.Step(f).CloseNative,"Escape release closes owned modal without a focus-clearing mouse click");
            flow=new LedgerJournalLifecycle();f=Frame();Open(flow,f);flow.CloseRequested=true;
            check(flow.Step(f).CloseNative&&flow.Step(f).CloseNative&&flow.Open,"native close refusal during animation is retried without pretending closed or losing ownership");
            f.Top=false;var superseded=flow.Step(f);check(superseded.Interrupted&&!superseded.CloseNative&&!flow.Open,"a panel arriving during deferred close is never popped as if it were Ledger");
            flow=new LedgerJournalLifecycle();f=Frame();flow.Fault(100);f.Now=200;flow.Step(f);f.ShortcutHeld=true;check(!flow.Step(f).OpenNative,"transient exception uses bounded recovery cooldown, no automatic reopen loop");
            f.Now=1200;f.ShortcutHeld=false;flow.Step(f);f.ShortcutHeld=true;check(flow.Step(f).OpenNative,"fresh key can recover after exception without restarting game");
            foreach(string missing in new[]{"ready","scope","stack","world","focus"})
            {
                flow=new LedgerJournalLifecycle();f=Frame();flow.Step(f);
                if(missing=="ready")f.Ready=false;if(missing=="scope")f.Scoped=false;if(missing=="stack")f.Empty=false;if(missing=="world")f.WorldVisible=false;if(missing=="focus")f.Focused=false;
                f.ShortcutHeld=true;check(!flow.Step(f).OpenNative,"opening guard preserved: "+missing);
            }
            var focus=new LedgerOwnedGuiFocus();focus.Remember(17,23);focus.RequestClear();
            check(focus.ClearKeyboard(17)&&focus.ClearHot(23)&&!focus.ClearKeyboard(41)&&!focus.ClearHot(42),"deferred OnGUI cleanup only clears controls captured from Ledger, never foreign controls");
            focus.Cleared();check(!focus.ClearKeyboard(17)&&!focus.ClearHot(23),"focus cleanup is idempotent and leaves no stale ownership");
            TabPreferenceChecks(check,root);
            ProbeBudgetChecks(check,root);
        }
        private static void TabPreferenceChecks(Action<bool,string> check,string root)
        {
            string path=Path.Combine(root,"last-tab-user-preference.txt");int writes=0;
            Func<string> read=()=>File.Exists(path)?File.ReadAllText(path):null;
            Action<string> write=id=>{File.WriteAllText(path,id);writes++;};
            var tabs=new LedgerTabPreference(read,write);
            check(tabs.Id=="markets"&&tabs.Index==0&&writes==0,"existing installation without tab preference opens Markets without writing user data");
            string[] ids={"markets","requests","locations","comparison","production","item-browser","markers"};
            for(int i=0;i<ids.Length;i++){
                tabs.Select(i);int prior=writes;tabs.Select(i);
                check(tabs.Id==ids[i]&&writes==prior,"stable tab identity and no per-frame repeat writes: "+ids[i]);
                var restart=new LedgerTabPreference(read,write);
                check(restart.Id==ids[i]&&restart.Index==i,"restart restores serialized tab preference: "+ids[i]);
            }
            check(File.ReadAllText(path)=="markers"&&!char.IsDigit(File.ReadAllText(path)[0]),"tab persistence contains a stable semantic ID, never the display index");
            foreach(string invalid in new[]{"","Trades","trades","4","removed-tab","production\0garbage",new string('x',1025)}){
                File.WriteAllText(path,invalid);var fallback=new LedgerTabPreference(read,write);
                check(fallback.Id=="markets"&&fallback.Index==0,"retired/corrupt/unknown tab safely falls back: "+invalid.Substring(0,Math.Min(20,invalid.Length)));
            }
            tabs=new LedgerTabPreference(()=>"production",write);var flow=new LedgerJournalLifecycle();var f=Frame();Open(flow,f);
            flow.CloseRequested=true;check(flow.Step(f).CloseNative,"last-tab fixture requests a normal owned close");f.Present=f.Top=false;f.Empty=true;flow.Step(f);Open(flow,f);
            check(tabs.Id=="production","normal close and shortcut reopen preserve selected Production tab");
            f.Top=false;check(flow.Step(f).Interrupted,"last-tab fixture exercises native panel supersession");f.Top=true;flow.Step(f);f.Top=f.Present=false;f.Empty=true;flow.Step(f);Open(flow,f);
            check(tabs.Id=="production","F2/F4 dismissal and reopening do not reset selected tab");
            flow.FocusLost();f.Focused=false;flow.Step(f);f.Present=f.Top=false;f.Empty=f.Focused=true;flow.Step(f);Open(flow,f);
            check(tabs.Id=="production","focus loss and release/reopen preserve selected tab");
            flow=new LedgerJournalLifecycle();f=Frame();flow.RequestOpen(0);check(flow.Step(f).OpenNative,"last-tab fixture opens through shared deferred menu lifecycle");flow.Opened();
            check(tabs.Id=="production","Escape-menu opening uses the same selected-tab preference");
            tabs.Select(0);check(new LedgerTabPreference(read,write).Id=="markets","explicit item/offers navigation to Markets replaces and remembers prior tab");
            var throwingRead=new LedgerTabPreference(()=>{throw new IOException("fixture unreadable preference");},write);check(throwingRead.Id=="markets","preference read failure does not prevent opening");
            var throwingWrite=new LedgerTabPreference(()=>"production",id=>{throw new IOException("fixture unwritable preference");});throwingWrite.Select(1);check(throwingWrite.Id=="requests","preference write failure retains usable in-session selection");
            string workspace=Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(typeof(Probe).Assembly.Location))));
            string window=File.ReadAllText(Path.Combine(workspace,"src/TradeLedgerProbeB/LedgerPrototypeWindow.cs"));
            string open=window.Substring(window.IndexOf("private void Open()"),window.IndexOf("private void Load()")-window.IndexOf("private void Open()"));
            string poll=window.Substring(window.IndexOf("public void Poll("),window.IndexOf("private void Open()")-window.IndexOf("public void Poll("));
            check(!System.Text.RegularExpressions.Regex.IsMatch(open+poll,@"\bview\s*=(?!=)"),"actual open/background capture-refresh path cannot reset or auto-switch the tab");
            check(window.Contains("set{tabPreference.Select(value);}")&&window.Contains("window.RestoreTabPreference();")&&window.Contains("PlayerPrefs.Save()"),"actual shared view setter and bootstrap persist selection through verified engine UI preferences");
            check(window.Contains("GUI.skin.button))RootTab(i);")&&File.ReadAllText(Path.Combine(workspace,"src/TradeLedgerProbeB/LedgerJournalUiWindow.cs")).Contains("returnPages.Clear();view=next;")&&File.ReadAllText(Path.Combine(workspace,"src/TradeLedgerProbeB/LedgerItemWindow.cs")).Contains("view=0;")&&File.ReadAllText(Path.Combine(workspace,"src/TradeLedgerProbeB/LedgerLocationWindow.cs")).Contains("view=0;"),"tab clicks and explicit existing market jumps all use the same preference setter");
        }
        private static void ProbeBudgetChecks(Action<bool,string> check,string root)
        {
            var app=new InterfaceMock(typeof(IApplication));var api=new InterfaceMock(typeof(IModApi));var local=new InterfaceMock(typeof(IPlayer));var gui=new InterfaceMock(typeof(IGui));
            app.Calls["get_Mode"]=a=>ApplicationMode.SinglePlayer;app.Calls["get_State"]=a=>GameState.Running;app.Calls["get_GameTicks"]=a=>(ulong)1;
            app.Calls["get_LocalPlayer"]=a=>local.As<IPlayer>();app.Calls["GetPathFor"]=a=>Path.Combine(root,"lifecycle-no-game-save");
            local.Calls["get_Id"]=a=>1002;local.Calls["get_Name"]=a=>"Fixture";gui.Calls["get_IsWorldVisible"]=a=>true;
            api.Calls["get_Application"]=a=>app.As<IApplication>();api.Calls["get_ClientPlayfield"]=a=>null;api.Calls["get_GUI"]=a=>gui.As<IGui>();int announcements=0;api.Calls["Log"]=a=>{announcements++;return null;};
            var primary=Path.Combine(root,"logger-full-init");Directory.CreateDirectory(primary);for(int i=0;i<128;i++)File.WriteAllText(Path.Combine(primary,"TradeLedgerProbe-fixture-"+i+".log"),"");
            string config=LedgerInstallPaths.Current.Settings;var prior=File.Exists(config)?File.ReadAllBytes(config):null;File.WriteAllText(config,"RuntimeObservation=off\n");
            try
            {
                {
                    var probe=new Probe(primary,Path.Combine(root,"must-not-evade-log-cap"));probe.Init(api.As<IModApi>());app.Emit("GameEntered",true);app.Emit("Update");
                    check(probe.WorldReady,"actual Probe remains ready with full preserved legacy evidence and independently managed diagnostics");
                    for(int i=0;i<100;i++)probe.Trace(TraceLevel.Error,"LedgerViewError","fixture bounded error");
                    check(announcements<=36,"file-cap fallback essentials are bounded in native logger, no disk-cap evasion");
                    check(Directory.GetFiles(primary).Length==128&&!Directory.Exists(Path.Combine(root,"must-not-evade-log-cap")),"legacy evidence is preserved without deletion or fallback-root writes");
                    app.Emit("GameEntered",false);check(!probe.WorldReady,"log independence never bypasses actual world leave");probe.Shutdown();
                }
                var active=new Probe(Path.Combine(root,"logger-active-init"),Path.Combine(root,"unused-fallback"));active.Init(api.As<IModApi>());app.Emit("GameEntered",true);
                var field=typeof(Probe).GetField("log",BindingFlags.NonPublic|BindingFlags.Instance);((BoundedLog)field.GetValue(active)).Dispose();
                var bounded=new BoundedLog(Path.Combine(root,"logger-mid-session"),"fixture",8192,16384);field.SetValue(active,bounded);
                for(int i=0;i<60;i++)bounded.Write("fixture","0",new string('a',500));
                check(bounded.Status=="budget-exhausted"&&active.WorldReady,"actual Probe remains world-ready after a running diagnostic file stops");active.Shutdown();
            }
            finally{if(prior==null)File.Delete(config);else File.WriteAllBytes(config,prior);}
        }
    }
}
