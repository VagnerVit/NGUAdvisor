using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // Human names for the fixed index-based systems (Diggers, Beards) and single-value systems
    // (Wandoos OS, NGU difficulty), so the editor can show "Stats" instead of a bare 2. Zero game deps.
    public static class SystemCatalog
    {
        // Digger slot indices -> name (from the sample-profile documentation).
        public static readonly IReadOnlyList<KeyValuePair<int, string>> Diggers = new List<KeyValuePair<int, string>>
        {
            new KeyValuePair<int, string>(0, "Drops"),
            new KeyValuePair<int, string>(1, "Wandoos"),
            new KeyValuePair<int, string>(2, "Stats"),
            new KeyValuePair<int, string>(3, "Adventure"),
            new KeyValuePair<int, string>(4, "Energy NGU"),
            new KeyValuePair<int, string>(5, "Magic NGU"),
            new KeyValuePair<int, string>(6, "Energy Beard"),
            new KeyValuePair<int, string>(7, "Magic Beard"),
            new KeyValuePair<int, string>(8, "Power (PP)"),
            new KeyValuePair<int, string>(9, "Daycare"),
            new KeyValuePair<int, string>(10, "Blood"),
            new KeyValuePair<int, string>(11, "EXP"),
        };

        public static readonly IReadOnlyList<KeyValuePair<int, string>> Beards = new List<KeyValuePair<int, string>>
        {
            new KeyValuePair<int, string>(0, "Stats"),
            new KeyValuePair<int, string>(1, "Drops"),
            new KeyValuePair<int, string>(2, "Number"),
            new KeyValuePair<int, string>(3, "NGU"),
            new KeyValuePair<int, string>(4, "Wandoos"),
            new KeyValuePair<int, string>(5, "Adventure"),
            new KeyValuePair<int, string>(6, "GPS"),
        };

        public static readonly IReadOnlyList<KeyValuePair<int, string>> WandoosOS = new List<KeyValuePair<int, string>>
        {
            new KeyValuePair<int, string>(0, "Wandoos 98"),
            new KeyValuePair<int, string>(1, "Wandoos Meh"),
            new KeyValuePair<int, string>(2, "Wandoos XL"),
        };

        public static readonly IReadOnlyList<KeyValuePair<int, string>> Difficulty = new List<KeyValuePair<int, string>>
        {
            new KeyValuePair<int, string>(0, "Normal"),
            new KeyValuePair<int, string>(1, "Evil"),
            new KeyValuePair<int, string>(2, "Sadistic"),
        };

        // Consumable codes (from ConsumablesManager) -> label. Items entries are "CODE" or "CODE:amount".
        public static readonly IReadOnlyList<KeyValuePair<string, string>> Consumables = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("EPOT-A", "Energy Potion A"),
            new KeyValuePair<string, string>("EPOT-B", "Energy Potion B"),
            new KeyValuePair<string, string>("EPOT-C", "Energy Potion C"),
            new KeyValuePair<string, string>("MPOT-A", "Magic Potion A"),
            new KeyValuePair<string, string>("MPOT-B", "Magic Potion B"),
            new KeyValuePair<string, string>("MPOT-C", "Magic Potion C"),
            new KeyValuePair<string, string>("R3POT-A", "R3 Potion A"),
            new KeyValuePair<string, string>("R3POT-B", "R3 Potion B"),
            new KeyValuePair<string, string>("R3POT-C", "R3 Potion C"),
            new KeyValuePair<string, string>("EBARBAR", "Energy Bar"),
            new KeyValuePair<string, string>("MBARBAR", "Magic Bar"),
            new KeyValuePair<string, string>("MUFFIN", "Muffin"),
            new KeyValuePair<string, string>("LC", "Lucky Charm"),
            new KeyValuePair<string, string>("SLC", "Super Lucky Charm"),
            new KeyValuePair<string, string>("MAYO", "Mayo"),
        };

        public class ChallengeInfo
        {
            public readonly string Code, Label;
            public readonly int Cap;   // max completion count (from observed profile usage)
            public ChallengeInfo(string code, string label, int cap) { Code = code; Label = label; Cap = cap; }
        }

        // Challenge codes (from BaseRebirth.ParseChallenges) with a max count. Entries are "CODE-count".
        // Labels carry no "Challenge" suffix — every UI that shows them is already in a challenge context.
        public static readonly IReadOnlyList<ChallengeInfo> Challenges = new List<ChallengeInfo>
        {
            new ChallengeInfo("BASIC", "Basic", 5),
            new ChallengeInfo("NOAUG", "No Augs", 5),
            new ChallengeInfo("24HR", "24 Hour", 10),
            new ChallengeInfo("100LC", "100 Level", 5),
            new ChallengeInfo("NOEC", "No Equipment", 5),
            new ChallengeInfo("TC", "Troll", 7),
            new ChallengeInfo("NORB", "No Rebirth", 10),
            new ChallengeInfo("LSC", "Laser Sword", 20),
            new ChallengeInfo("BLIND", "Blind", 10),
            new ChallengeInfo("NONGU", "No NGU", 10),
            new ChallengeInfo("NOTM", "No Time Machine", 10),
        };

        // Rebirth types actually used in profiles. "Time" uses only a time; the rest also take a Target.
        // Labels omit "Rebirth" — they render inside the REBIRTH card, and the row's own fields
        // ("at h m s", "target") carry the rest of the sentence.
        public static readonly IReadOnlyList<KeyValuePair<string, string>> RebirthTypes = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("Time", "Set time"),
            new KeyValuePair<string, string>("Number", "Number target"),
            new KeyValuePair<string, string>("TimeBalancedMuffin", "Muffin balance"),
            new KeyValuePair<string, string>("Bosses", "Bosses"),
        };

        public static bool TypeTakesTarget(string type) =>
            !string.Equals(type, "Time", System.StringComparison.OrdinalIgnoreCase);

        public static string LabelOf(IReadOnlyList<KeyValuePair<string, string>> map, string code)
        {
            foreach (var kv in map) if (string.Equals(kv.Key, code, System.StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return code;
        }

        public static string NameOf(IReadOnlyList<KeyValuePair<int, string>> map, int index)
        {
            foreach (var kv in map) if (kv.Key == index) return kv.Value;
            return index.ToString();
        }

        public static string Display(IReadOnlyList<KeyValuePair<int, string>> map, int index) => $"{index} — {NameOf(map, index)}";
    }
}
