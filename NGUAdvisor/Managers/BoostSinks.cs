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

        // Game's autoTransform values: 0 none, 1 power, 2 toughness, 3 special.
        public const int TypeNone = 0;
        public const int TypePower = 1;
        public const int TypeToughness = 2;
        public const int TypeSpecial = 3;

        // Which boost type is worth the most right now, for the game's auto-transform setting.
        //
        // Priced at the TOP tier on purpose: a boost small enough to fit inside every channel's
        // headroom delivers its full value whatever its type, so lower tiers answer "they are all
        // equal" and the choice would be arbitrary. Only the tiers that overflow expose which sink
        // still has room -- and overflow is destroyed, so that is exactly the loss the transform
        // exists to avoid.
        //
        // TypeNone comes back only when nothing can absorb a boost at all: the cube is a soft sink
        // that never fully saturates, so as long as it is usable, Power or Toughness beats None.
        // Delivered value of one TOP-tier boost per type: [power, toughness, special].
        public static double[] TypeScores(Sinks s)
        {
            if (s == null) return new double[3];
            int tier = BoostValueMath.Ladder.Length;

            return new[]
            {
                BoostValueMath.WithRecycling(tier, s.RecycleChance, t =>
                    BoostValueMath.Delivered(BoostValueMath.ValueOfTier(t), s.PowerGearHeadroom,
                        s.CubePowerRaw, s.CubePowerSoftcap, s.CubeUsable)),
                BoostValueMath.WithRecycling(tier, s.RecycleChance, t =>
                    BoostValueMath.Delivered(BoostValueMath.ValueOfTier(t), s.ToughnessGearHeadroom,
                        s.CubeToughnessRaw, s.CubeToughnessSoftcap, s.CubeUsable)),
                BoostValueMath.WithRecycling(tier, s.RecycleChance, t =>
                    BoostValueMath.Delivered(BoostValueMath.ValueOfTier(t), s.SpecialGearHeadroom, 0.0, 0.0, false)),
            };
        }

        // Same three numbers with the cube EXCLUDED — what the gear alone can absorb.
        public static double[] GearScores(Sinks s)
        {
            if (s == null) return new double[3];
            int tier = BoostValueMath.Ladder.Length;
            return new[]
            {
                BoostValueMath.WithRecycling(tier, s.RecycleChance, t =>
                    BoostValueMath.Delivered(BoostValueMath.ValueOfTier(t), s.PowerGearHeadroom, 0.0, 0.0, false)),
                BoostValueMath.WithRecycling(tier, s.RecycleChance, t =>
                    BoostValueMath.Delivered(BoostValueMath.ValueOfTier(t), s.ToughnessGearHeadroom, 0.0, 0.0, false)),
                BoostValueMath.WithRecycling(tier, s.RecycleChance, t =>
                    BoostValueMath.Delivered(BoostValueMath.ValueOfTier(t), s.SpecialGearHeadroom, 0.0, 0.0, false)),
            };
        }

        // GEAR DECIDES THE TYPE; the cube only breaks a tie.
        //
        // Not TypeScores' argmax, and the reason is the whole point of this method: the cube is a soft
        // sink that accepts Power and Toughness EQUALLY, so below its softcap every boost "delivers"
        // its full value whatever it is — which flattens the three types into a tie and hands the
        // decision to whichever branch happens to be tested first. Live case (2026-08-14): gear
        // headroom P=18700, T=0, S=564, cube far under cap → scores P=T=18888, and the advisor sat on
        // Toughness, the one type the gear could not use at all.
        //
        // The cube cannot tell P from T, so it must not be what picks between them. Gear can — and
        // Special exists ONLY in gear (the cube has no special channel), so pricing it against a cube
        // that eats everything guarantees it never wins.
        public static int BestType(Sinks s)
        {
            double[] gear = GearScores(s);
            double best = Math.Max(gear[0], Math.Max(gear[1], gear[2]));
            if (best > 0.0)
            {
                if (best == gear[2]) return TypeSpecial;
                if (best == gear[0]) return TypePower;
                return TypeToughness;
            }

            // Gear is saturated: the cube is the only sink left, and it takes just P and T.
            if (s == null || !s.CubeUsable) return TypeNone;
            double top = BoostValueMath.ValueOfTier(BoostValueMath.Ladder.Length);
            double cubePower = BoostValueMath.CubeGain(s.CubePowerRaw, s.CubePowerSoftcap, top);
            double cubeToughness = BoostValueMath.CubeGain(s.CubeToughnessRaw, s.CubeToughnessSoftcap, top);
            if (cubePower <= 0.0 && cubeToughness <= 0.0) return TypeNone;
            return cubeToughness > cubePower ? TypeToughness : TypePower;
        }

        public static string TypeName(int type)
        {
            switch (type)
            {
                case TypePower: return "Power";
                case TypeToughness: return "Toughness";
                case TypeSpecial: return "Special";
                default: return "None";
            }
        }
    }
}
