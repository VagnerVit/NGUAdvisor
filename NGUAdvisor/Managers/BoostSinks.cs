using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // What a boost is worth RIGHT NOW, per boost type, given where it could actually go.
    //
    // A dropped boost is one of three types, uniformly (LootDrop rolls Random.Range(1, 4) over the
    // Power / Toughness / Special variants of the same tier), and each type has its own sinks:
    //
    //   Power, Toughness -> one gear channel per item, overflow DESTROYED (Equipment.boostEquip
    //                       clamps curAttack at floor(capAttack * (1 + level/100))), plus the
    //                       Infinity Cube as a soft sink.
    //   Special          -> CASCADES spec1 -> spec2 -> spec3 on one item, so its usable headroom is
    //                       the sum of the three slots. The cube has no special channel.
    //
    // Because overflow is destroyed, the value of a boost is min(tierValue, best SINGLE item's
    // headroom) -- not the total headroom across the whole loadout. That is the correction that
    // matters most at high tiers: a 10K boost dropped when the hungriest item needs 300 is worth 300.
    public static class BoostSinks
    {
        public class Sinks
        {
            public double PowerGearHeadroom;
            public double ToughnessGearHeadroom;
            public double SpecialGearHeadroom;
            public double CubePowerRaw;
            public double CubePowerSoftcap;
            public double CubeToughnessRaw;
            public double CubeToughnessSoftcap;
            public bool CubeUsable;
            public double RecycleChance;
        }

        private static IEnumerable<int> TargetIds()
        {
            HashSet<int> ids = new HashSet<int>();
            try
            {
                foreach (int id in LoadoutManager.CurrentGearIds()) ids.Add(id);
            }
            catch (Exception e) { Main.LogDebug($"BoostSinks gear ids: {e.Message}"); }
            int[] prio = Main.Settings?.PriorityBoosts;
            if (prio != null)
                foreach (int id in prio) ids.Add(id);
            return ids;
        }

        public static Sinks Current()
        {
            Sinks s = new Sinks();
            try
            {
                Character c = Main.Character;
                InventoryController ic = Main.InventoryController;

                foreach (int id in TargetIds())
                {
                    try
                    {
                        ih slot = LoadoutManager.FindItemSlot(id);
                        if (slot?.equipment == null) continue;
                        BoostsNeeded need = slot.equipment.GetNeededBoosts();
                        s.PowerGearHeadroom = Math.Max(s.PowerGearHeadroom, need.power);
                        s.ToughnessGearHeadroom = Math.Max(s.ToughnessGearHeadroom, need.toughness);
                        s.SpecialGearHeadroom = Math.Max(s.SpecialGearHeadroom, need.special);
                    }
                    catch (Exception e) { Main.LogDebug($"BoostSinks item {id}: {e.Message}"); }
                }

                s.CubePowerRaw = c.inventory.cubePower;
                s.CubeToughnessRaw = c.inventory.cubeToughness;
                s.CubePowerSoftcap = ic.cubePowerSoftcap();
                s.CubeToughnessSoftcap = ic.cubeToughnessSoftcap();
                // cubePower()/cubeToughness() return 0 flat inside the No Equipment challenge, which
                // is exactly when the softcaps read 0 too.
                s.CubeUsable = s.CubePowerSoftcap > 0.0 || s.CubeToughnessSoftcap > 0.0;
                s.RecycleChance = c.totalRecycleBonus();
            }
            catch (Exception e) { Main.LogDebug($"BoostSinks.Current: {e.Message}"); }
            return s;
        }

        // Expected adventure-stat points delivered by ONE dropped boost of `tier`, averaged over the
        // three equally likely types and following the recycling chain (which preserves the type).
        public static double ValueOfDrop(int tier, Sinks s)
        {
            if (s == null || tier < 1) return 0.0;

            double power = BoostValueMath.WithRecycling(tier, s.RecycleChance, t =>
                BoostValueMath.Delivered(BoostValueMath.ValueOfTier(t), s.PowerGearHeadroom,
                    s.CubePowerRaw, s.CubePowerSoftcap, s.CubeUsable));

            double toughness = BoostValueMath.WithRecycling(tier, s.RecycleChance, t =>
                BoostValueMath.Delivered(BoostValueMath.ValueOfTier(t), s.ToughnessGearHeadroom,
                    s.CubeToughnessRaw, s.CubeToughnessSoftcap, s.CubeUsable));

            double special = BoostValueMath.WithRecycling(tier, s.RecycleChance, t =>
                BoostValueMath.Delivered(BoostValueMath.ValueOfTier(t), s.SpecialGearHeadroom, 0.0, 0.0, false));

            return (power + toughness + special) / 3.0;
        }
    }
}
