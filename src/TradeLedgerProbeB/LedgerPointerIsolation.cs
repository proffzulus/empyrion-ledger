using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TradeLedgerProbeB
{
    public interface ILedgerPointerCamera { bool Mouse {get;set;} bool Touch {get;set;} }
    public interface ILedgerPointerBackend
    { IEnumerable<ILedgerPointerCamera> Cameras {get;} bool Tooltips {get;set;} void ClearHover(); }
    // Reversible UI-only lease. Keyboard/controller and the native modal cursor lease are untouched.
    public sealed class LedgerPointerIsolation
    {
        private sealed class Saved { internal ILedgerPointerCamera Camera; internal bool Mouse,Touch; }
        private readonly ILedgerPointerBackend backend;
        private readonly List<Saved> saved=new List<Saved>();
        private bool active,tooltips;
        public LedgerPointerIsolation(ILedgerPointerBackend value){backend=value;}
        public bool Active {get{return active;}}
        public int CapturedCameras {get{return saved.Count;}}
        public void Sync(bool ownedTop,bool waitForRelease,bool pointerIdle)
        {
            if(!ownedTop){if(active&&(!waitForRelease||pointerIdle))Release();return;}
            if(!active){tooltips=backend.Tooltips;active=true;backend.ClearHover();}
            backend.Tooltips=false;
            foreach(var camera in backend.Cameras)
            {
                if(saved.Find(s=>ReferenceEquals(s.Camera,camera))==null)
                {if(saved.Count>=32)throw new InvalidOperationException("pointer-camera-bound");saved.Add(new Saved{Camera=camera,Mouse=camera.Mouse,Touch=camera.Touch});}
                camera.Mouse=false;camera.Touch=false;
            }
        }
        public void Release()
        {
            if(!active)return;
            Exception failure=null;
            foreach(var s in saved)
            {try{s.Camera.Mouse=s.Mouse;}catch(Exception e){failure=e;}try{s.Camera.Touch=s.Touch;}catch(Exception e){failure=e;}}
            try{backend.Tooltips=tooltips;}catch(Exception e){failure=e;}
            saved.Clear();active=false;
            if(failure!=null)throw new InvalidOperationException("pointer-restore-failure",failure);
        }
    }
    internal sealed class LedgerNativePointerBackend : ILedgerPointerBackend
    {
        private readonly FieldInfo list,mouse,touch,tooltips;
        private readonly MethodInfo clear,hide;
        private readonly Dictionary<object,CameraPort> ports=new Dictionary<object,CameraPort>();
        private sealed class CameraPort : ILedgerPointerCamera
        {
            internal object Target;internal FieldInfo M,T;
            private bool Alive {get{return (Target as UnityEngine.Object)!=null;}}
            public bool Mouse {get{return Alive&&(bool)M.GetValue(Target);}set{if(Alive)M.SetValue(Target,value);}}
            public bool Touch {get{return Alive&&(bool)T.GetValue(Target);}set{if(Alive)T.SetValue(Target,value);}}
        }
        internal LedgerNativePointerBackend()
        {
            var a=Assembly.Load("NGUIAssembly");if(Safe.Hash(a.Location)!=BuildContract.Hashes["NGUIAssembly.dll"])throw new InvalidOperationException("pointer-build-mismatch");
            var m=a.ManifestModule;list=LedgerNativeContracts.Field(m,0x040003f5);mouse=LedgerNativeContracts.Field(m,0x04000408);touch=LedgerNativeContracts.Field(m,0x04000409);tooltips=LedgerNativeContracts.Field(m,0x04000421);
            clear=(MethodInfo)LedgerNativeContracts.Method(m,0x0600066d);hide=(MethodInfo)LedgerNativeContracts.Method(m,0x06000697);
            if(!list.IsStatic||!tooltips.IsStatic||mouse.IsStatic||touch.IsStatic||mouse.FieldType!=typeof(bool)||touch.FieldType!=typeof(bool)||tooltips.FieldType!=typeof(bool)||
                mouse.DeclaringType.FullName!="UICamera"||!clear.IsStatic||!hide.IsStatic||clear.ReturnType!=typeof(void)||hide.ReturnType!=typeof(bool)||
                clear.GetParameters().Length!=1||hide.GetParameters().Length!=1||clear.GetParameters()[0].ParameterType!=typeof(GameObject)||hide.GetParameters()[0].ParameterType!=typeof(GameObject))throw new InvalidOperationException("pointer-signature-mismatch");
        }
        public bool Tooltips {get{return (bool)tooltips.GetValue(null);}set{tooltips.SetValue(null,value);}}
        public void ClearHover(){ports.Clear();clear.Invoke(null,new object[]{null});hide.Invoke(null,new object[]{null});}
        public IEnumerable<ILedgerPointerCamera> Cameras
        {
            get
            {
                object value=list.GetValue(null);if(value==null)yield break;
                var type=value.GetType();var buffer=type.GetField("buffer");var size=type.GetField("size");
                if(buffer==null||size==null||size.FieldType!=typeof(int)||buffer.FieldType.GetElementType()!=mouse.DeclaringType)throw new InvalidOperationException("pointer-list-signature");
                int count=(int)size.GetValue(value);var array=buffer.GetValue(value) as Array;
                if(count<0||count>32||count>0&&(array==null||array.Length<count))throw new InvalidOperationException("pointer-list-bound");
                for(int i=0;i<count;i++)
                {
                    object target=array.GetValue(i);if((target as UnityEngine.Object)==null)continue;
                    CameraPort port;if(!ports.TryGetValue(target,out port)){if(ports.Count>=64)throw new InvalidOperationException("pointer-port-bound");port=new CameraPort{Target=target,M=mouse,T=touch};ports.Add(target,port);}
                    yield return port;
                }
            }
        }
    }
}
