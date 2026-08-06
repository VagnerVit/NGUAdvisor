using System;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    // Each test pins one decompiled game rule; the rule is named in the test name.
    public class BoostValueMathTests
    {
        [Fact]
        public void LadderMatchesItemNameDescTiers()
        {
            // ItemNameDesc.itemName[1..13]: "Power Boost 1" .. "Power Boost 10K".
            Assert.Equal(13, BoostValueMath.Ladder.Length);
            Assert.Equal(1, BoostValueMath.ValueOfTier(1));
            Assert.Equal(200, BoostValueMath.ValueOfTier(8));
            Assert.Equal(10000, BoostValueMath.ValueOfTier(13));
            Assert.Equal(8, BoostValueMath.TierOfValue(200));
            Assert.Equal(0, BoostValueMath.TierOfValue(300));
        }

        [Fact]
        public void RollProbabilityIsCappedLikeMathfMin()
        {
            // LootDrop: Mathf.Min(chance * lootFactor, cap)
            Assert.Equal(0.05, BoostValueMath.RollProbability(0.01, 5.0, 0.1), 10);
            Assert.Equal(0.1, BoostValueMath.RollProbability(0.01, 50.0, 0.1), 10);
        }

        [Fact]
        public void GearOverflowIsDestroyed()
        {
            // Equipment.boostEquip clamps curAttack at floor(cap * (1 + level/100)) AFTER adding the
            // whole boost, so the excess is gone -- a 10K boost into 300 of headroom is worth 300.
            Assert.Equal(300, BoostValueMath.Delivered(10000, 300, 0, 0, false));
            Assert.Equal(500, BoostValueMath.Delivered(500, 4000, 0, 0, false));
            Assert.Equal(0, BoostValueMath.Delivered(10000, 0, 0, 0, false));
        }

        [Fact]
        public void CubeIsSoftCappedBySquareRoot()
        {
            // InventoryController.cubePower(): raw <= softcap ? raw : softcap + sqrt(raw - softcap)
            Assert.Equal(100, BoostValueMath.CubeEffective(100, 1000), 10);
            Assert.Equal(1000, BoostValueMath.CubeEffective(1000, 1000), 10);
            Assert.Equal(1010, BoostValueMath.CubeEffective(1100, 1000), 10);
        }

        [Fact]
        public void CubeGainBelowSoftcapIsFullValueAndDecaysAbove()
        {
            Assert.Equal(500, BoostValueMath.CubeGain(0, 1000, 500), 10);

            // Straddling the softcap: 200 to reach it, then sqrt(300) beyond.
            Assert.Equal(200 + Math.Sqrt(300), BoostValueMath.CubeGain(800, 1000, 500), 10);

            // Deep past the softcap a 10K boost is worth single digits, not 10K.
            double deep = BoostValueMath.CubeGain(1_000_000, 1000, 10000);
            Assert.True(deep > 0, "the cube never becomes a dead sink");
            Assert.True(deep < 6, $"expected sharply diminishing returns, got {deep}");
        }

        [Fact]
        public void BoostGoesWhereverItHelpsMost()
        {
            // Gear channel nearly full, cube wide open -> the cube wins.
            Assert.Equal(5000, BoostValueMath.Delivered(5000, 10, 0, 100000, true), 10);
            // Gear channel open, cube deep past its softcap -> gear wins.
            Assert.Equal(5000, BoostValueMath.Delivered(5000, 9000, 1_000_000, 1000, true), 10);
        }

        [Fact]
        public void RecyclingWalksTheLadderDown()
        {
            // InventoryController.boostRecycle: consuming a boost returns makeLoot(id - 1) with
            // probability totalRecycleBonus(), recursively, and never below tier 1.
            Func<int, double> full = t => BoostValueMath.ValueOfTier(t);

            Assert.Equal(10000, BoostValueMath.WithRecycling(13, 0.0, full), 6);

            // r = 0.5 over 10000 + 5000 + 2000 + 1000 + 500 + 200 + ...
            double expected = 10000 + 0.5 * 5000 + 0.25 * 2000 + 0.125 * 1000 + 0.0625 * 500
                + 0.03125 * 200 + 0.015625 * 100 + 0.0078125 * 50 + 0.00390625 * 20
                + 0.001953125 * 10 + 0.0009765625 * 5 + 0.00048828125 * 2 + 0.000244140625 * 1;
            Assert.Equal(expected, BoostValueMath.WithRecycling(13, 0.5, full), 6);

            // Tier 1 has nothing below it (ids 1/14/27 are excluded from the recycle roll).
            Assert.Equal(1, BoostValueMath.WithRecycling(1, 0.9, full), 6);
        }

        [Fact]
        public void RecyclingRespectsTheSameChannelHeadroom()
        {
            // The returned boost keeps its type, so it hits the same (already tight) channel: every
            // tier down to 200 saturates the same 150 of headroom, and only the tail is worth less.
            Func<int, double> capped = t => Math.Min(BoostValueMath.ValueOfTier(t), 150);

            double expected = 0;
            double weight = 1.0;
            for (int t = 13; t >= 1; t--)
            {
                expected += weight * capped(t);
                weight *= 0.5;
            }

            Assert.Equal(expected, BoostValueMath.WithRecycling(13, 0.5, capped), 6);

            // Sanity: the cap makes the chain worth far less than the uncapped ladder would suggest.
            Assert.True(expected < 300, $"expected the 150 cap to dominate, got {expected}");
        }

        [Fact]
        public void FullRecyclingSumsTheWholeLadder()
        {
            // totalRecycleBonus() can exceed 1.0 (purchases plus 10% per Basic Challenge completion);
            // WithRecycling clamps it, so a guaranteed return walks every tier down exactly once.
            Func<int, double> full = t => BoostValueMath.ValueOfTier(t);
            double all = 0;
            foreach (double v in BoostValueMath.Ladder) all += v;
            Assert.Equal(all, BoostValueMath.WithRecycling(13, 1.5, full), 6);
        }

        [Fact]
        public void IdlePaysSpawnLatencyOnEveryKill()
        {
            // AdventureController zeroes idleAttackTimer on spawn and only advances it mid-fight, so
            // the first swing lands a full attackSpeed after the spawn.
            Assert.Equal(5.0, BoostValueMath.CycleSeconds(true, 4.0, 1.0, 1), 10);
            Assert.Equal(6.0, BoostValueMath.CycleSeconds(true, 4.0, 1.0, 2), 10);
        }

        [Fact]
        public void ManualLandsTheOpeningSwingOnTheSpawnFrame()
        {
            // PlayerController.moveTimer keeps ticking through the respawn.
            Assert.Equal(4.0, BoostValueMath.CycleSeconds(false, 4.0, 1.0, 1), 10);
            Assert.Equal(5.0, BoostValueMath.CycleSeconds(false, 4.0, 1.0, 2), 10);
        }

        [Fact]
        public void ManualIsAtMostTwiceAsFastAndNeverSlower()
        {
            foreach (double respawn in new[] { 0.0, 0.2, 0.8, 1.0, 2.0, 4.0, 10.0 })
            {
                double idle = BoostValueMath.CycleSeconds(true, respawn, 0.8, 1);
                double manual = BoostValueMath.CycleSeconds(false, respawn, 0.8, 1);
                Assert.True(manual <= idle, $"respawn {respawn}: manual {manual} > idle {idle}");
                Assert.True(idle / manual <= 2.0 + 1e-9, $"respawn {respawn}: ratio {idle / manual}");
            }
        }

        [Fact]
        public void HitsToKillUsesTheWorstRollAndCountsEnemyRegen()
        {
            Assert.Equal(1, BoostValueMath.HitsToKill(100, 100, 0, 1.0), 10);
            Assert.Equal(2, BoostValueMath.HitsToKill(101, 100, 0, 1.0), 10);

            // Regen eats 30 per swing, so net is 70 per swing.
            Assert.Equal(2, BoostValueMath.HitsToKill(140, 100, 30, 1.0), 10);

            // Damage below the regen the enemy heals in one swing: never dies.
            Assert.True(double.IsInfinity(BoostValueMath.HitsToKill(100, 50, 60, 1.0)));
            Assert.True(double.IsInfinity(BoostValueMath.HitsToKill(100, 0, 0, 1.0)));
        }

        [Fact]
        public void ExpectedHitsIsExactAtBothEndsOfTheRollBand()
        {
            // r = maxHP / damage. Every roll one-shots at r <= 0.8; at r = 1.2 the first swing never
            // kills but the second always does.
            Assert.Equal(1.0, BoostValueMath.ExpectedHits(80, 100, 0, 1.0), 10);
            Assert.Equal(1.0, BoostValueMath.ExpectedHits(50, 100, 0, 1.0), 10);
            Assert.Equal(2.0, BoostValueMath.ExpectedHits(120, 100, 0, 1.0), 10);

            // r = 1.0: half the rolls one-shot, so 1.5 swings on average.
            Assert.Equal(1.5, BoostValueMath.ExpectedHits(100, 100, 0, 1.0), 10);
        }

        [Fact]
        public void ExpectedHitsIsNotInflatedByTheSafetyFactor()
        {
            // The old cadence input was ceil(maxHP / (0.8 * mean)) -- 13 swings for a 10-swing enemy,
            // a 25% overstatement that made multi-swing zones look worse than they are.
            double old = Math.Ceiling(1000 / (0.8 * 100));
            double now = BoostValueMath.ExpectedHits(1000, 100, 0, 1.0);
            Assert.Equal(13, old);
            Assert.Equal(10.5, now, 10);
            Assert.True(now < old);
        }

        [Fact]
        public void ExpectedHitsNeverDropsBelowTwoOnceTheFirstSwingCannotKill()
        {
            for (double hp = 120; hp <= 200; hp += 5)
                Assert.True(BoostValueMath.ExpectedHits(hp, 100, 0, 1.0) >= 2.0, $"hp {hp}");
        }

        [Fact]
        public void ExpectedHitsRespectsEnemyRegen()
        {
            // Regen of 30 per 1s swing cuts net damage from 100 to 70, so the same enemy takes longer.
            double without = BoostValueMath.ExpectedHits(140, 100, 0, 1.0);
            double with = BoostValueMath.ExpectedHits(140, 100, 30, 1.0);
            Assert.True(with > without, $"regen must cost swings: {with} vs {without}");
            Assert.Equal(140 / 70.0 + 0.5, with, 10);

            // Damage below what the enemy heals in one swing interval: never dies.
            Assert.True(double.IsInfinity(BoostValueMath.ExpectedHits(100, 50, 60, 1.0)));
        }

        [Fact]
        public void SustainedRotationFillsSpareSlotsWithRegularAttacks()
        {
            // 1s global cooldown, so 1 slot/s. A 5s-cooldown move at 500 damage takes 0.2 of the slots
            // and regular attacks (100) take the other 0.8.
            double d = BoostValueMath.SustainedDamagePerSlot(100, 1.0, new[] { new[] { 500.0, 5.0 } });
            Assert.Equal(0.2 * 500 + 0.8 * 100, d, 10);

            // No big moves -> exactly the regular attack.
            Assert.Equal(100, BoostValueMath.SustainedDamagePerSlot(100, 1.0), 10);
            Assert.Equal(100, BoostValueMath.SustainedDamagePerSlot(100, 1.0, null), 10);
        }

        [Fact]
        public void SustainedRotationCannotExceedOneMovePerGlobalCooldown()
        {
            // Three moves each claiming to fire every 0.5s cannot all fit in 1 slot/s: the total rate
            // is capped, and the regular attack gets nothing.
            double d = BoostValueMath.SustainedDamagePerSlot(100, 1.0,
                new[] { 900.0, 0.5 }, new[] { 800.0, 0.5 }, new[] { 700.0, 0.5 });
            Assert.Equal(900, d, 10);
        }

        [Fact]
        public void SustainedRotationIsAtLeastTheRegularAttack()
        {
            double d = BoostValueMath.SustainedDamagePerSlot(100, 0.8, new[] { new[] { 300.0, 12.0 } });
            Assert.True(d >= 100, $"a rotation must never be worse than spamming regular attacks, got {d}");
        }

        [Fact]
        public void ZoneZeroBoostGateIsTheHardestNormalNotTheBoss()
        {
            // createEnemyTable() zone 0: normals 40/45/55 HP (hardest def 7), boss 100 HP def 9.
            // Boost rolls only fire on enemyType.normal, so the requirement is the hardest NORMAL:
            // 55 / 0.8 + 7/2 = 72.25 -- not the OPower table's boss-derived 129.5.
            double needForNormal = 55 / 0.8 + 7 / 2.0;
            double needForBoss = 100 / 0.8 + 9 / 2.0;
            Assert.Equal(72.25, needForNormal, 6);
            Assert.Equal(129.5, needForBoss, 6);

            // At exactly the normal requirement the hardest normal dies in one swing.
            double damage = (needForNormal - 7 / 2.0) * 0.8;
            Assert.Equal(1, BoostValueMath.HitsToKill(55, damage, 0, 1.0), 10);
        }
    }
}
