using System;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    // Completion belongs to an observed request occurrence, not an NPC/item or a native waypoint.
    public static class LedgerRequestMarkers
    {
        public static string Endpoint(LedgerBookmarkPlan p)
        {return p.World+":"+LedgerJson.Hash(p.Playfield)+":npc:"+p.NpcId.ToString(CultureInfo.InvariantCulture);}
        public static void Suspend(LedgerMarkerModel m)
        {foreach(var p in m.Data.Destinations.SelectMany(d=>d.Purposes).Where(p=>p.Kind=="request"))p.RequestBlocked=true;}
        private static bool Same(LedgerMarkerDestination d,LedgerMarkerPurpose p,LedgerObservation o)
        {return o!=null&&o.Context!=null&&o.Identity!=null&&o.Dialogue!=null&&o.Context.WorldKey==d.World&&o.Context.Playfield==d.Playfield&&o.Identity.NpcEntityId==d.Npc&&o.Dialogue.BodyVerbatim==p.Plan.RequestBody&&(!d.Structure.HasValue||!o.Identity.StructureEntityId.HasValue||d.Structure==o.Identity.StructureEntityId)&&(d.Block==null||o.Identity.BlockGrid==null||d.Block==o.Identity.BlockGrid);}
        private static bool Bind(LedgerMarkerModel m,LedgerMarkerDestination d,LedgerMarkerPurpose p,LedgerObservation[] seen)
        {
            if(p.Plan.RequestKey!=null)return true;
            var exact=seen.Where(o=>Same(d,p,o)).ToArray();
            // A legacy purpose stores the observation UTC it was marked from. Never attach it
            // to an arbitrary latest recurrence just because NPC and words happen to match.
            var at=exact.Where(o=>o.Utc==p.Plan.ObservedUtc).Select(LedgerManualCompletion.Key).Distinct().ToArray();
            if(at.Length==0)at=exact.Select(LedgerManualCompletion.Key).Distinct().ToArray();
            if(at.Length!=1)return false;
            string previous=p.Plan.RequestKey;p.Plan.RequestKey=at[0];string key=LedgerMarkerModel.PurposeKey(p.Plan);
            if(d.Purposes.Any(other=>!ReferenceEquals(other,p)&&other.Key==key)){p.Plan.RequestKey=previous;return false;}
            p.Key=key;m.Revision++;return true;
        }
        public static void Apply(LedgerMarkerModel m,LedgerViewData view,string world,int player)
        {
            if(view==null||view.WorldKey!=world||view.ManualPlayerId!=player||!view.ManualStateReady||view.ManualReadError!=null)return;
            foreach(var d in m.Data.Destinations.Where(d=>d.World==world&&d.Player==player))foreach(var p in d.Purposes.Where(p=>p.Kind=="request")){
                if(!Bind(m,d,p,view.DialoguePages.ToArray())){p.RequestBlocked=true;continue;}
                bool done;string stamp;
                if(view.ManualCompletions.TryGetValue(p.Plan.RequestKey,out done)&&view.CompletionStamps.TryGetValue(p.Plan.RequestKey,out stamp))Set(m,p,done,stamp);
                p.RequestBlocked=false;
            }
        }
        public static void Committed(LedgerMarkerModel m,LedgerManualCompletion annotation)
        {
            if(annotation==null||annotation.IsTracking||annotation.IsArchive||!annotation.StoredCompleted.HasValue)return;
            foreach(var d in m.Data.Destinations.Where(d=>d.World==annotation.World&&d.Player==annotation.PlayerId))foreach(var p in d.Purposes.Where(p=>p.Kind=="request")){
                if(Endpoint(p.Plan)!=annotation.Endpoint||p.Plan.RequestBody!=annotation.Body)continue;
                // Exact old-format first occurrence is safely recoverable; a later recurring
                // occurrence must first bind from its observed instance in the private view.
                if(p.Plan.RequestKey==null&&annotation.RequestKey==LedgerManualCompletion.KeyFor(annotation.Endpoint,annotation.Body)){
                    p.Plan.RequestKey=annotation.RequestKey;m.Revision++;
                }
                if(p.Plan.RequestKey!=annotation.RequestKey)continue;
                Set(m,p,annotation.StoredCompleted.Value,annotation.Utc);p.RequestBlocked=false;
            }
        }
        private static void Set(LedgerMarkerModel m,LedgerMarkerPurpose p,bool done,string stamp)
        {
            if(string.IsNullOrEmpty(stamp)||p.RequestStateUtc!=null&&string.CompareOrdinal(stamp,p.RequestStateUtc)<0)return;
            if(p.RequestCompleted!=done||p.RequestStateUtc!=stamp){p.RequestCompleted=done;p.RequestStateUtc=stamp;p.Consumed=done;m.Revision++;}
        }
    }
}
