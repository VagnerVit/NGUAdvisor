using System;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class NguGainTests
    {
        private const int Normal = 0;

        [Fact]
        public void DropChanceCurveMatchesTheGameReadout()
        {
            // State export 2026-10-08: NGU Drop Chance L885567 reads 30.83x (boost factor 0.001).
            double bonus = NguGain.Bonus(false, 6, Normal, 885567, 0.001);
            Assert.InRange(bonus, 30.8, 30.86);
        }

        [Fact]
        public void PastTheSoftcapTheRatioIsAPowerNotLinear()
        {
            // Ygg past L400 is L^0.33: +10 % levels is ~+3.2 % bonus, never the linear +10 %.
            double ratio = NguGain.Ratio(true, 0, Normal, 5_000_000, 500_000, 1.0);
            Assert.InRange(ratio, 1.030, 1.033);
        }

        [Fact]
        public void BelowTheSoftcapTheCurveIsLinear()
        {
            double ratio = NguGain.Ratio(false, 7, Normal, 100, 100, 0.01);
            Assert.Equal(3.0 / 2.0, ratio, 6);
        }

        [Fact]
        public void LinearNgusStayLinearAtAnyLevel()
        {
            double ratio = NguGain.Ratio(false, 3, Normal, 1_000_000, 100_000, 0.01);
            Assert.Equal((1 + 0.01 * 1_100_000) / (1 + 0.01 * 1_000_000), ratio, 6);
        }

        [Fact]
        public void AnHourNeverGainsMoreThanOneLevelPerTick()
        {
            Assert.Equal(180_000, NguGain.LevelsInHour(100, 1e12), 0);
        }

        [Fact]
        public void TheRateFallsAsLevelsClimbWithinTheHour()
        {
            // 10k levels/hr quoted at L10k: cost grows with the level, so the hour lands below 10k.
            double gained = NguGain.LevelsInHour(10_000, 10_000);
            Assert.InRange(gained, 7_000, 8_000);
        }

        [Fact]
        public void ASlowLaneHighUpBarelySlowsDown()
        {
            double gained = NguGain.LevelsInHour(5_000_000, 1_000);
            Assert.InRange(gained, 999.0, 1_000.0);
        }

        [Fact]
        public void CappedStartFallsBackToTheSlowdownOnceTheLevelOutgrowsTheBar()
        {
            // Progress 2/tick at L999 (K=2000): 1 level/tick until L1999, then sqrt growth.
            double gained = NguGain.LevelsInHour(999, 2.0 * 180_000);
            double expected = Math.Sqrt(2000.0 * 2000.0 + 2 * 2000.0 * (180_000 - 1000)) - 1 - 999;
            Assert.Equal(expected, gained, 0);
        }
    }
}
