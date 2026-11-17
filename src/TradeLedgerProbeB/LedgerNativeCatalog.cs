using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Eleon.Modding;
using UnityEngine;

namespace TradeLedgerProbeB
{
    // Exact-build read-only item-definition and existing icon-atlas access. No markets/entities/branches.
    internal sealed class LedgerNativeCatalog
    {
        private readonly Assembly game;
        private readonly IApplication application;
        private readonly FieldInfo definitions,properties,definitionId,definitionName,world,icons,texture,sprites;
        private readonly MethodInfo hasKey,getString,localize,exists;
        private readonly FieldInfo blockDefinitions,blockProperties,blockGroup,blockName,sharedProperties,definitionGroup;
        private readonly Dictionary<int,object> activeDefinitions=new Dictionary<int,object>();
        private LedgerCatalog activeCatalog;
        private sealed class Roots {internal object Primary,Shared;}
        private sealed class ReferenceKey:IEqualityComparer<object>{public new bool Equals(object a,object b){return ReferenceEquals(a,b);}public int GetHashCode(object value){return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);}}
        private readonly Dictionary<object,Roots> propertyRoots=new Dictionary<object,Roots>(new ReferenceKey());
        internal LedgerNativeCatalog(Assembly assembly,IApplication app)
        {
            game=assembly;application=app;
            LedgerNativeContracts.Require(game,"catalogue");
            var m=game.ManifestModule;
            definitions=LedgerNativeContracts.Field(m,0x04003251);properties=LedgerNativeContracts.Field(m,0x04003256);
            definitionId=LedgerNativeContracts.Field(m,0x04003255);definitionName=LedgerNativeContracts.Field(m,0x04003257);
            hasKey=(MethodInfo)LedgerNativeContracts.Method(m,0x06007806);getString=(MethodInfo)LedgerNativeContracts.Method(m,0x060077f3);
            world=LedgerNativeContracts.Field(m,0x040064e6);icons=LedgerNativeContracts.Field(m,0x04006532);texture=LedgerNativeContracts.Field(m,0x04006367);sprites=LedgerNativeContracts.Field(m,0x04006368);
            Assembly ngui=Assembly.Load("NGUIAssembly");
            if(Safe.Hash(ngui.Location)!=BuildContract.Hashes["NGUIAssembly.dll"])throw new InvalidOperationException("catalog-localization-build-mismatch");
            localize=(MethodInfo)LedgerNativeContracts.Method(ngui.ManifestModule,0x060002ed);
            exists=ngui.GetType("Localization",true).GetMethod("Exists",new[]{typeof(string)});
            try{LedgerNativeContracts.Require(game,"definition-properties");
                definitionGroup=LedgerNativeContracts.Field(m,0x0400325c);sharedProperties=LedgerNativeContracts.Field(m,0x04007121);
                blockDefinitions=LedgerNativeContracts.Field(m,0x04001480);blockProperties=LedgerNativeContracts.Field(m,0x04001488);
                blockGroup=LedgerNativeContracts.Field(m,0x040014e7);blockName=LedgerNativeContracts.Field(m,0x0400149d);
            }catch(Exception){/* Optional metadata degrades independently; no unknown value becomes zero. */}
            if(definitions.FieldType.FullName!="Assembly-CSharp.AddinEditor[]" || properties.FieldType.FullName!="DynamicProperties" ||
                getString.ReturnType!=typeof(string) || hasKey.ReturnType!=typeof(bool) || localize.GetParameters().Length!=2 || localize.GetParameters()[0].ParameterType!=typeof(string) || localize.GetParameters()[1].ParameterType!=typeof(bool) || localize.ReturnType!=typeof(string))throw new InvalidOperationException("catalog-signature-mismatch");
        }
        private string Property(object definition,string name)
        {
            string value=FullProperty(definition,name);return value!=null&&value.Length<=256?value:null;
        }
        internal void PropertyRoots(object definition,out object primary,out object shared)
        {
            Roots cached;if(propertyRoots.TryGetValue(definition,out cached)){primary=cached.Primary;shared=cached.Shared;return;}
            primary=properties.GetValue(definition);object group=definitionGroup==null?null:definitionGroup.GetValue(definition);
            if(blockDefinitions!=null){var blocks=blockDefinitions.GetValue(null) as Array;int id=(int)definitionId.GetValue(definition);
                if(blocks!=null&&blocks.Length<=65536&&id>0&&id<blocks.Length){object block=blocks.GetValue(id);
                    if(block!=null&&LedgerItemDisplay.SameDefinition((string)definitionName.GetValue(definition),(string)blockName.GetValue(block))){primary=blockProperties.GetValue(block);group=blockGroup.GetValue(block);}
                }
            }
            shared=group==null||sharedProperties==null?null:sharedProperties.GetValue(group);
            propertyRoots.Add(definition,new Roots{Primary=primary,Shared=shared});
        }
        internal object Definition(int id){object value;return activeDefinitions.TryGetValue(id,out value)?value:null;}
        internal string ItemInternalName(int id){LedgerCatalogItem item;return activeCatalog!=null&&activeCatalog.Items.TryGetValue(id,out item)?item.InternalName:null;}
        internal string FullProperty(object definition,string name)
        {
            object own,shared;PropertyRoots(definition,out own,out shared);return ReadProperty(own,name)??ReadProperty(shared,name);
        }
        internal string ReadProperty(object p,string name)
        {
            if(p==null||!(bool)hasKey.Invoke(p,new object[]{name}))return null;
            string value=(string)getString.Invoke(p,new object[]{name});return value!=null&&value.Length<=16384?value:null;
        }

        internal string LocalizeRich(string key)
        {
            if(string.IsNullOrEmpty(key))return null;
            if(exists==null||!(bool)exists.Invoke(null,new object[]{key}))return key;
            string text=(string)localize.Invoke(null,new object[]{key,true});return text!=null&&text.Length<=16384?text:null;
        }
        internal string ItemName(int id){LedgerCatalogItem item;return activeCatalog!=null&&activeCatalog.Items.TryGetValue(id,out item)?item.Markup??item.Name:"Unknown item";}
        private string Loca(string key)
        {
            if(string.IsNullOrEmpty(key) || exists==null || !(bool)exists.Invoke(null,new object[]{key}))return key;
            string value=(string)localize.Invoke(null,new object[]{key,true});return string.IsNullOrEmpty(value)||value.Length>256?key:LedgerPresentation.Text(value);
        }
        internal LedgerCatalog Read(bool referenceCatalog=false)
        {
            var result=new LedgerCatalog{Source="active IApplication.GetBlockAndItemMapping;DomainLayout.323A/323F labels+Info;Localization.Get(key,true) active language;not-market-data"};
            result.ContentIdentity=Loca("invTitle");
            activeDefinitions.Clear();propertyRoots.Clear();activeCatalog=result;
            var map=application.GetBlockAndItemMapping();var defs=definitions.GetValue(null) as Array;
            if(map==null || defs==null || map.Count>32768 || defs.Length>65536)throw new InvalidOperationException("catalog-size-or-readiness");
            foreach(var def in defs)
            {
                if(def==null)continue;string name=(string)definitionName.GetValue(def);int id=(int)definitionId.GetValue(def),mapped;
                if(id<=0 || name==null || name.Length>160)continue;
                bool mappedIdentity=map.TryGetValue(name,out mapped)&&mapped==id;
                if(!mappedIdentity&&(!referenceCatalog||id>=defs.Length||!ReferenceEquals(defs.GetValue(id),def)))continue;
                string custom=Property(def,"CustomName");string label=string.IsNullOrEmpty(custom)?name:custom;
                activeDefinitions[id]=def;
                string info=Property(def,"Info");
                result.Add(new LedgerCatalogItem{Id=id,InternalName=name,Name=Loca(label),Markup=LocalizeRich(label),EnglishName=null,Description=LocalizeRich(info),Category=Property(def,"Category")??"Uncategorized",Icon=Property(def,"CustomIcon")??name,IsTerrain=string.Equals(Property(def,"Shape"),"Terrain",StringComparison.OrdinalIgnoreCase),IsCurrency=Property(def,"Credits")!=null});
            }
            return result;
        }
        internal LedgerIcon Icon(string key)
        {
            if(string.IsNullOrEmpty(key)||key.Length>160)return null;
            object w=world.GetValue(null);if(w==null)return null;object manager=icons.GetValue(w);if(manager==null)return null;
            var tex=texture.GetValue(manager) as Texture2D;var list=sprites.GetValue(manager) as IList;
            if(tex==null || list==null || list.Count>65536)return null;
            foreach(var s in list)
            {
                if(s==null)continue;var type=s.GetType();
                if(type.FullName!="UISpriteData")return null;
                if((string)type.GetField("name").GetValue(s)!=key)continue;
                int x=(int)type.GetField("x").GetValue(s),y=(int)type.GetField("y").GetValue(s),width=(int)type.GetField("width").GetValue(s),height=(int)type.GetField("height").GetValue(s);
                if(!LedgerBrowse.ValidSprite(x,y,width,height,tex.width,tex.height))return null;
                return new LedgerIcon{Texture=tex,Uv=new Rect((float)x/tex.width,1f-(float)(y+height)/tex.height,(float)width/tex.width,(float)height/tex.height)};
            }
            return null;
        }
    }
    internal sealed class LedgerIcon { internal Texture2D Texture;internal Rect Uv; }
}
