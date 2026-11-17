using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TradeLedgerProbeB
{
    // An owned raycastable button, visible only beside the native in-game Escape menu.
    // No full-screen raycast target, cursor setter, menu replacement or new game hook.
    internal sealed class LedgerMenuEntry : IDisposable
    {
        private GameObject surface;
        private TextMeshProUGUI label;
        private RectTransform rect;
        internal bool Clicked;
        internal void Sync(GameObject menu, bool eligible)
        {
            if(!eligible||menu==null){Hide();return;}
            if(surface==null)
            {
                surface=new GameObject("TradeLedger.MenuEntry",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
                var canvas=surface.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32000;
                var go=new GameObject("Trade Ledger",typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(surface.transform,false);
                rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=Vector2.zero;
                go.GetComponent<Image>().color=new Color(.045f,.16f,.25f,.96f);
                go.GetComponent<Button>().onClick.AddListener(()=>Clicked=true);
                var text=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));text.transform.SetParent(go.transform,false);
                var tr=text.GetComponent<RectTransform>();tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=new Vector2(8,2);tr.offsetMax=new Vector2(-8,-2);
                label=text.GetComponent<TextMeshProUGUI>();label.text="Trade Ledger  ]";label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
                // Borrow only font metadata from this already-open menu, never world/UI payloads.
                var native=menu.GetComponentInChildren<TMP_Text>();if(native!=null&&native.font!=null)label.font=native.font;
            }
            float scale=LedgerLayout.For(Screen.width,Screen.height).Scale;
            rect.anchoredPosition=new Vector2(20*scale,20*scale);rect.sizeDelta=new Vector2(180*scale,34*scale);label.fontSize=LedgerLayout.BodyFont*scale;
            surface.SetActive(true);
        }
        internal void Hide()
        {
            if(surface==null)return;
            var es=EventSystem.current;var selected=es==null?null:es.currentSelectedGameObject;
            if(selected!=null&&selected.transform.IsChildOf(surface.transform))es.SetSelectedGameObject(null);
            surface.SetActive(false);
        }
        public void Dispose(){Hide();if(surface!=null)UnityEngine.Object.Destroy(surface);surface=null;}
    }
}
