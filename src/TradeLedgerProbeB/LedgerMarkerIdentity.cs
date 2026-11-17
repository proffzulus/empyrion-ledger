using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TradeLedgerProbeB
{
    // NGUI consumes these balanced RGB commands without producing a glyph or a space.
    // TL1 + scope(48 bits) + random destination UUID(128 bits) + checksum(16 bits).
    // This is an ownership discriminator, not a security/authentication boundary.
    public static class LedgerMarkerIdentity
    {
        private const string Magic="[544C31]";
        public const int Length=99;
        public static bool ValidId(string id){Guid value;return id!=null&&id.Length==32&&Guid.TryParseExact(id,"N",out value)&&value!=Guid.Empty;}
        public static string Scope(LedgerMarkerDocument doc,string world,int player)
        {return LedgerJson.Hash("marker-identity-v1|"+doc.IdentityScope+"|"+world+"|"+player.ToString(CultureInfo.InvariantCulture)).Substring(0,12).ToUpperInvariant();}
        public static string Tag(LedgerMarkerDocument doc,LedgerMarkerDestination d)
        {
            if(!ValidId(doc.IdentityScope)||!ValidId(d.IdentityId))throw new InvalidOperationException("Marker identity unavailable");
            string body=Scope(doc,d.World,d.Player)+d.IdentityId.ToUpperInvariant();
            body+=LedgerJson.Hash("TL1|"+body).Substring(0,4).ToUpperInvariant();
            var result=new StringBuilder(Magic);for(int i=0;i<body.Length;i+=6)result.Append('[').Append(body,i,6).Append(']');
            for(int i=0;i<9;i++)result.Append("[-]");return result.ToString();
        }
        public static bool Parse(string text,out string scope,out string id)
        {
            scope=id=null;if(text==null||text.Length>2048)return false;
            int at=text.IndexOf(Magic,StringComparison.OrdinalIgnoreCase);
            if(at<0||at+Length!=text.Length)return false;
            var body=new StringBuilder();
            for(int i=0;i<8;i++){
                int start=at+8+i*8;if(text[start]!='['||text[start+7]!=']')return false;
                for(int j=1;j<=6;j++){char c=text[start+j];if(!Uri.IsHexDigit(c))return false;body.Append(char.ToUpperInvariant(c));}
            }
            for(int i=at+72;i<text.Length;i+=3)if(text.Substring(i,3)!="[-]")return false;
            string payload=body.ToString();if(payload.Substring(44)!=LedgerJson.Hash("TL1|"+payload.Substring(0,44)).Substring(0,4).ToUpperInvariant())return false;
            scope=payload.Substring(0,12);id=payload.Substring(12,32).ToLowerInvariant();return ValidId(id);
        }
        public static bool Owned(LedgerMarkerDocument doc,LedgerMarkerDestination d,LedgerMarkerNative n)
        {
            string scope,id;return Target(d,n)&&Parse(n.Label,out scope,out id)&&scope==Scope(doc,d.World,d.Player)&&id==d.IdentityId;
        }
        public static bool Target(LedgerMarkerDestination d,LedgerMarkerNative n)
        {return n!=null&&n.Id>=0&&n.Player==d.Player&&!n.Shared&&(n.Type==2||n.Type==3)&&n.Playfield==d.Playfield&&Near(d.X,n.X)&&Near(d.Y,n.Y)&&Near(d.Z,n.Z);}
        private static bool Near(float a,float b){return !float.IsNaN(b)&&!float.IsInfinity(b)&&Math.Abs(a-b)<=.05f;}
        public static bool LegacyMatch(LedgerMarkerNative receipt,LedgerMarkerNative n)
        {
            if(receipt==null||n==null)return false;
            // Exact persisted full fingerprint, including flags/creator/type. A name or position is never enough.
            var copy=LedgerJson.Decode<LedgerMarkerNative>(LedgerJson.Encode(receipt));copy.Id=n.Id;return copy.Matches(n);
        }
    }

    // An asynchronous native private-bookmark SELECT is an enumeration fence, not a guessed delay.
    // No mutation is allowed until its result is represented in the current local collection.
    public sealed class LedgerMarkerLoadFence
    {
        private IList<LedgerMarkerNative> expected;
        public bool Ready {get;private set;}
        public void Completed(IList<LedgerMarkerNative> records){expected=records;Ready=false;}
        public bool Admit(IDictionary<int,LedgerMarkerNative> current)
        {
            if(Ready)return true;if(expected==null)return false;
            foreach(var n in expected){LedgerMarkerNative found;if(!current.TryGetValue(n.Id,out found)||!n.Core(found))return false;}
            Ready=true;expected=null;return true;
        }
    }
}
