using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeLedgerProbeB
{
    public static class LedgerHandInActorGate
    {
        public static bool Admit(bool visible,bool recentlyClosed,bool sameActor,bool missingActor)
        {return sameActor&&(visible||recentlyClosed)||recentlyClosed&&missingActor;}
    }
    public sealed class LedgerHandInAmounts
    {
        public int Item,Total;
        public bool Invalid;
        private readonly HashSet<int> containers=new HashSet<int>();
        public void Add(int container,int item,int requested,int meta,int actual,int expectedMeta,int maximum)
        {
            if(!containers.Add(container)||container<0||container>1||item<=0||requested<0||actual<0||actual>requested||meta!=expectedMeta||Item!=0&&Item!=item){Invalid=true;return;}
            Item=item;long sum=(long)Total+actual;if(sum>maximum||sum>1000000000){Invalid=true;return;}Total=(int)sum;
        }
    }
    public interface ILedgerHandInHost
    {
        void HandInClosed(string interaction);
        void HandInCancelled(string reason);
        void HandInRemoved(string interaction,int npc,int item,int meta,int removed,string evidence);
    }

    // One most-recent shown request in one local dialogue, never a saved-request search.
    public sealed class LedgerHandInCorrelation
    {
        private LedgerObservation shown;
        private LedgerRequestFact fact;
        private int player;
        private long closedAt=-1;
        private bool consumed;
        private readonly Dictionary<int,long> removed=new Dictionary<int,long>();
        private readonly HashSet<string> events=new HashSet<string>();
        public bool HasShown {get{return shown!=null;}}
        public void Reset(){shown=null;fact=null;removed.Clear();events.Clear();closedAt=-1;consumed=false;}
        public void Shown(LedgerObservation source,LedgerRequestFact request,int localPlayer)
        {
            if(source==null||source.Kind!="dialogue-shown"||source.Context==null||!source.Context.Recordable||source.Identity==null||!(source.Identity.NpcEntityId>0)||source.Identity.OwnerConsistent==false||localPlayer<=0){Reset();return;}
            if(shown!=null&&(source.Session!=shown.Session||source.Interaction!=shown.Interaction||LedgerDatabase.Key(source)!=LedgerDatabase.Key(shown)||source.Identity.StructureEntityId!=shown.Identity.StructureEntityId||source.Identity.BlockGrid!=shown.Identity.BlockGrid))Reset();
            if(source.Dialogue==null||source.Dialogue.BodyFullyRevealed!=true)return;
            if(request==null)return; // An acknowledgment/greeting never supplies new requirements.
            if(shown!=null&&LedgerManualCompletion.Key(shown)==LedgerManualCompletion.Key(source))return;
            Reset();shown=LedgerJson.Decode<LedgerObservation>(LedgerJson.Encode(source));fact=LedgerJson.Decode<LedgerRequestFact>(LedgerJson.Encode(request));player=localPlayer;
        }
        public bool Current(LedgerContext context,int localPlayer)
        {return shown!=null&&player==localPlayer&&context!=null&&context.Recordable&&shown.Context.WorldKey==context.WorldKey&&shown.Context.LocalSaveKey==context.LocalSaveKey&&shown.Context.Playfield==context.Playfield;}
        public void Close(string interaction,long now){if(shown!=null&&shown.Interaction==interaction&&closedAt<0)closedAt=now;}
        public LedgerManualCompletion Removal(string interaction,int npc,int item,int meta,int count,string evidence,long now,out string reason)
        {
            reason="no-shown-request";
            if(shown==null)return null;
            if(consumed){reason="already-consumed";return null;}
            if(interaction!=shown.Interaction||npc!=shown.Identity.NpcEntityId){reason="different-npc-or-interaction";return null;}
            if(closedAt>=0&&(now<closedAt||now-closedAt>1500)){reason="close-grace-expired";Reset();return null;}
            if(string.IsNullOrEmpty(evidence)||evidence.Length>128||events.Count>=64){reason="evidence-bound";return null;}
            if(!events.Add(evidence)){reason="repeated-native-call";return null;}
            if(meta!=0||item<=0||count<=0||count>1000000000){reason="unsupported-meta-or-invalid-removal";return null;}
            if(fact==null||fact.Truncated||!string.IsNullOrEmpty(fact.Conflict)||fact.Items.Count==0||fact.Items.Count>64||fact.Relation=="mixed-unknown") {reason="ambiguous-requirements";return null;}
            if(!fact.Items.Any(i=>i.ItemId==item)){reason="unrelated-item";return null;}
            long prior;removed.TryGetValue(item,out prior);removed[item]=Math.Min(1000000000L,prior+count);
            var branches=new List<int[]>();
            if(fact.Alternatives.Count>0)
            {
                if(fact.Alternatives.Any(a=>a==null||a.Inputs==null)){reason="ambiguous-alternatives";return null;}
                var common=Enumerable.Range(0,fact.Items.Count).Except(fact.Alternatives.SelectMany(a=>a.Inputs)).ToArray();
                branches.AddRange(fact.Alternatives.Select(a=>a.Inputs.Concat(common).Distinct().ToArray()));
            }
            else if(fact.Relation=="any")branches.AddRange(Enumerable.Range(0,fact.Items.Count).Select(i=>new[]{i}));
            else branches.Add(Enumerable.Range(0,fact.Items.Count).ToArray());
            if(branches.Count>16){reason="alternatives-bound";return null;}
            foreach(var branch in branches)
            {
                if(branch==null||branch.Length==0||branch.Length>64||branch.Any(i=>i<0||i>=fact.Items.Count))continue;
                var inputs=branch.Distinct().Select(i=>fact.Items[i]).ToList();
                if(inputs.Any(i=>!(i.ItemId>0)||!(i.Quantity>0)||i.Quantity>1000000000))continue;
                bool complete=inputs.GroupBy(i=>i.ItemId.Value).All(g=>removed.ContainsKey(g.Key)&&removed[g.Key]>=g.Sum(i=>i.Quantity.Value));
                if(!complete)continue;
                var card=new LedgerRequestCard{Source=shown,Request=new LedgerRequestRecord{Source=shown,Fact=fact},Pages=new List<LedgerObservation>{shown}};
                LedgerManualCompletion command;
                if(!LedgerManualCompletion.TryCreate(card,player,true,out command)){reason="annotation-identity-unavailable";return null;}
                command.AutomaticEvidence=shown.Session+":"+evidence;
                command.ObservedUtc=shown.Utc;command.ObservedSession=shown.Session;command.ObservedInteraction=shown.Interaction;command.ObservedPage=shown.Dialogue.Page;
                command.EvidenceDetail="interaction="+interaction+" npc="+npc+" branch="+string.Join(",",branch)+" actualRemoved="+string.Join(",",removed.OrderBy(p=>p.Key).Select(p=>p.Key+":"+p.Value))+" closed="+(closedAt>=0);
                consumed=true;reason="requirements-removed";return command;
            }
            reason="partial-or-unresolved-requirements";return null;
        }
    }
}
