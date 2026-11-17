using System;using System.IO;using System.Linq;using System.Reflection;
namespace TradeLedgerProbeB {
 // Installation identity only. Save authority continues to come from the local game's
 // guarded current-save context, never from this folder or the build machine.
 public sealed class LedgerInstallPaths {
  public readonly string Root,Settings,Adapter,Logs,LegacyRecovery,GameRoot;public readonly bool PublicRelease;
  public LedgerInstallPaths(string loader){
   Root=Path.GetDirectoryName(Path.GetFullPath(loader));string name=Path.GetFileNameWithoutExtension(loader);
   if(name!="TradeLedger"&&name!="TradeLedgerProbeB")throw new InvalidDataException("unsupported-ledger-loader-name");
   PublicRelease=name=="TradeLedger";Settings=Path.Combine(Root,name+".settings");Adapter=Path.Combine(Root,"runtime",name+".Runtime.dll");
   Logs=Path.Combine(Root,"logs");LegacyRecovery=Path.Combine(Root,"legacy-recovery");
   var mods=Directory.GetParent(Root);var content=mods==null?null:mods.Parent;
   if(mods!=null&&content!=null&&mods.Name.Equals("Mods",StringComparison.OrdinalIgnoreCase)&&content.Name.Equals("Content",StringComparison.OrdinalIgnoreCase)&&content.Parent!=null)GameRoot=content.Parent.FullName;
  }
  static readonly Lazy<LedgerInstallPaths> current=new Lazy<LedgerInstallPaths>(()=>new LedgerInstallPaths(typeof(Probe).Assembly.Location));
  public static LedgerInstallPaths Current {get{return current.Value;}}
  internal bool LegacyPackagePresent {get{return PublicRelease&&GameRoot!=null&&File.Exists(Path.Combine(GameRoot,"Content","Mods","TradeLedgerProbeB","TradeLedgerProbeB.dll"));}}
 }
 // AppDomain-wide identity survives separately loaded copies of the same assembly. No
 // native hook or world scan. The lease is held only by the normal game-loader instance.
 internal sealed class LedgerLoaderLease:IDisposable {
  const string Key="TradeLedger.active-loader.v1";readonly object token=new object();
  internal static IDisposable Acquire(){lock(string.Intern(Key)){if(AppDomain.CurrentDomain.GetData(Key)!=null)return null;var lease=new LedgerLoaderLease();AppDomain.CurrentDomain.SetData(Key,lease.token);return lease;}}
  public void Dispose(){lock(string.Intern(Key)){if(ReferenceEquals(AppDomain.CurrentDomain.GetData(Key),token))AppDomain.CurrentDomain.SetData(Key,null);}}
  internal static bool LegacyLoaded(){return AppDomain.CurrentDomain.GetAssemblies().Any(a=>a!=typeof(Probe).Assembly&&a.GetName().Name=="TradeLedgerProbeB");}
 }
}
