using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TradeLedgerProbeB
{
    internal sealed class LedgerNativeUiArt
    {
        private readonly Dictionary<string,LedgerIcon> icons=new Dictionary<string,LedgerIcon>();
        internal LedgerNativeUiArt()
        {
            // Only existing UI atlas metadata/textures; no window/entity/market inventories.
            var ngui=Assembly.Load("NGUIAssembly");
            if(Safe.Hash(ngui.Location)!=BuildContract.Hashes["NGUIAssembly.dll"])throw new InvalidOperationException("UI-art-build-mismatch");
            var type=ngui.GetType("UIAtlas",true);
            var atlases=Resources.FindObjectsOfTypeAll(type);if(atlases.Length>128)throw new InvalidOperationException("UI-atlas-bound");
            var names=new HashSet<string>{"IconWaypoint","IconPlanet","IconMap","IconSolarSystem","ArrowNavigationLeft","ArrowNavigationRight","IconReplace","IconWindowClose","IconInput"};
            foreach(var atlas in atlases)
            {
                var texture=type.GetProperty("texture").GetValue(atlas,null) as Texture2D;
                var list=type.GetProperty("spriteList").GetValue(atlas,null) as IList;
                if(texture==null||list==null||list.Count>16384)continue;
                foreach(var sprite in list)
                {
                    if(sprite==null||sprite.GetType().FullName!="UISpriteData")continue;var t=sprite.GetType();
                    string name=(string)t.GetField("name").GetValue(sprite);if(!names.Contains(name)||icons.ContainsKey(name))continue;
                    int x=(int)t.GetField("x").GetValue(sprite),y=(int)t.GetField("y").GetValue(sprite),w=(int)t.GetField("width").GetValue(sprite),h=(int)t.GetField("height").GetValue(sprite);
                    if(!LedgerBrowse.ValidSprite(x,y,w,h,texture.width,texture.height))continue;
                    icons[name]=new LedgerIcon{Texture=texture,Uv=new Rect((float)x/texture.width,1f-(float)(y+h)/texture.height,(float)w/texture.width,(float)h/texture.height)};
                }
            }
        }
        internal LedgerIcon Get(string name){LedgerIcon value;return icons.TryGetValue(name,out value)?value:null;}
    }
}
