using System;
using System.Collections.Generic;
using UnityEngine;

namespace NGUAdvisor.Managers
{
    // Phase 1b: read a live game Equipment into the scorer's per-item stat map.
    //
    // TWO VALUATIONS, and the caller MUST pick (there is no default on purpose):
    //
    //   maxed: false - the item's CURRENT values (curAttack/curDefense/specNCur). This is what the game
    //     actually adds to the character's stats right now, so it is the only correct valuation for a
    //     path that EQUIPS: GearOptimizer's candidate pools and CurrentScore, GearHunter's loot loadout.
    //   maxed: true  - the item boosted to cap at its current level, CalcCap(cap, level) = the item's
    //     FUTURE value. Correct for keep/trash verdicts and boost priority (InventoryAdvisor), and the
    //     valuation the web gear-optimizer uses (its item DB is hand-maintained maxed values), so it is
    //     also what GearOptimizerDiagnostic compares against the site.
    //
    // BOOSTS AND LEVEL ARE INDEPENDENT - this is why the split exists. Applying a boost raises
    // curAttack/curDefense/specNCur and NEVER `level` (docs/modules/TransformManager.md); level comes from
    // merging/daycare, and a merge raises cap * (1 + level/100) while leaving cur where it was. So a
    // level-100 item can sit at a fraction of its cap indefinitely, and scoring every candidate at cap
    // ranked such an item above a genuinely maxed one and equipped it (user-reported 2026-09-07).
    //
    // NOT YET INCLUDED (next iteration, needed to match the website exactly): set bonuses. The infinity cube
    // IS included (see BuildCube). Per-item spec stats dominate ranking, so this is a valid cut to validate the pipeline.
    public static class GameGearAdapter
    {
        private static float CalcCap(float cap, int level) => Mathf.Floor(cap * (1f + level / 100f));

        public static GearScorer.Item BuildItem(Equipment equip, bool isWeapon, bool maxed)
        {
            var item = new GearScorer.Item { IsWeapon = isWeapon };
            if (equip == null || equip.id == 0)
                return item;

            int level = equip.level;
            var ic = Main.InventoryController;

            // Power/Toughness: raw attack/defense (base-0 stats; scale-invariant for ranking).
            float power = maxed ? CalcCap(equip.capAttack, level) : equip.curAttack;
            if (power != 0) Add(item, GearObjectives.Stat.Power, power);
            float tough = maxed ? CalcCap(equip.capDefense, level) : equip.curDefense;
            if (tough != 0) Add(item, GearObjectives.Stat.Toughness, tough);

            // Spec %s: the game's getBonusFactor applies the correct per-stat divisor; ×100 = displayed %.
            AddSpec(ic, item, equip.spec1Type, maxed ? CalcCap(equip.spec1Cap, level) : equip.spec1Cur);
            AddSpec(ic, item, equip.spec2Type, maxed ? CalcCap(equip.spec2Cap, level) : equip.spec2Cur);
            AddSpec(ic, item, equip.spec3Type, maxed ? CalcCap(equip.spec3Cap, level) : equip.spec3Cur);
            return item;
        }

        private static void AddSpec(InventoryController ic, GearScorer.Item item, specType type, float raw)
        {
            if (type == specType.None || raw == 0) return;
            if (!GearObjectives.SpecTypeToStats.TryGetValue((int)type, out var stats)) return;
            double pct = ic.getBonusFactor(raw, type) * 100.0;
            if (pct == 0) return;
            foreach (var stat in stats)
                Add(item, stat, pct);
        }

        // The Infinity Cube - a fixed "item" present in every loadout. Power/Toughness from the game;
        // Drop/Gold/Hack/Wish from the tier formulas (ported from the gear-optimizer's cubeBaseItemData).
        public static GearScorer.Item BuildCubeItem()
        {
            var ic = Main.InventoryController;
            var item = new GearScorer.Item { IsWeapon = false };
            Add(item, GearObjectives.Stat.Power, ic.cubePower());
            Add(item, GearObjectives.Stat.Toughness, ic.cubeToughness());
            int tier = ic.infinityCubeTier();
            double drop = tier <= 0 ? 0 : tier == 1 ? 50 : 50 + (tier - 1) * 20;
            double gold = tier <= 1 ? 0 : tier == 2 ? 50 : Math.Pow(tier - 1, 1.3) * 50;
            double hack = tier <= 7 ? 0 : tier < 10 ? (tier - 8) * 5 + 10 : 20;
            double wish = tier <= 8 ? 0 : tier == 9 ? 10 : 20;
            if (drop != 0) Add(item, GearObjectives.Stat.DropChance, drop);
            if (gold != 0) Add(item, GearObjectives.Stat.GoldDrops, gold);
            if (hack != 0) Add(item, GearObjectives.Stat.HackSpeed, hack);
            if (wish != 0) Add(item, GearObjectives.Stat.WishSpeed, wish);
            return item;
        }

        // The character's nude adventure Power/Toughness (base with no gear) - a fixed "item".
        public static GearScorer.Item BuildBaseItem()
        {
            var ic = Main.InventoryController;
            var item = new GearScorer.Item { IsWeapon = false };
            Add(item, GearObjectives.Stat.Power, ic.adventureAttackBonus());
            Add(item, GearObjectives.Stat.Toughness, ic.adventureDefenseBonus());
            return item;
        }

        private static void Add(GearScorer.Item item, string stat, double value)
        {
            if (item.Stats.TryGetValue(stat, out var cur)) item.Stats[stat] = cur + value;
            else item.Stats[stat] = value;
        }
    }
}
