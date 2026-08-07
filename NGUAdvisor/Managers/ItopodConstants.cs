using System;

namespace NGUAdvisor.Managers
{
    /// <summary>
    /// ITOPOD floor math, derived from the game's own mob table and damage formula rather than
    /// from a precomputed normalizer. Unity-free so tests/NGUAdvisor.Tests can link it.
    ///
    /// Game truth (decomp):
    ///   AdventureController.createEnemyTable() -- every ITOPOD spawn is
    ///       Enemy(name, AR 1.2, atk 10, def 10, regen 1, hp 600)
    ///   AdventureController.powerUp(e, L)      -- attack/defense/maxHP/regen *= 1.05^L,
    ///                                             then each *= Random.Range(0.98f, 1.02f)
    ///   PlayerController                       -- damage = (totalAdvAttack - defense/divisor)
    ///                                             * multiplier * Random.Range(0.8f, 1.2f)
    ///                                             divisor = 3 for pierceAttack, else 2
    ///
    /// The defense subtraction happens BEFORE the multiplier, so the defense term does NOT shrink
    /// as the rotation gets stronger. The old FloorHpNormalizer/PiercingHpNormalizer constants
    /// (771.375 and 769.25) folded it in the other way -- they are exactly
    /// (600*1.02 + 10*1.02/divisor) / 0.8, i.e. correct only at multiplier 1, and increasingly
    /// OPTIMISTIC above it: +1.2 floors at multiplier 10, +3.4 at 29 (a full ult/charge/mega
    /// stack), +10.3 at 100. Those are floors the advisor would park on without being able to
    /// guarantee the one-shot it assumed.
    /// </summary>
    public static class ItopodConstants
    {
        // Per-floor stat growth: powerUp() raises every stat by this base per floor.
        public const double FloorGrowthBase = 1.05;

        // AdventureController.maxItopodLevel().
        public const int MaxFloor = 1600;

        // The single ITOPOD mob archetype, before powerUp().
        public const double BaseHp = 600.0;
        public const double BaseDefense = 10.0;

        // powerUp()'s per-stat jitter, worst case for us.
        public const double WorstEnemyRoll = 1.02;

        /// <summary>
        /// Adventure attack that GUARANTEES a one-shot on floor 0 with the given damage
        /// multiplier. Everything above scales by 1.05^floor, so this is the unit the floor
        /// logarithm is taken against.
        /// </summary>
        public static double AttackPerFloorUnit(double multiplier, bool piercing)
        {
            if (multiplier <= 0.0) return double.PositiveInfinity;
            double divisor = piercing ? 3.0 : 2.0;
            return BaseHp * WorstEnemyRoll / (BoostValueMath.MinRoll * multiplier)
                 + BaseDefense * WorstEnemyRoll / divisor;
        }

        /// <summary>
        /// Attack expressed in floor-0 one-shot units. Feed to <see cref="FloorOfNormalized"/>.
        /// </summary>
        public static double NormalizedAttack(double attack, double multiplier, bool piercing)
        {
            double unit = AttackPerFloorUnit(multiplier, piercing);
            if (unit <= 0.0 || double.IsInfinity(unit) || double.IsNaN(unit)) return 0.0;
            return attack / unit;
        }

        public static int FloorOfNormalized(double normalizedAttack)
        {
            if (normalizedAttack <= 1.0 || double.IsNaN(normalizedAttack)) return 0;
            double floor = Math.Floor(Math.Log(normalizedAttack, FloorGrowthBase));
            if (floor < 0.0) return 0;
            if (floor > MaxFloor) return MaxFloor;
            return (int)floor;
        }

        /// <summary>Highest floor this attack and multiplier can one-shot on the worst roll.</summary>
        public static int BestFloor(double attack, double multiplier, bool piercing)
            => FloorOfNormalized(NormalizedAttack(attack, multiplier, piercing));

        /// <summary>
        /// Damage multiplier required to one-shot <paramref name="floor"/>. Returns +inf when no
        /// multiplier can do it -- past a point the scaled defense alone consumes the whole swing,
        /// which is the failure mode the old formula could not express at all.
        /// </summary>
        public static double MultiplierForFloor(double attack, int floor, bool piercing)
        {
            if (attack <= 0.0) return double.PositiveInfinity;
            if (floor < 0) floor = 0;
            double perUnit = attack / Math.Pow(FloorGrowthBase, floor);
            double defenseTerm = BaseDefense * WorstEnemyRoll / (piercing ? 3.0 : 2.0);
            double headroom = perUnit - defenseTerm;
            if (headroom <= 0.0) return double.PositiveInfinity;
            return BaseHp * WorstEnemyRoll / (BoostValueMath.MinRoll * headroom);
        }
    }
}
