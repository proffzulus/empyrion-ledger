using System;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public sealed partial class LedgerPrototypeWindow
    {
        private ulong? offerGameTick;
        private long offerClockAt = -1000;
        private string offerClockScope;
        private void PollOfferClock(long now, bool ready)
        {
            string scope = ready && currentContext != null && currentContext.Recordable ?
                currentContext.WorldKey + "|" + currentContext.LocalSaveKey : null;
            if (scope != offerClockScope) { offerClockScope = scope; offerGameTick = null; offerClockAt = -1000; }
            if (scope == null) { offerGameTick = null; return; }
            if (now >= offerClockAt && now - offerClockAt < 500) return;
            offerClockAt = now;
            try { offerGameTick = Application == null ? (ulong?)null : Application.GameTicks; }
            catch (Exception) { offerGameTick = null; }
        }
        private LedgerOfferAge OfferAge(LedgerObservation market)
        { return LedgerFreshness.Evaluate(market, currentContext, offerGameTick); }
        private void OfferBackdrop(Rect rect, LedgerObservation market)
        {
            uint color = LedgerFreshness.Background(OfferAge(market));
            if (color != 0)
            {
                Color old = GUI.color;
                GUI.color = new Color(((color >> 16) & 255) / 255f, ((color >> 8) & 255) / 255f, (color & 255) / 255f, (color >> 24) / 255f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old;
            }
            if (rect.Contains(Event.current.mousePosition)) iconHoverName = LedgerFreshness.Hint(market, currentContext, offerGameTick);
        }
        private void OfferVendor(Rect rect, string vendor, LedgerObservation market, GUIStyle style)
        {
            var state = OfferAge(market); string badge = LedgerFreshness.Badge(state);
            float reserve = LedgerFreshness.BadgeWidth(state);
            Color previous=GUI.contentColor;GUI.contentColor=OfferTextColor(market,previous);
            GUI.Label(new Rect(rect.x, rect.y, Math.Max(0, rect.width - reserve), rect.height), LedgerPresentation.ItemRich(vendor), style);
            if (reserve > 0) GUI.Label(new Rect(rect.xMax - reserve, rect.y, reserve, Math.Min(24, rect.height)), new GUIContent(badge, LedgerFreshness.Hint(market, currentContext, offerGameTick)), small);
            GUI.contentColor=previous;
        }
        private Color OfferTextColor(LedgerObservation market,Color fallback)
        {uint color=LedgerFreshness.TextColor(OfferAge(market));return color==0?fallback:new Color(((color>>16)&255)/255f,((color>>8)&255)/255f,(color&255)/255f,1);}
        private void OfferCells(Rect rect, string[] values, float[] columns, LedgerObservation market, int vendor)
        {
            float x = rect.x;
            for (int i = 0; i < values.Length; i++)
            {
                Color previous=GUI.contentColor;GUI.contentColor=OfferTextColor(market,previous);
                float w = rect.width * columns[i]; var cell = new Rect(x + 5, rect.y + 3, w - 10, rect.height - 4);
                if (i == vendor) OfferVendor(cell, values[i], market, itemButton);
                else GUI.Label(cell, LedgerPresentation.ItemRich(values[i] ?? "unknown"), itemButton);
                x += w;
                GUI.contentColor=previous;
            }
        }
    }
}
