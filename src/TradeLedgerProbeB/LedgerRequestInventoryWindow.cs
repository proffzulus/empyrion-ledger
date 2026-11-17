using System;
using System.Collections.Generic;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public sealed partial class LedgerPrototypeWindow
    {
        internal Func<LedgerInventorySnapshot> InventoryReader;
        private IDictionary<string,LedgerRequestInventoryProgress> RequestInventoryProgress
        {get{return NativeBookmarks==null?null:NativeBookmarks.InventoryProgress;}}
        // A stock refresh may reorder rows between press and release. Bind the gesture to the occurrence.
        private bool RequestInventoryAction(Rect rect,string key,string title,string hint,bool can)
        {
            bool enabled=GUI.enabled;GUI.enabled=enabled&&can;
            bool clicked=RequestAnnotationClick(rect,key,GUI.enabled);
            GUI.Label(rect,new GUIContent(title,hint),GUI.skin.button);GUI.enabled=enabled;
            return clicked;
        }
    }
}
