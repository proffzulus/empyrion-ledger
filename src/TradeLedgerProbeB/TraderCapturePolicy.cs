using System;

namespace TradeLedgerProbeB
{
    // Pure policy used by the actual reader and offline tests. No Unity/game actions.
    public sealed class TraderVisibilityProof
    {
        public bool NativeSessionOpen, InStack, AtTop, EntryOpen, ControllerConfigMatches;
        public bool RootMatches, RootActive, AlphaReady;
        public int Owner = -1, Local = -1, StackCount, CandidateDepth = -1;
        public string TopKey, CandidateKey, LinkedWindowKey;
        public bool BagTypeMatches, BagEntryOpen, BagConfigMatches, BagRootMatches, BagRootActive;
        public bool BagAlphaReady, SameOwnerObject, DistinctOpaquePanels;
        public int BagOwner = -1;
    }

    public static class TraderScopePolicy
    {
        public static bool IsOpaque(float alpha, float finalAlpha)
        { return alpha >= .99f && alpha <= 1f && finalAlpha >= .99f && finalAlpha <= 1f; }
        // PartitionConverterStoreSite is a struct: registration stores a value copy, not a reference.
        public static bool ConfigurationCopiesMatch(Func<int, object> left, Func<int, object> right)
        {
            for (int row = 0xc132; row <= 0xc13e; row++)
            {
                object a = left(row), b = right(row);
                if (a == null || b == null) { if (a != null || b != null) return false; continue; }
                if (a is bool && b is bool) { if ((bool)a != (bool)b) return false; }
                else if (a is string && b is string) { if (!string.Equals((string)a, (string)b, StringComparison.Ordinal)) return false; }
                else return false;
            }
            return true;
        }
        public static string Reject(TraderVisibilityProof p)
        {
            if (!p.NativeSessionOpen) return "trader-native-open-session-missing-or-closed";
            if (!p.InStack || p.CandidateKey != "TraderNPC") return "trader-current-entry-not-in-open-stack";
            if (!p.EntryOpen) return "trader-manager-entry-marked-closed";
            if (!p.ControllerConfigMatches) return "trader-controller-registration-config-mismatch";
            if (!p.RootMatches || !p.RootActive) return "trader-root-inactive-or-transform-mismatch";
            if (p.Local < 0 || p.Owner != p.Local) return "trader-owner-not-local";
            if (!p.AlphaReady) return "trader-alpha-animation-or-readiness-pending";
            if (p.AtTop && p.TopKey == "TraderNPC") return null;
            if (p.StackCount != 2 || p.CandidateDepth != 1 || p.TopKey != "Bag") return "trader-unrelated-or-extra-blocking-window";
            if (p.LinkedWindowKey != "Bag") return "trader-direct-linked-Bag-proof-missing";
            if (!p.BagTypeMatches || !p.BagConfigMatches) return "trader-Bag-controller-registration-proof-missing";
            if (!p.BagEntryOpen || !p.BagRootMatches || !p.BagRootActive) return "trader-Bag-closed-inactive-or-root-mismatch";
            if (p.BagOwner != p.Local || !p.SameOwnerObject) return "trader-Bag-local-owner-reference-proof-missing";
            if (!p.BagAlphaReady || !p.DistinctOpaquePanels) return "trader-Bag-covisibility-opacity-proof-pending";
            return null;
        }
    }

    public enum TraderReadResult { ScopeRejected, ModelMissing, RowsMissing, Processing, WaitingStable, Captured, Unchanged }

    public sealed class TraderRetryLoop
    {
        public const long IntervalMs = 250, TimeoutMs = 30000;
        public bool Active { get; private set; }
        public bool Expired { get; private set; }
        public long Interaction { get; private set; }
        public long Polls { get; private set; }
        private long lastPoll, waitingSince;
        private bool waiting;
        public void Begin(long now, long interaction)
        { Active = true; Expired = false; Interaction = interaction; Polls = 0; lastPoll = now - IntervalMs; waitingSince = now; waiting = true; }
        public bool Due(long now)
        { if (!Active || now - lastPoll < IntervalMs) return false; lastPoll = now; Polls++; return true; }
        public bool Poll(long now, Func<bool> scope, Func<TraderReadResult> payload, out TraderReadResult result)
        {
            result = TraderReadResult.ScopeRejected;
            if (!Due(now)) return false;
            result = scope() ? payload() : TraderReadResult.ScopeRejected;
            Finish(now, result);
            return true;
        }
        public bool Finish(long now, TraderReadResult result)
        {
            if (!Active) return false;
            if (result == TraderReadResult.Captured || result == TraderReadResult.Unchanged) { waiting = false; return false; }
            if (!waiting) { waiting = true; waitingSince = now; }
            if (now - waitingSince < TimeoutMs) return false;
            Expired = true; Active = false; return true;
        }
        public void Stop() { Active = false; }
    }

    public sealed class TraderSnapshotStability
    {
        public string Current { get; private set; }
        private string pending;
        private int samples;
        public TraderReadResult Sample(string digest, bool processing)
        {
            if (processing) { LoseScope(); return TraderReadResult.Processing; }
            if (digest != pending) { pending = digest; samples = 1; return TraderReadResult.WaitingStable; }
            samples = Math.Min(2, samples + 1);
            if (samples < 2) return TraderReadResult.WaitingStable;
            if (digest == Current) return TraderReadResult.Unchanged;
            Current = digest; return TraderReadResult.Captured;
        }
        public void LoseScope() { pending = null; samples = 0; }
    }
}
