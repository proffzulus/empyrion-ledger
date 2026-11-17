using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TradeLedgerProbeB
{
    public sealed class LedgerIngredient { public int Id;public int? Auxiliary,Quantity;public string Name; }
    public sealed class LedgerItemDetails
    {
        public string Description="Description unavailable",NativeStats,Mass="unknown",Volume="unknown",RecipeState="Recipe unavailable",Source;
        public int? OutputCount;
        public readonly List<LedgerIngredient> Ingredients=new List<LedgerIngredient>();
    }
    public static class LedgerItemDisplay
    {
        public static bool SameDefinition(string item,string block){return !string.IsNullOrWhiteSpace(item)&&string.Equals(item,block,StringComparison.Ordinal);}
        public static string Details(LedgerItemDetails d)
        {
            if(d==null)return "Item details unavailable";string description=string.IsNullOrWhiteSpace(d.Description)?"Description unavailable":d.Description;
            string stats=string.IsNullOrWhiteSpace(d.NativeStats)?"Mass: "+d.Mass+" / Volume: "+d.Volume:d.NativeStats;
            string plain=LedgerPresentation.Text(description);return NativeRich(!string.IsNullOrEmpty(plain)&&LedgerPresentation.Text(stats).Contains(plain)?stats:description+"\n\n"+stats);
        }
        // A request identifies the canonical definition, not an invented auxiliary variant.
        // This selection is transient UI state; it never inserts an observed item/offer into storage.
        public static LedgerViewItem Request(LedgerViewData data,LedgerCatalog catalog,LedgerRequestedItem request)
        {
            if(data==null||catalog==null||request==null||!request.ItemId.HasValue||request.ItemId.Value<=0)return null;
            LedgerCatalogItem definition;
            if(!catalog.Items.TryGetValue(request.ItemId.Value,out definition)||definition.Id!=request.ItemId.Value)return null;
            return Definition(definition,request.ItemId.Value+":catalog",null,true);
        }
        private static LedgerViewItem Definition(LedgerCatalogItem definition,string key,int? auxiliary,bool canonical)
        {return new LedgerViewItem{Key=key,Id=definition.Id,Auxiliary=auxiliary,Canonical=canonical,
            Name=string.IsNullOrWhiteSpace(definition.Name)?"Item name unavailable":LedgerPresentation.Text(definition.Name),
            Markup=definition.Markup,Category=definition.Category??"Uncategorized",Sprite=definition.Icon};}
        public static LedgerViewItem Ingredient(LedgerViewData data,LedgerCatalog catalog,LedgerIngredient ingredient)
        {
            if(data==null||ingredient==null||ingredient.Id<=0)return null;
            string key=ingredient.Id+":"+(ingredient.Auxiliary.HasValue?ingredient.Auxiliary.Value.ToString():"unknown");
            var known=System.Linq.Enumerable.FirstOrDefault(data.Items(null),i=>i.Key==key);if(known!=null)return known;
            LedgerCatalogItem definition;if(catalog==null||!catalog.Items.TryGetValue(ingredient.Id,out definition))return null;
            return Definition(definition,key,ingredient.Auxiliary,false);
        }
        public static string EmptyMarket(LedgerViewData data,LedgerViewItem item,bool sellers,bool locationFiltered)
        {
            if(item==null)return "Select an item";
            if(data.Offers(item,true).Count==0&&data.Offers(item,false).Count==0)return "No observed offers for this item";
            return locationFiltered?"No observed offers in this location":sellers?"No observed sellers for this item":"No observed buyers for this item";
        }
        public static string Stat(string formatted)
        {
            if(string.IsNullOrWhiteSpace(formatted))return "unknown";
            double number;
            return double.TryParse(LedgerPresentation.Text(formatted),NumberStyles.Float,CultureInfo.InvariantCulture,out number)?formatted+" (units unavailable)":formatted;
        }
        public static string NativeRich(string source)
        {
            if(source==null)return "unknown";
            source=source.Length>16384?source.Substring(0,16384):source;
            source=source.Replace("\\n","\n").Replace("\uE200","").Replace("\uE201","");
            var tokens=new List<string>();
            string marked=Regex.Replace(source,@"\[(?:/?(?:b|i|c)|-|[0-9a-f]{6}|[0-9a-f]{8})\]|</?(?:b|i|color)(?:=#[0-9a-f]{6}(?:[0-9a-f]{2})?)?>",m=>
            {
                string t=m.Value.ToLowerInvariant(),replacement="";
                if(t=="[b]"||t=="<b>")replacement="<b>";
                else if(t=="[/b]"||t=="</b>")replacement="</b>";
                else if(t=="[i]"||t=="<i>")replacement="<i>";
                else if(t=="[/i]"||t=="</i>")replacement="</i>";
                else if(t=="[-]"||t=="</color>")replacement="</color>";
                else if(t.StartsWith("<color=#",StringComparison.Ordinal)||Regex.IsMatch(t,@"^\[[0-9a-f]{6}(?:[0-9a-f]{2})?\]$"))
                    replacement=t[0]=='['?"<color=#"+t.Substring(1,t.Length-2)+">":t;
                if(replacement.Length==0||tokens.Count>=2048)return "";
                tokens.Add(replacement);return "\uE200"+(tokens.Count-1).ToString(CultureInfo.InvariantCulture)+"\uE201";
            },RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));
            string plain=LedgerPresentation.Rich(marked);
            var output=new System.Text.StringBuilder();var active=new List<string>();var rendered=new List<string>();int offset=0;
            foreach(Match m in Regex.Matches(plain,"\uE200([0-9]{1,4})\uE201"))
            {
                AppendStyled(output,plain.Substring(offset,m.Index-offset),active,rendered);
                string tag=tokens[int.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)];
                if(tag.StartsWith("</",StringComparison.Ordinal))
                {
                    string open=tag=="</color>"?"<color=":tag=="</b>"?"<b>":"<i>";
                    int at=active.FindLastIndex(t=>t.StartsWith(open,StringComparison.Ordinal));
                    // Scenario/NGUI closers may cross: remove only the style actually ended.
                    if(at>=0)active.RemoveAt(at);
                }
                else if(active.Count<32 && (!tag.StartsWith("<color",StringComparison.Ordinal)||active.FindAll(t=>t.StartsWith("<color",StringComparison.Ordinal)).Count<16))active.Add(tag);
                offset=m.Index+m.Length;
                if(output.Length>65536)return LedgerPresentation.Rich(source);
            }
            AppendStyled(output,plain.Substring(offset),active,rendered);
            for(int i=rendered.Count-1;i>=0;i--)output.Append(CloseStyle(rendered[i]));
            return output.Length<=65536?output.ToString():LedgerPresentation.Rich(source);
        }
        private static string CloseStyle(string tag){return tag.StartsWith("<color",StringComparison.Ordinal)?"</color>":tag=="<b>"?"</b>":"</i>";}
        private static void AppendStyled(System.Text.StringBuilder output,string text,List<string> active,List<string> rendered)
        {
            if(text.Length==0)return;
            int common=0;while(common<active.Count&&common<rendered.Count&&active[common]==rendered[common])common++;
            for(int i=rendered.Count-1;i>=common;i--)output.Append(CloseStyle(rendered[i]));
            for(int i=common;i<active.Count;i++)output.Append(active[i]);
            output.Append(text);rendered.Clear();rendered.AddRange(active);
        }

        public static string TotalColor(string action){return action=="Bought"?"#FF7676":action=="Sold"?"#83E49D":null;}
    }
}
