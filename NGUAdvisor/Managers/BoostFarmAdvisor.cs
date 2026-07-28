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
    // Value model: boost-value per kill = sum(value_i * min(chance_i * dcFactor, 1)) * normal-enemy%.
    // dcFactor = lootFactor() for Normal zones, lootFactor()^(1/3) for Evil+ zones (the game's
    // lootChanceDisplayRooted marks them). Kill cadence is ~equal across one-shottable zones (idle
    // attack + respawn), so ranking per-kill is ranking per-second; only one-shottable (attack >=
    // zone OPower) and boss-unlocked zones compete.
    public static class BoostFarmAdvisor
    {
        private class ZoneBoost
        {
            public int Zone;
            public double[][] Rolls;   // {value, baseChance, chanceCap} — cap 1.0 when unextracted
            public bool Rooted;
        }

        private const double NormalEnemyShare = 0.77;

        private static readonly ZoneBoost[] Table =
        {
            // Zones 0-18: extracted VERBATIM from LootDrop.zoneNDrop (value = boost-tier value,
            // base chance, and the game's per-roll chance CAP — Mathf.Min(cap, chance*lootFactor)).
            // The old table lacked caps AND undervalued mid zones (user-reported: the Almanac ranked
            // Badly Drawn World 56.4 over A Very Strange Place 23.6 while we said the reverse — at
            // high drop chance AVSP saturates at its 0.25 cap while BDW's T7+T8 values keep going).
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
            new ZoneBoost { Zone = 22, Rolls = new[] { new[] { 500.0, 0.0001, 0.08 }, new[] { 1000.0, 0.0001, 0.08 } }, Rooted = true },
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

        // ITOPOD boost tier ladder (itopodDrop): tier index into the 13 boost values, from floor/50.
        private static readonly double[] BoostValues = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000 };

        public struct Verdict
        {
            public bool Known;
            public int BestZone;          // -1000 = ITOPOD
            public string BestName;
            public double BestRate;       // boost-value per kill
            public double ItopodRate;
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

        public static Verdict Analyze()
        {
            var v = new Verdict { BestZone = int.MinValue };
            try
            {
                var c = Main.Character;
                if (c == null) return v;

                double dc = c.lootFactor();
                double dcRoot = Math.Pow(dc, 1.0 / 3.0);
                double attack = c.totalAdvAttack();

                double bestRate = 0;
                int bestZone = -1;
                foreach (var z in Table)
                {
                    try
                    {
                        // Unlocked = boss requirement met (ZoneHelpers.ZoneUnlocks, indexed by zone).
                        if (z.Zone >= ZoneHelpers.ZoneUnlocks.Length || c.bossID <= ZoneHelpers.ZoneUnlocks[z.Zone]) continue;
                        if (ZoneStatHelper.UserOverrides != null && ZoneStatHelper.UserOverrides.TryGetValue(z.Zone, out var st))
                        {
                            if (st.OPower > 0 && attack < st.OPower) continue;   // not one-shottable idle
                        }
                        double factor = z.Rooted ? dcRoot : dc;
                        double rate = 0;
                        foreach (var roll in z.Rolls)
                        {
                            double cap = roll.Length > 2 ? roll[2] : 1.0;
                            rate += roll[0] * Math.Min(roll[1] * factor, cap);
                        }
                        rate *= NormalEnemyShare;
                        if (rate > bestRate)
                        {
                            bestRate = rate;
                            bestZone = z.Zone;
                        }
                    }
                    catch { }
                }

                // ITOPOD at the OPTIMAL floor: tier = floor/50, laddered into the boost-value table.
                double idleAttack = attack * c.idleAttackPower();
                int optFloor = idleAttack > ItopodConstants.FloorHpNormalizer ? (int)Math.Floor(Math.Log(idleAttack / ItopodConstants.FloorHpNormalizer, ItopodConstants.FloorGrowthBase)) : 0;
                int tier = Math.Max(1, Math.Min(optFloor / 50 + 1, 24));
                int idx = tier >= 24 ? 13 : tier >= 18 ? 12 : tier >= 15 ? 11 : tier > 10 ? 10 : tier;
                v.ItopodRate = 0.14 * BoostValues[idx - 1];

                v.Known = true;
                if (bestZone >= 0 && bestRate > v.ItopodRate)
                {
                    v.BestZone = bestZone;
                    v.BestName = ZoneHelpers.ZoneList.TryGetValue(bestZone, out var n) ? n : $"Zone {bestZone}";
                    v.BestRate = bestRate;
                    v.Text = $"Best boost farm: {v.BestName} (~{bestRate:0.##} boost-value/kill vs ITOPOD {v.ItopodRate:0.##})";
                }
                else
                {
                    v.BestZone = -1000;
                    v.BestName = "ITOPOD";
                    v.BestRate = v.ItopodRate;
                    v.Text = $"Best boost farm: ITOPOD (~{v.ItopodRate:0.##} boost-value/kill beats every one-shottable zone)";
                }
                return v;
            }
            catch (Exception e) { Main.LogDebug($"BoostFarmAdvisor: {e.Message}"); return v; }
        }
    }
}
