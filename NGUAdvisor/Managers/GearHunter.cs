using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // GEAR HUNT (user feature 2026-07-11): camp a user-chosen stage for its drops — the manual
    // tool for gear sniping and item leveling in newly reached zones. Two halves:
    //  - ZONE: the picked stage outranks the automatic gear/boost farms (AdvisorApply.ApplyZones);
    //    mode locks (titan/gold/quest/ygg) still take precedence and restore normally.
    //  - GEAR: a hybrid "Loot Hunter" loadout — the user curates an ACCESSORY POOL (Drop Chance /
    //    Respawn pieces, Loadouts › Loot Hunter); the advisor equips the best N of the pool
    //    (N = accessory slots) and fills every non-accessory slot with the optimizer's best
    //    Power/Toughness so kills keep landing (AdvisorApply.ApplyGearRefresh).
    public static class GearHunter
    {
        public static bool Active =>
            Main.Settings != null && Main.Settings.GearHuntEnabled && Main.Settings.GearHuntZone >= 0;

        public static bool ZoneReachable()
        {
            try { return Main.Settings.GearHuntZone <= ZoneHelpers.GetMaxReachableZone(false); }
            catch { return false; }
        }

        // Loot value of one accessory: drops/hour ~ (1 + DC) x kill rate (respawn cut). Both are
        // scored from base 100 per point ON PURPOSE — GearScorer treats Respawn as base-zero, so a
        // product objective would let ANY respawn item outrank ALL drop-chance items (the base-zero
        // explosion the "Adventure" objective comment documents).
        private static double LootScore(GearScorer.Item it)
        {
            double dc = 0, rs = 0;
            if (it?.Stats != null)
            {
                it.Stats.TryGetValue(GearObjectives.Stat.DropChance, out dc);
                it.Stats.TryGetValue(GearObjectives.Stat.Respawn, out rs);
            }
            return (100.0 + dc) * (100.0 + rs);
        }

        // Best OWNED copy of each accessory id (dupes exist at different levels). want == null
        // scans EVERYTHING — the inventory-wide fallback; a set restricts to the pool (entries not
        // in the inventory yet — future drops — simply don't resolve).
        private static Dictionary<int, GearScorer.Item> OwnedAccessories(HashSet<int> want)
        {
            var bestScore = new Dictionary<int, double>();
            var items = new Dictionary<int, GearScorer.Item>();

            void Consider(Equipment e)
            {
                if (e == null || e.id == 0 || e.type != part.Accessory) return;
                if (want != null && !want.Contains(e.id)) return;
                var it = GameGearAdapter.BuildItem(e, false);
                double s = LootScore(it);
                if (!bestScore.TryGetValue(e.id, out var old) || s > old) { bestScore[e.id] = s; items[e.id] = it; }
            }

            var inv = Main.Character.inventory;
            if (inv.accs != null) foreach (var a in inv.accs) Consider(a);
            if (inv.inventory != null) foreach (var e in inv.inventory) Consider(e);
            return items;
        }

        private static double StatOf(GearScorer.Item it, string stat)
        {
            double v = 0;
            if (it?.Stats != null) it.Stats.TryGetValue(stat, out v);
            return v;
        }

        // ---- AUTO mode = a real optimizer pass over the pool (user request): the same greedy-fill
        // + local-swap the gear optimizer uses for accessories, restricted to the pool and scored
        // at SET level with the actual drops/hour shape:
        //     score = (100 + ΣDC) / cycleTime,   cycleTime = attackSec + respawnSec(set)
        //     respawnSec(set) = nonGearRespawn x max(0.2, 1 - ΣRespawn/100)
        // The 0.2 floor is the game's own hard cap (decomp AdventureController.respawnTime:
        // gear respawn factor = 1 - bonuses[Respawn], floored at 0.2) — respawn stacked past 80%
        // total reduction is wasted, and the set score knows it, so DC and Respawn trade off
        // honestly instead of by per-item rank. ----

        private const double AttackSec = 1.0;   // idle attack share of the kill cycle (relative weight)

        // The respawn seconds that are NOT from gear (NGU/clock/perk/wish factors) — constant
        // across candidate sets, derived from the live total by dividing out the current gear factor.
        private static double NonGearRespawnSec()
        {
            try
            {
                var c = Main.Character;
                double frac = c.inventoryController.bonuses[specType.Respawn];
                double gearFactor = Math.Max(0.2, 1.0 - frac);
                return c.adventureController.respawnTime() / gearFactor;
            }
            catch { return 3.5; }
        }

        private static List<int> OptimizeAccessorySubset(Dictionary<int, GearScorer.Item> pool, int slots, out double sumDc, out double sumRs)
        {
            var picked = new List<int>();
            double dc = 0, rs = 0;
            double nonGear = NonGearRespawnSec();

            double Score(double d, double r) =>
                (100.0 + d) / (AttackSec + nonGear * Math.Max(0.2, 1.0 - r / 100.0));

            // Greedy fill: each slot takes the candidate with the best marginal set-score gain;
            // a candidate adding nothing (no DC, no respawn past the floor) never improves -> stop,
            // leaving the slot for the P/T top-up.
            while (picked.Count < slots)
            {
                int best = 0; double bs = Score(dc, rs);
                foreach (var kv in pool)
                {
                    if (picked.Contains(kv.Key)) continue;
                    double s = Score(dc + StatOf(kv.Value, GearObjectives.Stat.DropChance),
                                     rs + StatOf(kv.Value, GearObjectives.Stat.Respawn));
                    if (s > bs) { bs = s; best = kv.Key; }
                }
                if (best == 0) break;
                picked.Add(best);
                dc += StatOf(pool[best], GearObjectives.Stat.DropChance);
                rs += StatOf(pool[best], GearObjectives.Stat.Respawn);
            }

            // Local swap until stable.
            for (int iter = 0; iter < 20; iter++)
            {
                bool improved = false;
                for (int i = 0; i < picked.Count; i++)
                {
                    var curIt = pool[picked[i]];
                    double dBase = dc - StatOf(curIt, GearObjectives.Stat.DropChance);
                    double rBase = rs - StatOf(curIt, GearObjectives.Stat.Respawn);
                    int bestId = picked[i]; double bs = Score(dc, rs);
                    foreach (var kv in pool)
                    {
                        if (picked.Contains(kv.Key)) continue;
                        double s = Score(dBase + StatOf(kv.Value, GearObjectives.Stat.DropChance),
                                         rBase + StatOf(kv.Value, GearObjectives.Stat.Respawn));
                        if (s > bs) { bs = s; bestId = kv.Key; }
                    }
                    if (bestId != picked[i])
                    {
                        picked[i] = bestId;
                        dc = dBase + StatOf(pool[bestId], GearObjectives.Stat.DropChance);
                        rs = rBase + StatOf(pool[bestId], GearObjectives.Stat.Respawn);
                        improved = true;
                    }
                }
                if (!improved) break;
            }

            sumDc = dc; sumRs = rs;
            return picked;
        }

        // The hybrid loadout. Empty array = nothing resolvable (caller skips this pass).
        // Runs the optimizer — main thread only, and callers should throttle.
        public static int[] ResolveLoadout(out string summary)
        {
            summary = "";
            try
            {
                var adv = GearOptimizer.FindObjective("Adventure");
                var best = adv != null ? GearOptimizer.Optimize(adv) : null;
                if (best == null) return new int[0];

                var ids = new List<int>();
                foreach (var id in new[] { best.MainWeapon, best.OffWeapon, best.Head, best.Chest, best.Legs, best.Boots })
                    if (id > 0) ids.Add(id);

                int slots = 0;
                try { slots = Math.Max(0, Main.InventoryController.accessorySpaces()); } catch { }
                var poolIds = (Main.Settings.LootHunterAccessories ?? new int[0]).Where(x => x > 0).ToArray();
                var pool = OwnedAccessories(poolIds.Length > 0 ? new HashSet<int>(poolIds) : null);
                bool poolIsAll = poolIds.Length == 0;   // empty pool: the whole inventory is the pool

                // Per-type QUOTAS (user rule): Respawn count first (ranked by Respawn), then Drop
                // Chance count (ranked by DC). Both 0 = auto (optimizer subset search). The pool is
                // the PREFERRED list — quota shortfalls fall back to the whole owned inventory
                // (user-reported: a one-item pool made the hunt look inert; the demand is the quota).
                int wantR = Math.Max(0, Main.Settings.LootHunterRespawnCount);
                int wantD = Math.Max(0, Main.Settings.LootHunterDropCount);
                var picked = new List<int>();
                string poolNote;
                if (wantR == 0 && wantD == 0)
                {
                    picked = OptimizeAccessorySubset(pool, slots, out var sumDc, out var sumRs);
                    poolNote = $"{picked.Count} acc optimized over {(poolIsAll ? "inventory" : "pool")} (+{sumDc:0}% DC, -{Math.Min(sumRs, 80):0}% respawn)";
                }
                else
                {
                    int gotR = 0, gotD = 0;
                    void FillQuota(Dictionary<int, GearScorer.Item> from, string stat, int want, ref int got)
                    {
                        foreach (var kv in from.Where(kv => !picked.Contains(kv.Key) && StatOf(kv.Value, stat) > 0)
                                           .OrderByDescending(kv => StatOf(kv.Value, stat)))
                        {
                            if (got >= want || picked.Count >= slots) break;
                            picked.Add(kv.Key); got++;
                        }
                    }
                    FillQuota(pool, GearObjectives.Stat.Respawn, wantR, ref gotR);
                    FillQuota(pool, GearObjectives.Stat.DropChance, wantD, ref gotD);
                    bool fellBack = false;
                    if (!poolIsAll && (gotR < wantR || gotD < wantD))
                    {
                        var all = OwnedAccessories(null);
                        FillQuota(all, GearObjectives.Stat.Respawn, wantR, ref gotR);
                        FillQuota(all, GearObjectives.Stat.DropChance, wantD, ref gotD);
                        fellBack = true;
                    }
                    poolNote = $"{gotR}/{wantR} respawn + {gotD}/{wantD} DC"
                        + (poolIsAll ? " from inventory" : fellBack ? " (pool + inventory)" : " from pool");
                }

                // Remaining slots: top up with the optimizer's best P/T accessories.
                foreach (var a in best.Accessories)
                    if (picked.Count < slots && a > 0 && !picked.Contains(a)) picked.Add(a);
                ids.AddRange(picked);

                int asked = (Main.Settings.LootHunterAccessories ?? new int[0]).Count(x => x > 0);
                summary = poolNote + " + best P/T gear"
                    + (asked > pool.Count ? $" · {asked - pool.Count} pool item(s) not owned yet" : "");
                return ids.Distinct().ToArray();
            }
            catch (Exception e) { Main.LogDebug($"GearHunter resolve: {e.Message}"); return new int[0]; }
        }
    }
}
