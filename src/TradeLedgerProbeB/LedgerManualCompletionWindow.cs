using System;
using System.Collections.Generic;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public sealed partial class LedgerPrototypeWindow
    {
        internal LedgerStore ManualStore;
        private int? manualPlayer;
        private int manualRevision,pendingManualRevision;
        private string manualError;
        private readonly Dictionary<string,LedgerRequestPending> manualPending=new Dictionary<string,LedgerRequestPending>();
        private readonly LedgerRequestClick requestClick=new LedgerRequestClick();
        private int requestHotControl;
        private sealed class ManualResult { internal LedgerManualCompletion Value; internal string Error; }
        private readonly Queue<ManualResult> manualResults=new Queue<ManualResult>();
        private void SetManualPlayer(int? value)
        {
            if(manualPlayer==value)return;viewReader.Invalidate();
            CloseUiMenu();returnPages.Clear();ResetPlaceBrowser();manualPlayer=value;manualError=null;manualRevision++;CancelRequestGesture();
            data.ManualStateReady=false;data.ManualCompletions.Clear();data.TrackingStateReady=false;data.ManualTracking.Clear();
            data.ArchiveStateReady=false;data.ConversationArchives.Clear();
            requestFilters=new LedgerRequestFilters();requestPopup=0;
            if(open)
            {
                InvalidateViewRead();
                refreshRequested=true;
            }
        }
        private void ApplyManualPending(){foreach(var p in manualPending.Values)p.Apply(data);}
        private void PollManualResults()
        {
            lock(resultGate)while(manualResults.Count>0)
            {
                var result=manualResults.Dequeue();var value=result.Value;LedgerRequestPending operation;
                if(!manualPending.TryGetValue(value.PendingKey,out operation))continue;
                manualPending.Remove(value.PendingKey);manualRevision++;
                if(value.World==world&&value.PlayerId==manualPlayer)
                {
                    operation.Apply(data,result.Error!=null);
                    manualError=result.Error==null?null:"Request change wasn't saved. Please try again.";
                    refreshRequested=true;
                }
                if(result.Error!=null)Log("LedgerManualAnnotationError",result.Error);
                else Log(value.IsArchive?"LedgerConversationArchiveSaved":value.IsTracking?"LedgerRequestTrackSaved":"LedgerManualCompletionSaved","request="+value.RequestKey+" value="+(value.IsArchive?value.Archived:value.IsTracking?value.Tracked:value.Completed)+" source=explicit-player-control");
            }
        }
        private void CancelRequestGesture()
        {requestClick.Cancel();if(requestHotControl!=0&&GUIUtility.hotControl==requestHotControl)GUIUtility.hotControl=0;requestHotControl=0;}
        private void EndRequestGesture(){if(Event.current.rawType==EventType.MouseUp)CancelRequestGesture();}
        private bool RequestAnnotationClick(Rect rect,string key,bool can)
        {
            int control=GUIUtility.GetControlID(key.GetHashCode(),FocusType.Passive,rect);
            var e=Event.current;
            if(e.type==EventType.MouseDown&&e.button==0&&e.clickCount==1&&rect.Contains(e.mousePosition)&&can)
            {requestClick.Begin(key);requestHotControl=control;GUIUtility.hotControl=control;e.Use();}
            else if(e.type==EventType.MouseUp&&e.button==0)
            {
                bool click=requestClick.Release(key,can&&rect.Contains(e.mousePosition));
                if(click){CancelRequestGesture();e.Use();return true;}
            }
            return false;
        }
        private void DrawCompletion(Rect rect,LedgerRequestCard card){DrawRequestAnnotation(rect,card,false);}
        private void DrawTracking(Rect rect,LedgerRequestCard card){DrawRequestAnnotation(rect,card,true);}
        private void DrawConversationArchive(Rect rect,LedgerRequestCard card)
        {
            bool enabled=GUI.enabled;GUI.enabled=enabled&&data.ArchiveStateReady&&data.ManualPlayerId==manualPlayer&&ManualStore!=null&&manualPending.Count<32;
            bool clicked=Button(rect,requestFilters.Archived?"Restore":"Archive");GUI.enabled=enabled;
            if(!clicked)return;
            foreach(var page in card.Pages)
            {
                LedgerManualCompletion command;
                if(!LedgerManualCompletion.TryArchive(page,manualPlayer,!requestFilters.Archived,out command)||manualPending.ContainsKey(command.PendingKey))continue;
                if(manualPending.Count>=32){manualError="Some pages remain. Archive again after saving.";break;}
                var operation=new LedgerRequestPending{Command=command,Prior=data.ConversationArchived(page)};
                if(!ManualStore.EnqueueManualCompletion(command,error=>{lock(resultGate)manualResults.Enqueue(new ManualResult{Value=command,Error=error});}))
                {manualError="Conversation change wasn't saved. Please try again.";break;}
                manualPending.Add(command.PendingKey,operation);operation.Apply(data);manualRevision++;
            }
            CancelRequestGesture();
        }
        private void DrawRequestAnnotation(Rect rect,LedgerRequestCard card,bool tracking)
        {
            bool active=tracking?data.ManuallyTracked(card):data.ManuallyCompleted(card);
            bool can=data.ManualStateReady&&(!tracking||data.TrackingStateReady)&&data.ManualPlayerId==manualPlayer&&LedgerManualCompletion.CanCreate(card,manualPlayer);
            LedgerManualCompletion command=null;
            if(can){if(tracking)LedgerManualCompletion.TryTrack(card,manualPlayer,!active,out command);else LedgerManualCompletion.TryCreate(card,manualPlayer,!active,out command);}
            bool waiting=command!=null&&manualPending.ContainsKey(command.PendingKey);
            string hint=card.Request==null?"No identified request in this conversation":!can?"Request state unavailable":waiting?"Saving request change...":tracking?(active?"Stop tracking":"Keep this request at the top"):(active?"Move to Pending":"Move to Done");
            if(rect.Contains(Event.current.mousePosition))iconHoverName=hint;
            bool enabled=GUI.enabled;GUI.enabled=enabled&&can&&!waiting&&ManualStore!=null&&manualPending.Count<32;
            Color old=GUI.color;if(active)GUI.color=new Color(.5f,.9f,1f);
            if(tracking)GUI.Label(rect,active?"Tracked":"Track",GUI.skin.button);
            else
            {
                var box=new Rect(rect.x+(rect.width-LedgerRequestRowLayout.Checkbox)/2,rect.y+(rect.height-LedgerRequestRowLayout.Checkbox)/2,LedgerRequestRowLayout.Checkbox,LedgerRequestRowLayout.Checkbox);ControlFrame(box,active);
                if(active){GUI.color=new Color(.5f,.95f,1f,1);for(int i=0;i<7;i++)GUI.DrawTexture(new Rect(box.x+4+i,box.y+box.height/2+i*.65f,2,2),Texture2D.whiteTexture);for(int i=0;i<12;i++)GUI.DrawTexture(new Rect(box.x+10+i*.65f,box.y+box.height/2+4-i,2,2),Texture2D.whiteTexture);}
            }
            GUI.color=old;
            bool clicked=command!=null&&RequestAnnotationClick(rect,command.PendingKey,GUI.enabled);GUI.enabled=enabled;
            if(!clicked)return;
            var operation=new LedgerRequestPending{Command=command,Prior=active};
            if(ManualStore.EnqueueManualCompletion(command,error=>{lock(resultGate)manualResults.Enqueue(new ManualResult{Value=command,Error=error});}))
            {
                manualPending.Add(command.PendingKey,operation);operation.Apply(data);manualRevision++;
                // The mouse-up is consumed before the next list snapshot can move/reorder this row.
            }
            else{manualError="Request change wasn't saved. Please try again.";Log("LedgerManualAnnotationError","queue-unavailable;state-unchanged");}
        }
    }
}
