using System;
using System.Collections.Generic;
using System.Globalization;
using Eleon.Modding;

namespace TradeLedgerProbeB
{
    internal sealed partial class LedgerProductionNative
    {
        private readonly LedgerProductionActions actions=new LedgerProductionActions();
        private bool LifecycleFamily(int definition)
        {
            // One active definition for this player action, never a world/device enumeration.
            var blocks=F(0x1480,null) as Array;if(blocks==null||definition<=0||definition>=blocks.Length)return false;
            object block=blocks.GetValue(definition);if(block==null)return false;
            string type=block.GetType().FullName;
            return type=="Assembly-CSharp.ContextContext"||type=="Assembly-CSharp.EditorTable"||type=="Assembly-CSharp.ViewDictionary"||type=="Assembly-CSharp.ContextScope"||type=="Assembly-CSharp.ConditionSettings";
        }
        internal string BeginPickup(object[] args,LedgerContext c,IApplication app,int player)
        {
            object w=World(c,app,player);if(w==null||args==null||args.Length!=5||!ReferenceEquals(args[0],w)||!(args[1] is int)||!(args[4] is int)||(int)args[4]!=player||args[2]==null||args[2].GetType().FullName!="Vector3i")return null;
            int definition=Block(args[3]);if(!LifecycleFamily(definition))return null;
            return LedgerProductionLifecycle.PickupBeginning(c,player,(int)args[1],Vector(args[2]),definition,Block(Invoke(0x53d1,w,(int)args[1],args[2])));
        }
        internal LedgerObservation Action(string kind,object instance,object[] args,object result,LedgerContext c,IApplication app,int player,long now)
        {
            object w=World(c,app,player);if(w==null||args==null)return null;
            int clr,oldDefinition,newDefinition;object grid;
            if(kind=="pickup"){
                // SaveOutline performs permission/identity checks, then commits removal at IL00E9.
                // Sending a network request/early return with the old block still present is not success.
                if(args.Length!=5||!ReferenceEquals(args[0],w)||!(args[1] is int)||!(args[4] is int)||(int)args[4]!=player)return null;
                clr=(int)args[1];grid=args[2];oldDefinition=Block(args[3]);if(!LifecycleFamily(oldDefinition))return null;newDefinition=Block(Invoke(0x53d1,w,clr,grid));
                if(!LedgerProductionLifecycle.PickupCommitted(result as string,c,player,clr,Vector(grid),oldDefinition,newDefinition))return null;
            }else if(kind=="commit"){
                // 4EBF emits BlockChanged before SetBlock. This postfix runs only on normal return,
                // checks its returned old value AND the exact action's committed target block.
                if(result==null||args.Length!=11||instance==null||instance.GetType().FullName!="Assembly-CSharp.ActionLayout"||!ReferenceEquals(F(0x4ac0,instance),w)||!(args[1] is bool)||!(bool)args[1]||!(args[10] is int)||(int)args[10]!=player||(int)Invoke(0x4eb4,instance)!=0)return null;
                grid=args[0];if(grid==null||grid.GetType().FullName!="Vector3i")return null;
                oldDefinition=Block(result);newDefinition=Block(args[2]);if(oldDefinition<=0||newDefinition<=0||oldDefinition==newDefinition||!LifecycleFamily(oldDefinition))return null;
                // Read ONLY this explicit action's target. Never poll for missing devices.
                if(Block(Invoke(0x4eec,instance,grid))!=newDefinition)return null;
                object chunk=Invoke(0x4ebf,instance,grid);if(chunk==null||chunk.GetType().FullName!="Assembly-CSharp.ActivatorFactory")return null;
                clr=(int)Invoke(0x4d54,chunk);
            }else return null;
            if(grid==null||grid.GetType().FullName!="Vector3i")return null;
            return actions.Accept(c,player,clr,Vector(grid),oldDefinition,newDefinition,kind=="pickup",true,now);
        }
    }
    public sealed partial class Probe
    {
        public string ProducerPickupBeginning(object[] args)
        {
            if(!runtimeEnabled||!OnApiThread||!WorldReady||ledger==null||ledgerContext==null||!ledger.OwnedPublicationAllowed)return null;
            try{if(productionSource==null)productionSource=new LedgerProductionNative(application.GetType().Assembly,application);return productionSource.BeginPickup(args,ledgerContext,application,localId);}
            catch(Exception e){ProductionFailure(e,"lifecycle-pickup-begin");return null;}
        }
        public void ProducerAction(string kind,object instance,object[] args,object result)
        {
            if(!runtimeEnabled||!OnApiThread||!WorldReady||ledger==null||ledgerContext==null||!ledger.OwnedPublicationAllowed)return;
            try{
                if(productionSource==null)productionSource=new LedgerProductionNative(application.GetType().Assembly,application);
                var o=productionSource.Action(kind,instance,args,result,ledgerContext,application,localId,ElapsedMs);
                if(o!=null){Record(o);try{Trace(TraceLevel.Warn,"ProductionLifecycleObserved","reason="+o.ProductionRetirement.Reason+" block="+o.ProductionRetirement.Block+" oldDefinition="+o.ProductionRetirement.OldDefinition+" newDefinition="+o.ProductionRetirement.NewDefinition+" journalOnly=true");}catch(Exception){}}
            }catch(Exception e){ProductionFailure(e,"lifecycle-"+kind);}
        }
    }
}
