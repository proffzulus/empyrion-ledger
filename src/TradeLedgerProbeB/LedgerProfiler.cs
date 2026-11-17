using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TradeLedgerProbeB
{
    public enum LedgerPerfPath
    {
        Update,GameEvent,UpdateCallbackGap,CallbackGateWait,ObserverPoll,ObserverHook,TraderCapture,DialogueCapture,
        ProductionPoll,ProducerShown,ScannerHud,MapDeposit,ResourceDiscovery,ObjectDiscovery,ObjectReconcile,
        RequestRecognition,InventoryMatch,MarkerRefresh,MarkerSync,SnapshotRead,StoreEnqueue,StoreOpen,SqliteSave,
        Publication,PublicationGateWait,UiPoll,UiRender,GraphRender,GraphBuild,StoreQueueDepth,Count
    }
    // No strings, delegates, native reads, heap buffers or clock read in the disabled sample path.
    public struct LedgerPerfScope : IDisposable
    {
        private LedgerProfileSession session;
        private readonly LedgerPerfSample sample;
        internal LedgerPerfScope(LedgerProfileSession session,LedgerPerfPath path)
        {this.session=session;sample=session.Begin(path);}
        public void Dispose(){var s=session;session=null;if(s!=null)s.End(sample);}
    }
    internal struct LedgerPerfSample
    {internal LedgerPerfPath Path;internal long At,Duration,Update,Token;internal int Epoch,Slot,Value;internal bool Main;}

    // Session-only opt-in, independent of capture/logger/readiness and native save publication.
    public static class LedgerProfiler
    {
        private static LedgerProfileSession current;
        private static readonly object control=new object();
        private static string root,identity;
        private static int mainThread;
        public static bool Enabled {get{var s=Volatile.Read(ref current);return s!=null&&s.Accepting;}}
        public static string State {get{var s=Volatile.Read(ref current);return s==null?"Off":s.Accepting?"On":!s.Done?"Saving":s.Error==null?"Off":"Error";}}
        public static string Output {get{var s=Volatile.Read(ref current);return s==null?null:s.Output;}}
        public static string StopReason {get{var s=Volatile.Read(ref current);return s==null?null:s.Reason;}}
        public static string ButtonLabel {get{string state=State;return state=="On"?"Profile: On":state=="Saving"?"Profile: Saving":state=="Error"?"Profile: Error":"Profile: Off";}}
        internal static void Configure(string logRoot,string fingerprint,string runtimeHash,int apiThread,bool observation,string diagnostics)
        {
            lock(control){root=logRoot;mainThread=apiThread;
                identity="version=1.0.0.0 build=5232 fingerprint="+fingerprint+" nativeHash="+runtimeHash+" runtimeObservation="+observation+" diagnostics="+diagnostics;}
        }
        public static bool SetEnabled(bool enabled)
        {
            lock(control){var old=Volatile.Read(ref current);
                if(!enabled){if(old!=null)old.Stop("manual-or-shutdown");return true;}
                if(old!=null&&!old.Done)return old.Accepting;
                if(string.IsNullOrEmpty(root))return false;
                try{var next=new LedgerProfileSession(root,identity,mainThread,new LedgerProfileLimits());Volatile.Write(ref current,next);next.Start();return true;}
                catch(Exception){return false;} // Diagnostics must never affect game/capture behavior.
            }
        }
        public static LedgerPerfScope Measure(LedgerPerfPath path)
        {var s=Volatile.Read(ref current);return s==null||!s.Accepting?default(LedgerPerfScope):new LedgerPerfScope(s,path);}
        public static void Tick(){var s=Volatile.Read(ref current);if(s!=null&&s.Accepting)s.Tick();}
        public static void ScopeChanged(){var s=Volatile.Read(ref current);if(s!=null&&s.Accepting)Interlocked.Increment(ref s.Epoch);}
        public static void QueueDepth(int depth){var s=Volatile.Read(ref current);if(s!=null&&s.Accepting)s.QueueDepth(depth);}
    }
    internal sealed class LedgerProfileLimits
    {
        internal int Capacity=4096,MaxSeconds=600,FlushMilliseconds=1000,SummaryMilliseconds=5000,FileBytes=2*1024*1024,Files=2,MaxSamplesPerSecond=6000;
        internal double SlowMilliseconds=10;
        internal Func<int,TextWriter> Writer; // Offline failure/backpressure fixture only; runtime always file-backed.
    }
    internal sealed class LedgerProfileSession
    {
        private readonly object gate=new object();
        private readonly LedgerPerfSample[] ring;
        private readonly LedgerPerfSample[] activeSamples=new LedgerPerfSample[64];
        private readonly long[] activeTokens=new long[64];
        private readonly LedgerProfileLimits limits;
        private readonly string root,identity;
        private int main;
        private readonly long started=Stopwatch.GetTimestamp();
        private readonly DateTime startedUtc=DateTime.UtcNow;
        private readonly AutoResetEvent wake=new AutoResetEvent(false);
        private int head,count,rateCount;private long rateSecond;
        internal int Epoch;
        internal volatile bool Accepting=true,Done;
        internal volatile string Error,Reason,Output;
        internal long Dropped,Accepted,Processed,SlowSuppressed,InFlight,InFlightAtStop,RateDropped,ActiveDropped;
        private long scopeSequence;private int activeCursor;
        private long update,lastTick;
        private long queueSamples;private int queueLast,queueMax;
        private Thread worker;
        internal LedgerProfileSession(string root,string identity,int main,LedgerProfileLimits limits)
        {this.root=root;this.identity=identity;this.main=main;this.limits=limits;ring=new LedgerPerfSample[limits.Capacity];}
        internal void Start(){try{worker=new Thread(Work){IsBackground=true,Name="TradeLedger-profiler"};worker.Start();}catch(Exception e){Error=e.GetType().Name;Accepting=false;Done=true;wake.Dispose();throw;}}
        internal LedgerPerfSample Begin(LedgerPerfPath path)
        {
            Interlocked.Increment(ref InFlight);var s=new LedgerPerfSample{Path=path,At=Stopwatch.GetTimestamp(),Update=Interlocked.Read(ref update),Epoch=Volatile.Read(ref Epoch),Main=Thread.CurrentThread.ManagedThreadId==Volatile.Read(ref main),Slot=-1,Token=Interlocked.Increment(ref scopeSequence)};
            int start=(Interlocked.Increment(ref activeCursor)&int.MaxValue)%activeTokens.Length;
            for(int i=0;i<activeTokens.Length;i++){int slot=(start+i)%activeTokens.Length;if(Interlocked.CompareExchange(ref activeTokens[slot],-1,0)!=0)continue;
                s.Slot=slot;activeSamples[slot]=s;Volatile.Write(ref activeTokens[slot],s.Token);return s;}
            Interlocked.Increment(ref ActiveDropped);return s;
        }
        internal void End(LedgerPerfSample sample)
        {if(sample.Slot>=0&&Interlocked.CompareExchange(ref activeTokens[sample.Slot],-1,sample.Token)!=sample.Token)return;
            try{sample.Duration=Stopwatch.GetTimestamp()-sample.At;Record(sample);}catch(Exception){Interlocked.Increment(ref Dropped);}finally{if(sample.Slot>=0)Volatile.Write(ref activeTokens[sample.Slot],0);Interlocked.Decrement(ref InFlight);}}
        internal void Tick()
        {
            long now=Stopwatch.GetTimestamp(),previous=Interlocked.Exchange(ref lastTick,now);Interlocked.Increment(ref update);
            Volatile.Write(ref main,Thread.CurrentThread.ManagedThreadId);
            if(Milliseconds(now-started)>=limits.MaxSeconds*1000L){Stop("time-limit");return;}
            if(previous!=0&&Milliseconds(now-previous)>=100)Record(new LedgerPerfSample{Path=LedgerPerfPath.UpdateCallbackGap,At=previous,Duration=now-previous,Update=Interlocked.Read(ref update),Epoch=Volatile.Read(ref Epoch),Main=true});
        }
        internal void QueueDepth(int depth)
        {Record(new LedgerPerfSample{Path=LedgerPerfPath.StoreQueueDepth,At=Stopwatch.GetTimestamp(),Update=Interlocked.Read(ref update),Epoch=Volatile.Read(ref Epoch),Main=Thread.CurrentThread.ManagedThreadId==Volatile.Read(ref main),Slot=-1,Value=depth});}
        internal static double Milliseconds(long ticks){return ticks*1000.0/Stopwatch.Frequency;}
        internal void Record(LedgerPerfSample sample)
        {
            if(!Accepting||sample.Duration<0||(int)sample.Path<0||sample.Path>=LedgerPerfPath.Count){Interlocked.Increment(ref Dropped);return;}
            // A profiler never waits behind its writer. Bound lock holding to one value copy.
            if(!Monitor.TryEnter(gate)){Interlocked.Increment(ref Dropped);return;}
            try{if(!Accepting||count==ring.Length){Interlocked.Increment(ref Dropped);return;}
                long second=(sample.At-started)/Stopwatch.Frequency;if(second>rateSecond){rateSecond=second;rateCount=0;}
                if(rateCount>=limits.MaxSamplesPerSecond){Interlocked.Increment(ref Dropped);Interlocked.Increment(ref RateDropped);return;}rateCount++;
                ring[(head+count)%ring.Length]=sample;count++;Interlocked.Increment(ref Accepted);}
            finally{Monitor.Exit(gate);}
        }
        internal void Stop(string reason)
        {if(!Accepting)return;Reason=reason;InFlightAtStop=Interlocked.Read(ref InFlight);Accepting=false;try{wake.Set();}catch(ObjectDisposedException){}}
        internal int Drain(LedgerPerfSample[] batch)
        {lock(gate){int n=Math.Min(count,batch.Length);for(int i=0;i<n;i++)batch[i]=ring[(head+i)%ring.Length];head=(head+n)%ring.Length;count-=n;return n;}}
        private sealed class Aggregate
        {
            internal long Count;internal double Total,Max;internal readonly long[] Buckets=new long[15];
            internal void Add(double ms){Count++;Total+=ms;Max=Math.Max(Max,ms);int i=0;while(i<Bounds.Length-1&&ms>Bounds[i])i++;Buckets[i]++;}
            internal string Percentile(double fraction){long seen=0,target=(long)Math.Ceiling(Count*fraction);for(int i=0;i<Bounds.Length;i++){seen+=Buckets[i];if(seen>=target)return double.IsPositiveInfinity(Bounds[i])?">1000":F(Bounds[i]);}return "unknown";}
        }
        private static readonly double[] Bounds={.05,.1,.25,.5,1,2,5,10,20,50,100,250,500,1000,double.PositiveInfinity};
        private static string F(double n){return n.ToString("0.###",CultureInfo.InvariantCulture);}
        private sealed class OutputLimit : IOException { }
        private TextWriter output;private long bytes;private int fileIndex;private string stem;
        private void OpenOutput()
        {
            if(limits.Writer!=null)output=limits.Writer(fileIndex);
            else {
                Directory.CreateDirectory(root);if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked profiler root");
                string folder=Path.Combine(root,"profiler-v1");Directory.CreateDirectory(folder);
                if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked profiler directory");
                if(stem==null){Retain(folder);stem="LedgerPerf-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N");}
                string path=Path.Combine(folder,stem+"-"+fileIndex+".log");Output=path;
                output=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false));
            }
            Line("LEDGER-PERF-v1\t"+identity+"\tstart_utc="+startedUtc.ToString("o",CultureInfo.InvariantCulture)+"\tpart="+fileIndex+"\tfrequency="+Stopwatch.Frequency+"\tcapacity="+ring.Length+"\tmax_seconds="+limits.MaxSeconds+"\tmax_samples_per_second="+limits.MaxSamplesPerSecond+"\tslow_ms="+F(limits.SlowMilliseconds),true);
            Line("NOTE\tInclusive scopes overlap. main=registered API Update thread; other=worker/other callback. UpdateCallbackGap is callback spacing, NOT game frame time or proof of cause. Epoch is anonymous. In-flight scopes at stop may be omitted.",true);
        }
        private void Retain(string folder)
        {
            // Only this new format's owned files; legacy/operational diagnostics are never visited.
            var paths=Directory.EnumerateFiles(folder,"LedgerPerf-*.log").Take(65).ToArray();if(paths.Length>64)throw new IOException("Profiler retention enumeration bound");
            var owned=paths.Where(p=>Regex.IsMatch(Path.GetFileName(p),@"^LedgerPerf-\d{8}T\d{9}Z-[a-f0-9]{32}-[01]\.log$")).Select(p=>new FileInfo(p)).OrderBy(f=>f.LastWriteTimeUtc).ToList();
            long total=owned.Sum(f=>f.Length);
            while(owned.Count>14||total+4L*1024*1024>32L*1024*1024){if(owned.Count==0)throw new IOException("Profiler retention budget");var f=owned[0];owned.RemoveAt(0);
                if((f.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked profiler file");
                using(var exclusive=File.Open(f.FullName,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}File.Delete(f.FullName);total-=f.Length;}
        }
        private void Line(string text,bool final=false)
        {
            int size=Encoding.UTF8.GetByteCount(text)+2;
            int reserve=final?0:32768;
            if(bytes+size>limits.FileBytes-reserve){if(final)return;if(fileIndex+1>=limits.Files)throw new OutputLimit();output.Flush();output.Dispose();fileIndex++;bytes=0;OpenOutput();}
            output.WriteLine(text);bytes+=size;
        }
        private string Sample(LedgerPerfSample s,string kind)
        {return kind+"\tpath="+s.Path+"\tthread="+(s.Main?"main":"other")+"\tat_ms="+F(Milliseconds(s.At-started))+"\tduration_ms="+F(Milliseconds(s.Duration))+"\tupdate="+s.Update+"\tscope_epoch="+s.Epoch;}
        private void Summary(Aggregate[] stats,bool final,long writerTicks)
        {
            Line("SUMMARY\telapsed_ms="+F(Milliseconds(Stopwatch.GetTimestamp()-started))+"\tupdate="+Interlocked.Read(ref update)+"\tscope_epoch="+Volatile.Read(ref Epoch)+"\taccepted="+Interlocked.Read(ref Accepted)+"\tprocessed="+Processed+"\tdropped="+Interlocked.Read(ref Dropped)+"\trate_dropped="+Interlocked.Read(ref RateDropped)+"\tactive_scope_dropped="+Interlocked.Read(ref ActiveDropped)+"\tunprocessed="+(Interlocked.Read(ref Accepted)-Processed)+"\tinflight_at_stop="+Interlocked.Read(ref InFlightAtStop)+"\tslow_context_suppressed="+SlowSuppressed+"\twriter_busy_ms="+F(Milliseconds(writerTicks))+"\tstop="+(Reason??"running"),final);
            for(int i=0;i<stats.Length;i++){var a=stats[i];if(a.Count==0)continue;Line("AGG\tpath="+(LedgerPerfPath)(i/2)+"\tthread="+(i%2==0?"main":"other")+"\tcount="+a.Count+"\ttotal_ms="+F(a.Total)+"\tmax_ms="+F(a.Max)+"\tp50_upper_ms="+a.Percentile(.5)+"\tp95_upper_ms="+a.Percentile(.95),final);}
            if(queueSamples>0)Line("GAUGE\tpath=StoreQueueDepth\tsamples="+queueSamples+"\tlast="+queueLast+"\tmax="+queueMax,final);
        }
        private void Work()
        {
            var stats=new Aggregate[(int)LedgerPerfPath.Count*2];for(int i=0;i<stats.Length;i++)stats[i]=new Aggregate();
            var batch=new LedgerPerfSample[128];var recent=new LedgerPerfSample[16];int recentAt=0,recentCount=0,after=0;long lastSlow=-Stopwatch.Frequency,nextSummary=started,nextFlush=started,writerTicks=0;
            try{
                OpenOutput();output.Flush();
                while(true){long workAt=Stopwatch.GetTimestamp();int n=Drain(batch);
                    for(int j=0;j<n;j++){var s=batch[j];Processed++;if(s.Path==LedgerPerfPath.StoreQueueDepth){queueSamples++;queueLast=s.Value;queueMax=Math.Max(queueMax,s.Value);continue;}
                        double ms=Milliseconds(s.Duration);stats[(int)s.Path*2+(s.Main?0:1)].Add(ms);
                        if(ms>=limits.SlowMilliseconds){if(s.At-lastSlow>=Stopwatch.Frequency){for(int k=0;k<recentCount;k++)Line(Sample(recent[(recentAt-recentCount+k+recent.Length)%recent.Length],"BEFORE"));Line(Sample(s,"SLOW"));lastSlow=s.At;after=8;}else SlowSuppressed++;}
                        else if(after>0){Line(Sample(s,"AFTER"));after--;}
                        recent[recentAt]=s;recentAt=(recentAt+1)%recent.Length;recentCount=Math.Min(recentCount+1,recent.Length);
                    }
                    long now=Stopwatch.GetTimestamp();writerTicks+=now-workAt;
                    if(Milliseconds(now-started)>=limits.MaxSeconds*1000L)Stop("time-limit");
                    if(now>=nextSummary){Summary(stats,false,writerTicks);nextSummary=now+(long)(Stopwatch.Frequency*limits.SummaryMilliseconds/1000.0);}
                    if(now>=nextFlush){int emitted=0;for(int k=0;k<activeTokens.Length&&emitted<16;k++){long token=Volatile.Read(ref activeTokens[k]);if(token<=0)continue;var s=activeSamples[k];
                            if(s.Token!=token||Volatile.Read(ref activeTokens[k])!=token||Milliseconds(now-s.At)<100)continue;s.Duration=now-s.At;Line(Sample(s,"ACTIVE_INCOMPLETE"));emitted++;}
                        output.Flush();nextFlush=now+(long)(Stopwatch.Frequency*limits.FlushMilliseconds/1000.0);}
                    writerTicks+=Stopwatch.GetTimestamp()-now;
                    if(!Accepting&&n==0)break;
                    if(n==0)wake.WaitOne(limits.FlushMilliseconds);
                }
                Summary(stats,true,writerTicks);output.Flush();
            }
            catch(OutputLimit){Stop("byte-limit");try{Summary(stats,true,writerTicks);output.Flush();}catch(Exception){Error="Final summary write failed";}}
            catch(Exception e){Error=e.GetType().Name;Stop("output-error");}
            finally{Accepting=false;try{if(output!=null)output.Dispose();}catch(Exception e){Error=e.GetType().Name;}Done=true;wake.Dispose();}
        }
    }
}
