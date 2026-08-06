using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // Boost-farm advisor (Farmer Sanc's Almanac model, constants re-sourced from the CURRENT game):
    // per-zone boost rolls come from the game's own zone tooltips (lootChanceDisplay(chance, avgBoost)
    // in AdventureController — the developer's documentation of the drop code) plus direct extraction
    // for the early zones; ITOPOD from itopodDrop: flat 14% chance (NOT drop-chance scaled), boost
    // tier laddered from floor tier (one tier per 50 floors).
    //
    // Rate model — boost POINTS PER SECOND, not per kill:
    //
    //   rate = sum_rolls min(chance_i * dcFactor, cap_i) * BoostSinks.ValueOfDrop(tier_i)
    //          * ZoneCadence.NormalKillsPerSecond(zone, mode)
    //
    // dcFactor = lootFactor() for Normal zones, lootFactor()^(1/3) for Evil+ (the game's
    // lootChanceDisplayRooted marks them; verified: zones 20-45 use lootFactorRooted()).
    //
    // Three things this deliberately does NOT simplify any more, because each one changed the
    // ranking:
    //
    //  1. A dropped boost is priced by what it can actually absorb (BoostSinks): overflow past a
    //     gear channel's cap is destroyed, the cube is a sqrt-diminishing soft sink, and the
    //     recycling chain adds the lower tiers back. A flat "tier value" over-priced high tiers.
    //  2. Kill cadence is measured per zone (ZoneCadence) from the game's own spawn table, per
    //     combat mode, including multi-hit enemies, enemy regen and paralyzer downtime. The old
    //     "cadence is ~equal across one-shottable zones" shortcut both hid the ~2x idle/offensive
    //     gap and excluded every zone that needs a second swing.
    //  3. Zones are gated on killing NORMAL enemies (the only enemyType that rolls boosts) and on
    //     surviving the zone — not on OPower, which is calibrated on the boss.
    public static class BoostFarmAdvisor
    {
        private class ZoneBoost
        {
            public int Zone;
            public double[][] Rolls;   // {value, baseChance, chanceCap} — cap 1.0 when unextracted
            public bool Rooted;
        }

        private static readonly ZoneBoost[] Table =
        {
            // Zones 0-18: extracted VERBATIM from LootDrop.zoneNDrop (value = boost-tier value,
            // base chance, and the game's per-roll chance CAP — Mathf.Min(cap, chance*lootFactor)).
            // The old table lacked caps AND undervalued mid zones (user-reported: the Almanac ranked
            // Badly Drawn World 56.4 over A Very Strange Place 23.6 while we said the reverse — at
            // high drop chance AVSP saturates at its 0.25 cap while BDW's T7+T8 values keep going).
            // Zones 0 and 1 fire a single uncapped tier-1 roll — dominated by zone 2 in practice,
            // but they belong here so the table is the whole truth rather than the useful part.
            new ZoneBoost { Zone = 0, Rolls = new[] { new[] { 1.0, 0.15, 1.0 } } },
            new ZoneBoost { Zone = 1, Rolls = new[] { new[] { 1.0, 0.15, 1.0 } } },
            new ZoneBoost { Zone = 2, Rolls = new[] { new[] { 1.0, 0.12, 1.0 }, new[] { 2.0, 0.08, 1.0 } } },
            new ZoneBoost { Zone = 3, Rolls = new[] { new[] { 1.0, 0.13, 1.0 }, new[] { 2.0, 0.12, 1.0 } } },
            new ZoneBoost { Zone = 4, Rolls = new[] { new[] { 5.0, 0.08, 1.0 }, new[] { 2.0, 0.08, 1.0 } } },
            new ZoneBoost { Zone = 5, Rolls = new[] { new[] { 5.0, 0.015, 1.0 }, new[] { 2.0, 0.06, 1.0 } } },
            new ZoneBoost { Zone = 7, Rolls = new[] { new[] { 5.0, 0.03, 0.15 }, new[] { 10.0, 0.03, 0.15 } } },
            new ZoneBoost { Zone = 9, Rolls = new[] { new[] { 10.0, 0.07, 0.15 }, new[] { 20.0, 0.07, 0.15 } } },
            new ZoneBoost { Zone = 10, Rolls = new[] { new[] { 10.0, 0.06, 0.2 }, new[] { 20.0, 0.06, 0.2 } } },
            new ZoneBoost { Zone = 12, Rolls = new[] { new[] { 20.0, 0.03, 0.25 }, new[] { 50.0, 0.03, 0.25 } } },
            new ZoneBoost { Zone = 13, Rolls = new[] { new[] { 50.0, 0.011, 0.15 }, new[] { 100.0, 0.011, 0.15 } } },
            new ZoneBoost { Zone = 15, Rolls = new[] { new[] { 50.0, 0.0035, 0.25 }, new[] { 100.0, 0.0035, 0.25 } } },
            new ZoneBoost { Zone = 17, Rolls = new[] { new[] { 100.0, 0.001, 0.2 }, new[] { 200.0, 0.001, 0.2 } } },
            new ZoneBoost { Zone = 18, Rolls = new[] { new[] { 200.0, 0.00012, 0.2 }, new[] { 500.0, 0.00012, 0.2 } } },
            // Evil-era zones (20+): chance + per-roll cap sourced VERBATIM from LootDrop.zone{N}Drop
            // (Mathf.Min(chance*lootFactorRooted, cap)); Rooted=Evil (drop chance cube-rooted). Each zone fires
            // TWO boost rolls with identical chance/cap (roll 2 = next boost tier up, makeLoot id+1) — both are
            // modelled now. VALUE FIX (finding #21, larger than first scoped): the old single-roll `value` field
            // held the tooltip's display-cap PERCENT (lootChanceDisplayRooted's 2nd arg, e.g. 10 for zone 20),
            // NOT a boost value — so Evil zones were undervalued ~20-1000x vs ITOPOD and effectively never won.
            // Values below are the real boost ladder {200,500,1000,2000,5000,10000}, keyed by the makeLoot item
            // id (id 8="Power Boost 200", id 9=500, ... verified LootDrop.zone{N}Drop + ItemNameDesc). Zones 36+
            // roll 1 is already the 10K ceiling so roll 2 repeats it (id stays 13/26/39). Zone 29 chance is
            // 1.5E-05 in the DROP CODE — the in-game tooltip's 1.5E-06 is a typo (verified LootDrop.zone29Drop).
            // NOTE: this materially changes Evil boost-farm vs ITOPOD recommendations — validate in-game.
            new ZoneBoost { Zone = 20, Rolls = new[] { new[] { 200.0, 0.00055, 0.1 }, new[] { 500.0, 0.00055, 0.1 } }, Rooted = true },
            new ZoneBoost { Zone = 21, Rolls = new[] { new[] { 200.0, 0.00012, 0.1 }, new[] { 500.0, 0.00012, 0.1 } }, Rooted = true },
            // Zone 22's two rolls do NOT share a cap — zone22Drop caps roll 1 at 0.08f and roll 2 at
            // 0.06f. We had 0.08 on both, which over-priced PPPL by up to 25% at high drop chance
            // (the 1000-value roll is two thirds of the zone's total). The Almanac had this right.
            new ZoneBoost { Zone = 22, Rolls = new[] { new[] { 500.0, 0.0001, 0.08 }, new[] { 1000.0, 0.0001, 0.06 } }, Rooted = true },
            new ZoneBoost { Zone = 24, Rolls = new[] { new[] { 1000.0, 5E-05, 0.07 }, new[] { 2000.0, 5E-05, 0.07 } }, Rooted = true },
            new ZoneBoost { Zone = 25, Rolls = new[] { new[] { 1000.0, 3E-05, 0.08 }, new[] { 2000.0, 3E-05, 0.08 } }, Rooted = true },
            new ZoneBoost { Zone = 27, Rolls = new[] { new[] { 1000.0, 2.2E-05, 0.09 }, new[] { 2000.0, 2.2E-05, 0.09 } }, Rooted = true },
            new ZoneBoost { Zone = 28, Rolls = new[] { new[] { 2000.0, 1.8E-05, 0.1 }, new[] { 5000.0, 1.8E-05, 0.1 } }, Rooted = true },
            new ZoneBoost { Zone = 29, Rolls = new[] { new[] { 2000.0, 1.5E-05, 0.1 }, new[] { 5000.0, 1.5E-05, 0.1 } }, Rooted = true },
            new ZoneBoost { Zone = 31, Rolls = new[] { new[] { 2000.0, 6E-07, 0.15 }, new[] { 5000.0, 6E-07, 0.15 } }, Rooted = true },
            new ZoneBoost { Zone = 32, Rolls = new[] { new[] { 5000.0, 4E-07, 0.1 }, new[] { 10000.0, 4E-07, 0.1 } }, Rooted = true },
            new ZoneBoost { Zone = 33, Rolls = new[] { new[] { 5000.0, 2.5E-07, 0.15 }, new[] { 10000.0, 2.5E-07, 0.15 } }, Rooted = true },
            new ZoneBoost { Zone = 35, Rolls = new[] { new[] { 5000.0, 1E-07, 0.15 }, new[] { 10000.0, 1E-07, 0.15 } }, Rooted = true },
            new ZoneBoost { Zone = 36, Rolls = new[] { new[] { 10000.0, 6E-08, 0.15 }, new[] { 10000.0, 6E-08, 0.15 } }, Rooted = true },
            new ZoneBoost { Zone = 37, Rolls = new[] { new[] { 10000.0, 4E-08, 0.15 }, new[] { 10000.0, 4E-08, 0.15 } }, Rooted = true },
            new ZoneBoost { Zone = 39, Rolls = new[] { new[] { 10000.0, 2.5E-08, 0.16 }, new[] { 10000.0, 2.5E-08, 0.16 } }, Rooted = true },
            new ZoneBoost { Zone = 40, Rolls = new[] { new[] { 10000.0, 2E-08, 0.17 }, new[] { 10000.0, 2E-08, 0.17 } }, Rooted = true },
            new ZoneBoost { Zone = 41, Rolls = new[] { new[] { 10000.0, 1.6E-08, 0.17 }, new[] { 10000.0, 1.6E-08, 0.17 } }, Rooted = true },
            new ZoneBoost { Zone = 43, Rolls = new[] { new[] { 10000.0, 1E-08, 0.17 }, new[] { 10000.0, 1E-08, 0.17 } }, Rooted = true },
        };

        public struct Verdict
        {
            public bool Known;
            public int BestZone;          // -1000 = ITOPOD
            public string BestName;
            public double BestRate;       // boost points per SECOND, at BestMode
            public double ItopodRate;     // ditto, for ITOPOD at its optimal floor
            public int BestMode;          // Settings.CombatMode value that produced BestRate
            public double RateAtCurrentMode;
            public string Text;
        }

        // Farm Best Boost demand gate: boosts only pay while something consumes them — equipped or
        // priority-listed gear still missing boosts, or an Infinity Cube under its softcap. The
        // game CLAMPS effective cube power/toughness at base + gear attack/defense (decompile:
        // InventoryController.cubePower()/cubeToughness()), so feeding a capped cube adds nothing
        // until other stats grow — ITOPOD PP/EXP beats boost farming then.
        public static bool BoostDemandExists(out string why)
        {
            try
            {
                var c = Main.Character;
                var ic = Main.InventoryController;
                if (c.inventory.cubePower < ic.cubePowerSoftcap()) { why = "cube power under softcap"; return true; }
                if (c.inventory.cubeToughness < ic.cubeToughnessSoftcap()) { why = "cube toughness under softcap"; return true; }

                bool NeedsBoosts(int id)
                {
                    var slot = LoadoutManager.FindItemSlot(id);
                    return slot != null && slot.equipment.GetNeededBoosts().Total() > 0;
                }
                foreach (var id in LoadoutManager.CurrentGearIds())
                    if (NeedsBoosts(id)) { why = $"equipped {Main.ItemNameNice(id)} needs boosts"; return true; }
                var prio = Main.Settings?.PriorityBoosts;
                if (prio != null)
                    foreach (var id in prio)
                        if (NeedsBoosts(id)) { why = $"{Main.ItemNameNice(id)} needs boosts"; return true; }

                why = "cube at softcap, no gear needs boosts";
                return false;
            }
            catch (Exception e)
            {
                Main.LogDebug($"BoostDemand: {e.Message}");
                why = "demand unknown";
                return true;   // fail open: keep the classic always-boost behavior
            }
        }

        // Boost points a single NORMAL kill in this zone is expected to yield, priced through the
        // live sinks. The type split (1/3 Power, 1/3 Toughness, 1/3 Special) and the recycling chain
        // both live in BoostSinks.ValueOfDrop.
        private static double ValuePerNormalKill(ZoneBoost z, double dc, double dcRoot, BoostSinks.Sinks sinks)
        {
            double factor = z.Rooted ? dcRoot : dc;
            double total = 0;
            foreach (var roll in z.Rolls)
            {
                double cap = roll.Length > 2 ? roll[2] : 1.0;
                double p = BoostValueMath.RollProbability(roll[1], factor, cap);
                int tier = BoostValueMath.TierOfValue(roll[0]);
                total += p * BoostSinks.ValueOfDrop(tier, sinks);
            }
            return total;
        }

        // The combat modes worth comparing for a farm park: Idle and Offensive. Snipe (1) waits out
        // pre-casts and Defensive (2) inserts WaitFor stalls, so neither can beat Offensive on
        // throughput; mode 4 is not reachable from the Adventure dropdown.
        private static int[] CandidateModes()
            => CombatHelpers.RegularAttackUnlocked() ? new[] { 0, 3 } : new[] { 0 };

        // ITOPOD boost points per second at the floor the given mode can hold. The floor -> tier
        // ladder and its bends are ITOPODManager's knowledge, not ours.
        private static double ItopodRate(int combatMode, BoostSinks.Sinks sinks)
        {
            int floor = ITOPODManager.OptimalFloorForMode(combatMode);
            int tier = Math.Max(1, Math.Min(floor / 50 + 1, 24));
            int idx = tier >= 24 ? 13 : tier >= 18 ? 12 : tier >= 15 ? 11 : tier > 10 ? 10 : tier;
            double perKill = 0.14 * BoostSinks.ValueOfDrop(idx, sinks);

            // At its optimal floor every ITOPOD enemy dies to one swing, and the pod has no bosses,
            // so the cycle is the bare spawn-plus-swing loop.
            double cycle = BoostValueMath.CycleSeconds(ZoneCadence.IsIdle(combatMode),
                CombatHelpers.BaseRespawnTime(), ZoneCadence.SwingSeconds(combatMode), 1.0);
            return cycle > 0 ? perKill / cycle : 0;
        }

        public static Verdict Analyze()
        {
            var v = new Verdict { BestZone = int.MinValue };
            try
            {
                var c = Main.Character;
                if (c == null) return v;

                double dc = c.lootFactor();
                double dcRoot = Math.Pow(dc, 1.0 / 3.0);
                BoostSinks.Sinks sinks = BoostSinks.Current();
                int[] modes = CandidateModes();
                int currentMode = Main.Settings?.CombatMode ?? 0;

                double bestRate = 0;
                int bestZone = -1;
                int bestMode = currentMode;
                double bestAtCurrentMode = 0;

                foreach (var z in Table)
                {
                    try
                    {
                        // Unlocked = boss requirement met (ZoneHelpers.ZoneUnlocks, indexed by zone).
                        if (z.Zone >= ZoneHelpers.ZoneUnlocks.Length || c.bossID <= ZoneHelpers.ZoneUnlocks[z.Zone]) continue;

                        double perKill = ValuePerNormalKill(z, dc, dcRoot, sinks);
                        if (perKill <= 0) continue;   // nothing this zone drops can be absorbed

                        foreach (int mode in modes)
                        {
                            ZoneCadence.Estimate est = ZoneCadence.For(z.Zone, mode);
                            if (!est.Known || !est.Killable) continue;
                            if (!ZoneCadence.Survivable(z.Zone, mode, est)) continue;

                            double rate = perKill * est.NormalKillsPerSecond;
                            if (rate > bestRate)
                            {
                                bestRate = rate;
                                bestZone = z.Zone;
                                bestMode = mode;
                            }
                        }
                    }
                    catch (Exception e) { Main.LogDebug($"BoostFarm zone {z.Zone}: {e.Message}"); }
                }

                // Re-price the winner at the mode actually configured, so the UI can say what the
                // current setting costs instead of only what the best one earns.
                if (bestZone >= 0)
                {
                    ZoneCadence.Estimate cur = ZoneCadence.For(bestZone, currentMode);
                    if (cur.Known && cur.Killable && ZoneCadence.Survivable(bestZone, currentMode, cur))
                    {
                        ZoneBoost zb = Array.Find(Table, t => t.Zone == bestZone);
                        if (zb != null)
                            bestAtCurrentMode = ValuePerNormalKill(zb, dc, dcRoot, sinks) * cur.NormalKillsPerSecond;
                    }
                }

                double itopodBest = 0;
                int itopodMode = currentMode;
                foreach (int mode in modes)
                {
                    double r = ItopodRate(mode, sinks);
                    if (r > itopodBest) { itopodBest = r; itopodMode = mode; }
                }
                v.ItopodRate = itopodBest;

                v.Known = true;
                if (bestZone >= 0 && bestRate > itopodBest)
                {
                    v.BestZone = bestZone;
                    v.BestName = ZoneHelpers.ZoneList.TryGetValue(bestZone, out var n) ? n : $"Zone {bestZone}";
                    v.BestRate = bestRate;
                    v.BestMode = bestMode;
                    v.RateAtCurrentMode = bestAtCurrentMode;
                    v.Text = $"Best boost farm: {v.BestName} in {ModeName(bestMode)} (~{bestRate:0.###} boost/s vs ITOPOD {itopodBest:0.###})";
                }
                else
                {
                    v.BestZone = -1000;
                    v.BestName = "ITOPOD";
                    v.BestRate = itopodBest;
                    v.BestMode = itopodMode;
                    v.RateAtCurrentMode = ItopodRate(currentMode, sinks);
                    // Bosses roll no boosts, so Bosses Only makes every adventure zone worth exactly
                    // zero. Say so — otherwise the verdict reads as a drop-chance problem.
                    bool bossOnly = Main.Settings != null && Main.Settings.SnipeBossOnly;
                    v.Text = bossOnly && bestRate <= 0
                        ? $"Best boost farm: ITOPOD in {ModeName(itopodMode)} (~{itopodBest:0.###} boost/s) — Bosses Only zeroes every zone, bosses drop no boosts"
                        : $"Best boost farm: ITOPOD in {ModeName(itopodMode)} (~{itopodBest:0.###} boost/s beats every farmable zone)";
                }
                return v;
            }
            catch (Exception e) { Main.LogDebug($"BoostFarmAdvisor: {e.Message}"); return v; }
        }

        public static string ModeName(int mode)
        {
            switch (mode)
            {
                case 0: return "Idle";
                case 1: return "Snipe";
                case 2: return "Defensive";
                case 3: return "Offensive";
                default: return $"mode {mode}";
            }
        }
    }
}
