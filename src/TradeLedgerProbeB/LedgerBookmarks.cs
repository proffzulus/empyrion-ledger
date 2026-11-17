using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TradeLedgerProbeB
{
    // A frozen user intention from one already observed offer, never a trade action or live entity.
    public sealed class LedgerBookmarkPlan
    {
        public string World, Playfield, OfferKey, Label, ColorHex, ObservedUtc;
        public int NpcId, ItemId;
        public int? Auxiliary;
        public int? StructureId;
        public string Block,PositionSource,ItemName,UnitPrice,Quantity,Place,Vendor,RequestBody,DeliveryText;
        public string TargetKey,TargetKind,RequestKey; // Exact observed occurrence, never just NPC/item.
        public LedgerObservation RefreshObservation; // Bounded observed trader schedule, not remote stock.
        public float X,Y,Z;
        public bool PlayerBuys, AllowExactNativeReuse;
        public bool VisibleInUniverse=true, VisibleInPlayfield=true, RemoveOnApproach=true;
        public string NativeLabel { get { return "[c]["+ColorHex+"]"+Label+"[-][/c]"; } }
        // Exact build5188 data fields, independently checked against native editor IL.
        public Dictionary<int,object> NativeValues()
        {
            return new Dictionary<int,object>{{0x04005a16,NativeLabel},{0x04005a12,Playfield},
                {0x04005a19,0},{0x04005a1a,false},{0x04005a1b,VisibleInUniverse},
                {0x04005a1c,RemoveOnApproach},{0x04005a1d,VisibleInPlayfield},{0x04005a1e,false}};
        }
    }
    public static class LedgerBookmarkPolicy
    {
        public static bool CurrentScope(LedgerBookmarkPlan p,LedgerContext c)
        { return p!=null&&c!=null&&c.Recordable&&c.Mode=="SinglePlayer"&&c.WorldKey==p.World; }
        public static bool TryCreate(LedgerViewData data,LedgerViewOffer row,LedgerContext current,out LedgerBookmarkPlan plan,out string reason)
        {
            plan=null;reason="Select an observed offer row";
            if(data==null||row==null||row.Market==null||row.Offer==null)return false;
            var o=row.Market;var offer=row.Offer;
            if(current==null||!current.Recordable||current.Mode!="SinglePlayer"||o.Context==null||o.Context.Mode!="SinglePlayer"||
                data.WorldKey!=current.WorldKey||o.Context.WorldKey!=current.WorldKey){reason="Current single-player save scope does not match";return false;}
            if(o.FullCurrentAssortment!=true||!LedgerOffer.Certified(offer)||!(offer.ItemId>0)||
                (row.Sellers?offer.NpcSellsSupported:offer.NpcBuysSupported)!=true){reason="Offer identity/direction support is not certified";return false;}
            if(o.Offers==null||o.Offers.Count(r=>LedgerOffer.Certified(r)&&r.ItemId==offer.ItemId&&r.AuxiliaryRaw==offer.AuxiliaryRaw)!=1)
            {reason="Duplicate current item/variant rows cannot identify one offer safely";return false;}
            if(string.IsNullOrWhiteSpace(o.Context.Playfield)||o.Context.Playfield.Length>160||o.Context.Playfield.Contains("#")){reason="Exact non-instance playfield is unknown";return false;}
            var id=data.ObservedPositionIdentity(o);float x,y,z;string position,source;
            if(!NavigationPosition(id,out position,out source)||!Position(position,source,out x,out y,out z)){reason="No recorded NPC/POI coordinates. Open this trader once to record its location.";return false;}
            string name=offer.Visible==null?null:LedgerPresentation.Text(offer.Visible.Name);
            if(string.IsNullOrWhiteSpace(name)||name=="unknown"||name.Length>120||name.IndexOfAny(new[]{'[',']','<','>','\r','\n'})>=0){reason="Safe captured item display name is unavailable";return false;}
            string price=row.Price;
            if(price==null||price.Length>40||price.IndexOfAny(new[]{'[',']','<','>','\r','\n'})>=0){reason="Safe observed price is unavailable";return false;}
            plan=new LedgerBookmarkPlan{World=current.WorldKey,Playfield=o.Context.Playfield,NpcId=o.Identity.NpcEntityId.Value,
                ItemId=offer.ItemId.Value,Auxiliary=offer.AuxiliaryRaw,OfferKey=LedgerBrowse.OfferKey(o,offer,row.Sellers),
                PlayerBuys=row.Sellers,ColorHex=row.Sellers?"80E680":"80E5FF",X=x,Y=y,Z=z,ObservedUtc=o.Utc,
                Label=(row.Sellers?"Buy ":"Sell ")+name+": "+price+" ("+row.Quantity+")",
                StructureId=id.StructureEntityId,Block=id.BlockGrid,PositionSource=source,ItemName=name,UnitPrice=price,Quantity=row.Quantity,
                Place=data.PlaceFor(o),Vendor=data.VendorFor(o),RefreshObservation=LedgerTradeUpdates.Evidence(o),
                AllowExactNativeReuse=data.Items(null).Count(i=>i.Name.Equals(name,StringComparison.OrdinalIgnoreCase))==1};
            reason=null;return true;
        }
        public static bool TryPlace(LedgerViewData data,LedgerObjectRecord record,LedgerContext current,out LedgerBookmarkPlan plan,out string reason)
        {
            plan=null;reason="Recorded place identity unavailable";
            if(data==null||record==null||record.IdentityConflict||!LedgerDiscoveredObjects.Valid(record.Latest)||record.Key!=LedgerDiscoveredObjects.Key(record.Latest))return false;
            var o=record.Latest;var f=o.DiscoveredObject;
            if(current==null||!current.Recordable||current.Mode!="SinglePlayer"||current.WorldKey!=data.WorldKey||o.Context.WorldKey!=current.WorldKey||f.Player!=data.ManualPlayerId){reason="Current save/player scope does not match";return false;}
            if(string.IsNullOrEmpty(o.Context.Playfield)||o.Context.Playfield.Length>160||o.Context.Playfield.Contains("#")){reason="Exact non-instance playfield unavailable";return false;}
            // Only the recorded bound-instance position (the same world-coordinate source
            // as native marker3710 -> entity1C8A/1B3A). Saved spawn profiles can be stale;
            // they remain searchable but do not grant navigation coordinates here.
            if(!f.X.HasValue||!f.Y.HasValue||!f.Z.HasValue||f.PositionSource==null||!(f.PositionSource=="bound instance position1B3A"||f.PositionSource.Contains("1B3A")&&f.PositionSource.Contains("EntityBridge.Position"))){reason="No supported recorded instance coordinates";return false;}
            string name=System.Text.RegularExpressions.Regex.Replace(LedgerPresentation.Text(data.Objects.Title(record)),@"[\[\]<>\r\n]|\s+"," ").Trim();
            if(name.Length==0||name.Length>512){reason="Safe place name unavailable";return false;}
            plan=new LedgerBookmarkPlan{World=current.WorldKey,Playfield=o.Context.Playfield,TargetKind="place",TargetKey="place:"+record.Key,StructureId=f.NativeId,
                PositionSource=f.PositionSource,X=(float)f.X.Value,Y=(float)f.Y.Value,Z=(float)f.Z.Value,ObservedUtc=o.Utc,Place=name,Label="Visit: "+name,ColorHex="FFFFFF",RemoveOnApproach=true};
            reason=null;return true;
        }
        public static bool TryRequest(LedgerViewData data,LedgerObservation request,LedgerContext current,out LedgerBookmarkPlan plan,out string reason,LedgerRequestFact fact=null)
        {
            plan=null;reason="Requesting NPC identity is unavailable";
            if(data==null||request==null||request.Context==null||request.Identity==null||!(request.Identity.NpcEntityId>0))return false;
            if(current==null||!current.Recordable||current.Mode!="SinglePlayer"||request.Context.Mode!="SinglePlayer"||data.WorldKey!=current.WorldKey||request.Context.WorldKey!=current.WorldKey)
            {reason="Current single-player save scope does not match";return false;}
            if(string.IsNullOrWhiteSpace(request.Context.Playfield)||request.Context.Playfield.Length>160||request.Context.Playfield.Contains("#")){reason="Exact non-instance playfield is unknown";return false;}
            string position,source;var id=data.ObservedPositionIdentity(request);float x,y,z;
            if(!NavigationPosition(id,out position,out source)||!Position(position,source,out x,out y,out z)){reason="No recorded NPC/POI coordinates. Open this NPC or trader once to record its location.";return false;}
            string name=LedgerPresentation.Text(data.VendorFor(request));
            if(string.IsNullOrWhiteSpace(name)||name.Length>120||name.IndexOfAny(new[]{'[',']','<','>','\r','\n'})>=0){reason="Safe requesting NPC name is unavailable";return false;}
            if(fact==null){var known=data.ObservedRequests.FirstOrDefault(r=>ReferenceEquals(r.Source,request)||r.Source.Context.WorldKey==request.Context.WorldKey&&LedgerDatabase.Key(r.Source)==LedgerDatabase.Key(request)&&r.Source.Dialogue!=null&&request.Dialogue!=null&&r.Source.Dialogue.BodyVerbatim==request.Dialogue.BodyVerbatim);fact=known==null?null:known.Fact;}
            if(fact==null){reason="Conversation only; no recognized delivery requirement";return false;}
            string delivery=DeliveryLabel(fact,data.Catalog),requestKey;
            try{requestKey=LedgerManualCompletion.Key(request);}catch(ArgumentException){reason="Exact observed request identity is unavailable";return false;}
            plan=new LedgerBookmarkPlan{World=current.WorldKey,Playfield=request.Context.Playfield,NpcId=request.Identity.NpcEntityId.Value,
                OfferKey=LedgerDatabase.Key(request)+"|request-destination",RequestKey=requestKey,Label=delivery,DeliveryText=delivery,ColorHex="80E5FF",X=x,Y=y,Z=z,ObservedUtc=request.Utc,AllowExactNativeReuse=true,
                StructureId=id.StructureEntityId,Block=id.BlockGrid,PositionSource=source,Place=data.PlaceFor(request),Vendor=name,RequestBody=request.Dialogue==null?null:request.Dialogue.BodyVerbatim};
            reason=null;return true;
        }
        public static string DeliveryLabel(LedgerRequestFact fact,LedgerCatalog catalog=null)
        {
            if(fact==null||fact.Items==null||fact.Items.Count==0)return "Delivery - items unknown";
            Func<LedgerRequestedItem,string> item=i=>{
                LedgerCatalogItem definition;string name=i==null?null:i.Name;
                if(i!=null&&i.ItemId>0&&catalog!=null&&catalog.Items.TryGetValue(i.ItemId.Value,out definition)&&!string.IsNullOrWhiteSpace(definition.Name))name=definition.Name;
                name=System.Text.RegularExpressions.Regex.Replace(LedgerPresentation.Text(name),@"[\[\]<>\r\n]|\s+"," ").Trim();
                if(name.Length==0||name=="unknown")name="item unknown";
                if(name.Length>512)name="item name unavailable";
                return i!=null&&i.Quantity>=0?i.Quantity.Value.ToString(CultureInfo.InvariantCulture)+" "+name:name+" (quantity unknown)";
            };
            string text;
            if(fact.Alternatives!=null&&fact.Alternatives.Count>0){
                if(fact.Alternatives.Any(a=>a==null||a.Inputs==null||a.Inputs.Length==0||a.Inputs.Any(i=>i<0||i>=fact.Items.Count)))return "Delivery - requirements unresolved";
                text=string.Join(" or ",fact.Alternatives.Select(a=>"("+string.Join(" + ",a.Inputs.Select(i=>item(fact.Items[i])))+")"));
                var common=Enumerable.Range(0,fact.Items.Count).Except(fact.Alternatives.SelectMany(a=>a.Inputs)).ToArray();
                if(common.Length>0)text=string.Join(", ",common.Select(i=>item(fact.Items[i])))+" + ("+text+")";
            }else text=string.Join(fact.Relation=="any"?" or ":", ",fact.Items.Select(item));
            if(fact.Truncated)text+=", other items unknown";
            return text.Length>1200?(fact.Alternatives!=null&&fact.Alternatives.Count>0?"Delivery - alternatives":"Delivery - multiple items"):"Delivery "+text;
        }
        public static bool NavigationPosition(LedgerIdentity id,out string position,out string source)
        {
            position=source=null;if(id==null||id.OwnerConsistent==false)return false;float x,y,z;
            if(Position(id.NativePosition,id.PositionSource,out x,out y,out z)){position=id.NativePosition;source=id.PositionSource;return true;}
            if(id.StructureEntityId>0&&id.OwnerConsistent==true&&id.StructurePositionSource=="currentBE30.524D.4A36.4A98/ResourceSerializer.1B3A/EntityBridge.Position;POI-origin-not-NPC"&&Position(id.StructureNativePosition,id.StructurePositionSource,out x,out y,out z))
            {position=id.StructureNativePosition;source=id.StructurePositionSource;return true;}return false;
        }
        public static bool Position(string text,string source,out float x,out float y,out float z)
        {
            x=y=z=0;
            if(source==null||!source.Contains("1B3A")||!source.Contains("EntityBridge.Position"))return false;
            var parts=(text??"").Split(',');
            return parts.Length==3&&float.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out x)&&
                float.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out y)&&float.TryParse(parts[2],NumberStyles.Float,CultureInfo.InvariantCulture,out z)&&Finite(x)&&Finite(y)&&Finite(z);
        }
        private static bool Finite(float n){return !float.IsNaN(n)&&!float.IsInfinity(n)&&Math.Abs(n)<1000000000;}
        public static bool ExactNativeMatch(LedgerBookmarkPlan p,string name,string playfield,int owner,int localId,
            int type,int expectedType,float x,float y,float z,bool shared,bool universe,bool remove,bool hud,bool ownedByOtherIntention=false)
        {
            return !ownedByOtherIntention&&p.AllowExactNativeReuse&&name==p.NativeLabel&&playfield==p.Playfield&&owner==localId&&type==expectedType&&!shared&&
                universe==p.VisibleInUniverse&&remove==p.RemoveOnApproach&&hud==p.VisibleInPlayfield&&
                Math.Abs(x-p.X)<.001f&&Math.Abs(y-p.Y)<.001f&&Math.Abs(z-p.Z)<.001f;
        }
    }
}
