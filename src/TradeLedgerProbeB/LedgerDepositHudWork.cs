using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TradeLedgerProbeB
{
    // Memo of already admitted UI information, NOT permission to read a native object.
    // Callers must check current publication/visibility before consulting it and revalidate
    // the whole batch before remembering anything. No timestamps or persisted facts here.
    public struct LedgerDepositHudSample
    {
        public object Marker,View,Label,Resource,Position,Definition;
        public int Id,SizeBand;
        public string Body;
        public bool Same(LedgerDepositHudSample other)
        {
            return ReferenceEquals(Marker,other.Marker)&&ReferenceEquals(View,other.View)&&ReferenceEquals(Label,other.Label)&&
                ReferenceEquals(Resource,other.Resource)&&Id==other.Id&&SizeBand==other.SizeBand&&
                Equals(Position,other.Position)&&Equals(Definition,other.Definition)&&Body==other.Body;
        }
    }
    public sealed class LedgerDepositHudWork
    {
        public const int Batch=64,Limit=1024;
        private sealed class References:IEqualityComparer<object>
        {public new bool Equals(object a,object b){return ReferenceEquals(a,b);}public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);}}
        private readonly Dictionary<object,LedgerDepositHudSample> admitted=new Dictionary<object,LedgerDepositHudSample>(new References());
        private string scope;
        private object world,actor,controller,root,views,published;
        private ulong tick;
        private int offset;
        public int Remembered {get{return admitted.Count;}}
        public void Reset()
        {admitted.Clear();scope=null;world=actor=controller=root=views=published=null;tick=0;offset=0;}
        public void Bind(string nextScope,object nextWorld,object nextActor,object nextController,object nextRoot,object nextViews,object nextPublished,ulong nextTick)
        {
            bool newOwner=scope!=nextScope||!ReferenceEquals(world,nextWorld)||!ReferenceEquals(actor,nextActor)||!ReferenceEquals(controller,nextController)||!ReferenceEquals(root,nextRoot)||nextTick<tick;
            if(newOwner||!ReferenceEquals(views,nextViews)||!ReferenceEquals(published,nextPublished))admitted.Clear();
            if(newOwner)offset=0; // Retain fair progress when native publication replaces its list.
            scope=nextScope;world=nextWorld;actor=nextActor;controller=nextController;root=nextRoot;views=nextViews;published=nextPublished;tick=nextTick;
        }
        public int Take(int count,out int start)
        {
            start=0;if(count<=0||count>Limit)return 0;
            start=offset%count;int take=Math.Min(Batch,count);offset=(start+take)%count;return take;
        }
        public bool Unchanged(LedgerDepositHudSample sample)
        {LedgerDepositHudSample old;return sample.Marker!=null&&admitted.TryGetValue(sample.Marker,out old)&&old.Same(sample);}
        public void Forget(object marker){if(marker!=null)admitted.Remove(marker);}
        public void Remember(LedgerDepositHudSample sample)
        {
            if(sample.Marker==null)return;
            if(admitted.Count>=Limit&&!admitted.ContainsKey(sample.Marker))admitted.Clear();
            admitted[sample.Marker]=sample;
        }
    }
}
