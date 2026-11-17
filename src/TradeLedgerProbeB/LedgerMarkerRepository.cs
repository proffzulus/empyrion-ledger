using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace TradeLedgerProbeB
{
    public sealed class LedgerMarkerUncertainException : Exception
    {public LedgerMarkerUncertainException(Exception inner):base("Native submission outcome uncertain; no automatic retry",inner){}}
    public sealed class LedgerMarkerEnumerationPendingException : InvalidOperationException
    {public LedgerMarkerEnumerationPendingException():base("Initial private bookmark enumeration pending; no marker mutations permitted") {}}
    // Save-contained owned metadata; staged outside the save tree, no gameplay-long lease/read handle.
    public sealed class LedgerMarkerRepository
    {
        private readonly string path,temporary;
        private readonly Action<Action> publish;
        private readonly object gate=new object();
        private byte[] pending;
        private bool writing;
        public string Error {get;private set;}
        public LedgerMarkerRepository(string file,string workRoot=null,Action<Action> publisher=null)
        {path=file;temporary=Path.Combine(workRoot??Path.GetDirectoryName(file),"marker-manager-"+Guid.NewGuid().ToString("N")+".pending");publish=publisher;}
        public LedgerMarkerDocument Read()
        {
            if(!File.Exists(path))return new LedgerMarkerDocument();
            if(new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("Marker manager file exceeds4MiB; original preserved");
            var data=LedgerJson.Decode<LedgerMarkerDocument>(File.ReadAllBytes(path));LedgerMarkerModel.Validate(data);return data;
        }
        public void Save(LedgerMarkerDocument data)
        {
            LedgerMarkerModel.Validate(data);byte[] bytes=LedgerJson.Encode(data);
            if(bytes.Length>4*1024*1024)throw new InvalidDataException("Marker manager output exceeds4MiB");
            lock(gate){pending=bytes;Error=null;if(writing)return;writing=true;ThreadPool.QueueUserWorkItem(_=>Work());}
        }
        private void Work()
        {
            int failures=0;
            while(true)
            {
                byte[] bytes;lock(gate){bytes=pending;if(bytes==null){writing=false;Monitor.PulseAll(gate);return;}}
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(temporary));
                    using(var f=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);f.Flush(true);}
                    Action action=()=>{Directory.CreateDirectory(Path.GetDirectoryName(path));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);};
                    if(publish==null)action();else publish(action);
                    lock(gate){if(ReferenceEquals(pending,bytes))pending=null;Error=null;}failures=0;
                }
                catch(LedgerPublicationPausedException){Thread.Sleep(100);}
                catch(Exception e)
                {
                    lock(gate)Error=e.GetType().Name+": "+e.Message;
                    if(++failures>=3){lock(gate){writing=false;Monitor.PulseAll(gate);}return;}Thread.Sleep(250);
                }
            }
        }
        public bool Wait(int milliseconds){lock(gate){if(writing)Monitor.Wait(gate,milliseconds);return !writing&&pending==null&&Error==null;}}
    }

    public interface ILedgerMarkerBackend
    {
        System.Collections.Generic.IDictionary<int,LedgerMarkerNative> Read();
        void Create(LedgerMarkerDestination target,LedgerMarkerProjection projection,Action<LedgerMarkerNative> completed);
        LedgerMarkerNative Update(LedgerMarkerNative expected,LedgerMarkerProjection projection);
        void Delete(LedgerMarkerNative expected);
    }
    // API-thread coordinator. A failed/conflicting destination never gates unrelated destinations.
    public sealed class LedgerMarkerCoordinator
    {
        public readonly LedgerMarkerModel Model;
        public string Status {get;private set;}
        private readonly System.Collections.Generic.Dictionary<string,DateTime> pending=new System.Collections.Generic.Dictionary<string,DateTime>();
        private readonly System.Collections.Generic.HashSet<string> failedCreates=new System.Collections.Generic.HashSet<string>();
        public LedgerMarkerCoordinator(LedgerMarkerModel model){Model=model;}
        public bool IsPending(LedgerMarkerDestination d){return pending.ContainsKey(d.Key);}
        public void Sync(ILedgerMarkerBackend backend,string world,int player,bool nativeSaveAllowed=true)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.MarkerSync))
            {
            if(!nativeSaveAllowed)return;
            var native=backend.Read();
            Status=null;
            // Only this document's namespace + world + player is eligible for orphan cleanup.
            string identityScope=LedgerMarkerIdentity.Scope(Model.Data,world,player);
            var identities=new System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<LedgerMarkerNative>>();
            foreach(var n in native.Values.ToArray()){
                string scope,id;if(n.Player!=player||n.Shared||(n.Type!=2&&n.Type!=3)||!LedgerMarkerIdentity.Parse(n.Label,out scope,out id)||scope!=identityScope||Model.Data.DetachedIdentities.Contains(id))continue;
                if(!Model.Data.Destinations.Any(d=>d.World==world&&d.Player==player&&d.IdentityId==id)){
                    try{backend.Delete(n);}catch(Exception e){Status="Owned orphan cleanup pending: "+e.GetBaseException().Message;}
                }else{
                    System.Collections.Generic.List<LedgerMarkerNative> list;if(!identities.TryGetValue(id,out list))identities.Add(id,list=new System.Collections.Generic.List<LedgerMarkerNative>());list.Add(n);
                }
            }
            if(!Model.Data.LogicalWaypoints){Model.Data.LogicalWaypoints=true;Model.Revision++;} // Absence never acknowledges an active goal.
            foreach(var d in Model.Data.Destinations.ToArray())
            {
                if(d.World!=world||d.Player!=player)continue;
                try
                {
                    string receiptScope,receiptId;
                    if(d.Native!=null&&LedgerMarkerIdentity.Parse(d.Native.Label,out receiptScope,out receiptId)&&(receiptScope!=identityScope||receiptId!=d.IdentityId)){
                        d.Native=d.PendingNative=null;d.Conflict=false;d.Status="Foreign identity receipt ignored; native marker untouched";Model.Revision++;
                    }
                    System.Collections.Generic.List<LedgerMarkerNative> known;if(!identities.TryGetValue(d.IdentityId,out known))known=new System.Collections.Generic.List<LedgerMarkerNative>();
                    var tagged=known.Where(n=>LedgerMarkerIdentity.Target(d,n)).OrderBy(n=>n.Id).ToList();
                    bool misplaced=known.Any(n=>!LedgerMarkerIdentity.Target(d,n));
                    if(misplaced||tagged.Count>128){d.Conflict=true;d.Status="Owned identity has conflicting location/count; native markers untouched";continue;}
                    if(tagged.Count>0){
                        var chosen=tagged.FirstOrDefault(n=>d.Native!=null&&n.Id==d.Native.Id)??tagged[0];
                        if(d.Native==null||!d.Native.Matches(chosen)||d.Conflict||d.PendingNative!=null){d.Native=chosen;d.PendingNative=null;d.Conflict=false;d.Status="";Model.Revision++;}
                        foreach(var extra in tagged.Where(n=>n.Id!=chosen.Id))if(!d.Duplicates.Any(n=>n.Id==extra.Id)){d.Duplicates.Add(extra);Model.Revision++;}
                        // A current-session submitted create still waits for its own callback;
                        // the tag proves presence, but is not permission to replay a command.
                        failedCreates.Remove(d.Key);
                    }
                    else if(d.Native==null||!native.ContainsKey(d.Native.Id)||!d.Native.Core(native[d.Native.Id])){
                        // Pre-tag migration: use a unique complete persisted receipt, never a label/location guess.
                        var proofs=d.LegacyCandidates.Concat(d.Duplicates).Concat(new[]{d.Native,d.PendingNative}).Where(n=>n!=null).ToList();
                        var matches=proofs.Count==0?new System.Collections.Generic.List<LedgerMarkerNative>():native.Values.Where(n=>{string s,id;return !LedgerMarkerIdentity.Parse(n.Label,out s,out id)&&proofs.Any(p=>LedgerMarkerIdentity.LegacyMatch(p,n));}).ToList();
                        if(matches.Count>128||matches.Count>1&&matches.Any(n=>!proofs.Any(p=>p.Matches(n)))){d.Conflict=true;d.Status="Multiple untagged receipt matches; left untouched; no duplicate created";continue;}
                        if(matches.Count>0){var candidate=matches.OrderBy(n=>n.Id).First();bool claimed=Model.Data.Destinations.Any(other=>other!=d&&other.LegacyCandidates.Concat(new[]{other.Native,other.PendingNative}).Where(p=>p!=null).Any(p=>matches.Any(n=>LedgerMarkerIdentity.LegacyMatch(p,n))));
                            if(claimed){d.Conflict=true;d.Status="Ambiguous persisted ownership; native marker untouched";continue;}
                            foreach(var extra in matches.Where(n=>n.Id!=candidate.Id))if(!d.Duplicates.Any(n=>n.Id==extra.Id))d.Duplicates.Add(extra);
                            if(d.LegacyCandidates.Any(p=>LedgerMarkerIdentity.LegacyMatch(p,candidate))){
                                d.Pinned|=!candidate.Approach;
                                foreach(var purpose in d.Purposes)if(purpose.Visibility==0&&purpose.Routes.Count==0&&matches.Any(n=>purpose.Plan.NativeLabel==n.Label))purpose.UngroupedShown=true;
                                d.LegacyCandidates.Clear();
                            }
                            d.Native=candidate;d.PendingNative=null;d.Conflict=false;Model.Revision++;
                        }
                    }
                    if(d.Native==null&&!d.DeleteRequested)
                    {
                        var candidates=d.LegacyCandidates.Where(c=>{string s,id;return native.ContainsKey(c.Id)&&!LedgerMarkerIdentity.Parse(native[c.Id].Label,out s,out id)&&c.Matches(native[c.Id]);}).ToList();
                        if(candidates.Count>0)
                        {
                            // Require unambiguous receipt ownership across destinations, not a name/tuple search.
                            var candidate=candidates.First();bool ambiguous=Model.Data.Destinations.Any(other=>other.Key!=d.Key&&other.LegacyCandidates.Any(c=>c.Id==candidate.Id&&c.Matches(native[c.Id])));
                            if(!ambiguous){d.Native=native[candidate.Id];d.Pinned|=!candidate.Approach;foreach(var purpose in d.Purposes)if(purpose.Visibility==0&&purpose.Routes.Count==0&&purpose.Plan.NativeLabel==candidate.Label)purpose.UngroupedShown=true;foreach(var extra in candidates.Skip(1))if(extra.Id!=candidate.Id)d.Duplicates.Add(extra);d.LegacyCandidates.Clear();Model.Revision++;}
                            else{d.Conflict=true;d.Status="Ambiguous legacy ownership; native marker untouched. Detach before a separate Mark";}
                        }
                    }
                    Model.Reconcile(d,native);
                    DateTime at;if(pending.TryGetValue(d.Key,out at))
                    {if(DateTime.UtcNow-at>TimeSpan.FromSeconds(10))d.Status="Native creation pending/uncertain; unrelated destinations remain available";continue;}
                    if(d.DeleteRequested)
                    {
                        LedgerMarkerNative current;if(d.Native!=null&&native.TryGetValue(d.Native.Id,out current)&&d.Native.Matches(current))backend.Delete(d.Native);
                        foreach(var extra in d.Duplicates){LedgerMarkerNative found;if(native.TryGetValue(extra.Id,out found)&&extra.Matches(found))backend.Delete(extra);}
                        foreach(var purpose in d.Purposes)LedgerRequestInventory.Remove(Model,d,purpose);Model.RememberServiceDelete(d);failedCreates.Remove(d.Key);Model.Data.Destinations.Remove(d);Model.Revision++;continue;
                    }
                    if(d.Conflict)continue;
                    foreach(var extra in d.Duplicates.ToArray()){
                        LedgerMarkerNative found;if(native.TryGetValue(extra.Id,out found)&&extra.Matches(found))backend.Delete(extra);
                        d.Duplicates.Remove(extra);Model.Revision++;
                    }
                    var projection=Model.Project(d);
                    // Attach only at the native boundary; visible journal/UI projections stay unchanged.
                    projection.Label+=LedgerMarkerIdentity.Tag(Model.Data,d);
                    if(!projection.Visible&&d.Native!=null&&d.Purposes.All(p=>!p.Service)){backend.Delete(d.Native);d.Native=d.PendingNative=null;d.CreateRequested=false;Model.Revision++;continue;}
                    if(d.Native==null&&projection.Visible&&(d.CreateRequested||!failedCreates.Contains(d.Key)))
                    {
                        failedCreates.Remove(d.Key);pending[d.Key]=DateTime.UtcNow;d.CreateRequested=false;d.Status="Creating native marker...";Model.Revision++;
                        backend.Create(d,projection,result=>
                        {
                            pending.Remove(d.Key);
                            if(result!=null){d.Native=result;d.LegacyCandidates.Clear();d.Status="";}else{failedCreates.Add(d.Key);LedgerRouteFlow.Acknowledge(Model,d);d.Status="Native creation failed; Mark retries explicitly";}
                            Model.Revision++;
                        });
                    }
                    else if(d.Native!=null)
                    {
                        var desired=d.Native.ChangedProjection(projection);
                        if(desired!=null)
                        {
                            d.PendingNative=desired;Model.Revision++; // Recognize own update even if native command throws after local replacement.
                            d.Native=backend.Update(d.Native,projection);d.PendingNative=null;d.Status="";Model.Revision++;
                        }
                    }
                }
                catch(Exception e){if(!(e is LedgerMarkerUncertainException)){pending.Remove(d.Key);if(d.Native==null){failedCreates.Add(d.Key);LedgerRouteFlow.Acknowledge(Model,d);}}d.Status="Marker action failed: "+e.Message;}
            }

            }
        }
    }
}
