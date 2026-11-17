using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TradeLedgerProbe;

namespace TradeLedgerProbeB
{
    public enum TraceLevel { Debug, Info, Warn, Error }
    public interface IProbeHost
    {
        bool OnApiThread { get; }
        bool WorldReady { get; }
        int LocalPlayerId { get; }
        long ElapsedMs { get; }
        void Trace(TraceLevel level, string kind, string detail);
    }
    public interface IRuntimeObserver : IDisposable
    {
        void Start(IProbeHost host, Assembly game);
        void Poll();
        void NativeEvent(int type, object arg1, object arg2);
        void ResetWorld();
    }
    public sealed class FieldSpec
    {
        public readonly int Token;
        public readonly string Type, Name, FieldType;
        public FieldSpec(int token, string type, string name, string fieldType)
        { Token = token; Type = type; Name = name; FieldType = fieldType; }
    }
    public sealed class MethodSpec
    {
        public readonly int Token, ParameterCount;
        public readonly string Type, Name, Signature;
        public MethodSpec(int token, string type, string name, int count, string signature)
        { Token = token; Type = type; Name = name; ParameterCount = count; Signature = signature; }
    }
    public sealed class FieldOperandSpec
    {
        public readonly int Offset, Token;
        public readonly string DeclaringType, Name, FieldType;
        public FieldOperandSpec(int offset, int token, string declaringType, string name, string fieldType)
        { Offset = offset; Token = token; DeclaringType = declaringType; Name = name; FieldType = fieldType; }
    }

    public static class Safe
    {
        public static string Text(string value, int limit = 240) { return Compact.Text(value, limit); }
        public static string Value(object value) { return Compact.Value(value); }
        public static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
        }
    }

    // Pure policy shared by the real adapter and the offline tests.
    public static class ObservationRules
    {
        public static bool Scope(bool fingerprint, bool optIn, bool apiThread, bool world,
            bool topWindowMatches, bool rootActive, bool alphaReady, int ownerId, int localId)
        {
            return fingerprint && optIn && apiThread && world && topWindowMatches &&
                rootActive && alphaReady && localId >= 0 && ownerId == localId;
        }
        public static T ReadIfVisible<T>(bool scope, Func<T> read) { return scope ? read() : default(T); }
        public static bool SafeModel(int rowCount, int eligibleModelCount, bool processing, bool searchEmpty)
        {
            return rowCount >= 0 && rowCount <= 256 && rowCount == eligibleModelCount &&
                !processing && searchEmpty;
        }
        public static bool FingerprintsMatch(IDictionary<string, string> expected, Func<string, string> readHash)
        {
            foreach (var pair in expected)
                if (!string.Equals(pair.Value, readHash(pair.Key), StringComparison.OrdinalIgnoreCase)) return false;
            return expected.Count > 0;
        }
        public static bool MatchesChoiceState(string displayedState, object eventState, object eventIndex)
        {
            return displayedState != null && eventState is string && eventIndex is int &&
                (int)eventIndex >= 0 && (int)eventIndex <= 32 &&
                string.Equals(displayedState, (string)eventState, StringComparison.Ordinal);
        }

        // Text spans only. No item IDs, eligibility parsing, configuration, or authoritative counts.
        public static List<string> CompoundTextCandidates(string displayedText)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(displayedText) || displayedText.Length > 4096 ||
                (displayedText.IndexOf(" + ", StringComparison.Ordinal) < 0 && displayedText.IndexOf(" and ", StringComparison.Ordinal) < 0)) return result;
            var regex = new Regex(@"\b[1-9]\d{0,5}\s+(?:[x×]\s+)?[\p{L}][\p{L}\p{N} '\-]{0,60}",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(30));
            foreach (string segment in Regex.Split(displayedText, @"\s+and\s+|\s+\+\s+"))
            {
                Match match = regex.Match(segment);
                if (match.Success) result.Add(match.Value.Trim());
                if (result.Count == 8) break;
            }
            if (result.Count < 2) result.Clear();
            return result;
        }
    }

    public sealed class RawOfferCandidate
    {
        public readonly int Item, Variant, Stock, Capacity, PriceA, PriceB;
        private RawOfferCandidate(int item, int variant, int stock, int capacity, int priceA, int priceB)
        { Item = item; Variant = variant; Stock = stock; Capacity = capacity; PriceA = priceA; PriceB = priceB; }
        public static RawOfferCandidate ReadAvailable(Func<int, int> read)
        {
            int stock = read(0x5348), capacity = read(0x5349);
            // Audit's renderer filter: don't even request identifying fields for excluded entries.
            if (stock <= 0 && capacity <= 0) return null;
            return new RawOfferCandidate(read(0x5346), read(0x5347), stock, capacity, read(0x534a), read(0x534b));
        }
        public string Describe()
        {
            return "raw532B=" + Item + " raw532C=" + Variant + " raw532D=" + Stock + " raw532E=" + Capacity +
                " raw532F=" + PriceA + " raw5330=" + PriceB +
                " demandDifferenceCandidate=" + ((long)Capacity - Stock) +
                " interpretation=UNVERIFIED item/variant/stock/capacity/two-base-prices/demand-difference;zero-direction-preserved rowPairing=UNKNOWN";
        }
    }
}
