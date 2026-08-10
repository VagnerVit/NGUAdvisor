using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class PpEtaTests
    {
        [Fact]
        public void AlreadyAffordableYieldsNoEstimate()
        {
            Assert.Null(PpEta.HoursTo(cost: 100, banked: 100, perHour: 50));
            Assert.Null(PpEta.HoursTo(cost: 100, banked: 250, perHour: 50));
        }

        [Fact]
        public void NoRateYieldsNoEstimateRatherThanInfinity()
        {
            Assert.Null(PpEta.HoursTo(cost: 100, banked: 0, perHour: 0));
            Assert.Null(PpEta.HoursTo(cost: 100, banked: 0, perHour: -5));
        }

        [Fact]
        public void NonFiniteRateYieldsNoEstimate()
        {
            Assert.Null(PpEta.HoursTo(100, 0, double.NaN));
            Assert.Null(PpEta.HoursTo(100, 0, double.PositiveInfinity));
        }

        [Fact]
        public void NormalCaseDividesTheShortfallByTheRate()
        {
            var h = PpEta.HoursTo(cost: 2_500_000, banked: 1_230_000, perHour: 380_000);
            Assert.NotNull(h);
            Assert.Equal(3.342, h.Value, 3);   // 1_270_000 / 380_000
        }

        [Fact]
        public void AVeryLargeShortfallStaysFiniteAndPositive()
        {
            var h = PpEta.HoursTo(long.MaxValue / 2, 0, 1.0);
            Assert.NotNull(h);
            Assert.True(h.Value > 0 && !double.IsInfinity(h.Value));
        }
    }
}
