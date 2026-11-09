using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class BeardValueTests
    {
        [Theory]
        [InlineData(BeardValue.Stats, 100, 6.0)]                       // 1 + 100 × 0.05, no softcap
        [InlineData(BeardValue.Number, 1000, 11.0)]                    // 1 + 1000 × 0.01 at the softcap
        [InlineData(BeardValue.Drops, 1000, 1.5)]                      // 1 + 1000 × 0.0005
        public void TempBonusIsLinearToTheSoftcap(int id, double level, double expected)
            => Assert.Equal(expected, BeardValue.Temp(id, level), 9);

        [Fact]
        public void PastTheSoftcapTheGameCurveTakesOver()
        {
            // AllBeardsController.tempNumberBonus: 1 + L^0.5 × 31.7 × 0.01.
            Assert.Equal(1.0 + System.Math.Sqrt(4000) * 31.7 * 0.01, BeardValue.Temp(BeardValue.Number, 4000), 9);
        }

        [Theory]
        [InlineData(1800, 0, 0.0)]       // nothing in the first hour
        [InlineData(10800, 0, 1.0)]
        [InlineData(86400, 0, 8.0)]      // 24 h reaches the cap with perk 21 at 0
        [InlineData(200000, 0, 8.0)]
        public void TimeFactorMirrorsTheGame(double seconds, int perk21, double expected)
            => Assert.Equal(expected, BeardValue.TimeFactor(seconds, perk21), 9);

        [Fact]
        public void TrimmingsAreSqrtOfLevelTimesTimeFactorCappedAtTheLevel()
        {
            Assert.Equal(80.0, BeardValue.Trimmings(100, 8.0));
            Assert.Equal(4.0, BeardValue.Trimmings(4, 8.0));
            Assert.Equal(0.0, BeardValue.Trimmings(100, 0.0));
        }

        [Fact]
        public void LastingGainIsThePermBonusRatio()
        {
            double gain = BeardValue.LastingGain(BeardValue.Stats, 10000, 1000, 8.0);
            Assert.Equal((1.0 + 1800 * 0.01) / (1.0 + 1000 * 0.01), gain, 9);
        }
    }
}
