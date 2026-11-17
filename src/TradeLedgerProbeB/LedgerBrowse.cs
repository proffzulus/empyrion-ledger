using System;
using System.Globalization;

namespace TradeLedgerProbeB
{
    public sealed class LedgerPlayerPoint { public string World,Playfield,SaveScope;public double X,Y,Z;public LedgerGeography Geography; }
    public static class LedgerBrowse
    {
        public static bool SamePositionScope(LedgerPlayerPoint p,LedgerContext c)
        {return p!=null&&c!=null&&c.Recordable&&p.World==c.WorldKey&&p.Playfield==c.Playfield&&p.SaveScope==(c.LocalSaveKey??c.SavePath);}
        public static string OfferKey(LedgerObservation o,LedgerOffer offer,bool sellers)
        {return LedgerDatabase.Key(o)+"|item:"+offer.ItemId+":"+offer.AuxiliaryRaw+"|"+(sellers?"sell":"buy");}
        public static bool ValidSprite(int x,int y,int w,int h,int tw,int th)
        {return x>=0&&y>=0&&w>0&&h>0&&tw>0&&th>0&&(long)x+w<=tw&&(long)y+h<=th;}
        public static bool CloseAfterKey(bool ownedTop,bool requested,bool escapeHeld)
        {return ownedTop&&requested&&!escapeHeld;}
        public static string Distance(LedgerPlayerPoint player,string world,string playfield,string position,string source)
        {
            if(player==null || player.World!=world)return "unknown";
            if(player.Playfield!=playfield)return "other playfield";
            var distance=DistanceMeters(player,world,playfield,position,source);if(!distance.HasValue)return "unknown";double d=distance.Value;
            return "~"+(d<1000?d.ToString("0.0",CultureInfo.InvariantCulture)+" m":(d/1000).ToString("0.00",CultureInfo.InvariantCulture)+" km");
        }
        public static double? DistanceMeters(LedgerPlayerPoint player,string world,string playfield,string position,string source)
        {
            if(player==null||player.World!=world||player.Playfield!=playfield)return null;
            if(string.IsNullOrEmpty(source)||!source.Contains("1B3A")||!source.Contains("EntityBridge.Position"))return null;
            string[] parts=(position??"").Split(',');double x,y,z;
            if(parts.Length!=3||!double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out x)||!double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out y)||!double.TryParse(parts[2],NumberStyles.Float,CultureInfo.InvariantCulture,out z))return null;
            if(!Finite(x)||!Finite(y)||!Finite(z)||!Finite(player.X)||!Finite(player.Y)||!Finite(player.Z))return null;
            double d=Math.Sqrt((player.X-x)*(player.X-x)+(player.Y-y)*(player.Y-y)+(player.Z-z)*(player.Z-z));
            return d;
        }
        private static bool Finite(double x){return !double.IsNaN(x)&&!double.IsInfinity(x)&&Math.Abs(x)<1000000000;}
        public static string DistanceReason(LedgerPlayerPoint player,string world,string playfield,string position,string source)
        {
            if(player==null)return "Current player position is unavailable.";
            if(player.World!=world)return "Different save/world; coordinates cannot be compared.";
            if(player.Playfield!=playfield)return "Other playfield. No verified inter-playfield distance; native bookmarks still use that destination's own coordinates.";
            if(string.IsNullOrEmpty(position))return "No recorded NPC/POI coordinates. Open this trader once to record its location; a name or block offset is not a position.";
            if(string.IsNullOrEmpty(source)||!source.Contains("1B3A")||!source.Contains("EntityBridge.Position"))return "Stored coordinates have no verified playfield-native position source.";
            return Distance(player,world,playfield,position,source)=="unknown"?"Position is invalid or incomplete.":(source.Contains("POI-origin")?"Straight-line distance within this playfield, from the observed POI origin (not exact NPC position).":"Straight-line distance within this playfield, from the observed NPC position.");
        }
    }
}
