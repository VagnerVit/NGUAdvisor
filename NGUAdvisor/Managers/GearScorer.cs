using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // Phase 1 of the native gear optimizer (route C3): a faithful C# port of the gear-optimizer's SCORING
    // math (score_vals / get_raw_vals / hardcap from its util.js). Pure and game-independent so it can be
    // validated against the website's JS as an oracle before it is fed live game item data.
    //
    // An "objective" (their "factor") is a list of stat names + optional exponents. The score is the product
    // of each stat's multiplier (raw total / 100), each raised to its exponent. Higher = better.
    public static class GearScorer
    {
        // One equipped item: its per-stat bonus values, and whether it occupies a weapon slot (offhand math).
        public class Item
        {
            public Dictionary<string, double> Stats;
            public bool IsWeapon;
            public Item() { Stats = new Dictionary<string, double>(StringComparer.Ordinal); }
        }

        // Stats that accumulate from 0 (the item bonuses ARE the whole multiplier); everything else from 100%.
        private static bool BaseZero(string stat) => stat == "Respawn" || stat == "Power" || stat == "Toughness";

        // The starting total for a stat before any item contributes. Exposed so GearOptimizer's inner-loop
        // scorer starts from the same baseline as GetRawVals instead of restating the rule.
        public static double BaseValue(string stat) => BaseZero(stat) ? 0.0 : 100.0;

        // The game floors the respawn factor at 0.2 (decomp AdventureController.respawnTime and
        // NGUController.respawnBonus, both: factor = 1 - bonuses[Respawn], clamped up to 0.2), so a gear
        // Respawn total past 80% buys NOTHING IN GAME. GameGearAdapter feeds displayed percents, so the
        // threshold is 80 here.
        public const double RespawnCapPercent = 80.0;

        // A stat's maximum SCOREABLE total. This is a GAME THRESHOLD, not a scoring preference: scoring
        // Respawn linearly told the search that the 81st point was worth as much as the first, and it
        // paid real accessory slots for it. Only Respawn has one — the cap belongs to the stat, so it
        // must never be widened into a per-objective knob.
        //
        // DELIBERATE DIVERGENCE from the reference optimizer, which scores Respawn linearly with no
        // floor (gear-optimizer-comparison.md §Respawn cap). It is also NOT the site's `hardcap`, which
        // clamps relative to the nude total; this one is absolute, because the game's floor is.
        public static double CapValue(string stat) =>
            stat == "Respawn" ? RespawnCapPercent : double.PositiveInfinity;

        // Port of get_raw_vals. `equip` is in slot order (weapons first: 1st weapon = mainhand, 2nd = offhand).
        // offhandPercent is the offhand weapon's contribution (0..100).
        public static double[] GetRawVals(IReadOnlyList<Item> equip, IReadOnlyList<string> stats, double offhandPercent)
        {
            var vals = new double[stats.Count];
            for (int i = 0; i < stats.Count; i++)
            {
                var stat = stats[i];
                vals[i] = BaseZero(stat) ? 0.0 : 100.0;
                bool mainhand = true;
                foreach (var item in equip)
                {
                    if (item == null) continue;
                    double val = 0.0;
                    bool hasStat = item.Stats != null && item.Stats.TryGetValue(stat, out val);
                    if (item.IsWeapon)
                    {
                        // Flip mainhand on the FIRST weapon regardless of whether it carries this stat,
                        // so an offhand-only stat is correctly discounted by offhandPercent (matches the JS oracle,
                        // where every item carries every stat as 0 and the first weapon always flips mainhand).
                        if (mainhand) mainhand = false;
                        else if (hasStat) val *= offhandPercent / 100.0;
                    }
                    if (!hasStat) continue;
                    if (double.IsNaN(val)) continue;
                    vals[i] += val;
                }
                double cap = CapValue(stat);
                if (vals[i] > cap) vals[i] = cap;
            }
            return vals;
        }

        // Port of score_vals: product of (val/100)^exponent. exponents may be null (all weight 1).
        public static double ScoreVals(double[] vals, IReadOnlyList<double> exponents)
        {
            double res = 1.0;
            for (int i = 0; i < vals.Length; i++)
            {
                double v = vals[i] / 100.0;
                if (exponents != null && exponents.Count > i)
                    v = Math.Pow(v, exponents[i]);
                res *= v;
            }
            return res;
        }

        public static double ScoreRaw(IReadOnlyList<Item> equip, IReadOnlyList<string> stats, IReadOnlyList<double> exponents, double offhandPercent)
            => ScoreVals(GetRawVals(equip, stats, offhandPercent), exponents);
    }
}
