using System;
using System.Collections.Generic;

namespace TradeLedgerProbeB
{
    // Session-only navigation, independent from observed records and persisted tab preference.
    public sealed class LedgerUiTrail<T>
    {
        private readonly List<T> entries=new List<T>();
        public int Count {get{return entries.Count;}}
        public T Peek {get{return entries.Count==0?default(T):entries[entries.Count-1];}}
        public void Push(T value){if(entries.Count==24)entries.RemoveAt(0);entries.Add(value);}
        public bool Back(out T value){value=Peek;if(entries.Count==0)return false;entries.RemoveAt(entries.Count-1);return true;}
        public void Clear(){entries.Clear();}
    }
    public static class LedgerJournalUi
    {
        // Display order is separate from stable saved/navigation view IDs.
        public static readonly string[] Tabs={"Market","Requests","Locations","Comparison","Production","Item Browser","Destinations","Places"};
        public static readonly int[] TabOrder={0,1,4,6,2,3,5,7};
        public const string Dropdown=" \u25BE";
        public static readonly string[] Scopes={"POI","Body","Sector","System"};
        public static readonly string[] RouteModes={"Show","Hide","Auto"};
        public static int ProductionExtra(bool settings){return settings?68:0;}
        public static int ProductionHeader(bool settings){return 168+ProductionExtra(settings);}
        public static string Side(bool buy){return buy?"Buy here":"Sell here";}
        public static string RouteMode(LedgerMarkerRoute route){return route.Mode??(route.Shown?"show":"hide");}
        public static bool MarkerIssue(LedgerMarkerDestination d)
        {return d.Conflict||!string.IsNullOrEmpty(d.Status)&&(d.Status.IndexOf("failed",StringComparison.OrdinalIgnoreCase)>=0||d.Status.IndexOf("uncertain",StringComparison.OrdinalIgnoreCase)>=0||d.Status.IndexOf("pending",StringComparison.OrdinalIgnoreCase)>=0);}
        public static string BookmarkHint(bool can,string reason,bool ready,bool conflict,string status)
        {return !can?reason:conflict?status:!ready?"Markers unavailable":string.IsNullOrEmpty(status)?"Mark destination":status;}
        // Position and size share the same logical coordinate space as the UI; long warnings remain scrollable.
        public static float[] Tooltip(float x,float y,float width,float height,float screenWidth,float screenHeight)
        {width=Math.Max(80,Math.Min(width,screenWidth-16));height=Math.Max(28,Math.Min(height,screenHeight-16));return new[]{Math.Max(8,Math.Min(x+16,screenWidth-width-8)),Math.Max(8,Math.Min(y+22,screenHeight-height-8)),width,height};}
    }
    // Pure logical-space geometry shared by runtime menus and offline interaction tests.
    public static class LedgerPopupLayout
    {
        public const float Row=30,Padding=4;
        public static float[] Bounds(float x,float y,float width,float height,float desired,int count,float screenWidth,float screenHeight)
        {
            float w=Math.Min(Math.Max(160,desired),Math.Max(32,screenWidth-16));
            float below=Math.Max(0,screenHeight-8-(y+height+4)),above=Math.Max(0,y-4-84);
            float need=Math.Min(480,Math.Max(Row,count*Row)+Padding*2);
            bool down=below>=need||below>=above;float h=Math.Min(need,Math.Max(Row,down?below:above));
            h=Math.Min(h,Math.Max(Row,screenHeight-100));
            return new[]{Math.Max(8,Math.Min(x,screenWidth-w-8)),Math.Max(84,Math.Min(down?y+height+4:y-h-4,screenHeight-h-8)),w,h};
        }
        public static float Scroll(float current,float delta,int count,float viewport)
        {return Math.Max(0,Math.Min(current+delta*Row,Math.Max(0,count*Row+Padding*2-viewport)));}
        public static int Hit(float localY,float scroll,int count,float viewport)
        {if(localY<Padding||localY>=viewport-Padding)return -1;float at=localY+scroll-Padding;int n=(int)Math.Floor(at/Row);return n>=0&&n<count&&at-n*Row<28?n:-1;}
    }
    // A dismissal consumes its complete pointer gesture, including the later release.
    // Open menus also own wheels outside their rectangle; no underlying pane scroll.
    public sealed class LedgerPopupInput
    {
        private bool release;public bool Dismiss {get;private set;}
        public bool Begin(bool open,bool inside,bool pointer,bool down,bool up)
        {Dismiss=open&&pointer&&down&&!inside;if(open&&pointer&&down)release=true;bool block=pointer&&(open||release);if(up)release=false;return block;}
        public void Reset(){release=false;Dismiss=false;}
    }
}
