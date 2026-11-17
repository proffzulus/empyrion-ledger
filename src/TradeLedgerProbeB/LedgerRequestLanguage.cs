using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TradeLedgerProbeB
{
    // Bounded English grammar over ONLY the shown body and active item labels.
    // No dialogue-state/body lookup, scenario quest data or completion side effects.
    public static class LedgerRequestLanguage
    {
        private static Regex R(string p){return new Regex(p,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(40));}
        private const string Num=@"(?:[1-9]\d{0,2}(?:,\d{3}){1,3}|[1-9]\d{0,9}|one|two|three|four|five|six|seven|eight|nine|ten|an?|pair)";
        private static readonly Regex Quantity=R(@"(?<![\d.,\-])\b(?:x\s*)?(?<q>"+Num+@")\s+(?:(?:sets?|bags?|boxes?|bottles?|units?|pieces?|stacks?)\s+(?:or\s+so\s+)?of\s+)?(?:(?:of\s+)?your\s+|new\s+|or\s+so\s+)?$");
        private static readonly Regex Suffix=R(@"^[ \t]*[x\u00d7][ \t]*(?<q>[1-9]\d{0,9})(?!\d|[.,]\d)");
        private static readonly Regex Intent=R(@"\b(?:(?:I|we|residents|crew|they|the\s+client)(?:['’](?:ll|re)|\s+will)?\s+(?:(?:only|really|just|still|currently|here|all|badly)\s+){0,3}(?:need|want|require)|what\s+(?:the\s+rooms|I|we)(?:['’]ll|\s+will)?\s+(?:(?:really|just)\s+)?need|(?:bring|bringing|find|fetch|deliver|supply|collect|get)\s+(?:me|us|back|in)|(?:can|could|will|would)\s+you\s+(?:bring|find|deliver|supply|collect)|(?:resources|materials|parts)\s+required|(?:the\s+)?(?:recipe|order)\s+(?:calls\s+for|is\s+for)|for\s+(?:bringing(?:\s+in)?|replacing|delivering)|request\s+(?:(?:to\s+you|is|I['\u2019]ve\s+got\s+is)\s+)?for)\b");
        private static readonly Regex Context=R(@"\b(?:help\s+(?:us|me)\s+with\s+repairs|short\s+(?:on|of)\s+(?:supplies|parts)|supplies\s+needed|parts?\s+(?:needed|for\s+repairs)|(?:we|I)\s+(?:need|require)|(?:what|all)\s+(?:I|we)(?:['’]ll)?\s+need|(?:resources|materials)\s+required|Task\s*:|order\s+of|replacement\s+of|for\s+(?:bringing(?:\s+in)?|replacing|delivering)|requires?|repairs?|replac(?:e|ement|ing)|missing|work\s+orders?|supply\s+of|shipment\s+of|bringing\s+in|replacing\s+the|orders?\s+(?:right\s+here|for)|if\s+(?:we|I)\s+had|materials|supplies|shipments|help\s+(?:us|me)\s+out)\b");
        private static readonly Regex RejectRole=R(@"\b(?:(?:we|I)\s+(?:sell|stock|bought|received|delivered)|(?:I|we)['’](?:ll|re)\s+(?:give|pay)|(?:rewards?|payments?)\s*:|you\s+(?:will\s+)?(?:receive|get\s+paid))(?=\W|$)");
        private static readonly Regex Negated=R(@"\b(?:don['’]t|do\s+not|can['’]t|cannot|never|no\s+longer)\b[^.!?\n]{0,30}$");
        private static readonly Regex NameEnd=R(@"^(?:\s*(?:[.,;:!?\n\r()\-]|$)|\s+(?:and|or|to|for|from|with|but|then|should|is|are|will|though|please|in|of|that|would|instead|each|so|using|as)\b|\s+(?:I|we|you)\s+(?:would|will|can|could)\b)");
        private static readonly Regex Pronoun=R(@"\b(?:bring|find|get|deliver)\s+(?:me|us)\s+(?:(?:h+m+|um+|uh+)\s*\.*\s*)?(?<q>"+Num+@")(?:\s+of\s+(?:them|these|those))?\b|\b(?:recipe\s+calls\s+for)\s+(?<q>"+Num+@")\s+of\s+them\b");
        private static readonly Regex Reminder=R(@"\bdo\s+you\s+have(?=[^!?]{1,180}\b(?:I|we)\s+asked\s+for\b)");
        private static readonly Regex Pay=R(@"\b(?:(?:I|we)(?:['’]ll|\s+will)\s+(?:pay|wire|give|transfer)(?:\s+you)?|(?:the\s+)?(?:reward|payment)(?:\s+is)?\s*:)\s+(?<q>[1-9][\d,]{0,12})\s*(?:credits|CR)\b");
        // Acquisition/restocking roles, never dialogue-state/NPC dispatch.
        private static readonly Regex Acquisition=R(@"\b(?:(?:(?:I|we|they|residents|rooms|boys|crew)(?:['\u2019](?:ll|re)|\s+will)?|what\s+(?:the\s+)?(?:rooms|boys|crew))\s+(?:(?:still|only|here|be|right\s+now)\s+){0,3}(?:need(?:ing|in['\u2019])?|want|require)|(?:acquire|obtain|source|provide|restock|resupply|top\s+up|buy\s+up|track\s+down|part\s+with)|(?:get|bring)\s+(?:(?:me|us)\s+)?(?:back|the|their)|(?:help|do)\s+[^.!?\n]{0,30}(?:getting|restocking)|(?:getting|gluing)\s+|(?:required|fixable|solve\s+the\s+problem)\s+(?:for|with)|(?:looking\s+at\s+getting|in\s+need\s+of|need\s+replacements|food\s+run|first\s+thing\s+we\s+need))\b");
        private static readonly Regex Explicit=R(@"(?<![\d.,\-])\b(?:x[ \t]*)?(?<q>"+Num+@")[ \t]+(?<name>[^\r\n,;!?]{1,160}?)(?=[,;!?\r\n]|\.(?=\s|$)|$|[ \t]+(?:and|or)(?=[ \t]+(?:x[ \t]*)?"+Num+@"\b|[ \t]*[\r\n.,;!?]|[ \t]*$)|[ \t]+(?:so(?![ \t]+of\b)|using|would|should|will|are|is|though|please|instead|each|to|for|from|with|as|that)\b)");
        private static string[] numberWords={"one","two","three","four","five","six","seven","eight","nine","ten"};
        private static long? Count(string value)
        {
            if(value.Equals("pair",StringComparison.OrdinalIgnoreCase))return 2;
            if(value.Equals("a",StringComparison.OrdinalIgnoreCase)||value.Equals("an",StringComparison.OrdinalIgnoreCase))return 1;
            int index=Array.FindIndex(numberWords,w=>w.Equals(value,StringComparison.OrdinalIgnoreCase));if(index>=0)return index+1;
            if(!R(@"^(?:[1-9]\d{0,9}|[1-9]\d{0,2}(?:,\d{3}){1,3})$").IsMatch(value))return null;
            long n;return long.TryParse(value.Replace(",",""),NumberStyles.None,CultureInfo.InvariantCulture,out n)&&n<=1000000000?(long?)n:null;
        }
        internal static bool HasLaterNonRequestRole(string text,int position)
        {
            string before=text.Substring(0,position);
            var role=RejectRole.Matches(before).Cast<Match>().LastOrDefault();
            var intent=Intent.Matches(before).Cast<Match>().LastOrDefault();
            return role!=null&&(intent==null||role.Index>intent.Index);
        }
        internal static bool NonInput(string text,int start,int length)
        {
            int begin=Math.Max(text.LastIndexOf('\n',Math.Max(0,start-1)),text.LastIndexOf('.',Math.Max(0,start-1)))+1;
            string before=text.Substring(begin,start-begin),after=text.Substring(start,Math.Min(length+130,text.Length-start));
            string local=Regex.Split(after,@"[.!?\r\n]",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(40))[0];
            if(R(@"\bon\s+your\s+armor\b").IsMatch(local))return true;
            if(R(@"\b(?:you(?:['\u2019]ll|\s+will)?\s+(?:(?:definitely|probably|only|even|also|at\s+least)\s+){0,3}need|that(?:\s+will)?\s+require|(?:get|equip)\s+yourself)\b").IsMatch(before)&&R(@"\b(?:carry|carrying|transport|your\s+armor)\b").IsMatch(local))return true;
            return R(@"^\s*(?:(?:or|and)\s+[\p{L}-]+\s+)?(?:factory|shop|supplier|satellite|station)\b").IsMatch(text.Substring(start+length));
        }
        private static string Noun(string value)
        {
            string name=value.Trim().TrimEnd('.',',',':');
            name=R(@"\s+(?:and|or)\s*$").Replace(name,"");
            name=R(@"^(?:(?:sets?|bags?|boxes?|bottles?|units?|pieces?|stacks?|pair)\s+(?:or\s+so\s+)?of\s+|of\s+(?:your|their|both)\s+|single\s+|new\s+)").Replace(name,"");
            name=R(@"^of\s+(?!the\b|a\b|old\b|our\b|their\b|those\b|these\b)[\p{L}-]+(?:\s+[\p{L}-]+){0,2}['\u2019]s\s+(?:new\s+)?").Replace(name,"");
            return name.Trim();
        }
        private static bool Admitted(string text,int start,int length,List<Match> intents,bool context,bool list)
        {
            if(NonInput(text,start,length))return false;
            var cue=intents.LastOrDefault(m=>m.Index<=start&&start-m.Index<650);
            var role=RejectRole.Matches(text.Substring(0,start)).Cast<Match>().LastOrDefault();
            if(role!=null&&(cue==null||role.Index>cue.Index)&&!R(@"[.!?]\s+").IsMatch(text.Substring(role.Index+role.Length,start-role.Index-role.Length)))return false;
            if(list&&context)return true;
            if(cue!=null)
            {
                string between=text.Substring(cue.Index+cue.Length,Math.Max(0,start-cue.Index-cue.Length));
                if(!R(@"[.!?]\s+").IsMatch(between)&&!Negated.IsMatch(between))return true;
            }
            string prefix=text.Substring(Math.Max(0,start-45),Math.Min(45,start));
            string tail=text.Substring(start+length,Math.Min(90,text.Length-start-length));
            return context&&(R(@"\b(?:around|just|preferably|I['\u2019]d\s+estimate|I\s+would\s+say|we['\u2019]re\s+talking|that['\u2019]s|only\s+need)\s*$").IsMatch(prefix)||R(@"^\s*(?:would|should|will|are)\s+(?:be\s+)?(?:enough|do|required)\b").IsMatch(tail));
        }
        public static IEnumerable<string> LabelForms(string label)
        {
            var words=label.Split(' ');
            // A single grammatical inflection at a time; exact phrase/boundaries still required.
            for(int i=0;i<words.Length&&words.Length<=8;i++)
            {
                string w=words[i];if(!R(@"^[A-Za-z]{4,}$").IsMatch(w))continue;
                var copy=(string[])words.Clone();
                if(w.EndsWith("s",StringComparison.OrdinalIgnoreCase)&&!R(@"(?:ss|us|is)$|^(?:electronics|robotics|physics|mechanics)$").IsMatch(w))
                    copy[i]=w.EndsWith("ies",StringComparison.OrdinalIgnoreCase)?w.Substring(0,w.Length-3)+"y":w.Substring(0,w.Length-1);
                else if(w.EndsWith("trix",StringComparison.OrdinalIgnoreCase))copy[i]=w.Substring(0,w.Length-2)+"ices";
                else if(!w.EndsWith("s",StringComparison.OrdinalIgnoreCase))copy[i]=w+(R(@"(?:ch|sh|x|z)$").IsMatch(w)?"es":"s");
                else continue;
                yield return string.Join(" ",copy);
            }
            if(label.EndsWith(" Bottle",StringComparison.OrdinalIgnoreCase))
            {
                string contents=label.Substring(0,label.Length-7);
                yield return "Bottles of "+contents;yield return contents+" Gas Bottles";yield return "Bottles of "+contents+" Gas";
            }
            int colon=label.IndexOf(": ",StringComparison.Ordinal);
            if(colon>0&&colon<label.Length-2)
            {
                string type=label.Substring(0,colon),variant=label.Substring(colon+2);
                yield return variant+" "+type;
                yield return type+" called '"+variant+"'";
            }
        }
        public static bool CompletedNarrative(string text)
        {
            string plain=R(@"@[dpw]\d+\s*").Replace(text," ").Trim();
            bool completed=R(@"^(?:thank\s+you\s+for\s+(?:bringing|delivering)|thanks\s+for\s+(?:bringing|delivering|(?:all\s+)?your\s+help)|you['’]ve\s+(?:already\s+)?(?:delivered|brought)|the\s+request\s+(?:has\s+been|is)\s+completed)\b").IsMatch(plain);
            if(!completed)return false;
            int boundary=plain.IndexOfAny(new[]{'.','!','?','\n'});
            return boundary<0||!Intent.IsMatch(plain.Substring(boundary+1));
        }
        public static LedgerRequestFact Recognize(LedgerObservation o,LedgerCatalog catalog)
        {
            if(catalog==null||o.Kind!="dialogue-shown"||o.Dialogue.BodyVerbatim.Length>4096)return null;
            string text=LedgerPresentation.Text(o.Dialogue.BodyVerbatim).Replace("\\n","\n");
            text=R(@"@p\d+[ \t]*").Replace(text,"\n");text=R(@"@[dw]\d+[ \t]*").Replace(text," ");
            int firstBoundary=text.IndexOfAny(new[]{'.','!','?','\n'});
            if(firstBoundary>=0&&CompletedNarrative(text.Substring(0,firstBoundary)))
                text=new string(' ',firstBoundary+1)+text.Substring(firstBoundary+1);
            var intents=Intent.Matches(text).Cast<Match>().Concat(Acquisition.Matches(text).Cast<Match>()).Concat(R(@"\b(?:and\s+get|(?:it|this|that)(?:\s+will)?\s+requires?)\b").Matches(text).Cast<Match>()).Concat(Reminder.Matches(text).Cast<Match>()).Where(m=>!Negated.IsMatch(text.Substring(Math.Max(0,m.Index-30),Math.Min(30,m.Index)))).OrderBy(m=>m.Index).ToList();
            bool context=Context.IsMatch(text)||intents.Count>0||R(@"(?:\bTask\s*:|\b(?:should\s+(?:fix|work|do)|can\s+(?:you\s+)?handle|grocery\s+run|I['\u2019]d\s+like|running\s+low|supply\s+shortage|needin['\u2019]|restocking)\b)").IsMatch(text);if(!context)return null;
            var fact=new LedgerRequestFact{Rule="shown-English-structural-request+active-catalog;not-quest-status",CatalogSource=catalog.Source,Relation="all"};
            var evidence=new Dictionary<LedgerRequestedItem,int>();
            var matches=catalog.Match(text);
            var phrases=new List<Tuple<int,int>>();
            foreach(Match part in Explicit.Matches(text))
            {
                int start=part.Index;int lineStart=text.LastIndexOf('\n',Math.Max(0,start-1))+1;
                bool list=string.IsNullOrWhiteSpace(text.Substring(lineStart,start-lineStart));
                if(!Admitted(text,start,part.Length,intents,context,list))continue;
                string raw=part.Groups["name"].Value;
                raw=R(@"\s+(?:and\s+)?(?:I|we|you)(?:['\u2019]|\s|$).*$").Replace(raw,"").Trim();
                raw=R(@"\s+and\s+(?:bring|deliver|get|replace)\b.*$").Replace(raw,"").Trim();
                raw=R(@"\s+\(not\b[^)]*\)").Replace(raw,"").Trim();
                string name=Noun(raw);long? q=Count(part.Groups["q"].Value);
                bool article=R(@"^(?:a|an)$").IsMatch(part.Groups["q"].Value);
                string before=text.Substring(0,start);
                if(article&&!R(@"\b(?:bring|find|fetch|deliver|supply|collect|get|source|need|want|require|after)\s+(?:(?:me|us|you\s+to)\s+)*$").IsMatch(before)&&!list)continue;
                if(R(@"^(?:of|can|to|so|for|and|or|we|I|you|minutes?|tonnes?|months?|weeks?|years?|credits|CR|times?|percent)\b").IsMatch(name)||name.Length<2)continue;
                if(R(@"\b(?:factory|satellites?|supplier|shop)\s*$").IsMatch(name))continue;
                if(article&&R(@"^(?:sets?|bags?|boxes?|bottles?|units?)\s+of\b").IsMatch(raw))q=null;
                var known=catalog.ResolveMatch(raw)??catalog.ResolveMatch(name);
                if(article&&known==null&&(!char.IsUpper(name[0])||R(@"\d").IsMatch(name)))continue;
                if(article&&known==null&&!R(@"\b(?:bring|deliver|find|fetch|source|get|acquire|obtain)\s+(?:(?:me|us|just)\s+)*$").IsMatch(before))continue;
                if(known==null&&R(@"\b(?:shipment|supply|load|couple|number|break)\b").IsMatch(name))continue;
                if(known==null&&!list&&!R(@"\b(?:bring|deliver|need|find|fetch|source|get|acquire|obtain|want|require)\s+(?:(?:me|us|just|you\s+to|those)\s+)*$").IsMatch(before))continue;
                if(known==null&&!list&&char.IsLetter(part.Groups["q"].Value[0])&&!char.IsUpper(name[0]))continue;
                // A clause may end after the noun but never inside a named variant.
                Add(fact,known??new LedgerCatalogMatch{Text=name,Proof="explicit visible requirement phrase; full identity unresolved"},q,evidence,part.Groups["name"].Index);
                phrases.Add(Tuple.Create(part.Index,part.Index+part.Length));
            }
            foreach(var m in matches)
            {
                if(phrases.Any(p=>m.Start>=p.Item1&&m.Start<p.Item2)||NonInput(text,m.Start,m.Length))continue;
                string following=text.Substring(m.Start+m.Length);
                if(!NameEnd.IsMatch(following)&&!Suffix.IsMatch(following))continue;
                string before=text.Substring(0,m.Start);var q=Quantity.Match(before);long? quantity=q.Success?Count(q.Groups["q"].Value):null;
                // A set/bag of goods is a container, not an explicit number of goods.
                if(q.Success&&R(@"^(?:a|an)$").IsMatch(q.Groups["q"].Value)&&R(@"\b(?:set|bag|box|bottle|unit|piece|stack)\s+of\b").IsMatch(q.Value))quantity=null;
                if(q.Success&&R(@"^(?:a|an)$").IsMatch(q.Groups["q"].Value)&&!R(@"(?:need|require|find|get|bring|deliver)\s+(?:(?:me|us)\s+)?$").IsMatch(before.Substring(0,q.Index)))continue;
                var suffix=Suffix.Match(text.Substring(m.Start+m.Length));if(suffix.Success)quantity=Count(suffix.Groups["q"].Value);
                int numberStart=q.Success?q.Index:m.Start;
                int lineStart=text.LastIndexOf('\n',Math.Max(0,numberStart-1));lineStart=lineStart<0?0:lineStart+1;
                bool list=(q.Success||suffix.Success)&&string.IsNullOrWhiteSpace(text.Substring(lineStart,numberStart-lineStart));
                var cue=intents.LastOrDefault(x=>x.Index<m.Start&&m.Start-x.Index<420);
                var role=RejectRole.Matches(before).Cast<Match>().LastOrDefault();
                if(role!=null&&(cue==null||role.Index>cue.Index))continue;
                bool attached=cue!=null;
                if(attached)
                {
                    string middle=text.Substring(cue.Index+cue.Length,m.Start-cue.Index-cue.Length);
                    if(RejectRole.IsMatch(middle)||Negated.IsMatch(middle))attached=false;
                    // A new prose sentence cannot inherit a remote imperative. Explicit lists can.
                    if(!list&&R(@"[.!?]\s+[^\n]*$").IsMatch(middle))attached=false;
                }
                bool usingParts=context&&R(@"\busing\s+[^.!?\n]{0,160}$").IsMatch(before.Substring(0,numberStart))&&R(@"\bbring\s+(?:me|us)\s+those\b").IsMatch(text.Substring(m.Start));
                if(!attached&&!(list&&context)&&!usingParts)continue;
                if(!quantity.HasValue&&R(@"\bboth\s+(?:the\s+)?$").IsMatch(before))quantity=1;
                if(!quantity.HasValue&&m.Proof.Contains("colon")&&R(@"\bboth\b[^.!?\n]{0,100}$").IsMatch(before))quantity=1;
                if(!quantity.HasValue&&!R(@"(?:bring|find|fetch|deliver|supply|collect|get|need|requires?|obtain|provide)\s*(?:\.{3})?\s+(?:(?:me|us|some|back|the|their|is|now|just|those)\s+)*$").IsMatch(before))continue;
                Add(fact,m,quantity,evidence,m.Start);
            }
            // Explicit anaphora: nearest previously named catalog item in this text,
            // not a name/timing/entity inference. No cross-page pronoun guessing.
            foreach(Match pronoun in Pronoun.Matches(text))
            {
                // "bring me 5 Fertilizer" names its own input; it cannot also
                // assign five to the last mentioned supplier or company.
                if(!R(@"\bof\s+(?:them|these|those)\b").IsMatch(pronoun.Value)&&R(@"^\s+[\p{L}]").IsMatch(text.Substring(pronoun.Index+pronoun.Length)))continue;
                if(Negated.IsMatch(text.Substring(Math.Max(0,pronoun.Index-30),Math.Min(30,pronoun.Index))))continue;
                var named=matches.LastOrDefault(m=>m.Start+m.Length<=pronoun.Index&&pronoun.Index-m.Start-m.Length<400);
                if(named!=null)
                {
                    var cue=intents.LastOrDefault(m=>m.Index+m.Length<=named.Start);
                    bool attached=cue!=null&&named.Start-cue.Index<420&&!R(@"[.!?]").IsMatch(text.Substring(cue.Index+cue.Length,named.Start-cue.Index-cue.Length));
                    bool some=R(@"\bsome\s+$").IsMatch(text.Substring(0,named.Start));
                    if(attached||some)Add(fact,named,Count(pronoun.Groups["q"].Value),evidence,named.Start);
                }
            }
            // Explicit count-list entries remain useful even when their full noun is
            // absent/ambiguous in the catalog. Never hide an unresolved alternative.
            if(context)foreach(Match line in R(@"(?m)^[ \t]*(?:x\s*)?(?<q>[1-9][\d,]{0,12})\s+(?<name>[^\r\n.!?]{2,120})").Matches(text))
            {
                if(phrases.Any(p=>line.Index>=p.Item1&&line.Index<p.Item2))continue;
                string preceding=text.Substring(0,line.Index);var role=RejectRole.Matches(preceding).Cast<Match>().LastOrDefault();var intent=intents.LastOrDefault(m=>m.Index<line.Index);
                if(role!=null&&(intent==null||role.Index>intent.Index))continue;
                string name=R(@"\s+(?:to|for|and\s+replace)\b.*$").Replace(line.Groups["name"].Value,"").Trim();
                string noun=R(@"^(?:sets?|bags?|boxes?|bottles?|units?|pieces?|stacks?)\s+(?:or\s+so\s+)?of\s+").Replace(name,"");
                if(R(@"^(?:credits|CR|years?|months?|hours?)\b").IsMatch(name))continue;
                long? q=Count(line.Groups["q"].Value);if(!q.HasValue)continue;
                if(fact.Items.Any(i=>i.Quantity==q&&(name.StartsWith(i.Name,StringComparison.OrdinalIgnoreCase)||i.Name.Equals(noun,StringComparison.OrdinalIgnoreCase))))continue;
                var exact=catalog.ResolveMatch(name);Add(fact,exact??new LedgerCatalogMatch{Text=name,Proof="explicit shown count-list; unresolved full catalog phrase"},q,evidence,line.Index);
            }
            if(fact.Items.Count==0)return null;
            if(fact.Items.Count==1&&!fact.Items[0].Quantity.HasValue)
            {
                foreach(Match reference in R(@"\b(?<q>"+Num+@")\s+(?:of\s+(?:them|these|those)|barrels?\s+of\s+the\s+stuff)\b").Matches(text))
                    if(reference.Index>evidence[fact.Items[0]]&&!NonInput(text,reference.Index,reference.Length))fact.Items[0].Quantity=Count(reference.Groups["q"].Value);
            }
            // Use admitted requirement spans, never an earlier conversational mention
            // of the same noun. Otherwise an OR after that mention can be missed.
            var positions=evidence.Values.ToList();
            if(positions.Count>1)
            {
                string between=text.Substring(positions.Min(),positions.Max()-positions.Min());
                if(R(@"(?m)^\s*OR\s*$").IsMatch(between))fact.Relation="any";
                else if(R(@"\bor\b").IsMatch(between))fact.Relation=R(@"\band\b").IsMatch(between)?"mixed-unknown":"any";
            }
            foreach(Match conditional in R(@"(?m)^(?:If\s+you\s+want\s+to\s+(?:construct|make)|To\s+(?:construct|make))[^\r\n]+").Matches(text))
            {
                var indexes=fact.Items.Select((item,index)=>new{item,index}).Where(x=>evidence.ContainsKey(x.item)&&evidence[x.item]>=conditional.Index&&evidence[x.item]<conditional.Index+conditional.Length).Select(x=>x.index).ToArray();
                if(indexes.Length>0)fact.Alternatives.Add(new LedgerRequestAlternative{Inputs=indexes});
            }
            if(fact.Alternatives.Count>1)fact.Relation="any";else fact.Alternatives.Clear();
            Reward(fact,text);
            return fact;
        }
        private static void Add(LedgerRequestFact fact,LedgerCatalogMatch m,long? q,Dictionary<LedgerRequestedItem,int> evidence,int start)
        {
            var prior=fact.Items.FirstOrDefault(i=>(i.ItemId.HasValue&&i.ItemId==m.Id||i.ItemId==m.Id&&i.Name.Equals(m.Text,StringComparison.OrdinalIgnoreCase))&&(!i.Quantity.HasValue||!q.HasValue||i.Quantity==q));
            if(prior!=null){if(!prior.Quantity.HasValue)prior.Quantity=q;if(q.HasValue)evidence[prior]=start;return;}
            if(fact.Items.Count>=8){fact.Truncated=true;return;}
            var item=new LedgerRequestedItem{ItemId=m.Id,Quantity=q,Name=m.Text,Proof=m.Proof+"; explicit shown structural quantity separately"};fact.Items.Add(item);evidence[item]=start;
        }
        public static void Reward(LedgerRequestFact fact,string text)
        {
            var pay=Pay.Match(text);
            if(!pay.Success)pay=R(@"\b(?:contract|reward|payment)(?:['\u2019]s)?[^.!?\n]{0,70}?(?<q>[1-9][\d,]{0,12})\s+(?:galactic\s+standard\s+)?credits\b").Match(text);
            if(!pay.Success)pay=R(@"\b(?<q>[1-9][\d,]{0,12})\s+credits\s+waiting\s+for\s+you\b").Match(text);
            if(!pay.Success)pay=R(@"\b(?<q>[1-9][\d,]{0,12})\s+(?:galactic\s+standard\s+)?credits\b[^.!?\n]{0,50}\bas\s+a\s+payment\b").Match(text);
            if(pay.Success)
            {
                fact.RewardCredits=Count(pay.Groups["q"].Value);fact.RewardText=pay.Value;
                string tail=text.Substring(pay.Index+pay.Length);
                fact.RewardMode=R(@"^\s*(?:each|per\b)").IsMatch(tail)?"stated-per-unit":"stated-amount;no-derived-total";
            }
        }
    }
}
