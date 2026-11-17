using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeLedgerProbeB
{
    public sealed class LedgerRequestFilters
    {
        public bool Done; // A fresh game session defaults to Pending. Never an All state.
        public bool Conversations,Archived;
        public string Playfield,Poi,Search="",Place="all";
        public int? ItemId;public bool Other;
        public LedgerScope Location;
        public LedgerRequestFilters Copy(){return (LedgerRequestFilters)MemberwiseClone();}
        public void ResetRestrictions(){Playfield=Poi=null;Place="all";ItemId=null;Other=false;Search="";Location=null;}
    }
    public sealed class LedgerRequestChoice {public string Key,Label;public int? ItemId;}
    public sealed class LedgerRequestLayoutGate
    {
        private object rows,stock;private float width;private ulong? tick;private int revision,expanded;public int Builds {get;private set;}
        public bool Changed(object r,object inventory,float w,ulong? t,int v,int e){if(Builds>0&&ReferenceEquals(rows,r)&&ReferenceEquals(stock,inventory)&&width==w&&tick==t&&revision==v&&expanded==e)return false;rows=r;stock=inventory;width=w;tick=t;revision=v;expanded=e;Builds++;return true;}
        public static int First(float[] starts,float scroll){int low=0,high=starts.Length;while(low<high){int mid=low+(high-low)/2;if(starts[mid]<=scroll)low=mid+1;else high=mid;}return Math.Max(0,low-1);}
    }
    // Current view/filter only. No record cap or partial query, and no cached stock beyond
    // the admitted inventory projection reference used to order these exact results.
    public sealed class LedgerRequestListProjection
    {
        private LedgerViewData data;private LedgerRequestFilters filters;private object stock;private int revision,geography,stockCount;
        private List<LedgerRequestCard> rows;
        public int Builds {get;private set;}
        public List<LedgerRequestCard> Query(LedgerViewData next,LedgerRequestFilters f,IDictionary<string,LedgerRequestInventoryProgress> inventory,int manualRevision)
        {
            if(rows==null||!ReferenceEquals(data,next)||revision!=manualRevision||geography!=next.GeographyRevision||!ReferenceEquals(stock,inventory)||stockCount!=(inventory==null?0:inventory.Count)||!Same(filters,f)){
                data=next;filters=f.Copy();stock=inventory;stockCount=inventory==null?0:inventory.Count;revision=manualRevision;geography=next.GeographyRevision;rows=LedgerRequestBrowser.Query(next,f,inventory);Builds++;
            }return rows;
        }
        private static bool Same(LedgerRequestFilters a,LedgerRequestFilters b){return a!=null&&a.Done==b.Done&&a.Conversations==b.Conversations&&a.Archived==b.Archived&&a.Playfield==b.Playfield&&a.Poi==b.Poi&&a.Search==b.Search&&a.Place==b.Place&&a.ItemId==b.ItemId&&a.Other==b.Other&&ReferenceEquals(a.Location,b.Location);}
    }
    public static class LedgerRequestBrowser
    {
        public static string StableKey(LedgerRequestCard card)
        {return card.CompletionKey??(card.Request!=null?LedgerManualCompletion.Key(card.Source):LedgerDatabase.Key(card.Source)+"|raw-conversation");}
        public static string PoiKey(LedgerViewData data,LedgerRequestCard card)
        {
            var o=card.Source;var id=data.ObservedPositionIdentity(o);
            return o.Context.WorldKey+"|"+o.Context.Playfield+"|"+(id!=null&&id.StructureEntityId>0?"structure:"+id.StructureEntityId:"endpoint:"+LedgerDatabase.Key(o));
        }
        public static bool IsOther(LedgerRequestCard c)
        {return c.Request==null||c.Request.Fact.Truncated||c.Request.Fact.Items.Count==0||c.Request.Fact.Items.Any(i=>!i.ItemId.HasValue);}
        public static List<LedgerRequestCard> Query(LedgerViewData data,LedgerRequestFilters f,IDictionary<string,LedgerRequestInventoryProgress> inventory=null)
        {
            // Unread completion state cannot silently expose a known Done request as Pending.
            if(!data.ManualStateReady)return new List<LedgerRequestCard>();
            var places=data.RequestPlaces;
            var location=f.Location==null?null:data.Locations;
            var cards=f.Conversations||f.Archived?data.RequestCards().Where(c=>c.Request==null&&data.ArchiveStateReady)
                .Select(c=>{var pages=c.Pages.Where(p=>data.ConversationArchived(p)==f.Archived).ToList();return pages.Count==0?null:new LedgerRequestCard{Source=pages.Last(),Pages=pages};}).Where(c=>c!=null)
                :data.RequestCards().Where(c=>c.Request!=null&&data.ManuallyCompleted(c)==f.Done);
            return cards.Where(c=>places.Contains(f.Place,c.Source))
                .Where(c=>location==null||location.Contains(f.Location,c.Source))
                .Where(c=>f.Playfield==null||c.Source.Context.Playfield==f.Playfield)
                .Where(c=>f.Poi==null||PoiKey(data,c)==f.Poi)
                .Where(c=>f.Other?IsOther(c):!f.ItemId.HasValue||c.Request!=null&&c.Request.Fact.Items.Any(i=>i.ItemId==f.ItemId))
                .Where(c=>string.IsNullOrEmpty(f.Search)||(data.VendorFor(c.Source)+" "+data.PlaceFor(c.Source)+" "+string.Join(" ",c.Pages.Select(o=>LedgerPresentation.Text(o.Dialogue.BodyVerbatim)))).IndexOf(f.Search,StringComparison.OrdinalIgnoreCase)>=0)
                .OrderByDescending(data.ManuallyTracked).ThenByDescending(c=>{LedgerRequestInventoryProgress p;return !f.Done&&inventory!=null&&inventory.TryGetValue(StableKey(c),out p)?p.Rank:0;}).ThenBy(c=>data.PlaceFor(c.Source),StringComparer.OrdinalIgnoreCase)
                .ThenBy(c=>data.VendorFor(c.Source),StringComparer.OrdinalIgnoreCase).ThenBy(StableKey,StringComparer.Ordinal).ToList();
        }
        public static List<LedgerRequestChoice> Playfields(LedgerViewData data)
        {return data.RequestCards().Select(c=>c.Source.Context.Playfield).Distinct().OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).Select(s=>new LedgerRequestChoice{Key=s,Label=s??"Unknown playfield"}).ToList();}
        public static List<LedgerRequestChoice> Pois(LedgerViewData data,string playfield)
        {return data.RequestCards().Where(c=>playfield==null||c.Source.Context.Playfield==playfield).GroupBy(c=>PoiKey(data,c)).Select(g=>new LedgerRequestChoice{Key=g.Key,Label=data.PlaceFor(g.First().Source)})
            .Concat(data.PlaceBrowser.Rows.Where(r=>!r.Record.IdentityConflict&&(playfield==null||r.Record.Latest.Context.Playfield==playfield)).Select(r=>new LedgerRequestChoice{Key=r.RequestKey,Label=r.Name+" / "+r.Record.Latest.Context.Playfield}))
            .GroupBy(r=>r.Key).Select(g=>g.Last()).OrderBy(c=>c.Label,StringComparer.OrdinalIgnoreCase).ThenBy(c=>c.Key,StringComparer.Ordinal).ToList();}
        public static List<LedgerRequestChoice> Items(LedgerViewData data)
        {
            return data.RequestCards().Where(c=>c.Request!=null).SelectMany(c=>c.Request.Fact.Items).Where(i=>i.ItemId>0).GroupBy(i=>i.ItemId.Value)
                .Select(g=>{LedgerCatalogItem item;string name=data.Catalog!=null&&data.Catalog.Items.TryGetValue(g.Key,out item)?item.Name:g.First().Name;return new LedgerRequestChoice{Key=g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),ItemId=g.Key,Label=LedgerPresentation.Text(name)};})
                .OrderBy(c=>c.Label,StringComparer.OrdinalIgnoreCase).ThenBy(c=>c.ItemId).ToList();
        }
    }
    // An explicit row key, not ordinal control order, owns a down/up gesture across refresh/reorder.
    public sealed class LedgerRequestRowLayout
    {
        public readonly float Width;
        public LedgerRequestRowLayout(float width){Width=width;}
        public float Item {get{return Width-316;}}public float Deliver {get{return Width-236;}}
        public float Track {get{return Width-140;}}public float Complete {get{return Width-48;}}
        public float TitleWidth(bool item){return Width-(item?332:252);}
        public const int Checkbox=24,CompletionWidth=42,CompletionHeight=32;
    }
    public sealed class LedgerRequestHeaderLayout
    {
        public readonly float SearchWidth,PlayfieldWidth,PoiWidth,ItemWidth;
        public LedgerRequestHeaderLayout(float width)
        {SearchWidth=Math.Max(130,width*.17f);float available=width-178-SearchWidth-68-30;PlayfieldWidth=available*.27f;PoiWidth=available*.39f;ItemWidth=available*.34f;}
    }
    public sealed class LedgerRequestClick
    {
        private string key;
        public void Begin(string value){key=value;}
        public bool Release(string value,bool inside){if(key!=value)return false;key=null;return inside;}
        public void Cancel(){key=null;}
    }
    public sealed class LedgerRequestPending
    {
        public LedgerManualCompletion Command;public bool Prior;
        public void Apply(LedgerViewData data,bool rollback=false)
        {
            if(data.WorldKey!=Command.World||data.ManualPlayerId!=Command.PlayerId)return;
            (Command.IsArchive?data.ConversationArchives:Command.IsTracking?data.ManualTracking:data.ManualCompletions)[Command.RequestKey]=rollback?Prior:Command.IsArchive?Command.Archived:Command.IsTracking?Command.Tracked:Command.Completed;
        }
    }
}
