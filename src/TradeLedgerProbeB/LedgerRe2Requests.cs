using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TradeLedgerProbeB
{
    public sealed class LedgerRequestAlternative { public int[] Inputs; }
    public sealed class Re2RequestInput
    { public string InternalName,ExpectedLabel,Name; public long? Quantity;public int? Auxiliary;public bool Bind; }
    public sealed class Re2RequestDefinition
    { public string Hash,Source,Conflict;public Re2RequestInput[] Items;public int[][] Alternatives;public bool RequireFullyRevealed; }
    // Reference-only adapter. A private content-hash lookup cannot enumerate or
    // populate a journal. Only an already captured shown body can enter here.
    public static class LedgerRe2Requests
    {
        private static readonly Lazy<Dictionary<string,Re2RequestDefinition>> known=new Lazy<Dictionary<string,Re2RequestDefinition>>(Load);
        public static string ContentHash(string text)
        {
            string plain=LedgerPresentation.Text(text);
            plain=Regex.Replace(plain,@"@[pdw]\d+"," ",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(40));
            plain=Regex.Replace(plain,@"\s+"," ",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(40)).Trim();
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(plain))).Replace("-","");
        }
        private static Dictionary<string,Re2RequestDefinition> Load()
        {
            var result=new Dictionary<string,Re2RequestDefinition>(StringComparer.Ordinal);
            try
            {
                using(var stream=typeof(LedgerRe2Requests).Assembly.GetManifestResourceStream("TradeLedgerProbeB.Re2RequestDefinitions.json"))
                {
                    if(stream==null||stream.Length>2097152)return result;
                    var bytes=new byte[stream.Length];int read=0,n;while(read<bytes.Length&&(n=stream.Read(bytes,read,bytes.Length-read))>0)read+=n;
                    if(read!=bytes.Length)return result;
                    var definitions=LedgerJson.Decode<Re2RequestDefinition[]>(bytes);
                    if(definitions==null||definitions.Length>1024)return result;
                    foreach(var d in definitions)
                    {
                        if(d.Hash==null||d.Hash.Length!=64||d.Items==null||d.Items.Length==0||d.Items.Length>8||result.ContainsKey(d.Hash))return new Dictionary<string,Re2RequestDefinition>();
                        result.Add(d.Hash,d);
                    }
                }
            }
            catch(Exception){return new Dictionary<string,Re2RequestDefinition>();} // Generic recognition remains available.
            return result;
        }
        public static LedgerRequestFact Recognize(LedgerObservation o,LedgerCatalog catalog)
        {
            if(catalog==null||catalog.ContentIdentity==null||catalog.ContentIdentity.Length>256||!Regex.IsMatch(catalog.ContentIdentity,@"\bRE2 Build 46\b",RegexOptions.CultureInvariant))return null;
            if(o.Kind!="dialogue-shown"||o.Dialogue==null||string.IsNullOrEmpty(o.Dialogue.BodyVerbatim)||o.Dialogue.BodyVerbatim.Length>4096)return null;
            Re2RequestDefinition d;if(!known.Value.TryGetValue(ContentHash(o.Dialogue.BodyVerbatim),out d))return null;
            if(d.RequireFullyRevealed&&o.Dialogue.BodyFullyRevealed!=true)return null;
            var fact=new LedgerRequestFact{Rule="RE2-Build46;exact-visible-content;reviewed-visible-requirements;not-completion",CatalogSource=catalog.Source,DefinitionSource=d.Source,Conflict=d.Conflict,Relation=d.Alternatives.Length>0?"any":"all",Type=d.Alternatives.Length>0?"alternative-items":d.Items.Length>1?"compound-items":"single-item"};
            foreach(var input in d.Items)
            {
                var candidates=catalog.Items.Values.Where(i=>i.InternalName==input.InternalName&&LedgerPresentation.Text(i.Name)==input.ExpectedLabel).Take(2).ToArray();
                bool bound=input.Bind&&!input.Auxiliary.HasValue&&candidates.Length==1;
                fact.Items.Add(new LedgerRequestedItem{ItemId=bound?(int?)candidates[0].Id:null,Quantity=input.Quantity,Name=input.Name,Proof="exact shown Build46 definition; "+(bound?"active internal name + canonical label":"unresolved current definition/variant; no numeric ID guessed")});
            }
            foreach(var group in d.Alternatives)
                if(group!=null&&group.Length>0&&group.All(i=>i>=0&&i<fact.Items.Count))fact.Alternatives.Add(new LedgerRequestAlternative{Inputs=group});
            LedgerRequestLanguage.Reward(fact,LedgerPresentation.Text(o.Dialogue.BodyVerbatim));
            return fact;
        }
    }
}
