using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class RebirthLengthTests
    {
        private const double Hour = 3600.0;

        [Fact]
        public void TimeMultiIsLinearInFortyEightHoursPastTheFirstHour()
        {
            Assert.Equal(1.5, RebirthLength.TimeMulti(24 * Hour), 9);
            Assert.Equal(1.0 + 1.0 / 48.0, RebirthLength.TimeMulti(Hour), 9);
        }

        [Fact]
        public void TimeMultiUsesTheSubHourDivisors()
        {
            Assert.Equal((Hour - 1) / 2.0 / Hour, RebirthLength.TimeMulti(Hour - 1), 12);
            Assert.Equal(900.0 / 4.0 / Hour, RebirthLength.TimeMulti(900.0), 12);
        }

        [Fact]
        public void BeardFactorCapsAtEightAfterTwentyFourHoursWithoutThePerk()
        {
            Assert.Equal(0.0, RebirthLength.BeardTimeFactor(Hour - 1, 0));
            Assert.Equal(8.0, RebirthLength.BeardTimeFactor(24 * Hour, 0), 9);
            Assert.Equal(8.0, RebirthLength.BeardTimeFactor(40 * Hour, 0), 9);
            Assert.Equal(24 * Hour, RebirthLength.BeardCapSeconds(0), 6);
            Assert.Equal(3 * Hour, RebirthLength.BeardCapSeconds(21), 6);
        }

        [Fact]
        public void MarginalHourAtTwentyFourHoursIsBelowTheRunAverage()
        {
            double marginal = RebirthLength.MarginalHourGain(24 * Hour);
            double average = RebirthLength.AverageHourGain(24 * Hour);
            Assert.InRange(marginal, 0.0138, 0.0140);
            Assert.InRange(average, 0.0165, 0.0172);
            Assert.True(marginal < average);
        }

        [Fact]
        public void DescribeNamesTheBeardCap()
        {
            Assert.Contains("stop growing at 24 h", RebirthLength.Describe(12 * Hour, 0));
            Assert.Contains("capped", RebirthLength.Describe(30 * Hour, 0));
        }
    }
}
