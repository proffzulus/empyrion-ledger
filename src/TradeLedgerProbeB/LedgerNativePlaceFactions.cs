using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Eleon.Modding;
using UnityEngine;
namespace TradeLedgerProbeB {
 // Optional, UI-open-only projection. Only exact already-observed faction keys are
 // looked up. No entity/faction enumeration, native getters, DB writes or observers.
 internal sealed class LedgerNativePlaceFactions {
  readonly Module module;readonly Dictionary<int,FieldInfo> fields=new Dictionary<int,FieldInfo>();
  readonly MethodInfo world,actor,player;long next;LedgerViewData previous;
  internal LedgerNativePlaceFactions(Assembly game){LedgerNativeContracts.Require(game,"place-factions");module=game.ManifestModule;
   foreach(int token in new[]{0x5619,0x561d,0x5627,0x5629,0x12d9,0x12f8,0x12d7,0x12d8,0x5602,0x5603,0x5604,0x5609,0x560d,0x560e,0x1ba5,0x5640,0x1ba6,0x1305,0x64e6,0x4f4a,0x5d71})fields.Add(token,LedgerNativeContracts.Field(module,0x04000000+token));
   world=(MethodInfo)LedgerNativeContracts.Method(module,0x06006c05);actor=(MethodInfo)LedgerNativeContracts.Method(module,0x060053af);player=(MethodInfo)LedgerNativeContracts.Method(module,0x060053b0);
  }
  object F(int k,object v){return fields[k].GetValue(v);}
  object Current(LedgerViewData data,IApplication app,LedgerContext c){
   if(data==null||c==null||!c.Recordable||c.Mode!="SinglePlayer"||app==null||app.Mode.ToString()!="SinglePlayer"||app.State.ToString()!="Running"||app.LocalPlayer==null||!LedgerPlaceBrowser.Current(data,c.WorldKey,app.LocalPlayer.Id)||!LedgerContext.MatchesCurrentSave(c,app.GetPathFor(AppFolder.SaveGame)))return null;
   object scene=F(0x64e6,null),w=scene==null?null:world.Invoke(scene,null),d=w==null?null:F(0x4f4a,w);
   return d!=null&&(string)F(0x5d71,d)==c.Playfield&&(int)player.Invoke(w,null)==app.LocalPlayer.Id?w:null;
  }
  internal bool Refresh(LedgerViewData data,IApplication app,LedgerContext c,long now){
   if(ReferenceEquals(data,previous)&&now>=next-2000&&now<next)return false;previous=data;next=now+2000;
   var values=new Dictionary<int,LedgerFactionLive>();
   try{
    object w=Current(data,app,c),root=F(0x5619,null),a=w==null?null:actor.Invoke(w,null);
    object rep=root==null?null:F(0x5627,root),converter=root==null?null:F(0x5629,root);
    var identities=root==null?null:F(0x561d,root) as IDictionary;var points=rep==null?null:F(0x12f8,rep) as IDictionary;
    var columns=F(0x12d7,null) as IDictionary;var matrix=F(0x12d8,null) as IList;
    if(w!=null&&a!=null&&identities!=null&&rep!=null&&converter!=null&&ReferenceEquals(F(0x12d9,converter),rep)){
     var own=(FactionData)F(0x1305,F(0x1ba6,a));int race=(byte)F(0x5640,F(0x1ba5,a));
     if((int)own.Group!=0&&(int)own.Group!=1)return data.PlaceBrowser.ApplyFactions(values);
     foreach(int group in data.PlaceBrowser.FactionGroups){
      long key=(long)group<<32;object row=identities.Contains(key)?identities[key]:null;
      if(row==null||Convert.ToInt32(F(0x5602,row))!=group||(int)F(0x5603,row)!=0)continue;
      string name=F(0x5604,row) as string;var color=(Color32)F(0x5609,row);
      int? level=null;string source="Current native relation unavailable";
      bool friendly=(bool)F(0x560d,row),hostile=(bool)F(0x560e,row);
      if(friendly&&!hostile){level=3;source="Native fixed Friendly affiliation";}
      else if(hostile&&!friendly){level=0;source="Native fixed Unfriendly (always hostile) affiliation";}
      else if(!friendly&&!hostile){
       long repKey=LedgerPlaceFactions.ReputationKey(group,(int)own.Group,own.Id);
       int? score=points!=null&&points.Contains(repKey)?(int?)Convert.ToInt32(points[repKey]):null;
       if(score.HasValue){level=LedgerPlaceFactions.ReputationLevel(score);source="Current native player/faction reputation entry";}
       else if(columns!=null&&columns.Contains(group)&&matrix!=null&&race<matrix.Count){
        var rowValues=matrix[race] as IList;int column=Convert.ToInt32(columns[group]);
        if(rowValues!=null&&column>=0&&column<rowValues.Count){level=LedgerPlaceFactions.ReputationLevel(Convert.ToInt32(rowValues[column]));source="Current initialized native race/faction reputation matrix";}
       }
      }
      values[group]=new LedgerFactionLive{InternalName=name,Color=color.r.ToString("X2")+color.g.ToString("X2")+color.b.ToString("X2"),Level=level,Source=source};
     }
     if(!ReferenceEquals(Current(data,app,c),w)||!ReferenceEquals(actor.Invoke(w,null),a)||!ReferenceEquals(F(0x5619,null),root)||!ReferenceEquals(F(0x561d,root),identities)||!ReferenceEquals(F(0x5627,root),rep)||!ReferenceEquals(F(0x12f8,rep),points)||!ReferenceEquals(F(0x12d7,null),columns)||!ReferenceEquals(F(0x12d8,null),matrix))values.Clear();
    }
   }catch(Exception){values.Clear();} // Failed optional context never becomes Neutral.
   return data!=null&&data.PlaceBrowser.ApplyFactions(values);
  }
 }
 public sealed class LedgerFactionLive {public string InternalName,Color,Source;public int? Level;}
}
