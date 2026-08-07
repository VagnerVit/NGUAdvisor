using System;

namespace NGUAdvisor.Managers
{
    /// <summary>
    /// What an ITOPOD kill pays, per floor. Unity-free so tests/NGUAdvisor.Tests can link it.
    ///
    /// Sourced verbatim from LootDrop.itopodDrop / itopodTier / killsPerAP / killsPerEXP /
    /// itopodEXPAwarded and ItopodPerkController.progressGained + pointThreshold().
    ///
    /// The three currencies do NOT scale alike, which is the whole reason this exists:
    ///   boosts saturate at tier 24 (floor 1150) -- the ladder index stops moving
    ///   AP     saturates at tier 20 (floor  950) -- killsPerAP bottoms out at 20
    ///   EXP    grows QUADRATICALLY in tier and never saturates below maxItopodLevel
    ///   PP     grows linearly in the floor itself, on every single kill
    /// A boost-only model therefore sees a plateau above floor 1150 that is not there.
    /// </summary>
    public static class ItopodRewards
    {
        public enum Difficulty { Normal = 0, Evil = 1, Sadistic = 2 }

        // LootDrop.itopodDrop: flat roll, NOT scaled by drop chance.
        public const double BoostDropChance = 0.14;

        // ItopodPerkController.pointThreshold().
        public const double PpThreshold = 1000000.0;

        /// <summary>LootDrop.itopodTier.</summary>
        public static int Tier(int floor)
        {
            if (floor < 0) return 0;
            if (floor >= 2000) return 40;
            return 1 + floor / 50;
        }

        /// <summary>
        /// Boost ladder index (1..13) a kill at this tier drops, with itopodDrop's bends.
        /// Saturates at tier 24, which is floor 1150.
        /// </summary>
        public static int BoostLadderIndex(int tier)
        {
            int t = tier > 0 ? Math.Min(tier, 24) : 1;
            if (t < 1) t = 1;
            if (t >= 24) return 13;
            if (t >= 18) return 12;
            if (t >= 15) return 11;
            if (t > 10) return 10;
            return t;
        }

        /// <summary>LootDrop.killsPerAP. killsPerEXP is the SAME function.</summary>
        public static int KillsPerAp(int tier) => Math.Max(40 - tier, 20);

        /// <summary>
        /// LootDrop.killsPerEXP -- identical to killsPerAP, and both test the same
        /// `enemiesKilled % n == 0` counter, so AP and EXP always land on the SAME kill.
        /// The mode-3 "AP dance" is therefore an EXP dance too: AP is always exactly 1, while
        /// the EXP award is quadratic in the tier reached on that kill.
        /// </summary>
        public static int KillsPerExp(int tier) => KillsPerAp(tier);

        /// <summary>LootDrop.itopodEXPAwarded.</summary>
        public static double ExpAwarded(int tier)
        {
            if (tier < 1) return 0.0;
            if (tier < 3) return tier;
            return (double)(tier - 1) * (tier - 2) + 2.0;
        }

        public static double ApPerKill(int floor)
        {
            int per = KillsPerAp(Tier(floor));
            return per > 0 ? 1.0 / per : 0.0;
        }

        /// <summary>
        /// EXP per kill averaged over the award cycle. itopodDrop gates the EXP award on
        /// `tier >= 1`, so floor &lt; 0 pays nothing.
        /// </summary>
        public static double ExpPerKill(int floor)
        {
            int tier = Tier(floor);
            if (tier < 1) return 0.0;
            int per = KillsPerExp(tier);
            return per > 0 ? ExpAwarded(tier) / per : 0.0;
        }

        /// <summary>
        /// ItopodPerkController.progressGained. The difficulty base dominates on Sadistic
        /// (2000 vs a floor of at most 1600) and is dominated by the floor on Normal (200).
        /// </summary>
        public static double PpProgressPerKill(int floor, Difficulty difficulty, double ppBonus, double sadisticBasePpBonus = 0.0)
        {
            if (floor < 0) floor = 0;
            double bonus = ppBonus < 1.0 ? 1.0 : ppBonus;   // totalPPBonus() clamps at 1
            switch (difficulty)
            {
                case Difficulty.Evil: return (700.0 + floor) * bonus;
                case Difficulty.Sadistic: return (2000.0 + floor + sadisticBasePpBonus) * bonus;
                default: return (200.0 + floor) * bonus;
            }
        }

        /// <summary>Perk points per kill (progress / pointThreshold).</summary>
        public static double PpPerKill(int floor, Difficulty difficulty, double ppBonus, double sadisticBasePpBonus = 0.0)
            => PpProgressPerKill(floor, difficulty, ppBonus, sadisticBasePpBonus) / PpThreshold;
    }
}
