using System;
using System.Collections.Generic;
using UnityEngine;

namespace NGUAdvisor.Managers
{
    // Phase 1b: read a live game Equipment into the scorer's per-item stat map.
    //
    // Uses the item's MAX (boosted-to-cap) values scaled to its level - CalcCap(cap, level) - since the
    // advisor boosts gear to cap; this matches how the gear-optimizer optimizes for maxed gear.
    //   Power    = CalcCap(capAttack, level)
    //   Toughness= CalcCap(capDefense, level)
    //   spec i   = CalcCap(speciCap, level) added to the stat(s) that specType feeds (GearObjectives.SpecTypeToStats)
    //
    // NOT YET INCLUDED (next iteration, needed to match the website exactly): set bonuses. The infinity cube
    // IS included (see BuildCube). Per-item spec stats dominate ranking, so this is a valid cut to validate the pipeline.
    public static class GameGearAdapter
    {
        private static float CalcCap(float cap, int level) => Mathf.Floor(cap * (1f + level / 100f));

        public static GearScorer.Item BuildItem(Equipment equip, bool isWeapon)
        {
            var item = new GearScorer.Item { IsWeapon = isWeapon };
            if (equip == null || equip.id == 0)
                return item;

            int level = equip.level;
            var ic = Main.InventoryController;

            // Power/Toughness: maxed raw attack/defense (base-0 stats; scale-invariant for ranking).
            float power = CalcCap(equip.capAttack, level);
            if (power != 0) Add(item, GearObjectives.Stat.Power, power);
            float tough = CalcCap(equip.capDefense, level);
            if (tough != 0) Add(item, GearObjectives.Stat.Toughness, tough);

            // Spec %s: the game's getBonusFactor applies the correct per-stat divisor; ×100 = displayed %.
            AddSpec(ic, item, equip.spec1Type, CalcCap(equip.spec1Cap, level));
            AddSpec(ic, item, equip.spec2Type, CalcCap(equip.spec2Cap, level));
            AddSpec(ic, item, equip.spec3Type, CalcCap(equip.spec3Cap, level));
            return item;
        }

        private static void AddSpec(InventoryController ic, GearScorer.Item item, specType type, float rawMaxed)
        {
            if (type == specType.None || rawMaxed == 0) return;
            if (!GearObjectives.SpecTypeToStats.TryGetValue((int)type, out var stats)) return;
            double pct = ic.getBonusFactor(rawMaxed, type) * 100.0;
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
