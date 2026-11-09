using System;

namespace NGUAdvisor.Managers
{
    // What one seed buys when it raises a fruit's max tier by one (decomp FruitController.upgrade and
    // tierFactor). The fruit's effect scales with ceil(tier^1.5), the next tier costs
    // baseSeedCost * ceil((tier+1)^2), so the return per seed falls as the tier rises and is only
    // meaningful below the tier cap (10, or 24 after Troll x3).
    public static class FruitUpgradeValue
    {
        public static long TierFactor(int tier) => (long)Math.Ceiling(Math.Pow(tier, 1.5));

        public static long UpgradeCost(long baseSeedCost, int currentMaxTier)
            => baseSeedCost * (long)Math.Ceiling(Math.Pow(currentMaxTier + 1, 2.0));

        // Relative growth of the fruit's effect from the next tier, per seed. 0 at or above the cap or
        // when the fruit is not unlocked (a locked fruit has no effect to grow).
        public static double RelativeGainPerSeed(long baseSeedCost, int currentMaxTier, int capTier)
        {
            if (currentMaxTier < 1 || currentMaxTier >= capTier) return 0.0;
            long now = TierFactor(currentMaxTier);
            long next = TierFactor(currentMaxTier + 1);
            long cost = UpgradeCost(baseSeedCost, currentMaxTier);
            return cost <= 0 || now <= 0 ? 0.0 : (double)(next - now) / now / cost;
        }
    }
}
