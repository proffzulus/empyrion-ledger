using System;
using System.Collections.Generic;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public sealed partial class LedgerPrototypeWindow
    {
        private LedgerViewData sellerHintData;
        private LedgerRequestSellers requestSellers;
        private List<LedgerRequestSellerHint> RequestSellerHints(LedgerRequestCard card)
        {
            if(card.Request==null||requestFilters.Done)return new List<LedgerRequestSellerHint>();
            PrepareRequestSellers();
            return requestSellers.Query(card.Request.Fact,currentContext,offerGameTick);
        }
        private void PrepareRequestSellers(){if(!ReferenceEquals(data,sellerHintData)){sellerHintData=data;requestSellers=new LedgerRequestSellers(data);}}
        private ulong RequestSellerLayoutVersion(){if(requestFilters.Done||requestFilters.Conversations||requestFilters.Archived)return 0;PrepareRequestSellers();return requestSellers.PresentationVersion(currentContext,offerGameTick);}
        private void DrawRequestSeller(Rect rect,LedgerRequestCard card,LedgerRequestSellerHint hint,string text)
        {
            if(hint==null)return;hint.Clock=offerGameTick;Color old=GUI.contentColor;uint color=LedgerRequestCardPresentation.SellerColor(hint);
            GUI.contentColor=color!=0?new Color(((color>>16)&255)/255f,((color>>8)&255)/255f,(color&255)/255f,1):new Color(.68f,.76f,.82f);
            GUI.Label(new Rect(rect.x,rect.y,rect.width-76,rect.height),new GUIContent(LedgerPresentation.Rich(text),hint.Tooltip),rich);GUI.contentColor=old;
            string key="seller:"+LedgerRequestBrowser.StableKey(card)+":"+hint.Branch+":"+hint.Item+":"+(hint.Target==null?"unresolved":hint.Target.Key);
            if(hint.Target!=null&&RequestInventoryAction(new Rect(rect.xMax-70,rect.y,70,26),key,"Market",hint.Tooltip,true))SelectCatalogItem(hint.Target,true);
        }
    }
}
