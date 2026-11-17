using System;

namespace TradeLedgerProbeB
{
    // Menu presentation may pause Running/capture readiness. Keep only an explicit,
    // short-lived user intent; opening still waits for the ordinary lifecycle gates.
    public sealed class LedgerMenuHandoff
    {
        private string scope;private long until;
        public void Request(string current,long now){scope=current;until=now+2500;}
        public bool Pending(string current,long now){return scope!=null&&scope==current&&now<=until;}
        public bool Take(string current,bool running,long now){if(current==null||current!=scope||now>until){scope=null;return false;}if(!running||scope==null)return false;scope=null;return true;}
        public void Cancel(){scope=null;}
    }
    // User/application UI preference, not campaign knowledge or a persisted display index.
    public sealed class LedgerTabPreference
    {
        public const string StorageKey="TradeLedger.Ui.LastTab.v1",DefaultId="markets";
        private static readonly string[] ids={DefaultId,"requests","locations","comparison","production","item-browser","markers","places"};
        private readonly Action<string> persist;
        public string Id {get;private set;}
        public int Index {get{return Array.IndexOf(ids,Id);}}
        public LedgerTabPreference(Func<string> read=null,Action<string> write=null)
        {
            Id=DefaultId;persist=write;
            try{string stored=read==null?null:read();if(Array.IndexOf(ids,stored)>=0)Id=stored;}catch(Exception){/* Optional UI preference; keep a usable default. */}
        }
        public void Select(int index)
        {
            string next=index>=0&&index<ids.Length?ids[index]:DefaultId;
            if(next==Id)return;Id=next;
            try{if(persist!=null)persist(Id);}catch(Exception){/* Retain this session's selection even if preferences are unavailable. */}
        }
    }
    public sealed class LedgerJournalFrame
    {
        public bool Focused,Ready,Scoped,Present,Top,Empty,WorldVisible,Typing,ShortcutHeld,EscapeHeld,SwitchableNative,ForeignNative;
        public long Now;
    }
    public sealed class LedgerJournalAction
    {
        public bool OpenNative,CloseNative,Interrupted,CloseForeign;
        public bool OpenAttempt;
        public string ClosedReason,OpenRejected;
    }
    // One physical reader at a time, even across close/reopen and scope replacement.
    // A cancelled reader must settle, but cannot publish into the next generation.
    public sealed class LedgerViewReadWork
    {
        public bool Busy {get;private set;}
        private long serial,active;private int generation;
        public bool Begin(int epoch,out long ticket){ticket=0;if(Busy)return false;Busy=true;generation=epoch;ticket=active=++serial;return true;}
        public bool Complete(long ticket,int epoch){if(!Busy||ticket!=active)return false;Busy=false;return generation==epoch;}
    }
    // The actual view and interrupted-flow harness use this same state machine.
    // It owns no cursor/OS focus, game stack, database or Unity object.
    public sealed class LedgerJournalLifecycle
    {
        public const int ShortcutCode=93; // Unity KeyCode.RightBracket, not an Event.character comparison.
        public const string ShortcutLabel="]";
        public static bool IsTyping(bool eventSystemReady,bool nativeBlock,bool nguiActive,bool uguiFocused,bool tmpFocused,bool debugFocused)
        {return !eventSystemReady||nativeBlock||nguiActive||uguiFocused||tmpFocused||debugFocused;}
        public bool Open,CloseRequested,EscapePending,Retiring;
        private bool releaseRequired=true,previousHeld,focusLost;
        private long retryAt;
        private long clickUntil;
        public void RequestOpen(long now){clickUntil=now+2500;}
        public void FocusLost(){focusLost=true;clickUntil=0;releaseRequired=true;previousHeld=false;}
        public void Fault(long now){clickUntil=0;Open=false;CloseRequested=EscapePending=false;Retiring=true;releaseRequired=true;retryAt=now+1000;}
        public void Opened(){clickUntil=0;Open=true;CloseRequested=EscapePending=false;Retiring=false;}
        public LedgerJournalAction Step(LedgerJournalFrame f)
        {
            bool wasOpen=Open;var a=StepCore(f);
            if(!wasOpen&&a.OpenAttempt&&!a.OpenNative&&!a.CloseForeign)
                a.OpenRejected=Retiring?"owned-modal-retiring":!f.Focused?"focus":!f.Ready?"world-not-ready":!f.Scoped?"save-scope":f.Typing?"typing":f.ForeignNative?"foreign-native-window":!f.Empty?"native-stack-or-fade":!f.WorldVisible?"world-not-visible":f.Now<retryAt?"recovery-cooldown":"key-release-required";
            return a;
        }
        private LedgerJournalAction StepCore(LedgerJournalFrame f)
        {
            var result=new LedgerJournalAction();
            bool pressed=false;
            if(!f.Focused){releaseRequired=true;previousHeld=false;}
            else if(releaseRequired){if(!f.ShortcutHeld){releaseRequired=false;previousHeld=false;}}
            else {pressed=f.ShortcutHeld&&!previousHeld;previousHeld=f.ShortcutHeld;}
            result.OpenAttempt=pressed||clickUntil>0&&f.Now<=clickUntil;
            if(Open&&(focusLost||!f.Focused||!f.Ready||!f.Scoped||!f.Present||!f.Top))
            {
                result.ClosedReason=focusLost||!f.Focused?"focus-lost":!f.Ready||!f.Scoped?"world-context-unavailable":!f.Present?"native-dismissed":"native-panel-superseded";
                result.Interrupted=true;Open=false;CloseRequested=EscapePending=false;Retiring=f.Present;
            }
            focusLost=false;
            if(Retiring)
            {
                // Never pop a newer native panel. Retire only our exact modal when it resurfaces.
                if(f.Present){result.CloseNative=f.Top;return result;}
                Retiring=false;
                return result; // A close/recovery frame cannot also reopen.
            }
            if(result.ClosedReason!=null)return result;
            if(Open)
            {
                if(f.EscapeHeld){CloseRequested=true;EscapePending=true;}
                if(LedgerWindowPolicy.CanToggle(pressed,f.Typing,0)){CloseRequested=true;EscapePending=false;}
                result.CloseNative=LedgerBrowse.CloseAfterKey(f.Top,CloseRequested,EscapePending&&f.EscapeHeld);
                return result;
            }
            bool clicked=clickUntil>0&&f.Now<=clickUntil;
            if(f.Now>clickUntil||!f.Focused||!f.Ready||!f.Scoped||f.Typing||f.ForeignNative)clickUntil=0;
            if(clickUntil==0)clicked=false;
            if(pressed&&!f.Typing&&f.Focused&&f.Ready&&f.Scoped&&f.Now>=retryAt&&f.SwitchableNative){RequestOpen(f.Now);result.CloseForeign=true;return result;}
            result.OpenNative=(clicked||LedgerWindowPolicy.CanToggle(pressed,f.Typing,0))&&!f.Typing&&f.Focused&&f.Now>=retryAt&&LedgerWindowPolicy.CanOpen(f.Ready,f.Scoped,f.Empty,f.WorldVisible);
            if(result.OpenNative)clickUntil=0;
            return result;
        }
    }
    public sealed class LedgerOwnedGuiFocus
    {
        private int keyboard,hot;
        private bool pending;
        public void Remember(int ownKeyboard,int ownHot){keyboard=ownKeyboard;hot=ownHot;}
        public void RequestClear(){pending=true;}
        public bool ClearKeyboard(int current){return pending&&keyboard!=0&&current==keyboard;}
        public bool ClearHot(int current){return pending&&hot!=0&&current==hot;}
        public void Cleared(){pending=false;keyboard=hot=0;}
    }
}
