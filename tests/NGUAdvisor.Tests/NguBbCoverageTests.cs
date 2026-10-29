using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class NguBbCoverageTests
    {
        [Fact]
        public void SwappingAnItemMovesTheGameTotalByItsShareOfTheWholeBonusSum()
        {
            // Game sum 3.0 (gear + set bonuses); the respawn ring replaces a 50 % NGU item.
            double factor = NguBbCoverage.SwapFactor(3.0, 4.0, 3.5);
            Assert.Equal(3.5 / 4.0, factor, 12);
        }

        [Fact]
        public void SameSetLeavesTheTotalUnchanged()
        {
            Assert.Equal(1.0, NguBbCoverage.SwapFactor(2.7, 3.1, 3.1), 12);
        }

        [Fact]
        public void ASetWeakerInAnyFactorIsNotFreeByItself()
        {
            Assert.True(NguBbCoverage.NoWeaker(new[] { 2.0, 1.5, 3.0 }, new[] { 2.0, 1.5, 3.0 }));
            Assert.False(NguBbCoverage.NoWeaker(new[] { 2.0, 1.5, 2.9 }, new[] { 2.0, 1.5, 3.0 }));
        }

        [Fact]
        public void OnlyALowerCapIsChargedToTheLanes()
        {
            Assert.Equal(-100.0, NguBbCoverage.CapChange(1000, 0.9), 9);
            Assert.Equal(0.0, NguBbCoverage.CapChange(1000, 1.2), 9);
        }

        [Fact]
        public void CoverageNeedsTheHeadroomOnTopOfEveryLanesBb()
        {
            double[] bbs = { 40e9, 50e9, 10e9 };
            Assert.True(NguBbCoverage.Covers(102e9, bbs, 1.02));
            Assert.False(NguBbCoverage.Covers(101e9, bbs, 1.02));
        }
    }
}
