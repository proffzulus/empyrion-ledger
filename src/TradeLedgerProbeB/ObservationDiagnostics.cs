using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TradeLedgerProbeB
{
    // An optional host extension; API2 loading and the version-specific reader stay separate.
    public interface IObservationContext { string ContextEvidence { get; } }

    public sealed class InteractionCycle
    {
        private long next;
        public long Number { get; private set; }
        public bool Active { get; private set; }
        public bool NativeOpenSeen { get; private set; }
        public string LastReason { get; private set; }
        public void Begin() { Number = ++next; Active = true; NativeOpenSeen = false; LastReason = null; }
        public void MarkOpen() { NativeOpenSeen = true; }
        public bool NewReason(string reason)
        { if (LastReason == reason) return false; LastReason = reason; return true; }
        public void End() { Active = false; NativeOpenSeen = false; LastReason = null; }
    }

    public static class WindowScopePolicy
    {
        // A companion name alone proves neither ownership nor co-visibility. No allowlist bypass.
        public static string Reject(bool candidateInStack, bool candidateAtTop, bool rootMatch,
            bool activeSelf, bool activeHierarchy, bool alphaReady, int owner, int local)
        {
            if (!candidateInStack) return "candidate-controller-not-in-open-stack";
            if (!candidateAtTop) return "candidate-below-top-companion-ownership-or-covisibility-UNPROVEN";
            if (!rootMatch) return "candidate-root-transform-mismatch";
            if (!activeSelf || !activeHierarchy) return "candidate-root-inactive";
            if (owner < 0 || local < 0 || owner != local) return "candidate-owner-not-local-or-unavailable";
            if (!alphaReady) return "candidate-panel-alpha-or-readiness-missing";
            return null;
        }
    }

    public struct VisibleGlyph
    {
        public int Index, Length;
        public char Character;
        public VisibleGlyph(int index, int length, char character) { Index = index; Length = length; Character = character; }
    }
    public static class RevealedText
    {
        public static string Read(string source, int visible, int available, Func<int, VisibleGlyph> glyphAt, out string status)
        {
            status = "source-or-glyph-count-unavailable";
            if (source == null || visible <= 0 || visible > available) return null;
            int end = 0;
            for (int i = 0; i < Math.Min(visible, 4096); i++)
            {
                VisibleGlyph g = glyphAt(i);
                if (g.Index < end || g.Index < 0 || g.Index >= source.Length || g.Length < 1 ||
                    g.Length > source.Length - g.Index || source[g.Index] != g.Character)
                { status = "stale-or-unsupported-glyph-source-mapping"; return null; }
                end = g.Index + g.Length;
            }
            status = visible > 4096 ? "revealed-prefix-glyph-bound-4096" : "revealed-prefix-glyph-mapping-matched";
            return source.Substring(0, end);
        }
    }

    public sealed class DumpBudget
    {
        public const int MaxPerSecond = 1024, MaxPerInteraction = 4096, MaxPerSession = 24000;
        private long window = -1;
        private int perWindow;
        private readonly Dictionary<long, int> interactionCounts = new Dictionary<long, int>();
        public long Captured { get; private set; }
        public long Dropped { get; private set; }
        public string LastLimit { get; private set; }
        public bool Take(long elapsedMs, long interactionKey)
        {
            if (window != elapsedMs / 1000) { window = elapsedMs / 1000; perWindow = 0; }
            int perInteraction;
            interactionCounts.TryGetValue(interactionKey, out perInteraction);
            LastLimit = Captured >= MaxPerSession ? "session-records" :
                (!interactionCounts.ContainsKey(interactionKey) && interactionCounts.Count >= 512) ? "interaction-count" :
                perInteraction >= MaxPerInteraction ? "interaction-records" : perWindow >= MaxPerSecond ? "rate" : null;
            if (LastLimit != null) { Dropped++; return false; }
            Captured++; perWindow++; interactionCounts[interactionKey] = perInteraction + 1; return true;
        }
    }

    // Raw evidence tracking only: a pause never certifies a completed request or a task.
    public sealed class DialoguePages
    {
        public int Page { get; private set; }
        public int Sample { get; private set; }
        public int StableSamples { get; private set; }
        public long ChangedAt { get; private set; }
        public string State { get; private set; }
        public string Body { get; private set; }
        public bool Add(string state, string body, long now)
        {
            body = body ?? "";
            bool boundary = Page == 0 || state != State ||
                (!string.IsNullOrEmpty(Body) && !body.StartsWith(Body, StringComparison.Ordinal) && body != Body);
            if (boundary) Page++;
            if (state == State && body == Body) { StableSamples++; return boundary; }
            State = state; Body = body; Sample++; StableSamples = 1; ChangedAt = now;
            return boundary;
        }
    }

    public static class DumpJson
    {
        public static string Quote(string value, int limit = 1800)
        {
            if (value == null) return "null";
            var b = new StringBuilder("\"");
            for (int i = 0; i < Math.Min(value.Length, limit); i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 32 || char.IsSurrogate(c)) b.Append("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            if (value.Length > limit) b.Append("[value-truncated]");
            return b.Append('"').ToString();
        }
        public static string Record(params string[] pairs)
        {
            if (pairs.Length % 2 != 0 || pairs.Length > 48) throw new ArgumentException("bounded-flat-pairs");
            var b = new StringBuilder("{");
            for (int i = 0; i < pairs.Length; i += 2)
            { if (i != 0) b.Append(','); b.Append(Quote(pairs[i], 80)).Append(':').Append(Quote(pairs[i + 1])); }
            return b.Append('}').ToString();
        }
    }
}
