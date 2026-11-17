using System;
using System.Collections.Generic;
using System.IO;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal static class LedgerDepositHudWorkTests
    {
        private sealed class Fixture
        {
            internal readonly LedgerDepositHudWork Work=new LedgerDepositHudWork();
            internal object World=new object(),Actor=new object(),Controller=new object(),Root=new object(),Views=new object(),Published=new object();
            internal string Scope="save|world|surface|42";
            internal ulong Tick=100;
            internal int Parses,Dtos,Hashes;
            internal void Bind(){Work.Bind(Scope,World,Actor,Controller,Root,Views,Published,Tick);}
            internal LedgerDepositHudSample Sample(string body="Small 100%")
            {return new LedgerDepositHudSample{Marker=new object(),View=new object(),Label=new object(),Resource=new object(),Position=123,Definition=45,Id=1,SizeBand=3,Body=body};}
            // Offline admission fixture; native wiring/order is checked separately below.
            internal LedgerProductionSnapshot Observe(LedgerDepositHudSample s,bool visible=true,bool published=true,bool final=true,string rendered=null)
            {
                Bind();if(!visible||!published){Work.Forget(s.Marker);return null;}
                if(Work.Unchanged(s))return null;
                Parses++;var d=LedgerDepositDetails.Read(s.Body,rendered??s.Body,s.Body.StartsWith("Medium",StringComparison.Ordinal)?"Medium":"Small",true);
                LedgerProductionSnapshot p=null;
                if(d!=null){Dtos++;p=new LedgerProductionSnapshot{Kind="deposit",Key=s.Id.ToString(),Tick=Tick};d.Apply(p,Tick,"HUD work fixture");}
                if(!final){Work.Forget(s.Marker);return null;}
                Work.Remember(s);if(p!=null){Hashes++;LedgerProduction.ContentKey(p);}return p;
            }
        }
        public static void Run(Action<bool,string> check)
        {
            int n=0;Action<bool,string> test=(ok,msg)=>{n++;check(ok,"HUD work: "+msg);};
            var f=new Fixture();var s=f.Sample();var first=f.Observe(s);
            test(first!=null&&first.RemainingPercent==100&&f.Parses==1&&f.Dtos==1&&f.Hashes==1,"first admitted visible sample captures normally");
            for(int i=0;i<1000;i++){f.Tick++;f.Observe(s,rendered:s.Body+"\n"+i+" m");}
            test(f.Parses==1&&f.Dtos==1&&f.Hashes==1,"1000 unchanged/distance-only repeats add zero parse/DTO/content-hash work");
            s.Body="Small 60%";var changed=f.Observe(s);test(changed!=null&&changed.RemainingPercent==60&&f.Parses==2,"legitimate remaining change still captures");
            s.Body="Medium 60%";s.SizeBand=5;changed=f.Observe(s);test(changed!=null&&changed.Size=="Medium"&&changed.PercentageText=="60%"&&f.Parses==3,"size/band change invalidates memo even for same marker");
            f.Observe(s,visible:false);test(!f.Work.Unchanged(s),"hidden sample invalidates memo, cached body is not permission");
            test(f.Observe(s)!=null,"reappearance captures again");
            f.Observe(s,published:false);test(!f.Work.Unchanged(s),"unbound/mixed publication invalidates memo");
            test(f.Observe(s)!=null,"republication captures again");
            var rebound=s;rebound.View=new object();test(f.Observe(rebound)!=null,"view rebind captures again");
            rebound.Label=new object();test(f.Observe(rebound)!=null,"UILabel rebind captures again");
            rebound.Resource=new object();test(f.Observe(rebound)!=null,"resource rebind captures again even at same ID/name");
            rebound.Marker=new object();test(f.Observe(rebound)!=null,"marker replacement remains independent");
            rebound.Id=2;test(f.Observe(rebound)!=null,"numeric identity mutation invalidates memo");
            rebound.Position=124;test(f.Observe(rebound)!=null,"position mutation invalidates memo");
            rebound.Definition=46;test(f.Observe(rebound)!=null,"definition/variant mutation invalidates memo");
            rebound.SizeBand++;test(f.Observe(rebound)!=null,"changed localized size source invalidates memo even before body changes");
            var neighbor=f.Sample(rebound.Body);neighbor.Id=rebound.Id;test(f.Observe(neighbor)!=null&&f.Work.Unchanged(rebound),"same-resource/ID-looking neighbor does not alias another marker object");
            var rejected=f.Sample();int before=f.Work.Remembered;test(f.Observe(rejected,visible:false)==null&&f.Work.Remembered==before&&!f.Work.Unchanged(rejected),"initial admission failure caches no fact");
            test(f.Observe(rejected,final:false)==null&&!f.Work.Unchanged(rejected),"failed final publication revalidation caches no fact");
            test(f.Observe(rejected)!=null,"failed final validation retries normally on next admitted poll");
            var partial=f.Sample("Iron Deposit");before=f.Parses;test(f.Observe(partial)==null&&f.Parses==before+1,"name-only partial never fabricates size or percentage");
            f.Observe(partial);test(f.Parses==before+1,"unchanged admitted partial text avoids repeated parsing too");
            partial.Body="Small <5%";var rounded=f.Observe(partial);test(rounded!=null&&rounded.RemainingPercent==null&&rounded.PercentageText=="<5%","later complete rounded display captured without precise hidden depletion");
            partial.Body="Small 0%";test(f.Observe(partial).RemainingPercent==0,"observed zero is retained as zero");
            f.Work.Reset();test(!f.Work.Unchanged(partial)&&f.Work.Remembered==0,"unload/modal/no-HUD reset drops all native reference memos");
            test(f.Observe(partial)!=null,"same-scope reopening after reset captures anew");
            foreach(string scope in new[]{"other-save|world|surface|42","save|other-world|surface|42","save|world|moon|42","save|world|surface|43"}){f.Scope=scope;test(f.Observe(partial)!=null,"scope change captures anew: "+scope);}
            f.World=new object();test(f.Observe(partial)!=null,"world reference replacement invalidates same textual scope");
            f.Actor=new object();test(f.Observe(partial)!=null,"local actor replacement invalidates same player ID");
            f.Controller=new object();test(f.Observe(partial)!=null,"controller replacement invalidates memo");
            f.Root=new object();test(f.Observe(partial)!=null,"root replacement invalidates memo");
            f.Views=new object();test(f.Observe(partial)!=null,"binding dictionary replacement invalidates memo");
            f.Published=new object();test(f.Observe(partial)!=null,"native publication list replacement invalidates memo");
            f.Tick=1;test(f.Observe(partial)!=null,"game-time rollback invalidates memo");
            var work=new LedgerDepositHudWork();int start;test(work.Take(0,out start)==0&&work.Take(1025,out start)==0,"empty/oversized publication fails bounded without indexing");
            work.Bind("scope",f.World,f.Actor,f.Controller,f.Root,f.Views,f.Published,1);
            var seen=new HashSet<int>();int reads=0,max=0;
            for(int pass=0;pass<16;pass++){int take=work.Take(1024,out start);max=Math.Max(max,take);for(int i=0;i<take;i++){seen.Add((start+i)%1024);reads++;}}
            test(reads==1024&&seen.Count==1024&&max==64,"1024 bindings traversed fairly in16 polls, at most64 indexed reads/poll, no prefix/full-copy work");
            test(work.Take(1024,out start)==64&&start==0,"round-robin wraps deterministically");
            work.Remember(partial);var replacement=new object();work.Bind("scope",f.World,f.Actor,f.Controller,f.Root,f.Views,replacement,2);
            test(work.Remembered==0&&work.Take(1024,out start)==64&&start==64,"publication replacement clears memo but preserves round-robin progress");
            work.Bind("scope",f.World,f.Actor,f.Controller,f.Root,new object(),replacement,3);
            test(work.Take(1024,out start)==64&&start==128,"binding dictionary replacement also preserves fair progress");
            work.Reset();seen.Clear();for(int pass=0;pass<3;pass++){int take=work.Take(130,out start);for(int i=0;i<take;i++)seen.Add((start+i)%130);}
            test(seen.Count==130,"sparse/non-multiple batch reaches every entry rather than restarting at first64");
            test(work.Take(1,out start)==1&&start==0,"shrinking publication cannot leave invalid cursor");
            work.Reset();for(int i=0;i<1025;i++){var sample=f.Sample();sample.Id=i;work.Remember(sample);}
            test(work.Remembered<=LedgerDepositHudWork.Limit,"native-reference memo stays bounded at1024");
            string checkout=AppDomain.CurrentDomain.BaseDirectory;while(checkout!=null&&!File.Exists(Path.Combine(checkout,"PROJECT_BRIEF.md")))checkout=Path.GetDirectoryName(checkout.TrimEnd(Path.DirectorySeparatorChar));
            string source=File.ReadAllText(Path.Combine(checkout,"src/TradeLedgerProbeB/LedgerDepositHudNative.cs"));
            int gate=source.IndexOf("if(hudWork.Unchanged(sample))continue;",StringComparison.Ordinal),parse=source.IndexOf("LedgerDepositDetails.Read(",StringComparison.Ordinal),dto=source.IndexOf("var p=Deposit(",StringComparison.Ordinal);
            test(gate>=0&&gate<parse&&parse<dto,"native unchanged gate precedes parsing/DTO/Capture and thus downstream JSON/hash");
            test(source.IndexOf("if(!VisibleDepositLabel(label))",StringComparison.Ordinal)<gate&&source.IndexOf("if(!PublishedDepositBody(body,rendered))",StringComparison.Ordinal)<gate,"current visibility/publication required before any memo lookup");
            test(!source.Contains("new List<DictionaryEntry>")&&!source.Contains("in views)")&&source.Contains("F(0xab72,controller) as IList"),"no full dictionary copy/enumeration; native published indexed list only");
            test(source.Split(new[]{"HudCurrent(entry,controller,root,world)"},StringSplitOptions.None).Length-1==2,"common HUD configuration checked twice per batch, not per accepted row");
            int commit=source.IndexOf("hudWork.Remember(pending.Sample)",StringComparison.Ordinal);
            test(commit>source.IndexOf("if(stack.Count!=0||",StringComparison.Ordinal)&&commit>source.IndexOf("if(!HudRowCurrent(",StringComparison.Ordinal),"both common and row final revalidation precede committing memo/fact");
            test(source.Contains("hudPending.Clear();}")&&source.Contains("hudWork.Forget(pending.Sample.Marker)"),"pending references cleared in finally; final row failure invalidates memo");
            test(source.Contains("rendered.IndexOf(body,StringComparison.Ordinal)>=0||"),"verbatim native publication avoids repeated rich-text normalization");
            test(source.Contains("if(published.Count!=publicationCount||views.Count!=publicationCount)")&&source.Contains("published[(start+i)%publicationCount]"),"native publication count is fixed and rechecked during bounded indexing");
            string native=File.ReadAllText(Path.Combine(checkout,"src/TradeLedgerProbeB/LedgerProductionNative.cs"));
            test(native.Contains("internal LedgerObservation Finish(){hudWork.Reset();")&&native.Contains("if(w==null){hudWork.Reset();"),"unload/invalid world lifecycle resets memo");
            test(native.Contains("Window(\"Map\"")&&native.Contains("ScannerDeposits(c,w,player,ticks)"),"Map hover and visible HUD remain wired");
            test(native.Contains("internal LedgerObservation Discover(")&&native.Contains("proof.Admitted()")&&native.Contains("details.Apply(p,ticks,\"local ResourceDiscovered23"),"immediate local offscreen event capture remains outside HUD memo");
            Console.WriteLine("HUD work checks passed: "+n+" (offline operation counts/native wiring; no Unity timing claim)");
        }
    }
}
