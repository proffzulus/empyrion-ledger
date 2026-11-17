using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace TradeLedgerProbe
{
    // No rotation, deletion, rewrite, or seeking. On a bound, append a final marker and stop.
    internal sealed class BoundedLog : TradeLedgerProbeB.IDiagnosticLog
    {
        internal const long DefaultSessionLimit = 32 * 1024 * 1024;
        internal const long DefaultFolderLimit = 128 * 1024 * 1024;
        internal const int DefaultFileLimit = 128;
        private const int Reserve = 2048;
        private readonly object gate = new object();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly string session;
        private readonly string folder;
        private readonly long sessionLimit, folderLimit;
        private readonly int fileLimit;
        private readonly Mutex folderMutex;
        private readonly FileStream stream;
        private readonly UTF8Encoding encoding = new UTF8Encoding(false);
        private long sequence, dropped, rateWindow;
        private long totalDropped, truncated, written, attempted;
        public string Counters { get { lock (gate) return "attempted=" + attempted + " written=" + written + " dropped=" + totalDropped + " lineTruncated=" + truncated + " pendingDropped=" + dropped + " status=" + Status; } }
        private int inWindow;
        private bool stopped;
        public string Path { get; private set; }
        public string Status { get; private set; }

        internal BoundedLog(string directory, string sessionId,
            long sessionBytes = DefaultSessionLimit, long folderBytes = DefaultFolderLimit,
            int maxFiles = DefaultFileLimit)
        {
            if (sessionBytes < 8192 || folderBytes < sessionBytes || maxFiles < 1)
                throw new ArgumentOutOfRangeException("limits");
            session = sessionId;
            folder = System.IO.Path.GetFullPath(directory);
            sessionLimit = sessionBytes; folderLimit = folderBytes; fileLimit = maxFiles;
            Directory.CreateDirectory(folder);
            using (var sha = SHA256.Create())
            {
                string key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(folder.ToUpperInvariant()))).Replace("-", "");
                folderMutex = new Mutex(false, "Local\\TradeLedgerProbe-" + key);
            }
            bool held = false;
            try
            {
                held = Acquire();
                if (!held) throw new IOException("Log folder busy");
                int files; long bytes = FolderBytes(out files);
                // Budget failure must not trigger fallback and evade the folder bound.
                if (files >= fileLimit || bytes + Reserve >= folderLimit)
                    throw new LogBudgetException();
                Path = System.IO.Path.Combine(folder, "TradeLedger-" +
                    DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "-" + session + ".log");
                stream = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                Status = "active";
            }
            catch
            {
                if (held) { folderMutex.ReleaseMutex(); held = false; }
                folderMutex.Dispose();
                throw;
            }
            finally { if (held) folderMutex.ReleaseMutex(); }
        }

        private bool Acquire()
        {
            try { return folderMutex.WaitOne(0); }
            catch (AbandonedMutexException) { return true; }
        }

        private long FolderBytes(out int files)
        {
            long total = 0; files = 0;
            foreach (string file in Directory.EnumerateFiles(folder, "*.log", SearchOption.TopDirectoryOnly))
            {
                string name=System.IO.Path.GetFileName(file);if(!name.StartsWith("TradeLedger-",StringComparison.Ordinal)&&!name.StartsWith("TradeLedgerProbe-",StringComparison.Ordinal))continue;
                files++;
                total = checked(total + new FileInfo(file).Length);
                if (files > fileLimit || total >= folderLimit) break;
            }
            return total;
        }

        public bool Write(string kind, string ticks, string detail)
        {
            lock (gate)
            {
                attempted++;
                if (stopped) { totalDropped++; return false; }
                bool held = false;
                try
                {
                    long window = clock.ElapsedMilliseconds / 1000;
                    if (window != rateWindow) { rateWindow = window; inWindow = 0; }
                    if (++inWindow > 2000 && kind != "Shutdown" && kind != "LogClosed" && kind != "LoggerCounters" && kind != "ObserverCounters" && kind != "HookCounterFinal") { dropped++; totalDropped++; return false; }
                    held = Acquire();
                    if (!held) { dropped++; totalDropped++; return false; }
                    int files; long folderBytes = FolderBytes(out files);
                    string suffix = dropped > 0 ? " droppedSincePrevious=" + dropped : "";
                    string body = kind + " " + detail + suffix;
                    if (body.Length > 6000) { body = body.Substring(0, 6000) + " [line-truncated]"; truncated++; }
                    byte[] line = Line(ticks, body);
                    if (stream.Length + line.Length + Reserve > sessionLimit ||
                        folderBytes + line.Length + Reserve > folderLimit || files > fileLimit)
                    {
                        byte[] terminal = Line(ticks, "LogStopped [WARN] reason=size-or-file-budget furtherRecordsNotCaptured=true");
                        if (stream.Length + terminal.Length <= sessionLimit && folderBytes + terminal.Length <= folderLimit)
                        { stream.Write(terminal, 0, terminal.Length); stream.Flush(); }
                        Status = "budget-exhausted"; stopped = true;
                        totalDropped++;
                        return false;
                    }
                    stream.Write(line, 0, line.Length);
                    stream.Flush();
                    written++;
                    dropped = 0;
                    return true;
                }
                catch (Exception error)
                {
                    Status = "io-disabled:" + error.GetType().Name;
                    totalDropped++;
                    stopped = true;
                    return false;
                }
                finally { if (held) folderMutex.ReleaseMutex(); }
            }
        }

        private byte[] Line(string ticks, string body)
        {
            return encoding.GetBytes(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                " seq=" + (++sequence).ToString("D6", CultureInfo.InvariantCulture) +
                " elapsedMs=" + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) +
                " session=" + session + " ticks=" + ticks +
                " thread=" + Thread.CurrentThread.ManagedThreadId + " " + body + Environment.NewLine);
        }

        public void Dispose()
        {
            lock (gate)
            {
                stopped = true;
                try { stream.Dispose(); } finally { folderMutex.Dispose(); }
            }
        }
    }

    internal sealed class LogBudgetException : IOException { }
}
