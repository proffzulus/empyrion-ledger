using System;
using System.Threading;

namespace TradeLedgerProbeB
{
    // Exception.ToString is intentional and restricted to caught exceptions, never game payloads.
    public static class ExceptionTrace
    {
        private static int nextId;
        public const int CharacterLimit = 32768;
        public static void Write(IProbeHost host, string where, Exception error, string attribution)
        {
            try
            {
                int id = Interlocked.Increment(ref nextId);
                string text;
                try { text = error.ToString(); }
                catch (Exception formatError) { text = error.GetType().FullName + " ToString-failed=" + formatError.GetType().FullName; }
                int length = Math.Min(text.Length, CharacterLimit);
                host.Trace(TraceLevel.Error, "ExceptionDetail", "errorId=" + id + " where=" + Safe.Text(where) +
                    " " + attribution + " type=" + Safe.Text(error.GetType().FullName) + " hresult=0x" + error.HResult.ToString("X8") +
                    " originalChars=" + text.Length + " capturedChars=" + length + " truncated=" + (length != text.Length) +
                    " format=Exception.ToString chunkChars=384");
                for (int offset = 0; offset < length; offset += 384)
                    host.Trace(TraceLevel.Error, "ExceptionChunk", "errorId=" + id + " offset=" + offset + " " + attribution +
                        " text=" + Safe.Text(text.Substring(offset, Math.Min(384, length - offset)), 384));
            }
            catch (Exception) { /* Diagnostics must never throw back into the host or mask the first fault. */ }
        }
    }
}
