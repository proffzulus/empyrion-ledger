using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal static class LedgerGraphRefinementTests
    {
        private static LedgerBrowserItem Item(LedgerBrowserCatalog c,string name){return c.Items.Values.Single(i=>i.InternalName==name);}
        internal static void Render(LedgerBrowserCatalog c,string path,int width,int height,string rootName,string[] expansions,bool how,LedgerBrowserState exploration=null,bool fit=true)
        {
            var l=LedgerLayout.For(width,height);var state=exploration??new LedgerBrowserState();var selected=Item(c,rootName);if(exploration==null){state.Bind(c);state.Select(selected.Key);foreach(string name in expansions)state.Expand(c,Item(c,name).Key,how);}
            float vx=l.LeftWidth+28,vy=376,vw=l.Width-vx-16,vh=l.Height-vy-24;if(fit){state.Fit(vw,vh);if(exploration!=null)state.Center(vw,vh);}var graph=LedgerGraphGeometry.Project(state,c);
            using(var image=new Bitmap(width,height))using(var g=Graphics.FromImage(image))using(var font=new Font("Segoe UI",15,GraphicsUnit.Pixel))using(var head=new Font("Segoe UI",19,FontStyle.Bold,GraphicsUnit.Pixel))using(var edgeFont=new Font("Segoe UI",11,GraphicsUnit.Pixel))
            {
                g.ScaleTransform(l.Scale,l.Scale);g.Clear(Color.FromArgb(23,57,78));g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString("TRADE LEDGER",head,Brushes.White,18,12);var tabs=LedgerJournalUi.Tabs;for(int n=0;n<tabs.Length;n++)g.DrawString(tabs[n],font,n==5?Brushes.Cyan:Brushes.Gainsboro,18+n*(l.Width-36)/tabs.Length,56);
                g.FillRectangle(Brushes.DarkSlateGray,16,96,l.LeftWidth,28);g.DrawString("Search items",font,Brushes.White,22,100);g.DrawString("All       Craftable      Non-craftable",font,Brushes.White,18,131);
                float ty=166;foreach(var row in LedgerUi.Tree(c.Query("",0).Select(i=>i.Selection),new HashSet<string>(),"")){if(ty>l.Height-36)break;g.DrawString(row.Item==null?"v "+row.Category:"   "+row.Item.Name,font,Brushes.Gainsboro,18,ty);ty+=26;}
                g.FillRectangle(Brushes.DarkSlateGray,vx,96,vw,210);g.DrawString(selected.Name,head,Brushes.White,vx+64,103);g.DrawString("Descriptions and native item icons remain runtime sourced; unavailable in this non-game preview.",font,Brushes.Gainsboro,vx+8,170);
                g.DrawString("Back     Fit     Center      -     +     Clear",font,Brushes.White,vx,320);
                if(state.Notice!=null)g.DrawString(state.Notice,font,Brushes.Gainsboro,vx,348);
                g.SetClip(new RectangleF(vx,vy,vw,vh));
                foreach(var link in graph.Connections)
                {
                    for(int n=1;n<link.Points.Count;n++){var a=link.Points[n-1];var b=link.Points[n];if(LedgerGraphGeometry.Clip(ref a,ref b,vw,vh))g.DrawLine(Pens.SkyBlue,vx+a.X,vy+a.Y,vx+b.X,vy+b.Y);}
                    if(state.Overview)continue;var size=g.MeasureString(link.Label,edgeFont);g.FillRectangle(Brushes.DarkSlateGray,vx+link.LabelAt.X-size.Width/2-3,vy+link.LabelAt.Y-2,size.Width+6,21);g.DrawString(link.Label,edgeFont,Brushes.Cyan,vx+link.LabelAt.X-size.Width/2,vy+link.LabelAt.Y);
                }
                foreach(var item in graph.Items)
                {
                    var b=item.Value;var r=new RectangleF(vx+b.X,vy+b.Y,b.Width,b.Height);if(!r.IntersectsWith(new RectangleF(vx,vy,vw,vh)))continue;
                    g.FillRectangle(Brushes.DarkSlateGray,r);g.DrawRectangle(Pens.SteelBlue,r.X,r.Y,r.Width,r.Height);
                    if(state.Overview){using(var nameFont=new Font("Segoe UI",LedgerGraphGeometry.OverviewFont(state.Zoom),GraphicsUnit.Pixel)){
                        g.DrawString(c.Items[item.Key].Name,nameFont,Brushes.Gainsboro,new RectangleF(r.X+1,r.Y,Math.Max(.1f,r.Width-2),r.Height),new StringFormat{Trimming=StringTrimming.EllipsisCharacter});}continue;}
                    var header=LedgerGraphGeometry.Header(b);var picture=LedgerGraphGeometry.Icon(b,1);
                    using(var titleFont=new Font("Segoe UI",Math.Max(11,16*state.Zoom),GraphicsUnit.Pixel))
                    {
                        Func<string,float,float> measure=(value,w)=>g.MeasureString(value,titleFont,(int)w).Height;
                        string title=LedgerGraphGeometry.Title(c.Items[item.Key].Name,header.Width,header.Height,measure);
                        g.DrawString(title,titleFont,Brushes.Gainsboro,new RectangleF(vx+header.X,vy+header.Y,header.Width,header.Height));
                    }
                    g.FillRectangle(Brushes.Teal,vx+picture.X,vy+picture.Y,picture.Width,picture.Height);
                    g.DrawString("Native\nimage*",edgeFont,Brushes.White,new RectangleF(vx+picture.X,vy+picture.Y,picture.Width,picture.Height));
                    var controls=LedgerGraphGeometry.Controls(b);
                    for(int n=0;n<3;n++)
                    {
                        if(n<2&&!state.CanExpand(c,item.Key,n==0))continue;
                        var v=controls[n];g.DrawRectangle(Pens.SteelBlue,vx+v.X,vy+v.Y,v.Width,v.Height);
                        string label=n==2?"Market":"+";var size=g.MeasureString(label,edgeFont);g.DrawString(label,edgeFont,Brushes.Gainsboro,vx+v.X+(v.Width-size.Width)/2,vy+v.Y+(v.Height-size.Height)/2);
                    }
                }
                g.ResetClip();g.DrawString("NON-GAME SOURCE-FIXTURE PREVIEW; shared geometry/clip, not proof of runtime transform correctness",font,Brushes.Gold,18,l.Height-23);image.Save(path,ImageFormat.Png);
            }
        }
    }
}
