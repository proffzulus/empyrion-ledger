using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    public static class LedgerDistanceRefresh
    {
        // Call once on snapshot publication, never from a row paint. Keep the old
        // immutable point until the guarded read settles, then replace atomically.
        // A completed unavailable read is real Unknown, not permission to reuse it.
        public static LedgerPlayerPoint Replace(LedgerPlayerPoint prior,LedgerContext current,int? observedPlayer,int? currentPlayer,Func<LedgerGeography> read)
        {
            if(!LedgerBrowse.SamePositionScope(prior,current)||!(currentPlayer>0)||observedPlayer!=currentPlayer)return null;
            LedgerGeography next=null;try{if(read!=null)next=read();}catch(Exception){/* Optional coordinates unavailable; never retain them as current. */}
            if(!LedgerBrowse.SamePositionScope(prior,current))return null;
            return new LedgerPlayerPoint{World=prior.World,Playfield=prior.Playfield,SaveScope=prior.SaveScope,X=prior.X,Y=prior.Y,Z=prior.Z,Geography=next};
        }
    }
    // Immutable, observed-PF-only projection. Native map units, not local Entity.Position vectors.
    public sealed class LedgerDistanceIndex
    {
        private sealed class Point { public double[] System,Sector;public bool Conflict; }
        private readonly string world;
        private readonly Dictionary<string,Point> points=new Dictionary<string,Point>(StringComparer.Ordinal);
        private readonly HashSet<string> observed=new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<LedgerGeography,Point> decoded=new Dictionary<LedgerGeography,Point>();
        private LedgerGeography originSource;private Point origin;
        public LedgerDistanceIndex(string world,IEnumerable<LedgerContext> contexts)
        {
            this.world=world;
            foreach(var c in contexts){if(c==null||c.WorldKey!=world||string.IsNullOrEmpty(c.Playfield))continue;
                observed.Add(c.Playfield);
                var p=Read(c.Geography);if(p==null)continue;Point old;
                if(points.TryGetValue(c.Playfield,out old)){if(!Same(old.System,p.System)||!Same(old.Sector,p.Sector))old.Conflict=true;}
                else points.Add(c.Playfield,p);
            }
        }
        private Point Read(LedgerGeography g)
        {
            Point cached;if(g!=null&&decoded.TryGetValue(g,out cached))return cached;
            // Header/name-only or unverified historical coordinates cannot authorize numeric distances.
            if(g==null||g.Source==null||!g.Source.StartsWith("native exact observed PF 5D4B;system5D49/sector5D4A;",StringComparison.Ordinal))return null;
            var system=Coordinate(g.SystemCoordinates);
            var result=new Point{System=system,Sector=Coordinate(g.SectorCoordinates),Conflict=system==null};decoded[g]=result;return result;
        }
        private static double[] Coordinate(string s)
        {
            var parts=(s??"").Split(',');if(parts.Length!=3)return null;var a=new double[3];
            for(int i=0;i<3;i++){int v;if(!int.TryParse(parts[i],NumberStyles.Integer,CultureInfo.InvariantCulture,out v)||v==int.MinValue||v==int.MaxValue)return null;a[i]=v;}
            return a;
        }
        private static bool Same(double[] a,double[] b){return a==null?b==null:b!=null&&a[0]==b[0]&&a[1]==b[1]&&a[2]==b[2];}
        private static double Separation(double[] a,double[] b){double x=a[0]-b[0],y=a[1]-b[1],z=a[2]-b[2];return Math.Sqrt(x*x+y*y+z*z);}
        public LedgerDistance Resolve(LedgerPlayerPoint player,LedgerContext target)
        {
            if(player==null||target==null||string.IsNullOrEmpty(world)||player.World!=world||target.WorldKey!=world||string.IsNullOrEmpty(player.Playfield)||string.IsNullOrEmpty(target.Playfield)||player.Playfield==target.Playfield)return null;
            // Native geography is an immutable DTO replaced on scope/projection changes. Parse once,
            // not once per offer/GUI event; the live player/save/PF admission still runs every time.
            if(!ReferenceEquals(originSource,player.Geography)){originSource=player.Geography;origin=Read(originSource);}
            var from=origin;Point to,knownFrom;
            // Exact already-observed PFs may gain optional metadata after projection.
            // Decode each immutable DTO once; no world/cache enumeration is introduced.
            if(observed.Contains(target.Playfield)){
                var latest=Read(target.Geography);Point prior;
                if(latest!=null){if(points.TryGetValue(target.Playfield,out prior)){if(!Same(prior.System,latest.System)||!Same(prior.Sector,latest.Sector))prior.Conflict=true;}else points.Add(target.Playfield,latest);}
            }
            if(from==null||from.Conflict||!points.TryGetValue(target.Playfield,out to)||to.Conflict)return null;
            if(points.TryGetValue(player.Playfield,out knownFrom)&&(knownFrom.Conflict||!Same(knownFrom.System,from.System)||knownFrom.Sector!=null&&from.Sector!=null&&!Same(knownFrom.Sector,from.Sector)))return null;
            if(!Same(from.System,to.System))return new LedgerDistance(Separation(from.System,to.System)*0.1/10000,"ly","Stellar-system separation.");
            if(from.Sector==null||to.Sector==null||Same(from.Sector,to.Sector))return null;
            return new LedgerDistance(Separation(from.Sector,to.Sector)*0.1,"AU","Planetary-sector separation; not surface distance.");
        }
    }
    public sealed class LedgerDistance
    {
        public readonly double Value;public readonly string Unit,Reason;
        internal LedgerDistance(double value,string unit,string reason){Value=value;Unit=unit;Reason=reason;}
        public string Text { get {return Value>0&&Value<0.1?"<0.1 "+Unit:Value.ToString("0.0",CultureInfo.InvariantCulture)+" "+Unit;} }
    }
    public sealed partial class LedgerViewData
    {
        private LedgerDistanceIndex distances;
        public void PrepareDistances()
        {
            // Only price-table destinations (the reader already caps markets at512); do not scan
            // request history, device/deposit stores or a universe catalogue on the game thread.
            distances=new LedgerDistanceIndex(WorldKey,Markets.Where(o=>o!=null).Select(o=>o.Context));
        }
        private LedgerDistance CrossDistance(LedgerObservation o,LedgerPlayerPoint player)
        {return distances==null||o==null?null:distances.Resolve(player,o.Context);}
        public decimal? DistanceSortFor(LedgerObservation o,LedgerPlayerPoint player)
        {
            if(o==null||o.Context==null)return null;
            if(player!=null&&player.Playfield!=o.Context.Playfield){var d=CrossDistance(o,player);
                // Native map's ly/AU ratio; native Partition.SearchContext uses 150000*1000 per AU.
                return d==null?null:(decimal?)(d.Value*(d.Unit=="ly"?10000:1)*150000000);}
            var local=DistanceMetersFor(o,player);return local.HasValue?(decimal?)local.Value:null;
        }
    }
}
