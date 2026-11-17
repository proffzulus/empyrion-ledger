using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TradeLedgerProbeB
{
    // Build-5188 format adapter. Only resolved item/template metadata, never markets, inventory or unlock state.
    internal sealed class LedgerNativeBrowser
    {
        private readonly Module module;
        private readonly LedgerNativeCatalog native;
        private FieldInfo F(int token,string type,bool isStatic=false)
        {var f=LedgerNativeContracts.Field(module,token);if(f.FieldType.FullName!=type||f.IsStatic!=isStatic)throw new InvalidOperationException("browser-field-signature "+token.ToString("X8"));return f;}
        private readonly FieldInfo all,output,count,inputs,devices,descriptor,amount,expression,id,aux,aliases;
        internal LedgerNativeBrowser(Assembly game,LedgerNativeCatalog source)
        {
            LedgerNativeContracts.Require(game,"recipes");
            module=game.ManifestModule;native=source;
            all=LedgerNativeContracts.Field(module,0x0400196a);if(!all.IsStatic||all.FieldType!=typeof(List<>).MakeGenericType(module.GetType("Assembly-CSharp.NodeProvider")))throw new InvalidOperationException("browser-recipe-list-signature");
            output=F(0x0400198d,"System.Int32");count=F(0x0400198e,"System.Int32");
            inputs=LedgerNativeContracts.Field(module,0x04001991);devices=LedgerNativeContracts.Field(module,0x04001995);
            descriptor=F(0x04002fdf,"Assembly-CSharp.OptionsSerializer");amount=F(0x04002fe1,"System.Int32");expression=F(0x04002fe0,"Assembly-CSharp.ComponentContextConnectionToken");id=F(0x04002fe9,"System.Int32");aux=F(0x04002fec,"System.Int32");aliases=LedgerNativeContracts.Field(module,0x04003253);
            if(inputs.FieldType!=typeof(List<>).MakeGenericType(module.GetType("Assembly-CSharp.PaneFactory"))||devices.FieldType!=typeof(HashSet<string>)||aliases.FieldType!=typeof(Dictionary<,>).MakeGenericType(typeof(string),module.GetType("Assembly-CSharp.OptionsSerializer")))throw new InvalidOperationException("browser-container-signature");
        }
        private static LedgerBrowserItem Entry(LedgerCatalogItem i)
        {return new LedgerBrowserItem{Key=i.Id+":catalog",Id=i.Id,Canonical=true,InternalName=i.InternalName,Name=LedgerPresentation.Text(i.Name??i.InternalName),Markup=i.Markup,Icon=i.Icon,Category=string.IsNullOrWhiteSpace(i.Category)?"Uncategorized":i.Category.Trim(),Source="resolved DomainLayout.323A+ID/name mapping"};}
        internal LedgerBrowserCatalog Read(LedgerCatalog labels,string scope)
        {
            if(labels==null||labels.Items.Count==0||labels.Items.Count>=32768)throw new InvalidOperationException("browser-definition-readiness-or-bound");
            var result=new LedgerBrowserCatalog();
            foreach(var i in labels.Items.Values)
            {
                var entry=Entry(i);entry.Source+="|"+i.Description;object definition=native.Definition(i.Id);
                if(definition!=null)entry.RecipeAlias=native.FullProperty(definition,"TemplateRoot");
                if(definition!=null)entry.Source+="|"+native.FullProperty(definition,"Info")+"|"+native.FullProperty(definition,"Mass")+"|"+native.FullProperty(definition,"Volume")+"|"+native.FullProperty(definition,"TemplateRoot");
                result.Items.Add(entry.Key,entry);
            }
            var named=aliases.GetValue(null) as IDictionary;
            if(named==null||named.Count>65536)throw new InvalidOperationException("browser-named-descriptor-bound");
            foreach(DictionaryEntry pair in named)
            {
                string name=pair.Key as string;object d=pair.Value;if(name==null||name.Length>160||d==null||d.GetType()!=id.DeclaringType)continue;
                int item=(int)id.GetValue(d),variant=(int)aux.GetValue(d);LedgerCatalogItem basis;
                if(variant<=0||!labels.Items.TryGetValue(item,out basis))continue;
                string key=item+":"+variant;if(result.Items.ContainsKey(key))continue;
                // Named descriptor is exact identity evidence. Do not substitute a base icon/description for an unknown form.
                string label=native.LocalizeRich(name);
                result.Items.Add(key,new LedgerBrowserItem{Key=key,Id=item,Auxiliary=variant,Canonical=false,InternalName=name,Name=LedgerPresentation.Text(label),Markup=label,Category=basis.Category??"Uncategorized",Source="named descriptor 323C -> 2FD2/2FD5; variant-specific metadata unavailable"});
            }
            var list=all.GetValue(null) as IList;
            if(list==null||list.Count==0||list.Count>32768){result.Status="Recipes unavailable; craftability unknown";result.Seal(scope);return result;}
            bool complete=true;
            for(int index=0;index<list.Count;index++)
            {
                object r=list[index];if(r==null||r.GetType()!=output.DeclaringType){complete=false;continue;}
                int outId=(int)output.GetValue(r),batch=(int)count.GetValue(r);var ins=inputs.GetValue(r) as IList;var targets=devices.GetValue(r) as HashSet<string>;
                if(outId<=0||batch<=0||ins==null||ins.Count>128||targets==null||targets.Count>128){complete=false;continue;}
                string outKey=outId+":catalog";
                if(!result.Items.ContainsKey(outKey)){complete=false;continue;}
                var recipe=new LedgerBrowserRecipe{Key="template:"+index,OutputKey=outKey,OutputCount=batch,Source="resolved DialogSettings.1963;PageSerializer.1986/1987/198A/198E"};
                foreach(string target in targets.OrderBy(t=>t,StringComparer.Ordinal))
                    if(!string.IsNullOrWhiteSpace(target)&&target.Length<=160&&!string.Equals(target,"none",StringComparison.OrdinalIgnoreCase))recipe.Devices.Add(target);
                recipe.Manufacturing=recipe.Devices.Count>0;
                bool valid=true;
                foreach(object input in ins)
                {
                    if(input==null||input.GetType()!=descriptor.DeclaringType){valid=false;break;}object d=descriptor.GetValue(input);
                    if(d==null||d.GetType()!=id.DeclaringType){valid=false;break;}
                    int item=(int)id.GetValue(d),variant=(int)aux.GetValue(d),number=(int)amount.GetValue(input);
                    string key=item+":catalog";
                    if(!result.Items.ContainsKey(key)){valid=false;break;}
                    if(variant!=0)
                    {
                        key=item+":"+variant;
                        if(!result.Items.ContainsKey(key)){var basis=result.Items[item+":catalog"];result.Items.Add(key,new LedgerBrowserItem{Key=key,Id=item,Auxiliary=variant,InternalName=basis.InternalName,Name=basis.Name+" (form "+variant+")",Markup=basis.Markup+" (form "+variant+")",Category=basis.Category,Source="exact recipe input 2FD2/2FD5;variant display metadata unavailable"});}
                    }
                    recipe.Inputs.Add(new LedgerBrowserInput{ItemKey=key,Quantity=expression.GetValue(input)==null&&number>=0?(int?)number:null});
                }
                if(!valid){complete=false;continue;}
                result.Recipes.Add(recipe);
            }
            result.CompleteRecipes=complete;
            result.Status=complete?"Active definitions / manufacturing templates":"Partial recipe data; missing craftability remains unknown";
            result.Seal(scope);return result;
        }
    }
}
