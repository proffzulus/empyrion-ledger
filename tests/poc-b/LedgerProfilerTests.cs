using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal static class LedgerProfilerTests
    {
        private sealed class FailingWriter : StringWriter {public override void WriteLine(string s){throw new IOException("fixture-only");}}
        private sealed class BlockedWriter : StringWriter
        {
            internal readonly ManualResetEvent Entered=new ManualResetEvent(false),Release=new ManualResetEvent(false);private int first;
            public override void WriteLine(string s){if(Interlocked.Exchange(ref first,1)==0){Entered.Set();Release.WaitOne(5000);}base.WriteLine(s);}
        }
        private static bool Wait(Func<bool> done,int ms=4000){var sw=Stopwatch.StartNew();while(!done()&&sw.ElapsedMilliseconds<ms)Thread.Sleep(5);return done();}
        private static LedgerPerfSample Sample(double ms,int offset=0,LedgerPerfPath path=LedgerPerfPath.ScannerHud)
        {return new LedgerPerfSample{Path=path,At=Stopwatch.GetTimestamp()+offset*Stopwatch.Frequency,Duration=(long)(ms*Stopwatch.Frequency/1000),Main=true,Epoch=2,Update=17,Slot=-1};}
        private static LedgerProfileSession Session(string root,LedgerProfileLimits l)
        {return new LedgerProfileSession(root,"fixture build=5188 version=0.3.12.0",Thread.CurrentThread.ManagedThreadId,l);}
        [MethodImpl(MethodImplOptions.NoInlining)] private static void Baseline(int n){for(int i=0;i<n;i++)Noop();}
        [MethodImpl(MethodImplOptions.NoInlining)] private static void Noop() { }
        [MethodImpl(MethodImplOptions.NoInlining)] private static void Measured(int n){for(int i=0;i<n;i++)using(LedgerProfiler.Measure(LedgerPerfPath.Update)){} }
        private static double Time(Action action){var sw=Stopwatch.StartNew();action();return sw.Elapsed.TotalMilliseconds;}
        internal static void Run(Action<bool,string> check,string root)
        {
            int count=0;Action<bool,string> c=(v,message)=>{check(v,"profiler: "+message);count++;};string folder=Path.Combine(root,"profiler-fixtures");Directory.CreateDirectory(folder);
            c(!LedgerProfiler.Enabled&&LedgerProfiler.State=="Off","startup/default remains off");
            LedgerProfiler.Configure(Path.Combine(folder,"disabled"),"fixture","guarded-native",Thread.CurrentThread.ManagedThreadId,true,"Warn");
            for(int i=0;i<10000;i++){LedgerProfiler.Tick();LedgerProfiler.ScopeChanged();using(LedgerProfiler.Measure(LedgerPerfPath.Update)){} }
            c(!Directory.Exists(Path.Combine(folder,"disabled")),"disabled timing/ticks/scope changes create no folder/file");
            c(LedgerProfiler.Output==null&&LedgerProfiler.ButtonLabel=="Profile: Off","default control visible, no fabricated output path");
            var limits=new LedgerProfileLimits();c(limits.Capacity==4096&&limits.MaxSamplesPerSecond==6000&&limits.MaxSeconds==600,"runtime bounded sample and ten-minute limits");
            c(limits.FileBytes==2097152&&limits.Files==2&&limits.FlushMilliseconds==1000&&limits.SummaryMilliseconds==5000,"2x2MiB output,1s flush,5s aggregate cadence");
            c(Math.Abs(LedgerProfileSession.Milliseconds(Stopwatch.Frequency)-1000)<.0001,"monotonic timestamp frequency converted to milliseconds");
            c(typeof(LedgerPerfSample).IsValueType&&typeof(LedgerPerfSample).GetFields(BindingFlags.Instance|BindingFlags.NonPublic).All(f=>f.FieldType.IsValueType),"samples cannot retain text, inventory, identifiers or native objects");

            var text=new StringWriter();var s=Session(folder,new LedgerProfileLimits{Capacity=8,Writer=i=>text});
            for(int i=0;i<12;i++)s.Record(Sample(1));c(s.Accepted==8&&s.Dropped==4,"fixed buffer drops rather than grows");
            var batch=new LedgerPerfSample[3];c(s.Drain(batch)==3&&s.Drain(batch)==3&&s.Drain(batch)==2&&s.Drain(batch)==0,"bounded batches preserve all admitted records");
            s.Stop("fixture");s.Start();c(Wait(()=>s.Done)&&s.Error==null,"off-before-start and empty drain terminate safely");
            var rate=Session(folder,new LedgerProfileLimits{Capacity=16,MaxSamplesPerSecond=3,Writer=i=>new StringWriter()});
            for(int i=0;i<10;i++)rate.Record(Sample(1));c(rate.Accepted==3&&rate.RateDropped==7,"per-second sample quota independent from capacity");
            rate.Record(Sample(1,2));c(rate.Accepted==4,"monotonic next window admits new work");rate.Stop("fixture");rate.Start();c(Wait(()=>rate.Done),"rate-limited session drains");

            var contention=Session(folder,new LedgerProfileLimits{Writer=i=>new StringWriter()});
            var gate=typeof(LedgerProfileSession).GetField("gate",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(contention);var held=new ManualResetEvent(false);var release=new ManualResetEvent(false);
            var holder=new Thread(()=>{lock(gate){held.Set();release.WaitOne(3000);}});holder.Start();c(held.WaitOne(1000),"test writer lock acquired");
            var clock=Stopwatch.StartNew();contention.Record(Sample(1));clock.Stop();release.Set();holder.Join();
            c(clock.ElapsedMilliseconds<100&&contention.Dropped==1,"producer does not wait behind profiler buffer lock");contention.Stop("fixture");contention.Start();c(Wait(()=>contention.Done),"contended session shuts down");

            var report=new StringWriter();var summary=Session(folder,new LedgerProfileLimits{Capacity=64,Writer=i=>report});
            foreach(double ms in new[]{.01,.05,.1,1.0,9.999,10.0,20.0})summary.Record(Sample(ms));summary.Stop("fixture");summary.Start();c(Wait(()=>summary.Done)&&summary.Error==null,"summary worker drains and closes");
            string output=report.ToString();c(output.Contains("count=7")&&output.Contains("max_ms=20")&&output.Contains("total_ms=41.159"),"aggregate count/total/max in milliseconds");
            c(output.Contains("p50_upper_ms=1")&&output.Contains("p95_upper_ms=20"),"histogram percentile upper bounds are explicit");
            c(output.Contains("SLOW\tpath=ScannerHud")&&output.Contains("duration_ms=10"),"exact slow threshold is included");
            c(!System.Text.RegularExpressions.Regex.IsMatch(output,@"(?m)^SLOW[^\r\n]*duration_ms=9\.999"),"subthreshold sample not promoted");
            c(output.Contains("BEFORE\t")&&output.Contains("slow_context_suppressed=1"),"bounded recent context and repeated slow-event suppression");
            c(output.Contains("scope_epoch=2")&&output.Contains("update=17")&&output.Contains("NOT game frame time"),"anonymous scope/tick context and causality limits retained");

            var activeText=new StringWriter();var active=Session(folder,new LedgerProfileLimits{Writer=i=>activeText,FlushMilliseconds=10});var began=active.Begin(LedgerPerfPath.PublicationGateWait);active.Start();Thread.Sleep(160);active.Stop("fixture");
            c(Wait(()=>active.Done)&&activeText.ToString().Contains("ACTIVE_INCOMPLETE\tpath=PublicationGateWait"),"independent writer can report an unfinished/hung owned scope");
            c(activeText.ToString().Contains("inflight_at_stop=1"),"unfinished scopes at stop acknowledged explicitly");active.End(began);
            active.End(began);c(active.InFlight==0,"same claimed scope cannot be counted/released twice");
            var slots=Session(folder,new LedgerProfileLimits{Writer=i=>new StringWriter()});var starts=new List<LedgerPerfSample>();for(int i=0;i<70;i++)starts.Add(slots.Begin(LedgerPerfPath.Update));
            c(slots.ActiveDropped==6,"active-scope identity slots are bounded at64");foreach(var a in starts)slots.End(a);c(slots.InFlight==0,"all scopes released including overflow");slots.Stop("fixture");slots.Start();c(Wait(()=>slots.Done),"active-slot overflow cannot leak worker");

            var blocked=new BlockedWriter();var pressure=Session(folder,new LedgerProfileLimits{Capacity=8,Writer=i=>blocked});pressure.Start();c(blocked.Entered.WaitOne(1000),"slow filesystem fixture entered only on worker");
            for(int i=0;i<100;i++)pressure.Record(Sample(1));clock.Restart();pressure.Stop("manual");clock.Stop();
            double blockedStopMs=clock.Elapsed.TotalMilliseconds;
            c(clock.ElapsedMilliseconds<100&&!pressure.Done&&pressure.Dropped>=92,"disable is nonblocking despite output stall, buffer stays bounded");blocked.Release.Set();c(Wait(()=>pressure.Done),"released output finishes bounded async flush");
            c(pressure.Processed==pressure.Accepted,"normal stop drains admitted completed samples");
            var stalledWriter=new BlockedWriter();var stalled=Session(folder,new LedgerProfileLimits{MaxSeconds=0,Writer=i=>stalledWriter});stalled.Start();c(stalledWriter.Entered.WaitOne(1000),"time-expiry output fixture is actually blocked");stalled.Tick();
            c(!stalled.Accepting&&stalled.Reason=="time-limit","API tick expires recording even when the writer cannot run");stalledWriter.Release.Set();c(Wait(()=>stalled.Done),"expired stalled writer closes after release");
            var fail=Session(folder,new LedgerProfileLimits{Writer=i=>new FailingWriter()});fail.Start();c(Wait(()=>fail.Done)&&fail.Error=="IOException"&&!fail.Accepting,"writer failure isolates profiler and exposes error");
            fail.Record(Sample(1));c(fail.Dropped>0,"failed profiler cannot accept unseen work as saved");
            var timeout=Session(folder,new LedgerProfileLimits{MaxSeconds=0,Writer=i=>new StringWriter()});timeout.Start();c(Wait(()=>timeout.Done)&&timeout.Reason=="time-limit","automatic expiry disables session");
            var files=new List<StringWriter>();var rotation=Session(folder,new LedgerProfileLimits{Capacity=128,FileBytes=36000,Files=2,Writer=i=>{var w=new StringWriter();files.Add(w);return w;}});
            for(int i=0;i<100;i++)rotation.Record(Sample(20,i+1));rotation.Start();c(Wait(()=>rotation.Done)&&rotation.Reason=="byte-limit","output size cap automatically stops tracing");
            c(files.Count==2&&files.All(w=>Encoding.UTF8.GetByteCount(w.ToString())<=36000),"bounded rotation and final-summary reserve");
            c(files.All(w=>w.ToString().Contains("LEDGER-PERF-v1"))&&files.Last().ToString().Contains("unprocessed="),"every part has identity and final backpressure accounting");

            string keep=Path.Combine(folder,"retention"),managed=Path.Combine(keep,"profiler-v1");Directory.CreateDirectory(managed);File.WriteAllText(Path.Combine(keep,"legacy.log"),"preserve");File.WriteAllText(Path.Combine(managed,"personal.txt"),"preserve");
            for(int i=0;i<16;i++){string p=Path.Combine(managed,"LedgerPerf-20261111T000000000Z-"+i.ToString("x32")+"-0.log");File.WriteAllText(p,"old owned report");File.SetLastWriteTimeUtc(p,DateTime.UtcNow.AddDays(-2).AddSeconds(i));}
            var retained=Session(keep,new LedgerProfileLimits{MaxSeconds=0});retained.Start();c(Wait(()=>retained.Done)&&retained.Error==null,"real owned-file retention/write/close succeeds");
            c(Directory.GetFiles(managed,"LedgerPerf-*.log").Length==15,"retention reserves two parts without broad enumeration");
            c(File.ReadAllText(Path.Combine(keep,"legacy.log"))=="preserve"&&File.ReadAllText(Path.Combine(managed,"personal.txt"))=="preserve","unrelated/legacy evidence never pruned");
            c(File.ReadAllText(retained.Output).Contains("stop=time-limit"),"final summary survives normal owned-worker shutdown");
            string lockedRoot=Path.Combine(folder,"locked-retention"),lockedFolder=Path.Combine(lockedRoot,"profiler-v1");Directory.CreateDirectory(lockedFolder);
            for(int i=0;i<16;i++){string p=Path.Combine(lockedFolder,"LedgerPerf-20261111T000000000Z-"+i.ToString("x32")+"-0.log");File.WriteAllText(p,"preserve locked");File.SetLastWriteTimeUtc(p,DateTime.UtcNow.AddDays(-3).AddSeconds(i));}
            string oldest=Path.Combine(lockedFolder,"LedgerPerf-20261111T000000000Z-"+0.ToString("x32")+"-0.log");
            using(var lease=File.Open(oldest,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){var locked=Session(lockedRoot,new LedgerProfileLimits{MaxSeconds=0});locked.Start();c(Wait(()=>locked.Done)&&locked.Error=="IOException","retention fails closed on an exclusive file lock");}
            c(Directory.GetFiles(lockedFolder).Length==16&&File.ReadAllText(oldest)=="preserve locked","locked prior reports are not force-deleted or overwritten");
            var gaugeText=new StringWriter();var gauge=Session(folder,new LedgerProfileLimits{Writer=i=>gaugeText});gauge.QueueDepth(3);gauge.QueueDepth(8);gauge.QueueDepth(0);gauge.Stop("fixture");gauge.Start();c(Wait(()=>gauge.Done)&&gaugeText.ToString().Contains("GAUGE\tpath=StoreQueueDepth\tsamples=3\tlast=0\tmax=8"),"queue depth measured as records, separate from duration histogram");
#if !PROFILER_SOURCE_ONLY
            Integration(c,folder);
#endif

            string real=Path.Combine(folder,"toggle");LedgerProfiler.Configure(real,"fixture","native-contract",Thread.CurrentThread.ManagedThreadId,true,"Warn");
            for(int pass=0;pass<3;pass++){
                c(LedgerProfiler.SetEnabled(true)&&LedgerProfiler.Enabled&&LedgerProfiler.ButtonLabel=="Profile: On","explicit enable cycle "+pass);
                c(LedgerProfiler.SetEnabled(true),"repeat enable is idempotent "+pass);
                LedgerProfiler.Tick();using(LedgerProfiler.Measure(LedgerPerfPath.UiRender)){}LedgerProfiler.ScopeChanged();using(LedgerProfiler.Measure(LedgerPerfPath.ScannerHud)){};
                c(LedgerProfiler.Enabled,"UI close/reopen has no implicit stop or new-hook dependency "+pass);
                LedgerProfiler.SetEnabled(false);c(Wait(()=>LedgerProfiler.State=="Off"),"disable flush completes cycle "+pass);
                LedgerProfiler.SetEnabled(false);c(!LedgerProfiler.Enabled,"repeat disable remains off "+pass);
            }
            var saved=Directory.GetFiles(Path.Combine(real,"profiler-v1"),"*.log");c(saved.Length==3,"re-enable creates separate bounded recordings, not duplicate hooks/writers");
            c(saved.All(p=>File.ReadAllText(p).Contains("fingerprint=fixture")&&File.ReadAllText(p).Contains("nativeHash=native-contract")&&!File.ReadAllText(p).Contains("SavePath")),"report contains safe build configuration, no save path field");
            c(saved.All(p=>System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(p),@"(?m)^SUMMARY[^\r\n]*scope_epoch=1")),"scope changes persist in summaries even when fast samples drop");

            // Steady-state measurements: this process/.NET, not Unity frame timings. Warm JIT first.
            const int n=200000;Measured(1000);Baseline(1000);
            var allocMethod=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Public|BindingFlags.Static);
            Func<long> allocated=allocMethod==null?null:(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),allocMethod);
            var timer=new Stopwatch();long allocationBefore=allocated==null?0:allocated();timer.Start();Measured(n);timer.Stop();long allocationAfter=allocated==null?0:allocated();
            double off=timer.Elapsed.TotalMilliseconds,baseline=Time(()=>Baseline(n));
            c(allocated==null||allocationAfter-allocationBefore<=128,"disabled sample loop allocates no per-sample objects (clock object excluded)");
            c(!LedgerProfiler.Enabled,"benchmark does not leave tracing enabled");
            double enableMs=Time(()=>LedgerProfiler.SetEnabled(true));var current=(LedgerProfileSession)typeof(LedgerProfiler).GetField("current",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            long acceptedBefore=Interlocked.Read(ref current.Accepted),droppedBefore=Interlocked.Read(ref current.Dropped);double normal=Time(()=>Measured(1000));long normalAccepted=Interlocked.Read(ref current.Accepted)-acceptedBefore,normalDropped=Interlocked.Read(ref current.Dropped)-droppedBefore;
            double on=Time(()=>Measured(n));double disableMs=Time(()=>LedgerProfiler.SetEnabled(false));var flushClock=Stopwatch.StartNew();c(Wait(()=>LedgerProfiler.State=="Off"),"enabled overhead benchmark stops and flushes");flushClock.Stop();
            c(normalAccepted+normalDropped>=1000,"non-saturated enabled benchmark discloses admitted/dropped counts (including any concurrent owned work)");
            string metrics="Environment="+Environment.Version+";64bit="+Environment.Is64BitProcess+";StopwatchFrequency="+Stopwatch.Frequency+";logicalProcessors="+Environment.ProcessorCount+"\r\nIterations="+n+"\r\nBaseline_ms="+baseline.ToString("F6",CultureInfo.InvariantCulture)+"\r\nDisabled_ms="+off.ToString("F6",CultureInfo.InvariantCulture)+"\r\nDisabled_ns_per_scope="+(off*1000000/n).ToString("F3",CultureInfo.InvariantCulture)+"\r\nEnabled_ms="+on.ToString("F6",CultureInfo.InvariantCulture)+"\r\nEnabled_ns_per_scope="+(on*1000000/n).ToString("F3",CultureInfo.InvariantCulture)+"\r\nDisabled_allocated_bytes="+(allocated==null?"unavailable":(allocationAfter-allocationBefore).ToString())+"\r\nEnabled benchmark includes intentional rate-limit/backpressure saturation; no gameplay claim.\r\n";
            metrics+="Enabled_normal_ns_per_scope="+(normal*1000000/1000).ToString("F3",CultureInfo.InvariantCulture)+"\r\nEnabled_normal_accepted="+normalAccepted+"\r\nEnabled_normal_dropped="+normalDropped+"\r\n";
            metrics+="Enable_call_ms="+enableMs.ToString("F6",CultureInfo.InvariantCulture)+"\r\nDisable_call_ms="+disableMs.ToString("F6",CultureInfo.InvariantCulture)+"\r\nAsync_flush_wait_ms="+flushClock.Elapsed.TotalMilliseconds.ToString("F6",CultureInfo.InvariantCulture)+"\r\nBlocked_writer_stop_call_ms="+blockedStopMs.ToString("F6",CultureInfo.InvariantCulture)+"\r\n";
            File.WriteAllText(Path.Combine(root,"profiler-overhead.txt"),metrics);
            foreach(var size in new[]{new Size(1280,720),new Size(1920,1080),new Size(2560,1440)})c(Preview(root,size),"profile state control non-game preview "+size.Width);
            File.WriteAllText(Path.Combine(root,"profiler-checks.txt"),count.ToString(CultureInfo.InvariantCulture));
        }
#if !PROFILER_SOURCE_ONLY
        private static void Integration(Action<bool,string> c,string folder)
        {
            LedgerProfiler.Configure(Path.Combine(folder,"integration-profile"),"fixture-integration","native-contract",Thread.CurrentThread.ManagedThreadId,true,"Warn");
            c(LedgerProfiler.SetEnabled(true),"actual storage/snapshot integration explicitly starts profiling");
            string storeRoot=Path.Combine(folder,"integration-store");var context=LedgerContext.Create(Path.Combine(folder,"synthetic-save"),"Fixture PF","Fixture system","SinglePlayer");
            var observation=new LedgerObservation{Kind="market",Context=context,Utc=DateTime.UtcNow.ToString("o"),Source="private-fixture-source",FullCurrentAssortment=true,
                Identity=new LedgerIdentity{NpcEntityId=3003,StructureEntityId=1065,OwnerConsistent=true},Offers=new List<LedgerOffer>{new LedgerOffer{ItemId=101,AuxiliaryRaw=0,Stock=0,Capacity=10,RemainingNpcBuyingCapacity=10,NpcBuysSupported=true,NpcSellsSupported=true}}};
            using(var store=new LedgerStore(storeRoot)){
                c(store.Enqueue(observation)&&store.WaitForIdle(5000)&&store.Status.Committed==1,"profiling does not change actual observed-zero storage commit");
                LedgerViewData data=null;Exception failure=null;var reader=new Thread(()=>{try{data=LedgerViewData.Read(Path.Combine(storeRoot,LedgerDatabase.FileName),context.WorldKey);}catch(Exception e){failure=e;}});reader.Start();bool ended=reader.Join(5000);
                c(ended&&failure==null&&data!=null&&data.Markets.Single().Offers.Single().Stock==0,"actual worker snapshot retains observed zero with profiling enabled");
                LedgerProfiler.SetEnabled(false);c(Wait(()=>LedgerProfiler.State=="Off"),"actual storage profile drains without a gameplay-thread join");string report=File.ReadAllText(LedgerProfiler.Output);
                bool dropped=System.Text.RegularExpressions.Regex.Matches(report,@"\tdropped=(\d+)").Cast<System.Text.RegularExpressions.Match>().Any(m=>long.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)>0);
                foreach(string path in new[]{"StoreEnqueue","StoreOpen","SqliteSave","SnapshotRead"})c(report.Contains("AGG\tpath="+path)||dropped,"actual path sampled or loss explicitly acknowledged: "+path);
                c(report.Contains("AGG\tpath=SqliteSave\tthread=other")&&report.Contains("GAUGE\tpath=StoreQueueDepth")||dropped,"SQL/queue attribution or explicit contention loss, never invented samples");
                c(!report.Contains("private-fixture-source")&&!report.Contains(context.SavePath),"actual stored payload/save path cannot leak into timing output");
                string bad=Path.Combine(folder,"blocked-profile-root");File.WriteAllText(bad,"owned fixture obstruction");LedgerProfiler.Configure(bad,"fixture","native-contract",Thread.CurrentThread.ManagedThreadId,true,"Warn");LedgerProfiler.SetEnabled(true);
                c(Wait(()=>LedgerProfiler.State=="Error"),"real output-path failure exposes Error");var next=LedgerJson.Decode<LedgerObservation>(LedgerJson.Encode(observation));next.ObservationId=Guid.NewGuid().ToString("N");next.Utc=DateTime.UtcNow.AddSeconds(1).ToString("o");next.Sequence=2;
                c(store.Enqueue(next)&&store.WaitForIdle(5000)&&store.Status.Committed==2,"profiler output failure cannot revoke capture/storage readiness");LedgerProfiler.SetEnabled(false);
            }
        }
#endif
        private static bool Preview(string root,Size size)
        {
            var layout=LedgerLayout.For(size.Width,size.Height);float x=(layout.Width-252)*layout.Scale,width=150*layout.Scale;
            using(var bmp=new Bitmap(size.Width,size.Height))using(var g=Graphics.FromImage(bmp))using(var f=new Font("Segoe UI",15*layout.Scale,FontStyle.Regular,GraphicsUnit.Pixel)){
                g.Clear(Color.FromArgb(15,25,35));g.DrawString("Trade Ledger",f,Brushes.White,18*layout.Scale,16*layout.Scale);
                int y=12;foreach(string label in new[]{"Profile: Off","Profile: On","Profile: Saving","Profile: Error"}){g.DrawRectangle(Pens.SlateGray,x,y*layout.Scale,width,28*layout.Scale);g.DrawString(label,f,Brushes.White,x+6*layout.Scale,(y+4)*layout.Scale);y+=40;}
                g.DrawString("Session only | 10 minute limit | stop flushes asynchronously",f,Brushes.Silver,18*layout.Scale,205*layout.Scale);bmp.Save(Path.Combine(root,"profiler-control-"+size.Width+".png"));
                return x>700*layout.Scale&&x+width<(layout.Width-94)*layout.Scale&&g.MeasureString("Profile: Saving",f).Width<width-8*layout.Scale;
            }
        }
    }
}
