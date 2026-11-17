using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TradeLedgerProbeB.Runtime
{
    public sealed partial class RuntimeObserver
    {
        private Harmony tradeHarmony;
        private HookPatchSession tradePatches;
        private bool tradeEnabled;
        private TradeAttempt activeTrade;
        private sealed class TradeAttempt
        {
            internal object Controller,Model,Entry,Player;
            internal int CashCurrencyItem;
            internal bool CashGrantPending;
            internal long Interaction;
            internal LedgerTradeReceipt Receipt;
            internal LedgerTradeProof Proof;
            internal bool Invalid;internal LedgerOffer BeforeOffer;
        }
        private static readonly int[] TradeTokens={0x0600cd22,0x0600cd23,0x060058c6,0x0600cd24,0x06006067,0x060022b7,0x060022c1};
        private static MethodInfo TradeMethod(string name){return typeof(RuntimeObserver).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic);}
        private void StartTradeHooks()
        {
            try
            {
                LedgerNativeContracts.Require(module.Assembly,"trade");
                string[] signatures={"System.Void Assembly-CSharp.PcitureEditorConnectionToken::ExitDeployment()","System.Void Assembly-CSharp.PcitureEditorConnectionToken::SelectBitmap()","System.Single Assembly-CSharp.ActionEditor+DriveResolver::SortMemory(System.Int32,System.Boolean)","System.Boolean Assembly-CSharp.PcitureEditorConnectionToken::SplitBuilder(Assembly-CSharp.SolutionLayout,Assembly-CSharp.PaneFactory)","System.Void SQLiteDBGlobalData::FreeForm(System.Int32,System.String,System.Int32,System.String,Vector3i,System.Int32,System.Int32,System.Int32,System.Single)","System.Boolean Assembly-CSharp.SolutionLayout::ChangeMenu(System.Int32)","System.Boolean Assembly-CSharp.SolutionLayout::BatchBuildTemplate(Assembly-CSharp.PaneFactory,System.Boolean,System.Boolean,System.Boolean,System.Boolean)"};
                var targets=TradeTokens.Select(t=>(MethodInfo)LedgerNativeContracts.Method(module,t)).ToArray();
                for(int i=0;i<targets.Length;i++)if((HookContract.Signature(targets[i])!=signatures[i]&&!LedgerNativeContracts.Bound(targets[i],TradeTokens[i]))||targets[i].IsStatic||Harmony.GetPatchInfo(targets[i])!=null)throw new InvalidOperationException("trade-target-signature-or-owner-conflict:"+TradeTokens[i].ToString("X8"));
                foreach(int token in new[]{0xc024,0xc032,0xc034,0x2fdf,0x2fe0,0x2fe1,0x2fe9})if(!fields.ContainsKey(0x04000000+token))fields.Add(0x04000000+token,LedgerNativeContracts.Field(module,0x04000000+token));
                if(fields[0x0400c024].FieldType!=typeof(UIToggle)||fields[0x0400c032].FieldType.FullName!="Assembly-CSharp.ActionEditor+DriveResolver"||fields[0x0400c034].FieldType!=typeof(int))throw new InvalidOperationException("trade-field-signature");
                if(fields[0x04002fdf].FieldType.FullName!="Assembly-CSharp.OptionsSerializer"||fields[0x04002fe0].FieldType.FullName!="Assembly-CSharp.ComponentContextConnectionToken"||fields[0x04002fe1].FieldType!=typeof(int)||fields[0x04002fe9].FieldType!=typeof(int))throw new InvalidOperationException("cash-argument-field-signature");
                tradeHarmony=new Harmony(Owner+".completed-trades");
                tradePatches=new HookPatchSession(host,
                    (target,postfix)=>tradeHarmony.Patch(target,prefix:IsTradeAction(target)?new HarmonyMethod(TradeMethod("TradeBegin")):IsCashGrant(target)?new HarmonyMethod(TradeMethod("TradeCashGrantBegin")):null,postfix:new HarmonyMethod(postfix),finalizer:IsTradeAction(target)?new HarmonyMethod(TradeMethod("TradeAbort")):null),
                    target=>tradeHarmony.Unpatch(target,HarmonyPatchType.All,tradeHarmony.Id),
                    target=>{var info=Harmony.GetPatchInfo(target);return info!=null&&info.Owners.Contains(tradeHarmony.Id);},HarmonyCanary.PreDetourFailure,"completed-trades",(target,postfix)=>CompileTradeCopy(target,postfix));
                for(int i=0;i<targets.Length;i++)
                {
                    string callback=i<2?"TradeEnd":i==2?"TradePrice":i==3?"TradeDelivered":i==4?"TradeAudit":i==5?"TradeCashPaid":"TradeCashGranted";
                    if(!tradePatches.TryInstall(targets[i],TradeMethod(callback)))break;
                }
                tradeEnabled=tradePatches.Count==7&&!tradePatches.UnsafeState;
                host.Trace(tradeEnabled?TraceLevel.Info:TraceLevel.Warn,"TradeRecordingReady","installed="+tradePatches.Count+" required=7 enabled="+tradeEnabled+" scope=singleplayer-visible-bank-and-cash nativeLiveValidation=pending originalEightHooksUnchanged=true");
            }
            catch(Exception e){tradeEnabled=false;ExceptionTrace.Write(host,"trade-hook-start",e,"old-eight-hooks-retained=true");}
        }
        private static bool IsTradeAction(MethodInfo target){return LedgerNativeContracts.Token(target)==0x0600cd22||LedgerNativeContracts.Token(target)==0x0600cd23;}
        private static bool IsCashGrant(MethodInfo target){return LedgerNativeContracts.Token(target)==0x060022c1;}
        private void CompileTradeCopy(MethodInfo target,MethodInfo postfix)
        {
            // Compile the COMPLETE prefix/postfix/finalizer wrapper, never invoke it or a game method.
            ReplacementPreflight.AuditFields(target,host,"completed-trades");
            var library=typeof(Harmony).Assembly;const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
            var prefix=IsTradeAction(target)?new List<MethodInfo>{TradeMethod("TradeBegin")}:IsCashGrant(target)?new List<MethodInfo>{TradeMethod("TradeCashGrantBegin")}:new List<MethodInfo>();
            var final=IsTradeAction(target)?new List<MethodInfo>{TradeMethod("TradeAbort")}:new List<MethodInfo>();
            var type=library.GetType("HarmonyLib.MethodPatcher",true);var ctor=type.GetConstructors(flags).Single(c=>c.GetParameters().Length==7);
            object patcher=ctor.Invoke(new object[]{target,null,prefix,new List<MethodInfo>{postfix},new List<MethodInfo>(),final,false});
            var replacement=(MethodInfo)type.GetMethod("CreateReplacement",flags).Invoke(patcher,new object[]{null});
            var platform=library.GetType("MonoMod.Core.Platforms.PlatformTriple",true);object triple=platform.GetProperty("Current",flags).GetValue(null,null);
            var native=platform.GetMethod("GetNativeMethodBody",flags,null,new[]{typeof(MethodBase)},null);
            if((IntPtr)native.Invoke(triple,new object[]{replacement})==IntPtr.Zero)throw new InvalidOperationException("trade-copy-jit-zero");
            GC.KeepAlive(replacement);host.Trace(TraceLevel.Info,"TradeWrapperValidated","token=0x"+LedgerNativeContracts.Token(target).ToString("X8")+" completePrefixPostfixFinalizer=true gameCodeInvoked=false");
        }
        private static void TradeBegin(object __instance,MethodBase __originalMethod,out TradeAttempt __state)
        {
            __state=null;var o=current;if(o==null||!o.tradeEnabled||!o.host.OnApiThread)return;
            try{__state=o.BeginTrade(__instance,LedgerNativeContracts.Token(__originalMethod)==0x0600cd22);}catch(Exception e){o.host.Trace(TraceLevel.Warn,"TradeCaptureSkipped","stage=begin type="+e.GetType().Name);}
        }
        private TradeAttempt BeginTrade(object controller,bool buys)
        {
            if(activeTrade!=null){activeTrade.Invalid=true;return SkipTrade("nested-action");}
            var sink=host as ILedgerTradeHost;var context=sink==null?null:sink.TradeContext;
            if(context==null||!context.Recordable||context.Mode!="SinglePlayer"||host.LocalPlayerId<=0||!TraderLease(controller)||!VisibleScope(controller,true))return SkipTrade("world-player-or-visible-window");
            var frame=Direct.Begin(controller,true,()=>TraderLease(controller));if(!Direct.Current(frame))return SkipTrade("current-bound-model");
            var identity=Direct.TraderContext(frame);if(!identity.Current||!(identity.CachedNpcId>0)||!identity.OwnerConsistent)return SkipTrade("npc-owner-proof");
            object selected=Field(0xc032,controller);int rawIndex=Field<int>(0xc034,controller),quantity;
            var input=Field(0xc022,controller) as UIInput;
            if(input==null||!int.TryParse(input.value,NumberStyles.Integer,CultureInfo.InvariantCulture,out quantity)||quantity<=0||quantity>1000000||rawIndex<0||rawIndex>=frame.RawCount||!ReferenceEquals(selected,frame.Raw[rawIndex]))return SkipTrade("selected-entry-or-requested-quantity");
            OfferBinding found=null;PublishedOffer visible=null;
            for(int i=0;i<frame.RowCount;i++){var row=Direct.Published(frame.Rows[i]);var binding=Direct.Bind(frame,frame.Rows[i],i,row);if(binding.Bound&&ReferenceEquals(binding.Entry,selected)){if(found!=null)return SkipTrade("ambiguous-visible-selection");found=binding;visible=row;}}
            if(found==null||found.Index!=rawIndex||!Direct.Current(frame))return SkipTrade("selected-row-not-current-bound");
            var toggle=Field(0xc024,controller) as UIToggle;float factor=Field<float>(0xc030,controller);
            if(toggle==null||!LedgerTradeProof.Finite(factor))return SkipTrade("currency-or-modifier");
            var f=new TradeAttempt{Controller=controller,Model=frame.Model,Entry=selected,Player=Field(0xc02f,controller),Interaction=traderCycle.Number,Proof=new LedgerTradeProof{PlayerBuys=buys,Requested=quantity,Factor=factor,MonetaryAccount=!toggle.value},
                Receipt=new LedgerTradeReceipt{TradeId=Guid.NewGuid().ToString("N"),Utc=DateTime.UtcNow.ToString("o"),Context=LedgerJson.Decode<LedgerContext>(LedgerJson.Encode(context)),PlayerId=host.LocalPlayerId,Action=buys?"Bought":"Sold",ItemId=found.Item.Value,Auxiliary=found.Auxiliary.Value,ItemName=visible.Name,
                Identity=new LedgerIdentity{NpcEntityId=identity.CachedNpcId,StructureEntityId=identity.StructureEntityId,BlockGrid=identity.BlockGrid.HasValue?identity.BlockGrid.ToString():null,OwnerConsistent=identity.OwnerConsistent,StructureName=Direct.ObservedOwnerName(identity),StructureNameSource="current admitted direct owner",NpcSource="current BE30.5325 cached NPC"}}};
            f.BeforeOffer=LedgerOffer.Capture(0,visible,found);activeTrade=f;host.Trace(TraceLevel.Debug,"TradeAttemptObserved","action="+f.Receipt.Action+" item="+f.Receipt.ItemId+" requested="+quantity+" accountCurrency="+f.Proof.MonetaryAccount+" notYetConfirmed=true");return f;
        }
        private TradeAttempt SkipTrade(string reason)
        {host.Trace(TraceLevel.Warn,"TradeCaptureSkipped","stage=begin reason="+reason+" noJournalWrite=true");return null;}
        private static void TradePrice(object __instance,int __0,bool __1,float __result)
        {var o=current;var f=o==null?null:o.activeTrade;if(f==null||!o.host.OnApiThread||!ReferenceEquals(f.Entry,__instance))return;f.Proof.Price(__0,__1,__result);}
        private static void TradeDelivered(object __instance,bool __result)
        {var o=current;var f=o==null?null:o.activeTrade;if(f!=null&&o.host.OnApiThread&&ReferenceEquals(f.Controller,__instance))f.Proof.DeliverySucceeded=__result;}
        private static void TradeCashPaid(object __instance,int __0,bool __result)
        {
            var o=current;var f=o==null?null:o.activeTrade;
            if(f==null||!o.host.OnApiThread||f.Proof.MonetaryAccount||!f.Proof.PlayerBuys||!f.Proof.HasBuy||f.Proof.ReceiptSeen||!ReferenceEquals(f.Player,__instance))return;
            f.Proof.CashDebit(__0,__result);
            o.host.Trace(TraceLevel.Debug,"TradeCashDebit","amount="+__0+" succeeded="+__result+" calls="+f.Proof.CashDebitCalls+" source=229C-native-argument-and-return");
        }
        private sealed class CashGrantState
        {internal TradeAttempt Attempt;internal object Argument;internal int Quantity;internal bool Consumed;}
        private static void TradeCashGrantBegin(object __instance,object __0,bool __1,bool __2,bool __3,bool __4,out CashGrantState __state)
        {
            __state=null;var o=current;var f=o==null?null:o.activeTrade;
            if(f==null||!o.host.OnApiThread||f.Proof.MonetaryAccount||f.Proof.PlayerBuys||!f.Proof.HasSell||!f.Proof.HasBuy||f.Proof.ReceiptSeen||!ReferenceEquals(f.Player,__instance))return;
            try
            {
                // Only the exact synchronous cash-sale payout call with the native constant flags.
                // No inventory scan, getter, currency transfer or other game action is invoked here.
                if(!__1||!__2||!__3||__4||f.CashGrantPending||__0==null||__0.GetType().FullName!="Assembly-CSharp.PaneFactory"||o.Field(0x2fe0,__0)!=null){f.Invalid=true;return;}
                var descriptor=o.Field(0x2fdf,__0);
                if(descriptor==null||descriptor.GetType().FullName!="Assembly-CSharp.OptionsSerializer"){f.Invalid=true;return;}
                int qty=o.Field<int>(0x2fe1,__0),item=o.Field<int>(0x2fe9,descriptor);
                if(qty<=0||item<=0||f.Proof.CashCreditCalls>=256||f.CashCurrencyItem!=0&&f.CashCurrencyItem!=item){f.Invalid=true;return;}
                f.CashCurrencyItem=item;f.CashGrantPending=true;
                __state=new CashGrantState{Attempt=f,Argument=__0,Quantity=qty};
            }
            catch(Exception e){f.Invalid=true;o.host.Trace(TraceLevel.Warn,"TradeCaptureSkipped","stage=cash-payout-argument type="+e.GetType().Name);}
        }
        private static void TradeCashGranted(object __instance,bool __result,CashGrantState __state)
        {
            var o=current;var f=o==null?null:o.activeTrade;
            if(f==null||__state==null||__state.Consumed||!ReferenceEquals(f,__state.Attempt)||!o.host.OnApiThread)return;
            __state.Consumed=true;
            try
            {
                if(!ReferenceEquals(f.Player,__instance)||o.Field(0x2fe0,__state.Argument)!=null||o.Field<int>(0x2fe1,__state.Argument)!=__state.Quantity){f.Invalid=true;return;}
                f.Proof.CashCredit(__state.Quantity,__result);
                o.host.Trace(TraceLevel.Debug,"TradeCashPayout","stackAmount="+__state.Quantity+" succeeded="+__result+" calls="+f.Proof.CashCreditCalls+" confirmedSum="+f.Proof.CashCreditAmount+" source=22A6-native-argument-and-return");
            }
            catch(Exception e){f.Invalid=true;o.host.Trace(TraceLevel.Warn,"TradeCaptureSkipped","stage=cash-payout-result type="+e.GetType().Name);}
            finally{f.CashGrantPending=false;}
        }
        private static void TradeAudit(int __0,string __1,int __2,string __3,int __5,int __6,int __7,float __8)
        {
            var o=current;var f=o==null?null:o.activeTrade;if(f==null||!o.host.OnApiThread)return;
            if(__0!=f.Receipt.PlayerId||__1!=f.Receipt.Context.Playfield||__2!=f.Receipt.Identity.StructureEntityId||__6!=f.Receipt.ItemId||__5!=(f.Proof.PlayerBuys?0:1)){f.Invalid=true;return;}
            f.Proof.Receipt(__7,__8);
            o.host.Trace(TraceLevel.Debug,"TradeNativeOutcome","action="+f.Receipt.Action+" quantity="+__7+" nativeAuditQuote="+__8.ToString("R",CultureInfo.InvariantCulture)+" buyCalculation="+f.Proof.BuyCalculation.ToString("R",CultureInfo.InvariantCulture)+" sellCalculation="+f.Proof.SellCalculation.ToString("R",CultureInfo.InvariantCulture)+" monetaryAccount="+f.Proof.MonetaryAccount+" quoteIsNotNecessarilyPayment=true");
        }
        private static void TradeEnd(object __instance,TradeAttempt __state)
        {
            var o=current;if(o==null||__state==null)return;
            try{o.EndTrade(__instance,__state);}catch(Exception e){o.host.Trace(TraceLevel.Warn,"TradeCaptureSkipped","stage=end type="+e.GetType().Name);}finally{if(ReferenceEquals(o.activeTrade,__state))o.activeTrade=null;}
        }
        private void EndTrade(object controller,TradeAttempt f)
        {
            string total,unit,reason="scope-or-identity-changed";var sink=host as ILedgerTradeHost;var context=sink==null?null:sink.TradeContext;
            bool scope=!f.Invalid&&ReferenceEquals(activeTrade,f)&&context!=null&&context.WorldKey==f.Receipt.Context.WorldKey&&context.Playfield==f.Receipt.Context.Playfield&&host.LocalPlayerId==f.Receipt.PlayerId&&traderCycle.Number==f.Interaction&&TraderLease(controller)&&VisibleScope(controller,true)&&ReferenceEquals(Field(0xc031,controller),f.Model)&&Field<float>(0xc030,controller)==f.Proof.Factor&&(Field(0xc024,controller) as UIToggle).value==!f.Proof.MonetaryAccount&&ReferenceEquals(Field(0xc02f,controller),f.Player);
            if(!scope||!f.Proof.Confirm(out total,out unit,out reason)){host.Trace(TraceLevel.Warn,"TradeCaptureSkipped","reason="+(scope?reason:"scope-or-identity-changed")+" noJournalWrite=true");return;}
            f.Proof.Emitted=true;f.Receipt.Confirmed=true;f.Receipt.Quantity=f.Proof.Actual;f.Receipt.Total=total;f.Receipt.Unit=unit;
            f.Receipt.PaymentSource=f.Proof.MonetaryAccount?"Bank":"Cash";
            f.Receipt.Source="5188 CB6C/CB6D normal return +601F outcome +588D native price returns; "+(f.Proof.MonetaryAccount?"bank account branch":"cash debit229C success / payout22A6 successful stack sum; currencyItem="+f.CashCurrencyItem)+"; buy CB6E delivery true; no wallet/stock inference; unit=actual-total/quantity";
            if(f.Proof.PlayerBuys&&f.BeforeOffer.Stock.HasValue&&f.BeforeOffer.Stock.Value==f.Receipt.Quantity){var zero=f.BeforeOffer;zero.Stock=0;zero.NpcSellsAvailable=false;zero.RemainingNpcBuyingCapacity=null;zero.NpcBuysAvailable=false;zero.BindingState="confirmed-native-exhaustion";zero.BindingSource="same-visible-trader confirmed native purchase consumed exact shown stock";zero.LastEvidenceUtc=DateTime.UtcNow.ToString("o");if(exhaustedInteraction!=f.Interaction||exhaustedWorld!=f.Receipt.Context.WorldKey||exhaustedNpc!=f.Receipt.Identity.NpcEntityId.Value)exhaustedOffers.Clear();exhaustedInteraction=f.Interaction;exhaustedWorld=f.Receipt.Context.WorldKey;exhaustedNpc=f.Receipt.Identity.NpcEntityId.Value;if(exhaustedOffers.Count<256)exhaustedOffers[zero.ItemId+":"+zero.AuxiliaryRaw]=zero;}
            sink.RecordTrade(f.Receipt);
        }
        private static void TradeAbort(Exception __exception,TradeAttempt __state)
        {var o=current;if(o==null||__state==null||__exception==null)return;if(ReferenceEquals(o.activeTrade,__state))o.activeTrade=null;o.host.Trace(TraceLevel.Warn,"TradeCaptureSkipped","reason=native-action-exception noJournalWrite=true type="+__exception.GetType().Name);}
        private void StopTradeHooks(){tradeEnabled=false;activeTrade=null;if(tradePatches!=null)tradePatches.Dispose();}
    }
}
