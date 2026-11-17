using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using TradeLedgerProbeB;

namespace BOfflineTests
{
    internal static class LedgerDistanceTests
    {
        private const string Certified="native exact observed PF 5D4B;system5D49/sector5D4A;type5D8D/mapType5D51/space5D85;group5D4C-native-sector-space-PF;cache-only";
        private static LedgerGeography Geo(string system="0,0,0",string sector="0,0,0")
        {return new LedgerGeography{SystemCoordinates=system,SectorCoordinates=sector,Source=Certified};}
        private static LedgerObservation Observation(string pf,LedgerGeography geo,int id=1,string world="world")
        {return new LedgerObservation{Context=new LedgerContext{WorldKey=world,Playfield=pf,SolarSystem="Label is not an identity",Geography=geo},Identity=new LedgerIdentity{NpcEntityId=id,OwnerConsistent=true,NativePosition="3,4,0",PositionSource="1B3A/EntityBridge.Position"},Offers=new List<LedgerOffer>()};}
        private static LedgerViewData Data(params LedgerObservation[] observations)
        {var d=new LedgerViewData{WorldKey="world"};d.Markets.AddRange(observations);d.PrepareDistances();return d;}
        private static LedgerPlayerPoint Player(string pf="Home",LedgerGeography geo=null,string world="world")
        {return new LedgerPlayerPoint{World=world,Playfield=pf,Geography=geo??Geo()};}
        internal static void Run(Action<bool,string> check,string root)
        {
            int count=0;Action<bool,string> c=(v,s)=>{check(v,"distances: "+s);count++;};
            var near=Observation("Neighbor",Geo(sector:"300,400,0"),2);var far=Observation("Other star",Geo("300000,400000,0","99,88,77"),3);
            var local=Observation("Home",Geo(),4);var sameSector=Observation("Moon A",Geo(),5);var noGeo=Observation("Unresolved",null,6);
            var data=Data(local,near,far,sameSector,noGeo);var player=Player();
            c(data.DistanceFor(near,player)=="50.0 AU","3-4-5 sector-coordinate fixture =500 *native0.1 AU; not local NPC distance");
            c(data.DistanceFor(far,player)=="5.0 ly","3-4-5 galactic fixture =500000 *native0.1 /10000 ly");
            c(data.DistanceFor(local,player)=="~5.0 m","same-playfield metre display unchanged");
            local.Identity.NativePosition="1500,0,0";c(data.DistanceFor(local,player)=="~1.50 km","same-playfield kilometre display unchanged");
            c(data.DistanceFor(sameSector,player)=="unknown","orbit/moon sharing a sector is not fabricated zero AU");
            c(data.DistanceFor(noGeo,player)=="unknown","unavailable geography stays unknown");
            c(data.DistanceReasonFor(near,player)=="Planetary-sector separation; not surface distance.","concise AU explanation specifies native reference points");
            c(data.DistanceReasonFor(far,player)=="Stellar-system separation.","concise ly explanation");
            foreach(var xyz in new[]{"100,0,0","0,-100,0","0,0,100","-60,80,0"}){
                var o=Observation("Target",Geo(sector:xyz));c(Data(o).DistanceFor(o,player)=="10.0 AU","axis/negative-coordinate sector fixture "+xyz);}
            foreach(var xyz in new[]{"100000,0,0","0,-100000,0","0,0,100000","-60000,80000,0"}){
                var o=Observation("Target",Geo(xyz));c(Data(o).DistanceFor(o,player)=="1.0 ly","axis/negative-coordinate galactic fixture "+xyz);}
            var tiny=Observation("Tiny",Geo("1,0,0"));c(Data(tiny).DistanceFor(tiny,player)=="<0.1 ly","positive sub-display-resolution value cannot round to false zero");
            foreach(string bad in new[]{null,"","1,2","1,2,3,4","NaN,2,3","Infinity,0,0","1.1,2,3","2147483648,0,0","2147483647,0,0","-2147483648,0,0"}){
                var o=Observation("Invalid",Geo(bad));c(Data(o).DistanceFor(o,player)=="unknown","malformed/sentinel system coordinate rejected: "+bad);}
            foreach(string bad in new[]{null,"","1,2","NaN,0,0","1.5,0,0"}){
                var o=Observation("Invalid",Geo(sector:bad));c(Data(o).DistanceFor(o,player)=="unknown","invalid within-system sector rejected: "+bad);}
            var noSector=Observation("Star only",Geo("100000,0,0",null));c(Data(noSector).DistanceFor(noSector,player)=="1.0 ly","stellar separation does not need a fabricated sector");
            foreach(string source in new[]{null,"","header PlayfieldType","system5D49/sector5D4A","guessed by name"}){
                var g=Geo(sector:"100,0,0");g.Source=source;var o=Observation("Unproven",g);c(Data(o).DistanceFor(o,player)=="unknown","unproven coordinate provenance rejected: "+source);}
            var conflict=Observation("Neighbor",Geo(sector:"301,400,0"),7);
            c(Data(near,conflict).DistanceFor(near,player)=="unknown","conflicting exact PF coordinates fail closed");
            var conflictSystem=Observation("Neighbor",Geo("1,0,0","300,400,0"),8);
            c(Data(near,conflictSystem).DistanceFor(near,player)=="unknown","conflicting stellar identity for PF fails closed");
            var invalidDuplicate=Observation("Neighbor",Geo("invalid","300,400,0"),9);
            c(Data(near,invalidDuplicate).DistanceFor(near,player)=="unknown","malformed certified duplicate cannot be discarded in favor of valid-looking number");
            c(Data(near,near,near).DistanceFor(near,player)=="50.0 AU","repeated observations do not alter distance");
            c(data.DistanceFor(near,Player(world:"different save"))=="unknown","foreign current save cannot borrow geography");
            c(Data(Observation("Neighbor",near.Context.Geography,2,"different save")).DistanceFor(near,player)=="unknown","foreign recorded save excluded");
            c(data.DistanceFor(Observation("Neighbor",near.Context.Geography,2,"different save"),player)=="unknown","foreign target rejected even same PF label");
            c(data.DistanceFor(near,null)=="unknown","unavailable current player position");
            var unavailable=Player();unavailable.Geography=null;c(data.DistanceFor(near,unavailable)=="unknown","missing current native geography");
            c(data.DistanceFor(near,Player("Home",Geo("1,0,0")))=="unknown","current versus stored PF coordinate conflict");
            var duplicateName=Observation("Another PF",Geo("100000,0,0"));c(Data(duplicateName).DistanceFor(duplicateName,player)=="1.0 ly","identical system labels cannot join different coordinate identities");
            var noName=Observation("Unnamed system",Geo(sector:"300,400,0"));noName.Context.SolarSystem=null;
            c(Data(noName).DistanceFor(noName,player)=="50.0 AU","coordinate-backed hierarchy needs no guessed system/planet label");
            var moon=Observation("Moon only",Geo(sector:"30,40,0"));moon.Context.Geography.BodyKind="Moon";moon.Context.Geography.GroupName="Native orbit";
            c(Data(moon).DistanceFor(moon,player)=="5.0 AU","moon-only groups across sectors use actual coords without inventing central planet");
            player.X=1400;c(data.DistanceFor(local,player)=="~100.0 m","local movement refreshes local distance");
            c(data.DistanceFor(near,player)=="50.0 AU","local position is never mixed into sector frame");
            var moved=Player("Neighbor",near.Context.Geography);c(data.DistanceFor(far,moved)=="5.0 ly","PF change uses new current frame");
            c(data.DistanceFor(near,moved)=="~5.0 m","arrival restores unchanged local distance presentation");
            var other=Data(Observation("Neighbor",Geo(sector:"60,80,0")));c(other.DistanceFor(other.Markets[0],Player())=="10.0 AU","replacement projection does not reuse prior distance cache");
            var copy=LedgerJson.Decode<LedgerObservation>(LedgerJson.Encode(near));c(Data(copy).DistanceFor(copy,Player())=="50.0 AU","serialized/reloaded observation retains certified geography");
            local.Identity.NativePosition="0,0,0";local.Identity.PositionSource="unproven";c(data.DistanceFor(local,Player())=="unknown","local provenance guard unchanged");
            c(data.DistanceFor(null,player)=="unknown","null observation fail closed");
            var offers=new[]{local,near,far,noGeo}.Select(o=>new LedgerViewOffer{Market=o,Offer=new LedgerOffer{ItemId=100,AuxiliaryRaw=0},Sellers=true}).ToList();
            local.Identity.PositionSource="1B3A/EntityBridge.Position";
            var sort=new LedgerHeaderSort{Column=0};var columns=new[]{LedgerOfferColumn.Distance};
            c(LedgerTableSorting.Offers(offers,sort,columns,data,Player()).Select(r=>r.Market).SequenceEqual(new[]{local,near,far,noGeo}),"shared distance header sorts native-scale values, unknown last");
            sort.Descending=true;c(LedgerTableSorting.Offers(offers,sort,columns,data,Player()).Select(r=>r.Market).SequenceEqual(new[]{far,near,local,noGeo}),"reverse distance sort retains unknown last");
            var cache=new LedgerOfferSortCache();var a=cache.Apply(offers,sort,columns,data,Player(),1);c(ReferenceEquals(a,cache.Apply(offers,sort,columns,data,Player(),1)),"unchanged position sample reuses table sort");
            c(!ReferenceEquals(a,cache.Apply(offers,sort,columns,data,moved,2)),"new PF/position sample invalidates distance sorting");
            var index=new LedgerDistanceIndex("world",new[]{near.Context,far.Context});var origin=Player();index.Resolve(origin,near.Context);
            var originField=typeof(LedgerDistanceIndex).GetField("origin",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var parsed=originField.GetValue(index);
            var before=LedgerJson.Encode(near);for(int i=0;i<2000;i++)index.Resolve(origin,near.Context);
            c(ReferenceEquals(parsed,originField.GetValue(index)),"2000 repeated rows/GUI reads do not reparse current geometry or rebuild origin DTO");
            c(before.SequenceEqual(LedgerJson.Encode(near)),"distance display leaves prices/stock/raw observation bytes unchanged");
            origin.Geography=null;c(index.Resolve(origin,near.Context)==null,"current-source loss invalidates cached origin immediately");
            origin.Geography=Geo();c(index.Resolve(origin,near.Context).Text=="50.0 AU"&&!ReferenceEquals(parsed,originField.GetValue(index)),"new admitted origin restores number with fresh parsed frame");
            origin.World="other";c(index.Resolve(origin,near.Context)==null,"cached coordinate never bypasses current save guard");
            c(index.Resolve(Player("Neighbor",near.Context.Geography),near.Context)==null,"cross resolver cannot replace local same-PF logic");
            var scoped=Player();scoped.SaveScope="physical-a";var scope=new LedgerContext{WorldKey="world",Playfield="Home",LocalSaveKey="physical-a",Recordable=true};
            c(LedgerBrowse.SamePositionScope(scoped,scope),"unchanged admitted physical/PF/logical scope may reuse geometry");
            scope.LocalSaveKey="physical-b";c(!LedgerBrowse.SamePositionScope(scoped,scope),"portable world ID and PF unchanged but physical save changed: geometry invalidates");
            scope.LocalSaveKey="physical-a";scope.Playfield="Neighbor";c(!LedgerBrowse.SamePositionScope(scoped,scope),"PF change invalidates cached position and geometry");
            scope.Playfield="Home";scope.WorldKey="other";c(!LedgerBrowse.SamePositionScope(scoped,scope),"logical world change invalidates cached position and geometry");
            scope.WorldKey="world";scope.Recordable=false;c(!LedgerBrowse.SamePositionScope(scoped,scope),"unadmitted context cannot retain a cached position");
            foreach(var size in new[]{new Size(1280,720),new Size(1920,1080),new Size(2560,1440)})c(Preview(root,size,data,new[]{near,far,sameSector,noGeo}),"non-game readable distance column preview "+size.Width+"x"+size.Height);
            File.WriteAllText(Path.Combine(root,"distance-checks.txt"),count.ToString(CultureInfo.InvariantCulture));
        }
        private static bool Preview(string root,Size size,LedgerViewData data,LedgerObservation[] rows)
        {
            using(var bmp=new Bitmap(size.Width,size.Height))using(var g=Graphics.FromImage(bmp))using(var font=new Font("Segoe UI",12))using(var secondary=new Font("Segoe UI",10)){
                g.Clear(Color.FromArgb(15,21,29));float x=70,y=90,w=Math.Min(size.Width-140,1500);g.DrawString("Trade Ledger   /   Markets   /   Mining Equipment",font,Brushes.White,x,y);y+=52;
                g.DrawString("Price       Stock       Vendor / destination",secondary,Brushes.Silver,x,y);g.DrawString("Distance",secondary,Brushes.Silver,x+w-155,y);y+=34;
                foreach(var o in rows){g.DrawLine(Pens.DimGray,x,y-4,x+w,y-4);g.DrawString("14378      27           "+o.Context.Playfield,font,Brushes.White,x,y);
                    g.DrawString(data.DistanceFor(o,Player()),font,Brushes.White,x+w-155,y);y+=42;}
                g.DrawString("Non-game fixture preview - native sector/system distances; unknowns remain explicit",secondary,Brushes.Silver,x,y+30);
                bmp.Save(Path.Combine(root,"distances-"+size.Width+"x"+size.Height+".png"));
                return rows.All(o=>g.MeasureString(data.DistanceFor(o,Player()),font).Width<155);
            }
        }
    }
}
