using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TradeLedgerProbeB
{
    // No Harmony or runtime-adapter type reference: native diagnostics survive their absence/failure.
    internal static class RuntimeBootstrap
    {
        internal static string OptInReason = "not-read";
        internal static bool OptedIn()
        {
            try
            {
                string path = LedgerInstallPaths.Current.Settings;
                if (!File.Exists(path)) { OptInReason = "settings-missing"; return false; }
                if (new FileInfo(path).Length > 1024) { OptInReason = "settings-too-large"; return false; }
                var settings = LedgerDiagnosticSettings.Parse(File.ReadAllLines(path));
                bool enabled = settings.Valid && settings.Observation;
                OptInReason = enabled ? "explicit-on" : "off-or-invalid-settings";
                return enabled;
            }
            catch (Exception error) { OptInReason = "settings-error:" + error.GetType().Name; return false; }
        }

        internal static IRuntimeObserver Start(IProbeHost host)
        {
            string stage = "find-loaded-game-assembly";
            try
            {
                Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
                Assembly game = loaded.SingleOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (game == null) throw new InvalidOperationException("game-assembly-not-loaded");
                string managed = Path.GetDirectoryName(game.Location);
                stage = "native-feature-contract";
                // Build labels are evidence, not a universal permission gate.
                LedgerNativeContracts.Require(game,"runtime-core");
                stage = "verify-game-fingerprints";
                bool verified = ObservationRules.FingerprintsMatch(BuildContract.Hashes.Where(p=>p.Key!="Assembly-CSharp.dll").ToDictionary(p=>p.Key,p=>p.Value), name =>
                {
                    Assembly actual = loaded.SingleOrDefault(a => a.GetName().Name == Path.GetFileNameWithoutExtension(name));
                    string path = actual == null ? Path.Combine(managed, name) : actual.Location;
                    string hash = Safe.Hash(path);
                    host.Trace(TraceLevel.Debug, "GuardFile", "file=" + Safe.Text(name) + " loaded=" + (actual != null) + " sha256=" + hash);
                    return hash;
                });
                if (!verified) { host.Trace(TraceLevel.Error, "HookSkipped", "reason=strict-fingerprint-mismatch nativeDiagnosticsContinue=true"); return null; }
                stage = "verify-harmony-binary-and-conflicts";
                string folder = Path.GetDirectoryName(typeof(Probe).Assembly.Location);
                string harmonyPath = Path.Combine(folder, "runtime", "0Harmony.dll");
                if (Safe.Hash(harmonyPath) != BuildContract.HarmonyHash) throw new InvalidDataException("harmony-hash");
                Assembly existing = loaded.SingleOrDefault(a => a.GetName().Name == "0Harmony");
                if (existing != null && Safe.Hash(existing.Location) != BuildContract.HarmonyHash)
                    throw new InvalidDataException("conflicting-harmony");
                if (existing == null) Assembly.LoadFrom(harmonyPath);
                stage = "verify-and-load-adapter";
                string adapterPath = LedgerInstallPaths.Current.Adapter;
                if (Safe.Hash(adapterPath) != ProbeBuild.AdapterHash) throw new InvalidDataException("adapter-hash");
                Assembly adapter = Assembly.LoadFrom(adapterPath);
                var observer = (IRuntimeObserver)Activator.CreateInstance(adapter.GetType("TradeLedgerProbeB.Runtime.RuntimeObserver", true));
                stage = "validate-targets-and-install-postfixes";
                StartObserver(observer, host, game);
                return observer;
            }
            catch (Exception error)
            {
                ExceptionTrace.Write(host, "bootstrap", error, "stage=" + stage);
                host.Trace(TraceLevel.Error, "HookSkipped", "reason=bootstrap-failure stage=" + stage + " type=" + error.GetType().Name + " optIn=" + OptInReason + " nativeDiagnosticsContinue=true");
                return null;
            }
        }
        internal static void StartObserver(IRuntimeObserver observer, IProbeHost host, Assembly game)
        {
            try { observer.Start(host, game); }
            catch (Exception error)
            {
                // Record the original before cleanup, which may independently fail.
                ExceptionTrace.Write(host, "observer-start", error, "stage=validate-targets-and-install-postfixes");
                try { observer.Dispose(); }
                catch (Exception cleanup) { ExceptionTrace.Write(host, "observer-start-cleanup", cleanup, "originalPreserved=true"); }
                throw;
            }
        }
    }
}
