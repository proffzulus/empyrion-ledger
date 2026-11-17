using System;
using System.Reflection;
using HarmonyLib;

namespace TradeLedgerProbeB.Runtime
{
    public sealed partial class RuntimeObserver
    {
        private HookPatchSession productionPatch,productionLifecyclePatch;
        private bool productionLifecycleEnabled;
        private bool productionPickupSeen,productionCommitSeen;
        private bool productionVisibleEnabled;
        private long productionCallbackCount;
        private void StartProductionVisible()
        {
            try{
                LedgerNativeContracts.Require(module.Assembly,"production-hooks");
                var target=(MethodInfo)LedgerNativeContracts.Method(module,0x0600bbcc);
                if(HookContract.Signature(target)!="System.Void Eleon.Ngui.ItemGridItemGeneratorOutput::Update()"||target.IsStatic||Harmony.GetPatchInfo(target)!=null)throw new InvalidOperationException("producer-visible-contract");
                productionPatch=HarmonyCanary.CreateSession(host,new Harmony(Owner+".production-visible"),"production-visible");
                productionVisibleEnabled=productionPatch.TryInstall(target,typeof(RuntimeObserver).GetMethod("ProductionShown",BindingFlags.Static|BindingFlags.NonPublic));
                StartProductionLifecycle();
                host.Trace(TraceLevel.Warn,"ProductionObservationReady","shown-output-postfix="+productionVisibleEnabled+" accountingHooks=retired;last-shown-close-baseline;live-validation-pending");
            }catch(Exception e){ExceptionTrace.Write(host,"production-visible-bootstrap",e,"existing-observers-retained;guarded-published-copy-poll-independent;immediate-postfix-unavailable");}
        }
        private void StartProductionLifecycle()
        {
            try{
                var harmony=new Harmony(Owner+".production-lifecycle");
                var begin=typeof(RuntimeObserver).GetMethod("ProductionPickupBegin",BindingFlags.Static|BindingFlags.NonPublic);
                productionLifecyclePatch=new HookPatchSession(host,
                    (target,postfix)=>harmony.Patch(target,prefix:LedgerNativeContracts.Token(target)==0x06006c72?new HarmonyMethod(begin):null,postfix:new HarmonyMethod(postfix)),
                    target=>harmony.Unpatch(target,HarmonyPatchType.All,harmony.Id),
                    target=>{var p=Harmony.GetPatchInfo(target);return p!=null&&p.Owners.Contains(harmony.Id);},HarmonyCanary.PreDetourFailure,"production-lifecycle",
                    (target,postfix)=>ReplacementPreflight.Compile(target,postfix,host,"production-lifecycle",LedgerNativeContracts.Token(target)==0x06006c72?begin:null));
                int[] tokens={0x06006c72,0x06004ef3};string[] signatures={"System.Void Assembly-CSharp.MenuSiteConnectionToken::ToggleFile(World,System.Int32,Vector3i,Assembly-CSharp.ControlTable,System.Int32)","Assembly-CSharp.ControlTable Assembly-CSharp.ActionLayout::ReduceIcon(Vector3i,System.Boolean,Assembly-CSharp.ControlTable,System.Boolean,System.SByte,System.Boolean,System.Int32,System.Boolean,System.Boolean,System.Boolean,System.Int32)"};
                for(int i=0;i<tokens.Length;i++){
                    var target=(MethodInfo)LedgerNativeContracts.Method(module,tokens[i]);
                    if(target.IsStatic||(HookContract.Signature(target)!=signatures[i]&&!LedgerNativeContracts.Bound(target,tokens[i]))||Harmony.GetPatchInfo(target)!=null)throw new InvalidOperationException("producer-lifecycle-contract-"+tokens[i].ToString("X8"));
                    if(!productionLifecyclePatch.TryInstall(target,typeof(RuntimeObserver).GetMethod(i==0?"ProductionPickup":"ProductionCommit",BindingFlags.Static|BindingFlags.NonPublic)))break;
                }
                productionLifecycleEnabled=productionLifecyclePatch.Count==2&&!productionLifecyclePatch.UnsafeState;
                host.Trace(TraceLevel.Warn,"ProductionLifecycleReady","postfixes="+productionLifecyclePatch.Count+" enabled="+productionLifecycleEnabled+" destruction=not-observed;pickup=local-SaveOutline-committed-return;live-validation-pending");
            }catch(Exception e){productionLifecycleEnabled=false;ExceptionTrace.Write(host,"production-lifecycle-bootstrap",e,"existing-capture-retained;manual-Forget-available");}
        }
        private static void ProductionPickupBegin(object[] __args,out string __state)
        {
            __state=null;var o=current;if(o==null||!o.productionLifecycleEnabled||!o.host.OnApiThread||!o.host.WorldReady)return;
            try{var sink=o.host as ILedgerProductionUiHost;if(sink!=null)__state=sink.ProducerPickupBeginning(__args);}catch(Exception){/* Observation-only prefix never skips/changes native action. */}
        }
        private static void ProductionPickup(object __instance,object[] __args,string __state){if(__state!=null)ProductionActionCallback("pickup",__instance,__args,__state);}
        private static void ProductionCommit(object __instance,object[] __args,object __result){ProductionActionCallback("commit",__instance,__args,__result);}
        private static void ProductionActionCallback(string kind,object instance,object[] args,object result)
        {
            var o=current;if(o==null||!o.productionLifecycleEnabled||!o.host.OnApiThread||!o.host.WorldReady)return;
            try{if(kind=="pickup"?!o.productionPickupSeen:!o.productionCommitSeen){if(kind=="pickup")o.productionPickupSeen=true;else o.productionCommitSeen=true;try{o.host.Trace(TraceLevel.Warn,"ProductionLifecycleHookFired","kind="+kind+" callbackOnly=true;no-retirement-permission-without-local-committed-evidence");}catch(Exception){}}
                var sink=o.host as ILedgerProductionUiHost;if(sink!=null)sink.ProducerAction(kind,instance,args,result);}catch(Exception){/* Never propagate into native actions. */}
        }
        private static void ProductionShown(object __instance)
        {
            var o=current;if(o==null||!o.productionVisibleEnabled||!o.host.OnApiThread||!o.host.WorldReady)return;
            try{if(++o.productionCallbackCount==1)o.host.Trace(TraceLevel.Warn,"ProductionHookFired","token=0x0600BBCC first=true;callbacks-proven-not-payload-permission;published-copy-poll-independent=true");}catch(Exception){/* Optional diagnostic delivery. */}
            try{var sink=o.host as ILedgerProductionUiHost;if(sink!=null)sink.ProducerShown(__instance);}catch(Exception){/* Never propagate into native UI. */}
        }
    }
}
