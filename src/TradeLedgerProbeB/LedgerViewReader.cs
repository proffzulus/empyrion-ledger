using System;using System.IO;using System.Collections.Generic;
namespace TradeLedgerProbeB {
 // One owned consumer, one current scope, one current snapshot. Never a game/save reader lease.
 public sealed class LedgerViewReader {
  readonly object gate=new object();readonly LedgerRequestMemo requests=new LedgerRequestMemo();
  string path,world;int? player;LedgerCatalog catalog;object stamp;LedgerViewData value;long reads,reused;int epoch,loadedEpoch;
  public void Invalidate(){System.Threading.Interlocked.Increment(ref epoch);}
  public LedgerViewData Read(string file,string scope,LedgerCatalog items,int? owner,bool force=false){lock(gate){
   string full=Path.GetFullPath(file);if(path!=full||world!=scope||player!=owner||!ReferenceEquals(catalog,items)){path=full;world=scope;player=owner;catalog=items;stamp=null;value=null;requests.Clear();}
   using(LedgerPrivateMirror.Acquire(full)){
    object current=LedgerPrivateMirror.Version(full);
    int ticket=System.Threading.Volatile.Read(ref epoch);
    if(!force&&ticket==loadedEpoch&&current!=null&&ReferenceEquals(stamp,current)&&value!=null){reused++;return value;}
    // An untracked file always reads normally. Only the exclusive owned publisher certifies reuse.
    var next=LedgerViewData.ReadPrepared(full,scope,items,owner,requests);reads++;value=next;stamp=next.LoadedUtc==null||next.RetryableProjection||next.ObjectReadError!=null||next.ProductionReadError!=null||next.ManualReadError!=null||next.TrackingReadError!=null||next.ArchiveReadError!=null?null:current;loadedEpoch=ticket;return next;
   }
  }}
  public object Statistics(){lock(gate)return new{snapshotReads=reads,reusedReads=reused,recognitionParses=requests.Parses,recognitionHits=requests.Hits,recognitionEntries=requests.Count};}
 }
 // Pure shown-text recognition, scoped to the reader/catalogue. It never caches completion,
 // instance identity, timestamps, stock or an observation. Only current snapshot keys survive.
 public sealed class LedgerRequestMemo {
  struct Key:IEquatable<Key>{public string Kind,Body;public bool? Revealed;public bool Equals(Key k){return Kind==k.Kind&&Body==k.Body&&Revealed==k.Revealed;}public override bool Equals(object o){return o is Key&&Equals((Key)o);}public override int GetHashCode(){return (Kind??"").GetHashCode()^(Body??"").GetHashCode()^(Revealed.HasValue?(Revealed.Value?1:2):0);}}
  Dictionary<Key,LedgerRequestFact> facts=new Dictionary<Key,LedgerRequestFact>();HashSet<Key> used;
  LedgerCatalog catalog;public long Parses {get;private set;}public long Hits {get;private set;}public bool Incomplete {get;private set;}public int Count {get{return facts.Count;}}
  public void Clear(){facts.Clear();catalog=null;used=null;}
  public void Begin(){used=new HashSet<Key>();Incomplete=false;}
  public void End(){if(used==null)return;var retained=new Dictionary<Key,LedgerRequestFact>();foreach(var k in used){LedgerRequestFact f;if(facts.TryGetValue(k,out f))retained.Add(k,f);}facts=retained;used=null;}
  public LedgerRequestFact Recognize(LedgerObservation o,LedgerCatalog items){if(!ReferenceEquals(items,catalog)){facts.Clear();catalog=items;}
   if(o==null||o.Dialogue==null)return null;var k=new Key{Kind=o.Kind,Body=o.Dialogue.BodyVerbatim,Revealed=o.Dialogue.BodyFullyRevealed};if(used!=null)used.Add(k);
   LedgerRequestFact f;if(facts.TryGetValue(k,out f))Hits++;else{Parses++;bool complete;f=LedgerRequestRecognition.RecognizeMeasured(o,items,out complete);if(complete)facts.Add(k,f);else Incomplete=true;}return Copy(f);
  }
  static LedgerRequestFact Copy(LedgerRequestFact f){if(f==null)return null;var n=new LedgerRequestFact{Rule=f.Rule,CatalogSource=f.CatalogSource,RewardText=f.RewardText,Relation=f.Relation,RewardMode=f.RewardMode,Type=f.Type,DefinitionSource=f.DefinitionSource,Conflict=f.Conflict,RewardCredits=f.RewardCredits,Truncated=f.Truncated};foreach(var i in f.Items)n.Items.Add(new LedgerRequestedItem{ItemId=i.ItemId,Quantity=i.Quantity,Name=i.Name,Proof=i.Proof});foreach(var a in f.Alternatives)n.Alternatives.Add(new LedgerRequestAlternative{Inputs=a.Inputs==null?null:(int[])a.Inputs.Clone()});return n;}
 }
}
