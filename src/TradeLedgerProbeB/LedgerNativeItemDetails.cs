using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace TradeLedgerProbeB
{
    // Active definition/recipe metadata only; no inventory, market config, entity or gameplay calls.
    internal sealed class LedgerNativeItemDetails
    {
        private readonly Module module;
        private readonly LedgerNativeCatalog catalog;
        private readonly MethodInfo format;
        private readonly FieldInfo props,recipes,output,count,inputs,item,quantity,layout,itemId,aux;
        internal LedgerNativeItemDetails(Assembly game,LedgerNativeCatalog source)
        {
            LedgerNativeContracts.Require(game,"item-details");
            module=game.ManifestModule;catalog=source;
            props=LedgerNativeContracts.Field(module,0x04003256);
            format=(MethodInfo)LedgerNativeContracts.Method(module,0x06006ded);recipes=LedgerNativeContracts.Field(module,0x0400196b);
            output=LedgerNativeContracts.Field(module,0x0400198d);count=LedgerNativeContracts.Field(module,0x0400198e);inputs=LedgerNativeContracts.Field(module,0x04001991);
            item=LedgerNativeContracts.Field(module,0x04002fdf);quantity=LedgerNativeContracts.Field(module,0x04002fe1);layout=LedgerNativeContracts.Field(module,0x04002fe0);itemId=LedgerNativeContracts.Field(module,0x04002fe9);aux=LedgerNativeContracts.Field(module,0x04002fec);
            if(!format.IsStatic||format.ReturnType!=typeof(string)||format.GetParameters().Length!=4||props.FieldType.FullName!="DynamicProperties"||!recipes.IsStatic||recipes.FieldType!=typeof(Dictionary<,>).MakeGenericType(typeof(int),module.GetType("Assembly-CSharp.NodeProvider"))||count.FieldType!=typeof(int)||output.FieldType!=typeof(int)||quantity.FieldType!=typeof(int))throw new InvalidOperationException("item-details-signature");
        }
        internal LedgerItemDetails Read(LedgerViewItem selected)
        {
            var result=new LedgerItemDetails{RecipeState="Recipe unavailable",Source="active current-game definition and recipe metadata;no market inference"};
            object def=catalog.Definition(selected.Id);if(def==null)return result;
            object primary,sharedProperties;catalog.PropertyRoots(def,out primary,out sharedProperties);
            result.Description=catalog.LocalizeRich(catalog.FullProperty(def,"Info")??catalog.ReadProperty(sharedProperties,"Info"));
            result.Mass=LedgerItemDisplay.Stat(catalog.FullProperty(def,"Mass")??catalog.ReadProperty(sharedProperties,"Mass"));result.Volume=LedgerItemDisplay.Stat(catalog.FullProperty(def,"Volume")??catalog.ReadProperty(sharedProperties,"Volume"));
            try
            {
                string text=(string)format.Invoke(null,new[]{primary,sharedProperties,null,null});
                result.NativeStats=text!=null&&text.Length<=16384?text:null;
            }
            catch(Exception){result.Source+=";optional-native-formatted-stat-list-unavailable";}
            var cache=recipes.GetValue(null) as IDictionary;
            if(cache==null||cache.Count>32768)return result;
            if(!cache.Contains(selected.Id)){result.RecipeState="No recipe in active data";return result;}
            object recipe=cache[selected.Id];
            if(recipe==null||recipe.GetType()!=module.GetType("Assembly-CSharp.NodeProvider")||(int)output.GetValue(recipe)!=selected.Id)return result;
            int outCount=(int)count.GetValue(recipe);var list=inputs.GetValue(recipe) as IList;
            if(outCount<=0||list==null||list.Count>128)return result;
            foreach(object ingredient in list)
            {
                if(ingredient==null||ingredient.GetType()!=module.GetType("Assembly-CSharp.PaneFactory")){result.Ingredients.Clear();return result;}
                object descriptor=item.GetValue(ingredient);
                if(descriptor==null||descriptor.GetType()!=module.GetType("Assembly-CSharp.OptionsSerializer")){result.Ingredients.Clear();return result;}
                int id=(int)itemId.GetValue(descriptor);if(id<=0){result.Ingredients.Clear();return result;}
                int? amount=layout.GetValue(ingredient)==null?(int?)quantity.GetValue(ingredient):null;
                if(amount<=0)amount=null;
                result.Ingredients.Add(new LedgerIngredient{Id=id,Auxiliary=(int)aux.GetValue(descriptor),Quantity=amount,Name=catalog.ItemName(id)});
            }
            result.OutputCount=outCount;result.RecipeState="Active recipe";return result;
        }
    }
}
