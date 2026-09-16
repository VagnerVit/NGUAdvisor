using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // The challenge reward gates + the ranking, split out Unity-free so they can be shape-tested
    // (same reason TitanTables left OptimizationAdvisor). ChallengeAdvisor supplies the live
    // completion counts; everything here is table and arithmetic.
    //
    // NO OPINION TABLE: every row is a gate THIS codebase (or the decompiled game) already reads
    // somewhere else, `Source` names that reader, and the ranking key is the multiplier the game
    // applies — not an editorial ordering. Rewards with no multiplier rank after the numbered ones.
    public static class ChallengeGates
    {
        public struct Gate
        {
            public string Key;          // ChallengeOverlay/ChallengeDetector key
            public string Name;         // display name
            public int Needed;          // completions the gate requires
            public string Reward;       // what clearing it buys
            public double Multiplier;   // the game's factor, 0 when the reward is an unlock rather than a rate
            public string Source;       // where this advisor reads the gate
        }

        public static readonly Gate[] All =
        {
            new Gate { Key = "TC",    Name = "Troll Challenge", Needed = 1, Multiplier = 3.0,
                       Reward = "x3 NGU speed (energy and magic)", Source = "NGUBP.cs:152 / NGUAdvisors.cs:88" },
            new Gate { Key = "NOAUG", Name = "No Augs Challenge", Needed = 1, Multiplier = 1.1,
                       Reward = "x1.1 augment speed", Source = "AugmentBP.cs:123 / Extensions.cs:248" },
            new Gate { Key = "TC",    Name = "Troll Challenge", Needed = 5, Multiplier = 0,
                       Reward = "unlocks the Numbers fruit", Source = "SpendPlanner.cs:515" },
            new Gate { Key = "TC",    Name = "Troll Challenge", Needed = 6, Multiplier = 0,
                       Reward = "unlocks the 8th ritual (Turn Yourself Inside Out)", Source = "AllBloodMagicController.ritualsUnlocked (decomp)" },
            new Gate { Key = "TC",    Name = "Troll Challenge", Needed = 7, Multiplier = 0,
                       Reward = "keeps the golden beard/digger", Source = "BeardManager.cs:58 / OptimizationAdvisor.cs:1101" },
        };

        // The unmet gate worth running next: biggest game multiplier first, then the nearest rung of the
        // ladder. A key missing from `completions` is a challenge we cannot read — skipped, not guessed.
        public static Gate? Next(IDictionary<string, int> completions)
        {
            if (completions == null) return null;
            Gate? best = null;
            foreach (Gate g in All)
            {
                int have;
                if (!completions.TryGetValue(g.Key, out have)) continue;
                if (have >= g.Needed) continue;
                if (best == null) { best = g; continue; }
                Gate b = best.Value;
                bool better = g.Multiplier > b.Multiplier
                    || (g.Multiplier == b.Multiplier && g.Needed < b.Needed);
                if (better) best = g;
            }
            return best;
        }
    }
}
