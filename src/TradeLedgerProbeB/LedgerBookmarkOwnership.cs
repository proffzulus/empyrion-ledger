using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace TradeLedgerProbeB
{
    public sealed class LedgerOwnedBookmark
    {
        public int PlayerId,NativeId,NativeType;
        public LedgerBookmarkPlan Plan;
        public string Key {get{return Scope(Plan,PlayerId);}}
        public static string Scope(LedgerBookmarkPlan p,int player){return p.World+"|"+player+"|"+p.OfferKey;}
        public bool Matches(int id,string label,string playfield,int creator,int type,float x,float y,float z,bool shared,bool universe,bool approach,bool hud)
        {
            return id==NativeId&&creator==PlayerId&&label==Plan.NativeLabel&&playfield==Plan.Playfield&&type==NativeType&&(type==2||type==3)&&!shared&&
                universe==Plan.VisibleInUniverse&&approach==Plan.RemoveOnApproach&&hud==Plan.VisibleInPlayfield&&
                Math.Abs(x-Plan.X)<.001f&&Math.Abs(y-Plan.Y)<.001f&&Math.Abs(z-Plan.Z)<.001f;
        }
    }
    public enum LedgerBookmarkDecision { Create,Remove,AlreadyAbsent,AlreadyPresent,StaleClick,OwnershipChanged }
    public static class LedgerBookmarkOperations
    {
        public static LedgerBookmarkDecision Decide(bool remove,int expectedId,LedgerOwnedBookmark receipt,bool nativeMatches,bool nativeExists=false)
        {
            if(receipt!=null&&nativeExists&&!nativeMatches)return LedgerBookmarkDecision.OwnershipChanged;
            bool owned=receipt!=null&&nativeMatches;
            if(!remove)return owned?LedgerBookmarkDecision.AlreadyPresent:LedgerBookmarkDecision.Create;
            if(!owned)return LedgerBookmarkDecision.AlreadyAbsent;
            return receipt.NativeId==expectedId?LedgerBookmarkDecision.Remove:LedgerBookmarkDecision.StaleClick;
        }
    }
    // Separate bounded latest ownership receipts, outside replaceable binaries. No migration or writes to observations.
    public sealed class LedgerBookmarkOwnership
    {
        public sealed class Document {public string Format="TradeLedger.native-bookmark-ownership.v1";public List<LedgerOwnedBookmark> Records=new List<LedgerOwnedBookmark>();}
        private readonly Dictionary<string,LedgerOwnedBookmark> records=new Dictionary<string,LedgerOwnedBookmark>();
        private readonly string path,tempRoot;
        private readonly Action<string> error;
        private readonly Action<Action> publish;
        private bool writing,dirty;
        public LedgerBookmarkOwnership(string file,Action<string> onError,string stagingRoot=null,Action<Action> publisher=null)
        {
            path=file;error=onError;tempRoot=stagingRoot;publish=publisher;
            if(!File.Exists(file))return;
            if(new FileInfo(file).Length>1024*1024)throw new InvalidDataException("Bookmark ownership file exceeds bound");
            var doc=LedgerJson.Decode<Document>(File.ReadAllBytes(file));
            if(doc==null||doc.Format!="TradeLedger.native-bookmark-ownership.v1"||doc.Records==null||doc.Records.Count>1024)throw new InvalidDataException("Bookmark ownership document invalid");
            foreach(var r in doc.Records){if(!Valid(r)||records.ContainsKey(r.Key))throw new InvalidDataException("Bookmark ownership receipt invalid");records.Add(r.Key,r);}
        }
        private static bool Valid(LedgerOwnedBookmark r)
        {return r!=null&&r.NativeId>=0&&(r.NativeType==2||r.NativeType==3)&&r.PlayerId>0&&r.Plan!=null&&!string.IsNullOrEmpty(r.Plan.World)&&!string.IsNullOrEmpty(r.Plan.Playfield)&&!string.IsNullOrEmpty(r.Plan.OfferKey)&&r.Plan.OfferKey.Length<1024&&!string.IsNullOrEmpty(r.Plan.Label)&&r.Plan.Label.Length<300;}
        public LedgerOwnedBookmark Get(LedgerBookmarkPlan p,int player){lock(records){LedgerOwnedBookmark r;return records.TryGetValue(LedgerOwnedBookmark.Scope(p,player),out r)?r:null;}}
        public List<LedgerOwnedBookmark> For(string world,int player){lock(records)return records.Values.Where(r=>r.Plan.World==world&&r.PlayerId==player).ToList();}
        public void Put(LedgerOwnedBookmark r){if(!Valid(r))throw new InvalidDataException("Invalid ownership receipt");lock(records){if(records.Count>=1024&&!records.ContainsKey(r.Key))throw new InvalidDataException("Ownership receipt bound");records[r.Key]=r;Schedule();}}
        public void Forget(LedgerOwnedBookmark r){lock(records){records.Remove(r.Key);Schedule();}}
        private void Schedule(){dirty=true;if(writing)return;writing=true;ThreadPool.QueueUserWorkItem(_=>FlushWorker());}
        private void FlushWorker()
        {
            while(true)
            {
                byte[] bytes;lock(records){if(!dirty){writing=false;Monitor.PulseAll(records);return;}dirty=false;bytes=LedgerJson.Encode(new Document{Records=records.Values.ToList()});}
                string temp=tempRoot==null?path+".pending":Path.Combine(tempRoot,"bookmarks.pending");
                try
                {
                    if(bytes.Length>1024*1024)throw new InvalidDataException("Ownership output exceeds bound");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);f.Flush(true);}
                    Action replace=()=>{if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);};
                    if(publish==null)replace();else publish(replace);
                }
                catch(LedgerPublicationPausedException){lock(records)dirty=true;Thread.Sleep(100);}
                catch(Exception e){try{error(e.GetType().Name+": "+e.Message);}catch(Exception){/* Notification cannot escape the persistence worker. */}}
            }
        }
        public bool WaitForWrites(int milliseconds){lock(records){if(writing)Monitor.Wait(records,milliseconds);return !writing;}}
    }
}
