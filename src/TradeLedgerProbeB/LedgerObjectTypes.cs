using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TradeLedgerProbeB
{
    public sealed class LedgerObjectType
    {
        public string Template,DisplayName,Group,SourceHash,LayoutHash,VariantLabel;
        public string[] Aliases;public string[] DefinedTraderTypes;public int? DefinedDialogueLocations;
        // Type knowledge describes an authored prefab, NEVER current staff/offers/condition.
        public string KnowledgeScope="verified prefab definition;not current instance contents";
    }
    public static class LedgerObjectTypes
    {
        private static readonly Lazy<Dictionary<string,LedgerObjectType>> types=new Lazy<Dictionary<string,LedgerObjectType>>(Load);
        public static string TemplateKey(string value)
        {
            if(string.IsNullOrWhiteSpace(value)||value.Length>256)return null;
            value=value.Replace('\\','/');int slash=value.LastIndexOf('/');if(slash>=0)value=value.Substring(slash+1);
            return value.EndsWith(".epb",StringComparison.OrdinalIgnoreCase)?value.Substring(0,value.Length-4):value;
        }
        public static LedgerObjectType Lookup(string template)
        {var key=TemplateKey(template);LedgerObjectType result;return key!=null&&types.Value.TryGetValue(key,out result)?result:null;}
        private static Dictionary<string,LedgerObjectType> Load()
        {
            var result=new Dictionary<string,LedgerObjectType>(StringComparer.OrdinalIgnoreCase);
            using(var s=typeof(LedgerObjectTypes).Assembly.GetManifestResourceStream("TradeLedgerProbeB.DiscoveredObjectTypes.json")){
                if(s==null||s.Length>2097152)return result;using(var memory=new MemoryStream()){s.CopyTo(memory);var rows=LedgerJson.Decode<LedgerObjectType[]>(memory.ToArray());
                    if(rows==null||rows.Length>4096)return result;foreach(var r in rows)if(r.Template!=null&&r.Template==TemplateKey(r.Template)&&r.SourceHash!=null&&r.SourceHash.Length==64&&!result.ContainsKey(r.Template))result.Add(r.Template,r);
                }
            }return result;
        }
    }
}
