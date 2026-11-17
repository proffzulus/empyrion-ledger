using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    // Manual UI preferences, never market snapshots/offers. Metadata keeps a saved entry useful
    // even if it drops out of the latest assortment; its exact item/variant identity is retained.
    public sealed class LedgerItemPinScope
    {public string World;public int Player;public List<LedgerViewItem> Items=new List<LedgerViewItem>();}
    public static class LedgerItemPins
    {
        public static LedgerItemPinScope For(LedgerMarkerModel model,string world,int player,bool create=false)
        {
            if(model==null||string.IsNullOrEmpty(world)||player<=0)return null;
            var p=model.Data.ItemPins.FirstOrDefault(x=>x.World==world&&x.Player==player);
            if(p==null&&create){if(model.Data.ItemPins.Count>=64)throw new InvalidOperationException("Item pin scope limit64");p=new LedgerItemPinScope{World=world,Player=player};model.Data.ItemPins.Add(p);model.Revision++;}
            return p;
        }
        public static bool Has(LedgerItemPinScope pins,string key){return pins!=null&&pins.Items.Any(x=>x.Key==key);}
        public static void Toggle(LedgerMarkerModel model,string world,int player,LedgerViewItem item)
        {
            if(!Valid(item))throw new InvalidOperationException("Item identity unavailable");
            var p=For(model,world,player,true);if(p==null)throw new InvalidOperationException("Current save/player unavailable");
            if(!p.Items.Any(x=>x.Key==item.Key)){if(p.Items.Count>=32768)throw new InvalidOperationException("Item pin size limit32768");p.Items.Add(LedgerJson.Decode<LedgerViewItem>(LedgerJson.Encode(item)));}
            else p.Items.RemoveAll(x=>x.Key==item.Key);
            model.Revision++;
        }
        public static bool Valid(LedgerViewItem item)
        {return item!=null&&item.Id>0&&(!item.Canonical||!item.Auxiliary.HasValue)&&item.Key==item.Id.ToString(CultureInfo.InvariantCulture)+":"+(item.Canonical?"catalog":item.Auxiliary.HasValue?item.Auxiliary.Value.ToString(CultureInfo.InvariantCulture):"unknown")&&
            !string.IsNullOrWhiteSpace(item.Name)&&item.Name.Length<=512&&(item.Markup??"").Length<=4096&&(item.Sprite??"").Length<=512&&(item.Category??"").Length<=256;}
        public static void Validate(List<LedgerItemPinScope> scopes)
        {
            if(scopes.Count>64||scopes.Any(p=>p==null||string.IsNullOrEmpty(p.World)||p.World.Length>256||p.Player<=0||p.Items==null||p.Items.Count>32768||p.Items.Any(i=>!Valid(i))||p.Items.Select(i=>i.Key).Distinct().Count()!=p.Items.Count)||scopes.Select(p=>p.World+"|"+p.Player).Distinct().Count()!=scopes.Count)throw new InvalidOperationException("Invalid item pin preferences; original preserved");
        }
        public static List<LedgerViewItem> Include(IEnumerable<LedgerViewItem> source,LedgerItemPinScope pins,string search,Func<LedgerViewItem,bool> inScope=null)
        {
            var items=source.ToList();if(pins==null)return items;var keys=new HashSet<string>(items.Select(i=>i.Key),StringComparer.Ordinal);
            foreach(var saved in pins.Items){
                if(keys.Contains(saved.Key)||inScope!=null&&!inScope(saved))continue;
                if(!string.IsNullOrEmpty(search)&&(LedgerPresentation.Text(saved.Name)+" "+saved.Key).IndexOf(search,StringComparison.OrdinalIgnoreCase)<0)continue;
                items.Add(saved);keys.Add(saved.Key);
            }return items;
        }
        public static List<LedgerTreeEntry> Tree(IEnumerable<LedgerViewItem> source,ISet<string> collapsed,string search,LedgerItemPinScope pins)
        {
            var rows=source.GroupBy(i=>i.Key).Select(g=>g.First()).ToList();var result=new List<LedgerTreeEntry>();var keys=new HashSet<string>(pins==null?Enumerable.Empty<string>():pins.Items.Select(i=>i.Key),StringComparer.Ordinal);
            var top=rows.Where(i=>keys.Contains(i.Key)).OrderBy(i=>i.Name,StringComparer.OrdinalIgnoreCase).ThenBy(i=>i.Key,StringComparer.Ordinal).ToList();
            if(top.Count>0){result.Add(new LedgerTreeEntry{Category="Pinned",Expanded=true,PinnedGroup=true});result.AddRange(top.Select(i=>new LedgerTreeEntry{Category="Pinned",Item=i}));}
            result.AddRange(LedgerUi.Tree(rows.Where(i=>!keys.Contains(i.Key)),collapsed,search));return result;
        }
    }
}
