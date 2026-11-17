using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeLedgerProbeB
{
    // HUD projection only: no device state, thresholds, routes or native ownership changes.
    // A summary uses one stable member's actual recorded destination, not invented star coordinates.
    public static class LedgerMinerAttention
    {
        private static LedgerProductionPlace Parent(LedgerProductionPlace n,string role)
        {for(;n!=null;n=n.Parent)if(n.Role==role)return n;return null;}
        public static void Evaluate(LedgerMarkerModel model,IEnumerable<LedgerObservation> devices,LedgerContext current,int player)
        {
            foreach(var p in model.Data.Destinations.SelectMany(d=>d.Purposes)){p.ServiceGeoHidden=false;p.ServiceGeoLabel=null;}
            if(current==null||!current.Recordable||current.Mode!="SinglePlayer")return;
            var rows=devices.Where(o=>o!=null&&o.Context!=null&&o.Context.WorldKey==current.WorldKey&&o.Production!=null&&o.Production.Player==player&&o.Production.Kind=="producer").ToList();
            var places=new LedgerProductionPlaces(current.WorldKey,rows.Select(o=>o.Context).Concat(new[]{current}));
            var here=places.Resolve("playfield:"+current.Playfield);var system=Parent(here,"system");var group=Parent(here,"group");
            if(system==null||!system.Key.StartsWith("system:coordinates:",StringComparison.Ordinal))return;
            var miners=new HashSet<string>(rows.Where(o=>o.Production.Family=="AutoMiner").Select(o=>o.Production.Key));
            var summaries=new Dictionary<string,List<Tuple<LedgerMarkerDestination,LedgerMarkerPurpose>>>();
            var labels=new Dictionary<string,string>();
            foreach(var d in model.Data.Destinations.Where(d=>d.World==current.WorldKey&&d.Player==player&&!d.DeleteRequested&&!d.Conflict&&d.TargetKey!=null&&miners.Contains(d.TargetKey))){
                var leaf=places.Resolve("playfield:"+d.Playfield);var targetSystem=Parent(leaf,"system");var targetGroup=Parent(leaf,"group");
                LedgerProductionPlace summary=null;
                if(targetSystem!=null&&targetSystem.Key.StartsWith("system:coordinates:",StringComparison.Ordinal)&&targetSystem.Key!=system.Key)summary=targetSystem;
                else if(targetSystem!=null&&targetSystem.Key==system.Key&&group!=null&&targetGroup!=null&&targetGroup.Key!=group.Key)summary=targetGroup;
                if(summary==null)continue; // Unknown hierarchy stays concrete, never guessed by name.
                foreach(var p in d.Purposes.Where(p=>p.Service&&model.Active(p))){
                    List<Tuple<LedgerMarkerDestination,LedgerMarkerPurpose>> members;
                    if(!summaries.TryGetValue(summary.Key,out members)){members=new List<Tuple<LedgerMarkerDestination,LedgerMarkerPurpose>>();summaries.Add(summary.Key,members);labels.Add(summary.Key,summary.Label);}
                    members.Add(Tuple.Create(d,p));
                }
            }
            foreach(var pair in summaries){
                var members=pair.Value.OrderBy(x=>x.Item1.Key,StringComparer.Ordinal).ThenBy(x=>x.Item2.Key,StringComparer.Ordinal).ToList();
                foreach(var member in members)member.Item2.ServiceGeoHidden=true;
                var carrier=members[0].Item2;carrier.ServiceGeoHidden=false;
                carrier.ServiceGeoLabel="[c]["+LedgerProductionServices.Color+"]"+labels[pair.Key]+" — Managed miners ("+members.Select(x=>x.Item1.TargetKey).Distinct().Count()+")[-][/c]";
            }
        }
    }
}
