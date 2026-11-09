using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class FruitUpgradeValueTests
    {
        [Fact]
        public void TierFactorIsTheCeilingOfTheThreeHalvesPower()
        {
            Assert.Equal(1, FruitUpgradeValue.TierFactor(1));
            Assert.Equal(3, FruitUpgradeValue.TierFactor(2));
            Assert.Equal(32, FruitUpgradeValue.TierFactor(10));
        }

        [Fact]
        public void UpgradeCostIsBaseTimesCeilingOfSquare()
        {
            Assert.Equal(100 * 9, FruitUpgradeValue.UpgradeCost(100, 2));
        }

        [Fact]
        public void ReturnPerSeedFallsWithTierAndVanishesAtTheCap()
        {
            double low = FruitUpgradeValue.RelativeGainPerSeed(100, 2, 10);
            double high = FruitUpgradeValue.RelativeGainPerSeed(100, 8, 10);
            Assert.True(low > high && high > 0.0);
            Assert.Equal(0.0, FruitUpgradeValue.RelativeGainPerSeed(100, 10, 10));
            Assert.Equal(0.0, FruitUpgradeValue.RelativeGainPerSeed(100, 0, 10));
        }
    }
}
