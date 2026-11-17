using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Eleon.Modding;
using UnityEngine;

namespace TradeLedgerProbeB
{
    internal sealed partial class LedgerInventoryNative
    {
        private sealed class RefKey {internal string Key;}
        private readonly ConditionalWeakTable<object,RefKey> referenceKeys=new ConditionalWeakTable<object,RefKey>();
        private long referenceSequence;
        private string Key(object value){return referenceKeys.GetValue(value,_=>new RefKey{Key=(++referenceSequence).ToString(CultureInfo.InvariantCulture)}).Key;}
        private void BindAccess()
        {
            Bind(0x1c01,"Assembly-CSharp.MemoryHelper","Assembly-CSharp.MethodList");
            Bind(0x204b,"Assembly-CSharp.SolutionLayout","Assembly-CSharp.FormAttribute");
            Bind(0x1a56,"Assembly-CSharp.FormAttribute","Assembly-CSharp.MethodList");
            Bind(0x1a57,"Assembly-CSharp.FormAttribute","Assembly-CSharp.ActionLayout");
            Bind(0x1a59,"Assembly-CSharp.FormAttribute","Assembly-CSharp.BookmarkInvoker");
            Bind(0x1a5a,"Assembly-CSharp.FormAttribute","Assembly-CSharp.PaneFactory[]");
            Bind(0x1a5b,"Assembly-CSharp.FormAttribute","Assembly-CSharp.PaneFactory");
            BindList(0x1a5d,"Assembly-CSharp.FormAttribute","Assembly-CSharp.ActionLayout");
            Bind(0x1a61,"Assembly-CSharp.FormAttribute","System.Boolean");
            Bind(0x1a62,"Assembly-CSharp.FormAttribute","Assembly-CSharp.SolutionLayout");
            Bind(0x1a65,"Assembly-CSharp.FormAttribute","System.Single");
            BindStatic(0xa997,"Eleon.Ngui.DraggedItem","Eleon.Ngui.DraggableItem");
            BindStatic(0xa996,"Eleon.Ngui.DraggedItem","UnityEngine.Transform");
            Bind(0xa984,"Eleon.Ngui.DraggableItem","System.Boolean");
            Bind(0xa97d,"Eleon.Ngui.DraggableItem","UnityEngine.Transform");
            Bind(0xa988,"Eleon.Ngui.DraggableItem","Assembly-CSharp.ComponentContextConnectionToken");
            Bind(0xa97f,"Eleon.Ngui.DraggableItem","UILabel");
            Bind(0xa197,"Assembly-CSharp.ComponentContextConnectionToken","Assembly-CSharp.PaneFactory");
            Bind(0xa991,"Eleon.Ngui.DraggableItemSlot","Eleon.Ngui.ItemGrid");
            Bind(0xa9cd,"Eleon.Ngui.ItemGrid","Assembly-CSharp.PaneFactory[]");
            Bind(0xa9e9,"Eleon.Ngui.ItemGridConstructorInventory","Assembly-CSharp.ActivatorSettings");
            Bind(0xa9ea,"Eleon.Ngui.ItemGridConstructorInventory","Assembly-CSharp.ActivatorSettings+BuilderTable");
            Bind(0xa9fb,"Eleon.Ngui.ItemGridPlayerBelt","Assembly-CSharp.MethodList");
            Bind(0xaa01,"Eleon.Ngui.ItemGridPlayerBelt","Assembly-CSharp.SolutionLayout");
        }
        private void BindStatic(int token,string owner,string type)
        {var f=LedgerNativeContracts.Field(module,0x04000000+token);if(!f.IsStatic||f.DeclaringType.FullName!=owner||f.FieldType.FullName!=type)throw new InvalidOperationException("inventory-static-contract");fields.Add(token,f);}
        private void BindList(int token,string owner,string item)
        {var f=LedgerNativeContracts.Field(module,0x04000000+token);if(f.IsStatic||f.DeclaringType.FullName!=owner||!f.FieldType.IsGenericType||f.FieldType.GetGenericTypeDefinition().FullName!="System.Collections.Generic.List`1"||f.FieldType.GetGenericArguments()[0].FullName!=item)throw new InvalidOperationException("inventory-list-contract");fields.Add(token,f);}
        private object Static(int token){return fields[token].GetValue(null);}
        private static LedgerInventoryContext Missing(string name,string reason,LedgerInventoryAvailability state=LedgerInventoryAvailability.Unavailable)
        {return new LedgerInventoryContext(name,state,"5188 passive current local inventory",reason);}
        private LedgerInventoryContext Part(string name,Array slots,int physicalSize,int exported,bool wrapped,string identity)
        {
            try{
                if(slots==null||slots.Length!=physicalSize)throw new InvalidOperationException("slot array is not ready");
                var values=new List<ItemStack>();var keys=new List<string>();
                for(int i=0;i<exported;i++){
                    object slot=wrapped?F(0x2fd3,slots.GetValue(i)):slots.GetValue(i);
                    int count=ReadCount(slot);if(count==0)continue;
                    object item=F(0x2fdf,slot);var stack=new ItemStack{id=(int)F(0x2fe9,item),count=count,slotIdx=(byte)i,ammo=(int)F(0x2fec,item),decay=(int)F(0x2fea,item)};
                    if(stack.id<=0||stack.ammo<0||stack.decay<0)throw new InvalidOperationException("item identity unavailable");
                    values.Add(stack);keys.Add(Key(slot));
                }
                return new LedgerInventoryContext(name,LedgerInventoryAvailability.Available,"5188 backing fields; first "+exported+" slots","",values,keys,identity);
            }catch(Exception e){return Missing(name,e is InvalidOperationException?e.Message:"inventory read unavailable");}
        }
        private string Selector(object local,object link)
        {
            object active=F(0x1c01,local),personal=F(0x1c02,local),buffer=F(0x1a56,link),structure=F(0x1a57,link),domain=F(0x1a59,link);
            return Key(active)+"/"+Key(personal)+"/"+Key(buffer)+"/"+(structure==null?"-":Key(structure))+"/"+(domain==null?"-":Key(domain))+"/"+F(0x1a61,link)+"/"+F(0x1a65,link);
        }
        internal void Access(IPlayer player,ref LedgerInventoryContext backpack,out LedgerInventoryContext active,out LedgerInventoryContext cursor,out LedgerInventoryContext linked)
        {
            active=Missing("active toolbar","active source unavailable");cursor=Missing("cursor","cursor state unavailable");linked=Missing("linked toolbar","link state unavailable");
            object local=F(0xa810,player);if(local.GetType().FullName!="Assembly-CSharp.SolutionLayout")return;
            object personal=F(0x2045,local),domain=F(0x5288,personal),link=F(0x204b,local);
            if(!ReferenceEquals(local,F(0x1a62,link)))return;
            string selector=Selector(local,link);object held=Static(0xa997);
            Array bagSlots=F(0x52bb,domain) as Array;
            backpack=Part("backpack",bagSlots,40,40,false,"backpack:"+Key(domain));
            object current=F(0x1c01,local),baseToolbar=F(0x1c02,local),buffer=F(0x1a56,link),structure=F(0x1a57,link);
            bool enabled=(bool)F(0x1a61,link)&&structure!=null;
            bool linkedAdmitted=false;
            if(ReferenceEquals(current,baseToolbar)&&!enabled){
                active=Part("active toolbar",F(0x2fad,current) as Array,30,9,true,"personal-toolbar:"+Key(current));
                linked=new LedgerInventoryContext("linked toolbar",LedgerInventoryAvailability.Available,"5188 inactive link selector","unlinked",new List<ItemStack>(),identity:"unlinked");
            }else if(enabled&&ReferenceEquals(current,buffer)&&F(0x1a59,link)!=null){
                var allowed=F(0x1a5d,link) as IList;bool inRange=false;
                if(allowed!=null&&allowed.Count<=512)foreach(object entry in allowed)if(ReferenceEquals(entry,structure)){inRange=true;break;}
                if(inRange&&(float)F(0x1a65,link)<0&&F(0x1a5a,link)==null&&F(0x1a5b,link)==null){
                    active=Part("active toolbar",F(0x2fad,current) as Array,30,9,true,"linked-toolbar:"+Key(current)+":"+Key(F(0x1a59,link)));
                    linked=active;linkedAdmitted=active.Availability==LedgerInventoryAvailability.Available;
                }else active=linked=Missing("linked toolbar","link is changing, pending or out of range");
            }else active=linked=Missing("active toolbar","active source/link is not admitted");
            cursor=Cursor(held,local,personal,bagSlots,current,ReferenceEquals(current,baseToolbar)||linkedAdmitted);
            if(!ReferenceEquals(link,F(0x204b,local))||!ReferenceEquals(local,F(0x1a62,link))||!ReferenceEquals(held,Static(0xa997))||selector!=Selector(local,link)||!ReferenceEquals(personal,F(0x2045,local))||!ReferenceEquals(domain,F(0x5288,personal))||!ReferenceEquals(bagSlots,F(0x52bb,domain)))
                backpack=active=cursor=linked=Missing("inventory","inventory source changed during read");
        }
        private LedgerInventoryContext Cursor(object held,object local,object personal,Array bagSlots,object current,bool toolbarAdmitted)
        {
            if(held==null)return new LedgerInventoryContext("cursor",LedgerInventoryAvailability.Available,"5188 DraggedItem current pointer","",new List<ItemStack>(),identity:"cursor-empty");
            try{
                var component=held as Component;var dragRoot=Static(0xa996) as Transform;
                if(component==null||dragRoot==null||!component.gameObject.activeInHierarchy||component.transform.parent!=dragRoot||!(bool)F(0xa984,held))return Missing("cursor","cursor transfer is unsettled");
                var parent=F(0xa97d,held) as Transform;
                if(parent==null||!parent.gameObject.activeInHierarchy)return Missing("cursor","cursor origin unavailable");
                var origin=parent.GetComponent(module.Assembly.GetType("Eleon.Ngui.DraggableItemSlot",true));
                object grid=F(0xa991,origin);bool admitted=false;
                if(grid!=null&&grid.GetType().FullName=="Eleon.Ngui.ItemGridConstructorInventory")admitted=ReferenceEquals(F(0xa9e9,grid),personal)&&Convert.ToInt32(F(0xa9ea,grid))==1&&ReferenceEquals(F(0xa9cd,grid),bagSlots);
                else if(grid!=null&&(grid.GetType().FullName=="Eleon.Ngui.ItemGridPlayerBelt"||grid.GetType().FullName=="Eleon.Ngui.ItemGridPlayerWindowBelt"))admitted=toolbarAdmitted&&ReferenceEquals(F(0xaa01,grid),local)&&ReferenceEquals(F(0xa9fb,grid),current);
                if(!admitted)return Missing("cursor","cursor source is not an admitted personal/active toolbar");
                var label=F(0xa97f,held) as UILabel;int count;
                if(label==null||!label.enabled||!label.gameObject.activeInHierarchy||label.finalAlpha<.99f||!int.TryParse(label.text,NumberStyles.None,CultureInfo.InvariantCulture,out count)||count<=0)return Missing("cursor","shown cursor quantity unavailable");
                object invoker=F(0xa197,F(0xa988,held)),item=F(0x2fdf,invoker);int id=(int)F(0x2fe9,item);
                if(id<=0||label.text!=count.ToString(CultureInfo.InvariantCulture))return Missing("cursor","cursor identity/count changed");
                return new LedgerInventoryContext("cursor",LedgerInventoryAvailability.Available,"5188 current local dragged-item published quantity label","",new[]{new ItemStack{id=id,count=count,ammo=(int)F(0x2fec,item),decay=(int)F(0x2fea,item)}},new[]{Key(invoker)},"cursor:"+Key(held));
            }catch(Exception){return Missing("cursor","cursor source/count unavailable");}
        }
    }
}
