using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
namespace TradeLedgerProbeB {
 public sealed class LedgerPlaceNameFamily {public string Name;public string[] Templates,NativeNames;public bool Complete;}
 public sealed class LedgerPlaceTemplate {public string Template,SourceHash,Origin,Reason;public int? PotentialTraders;public bool ResearchVerified;}
 public sealed class LedgerPlaceNameData {public string Format,Content,Scope;public bool Complete;public LedgerPlaceNameFamily[] Families;public LedgerPlaceTemplate[] Templates;}
 public sealed class LedgerPlaceName {
  public string Display,Type,Variant,Hint;public bool Certified;
 }
 // Immutable static-definition index, warmed by the existing reader worker. No native
 // object access, catalogue IO or discovery-dependent numbering in OnGUI.
 public sealed class LedgerPlaceNames {
  static readonly Lazy<LedgerPlaceNames> current=new Lazy<LedgerPlaceNames>(Load);
  public static LedgerPlaceNames Current {get{return current.Value;}}
  readonly Dictionary<string,LedgerPlaceNameFamily> families=new Dictionary<string,LedgerPlaceNameFamily>(StringComparer.OrdinalIgnoreCase);
  readonly Dictionary<string,LedgerPlaceTemplate> templates=new Dictionary<string,LedgerPlaceTemplate>(StringComparer.OrdinalIgnoreCase);
  readonly bool complete;readonly string content;
  public LedgerPlaceNames(LedgerPlaceNameData data){
   complete=data!=null&&data.Complete&&data.Format=="TradeLedger.place-name-families.v1";content=data==null?null:data.Content;
   if(data!=null&&data.Templates!=null&&data.Templates.Length<=8192)foreach(var t in data.Templates){
    if(t==null||string.IsNullOrWhiteSpace(t.Template)||t.Template!=LedgerObjectTypes.TemplateKey(t.Template)||!Regex.IsMatch(t.SourceHash??"",@"\A[0-9A-F]{64}\z")||t.PotentialTraders<0||t.PotentialTraders>4096||(t.Origin!="scenario"&&t.Origin!="base")||!string.IsNullOrEmpty(t.Reason)&&t.PotentialTraders.HasValue)continue;
    if(templates.ContainsKey(t.Template))templates[t.Template]=null;else templates.Add(t.Template,t);
   }
   if(data==null||data.Families==null||data.Families.Length>8192)return;
   foreach(var f in data.Families){
    if(f==null||string.IsNullOrWhiteSpace(f.Name)||f.Name.Length>512||f.Templates==null||f.Templates.Length==0||f.Templates.Length>8192||f.NativeNames==null||f.NativeNames.Length>8192)continue;
    if(f.Templates.Any(t=>string.IsNullOrWhiteSpace(t)||t!=LedgerObjectTypes.TemplateKey(t))||f.Templates.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=f.Templates.Length||!f.Templates.SequenceEqual(f.Templates.OrderBy(t=>t,StringComparer.Ordinal)))continue;
    foreach(string key in new[]{f.Name}.Concat(f.NativeNames).Distinct(StringComparer.OrdinalIgnoreCase)){
     if(string.IsNullOrWhiteSpace(key)||key.Length>512)continue;
     LedgerPlaceNameFamily prior;if(families.TryGetValue(key,out prior)&&!ReferenceEquals(prior,f))families[key]=null;else if(!families.ContainsKey(key))families.Add(key,f);
    }
   }
  }
  public static string Letter(int index){if(index<0||index>8191)throw new ArgumentOutOfRangeException("index");string s="";do{s=(char)('A'+index%26)+s;index=index/26-1;}while(index>=0);return s;}
  public LedgerPlaceTemplate PotentialFor(string template,string observedContent){
   if(!Supported(observedContent))return null;LedgerPlaceTemplate t;
   return templates.TryGetValue(LedgerObjectTypes.TemplateKey(template)??"",out t)?t:null;
  }
  bool Supported(string observedContent){return complete&&content=="RE2 Build 46"&&Regex.IsMatch(observedContent??"",@"\bRE2 Build 46\b",RegexOptions.CultureInvariant);}
  public LedgerPlaceName Resolve(string name,string nativeName,string template,string observedContent){
   string display=LedgerPresentation.Text(name??"Unknown place");var result=new LedgerPlaceName{Display=display,Type="Type unknown"};
   if(!Supported(observedContent)){result.Hint="Variant catalogue unavailable for this content.";return result;}
   LedgerPlaceNameFamily family=null;
   if(!families.TryGetValue(display,out family)&&!string.IsNullOrEmpty(nativeName))families.TryGetValue(nativeName,out family);
   string key=LedgerObjectTypes.TemplateKey(template);int index=family==null?-1:Array.FindIndex(family.Templates,t=>string.Equals(t,key,StringComparison.OrdinalIgnoreCase));
   if(family==null||!family.Complete||index<0){result.Hint="Variant not established by the installed template catalogue.";return result;}
   // A renamed instance must keep its actual name, rather than borrowing a family's
   // ordinal through an old native/localization key or similarly named object.
   if(!string.Equals(display,family.Name,StringComparison.OrdinalIgnoreCase)&&!family.NativeNames.Contains(display,StringComparer.OrdinalIgnoreCase)){result.Hint="Observed instance name retained.";return result;}
   result.Certified=true;result.Variant=family.Templates.Length>1?Letter(index):null;result.Display=display+(result.Variant==null?"":" "+result.Variant);result.Type=family.Name+(result.Variant==null?"":" "+result.Variant);
   result.Hint=family.Templates.Length==1?"One installed template for this name.":"Variant "+result.Variant+" of "+family.Templates.Length+" installed templates, ordered by template name.";return result;
  }
  static LedgerPlaceNames Load(){
   try{using(var s=typeof(LedgerPlaceNames).Assembly.GetManifestResourceStream("TradeLedgerProbeB.PlaceNameFamilies.json")){
    if(s==null||s.Length>2097152)return new LedgerPlaceNames(null);using(var m=new MemoryStream()){s.CopyTo(m);return new LedgerPlaceNames(LedgerJson.Decode<LedgerPlaceNameData>(m.ToArray()));}
   }}catch(Exception){return new LedgerPlaceNames(null);}
  }
 }
}
