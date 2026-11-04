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
            // 1/p = 180.00004 ticks per level, and the bar discards its overflow: 181 ticks each.
            double gained = NguGain.LevelsInHour(5_000_000, 1_000);
            Assert.InRange(gained, 994.0, 995.0);
        }

        [Fact]
        public void CappedStartFallsBackToTheSlowdownOnceTheLevelOutgrowsTheBar()
        {
            // Progress 2/tick at L999 (K=2000): 1 tick per level for k = 1000..2000 (1001 ticks), then
            // 2 ticks for each of the next 2000 levels, 3 for the next 2000, ... Worked by hand: levels
            // with 2..12 ticks cost 155001 ticks in all (23001 levels), the remaining 24999 ticks buy
            // 1923 levels at 13 ticks each.
            double gained = NguGain.LevelsInHour(999, 2.0 * 180_000);
            Assert.Equal(24_924.0, gained, 3);
        }
    }
}
