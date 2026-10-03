using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // The guide's chapter-4 NGU priority (external/ngu-guide .../chapters/chapter-4.md, "NGU Priority"):
    // the first NGU over its bonus-change bar takes the pool; if none clears, Energy splits Adv/DC and
    // Magic splits Ygg/EXP. Augs, Wandoos, Power-α/β and Number are not on the list at all.
    // Ratios are NGUAdvisors' ×/hr bonus change with the whole pool (Respawn as old/new time, so the
    // guide's "<0.95x" reads as > 1/0.95 here).
    public static class NguGuidePriority
    {
        public struct Tier
        {
            public readonly int Id;
            public readonly double Enter;
            public Tier(int id, double enter) { Id = id; Enter = enter; }
        }

        // Same band as NGUAdvisors' 1.05 enter / 1.04 exit: a running lane must clearly fall off.
        public const double ExitBand = 0.01;

        // Energy ids: 2 Respawn, 3 Gold, 4 Adv-α, 6 Drop Chance, 7 Magic NGU, 8 PP.
        public static readonly Tier[] EnergyTiers =
        {
            new Tier(7, 1.05),
            new Tier(8, 1.05),
            new Tier(2, 1.0 / 0.95),
            new Tier(3, 1.2),
        };
        public static readonly int[] EnergyFallback = { 4, 6 };

        // Magic ids: 0 Ygg, 1 EXP, 4 TM, 5 Energy NGU, 6 Adv-β.
        public static readonly Tier[] MagicTiers =
        {
            new Tier(5, 1.05),
            new Tier(6, 1.05),
            new Tier(4, 1.2),
        };
        public static readonly int[] MagicFallback = { 0, 1 };

        // fullPoolRatio(id) is only called for ids in `candidates`.
        public static int[] Hot(bool magic, ICollection<int> candidates, Func<int, double> fullPoolRatio, int[] incumbents)
        {
            foreach (var t in Tiers(magic))
            {
                if (!candidates.Contains(t.Id)) continue;
                bool running = incumbents != null && incumbents.Contains(t.Id);
                if (fullPoolRatio(t.Id) >= (running ? t.Enter - ExitBand : t.Enter))
                    return new[] { t.Id };
            }
            return Fallback(magic).Where(candidates.Contains).ToArray();
        }

        // Leftover lanes, in guide order: lower tiers that also clear their bar, then the fallback pair.
        public static int[] Surplus(bool magic, ICollection<int> candidates, Func<int, double> fullPoolRatio, int[] hot)
        {
            var qualifying = Tiers(magic)
                .Where(t => candidates.Contains(t.Id) && fullPoolRatio(t.Id) >= t.Enter)
                .Select(t => t.Id);
            return qualifying.Concat(Fallback(magic).Where(candidates.Contains))
                .Where(id => !hot.Contains(id))
                .Distinct()
                .ToArray();
        }

        private static Tier[] Tiers(bool magic) => magic ? MagicTiers : EnergyTiers;
        private static int[] Fallback(bool magic) => magic ? MagicFallback : EnergyFallback;
    }
}
