using System;
using System.IO;
using System.Linq;
namespace TradeLedgerProbeB
{
    public sealed class LedgerSaveAlias { public string SavePath,CanonicalPath; public LedgerSaveGuard[] Guards; }
    public sealed class LedgerSaveGuard { public string RelativePath,Sha256; }
    public sealed class LedgerSaveAliases
    {
        private string cachedPath,cachedWorld,cachedState;
        public static string Notice(LedgerContext context,bool hasData)
        {
            if(context!=null&&context.ScopeState!=null&&context.ScopeState.StartsWith("portable-save-authority;",StringComparison.Ordinal))return null;
            if(context!=null&&context.ScopeState=="save-alias-invalid;isolated-path-scope")return "Save link could not be verified. Ledger access is paused; check the registered copy before continuing.";
            if(context!=null&&context.Recordable&&!hasData&&context.ScopeState!="explicit-verified-save-alias;memory-not-rollback-snapshot")return "No Ledger records for this save. Loaded a backup? Register this copy with register-ledger-save-copy.ps1 after closing the game. See docs/ledger-save-recovery-20261026.md.";
            return null;
        }
        public void Apply(LedgerContext context,string root)
        {
            if(context==null||!context.Recordable){cachedPath=cachedWorld=cachedState=null;return;}
            if(cachedPath!=context.SavePath)
            {
                cachedPath=context.SavePath;cachedWorld=null;cachedState=null;
                try
                {
                    string file=Path.Combine(root,"SaveAliases.json");
                    if(!File.Exists(file))return;
                    if(new FileInfo(file).Length>65536)throw new InvalidDataException();
                    var aliases=LedgerJson.Decode<LedgerSaveAlias[]>(File.ReadAllBytes(file));
                    var matches=aliases.Where(a=>string.Equals(Path.GetFullPath(a.SavePath).TrimEnd('\\','/'),context.SavePath,StringComparison.OrdinalIgnoreCase)).ToArray();
                    if(matches.Length==0)return;
                    if(matches.Length!=1)throw new InvalidDataException();
                    var alias=matches[0];
                    if(alias.Guards==null||alias.Guards.Length<2||alias.Guards.Length>8||!Path.IsPathRooted(alias.CanonicalPath))throw new InvalidDataException();
                    foreach(var guard in alias.Guards)
                    {
                        if(Path.IsPathRooted(guard.RelativePath))throw new InvalidDataException();
                        string path=Path.GetFullPath(Path.Combine(context.SavePath,guard.RelativePath));
                        if(!path.StartsWith(context.SavePath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(path)||new FileInfo(path).Length>2097152||LedgerJson.Hash(File.ReadAllBytes(path))!=guard.Sha256)throw new InvalidDataException();
                    }
                    var original=LedgerContext.Create(alias.CanonicalPath,context.Playfield,context.SolarSystem,context.Mode);
                    if(!original.Recordable)throw new InvalidDataException();
                    cachedWorld=original.WorldKey;cachedState="explicit-verified-save-alias;memory-not-rollback-snapshot";
                }
                catch(Exception){cachedState="save-alias-invalid;isolated-path-scope";}
            }
            if(cachedWorld!=null)context.WorldKey=cachedWorld;
            if(cachedState!=null)context.ScopeState=cachedState;
            if(cachedState=="save-alias-invalid;isolated-path-scope"){context.Recordable=false;context.WorldKey=LedgerJson.Hash("rejected-save-alias|"+context.SavePath);}
        }
    }
}
