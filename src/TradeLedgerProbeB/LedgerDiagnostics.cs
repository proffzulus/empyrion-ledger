using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TradeLedgerProbe;

namespace TradeLedgerProbeB
{
    internal interface IDiagnosticLog : IDisposable
    {
        string Path { get; }
        string Status { get; }
        string Counters { get; }
        bool Write(string kind, string ticks, string detail);
    }

    internal sealed class LedgerDiagnosticSettings
    {
        internal bool Observation;
        internal string Level = "Warn";
        internal bool Valid;
        internal bool DiagnosticsValid = true;
        internal static LedgerDiagnosticSettings Parse(string[] lines)
        {
            var result = new LedgerDiagnosticSettings(); var keys = new HashSet<string>();
            foreach (string source in lines)
            {
                string line = source.Trim(); if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int split = line.IndexOf('='); if (split < 1) return result;
                string key = line.Substring(0, split), value = line.Substring(split + 1);
                bool unique=keys.Add(key);
                if(key=="Diagnostics")
                {
                    if(!unique||!result.DiagnosticsValid||value!="Off"&&value!="Warn"&&value!="Info"&&value!="Debug")
                    {result.DiagnosticsValid=false;result.Level="Warn";}
                    else result.Level=value;
                    continue; // Invalid diagnostics never revoke an otherwise explicit observation opt-in.
                }
                if(!unique)return result;
                if (key == "RuntimeObservation" && (value == "on" || value == "off")) result.Observation = value == "on";
                else return result;
            }
            result.Valid = keys.Contains("RuntimeObservation"); return result;
        }
        internal static LedgerDiagnosticSettings Read()
        {
            try
            {
                string path = LedgerInstallPaths.Current.Settings;
                if (!File.Exists(path) || new FileInfo(path).Length > 1024) return new LedgerDiagnosticSettings();
                return Parse(File.ReadAllLines(path));
            }
            catch (Exception) { return new LedgerDiagnosticSettings(); }
        }
    }

    // A delivery boundary only: no capture, storage, readiness or hook return value depends on it.
    internal sealed class LedgerDiagnostics
    {
        private sealed class Repetition { internal long At, Suppressed; }
        private readonly Dictionary<string, Repetition> repeats = new Dictionary<string, Repetition>();
        private readonly object gate = new object();
        private readonly Repetition overflow = new Repetition { At = -60000 };
        internal IDiagnosticLog Sink;
        internal string Minimum = "Warn";
        internal long Filtered, Suppressed, Failures;
        private string allowedException;
        private static int Rank(string level) { return level == "ERROR" ? 3 : level == "WARN" ? 2 : level == "INFO" ? 1 : 0; }
        internal void Write(string level, string kind, string ticks, string detail, long now)
        {
            // Includes hostile/faulting sink properties, not only FileStream.Write exceptions.
            try
            {
                lock (gate)
                {
                    if (Minimum == "Off" || Rank(level) < Rank(Minimum.ToUpperInvariant())) { Filtered++; return; }
                    if (Sink == null) return;
                    if(kind=="ExceptionDetail")allowedException=null;
                    if(kind=="ExceptionChunk"&&Minimum!="Debug"&&(allowedException==null||!detail.StartsWith(allowedException+" ",StringComparison.Ordinal))){Suppressed++;return;}
                    if (Rank(level) >= 2 && Minimum != "Debug" && kind!="ExceptionChunk")
                    {
                        Repetition prior;
                        if (!repeats.TryGetValue(kind, out prior))
                        {
                            if (repeats.Count >= 64) prior=overflow;
                            else repeats[kind] = prior = new Repetition { At = now - 60000 };
                        }
                        if (now - prior.At < 60000) { prior.Suppressed++; Suppressed++; return; }
                        detail += " suppressedSameKind=" + prior.Suppressed;
                        prior.Suppressed = 0; prior.At = now;
                    }
                    if(kind=="ExceptionDetail"){int end=detail.IndexOf(' ');allowedException=end>0?detail.Substring(0,end):null;}
                    if(!Sink.Write(kind, ticks, "[" + level + "] " + detail))Failures++;
                }
            }
            catch (Exception) { System.Threading.Interlocked.Increment(ref Failures); }
        }
        internal static IDiagnosticLog Open(string root, string session)
        {
            // Legacy root files are investigation evidence and are NEVER part of retention.
            string folder = System.IO.Path.Combine(root, "operational-v1"); Directory.CreateDirectory(folder);
            if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("Operational diagnostic directory is linked");
            var files = new List<FileInfo>();
            foreach (string path in Directory.EnumerateFiles(folder, "*.log"))
            {
                var file = new FileInfo(path);
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0 && Regex.IsMatch(file.Name, @"^(?:TradeLedger|TradeLedgerProbe)-\d{8}T\d{9}Z-[a-f0-9]{32}\.log$")) files.Add(file);
            }
            files.Sort((a,b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            long bytes = 0; foreach (var file in files) bytes += file.Length;
            // At most eight 2MiB session files. Exclusive access protects concurrent active writers.
            while (files.Count >= 8 || bytes + 2 * 1024 * 1024 > 16 * 1024 * 1024)
            {
                if (files.Count == 0) break; var file = files[0]; files.RemoveAt(0);
                try { using (var handle = File.Open(file.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } File.Delete(file.FullName); bytes -= file.Length; }
                catch (IOException) { /* Never remove a live/locked session; logger may reach its bound instead. */ }
            }
            return new BoundedLog(folder, session, 2 * 1024 * 1024, 16 * 1024 * 1024, 8);
        }
    }
}
