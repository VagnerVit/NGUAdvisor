using System;

namespace NGUAdvisor.Managers
{
    // What ITOPOD pays per second, in all four currencies it actually produces.
    //
    // The pod used to enter the farm comparison as a single boost-points-per-second number computed
    // at one floor, from the regular attack alone. Two things were wrong with that:
    //
    //  1. The floor is not one number. ITOPODManager.OptimizeFloor re-picks it between every pair of
    //     kills from whichever move is off cooldown, so the yield is an average over the rotation
    //     (ITOPODManager.ProfileForMode).
    //  2. Boosts are the currency that saturates SOONEST. The ladder index stops moving at tier 24
    //     (floor 1150) and AP bottoms out at tier 20 (floor 950), while EXP keeps growing
    //     quadratically in the tier and PP linearly in the floor, on every kill, to floor 1600.
    //     Pricing the pod on boosts alone makes everything above floor 1150 look like a plateau.
    //
    // Each rate is priced per rotation slice and weighted by that slice's share of kills, because
    // every one of these rewards is a step function of the floor.
    public static class ItopodFarmAdvisor
    {
        public class Rates
        {
            public bool Known;
            public int CombatMode;
            public double KillsPerSecond;
            public int DefaultFloor;
            public int PeakFloor;
            public double BoostPerSecond;   // boost points, priced through BoostSinks
            public double PpPerSecond;      // perk points
            // Arbitrary points. Computed because it is cheap and true, but NOT a decision input and
            // not displayed: AP is always exactly 1 per award, so it cannot discriminate between
            // floors the way PP and EXP do.
            public double ApPerSecond;
            public double ExpPerSecond;     // raw EXP award, before addExp's own bonuses
        }

        // Per-kill rewards come from the game's own LootDrop and ItopodPerkController methods; ItopodRewards'
        // pure copies exist for the tests and the floor-climb planner.
        internal static double PpPerKill(ItopodPerkController perks, int floor, double ppBonus)
            => perks.baseProgressGained(Math.Max(0, floor)) * ppBonus / perks.pointThreshold();

        internal static double ApPerKill(LootDrop loot, int floor)
            => 1.0 / loot.killsPerAP(loot.itopodTier(floor));

        internal static double ExpPerKill(LootDrop loot, int floor)
        {
            int tier = loot.itopodTier(floor);
            return tier < 1 ? 0.0 : (double)loot.itopodEXPAwarded(tier) / loot.killsPerEXP(tier);
        }

        // Without sinks the boost component cannot be priced, so it stays zero — for callers that
        // only want PP/AP/EXP and should not pay for a BoostSinks.Current() snapshot.
        public static Rates ForMode(int combatMode) => ForMode(combatMode, null);

        public static Rates ForMode(int combatMode, BoostSinks.Sinks sinks)
        {
            Rates r = new Rates { CombatMode = combatMode };
            try
            {
                ITOPODManager.Profile p = ITOPODManager.ProfileForMode(combatMode);
                if (!p.Known || p.Slices == null || p.Slices.Length == 0) return r;

                ItopodPerkController perks = Main.Character.adventureController.itopod;
                LootDrop loot = Main.Character.adventureController.lootDrop;
                // usePills: false — the pill multiplier only applies while buffedKills remains, so
                // it is a burst, not a farm rate.
                double ppBonus = perks.totalPPBonus(false);

                double boost = 0.0, pp = 0.0, ap = 0.0, exp = 0.0;
                foreach (ITOPODManager.RotationSlice s in p.Slices)
                {
                    if (s.Fraction <= 0.0) continue;
                    int tier = loot.itopodTier(s.Floor);
                    if (sinks != null)
                        boost += s.Fraction * ItopodRewards.BoostDropChance
                               * BoostSinks.ValueOfDrop(ItopodRewards.BoostLadderIndex(tier), sinks);
                    pp += s.Fraction * PpPerKill(perks, s.Floor, ppBonus);
                    ap += s.Fraction * ApPerKill(loot, s.Floor);
                    exp += s.Fraction * ExpPerKill(loot, s.Floor);
                }

                r.Known = true;
                r.KillsPerSecond = p.KillsPerSecond;
                r.DefaultFloor = p.DefaultFloor;
                r.PeakFloor = p.PeakFloor;
                r.BoostPerSecond = boost * p.KillsPerSecond;
                r.PpPerSecond = pp * p.KillsPerSecond;
                r.ApPerSecond = ap * p.KillsPerSecond;
                r.ExpPerSecond = exp * p.KillsPerSecond;
            }
            catch (Exception e) { Main.LogDebug($"ItopodFarmAdvisor.ForMode({combatMode}): {e.Message}"); }
            return r;
        }

        // The mode that maximizes the currency the caller cares about. Modes worth comparing are the
        // same two BoostFarmAdvisor uses: Idle and Offensive.
        public static Rates Best(Func<Rates, double> currency, BoostSinks.Sinks sinks)
        {
            Rates best = new Rates();
            double bestValue = -1.0;
            int[] modes = CombatHelpers.RegularAttackUnlocked() ? new[] { 0, 3 } : new[] { 0 };
            foreach (int mode in modes)
            {
                Rates candidate = ForMode(mode, sinks);
                if (!candidate.Known) continue;
                double value = currency(candidate);
                if (value > bestValue) { bestValue = value; best = candidate; }
            }
            return best;
        }
    }
}
