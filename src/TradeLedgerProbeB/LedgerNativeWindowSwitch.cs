using System;
using System.Collections;
using System.IO;
using System.Reflection;

namespace TradeLedgerProbeB
{
    // Explicit user UI action only, after the owned modal has retired. Reuses the actual
    // registered OptionsInfo action/callback, including native CanExecute, not guessed window keys.
    internal sealed class LedgerNativeWindowSwitch
    {
        private readonly MethodInfo get,click,enabled;private readonly FieldInfo actions,control;
        internal LedgerNativeWindowSwitch(Assembly game)
        {
            LedgerNativeContracts.Require(game,"native-windows");
            var m=game.ManifestModule;get=(MethodInfo)LedgerNativeContracts.Method(m,0x0600bc76);click=(MethodInfo)LedgerNativeContracts.Method(m,0x06007157);enabled=(MethodInfo)LedgerNativeContracts.Method(m,0x06007167);actions=LedgerNativeContracts.Field(m,0x0400aa90);control=LedgerNativeContracts.Field(m,0x0400697b);
            if(!get.IsStatic||get.ReturnType.FullName!="Eleon.Ngui.UIActionGlobalManager"||click.IsStatic||click.ReturnType!=typeof(void)||click.GetParameters().Length!=0||control.FieldType.FullName!="Eleon.Control"||enabled.ReturnType!=typeof(bool)||enabled.GetParameters().Length!=0||!actions.FieldType.IsGenericType||actions.FieldType.GetGenericTypeDefinition()!=typeof(System.Collections.Generic.List<>)||actions.FieldType.GetGenericArguments()[0].FullName!="Assembly-CSharp.PageSetConnectionToken")throw new InvalidDataException("Window switch signature mismatch");
        }
        internal bool Open(int selected)
        {
            if(LedgerWindowSwitch.KeyForControl(selected)==0)return false;
            object manager=get.Invoke(null,null);var list=manager==null?null:actions.GetValue(manager) as IList;
            if(list==null||list.Count>256)return false;object found=null;
            foreach(object action in list){if(action==null||action.GetType()!=control.DeclaringType||Convert.ToInt32(control.GetValue(action))!=selected)continue;if(found!=null)return false;found=action;}
            if(found==null||!(bool)enabled.Invoke(found,null))return false;click.Invoke(found,null);return true;
        }
    }
}
