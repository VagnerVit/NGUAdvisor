using System;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    // Each test pins one decompiled game rule; the rule is named in the test name.
    public class ItopodFloorTests
    {
        // AdventureController.createEnemyTable(): Enemy(name, AR 1.2, atk 10, def 10, regen 1, hp 600).
        [Fact]
        public void BaseMobMatchesTheItopodSpawnTable()
        {
            Assert.Equal(600.0, ItopodConstants.BaseHp);
            Assert.Equal(10.0, ItopodConstants.BaseDefense);
            Assert.Equal(1600, ItopodConstants.MaxFloor);   // maxItopodLevel()
        }

        // The retired FloorHpNormalizer/PiercingHpNormalizer constants were exactly the floor-0 unit
        // at multiplier 1 — with the defense term folded INSIDE the 0.8 roll divisor. Pinning the
        // derivation documents where they came from and why they only held at multiplier 1.
        [Fact]
        public void OldNormalizersAreTheMultiplierOneSpecialCase()
        {
            double regular = (ItopodConstants.BaseHp * ItopodConstants.WorstEnemyRoll
                              + ItopodConstants.BaseDefense * ItopodConstants.WorstEnemyRoll / 2.0) / 0.8;
            double piercing = (ItopodConstants.BaseHp * ItopodConstants.WorstEnemyRoll
                               + ItopodConstants.BaseDefense * ItopodConstants.WorstEnemyRoll / 3.0) / 0.8;
            Assert.Equal(771.375, regular, 6);
            Assert.Equal(769.25, piercing, 6);

            // At multiplier 1 the correct unit is within 0.2% of it, so nothing shifts.
            Assert.Equal(770.1, ItopodConstants.AttackPerFloorUnit(1.0, false), 6);
        }

        // PlayerController subtracts the enemy's defense BEFORE the multiplier, so the defense term
        // is a constant. The old formula divided it by the multiplier too, which made the advisor
        // increasingly optimistic as the rotation got stronger.
        [Theory]
        [InlineData(1.0, 0)]
        [InlineData(10.0, 1)]
        [InlineData(29.0, 3)]
        [InlineData(100.0, 10)]
        public void OldFormulaOvershootsAndTheGapGrowsWithTheMultiplier(double multiplier, int expectedOvershoot)
        {
            // An attack sitting inside floor 500's band under the correct formula — 2% above the
            // boundary, well clear of it but far below the next floor's 5% step.
            const int floor = 500;
            double attack = Math.Pow(ItopodConstants.FloorGrowthBase, floor)
                            * ItopodConstants.AttackPerFloorUnit(multiplier, false) * 1.02;

            int correct = ItopodConstants.BestFloor(attack, multiplier, false);
            int old = OldBestFloor(attack, multiplier, false);

            Assert.Equal(floor, correct);
            Assert.Equal(floor + expectedOvershoot, old);
        }

        private static int OldBestFloor(double attack, double multiplier, bool piercing)
        {
            double normalizer = piercing ? 769.25 : 771.375;
            double f = Math.Floor(Math.Log(attack * multiplier / normalizer, ItopodConstants.FloorGrowthBase));
            return f < 0 ? 0 : f > ItopodConstants.MaxFloor ? ItopodConstants.MaxFloor : (int)f;
        }

        [Fact]
        public void PiercingNeedsLessAttackBecauseItSubtractsDefenseOverThree()
        {
            Assert.True(ItopodConstants.AttackPerFloorUnit(5.0, true)
                      < ItopodConstants.AttackPerFloorUnit(5.0, false));
        }

        [Fact]
        public void MultiplierForFloorInvertsBestFloor()
        {
            const double attack = 1e12;
            for (int floor = 0; floor <= 480; floor += 60)
            {
                double needed = ItopodConstants.MultiplierForFloor(attack, floor, false) * (1.0 + 1e-9);
                Assert.False(double.IsInfinity(needed));
                Assert.Equal(floor, ItopodConstants.BestFloor(attack, needed, false));
            }

            // Above the ceiling the scaled defense alone outruns the attack, and no multiplier helps.
            Assert.True(double.IsPositiveInfinity(ItopodConstants.MultiplierForFloor(attack, 600, false)));
        }

        // Past a point the scaled defense alone eats the whole swing and no multiplier gets there —
        // a case the old "normalizer / multiplier" form could not express.
        [Fact]
        public void MultiplierForFloorIsInfiniteWhenDefenseAloneExceedsTheAttack()
        {
            double attack = Math.Pow(ItopodConstants.FloorGrowthBase, 100) * 5.0;   // < 10*1.02/2 per unit
            Assert.True(double.IsPositiveInfinity(ItopodConstants.MultiplierForFloor(attack, 100, false)));
        }

        [Fact]
        public void FloorIsClampedToMaxItopodLevel()
        {
            Assert.Equal(ItopodConstants.MaxFloor, ItopodConstants.BestFloor(1e36, 1000.0, false));
            Assert.Equal(0, ItopodConstants.BestFloor(1.0, 1.0, false));
        }
    }

    public class ItopodRewardsTests
    {
        // LootDrop.itopodTier: 1 + level / 50.
        [Theory]
        [InlineData(0, 1)]
        [InlineData(49, 1)]
        [InlineData(50, 2)]
        [InlineData(950, 20)]
        [InlineData(1150, 24)]
        [InlineData(1600, 33)]
        public void TierIsOnePerFiftyFloors(int floor, int expected)
            => Assert.Equal(expected, ItopodRewards.Tier(floor));

        // itopodDrop's ladder bends. Tier 24 is the last one that moves the index.
        [Theory]
        [InlineData(1, 1)]
        [InlineData(10, 10)]
        [InlineData(11, 10)]
        [InlineData(15, 11)]
        [InlineData(18, 12)]
        [InlineData(24, 13)]
        [InlineData(33, 13)]
        public void BoostLadderIndexSaturatesAtTierTwentyFour(int tier, int expected)
            => Assert.Equal(expected, ItopodRewards.BoostLadderIndex(tier));

        // LootDrop.killsPerAP: Mathf.Max(40 - tier, 20).
        [Theory]
        [InlineData(1, 39)]
        [InlineData(19, 21)]
        [InlineData(20, 20)]
        [InlineData(33, 20)]
        public void KillsPerApBottomsOutAtTierTwenty(int tier, int expected)
            => Assert.Equal(expected, ItopodRewards.KillsPerAp(tier));

        // killsPerEXP is the same function as killsPerAP and both test the same enemiesKilled
        // counter, so the AP kill and the EXP kill are always the same kill.
        [Fact]
        public void ExpAndApShareTheirKillCounter()
        {
            for (int tier = 1; tier <= 40; tier++)
                Assert.Equal(ItopodRewards.KillsPerAp(tier), ItopodRewards.KillsPerExp(tier));
        }

        // LootDrop.itopodEXPAwarded: tier < 3 ? tier : (tier-1)(tier-2)+2.
        [Theory]
        [InlineData(1, 1.0)]
        [InlineData(2, 2.0)]
        [InlineData(3, 4.0)]
        [InlineData(10, 74.0)]
        [InlineData(24, 508.0)]
        public void ExpAwardIsQuadraticInTier(int tier, double expected)
            => Assert.Equal(expected, ItopodRewards.ExpAwarded(tier));

        // The point of the whole exercise: above floor 1150 boosts stop improving and AP already
        // has, but EXP keeps climbing. A boost-only model sees a plateau that is not there.
        [Fact]
        public void AboveFloorElevenFiftyOnlyExpAndPpStillGrow()
        {
            const int low = 1150;
            const int high = 1600;

            Assert.Equal(ItopodRewards.BoostLadderIndex(ItopodRewards.Tier(low)),
                         ItopodRewards.BoostLadderIndex(ItopodRewards.Tier(high)));
            Assert.Equal(ItopodRewards.ApPerKill(low), ItopodRewards.ApPerKill(high));
            Assert.True(ItopodRewards.ExpPerKill(high) > ItopodRewards.ExpPerKill(low));
            Assert.True(ItopodRewards.PpPerKill(high, ItopodRewards.Difficulty.Normal, 1.0)
                      > ItopodRewards.PpPerKill(low, ItopodRewards.Difficulty.Normal, 1.0));
        }

        // ItopodPerkController.progressGained, over pointThreshold() = 1e6.
        [Theory]
        [InlineData(ItopodRewards.Difficulty.Normal, 1000, 1200.0)]
        [InlineData(ItopodRewards.Difficulty.Evil, 1000, 1700.0)]
        [InlineData(ItopodRewards.Difficulty.Sadistic, 1000, 3000.0)]
        public void PpProgressUsesTheDifficultyBasePlusTheFloor(ItopodRewards.Difficulty diff, int floor, double expected)
        {
            Assert.Equal(expected, ItopodRewards.PpProgressPerKill(floor, diff, 1.0));
            Assert.Equal(expected / ItopodRewards.PpThreshold, ItopodRewards.PpPerKill(floor, diff, 1.0));
        }

        // totalPPBonus() clamps at 1 before it is applied.
        [Fact]
        public void PpBonusBelowOneIsClamped()
            => Assert.Equal(ItopodRewards.PpProgressPerKill(0, ItopodRewards.Difficulty.Normal, 1.0),
                            ItopodRewards.PpProgressPerKill(0, ItopodRewards.Difficulty.Normal, 0.5));
    }
}
