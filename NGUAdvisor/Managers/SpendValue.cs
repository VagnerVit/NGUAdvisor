using System;
using System.Collections.Generic;
using System.Globalization;

namespace NGUAdvisor.Managers
{
    // Value view of ITOPOD perks and Beast quirks whose effect is one factor of a multiplicative product.
    // It RANKS; SpendPlanner's guide order still decides what is bought.
    //
    // A factor (1 + L*e) inside a product has marginal relative gain e / ((1 + L*e)) per level whatever the
    // other factors are, so per point it is e / ((1 + L*e) * cost). That number is comparable only inside
    // one group (what the product multiplies): +1% energy power and +1% adventure stats are not the same
    // currency, and no ratio across groups says which is worth more.
    //
    // The id -> product mapping is the game's own: ItopodPerkController.energyPower1Bonus() ... and
    // BeastQuestPerkController.totalEnergyPowerBonus() ... in the decompile hard-code which perkLevel /
    // quirkLevel index feeds which product. Names and effectPerLevel, cost and caps are read live.
    public static class SpendValue
    {
        // Perks 6-11 (Generic I) use a literal 0.01 in the game, not effectPerLevel.
        public const double GenericOneEffect = 0.01;

        private struct GroupIds
        {
            public readonly string Group;
            public readonly int[] Ids;
            public GroupIds(string group, params int[] ids) { Group = group; Ids = ids; }
        }

        public const string GroupR3Prefix = "R3 ";

        // ItopodPerkController: total{Energy,Magic,Res3}{Power,Bar,Cap}Bonus, totalBoostBonus, totalStatBonus,
        // total{Energy,Magic}NGUBonus, totalAdventureBonus, totalSeedBonus, totalGoldDropBonus, totalQPBonus.
        private static readonly GroupIds[] PerkGroups =
        {
            new GroupIds("E power", 6, 57, 74, 116, 126, 135, 220),
            new GroupIds("E bars", 7, 58, 75, 117, 127, 136, 221),
            new GroupIds("E cap", 8, 59, 76, 118, 128, 137, 222),
            new GroupIds("M power", 9, 60, 77, 119, 129, 138, 223),
            new GroupIds("M bars", 10, 61, 78, 120, 130, 139, 224),
            new GroupIds("M cap", 11, 62, 79, 131, 140, 225),
            new GroupIds("R3 power", 95, 98, 101, 132, 141, 226),
            new GroupIds("R3 bars", 96, 99, 102, 133, 142, 227),
            new GroupIds("R3 cap", 97, 100, 103, 134, 143, 228),
            new GroupIds("boosts", 12, 33, 107, 229, 230),
            new GroupIds("attack/defense", 5, 54, 82),
            new GroupIds("E NGU speed", 13, 63, 80),
            new GroupIds("M NGU speed", 14, 64, 81),
            new GroupIds("adventure", 55, 83, 150, 152, 154),
            new GroupIds("seeds", 24),
            new GroupIds("gold drop", 23),
            new GroupIds("QP rewards", 89),
        };

        // BeastQuestPerkController: total{Energy,Magic,Res3}{Power,Cap,Bar}Bonus, totalStatBonus,
        // totalAdventureBonus, totalGoldBonus, totalBoostBonus, totalSeedBonus, totalBeardSpeedBonus,
        // total{Energy,Magic}NGUSpeed, ygg1 (92), blood1 (91).
        private static readonly GroupIds[] QuirkGroups =
        {
            new GroupIds("E power", 0, 35, 41, 61, 80, 177),
            new GroupIds("E cap", 1, 36, 42, 62, 81, 178),
            new GroupIds("E bars", 2, 37, 43, 63, 82, 179),
            new GroupIds("M power", 3, 38, 44, 64, 83, 180),
            new GroupIds("M cap", 4, 39, 45, 65, 84, 181),
            new GroupIds("M bars", 5, 40, 46, 66, 85, 182),
            new GroupIds("R3 power", 47, 67, 86, 183),
            new GroupIds("R3 cap", 48, 68, 87, 184),
            new GroupIds("R3 bars", 49, 69, 88, 185),
            new GroupIds("attack/defense", 7, 51, 76, 78, 170, 172),
            new GroupIds("adventure", 6, 8, 52, 77, 79, 171, 173),
            new GroupIds("gold", 9),
            new GroupIds("beard speed", 10),
            new GroupIds("boosts", 11, 53, 72, 73),
            new GroupIds("seeds", 12),
            new GroupIds("E NGU speed", 93, 95, 97),
            new GroupIds("M NGU speed", 94, 96, 98),
            new GroupIds("blood", 91),
            new GroupIds("yggdrasil yield", 92),
        };

        public static string PerkGroupOf(int id) => GroupOf(PerkGroups, id);
        public static string QuirkGroupOf(int id) => GroupOf(QuirkGroups, id);

        public static IEnumerable<KeyValuePair<string, int[]>> PerkIds()
        {
            foreach (GroupIds g in PerkGroups) yield return new KeyValuePair<string, int[]>(g.Group, g.Ids);
        }

        public static IEnumerable<KeyValuePair<string, int[]>> QuirkIds()
        {
            foreach (GroupIds g in QuirkGroups) yield return new KeyValuePair<string, int[]>(g.Group, g.Ids);
        }

        private static string GroupOf(GroupIds[] table, int id)
        {
            foreach (GroupIds g in table)
                foreach (int i in g.Ids)
                    if (i == id) return g.Group;
            return null;
        }

        public sealed class Candidate
        {
            public string Group;
            public int Id;
            public string Name;
            public long Level;
            public double EffectPerLevel;
            public long Cost;

            public double Gain => Gain(Level, EffectPerLevel, Cost);
        }

        // Relative gain of the whole product per point spent on the next level of one factor.
        public static double Gain(long level, double effectPerLevel, long cost)
        {
            if (cost <= 0 || double.IsNaN(effectPerLevel) || effectPerLevel <= 0) return 0;
            return effectPerLevel / ((1.0 + level * effectPerLevel) * cost);
        }

        public struct View
        {
            public bool GuideKnown;
            public string GuideName;
            public string GuideGroup;      // null = the guide buy feeds no product the model covers
            public double GuideGain;
            public bool HasBest;           // best value buy inside the guide buy's group (or overall if no guide buy)
            public string BestName;
            public string BestGroup;
            public double BestGain;
            public double Ratio;           // BestGain / GuideGain, 1 = the guide buy is the best of its group; NaN = not comparable
            public string OverallName;     // best gain across ALL groups -- different groups are not exchangeable
            public string OverallGroup;
            public double OverallGain;
        }

        public static View Compare(IList<Candidate> candidates, bool guideKnown, int guideId, string guideName)
        {
            View v = new View { GuideKnown = guideKnown, GuideName = guideName, Ratio = double.NaN };
            Candidate guide = null;
            if (guideKnown)
                foreach (Candidate c in candidates)
                    if (c.Id == guideId) { guide = c; break; }
            if (guide != null) { v.GuideGroup = guide.Group; v.GuideGain = guide.Gain; }

            Candidate overall = null;
            Candidate inGroup = null;
            foreach (Candidate c in candidates)
            {
                if (overall == null || c.Gain > overall.Gain) overall = c;
                if (guide != null && c.Group == guide.Group && (inGroup == null || c.Gain > inGroup.Gain)) inGroup = c;
            }
            if (overall != null) { v.OverallName = overall.Name; v.OverallGroup = overall.Group; v.OverallGain = overall.Gain; }
            Candidate best = inGroup ?? (guide == null ? overall : null);
            if (best != null)
            {
                v.HasBest = true; v.BestName = best.Name; v.BestGroup = best.Group; v.BestGain = best.Gain;
                if (guide != null && guide.Gain > 0) v.Ratio = best.Gain / guide.Gain;
            }
            return v;
        }

        // Percent of the product gained per 1000 points spent.
        public static string PerThousand(double gain)
        {
            return (gain * 1000.0 * 100.0).ToString("0.###", CultureInfo.InvariantCulture) + "%/1K";
        }

        public static string Describe(View v, string unit)
        {
            if (!v.HasBest) return "value: no ranked buy";
            string rate = PerThousand(v.BestGain) + " " + unit;
            if (!v.GuideKnown || v.GuideGroup == null)
                return "value: " + (v.GuideKnown ? v.GuideName + " feeds no ranked product; " : "")
                    + "best " + v.BestName + " (" + v.BestGroup + ") " + rate;
            if (v.Ratio <= 1.0000001)
                return "value: guide buy " + v.GuideName + " is the best " + v.GuideGroup + " buy, " + PerThousand(v.GuideGain) + " " + unit;
            return "value: guide buy " + v.GuideName + " " + PerThousand(v.GuideGain) + " " + unit + "; best " + v.GuideGroup
                + " buy " + v.BestName + " " + rate + " = " + v.Ratio.ToString("0.#", CultureInfo.InvariantCulture) + "x";
        }
    }
}
