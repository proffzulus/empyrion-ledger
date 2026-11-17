using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TradeLedgerProbeB
{
    internal sealed partial class LedgerProductionNative
    {
        private readonly LedgerDepositHudWork hudWork=new LedgerDepositHudWork();
        private struct PendingHud
        {internal LedgerDepositHudSample Sample;internal string Rendered;internal LedgerObservation Observation;}
        private readonly List<PendingHud> hudPending=new List<PendingHud>(LedgerDepositHudWork.Batch);
        private static bool VisibleDepositLabel(UILabel label)
        {return label!=null&&label.enabled&&label.gameObject.activeInHierarchy&&label.isVisible&&label.finalAlpha>.01f;}
        private static bool PublishedDepositBody(string body,string rendered)
        {
            if(string.IsNullOrWhiteSpace(body)||body.Length>4096||string.IsNullOrEmpty(rendered)||rendered.Length>8192)return false;
            // BB43 inserts the body verbatim. Keep normalized matching as a fallback.
            return rendered.IndexOf(body,StringComparison.Ordinal)>=0||LedgerPresentation.Text(rendered).IndexOf(LedgerPresentation.Text(body),StringComparison.Ordinal)>=0;
        }
        // Common root/config checks belong before AND after the batch, not per row.
        private bool HudCurrent(object entry,object controller,GameObject root,object world)
        {
            if(entry==null||controller==null||controller.GetType().FullName!="Assembly-CSharp.LineEventArgsConnectionToken"||root==null||!root.activeInHierarchy||!V<bool>(0xc187,entry)||F(0xab7b,controller) as Transform!=root.transform||!ReferenceEquals(F(0xab6a,controller),world))return false;
            var canvas=F(0xc189,entry) as CanvasGroup;if(canvas!=null&&canvas.alpha<=.01f)return false;
            return Config(F(0xc186,entry),F(0xab5b,controller),root);
        }
        private bool HudRowCurrent(LedgerDepositHudSample sample,string rendered,GameObject root,IDictionary views)
        {
            var view=sample.View as GameObject;var label=sample.Label as UILabel;object marker=sample.Marker,resource=sample.Resource;
            return view!=null&&view.activeInHierarchy&&view.transform.IsChildOf(root.transform)&&VisibleDepositLabel(label)&&
                view.transform.childCount>0&&view.transform.GetChild(0).childCount>0&&view.transform.GetChild(0).GetChild(0).GetComponent<UILabel>()==label&&
                views.Contains(marker)&&ReferenceEquals(views[marker],view)&&ReferenceEquals(F(0x3368,marker),resource)&&
                V<int>(0x332c,marker)==sample.Id&&V<int>(0x505f,resource)==sample.Id&&Equals(F(0x5060,resource),sample.Position)&&
                Equals(F(0x5062,resource),sample.Definition)&&V<int>(0x5068,resource)==sample.SizeBand&&V<string>(0x336a,marker)==sample.Body&&label.text==rendered;
        }
        private IEnumerable<LedgerObservation> ScannerDeposits(LedgerContext c,object world,int player,ulong ticks)
        {
            using(LedgerProfiler.Measure(LedgerPerfPath.ScannerHud))
            {
            var result=new List<LedgerObservation>();var stack=F(0xc181,manager) as Stack<string>;
            // A memo never admits a hidden/modal-covered/unbound view.
            if(stack==null||stack.Count!=0){hudWork.Reset();return result;}
            var windows=F(0xc180,manager) as IDictionary;if(windows==null||windows.Count>256){hudWork.Reset();return result;}
            object actor=Invoke(0x53af,world);if(actor==null||V<int>(0x1b31,actor)!=player){hudWork.Reset();return result;}
            foreach(DictionaryEntry window in windows){
                object entry=window.Value;if(entry==null)continue;object controller=F(0xc184,entry);if(controller==null||controller.GetType().FullName!="Assembly-CSharp.LineEventArgsConnectionToken")continue;
                var root=F(0xc185,entry) as GameObject;if(!HudCurrent(entry,controller,root,world))continue;
                // BB42 publishes A975 from A971.Keys; BB43 indexes it then point-looks up
                // A971. Never copy/enumerate this dictionary or a World resource registry.
                var views=F(0xab6e,controller) as IDictionary;var published=F(0xab72,controller) as IList;
                if(views==null||published==null||views.Count!=published.Count||views.Count==0||views.Count>1024)continue;
                hudWork.Bind(scope,world,actor,controller,root,views,published,ticks);
                int publicationCount=published.Count,start,count=hudWork.Take(publicationCount,out start);
                hudPending.Clear();
                try {
                    for(int i=0;i<count;i++){
                        if(published.Count!=publicationCount||views.Count!=publicationCount){hudWork.Reset();return result;}
                        object marker=published[(start+i)%publicationCount];if(marker==null)continue;
                        if(marker.GetType().FullName!="Assembly-CSharp.TreeNodeManager"||!views.Contains(marker)){hudWork.Forget(marker);continue;}
                        var view=views[marker] as GameObject;
                        if(view==null||!view.activeInHierarchy||!view.transform.IsChildOf(root.transform)||view.transform.childCount==0){hudWork.Forget(marker);continue;}
                        var first=view.transform.GetChild(0);if(first.childCount==0){hudWork.Forget(marker);continue;}
                        // BB43 IL03E9: markerView.child(0).child(0).GetComponent<UILabel>().
                        var label=first.GetChild(0).GetComponent<UILabel>();if(!VisibleDepositLabel(label)){hudWork.Forget(marker);continue;}
                        string body=V<string>(0x336a,marker),rendered=label.text;
                        if(!PublishedDepositBody(body,rendered)){hudWork.Forget(marker);continue;}
                        object resource=F(0x3368,marker);
                        if(resource==null||resource.GetType().FullName!="Assembly-CSharp.AddinResolver"||V<int>(0x332c,marker)!=V<int>(0x505f,resource)){hudWork.Forget(marker);continue;}
                        var sample=new LedgerDepositHudSample{Marker=marker,View=view,Label=label,Resource=resource,Body=body,Id=V<int>(0x505f,resource),Position=F(0x5060,resource),Definition=F(0x5062,resource),SizeBand=V<int>(0x5068,resource)};
                        // Admission above is current. Distance-only text changes do not
                        // require parsing, a DTO or JSON/hash deduplication again.
                        if(hudWork.Unchanged(sample))continue;
                        var details=LedgerDepositDetails.Read(body,rendered,(string)Invoke(0x37ba,marker),true);
                        LedgerObservation observation=null;
                        if(details!=null){
                            var p=Deposit(c,resource,player,ticks);details.Apply(p,ticks,"visible scanner HUD; FileManagerStoreSite.A971 marker/view; BB43 published UILabel; StubManager.3351/3353");p.Proof=p.DetailsSource;
                            observation=Capture(c,p);
                        }
                        hudPending.Add(new PendingHud{Sample=sample,Rendered=rendered,Observation=observation});
                    }
                    // No memo or fact from a mixed root/config/publication generation.
                    if(stack.Count!=0||!ReferenceEquals(F(0xc181,manager),stack)||!ReferenceEquals(F(0xc180,manager),windows)||!windows.Contains(window.Key)||!ReferenceEquals(windows[window.Key],entry)||!HudCurrent(entry,controller,root,world)||!ReferenceEquals(Invoke(0x53af,world),actor)||V<int>(0x1b31,actor)!=player||!ReferenceEquals(F(0xab6e,controller),views)||!ReferenceEquals(F(0xab72,controller),published)||published.Count!=publicationCount||views.Count!=publicationCount){hudWork.Reset();return result;}
                    foreach(var pending in hudPending){
                        if(!HudRowCurrent(pending.Sample,pending.Rendered,root,views)){hudWork.Forget(pending.Sample.Marker);continue;}
                        hudWork.Remember(pending.Sample);
                        if(pending.Observation!=null)result.Add(pending.Observation);
                    }
                    return result; // At most64 indexed bindings per poll, one registered HUD.
                } finally {hudPending.Clear();}
            }
            hudWork.Reset();return result;

            }
        }
    }
}
