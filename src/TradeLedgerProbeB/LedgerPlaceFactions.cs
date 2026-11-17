using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
namespace TradeLedgerProbeB {
 public enum LedgerPlaceRelation {Unknown,Hostile,Neutral,Friendly,Unfriendly,NotApplicable}
 public sealed class LedgerFactionDefinition {public int Group,Line;public string Name,InternalName,Abbrev,Color;}
 public sealed class LedgerFactionData {public string Format,Content;public LedgerFactionDefinition[] Factions;}
 public sealed class LedgerFactionPresentation {
  public string Name="Faction unknown",Color,InternalName,Source="Faction identity unavailable";
  public LedgerPlaceRelation Relation;public string Standing,RelationSource="Current player relation unavailable";
  public string RelationLabel {get{return Relation==LedgerPlaceRelation.NotApplicable?"N/A":Relation==LedgerPlaceRelation.Unknown?"Relation unknown":Standing=="Honored"?"Friendly (Honored)":Relation.ToString();}}
  public string RelationColor {get{return Relation==LedgerPlaceRelation.Hostile?"FF5555":Relation==LedgerPlaceRelation.Neutral?"FFFF55":Relation==LedgerPlaceRelation.Friendly?"55DD77":Relation==LedgerPlaceRelation.Unfriendly?"FFAA55":"A9B4BE";}}
  // Pure measured native level, not a heuristic from faction colour or names.
  public void Level(int? value,string source){Standing=null;Relation=LedgerPlaceRelation.Unknown;RelationSource=source;
   if(!value.HasValue)return;
   switch(value.Value){case 0:Relation=LedgerPlaceRelation.Hostile;break;case 1:Relation=LedgerPlaceRelation.Unfriendly;break;case 2:Relation=LedgerPlaceRelation.Neutral;break;case 3:Relation=LedgerPlaceRelation.Friendly;break;case 4:Relation=LedgerPlaceRelation.Friendly;Standing="Honored";break;}
  }
 }
 public sealed class LedgerPlaceFactions {
  static readonly Lazy<LedgerPlaceFactions> current=new Lazy<LedgerPlaceFactions>(Load);
  public static LedgerPlaceFactions Current {get{return current.Value;}}
  readonly Dictionary<int,LedgerFactionDefinition> rows=new Dictionary<int,LedgerFactionDefinition>();
  public LedgerPlaceFactions(LedgerFactionData data){if(data==null||data.Format!="TradeLedger.place-factions.v1"||data.Content!="RE2 Build 46"||data.Factions==null||data.Factions.Length>256)return;
   foreach(var r in data.Factions){if(r==null||r.Group<2||r.Group>=100||string.IsNullOrWhiteSpace(r.Name)||r.Name.Length>128||string.IsNullOrWhiteSpace(r.InternalName)||r.InternalName.Length>128||r.Color!=null&&!Regex.IsMatch(r.Color,@"\A[0-9A-F]{6}\z"))continue;if(rows.ContainsKey(r.Group))rows[r.Group]=null;else rows.Add(r.Group,r);}
  }
  public LedgerFactionPresentation Resolve(LedgerObjectFact f,bool conflict=false){var p=new LedgerFactionPresentation();LedgerFactionDefinition d;
   // Actual native FactionGroup.NoFaction=255, not a guess from an asteroid's name.
   if(f!=null&&!conflict&&f.FactionGroup==255){p.Name="No faction";p.Relation=LedgerPlaceRelation.NotApplicable;p.RelationSource="Native NoFaction assignment; player relation not applicable";return p;}
   if(f==null||conflict||!Regex.IsMatch(f.ContentIdentity??"",@"\bRE2 Build 46\b")||!f.FactionGroup.HasValue||f.FactionId!=0||!rows.TryGetValue(f.FactionGroup.Value,out d)||d==null)return p;
   p.Name=d.Name;p.Color=d.Color;p.InternalName=d.InternalName;p.Source="RE2 Factions.ecf:"+d.Line+"; exact observed faction group, localized name and configured faction colour";return p;
  }
  public static int? ReputationLevel(int? points){if(!points.HasValue||points<0||points>32000)return null;int n=points.Value;return n<=6000?0:n<=12000?1:n<=18000?2:n<=24000?3:4;}
  public static long ReputationKey(int npcGroup,int playerGroup,int playerId){return ((long)npcGroup<<40)|((long)(playerGroup&255)<<32)|(uint)playerId;}
  static LedgerPlaceFactions Load(){try{using(var s=typeof(LedgerPlaceFactions).Assembly.GetManifestResourceStream("TradeLedgerProbeB.PlaceFactions.json")){if(s==null||s.Length>131072)return new LedgerPlaceFactions(null);using(var b=new MemoryStream()){s.CopyTo(b);return new LedgerPlaceFactions(LedgerJson.Decode<LedgerFactionData>(b.ToArray()));}}}catch(Exception){return new LedgerPlaceFactions(null);}}
 }
}
