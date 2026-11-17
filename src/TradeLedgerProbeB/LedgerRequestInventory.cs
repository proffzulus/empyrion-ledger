using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    // Only intent/retirement is portable. Inventory counts and matching results are never persisted.
    public sealed class LedgerRequestInventoryIntent
    {public string World,RequestKey,CompletionUtc;public int Player;public bool Activated,Removed,Done;}
    public sealed class LedgerRequestInventoryItem
    {
        public int? Item;public string Name;public long? Needed,Backpack,Carry;public bool CarryComplete;
        public string Text {get{return Name+" "+N(Backpack)+"/"+N(Needed)+" pack; "+N(Carry)+(Carry.HasValue&&!CarryComplete?"+":"")+"/"+N(Needed)+" carry";}}
        internal static string N(long? n){return n.HasValue?n.Value.ToString(CultureInfo.InvariantCulture):"?";}
    }
    public sealed class LedgerRequestInventoryBranch
    {public readonly List<LedgerRequestInventoryItem> Items=new List<LedgerRequestInventoryItem>();public bool Ready,Prepared,Partial;}
    public sealed class LedgerRequestInventoryProgress
    {
        public readonly List<LedgerRequestInventoryBranch> Branches=new List<LedgerRequestInventoryBranch>();
        public bool Ready,Prepared,Partial,Ambiguous,Unknown;public string Reason;
        public int Rank {get{return Ready?3:Prepared?2:Partial?1:0;}}
        public string Status {get{return Ambiguous?"Requirements uncertain":Ready?"Backpack ready":Prepared?"Carry ready - move to backpack":Partial?"Partial stock":Unknown?"Stock unknown":"Stock unmatched";}}
        public string Text {get{return Status+"\n"+string.Join("\nOR ",Branches.Select(b=>string.Join("  |  ",b.Items.Select(i=>i.Text))));}}
        public string Marker
        {
            get{
                var b=Branches.OrderByDescending(x=>x.Ready).ThenByDescending(x=>x.Prepared).ThenByDescending(x=>x.Partial).FirstOrDefault();
                if(b==null)return "Delivery: check cargo";
                string text="Delivery: "+string.Join(", ",b.Items.Take(3).Select(i=>(Ready?LedgerRequestInventoryItem.N(i.Needed):LedgerRequestInventoryItem.N(i.Carry)+(i.Carry.HasValue&&!i.CarryComplete?"+":"")+"/"+LedgerRequestInventoryItem.N(i.Needed))+" "+i.Name));
                if(b.Items.Count>3)text+=", +"+(b.Items.Count-3)+" items";
                text+=Ambiguous?" (check requirements)":Ready?" (ready)":Prepared?" (move to pack)":" (check cargo)";
                if(Branches.Count>1)text+=" [option]";
                return text.Length<=240?text:"Delivery: "+(Branches.Count>1?"alternative items":"multiple items")+" ("+(Ready?"ready":Prepared?"move to pack":"check cargo")+")";
            }
        }
    }
    public sealed class LedgerRequestStock
    {
        public readonly Dictionary<int,long> Backpack=new Dictionary<int,long>(),Carry=new Dictionary<int,long>();
        public readonly Dictionary<string,long> Variants=new Dictionary<string,long>();
        public bool BackpackKnown,CarryComplete,Current;public string Reason,Coverage,PhysicalScope,Diagnostics;public int? Player;
        public static LedgerRequestStock Unknown(string reason){return new LedgerRequestStock{Reason=reason,Coverage=reason};}
        private static bool Available(LedgerInventoryContext c){return c!=null&&c.Availability==LedgerInventoryAvailability.Available&&c.Stacks!=null;}
        public static LedgerRequestStock Read(LedgerInventorySnapshot s)
        {
            if(s==null||!s.IsCurrent||s.PhysicalScope==null||!(s.PlayerId>0))return Unavailable(s,"Inventory unavailable");
            if(!Available(s.Cursor))return Unavailable(s,"Cursor source/count unavailable; matching paused");
            var result=new LedgerRequestStock{Current=true,PhysicalScope=s.PhysicalScope,Player=s.PlayerId,BackpackKnown=Available(s.Backpack),CarryComplete=Available(s.Backpack)&&Available(s.ActiveToolbar)};
            var all=new Dictionary<string,LedgerInventoryStack>();var bag=new HashSet<string>();
            var cursorKeys=new HashSet<string>(s.Cursor.Stacks.Select(x=>x.PhysicalKey));
            foreach(var c in new[]{s.Backpack,s.ActiveToolbar,s.Cursor}){
                if(!Available(c))continue;
                foreach(var stack in c.Stacks){
                    if(ReferenceEquals(c,s.Backpack)&&cursorKeys.Contains(stack.PhysicalKey))return Unknown("Cursor/backpack ownership overlaps; matching paused");
                    if(string.IsNullOrEmpty(stack.PhysicalKey)||stack.RawItemId<=0||stack.Count<=0||stack.Ammo<0||stack.Decay<0)return Unknown("Inventory stack identity unavailable");
                    LedgerInventoryStack prior;
                    if(all.TryGetValue(stack.PhysicalKey,out prior)){
                        if(prior.RawItemId!=stack.RawItemId||prior.Count!=stack.Count||prior.Ammo!=stack.Ammo||prior.Decay!=stack.Decay)return Unknown("Inventory transfer is unsettled");
                    }else all.Add(stack.PhysicalKey,stack);
                    if(ReferenceEquals(c,s.Backpack))bag.Add(stack.PhysicalKey);
                }
            }
            foreach(var pair in all){long n;string variant=pair.Value.RawItemId+":"+pair.Value.Ammo;result.Variants.TryGetValue(variant,out n);result.Variants[variant]=checked(n+pair.Value.Count);Add(result.Carry,pair.Value.RawItemId,pair.Value.Count);if(bag.Contains(pair.Key))Add(result.Backpack,pair.Value.RawItemId,pair.Value.Count);}
            result.Coverage=(result.BackpackKnown?"Backpack":"Backpack unavailable")+"; "+(Available(s.ActiveToolbar)?s.ActiveToolbar.Identity!=null&&s.ActiveToolbar.Identity.StartsWith("linked-",StringComparison.Ordinal)?"active linked toolbar":"active toolbar":"active toolbar unavailable")+"; cursor";
            result.Diagnostics=string.Join("\n",new[]{s.Backpack,s.ActiveToolbar,s.Cursor}.Where(c=>c!=null).Select(c=>c.Context+": "+c.Availability+(string.IsNullOrEmpty(c.Reason)?"":" - "+c.Reason)));result.Reason=result.Coverage;return result;
        }
        private static LedgerRequestStock Unavailable(LedgerInventorySnapshot s,string reason)
        {var result=Unknown(reason);result.Diagnostics=s==null?reason:string.Join("\n",new[]{s.Toolbar,s.Backpack,s.ActiveToolbar,s.Cursor}.Where(c=>c!=null).Select(c=>c.Context+": "+c.Availability+(string.IsNullOrEmpty(c.Reason)?"":" - "+c.Reason)));return result;}
        private static void Add(Dictionary<int,long> values,int item,long n){long prior;values.TryGetValue(item,out prior);values[item]=checked(prior+n);}
        public long? Variant(int id,int? auxiliary){long n;if(!Current||id<=0||!auxiliary.HasValue||auxiliary<0)return null;return Variants.TryGetValue(id+":"+auxiliary,out n)?n>=0?(long?)n:null:0;}
        public long? Bag(int id){long n;return !BackpackKnown?(long?)null:Backpack.TryGetValue(id,out n)?n:0;}
        public long? Total(int id){long n;return !Current?(long?)null:Carry.TryGetValue(id,out n)?n:0;}
    }
    // Two coherent independent pulls, not two references to an old cached snapshot.
    public sealed class LedgerRequestInventorySampler
    {
        private string prior,scope,session;private ulong? tick;private int? player;
        public void Reset(){prior=scope=session=null;tick=null;player=null;}
        public LedgerRequestStock Accept(LedgerInventorySnapshot s)
        {
            if(s==null||!s.IsCurrent||!s.GameTick.HasValue){Reset();return LedgerRequestStock.Unknown("Inventory unavailable");}
            var stock=LedgerRequestStock.Read(s);
            string signature=string.Join("|",new[]{s.Backpack,s.ActiveToolbar,s.Cursor}.Select(c=>c==null?"null":c.Availability+":"+c.Identity+":"+c.Reason+":"+(c.Stacks==null?"?":string.Join(",",c.Stacks.Select(i=>i.PhysicalKey+":"+i.RawItemId+":"+i.Ammo+":"+i.Decay+":"+i.Count)))));
            bool same=prior==signature&&scope==s.PhysicalScope&&session==s.Session&&player==s.PlayerId&&s.GameTick>=tick;
            prior=signature;scope=s.PhysicalScope;session=s.Session;player=s.PlayerId;tick=s.GameTick;
            if(!stock.Current)return stock;
            return same?stock:LedgerRequestStock.Unknown("Inventory settling");
        }
    }
    public static class LedgerRequestInventory
    {
        public static LedgerRequestInventoryProgress Match(LedgerRequestFact fact,LedgerRequestStock stock)
        {
            stock=stock??LedgerRequestStock.Unknown("Inventory unavailable");
            var p=new LedgerRequestInventoryProgress{Reason=stock.Reason,Unknown=!stock.Current||!stock.BackpackKnown||!stock.CarryComplete};
            if(fact==null||fact.Items==null||fact.Items.Count==0||fact.Items.Count>64){p.Ambiguous=true;return p;}
            p.Ambiguous=fact.Truncated||!string.IsNullOrEmpty(fact.Conflict)||fact.Relation=="mixed-unknown"||fact.Relation!=null&&fact.Relation!="all"&&fact.Relation!="any";
            var branches=new List<int[]>();var alternatives=fact.Alternatives??new List<LedgerRequestAlternative>();
            if(alternatives.Count>0){
                if(alternatives.Count>16||alternatives.Any(a=>a==null||a.Inputs==null||a.Inputs.Length==0||a.Inputs.Any(i=>i<0||i>=fact.Items.Count))){p.Ambiguous=true;branches.Add(Enumerable.Range(0,fact.Items.Count).ToArray());}
                else {var common=Enumerable.Range(0,fact.Items.Count).Except(alternatives.SelectMany(a=>a.Inputs)).ToArray();branches.AddRange(alternatives.Select(a=>a.Inputs.Concat(common).Distinct().ToArray()));}
            }else if(fact.Relation=="any")branches.AddRange(Enumerable.Range(0,fact.Items.Count).Select(i=>new[]{i}));
            else branches.Add(Enumerable.Range(0,fact.Items.Count).ToArray());
            foreach(var indexes in branches){
                var b=new LedgerRequestInventoryBranch{Ready=stock.Current&&stock.BackpackKnown&&!p.Ambiguous,Prepared=stock.Current&&!p.Ambiguous};
                // Repeated IDs within ONE branch consume their summed amount. Separate requests do not reserve stock.
                foreach(var group in indexes.Select(i=>Tuple.Create(i,fact.Items[i])).GroupBy(x=>x.Item2!=null&&x.Item2.ItemId>0?"id:"+x.Item2.ItemId:"unknown:"+x.Item1)){
                    var first=group.First().Item2;int? id=first==null?null:first.ItemId;long? need=null;
                    if(group.All(x=>x.Item2!=null&&x.Item2.Quantity>0&&x.Item2.Quantity<=1000000000)){long total=group.Sum(x=>x.Item2.Quantity.Value);if(total<=1000000000)need=total;}
                    var item=new LedgerRequestInventoryItem{Item=id,Name=LedgerPresentation.Text(first==null?"item unknown":first.Name??"item unknown"),Needed=need,Backpack=id>0?stock.Bag(id.Value):null,Carry=id>0?stock.Total(id.Value):null,CarryComplete=stock.CarryComplete};
                    b.Items.Add(item);b.Ready&=id>0&&need.HasValue&&item.Backpack>=need;b.Prepared&=id>0&&need.HasValue&&item.Carry>=need;
                    b.Partial|=id>0&&(item.Backpack>0||item.Carry>0);
                }
                p.Branches.Add(b);p.Ready|=b.Ready;p.Prepared|=b.Prepared;p.Partial|=b.Partial;
            }
            return p;
        }
        public static LedgerRequestInventoryIntent Intent(LedgerMarkerModel m,string world,int player,string key,bool create=false)
        {
            var state=m.Data.RequestInventory.FirstOrDefault(s=>s.World==world&&s.Player==player&&s.RequestKey==key);
            if(state==null&&create){if(m.Data.RequestInventory.Count>=16384)throw new InvalidOperationException("Request tracking intent limit reached");state=new LedgerRequestInventoryIntent{World=world,Player=player,RequestKey=key};m.Data.RequestInventory.Add(state);m.Revision++;}return state;
        }
        public static void Committed(LedgerMarkerModel m,LedgerManualCompletion command)
        {
            if(command==null||command.IsTracking||command.IsArchive||!command.StoredCompleted.HasValue)return;
            Completion(m,Intent(m,command.World,command.PlayerId,command.RequestKey,true),command.StoredCompleted.Value,command.Utc);
        }
        private static void Completion(LedgerMarkerModel m,LedgerRequestInventoryIntent s,bool done,string utc)
        {
            if(string.IsNullOrEmpty(utc)||s.CompletionUtc!=null&&string.CompareOrdinal(utc,s.CompletionUtc)<0)return;
            if(s.Done!=done||s.CompletionUtc!=utc){s.Done=done;s.CompletionUtc=utc;if(done)s.Activated=false;m.Revision++;}
        }
        public static void Remove(LedgerMarkerModel m,LedgerMarkerDestination d,LedgerMarkerPurpose p)
        {
            if(p.Kind!="request")return;
            string key=p.Plan.RequestKey??(p.Plan.RequestBody==null?null:LedgerManualCompletion.KeyFor(LedgerRequestMarkers.Endpoint(p.Plan),p.Plan.RequestBody));if(key==null)return;
            var s=Intent(m,d.World,d.Player,key,true);if(!s.Removed||s.Activated){s.Removed=true;s.Activated=false;m.Revision++;}p.InventoryAutoVisible=false;
        }
        public static void Resume(LedgerMarkerModel m,LedgerBookmarkPlan plan,int player)
        {var s=Intent(m,plan.World,player,plan.RequestKey);if(s!=null&&s.Removed){s.Removed=false;m.Revision++;}}
        public static void Suspend(LedgerMarkerModel m){foreach(var p in m.Data.Destinations.SelectMany(d=>d.Purposes)){p.InventoryAutoVisible=false;p.InventoryLabel=null;}}
        public static Dictionary<string,LedgerRequestInventoryProgress> Evaluate(LedgerMarkerModel m,LedgerViewData view,LedgerContext c,int player,LedgerRequestStock stock,LedgerProductionPlaces places=null)
        {
            Suspend(m);var result=new Dictionary<string,LedgerRequestInventoryProgress>();
            if(view==null||c==null||!c.Recordable||view.WorldKey!=c.WorldKey||view.ManualPlayerId!=player||!view.ManualStateReady||view.ManualReadError!=null)return result;
            if(stock!=null&&stock.Current&&(stock.PhysicalScope!=(c.LocalSaveKey??c.WorldKey)+"|"+c.Playfield||stock.Player!=player))stock=LedgerRequestStock.Unknown("Inventory scope changed");
            foreach(var card in view.RequestCards().Where(x=>x.Request!=null)){
                string key=LedgerRequestBrowser.StableKey(card);var state=Intent(m,c.WorldKey,player,key);
                bool done;string stamp;
                if(state!=null&&view.ManualCompletions.TryGetValue(key,out done)&&view.CompletionStamps.TryGetValue(key,out stamp))Completion(m,state,done,stamp);
                if(view.ManuallyCompleted(card)||state!=null&&state.Done)continue;
                var progress=Match(card.Request.Fact,stock);result[key]=progress;
                LedgerBookmarkPlan plan;string reason;
                if(!LedgerBookmarkPolicy.TryRequest(view,card.Source,c,out plan,out reason,card.Request.Fact)){progress.Reason+="; "+reason;continue;}
                var d=m.Find(plan,player);var purpose=d==null?null:d.Purposes.FirstOrDefault(p=>p.Plan.RequestKey==key);
                if(purpose!=null&&purpose.RequestCompleted)continue;
                if(state!=null&&state.Removed)continue;
                if(progress.Ready||progress.Prepared){state=state??Intent(m,c.WorldKey,player,key,true);if(!state.Activated){state.Activated=true;m.Revision++;}}
                if(state==null||!state.Activated)continue;
                try{
                    if(d==null||purpose==null){d=m.Ensure(plan,player,false);purpose=d.Purposes.First(p=>p.Key==LedgerMarkerModel.PurposeKey(plan));purpose.Visibility=-1;purpose.UngroupedShown=false;}
                    if(d.DeleteRequested){Remove(m,d,purpose);continue;}
                    purpose.InventoryLabel=progress.Marker;
                    var hierarchy=places??new LedgerProductionPlaces(c.WorldKey,new[]{card.Source.Context});
                    purpose.InventoryAutoVisible=!LedgerTradeUpdates.Suppressed(m,d,purpose,hierarchy);
                }catch(InvalidOperationException e){progress.Reason+="; "+e.Message;}
            }
            return result;
        }
        public static void Validate(LedgerMarkerDocument doc)
        {
            if(doc.RequestInventory==null)doc.RequestInventory=new List<LedgerRequestInventoryIntent>();
            if(doc.RequestInventory.Count>16384||doc.RequestInventory.Any(s=>s==null||string.IsNullOrEmpty(s.World)||s.World.Length>256||s.Player<=0||string.IsNullOrEmpty(s.RequestKey)||s.RequestKey.Length>256||(s.CompletionUtc??"").Length>64)||doc.RequestInventory.Select(s=>s.World+"|"+s.Player+"|"+s.RequestKey).Distinct().Count()!=doc.RequestInventory.Count)throw new InvalidOperationException("Invalid request inventory intents; existing file preserved");
        }
    }
}
