using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Eleon.Modding;

namespace TradeLedgerProbeB
{
    // Exact already-observed PF descriptor only. No cache.Values, global sectors or world discovery scan.
    internal sealed class LedgerNativeGeography
    {
        private readonly FieldInfo scene,currentDescriptor,pfName,system,sector,groupName,kind,mapKind,space,x,y,z;
        private readonly MethodInfo getWorld,getCatalog,lookup;
        private readonly LedgerGeographyProjection projection=new LedgerGeographyProjection();
        internal LedgerNativeGeography(Assembly game)
        {
            LedgerNativeContracts.Require(game,"geography");
            var m=game.ManifestModule;scene=LedgerNativeContracts.Field(m,0x040064e6);currentDescriptor=LedgerNativeContracts.Field(m,0x04004f4a);
            pfName=LedgerNativeContracts.Field(m,0x04005d71);groupName=LedgerNativeContracts.Field(m,0x04005d72);if(groupName.FieldType!=typeof(string))throw new InvalidDataException("Native group name signature");system=LedgerNativeContracts.Field(m,0x04005d6f);sector=LedgerNativeContracts.Field(m,0x04005d70);kind=LedgerNativeContracts.Field(m,0x04005db3);mapKind=LedgerNativeContracts.Field(m,0x04005d77);space=LedgerNativeContracts.Field(m,0x04005dab);
            x=LedgerNativeContracts.Field(m,0x04007415);y=LedgerNativeContracts.Field(m,0x04007416);z=LedgerNativeContracts.Field(m,0x04007417);
            getWorld=(MethodInfo)LedgerNativeContracts.Method(m,0x06006c05);getCatalog=(MethodInfo)LedgerNativeContracts.Method(m,0x06005eee);lookup=(MethodInfo)LedgerNativeContracts.Method(m,0x06005efe);
            if(system.FieldType.FullName!="Vector3i"||sector.FieldType!=system.FieldType||kind.FieldType.FullName!="PlayfieldType"||mapKind.FieldType.FullName!="SectorMapObjectType"||space.FieldType!=typeof(bool)||x.FieldType!=typeof(int))throw new InvalidDataException("Geography signature mismatch");
        }
        private string Coord(object value){return ((int)x.GetValue(value)).ToString(CultureInfo.InvariantCulture)+","+((int)y.GetValue(value)).ToString(CultureInfo.InvariantCulture)+","+((int)z.GetValue(value)).ToString(CultureInfo.InvariantCulture);}
        internal LedgerGeography Read(LedgerContext c,IApplication app)
        {
            if(c==null||!c.Recordable||c.Mode!="SinglePlayer"||app==null||app.Mode.ToString()!="SinglePlayer"||app.State.ToString()!="Running"||
                !LedgerContext.MatchesCurrentSave(c,app.GetPathFor(AppFolder.SaveGame))||c.Playfield.Contains("#"))return null;
            object s=scene.GetValue(null),w=s==null?null:getWorld.Invoke(s,null);if(w==null)return null;
            object d=currentDescriptor.GetValue(w);
            if(d==null||(string)pfName.GetValue(d)!=c.Playfield)
            {object cache=getCatalog.Invoke(null,null);d=cache==null?null:lookup.Invoke(cache,new object[]{c.Playfield,false,false});}
            if(d==null||(string)pfName.GetValue(d)!=c.Playfield)return null;
            bool isSpace=(bool)space.GetValue(d);int type=Convert.ToInt32(kind.GetValue(d)),icon=Convert.ToInt32(mapKind.GetValue(d));
            return new LedgerGeography{BodyKind=LedgerGeography.Classify(isSpace,type,icon),
                SystemCoordinates=Coord(system.GetValue(d)),SectorCoordinates=Coord(sector.GetValue(d)),GroupName=(string)groupName.GetValue(d),
                Source="native exact observed PF 5D4B;system5D49/sector5D4A;type5D8D/mapType5D51/space5D85;group5D4C-native-sector-space-PF;cache-only"};
        }
        internal bool Enrich(LedgerViewData data,IApplication app,LedgerContext current,long now)
        {
            try{
            if(data==null||app==null||current==null||app.Mode.ToString()!="SinglePlayer"||app.State.ToString()!="Running"||!LedgerContext.MatchesCurrentSave(current,app.GetPathFor(AppFolder.SaveGame))||app.LocalPlayer==null||app.LocalPlayer.Id!=data.ManualPlayerId)return false;
            bool changed=projection.Refresh(data,current,now,c=>Read(c,app));if(changed)data.GeographyChanged();return changed;
            }catch(Exception){return false;} // Optional label enrichment cannot revoke UI/marker availability.
        }
    }
}
