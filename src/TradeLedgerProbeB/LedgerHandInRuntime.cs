using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace TradeLedgerProbeB.Runtime
{
    public sealed partial class RuntimeObserver
    {
        private HookPatchSession handInPatches;
        private Harmony handInHarmony;
        private bool handInEnabled;
        private object handInNpc,handInPlayer;
        private string handInInteraction;
        private long handInClosedAt=-1;
        private HandInCall handInCall;
        private int handInSkipCount;
        private void HandInSkip(string reason)
        {
            int n=System.Threading.Interlocked.Increment(ref handInSkipCount);
            if(n<=12)host.Trace(TraceLevel.Warn,"HandInCaptureSkipped","stage=begin reason="+reason+" interaction="+(handInInteraction??"unknown"));
            else if(n==13)host.Trace(TraceLevel.Warn,"HandInCaptureSkipped","diagnostics-truncated limit=12 until-world-reset");
        }
        private sealed class HandInCall
        {
            internal string Id,Interaction;
            internal object Bag,Toolbar;
            internal int Npc,Requested,Meta;
            internal bool Invalid,Finished;
            internal LedgerHandInAmounts Amounts=new LedgerHandInAmounts();
        }
        private sealed class HandInAmountCall
        {
            internal HandInCall Call;
            internal int Container,Item,Requested,Meta;
        }
        private static MethodInfo HandInMethod(string name){return typeof(RuntimeObserver).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic);}
        private static bool HandInOuter(MethodInfo m){return LedgerNativeContracts.Token(m)==0x06006aaa;}
        private void StartHandInHooks()
        {
            try
            {
                LedgerNativeContracts.Require(module.Assembly,"hand-in");
                int[] tokens={0x06006aaa,0x0600ba92,0x06003380};
                string[] signatures={"System.Void DialogueSystem::API_RemoveItemFromLocalPlayer(System.String,System.Int32,System.Int32)","System.Int32 Assembly-CSharp.ToolbarOptionsConnectionToken::CleanActivator(System.Int32,System.Int32,System.Int32,System.Boolean,System.Boolean)","System.Int32 Assembly-CSharp.MethodList::LoadContext(System.Int32,System.Int32,System.Int32)"};
                var targets=tokens.Select(t=>(MethodInfo)LedgerNativeContracts.Method(module,t)).ToArray();
                for(int i=0;i<targets.Length;i++)if(targets[i].IsStatic||targets[i].ContainsGenericParameters||(HookContract.Signature(targets[i])!=signatures[i]&&!LedgerNativeContracts.Bound(targets[i],tokens[i]))||Harmony.GetPatchInfo(targets[i])!=null)throw new InvalidOperationException("hand-in-target-contract:"+tokens[i].ToString("X8"));
                foreach(int token in new[]{0x63ab,0x1c03,0x1c02})if(!fields.ContainsKey(0x04000000+token))fields.Add(0x04000000+token,LedgerNativeContracts.Field(module,0x04000000+token));
                if(fields[0x040063ab].DeclaringType.FullName!="DialogueSystem"||fields[0x040063ab].FieldType.FullName!="Assembly-CSharp.ReferenceHelper"||fields[0x04001c03].DeclaringType.FullName!="Assembly-CSharp.MemoryHelper"||fields[0x04001c03].FieldType.FullName!="Assembly-CSharp.ToolbarOptionsConnectionToken"||fields[0x04001c02].DeclaringType.FullName!="Assembly-CSharp.MemoryHelper"||fields[0x04001c02].FieldType.FullName!="Assembly-CSharp.MethodList")throw new InvalidOperationException("hand-in-field-contract");
                handInHarmony=new Harmony(Owner+".request-hand-in");
                handInPatches=new HookPatchSession(host,(target,postfix)=>handInHarmony.Patch(target,prefix:new HarmonyMethod(HandInMethod(HandInOuter(target)?"HandInBegin":"HandInAmountBegin")),postfix:new HarmonyMethod(postfix),finalizer:HandInOuter(target)?new HarmonyMethod(HandInMethod("HandInAbort")):null),
                    target=>handInHarmony.Unpatch(target,HarmonyPatchType.All,handInHarmony.Id),target=>{var p=Harmony.GetPatchInfo(target);return p!=null&&p.Owners.Contains(handInHarmony.Id);},HarmonyCanary.PreDetourFailure,"request-hand-in",CompileHandInCopy);
                foreach(var target in targets)if(!handInPatches.TryInstall(target,HandInMethod(HandInOuter(target)?"HandInEnd":"HandInAmount")))break;
                handInEnabled=handInPatches.Count==3&&!handInPatches.UnsafeState;
                host.Trace(TraceLevel.Warn,"HandInRecordingReady","installed="+handInPatches.Count+" required=3 enabled="+handInEnabled+" source=native-dialogue-removal-return-counts closeGraceMs=1500 liveValidation=pending");
            }
            catch(Exception e){handInEnabled=false;ExceptionTrace.Write(host,"hand-in-hooks",e,"existing-hooks-retained=true");}
        }
        private void CompileHandInCopy(MethodInfo target,MethodInfo postfix)
        {
            ReplacementPreflight.AuditFields(target,host,"request-hand-in");
            var library=typeof(Harmony).Assembly;const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
            var prefix=new List<MethodInfo>{HandInMethod(HandInOuter(target)?"HandInBegin":"HandInAmountBegin")};
            var final=HandInOuter(target)?new List<MethodInfo>{HandInMethod("HandInAbort")}:new List<MethodInfo>();
            var type=library.GetType("HarmonyLib.MethodPatcher",true);var ctor=type.GetConstructors(flags).Single(c=>c.GetParameters().Length==7);
            object patcher=ctor.Invoke(new object[]{target,null,prefix,new List<MethodInfo>{postfix},new List<MethodInfo>(),final,false});
            var replacement=(MethodInfo)type.GetMethod("CreateReplacement",flags).Invoke(patcher,new object[]{null});
            var platform=library.GetType("MonoMod.Core.Platforms.PlatformTriple",true);object triple=platform.GetProperty("Current",flags).GetValue(null,null);
            if((IntPtr)platform.GetMethod("GetNativeMethodBody",flags,null,new[]{typeof(MethodBase)},null).Invoke(triple,new object[]{replacement})==IntPtr.Zero)throw new InvalidOperationException("hand-in-copy-jit-zero");
            GC.KeepAlive(replacement);host.Trace(TraceLevel.Info,"HandInWrapperValidated","token=0x"+LedgerNativeContracts.Token(target).ToString("X8")+" fullWrapper=true gameCodeInvoked=false");
        }
        private void RememberHandIn(object controller,DialogueIdentity identity)
        {
            if(!identity.Current||!DialogueLease(controller)||!identity.NpcId.HasValue)return;
            handInNpc=identity.Npc;handInPlayer=Field(0xb955,controller);handInInteraction=dialogueCycle.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);handInClosedAt=-1;
        }
        private void ForgetHandIn(string reason)
        {
            if(handInCall!=null)handInCall.Invalid=true;
            handInNpc=handInPlayer=null;handInInteraction=null;handInClosedAt=-1;
            var sink=host as ILedgerHandInHost;if(sink!=null)sink.HandInCancelled(reason);
        }
        private void CloseHandIn()
        {
            if(handInInteraction==null)return;
            if(handInClosedAt<0)handInClosedAt=host.ElapsedMs;
            var sink=host as ILedgerHandInHost;if(sink!=null)sink.HandInClosed(handInInteraction);
        }
        private static void HandInBegin(object __instance,int __1,int __2,out HandInCall __state)
        {
            __state=null;var o=current;if(o==null||!o.handInEnabled)return;
            if(!o.host.OnApiThread||!o.host.WorldReady){o.HandInSkip("off-api-thread-or-world-not-ready");return;}
            try
            {
                if(o.activeTrade!=null){o.HandInSkip("ordinary-trade-active");return;}
                if(o.handInCall!=null){o.handInCall.Invalid=true;o.HandInSkip("nested-dialogue-removal");return;}
                // Capture the latest already revealed body before native mutation, without invoking game actions.
                if(o.dialogueCycle.Active&&o.VisibleScope(o.dialogue,false)){o.ObserveDialogue();o.ObserveDirectDialogue();}
                bool open=o.dialogueCycle.Active&&o.DialogueLease(o.dialogue)&&o.VisibleScope(o.dialogue,false)&&ReferenceEquals(o.handInNpc,o.Field(0xb956,o.dialogue));
                bool recentClose=o.handInClosedAt>=0&&o.host.ElapsedMs-o.handInClosedAt>=0&&o.host.ElapsedMs-o.handInClosedAt<=1500;
                if((!open&&!recentClose)||o.handInNpc==null||o.handInPlayer==null){o.HandInSkip("no-current-visible-actor-or-close-grace-expired");return;}
                object actor=o.Field(0x63ab,__instance);
                if(!LedgerHandInActorGate.Admit(open,recentClose,ReferenceEquals(actor,o.handInNpc),actor==null)||o.Field<int>(0x1b31,o.handInPlayer)!=o.host.LocalPlayerId||__1<=0||__1>1000000000){o.HandInSkip("native-npc-player-or-request-argument-mismatch");return;}
                __state=new HandInCall{Id=Guid.NewGuid().ToString("N"),Interaction=o.handInInteraction,Npc=o.Field<int>(0x1b31,o.handInNpc),Requested=__1,Meta=__2,Bag=o.Field(0x1c03,o.handInPlayer),Toolbar=o.Field(0x1c02,o.handInPlayer)};
                o.handInCall=__state;
            }
            catch(Exception e){o.host.Trace(TraceLevel.Warn,"HandInCaptureSkipped","stage=begin type="+e.GetType().Name);}
        }
        // Both native methods overwrite their count argument (starg) with the unremoved
        // remainder. Capture inputs before execution; only the return is actual removal.
        private static void HandInAmountBegin(object __instance,int __0,int __1,int __2,out HandInAmountCall __state)
        {
            __state=null;
            var o=current;var c=o==null?null:o.handInCall;if(c==null||!o.handInEnabled||!o.host.OnApiThread)return;
            try
            {
                bool bag=ReferenceEquals(__instance,c.Bag),bar=ReferenceEquals(__instance,c.Toolbar);
                if(!bag&&!bar)return;
                __state=new HandInAmountCall{Call=c,Container=bag?0:1,Item=__0,Requested=__1,Meta=__2};
            }
            catch(Exception){c.Invalid=true;}
        }
        private static void HandInAmount(HandInAmountCall __state,int __result)
        {
            var o=current;if(o==null||__state==null||!o.handInEnabled||!o.host.OnApiThread)return;
            var c=__state.Call;if(c==null||c.Finished||!ReferenceEquals(o.handInCall,c))return;
            try{c.Amounts.Add(__state.Container,__state.Item,__state.Requested,__state.Meta,__result,c.Meta,c.Requested);}
            catch(Exception){c.Invalid=true;}
        }
        private static void HandInEnd(HandInCall __state)
        {
            var o=current;if(o==null||__state==null||__state.Finished||!o.host.OnApiThread)return;
            __state.Finished=true;if(ReferenceEquals(o.handInCall,__state))o.handInCall=null;
            try
            {
                if(__state.Invalid||__state.Amounts.Invalid||!o.host.WorldReady){o.host.Trace(TraceLevel.Warn,"HandInCaptureSkipped","stage=return reason="+(__state.Invalid?"interaction-invalidated":__state.Amounts.Invalid?"invalid-removal-amounts":"world-not-ready")+" requested="+__state.Requested+" actualRemoved="+__state.Amounts.Total+" no-completion=true");return;}
                var sink=o.host as ILedgerHandInHost;if(sink!=null)sink.HandInRemoved(__state.Interaction,__state.Npc,__state.Amounts.Item,__state.Meta,__state.Amounts.Total,__state.Id);
            }
            catch(Exception e){o.host.Trace(TraceLevel.Warn,"HandInCaptureSkipped","stage=return type="+e.GetType().Name);}
        }
        private static void HandInAbort(Exception __exception,HandInCall __state)
        {
            var o=current;if(o==null||__exception==null||__state==null)return;
            __state.Invalid=true;if(ReferenceEquals(o.handInCall,__state))o.handInCall=null;
            // Void finalizer observes only; the original exception/result is never replaced.
        }
        private void StopHandInHooks(){handInEnabled=false;handInCall=null;if(handInPatches!=null)handInPatches.Dispose();}
    }
}
