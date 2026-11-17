using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using Eleon.Modding;

namespace TradeLedgerProbeB
{
    public enum LedgerInventoryAvailability { Available, Partial, Unavailable, Unsupported }

    // Owned immutable values. Raw ID/ammo/decay are not a fabricated canonical variant key.
    public sealed class LedgerInventoryStack
    {
        public int Slot { get; private set; }
        public int RawItemId { get; private set; }
        public int Count { get; private set; }
        public int Ammo { get; private set; }
        public int Decay { get; private set; }
        public string PhysicalKey {get;private set;}
        internal LedgerInventoryStack(ItemStack s,string key=null) { Slot=s.slotIdx;RawItemId=s.id;Count=s.count;Ammo=s.ammo;Decay=s.decay;PhysicalKey=key; }
    }
    public sealed class LedgerInventoryContext
    {
        public string Context { get; private set; }
        public LedgerInventoryAvailability Availability { get; private set; }
        public string Source { get; private set; }
        public string Reason { get; private set; }
        public string Identity {get;private set;}
        // Null means not read. An available empty list means a successful observed empty section.
        public ReadOnlyCollection<LedgerInventoryStack> Stacks { get; private set; }
        internal LedgerInventoryContext(string context,LedgerInventoryAvailability availability,string source,string reason,IList<ItemStack> stacks=null,IList<string> keys=null,string identity=null)
        {
            Context=context;Availability=availability;Source=source;Reason=reason;Identity=identity;
            if(stacks!=null){var copy=new List<LedgerInventoryStack>();for(int i=0;i<stacks.Count;i++)copy.Add(new LedgerInventoryStack(stacks[i],keys==null?null:keys[i]));Stacks=copy.AsReadOnly();}
        }
    }
    public sealed class LedgerInventorySnapshot
    {
        private readonly LedgerInventoryLifetime lifetime;private readonly long revision;
        public string Session { get; private set; }
        public string PhysicalScope { get; private set; }
        public int? PlayerId { get; private set; }
        public ulong? GameTick { get; private set; }
        public LedgerInventoryContext Character { get; private set; }
        public LedgerInventoryContext Toolbar { get; private set; }
        public LedgerInventoryContext Backpack { get; private set; }
        public LedgerInventoryContext Open { get; private set; }
        public LedgerInventoryContext Linked { get; private set; }
        public LedgerInventoryContext ActiveToolbar {get;private set;}
        public LedgerInventoryContext Cursor {get;private set;}
        // A point-in-time read, never a cached live inventory. Next update/event/read invalidates it.
        public bool IsCurrent { get { return lifetime.Current(revision); } }
        internal LedgerInventorySnapshot(LedgerInventoryLifetime owner,long version,string scope,int? player,ulong? tick,LedgerInventoryContext toolbar,LedgerInventoryContext backpack=null,LedgerInventoryContext active=null,LedgerInventoryContext cursor=null,LedgerInventoryContext linked=null)
        {
            lifetime=owner;revision=version;Session=owner.Session;PhysicalScope=scope;PlayerId=player;GameTick=tick;Toolbar=toolbar;
            Backpack=backpack??new LedgerInventoryContext("backpack",LedgerInventoryAvailability.Unavailable,"local personal Domain backing slots","not-read");
            Character=new LedgerInventoryContext("character",toolbar.Availability==LedgerInventoryAvailability.Available||Backpack.Availability==LedgerInventoryAvailability.Available?LedgerInventoryAvailability.Partial:LedgerInventoryAvailability.Unavailable,"local toolbar and personal backpack sections","Partial character coverage; equipment/armor and linked containers are not included. Inspect section availability separately.");
            Open=new LedgerInventoryContext("open",LedgerInventoryAvailability.Unsupported,"IGui / WindowOpened","No certified current-container selector, visibility/access/payload gate; a window key is not an inventory identity.");
            ActiveToolbar=active??new LedgerInventoryContext("active toolbar",LedgerInventoryAvailability.Unavailable,"native active selector","not-read");
            Cursor=cursor??new LedgerInventoryContext("cursor",LedgerInventoryAvailability.Unavailable,"visible dragged item","not-read");
            Linked=linked??new LedgerInventoryContext("linked",LedgerInventoryAvailability.Unsupported,"native link controller","Only an admitted active linked toolbar is supported; whole linked-container contents are not read.");
        }
    }
    internal sealed class LedgerInventoryLifetime
    {
        private long revision;
        internal readonly string Session=Guid.NewGuid().ToString("N");
        internal long Invalidate(){return Interlocked.Increment(ref revision);}
        internal bool Current(long value){return Interlocked.Read(ref revision)==value;}
    }
    internal static class LedgerInventoryRead
    {
        internal static LedgerInventoryContext Toolbar(Func<IList<ItemStack>> read)
        { return Section("toolbar",9,read); }
        internal static LedgerInventoryContext Backpack(Func<IList<ItemStack>> read)
        { return Section("backpack",40,read); }
        private static LedgerInventoryContext Section(string context,int bound,Func<IList<ItemStack>> read)
        {
            try {
                var input=read();if(input==null||input.Count>bound)throw new InvalidOperationException(context+"-not-ready-or-invalid-size");
                var slots=new HashSet<byte>();var copy=new List<ItemStack>();
                foreach(var s in input){if(s.slotIdx>=bound||!slots.Add(s.slotIdx)||s.id<=0||s.count<=0||s.ammo<0||s.decay<0)throw new InvalidOperationException("invalid-"+context+"-slot");copy.Add(s);}
                return new LedgerInventoryContext(context,LedgerInventoryAvailability.Available,"5188 local "+context+" backing slots; field reads only","",copy);
            }catch(Exception e){return new LedgerInventoryContext(context,LedgerInventoryAvailability.Unavailable,"5188 local "+context+" backing slots",e is InvalidOperationException?e.Message:"read-failed:"+e.GetType().Name);}
        }
    }
    // No native getters, methods, setters, enumeration, hooks or subscriptions. Exact-build field copy.
    internal sealed partial class LedgerInventoryNative
    {
        private readonly Module module;
        private readonly Dictionary<int,FieldInfo> fields=new Dictionary<int,FieldInfo>();
        internal LedgerInventoryNative(Assembly game)
        {
            LedgerNativeContracts.Require(game,"inventory");
            module=game.ManifestModule;
            Bind(0xa810,"Eleon.ModBridge.PlayerBridge","Assembly-CSharp.NetworkFactory");
            Bind(0x1c02,"Assembly-CSharp.MemoryHelper","Assembly-CSharp.MethodList");
            Bind(0x2fad,"Assembly-CSharp.MethodList","Assembly-CSharp.WindowEditor[]");
            Bind(0x2fd3,"Assembly-CSharp.WindowEditor","Assembly-CSharp.PaneFactory");
            Bind(0x2fdf,"Assembly-CSharp.PaneFactory","Assembly-CSharp.OptionsSerializer");
            Bind(0x2fe0,"Assembly-CSharp.PaneFactory","Assembly-CSharp.ComponentContextConnectionToken");
            Bind(0x2fe1,"Assembly-CSharp.PaneFactory","System.Int32");
            Bind(0xa19b,"Assembly-CSharp.ComponentContextConnectionToken","System.Int32");
            Bind(0x2fe9,"Assembly-CSharp.OptionsSerializer","System.Int32");
            Bind(0x2fec,"Assembly-CSharp.OptionsSerializer","System.Int32");
            Bind(0x2fea,"Assembly-CSharp.OptionsSerializer","System.Int32");
            Bind(0x2045,"Assembly-CSharp.SolutionLayout","Assembly-CSharp.OptionsInvoker");
            Bind(0x5288,"Assembly-CSharp.ActivatorSettings","Assembly-CSharp.BookmarkInvoker");
            Bind(0x52bb,"Assembly-CSharp.BookmarkInvoker","Assembly-CSharp.PaneFactory[]");
            BindAccess();
        }
        private void Bind(int token,string owner,string type)
        {
            var f=LedgerNativeContracts.Field(module,0x04000000+token);if(f.IsStatic||f.DeclaringType.FullName!=owner||f.FieldType.FullName!=type)throw new InvalidOperationException("inventory-field-contract");fields.Add(token,f);
        }
        private object F(int token,object value){if(value==null)throw new InvalidOperationException("toolbar-not-ready");return fields[token].GetValue(value);}
        internal IList<ItemStack> Toolbar(IPlayer player)
        {
            if(player==null||player.GetType().FullName!="Eleon.ModBridge.PlayerBridge")throw new InvalidOperationException("unsupported-local-player-wrapper");
            var slots=F(0x2fad,F(0x1c02,F(0xa810,player))) as Array;
            return ReadSlots(slots,9,true);
        }
        internal IList<ItemStack> Backpack(IPlayer player)
        {
            if(player==null||player.GetType().FullName!="Eleon.ModBridge.PlayerBridge")throw new InvalidOperationException("unsupported-local-player-wrapper");
            object local=F(0xa810,player);
            if(local.GetType().FullName!="Assembly-CSharp.SolutionLayout")throw new InvalidOperationException("personal-backpack-local-owner-unverified");
            object personal=F(0x2045,local);
            if(personal==null||personal.GetType().FullName!="Assembly-CSharp.OptionsInvoker")throw new InvalidOperationException("personal-backpack-not-ready");
            // 526F is the owned personal input Domain. 5274/5275 are selected sources and may differ.
            // Never call get_SplitMemory/CleanFile or walk selected/linked remote containers.
            object domain=F(0x5288,personal);var slots=F(0x52bb,domain) as Array;
            var copy=ReadSlots(slots,40,false);
            if(!ReferenceEquals(personal,F(0x2045,local))||!ReferenceEquals(domain,F(0x5288,personal))||!ReferenceEquals(slots,F(0x52bb,domain)))throw new InvalidOperationException("personal-backpack-changed-during-read");
            return copy;
        }
        private int ReadCount(object slot){return LedgerInventoryCount.Read(slot,F);}
        private IList<ItemStack> ReadSlots(Array slots,int bound,bool wrapped)
        {
            if(slots==null||slots.Length!=(wrapped?30:bound))throw new InvalidOperationException("personal-slot-array-not-ready-or-invalid-size");
            var result=new List<ItemStack>();
            for(int i=0;i<bound;i++){
                var slot=wrapped?F(0x2fd3,slots.GetValue(i)):slots.GetValue(i);
                // Native get_FindView can repair this count and emit a command. Never invoke it.
                int count=ReadCount(slot);if(count==0)continue;
                var item=F(0x2fdf,slot);result.Add(new ItemStack{id=(int)F(0x2fe9,item),count=count,slotIdx=(byte)i,ammo=(int)F(0x2fec,item),decay=(int)F(0x2fea,item)});
            }
            return result;
        }
    }
    internal static class LedgerInventoryCount
    {
        internal static bool Agrees(int cached,int encoded)
        {return cached>=0&&((encoded^1322077852)/43-2357)==cached;}
        internal static int Read(object slot,Func<int,object,object> field)
        {
            if(slot==null)throw new InvalidOperationException("slot-not-initialized");
            int count=(int)field(0x2fe1,slot);object companion=field(0x2fe0,slot);
            if(count<0)throw new InvalidOperationException("invalid-cached-count");
            // Native getter060033A5 may repair count/emit a command. Never call it.
            // Normal native slots have an owned companion (ctor0600AD51). Its pure
            // field expression0600AD56 must agree with the already-cached count.
            if(companion!=null){
                int encoded=(int)field(0xa19b,companion);
                if(!ReferenceEquals(field(0xa197,companion),slot)||!LedgerInventoryCount.Agrees(count,encoded))throw new InvalidOperationException("count-companion-owner-or-value-mismatch");
                if(encoded!=(int)field(0xa19b,companion))throw new InvalidOperationException("count-changing");
            }
            if(count!=(int)field(0x2fe1,slot)||!ReferenceEquals(companion,field(0x2fe0,slot)))throw new InvalidOperationException("count-changing");
            return count;
        }
    }
    public sealed partial class Probe
    {
        private readonly LedgerInventoryLifetime inventoryLifetime=new LedgerInventoryLifetime();
        private LedgerInventoryNative inventorySource;
        private bool inventoryUnloading;
        // Pull API; the request tracker samples this on the API thread at a bounded cadence.
        public LedgerInventorySnapshot ReadInventorySnapshot()
        {
            lock(gate){
                long revision=inventoryLifetime.Invalidate();string scope=null;int? id=null;ulong? tick=null;
                LedgerInventoryContext toolbar,backpack=null,active=null,cursor=null,linked=null;
                if(!OnApiThread||!initialized||stopped||worldLeft||inventoryUnloading||application==null||api==null)
                    toolbar=new LedgerInventoryContext("toolbar",LedgerInventoryAvailability.Unavailable,"API-thread gate","not-ready-or-wrong-thread");
                else try{
                    if(application.State.ToString()!="Running"||application.Mode.ToString()!="SinglePlayer")throw new InvalidOperationException("running-singleplayer-required");
                    var pf=api.ClientPlayfield;var c=LedgerContext.Create(application.GetPathFor(AppFolder.SaveGame),pf==null?null:pf.Name,null,"SinglePlayer");
                    if(!c.Recordable)throw new InvalidOperationException("physical-save-playfield-unavailable");
                    var p=application.LocalPlayer;if(p==null||!p.IsLocal||p.Id<0)throw new InvalidOperationException("local-player-unavailable");
                    scope=c.LocalSaveKey+"|"+c.Playfield;id=p.Id;tick=application.GameTicks;
                    toolbar=LedgerInventoryRead.Toolbar(()=>{if(inventorySource==null)inventorySource=new LedgerInventoryNative(application.GetType().Assembly);return inventorySource.Toolbar(p);});
                    if(inventorySource!=null)inventorySource.Access(p,ref backpack,out active,out cursor,out linked);
                    if(inventoryUnloading||worldLeft||application.State.ToString()!="Running"||application.Mode.ToString()!="SinglePlayer"||!ReferenceEquals(p,application.LocalPlayer)||!LedgerContext.MatchesCurrentSave(c,application.GetPathFor(AppFolder.SaveGame))||!ReferenceEquals(pf,api.ClientPlayfield)||application.GameTicks!=tick.Value)
                        throw new InvalidOperationException("inventory-context-changed-during-read");
                }catch(Exception e){toolbar=new LedgerInventoryContext("toolbar",LedgerInventoryAvailability.Unavailable,"local character gate","read-unavailable:"+e.GetType().Name+":"+e.Message);backpack=active=cursor=linked=null;scope=null;id=null;tick=null;}
                return new LedgerInventorySnapshot(inventoryLifetime,revision,scope,id,tick,toolbar,backpack,active,cursor,linked);
            }
        }
    }
}
