using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // The vocabulary of resource-priority tokens the advisor understands, per the project README.
    // The valid set DIFFERS by resource (Energy / Magic / R3), including index ranges - so the editor only
    // offers what actually applies to the timeline being edited. Structured build/parse keeps input typo-proof.
    //
    // Token grammar: [CAP]BASE[-index][:percent]   e.g.  CAPNGU-7:50, AUG-10, ALLNGU, CAPWAN:10
    public enum ResourceKind { Energy, Magic, R3 }

    public static class PriorityCatalog
    {
        public class BaseType
        {
            public readonly string Code;      // e.g. "NGU", "ALLNGU", "WAN"
            public readonly bool HasIndex;    // whether a -N index is meaningful
            public readonly int IndexMax;     // inclusive max index (resource-specific); 0 when no index
            public readonly string Label;     // human description (from README)
            public BaseType(string code, bool hasIndex, int indexMax, string label)
            { Code = code; HasIndex = hasIndex; IndexMax = indexMax; Label = label; }
            public string Display => $"{Code} — {Label}";
        }

        private static readonly List<BaseType> Energy = new List<BaseType>
        {
            new BaseType("NGU",     true, 8,  "NGU (by number)"),
            new BaseType("ALLNGU",  false, 0, "All NGUs"),
            new BaseType("AT",      true, 4,  "Advanced Training (by number)"),
            new BaseType("ALLAT",   false, 0, "All Advanced Training"),
            new BaseType("AUG",     true, 13, "Augment (by number)"),
            new BaseType("BESTAUG", false, 0, "Best Augment"),
            new BaseType("BT",      true, 11, "Basic Training (by number)"),
            new BaseType("ALLBT",   false, 0, "All Basic Training"),
            new BaseType("WAN",     false, 0, "Wandoos (energy)"),
            new BaseType("TM",      false, 0, "Time Machine (energy)"),
        };

        private static readonly List<BaseType> Magic = new List<BaseType>
        {
            new BaseType("NGU",     true, 6,  "NGU (by number)"),
            new BaseType("ALLNGU",  false, 0, "All NGUs"),
            new BaseType("WAN",     false, 0, "Wandoos (magic)"),
            new BaseType("TM",      false, 0, "Time Machine (magic)"),
            new BaseType("RIT",     true, 40, "Ritual (by number)"),
            // -index is SECONDS, not minutes: BR.CastRituals(secondsToRun) skips any ritual whose time
            // left exceeds it, and the README's example is BR-3600 = "ends before the 1 hour mark".
            // The editor advertised minutes with a 1440 max, so BR-30 read as half an hour when it is
            // half a minute. Max = 86400 (24h) to match the seconds reading.
            new BaseType("BR",      true, 86400, "Blood Rituals — cast (optional -seconds limit)"),
        };

        private static readonly List<BaseType> R3 = new List<BaseType>
        {
            new BaseType("HACK",    true, 14, "Hack (by number)"),
            new BaseType("ALLHACK", false, 0, "All Hacks"),
        };

        public static IReadOnlyList<BaseType> For(ResourceKind kind)
        {
            if (kind == ResourceKind.Energy) return Energy;
            if (kind == ResourceKind.Magic) return Magic;
            return R3;
        }

        // Union of every known base code, for structural recognition during Parse (resource-agnostic).
        private static readonly Dictionary<string, BaseType> AllByCode =
            Energy.Concat(Magic).Concat(R3)
                  .GroupBy(b => b.Code)
                  .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        public static BaseType Find(ResourceKind kind, string code) =>
            For(kind).FirstOrDefault(b => string.Equals(b.Code, code, StringComparison.OrdinalIgnoreCase));

        public static BaseType FindAny(string code) =>
            code != null && AllByCode.TryGetValue(code, out var bt) ? bt : null;

        public static bool IsValidFor(ResourceKind kind, string code) => Find(kind, code) != null;

        public static string Build(bool cap, string baseCode, int? index, int? percent)
        {
            var b = (baseCode ?? "").ToUpperInvariant();
            var sb = new System.Text.StringBuilder();
            if (cap) sb.Append("CAP");
            sb.Append(b);
            var bt = FindAny(b);
            if (index.HasValue && bt != null && bt.HasIndex)
                sb.Append('-').Append(index.Value);
            if (percent.HasValue)
                sb.Append(':').Append(percent.Value);
            return sb.ToString();
        }

        public struct Token
        {
            public bool Cap;
            public string Base;     // known base code, or null if unrecognized
            public int? Index;
            public int? Percent;    // 0..100
            public string Raw;
            public bool Recognized => Base != null;
        }

        public static Token Parse(string raw)
        {
            var t = new Token { Raw = raw };
            if (string.IsNullOrWhiteSpace(raw)) return t;

            var s = raw.Trim().ToUpperInvariant();

            var colon = s.IndexOf(':');
            if (colon >= 0)
            {
                if (int.TryParse(s.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct))
                    t.Percent = Math.Max(0, Math.Min(100, pct));
                s = s.Substring(0, colon);
            }

            if (s.StartsWith("CAP")) { t.Cap = true; s = s.Substring(3); }

            var dash = s.IndexOf('-');
            if (dash >= 0)
            {
                if (int.TryParse(s.Substring(dash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                    t.Index = idx;
                s = s.Substring(0, dash);
            }

            if (AllByCode.TryGetValue(s, out var bt))
                t.Base = bt.Code;

            return t;
        }
    }
}
