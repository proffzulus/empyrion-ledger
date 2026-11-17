using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TradeLedgerProbeB
{
    public sealed class LedgerCatalogItem
    {
        public int Id;
        public string InternalName, Name, EnglishName, Category, Icon,Markup,Description;
        public bool IsTerrain, IsCurrency; // Kept in reference catalog, not a transferable item-request alias.
    }
    public sealed class LedgerCatalogMatch { public int Start, Length; public string Text, Proof; public int? Id; }
    // Catalog contains item/block definitions only, never offers, entities, dialogue or inventories.
    public sealed class LedgerCatalog
    {
        public readonly Dictionary<int,LedgerCatalogItem> Items = new Dictionary<int,LedgerCatalogItem>();
        private sealed class Alias { internal string Name, Proof; internal int Id; }
        private sealed class Variant { internal string Type,Name;internal int Id; }
        private readonly List<Variant> variants=new List<Variant>();
        // Exact owner-confirmed phrase -> active definition, never numeric ID or fuzzy synonyms.
        // Both native internal name and localized catalog label must match this verified target.
        private static readonly Dictionary<string,string> OwnerConfirmedAliases = new Dictionary<string,string>(StringComparer.Ordinal)
        { { "Nanotubes|Plastic Tubing", "Plastic Tubes" } };
        private readonly Dictionary<string,List<Alias>> firstWords = new Dictionary<string,List<Alias>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex Words = new Regex(@"(?:[\p{L}][\p{L}\p{N}'-]*|\d+(?:\.\d+)*[\p{L}][\p{L}\p{N}'-]*)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(30));
        public string Source = "catalog-unavailable";
        public string ContentIdentity; // Actual active invTitle, never inferred from a save/path/NPC name.
        public void Add(LedgerCatalogItem item)
        {
            if(Items.Count>=32768 || item.Id<=0)return;
            Items[item.Id]=item;
            if(item.IsTerrain||item.IsCurrency)return;
            // RE2 Build46 Grocer script16133/16135 explicitly consumes this exact group,
            // Localization.csv258 names it Sensors and Signals. No global Blocks stripping.
            if(ContentIdentity!=null&&Regex.IsMatch(ContentIdentity,@"\bRE2 Build 46\b",RegexOptions.CultureInvariant)&&item.InternalName=="SensorTriggerBlocks"&&
                (LedgerPresentation.Text(item.Name??"")=="Sensors and Signals"||LedgerPresentation.Text(item.EnglishName??"")=="Sensors and Signals"))
                AddAlias("Sensor and Signal Blocks",item.Id,"RE2 Build46 TD_Grocer_Mission01 HasItem/RemoveItem SensorTriggerBlocks; English localization Sensors and Signals");
            // Internal definition names are authoritative identities too. Split only
            // explicit CamelCase boundaries; no abbreviation/synonym expansion.
            if(!string.IsNullOrEmpty(item.InternalName)&&item.InternalName.Length<=128)
            {
                string internalWords=Regex.Replace(item.InternalName,@"(?<=[a-z])(?=[A-Z])"," ");
                AddAlias(internalWords,item.Id,"active internal definition name; CamelCase word boundaries");
                foreach(string form in LedgerRequestLanguage.LabelForms(internalWords))AddAlias(form,item.Id,"active internal definition name; English inflection grammar");
            }
            foreach(string name in new[]{item.Name,item.EnglishName})
            {
                if(string.IsNullOrWhiteSpace(name) || name.Length>160)continue;
                string alias=LedgerPresentation.Text(name);
                AddAlias(alias,item.Id,"exact active catalog label");
                int colon=alias.IndexOf(": ",StringComparison.Ordinal);
                if(colon>0&&variants.Count<1024&&!variants.Any(v=>v.Id==item.Id&&v.Type==alias.Substring(0,colon)))
                    variants.Add(new Variant{Id=item.Id,Type=alias.Substring(0,colon),Name=alias.Substring(colon+2)});
                // Deterministic English number/word-order grammar, not edit distance or synonyms.
                foreach(string form in LedgerRequestLanguage.LabelForms(alias))
                    AddAlias(form,item.Id,"active catalog label; English inflection/container/colon grammar");
                string confirmed;
                if(OwnerConfirmedAliases.TryGetValue(item.InternalName+"|"+alias,out confirmed))
                    AddAlias(confirmed,item.Id,"owner-confirmed exact dialogue alias; active internal name + catalog label");
                if(!alias.EndsWith("s",StringComparison.OrdinalIgnoreCase))AddAlias(alias+"s",item.Id,"catalog label + English plural s");
                // Only an explicit description relation licenses this alias, not suffix stripping.
                // The same item must say "The fruit of the <its complete catalog label>.".
                if(item.Description!=null && item.Description.Length<=4096 &&
                    LedgerPresentation.Text(item.Description).StartsWith("The fruit of the "+alias+".",StringComparison.OrdinalIgnoreCase))
                    AddAlias(alias+" fruit",item.Id,"exact catalog description: The fruit of the <same item label>.");
            }
        }
        private void AddAlias(string name,int id,string proof)
        {
            var m=Words.Match(name);if(!m.Success || m.Index!=0)return;
            List<Alias> list;
            if(!firstWords.TryGetValue(m.Value,out list)){list=new List<Alias>();firstWords.Add(m.Value,list);}
            var prior=list.FirstOrDefault(x=>x.Name==name&&x.Id==id);
            if(prior!=null){if(proof=="exact active catalog label")prior.Proof=proof;return;}
            if(list.Count<512)list.Add(new Alias{Name=name,Id=id,Proof=proof});
        }
        public List<LedgerCatalogMatch> Match(string text)
        {
            var result=new List<LedgerCatalogMatch>();
            int coveredUntil=0;
            foreach(Match word in Words.Matches(text))
            {
                if(word.Index<coveredUntil)continue;
                List<Alias> aliases;if(!firstWords.TryGetValue(word.Value,out aliases))continue;
                var matches=aliases.Where(a=>word.Index+a.Name.Length<=text.Length && string.Compare(text,word.Index,a.Name,0,a.Name.Length,StringComparison.OrdinalIgnoreCase)==0 &&
                    (word.Index+a.Name.Length==text.Length || (!char.IsLetterOrDigit(text[word.Index+a.Name.Length]) && text[word.Index+a.Name.Length]!='-' && text[word.Index+a.Name.Length]!='\''))).ToList();
                if(matches.Count==0)continue;int longest=matches.Max(a=>a.Name.Length);var best=matches.Where(a=>a.Name.Length==longest).ToList();var ids=best.Select(a=>a.Id).Distinct().ToList();
                string proof=best.Select(a=>a.Proof).FirstOrDefault(p=>p=="exact active catalog label")??best[0].Proof;
                result.Add(new LedgerCatalogMatch{Start=word.Index,Length=longest,Text=text.Substring(word.Index,longest),Id=ids.Count==1?(int?)ids[0]:null,Proof=ids.Count==1?proof:"ambiguous catalog label"});
                coveredUntil=word.Index+longest;
                if(result.Count>=64)break;
            }
            // Shared-head coordination: "Fortify and Overclock Hacking Software".
            // A bare variant is never registered as an unconditional catalog alias.
            foreach(var full in result.ToArray())
            {
                var head=variants.FirstOrDefault(v=>v.Id==full.Id&&full.Text.EndsWith(v.Type,StringComparison.OrdinalIgnoreCase));if(head==null)continue;
                foreach(var v in variants.Where(v=>v.Type==head.Type&&v.Id!=head.Id))
                {
                    int start=Math.Max(0,full.Start-100);string prefix=text.Substring(start,full.Start-start);
                    var match=Regex.Match(prefix,@"(?<![\p{L}\p{N}])"+Regex.Escape(v.Name)+@"\s+(?:and|or)\s+(?:the\s+)?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(30));
                    if(match.Success&&!result.Any(x=>x.Start==start+match.Index)&&result.Count<64)
                        result.Add(new LedgerCatalogMatch{Start=start+match.Index,Length=v.Name.Length,Text=prefix.Substring(match.Index,v.Name.Length),Id=variants.Count(x=>x.Type==v.Type&&x.Name==v.Name)==1?(int?)v.Id:null,Proof="active colon label; coordinated shared type, not a bare variant alias"});
                }
            }
            return result.OrderBy(x=>x.Start).ToList();
        }
        public int? Resolve(string text)
        {
            var exact=ResolveMatch(text);return exact==null?null:exact.Id;
        }
        public LedgerCatalogMatch ResolveMatch(string text)
        {var exact=Match(text).Where(m=>m.Start==0&&m.Length==text.Length).ToList();return exact.Count==1?exact[0]:null;}
    }
    public sealed class LedgerRequestedItem
    {
        public int? ItemId;
        public long? Quantity;
        public string Name, Proof;
    }
    public sealed class LedgerRequestFact
    {
        public string Rule, CatalogSource, RewardText;
        public string Relation, RewardMode, Type;
        public string DefinitionSource,Conflict;
        public long? RewardCredits;
        public bool Truncated;
        public List<LedgerRequestedItem> Items = new List<LedgerRequestedItem>();
        public List<LedgerRequestAlternative> Alternatives = new List<LedgerRequestAlternative>();
    }
    public sealed class LedgerRequestRecord { public LedgerObservation Source; public LedgerRequestFact Fact; }
    public static class LedgerRequestRecognition
    {
        private static Regex R(string pattern){return new Regex(pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(30));}
        private static readonly Regex Cue=R(@"\b(?:what\s+the\s+rooms\s+need\s+is|bring\s+(?:me|us)|(?:I|we)\s+(?:need|want|require)\s+(?:from\s+you|you\s+to)|(?:can|could)\s+you\s+(?:bring|find|deliver|supply)|(?:find|fetch|deliver|supply|collect)\s+(?:me|us)|you\s+get\s+me|if\s+you\s+can\s+get\s+me\s+(?:just\s+)?|if\s+you\s+can\s+supply\s+(?:the\s+)?[\p{L} -]{1,40}\s+with|I['\u2019]m\s+offering\s+[^.!?\r\n]{0,64}\s+for)\b");
        // The recorded engineer names an order, without an imperative "bring me".
        // Require both the bounded order context and its explicit leading quantity line; never scan later prose.
        private static readonly Regex OrderCue=R(@"\b(?:this|current|next)\s+(?:one|order|request)\b[^.!?\r\n]{0,90}?\bis\s+for\s*(?:\.{3}|:)\s*");
        private static readonly Regex Direct=R(@"(?<![\d.,\-])\b(?:x\s*)?(?<q>[1-9][\d,]{0,12}|one|two)\s+(?<name>[\p{L}][\p{L}\p{N}' -]{1,90}?)(?=\s+(?:and|for|from|with|but|then|you|I['\u2019]m|I['\u2019]ll)\b|[+,.;!?\r\n]|$)");
        private static readonly Regex Inverse=R(@"\bsome\s+(?<name>[\p{L}][\p{L}\p{N}' -]{1,80}?)\s+and\s+bring\s+(?:me|us)[\s\p{L}.]{0,24}?(?<q>[1-9][\d,]{0,12})\s+of\s+(?:them|these|those)\b");
        private static readonly Regex Pay=R(@"\b(?:I['’]ll|I\s+will|we['’]ll|we\s+will)\s+pay\s+you\s+(?<q>[1-9][\d,]{0,12})\s+(?:credits|CR)\b");
        private static readonly Regex NoCash=R(@"\bnot\s+in\s+cash\b");
        private static readonly Regex Digits=R(@"^(?:[1-9]\d{0,9}|[1-9]\d{0,2}(?:,\d{3}){1,3})$");
        private static readonly Regex NounPrefix=R(@"\b(?:bring|find|fetch|deliver|supply|collect|get)\s+(?:(?:me|us)\s+)?(?:some\s+|0\s+)?$");
        private static readonly Regex NounSuffix=R(@"^(?:\s*(?:[.!?,;\r\n]|$)|\s+(?:for|from|and|but|then)\b)");
        private static long? Number(string text)
        {if(text.Equals("one",StringComparison.OrdinalIgnoreCase))return 1;if(text.Equals("two",StringComparison.OrdinalIgnoreCase))return 2;
            long n;return Digits.IsMatch(text)&&long.TryParse(text.Replace(",",""),NumberStyles.None,CultureInfo.InvariantCulture,out n)&&n>0&&n<=1000000000?(long?)n:null;}
        public static LedgerRequestFact Recognize(LedgerObservation o,LedgerCatalog catalog=null)
        {bool complete;return RecognizeMeasured(o,catalog,out complete);}
        internal static LedgerRequestFact RecognizeMeasured(LedgerObservation o,LedgerCatalog catalog,out bool complete)
        {
            complete=true;
            using(LedgerProfiler.Measure(LedgerPerfPath.RequestRecognition))
            { try{
            if(o==null||o.Dialogue==null||o.Dialogue.BodyFullyRevealed==false||string.IsNullOrEmpty(o.Dialogue.BodyVerbatim))return null;
            var reference=LedgerRe2Requests.Recognize(o,catalog);if(reference!=null)return reference;
            if(LedgerRequestLanguage.CompletedNarrative(LedgerPresentation.Text(o.Dialogue.BodyVerbatim)))return null;
            var structured=LedgerRequestLanguage.Recognize(o,catalog);
            var fact=structured??RecognizeCore(o,catalog);
            if(fact!=null)fact.Type=fact.Relation=="any"?"alternative-items":fact.Items.Count>1?"compound-items":"single-item";
            return fact;
        }catch(RegexMatchTimeoutException){complete=false;return null;}
            }
        }
        private static LedgerRequestFact RecognizeCore(LedgerObservation o,LedgerCatalog catalog)
        {
            if(o==null || o.Kind!="dialogue-shown" || o.Dialogue==null || string.IsNullOrEmpty(o.Dialogue.BodyVerbatim) || o.Dialogue.BodyVerbatim.Length>4096)return null;
            string text=LedgerPresentation.Text(o.Dialogue.BodyVerbatim);var cue=Cue.Match(text);bool order=false;
            if(!cue.Success){cue=OrderCue.Match(text);order=cue.Success;}if(!cue.Success)return null;
            // Negated commands are not requests. This intentionally supports a bounded set of English forms.
            string prefix=text.Substring(Math.Max(0,cue.Index-24),Math.Min(24,cue.Index));
            if(R(@"\b(?:don['’]t|do\s+not|can['’]t|cannot|never|no\s+longer)\b[^.!?]{0,24}$").IsMatch(prefix))return null;
            string clause=text.Substring(order?cue.Index+cue.Length:cue.Index);
            var fact=new LedgerRequestFact{Rule=order?"shown-English-order-context+leading-explicit-quantity;not-quest-status":"shown-English-request-cue+explicit-item-quantity;not-quest-status",CatalogSource=catalog==null?"unavailable;item-ID-unresolved":catalog.Source};
            if(order)
            {
                // Only the immediately shown x/count + noun line belongs to this order.
                // "2 months" precedes the cue boundary; subsequent numbers/rewards cannot become item quantities.
                var m=Direct.Match(clause);
                if(m.Success&&m.Index==0)Add(fact,m.Groups["name"].Value.Trim(),Number(m.Groups["q"].Value),catalog);
            }
            else foreach(Match m in Direct.Matches(clause))
            {
                if(LedgerRequestLanguage.HasLaterNonRequestRole(clause,m.Index))continue;
                string q=m.Groups["q"].Value;
                // "one can find some ..." in the real Cafe text is prose, not quantity one.
                // Spelled quantities require an immediately attached request verb as well.
                if(char.IsLetter(q[0]) && !NounPrefix.IsMatch(clause.Substring(0,m.Index)))continue;
                Add(fact,m.Groups["name"].Value.Trim(),Number(q),catalog);
            }
            if(!order)foreach(Match m in Inverse.Matches(text))if(!LedgerRequestLanguage.HasLaterNonRequestRole(text,m.Index))Add(fact,m.Groups["name"].Value.Trim(),Number(m.Groups["q"].Value),catalog);
            if(!order && fact.Items.Count==0 && catalog!=null)
                foreach(var m in catalog.Match(clause))
                    // A later catalogue mention is not sufficient: require an attached request verb.
                    if(NounPrefix.IsMatch(clause.Substring(0,m.Start)) && NounSuffix.IsMatch(clause.Substring(m.Start+m.Length)))Add(fact,m.Text,null,catalog);
            if(fact.Items.Count==0)return null;
            var reward=Pay.Match(clause);if(reward.Success){fact.RewardCredits=Number(reward.Groups["q"].Value);fact.RewardText=reward.Value;}
            else{var noCash=NoCash.Match(clause);if(noCash.Success)fact.RewardText=noCash.Value;}
            return fact;
        }
        private static void Add(LedgerRequestFact fact,string name,long? quantity,LedgerCatalog catalog)
        {
            if(fact.Items.Count>=8){fact.Truncated=true;return;}
            if(name.Length<2 || name.StartsWith("of ",StringComparison.OrdinalIgnoreCase) || R(@"^(?:credits|CR|them|these|those)\b").IsMatch(name))return;
            var match=catalog==null?null:catalog.ResolveMatch(name);int? id=match==null?null:match.Id;
            if(fact.Items.Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)&&x.Quantity==quantity))return;
            fact.Items.Add(new LedgerRequestedItem{ItemId=id,Quantity=quantity,Name=name,Proof=id.HasValue?match.Proof+"; text-derived quantity separate":match==null?"explicit shown phrase; no full catalog alias":"explicit shown phrase; ambiguous catalog alias"});
        }
    }
}
