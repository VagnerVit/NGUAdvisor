using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    // ItopodPerkController.awardHighestLevelPP and the climb it takes to collect it.
    public class ItopodFirstReachBonusTests
    {
        [Theory]
        [InlineData(10, 1)]
        [InlineData(100, 10)]
        [InlineData(110, 2)]
        [InlineData(621, 0)]
        [InlineData(630, 7)]
        [InlineData(690, 7)]
        [InlineData(700, 70)]
        [InlineData(1000, 100)]
        [InlineData(1600, 160)]
        [InlineData(0, 0)]
        public void BonusMatchesAwardHighestLevelPP(int floor, int expected)
        {
            Assert.Equal(expected, ItopodRewards.FirstReachBonusPp(floor));
        }

        [Theory]
        [InlineData(621, 630)]
        [InlineData(630, 640)]
        [InlineData(0, 10)]
        public void NextBonusFloorIsTheNextUnclaimedTen(int highest, int expected)
        {
            Assert.Equal(expected, ItopodRewards.NextBonusFloor(highest));
        }

        private static double PpPerKill(int floor)
            => ItopodRewards.PpPerKill(floor, ItopodRewards.Difficulty.Evil, 100.0);

        // Start is capped at highest − 1 and the level advances once per 10 kills: 621 → 630 is
        // 10 kills on each of 620..629.
        [Fact]
        public void ClimbCostsTenKillsPerFloorFromHighestMinusOne()
        {
            ItopodRewards.BonusClaim claim = ItopodRewards.BestBonusClaim(621, 630, 0.0, f => 2.0, PpPerKill);
            Assert.NotNull(claim);
            Assert.Equal(630, claim.Target);
            Assert.Equal(100, claim.Kills);
            Assert.Equal(200.0, claim.Seconds, 6);
            Assert.Equal(7.0, claim.BonusPp, 6);
        }

        // One-shot cadence on every climb floor: the climb pays at least the farm rate, so the
        // whole reachable run is taken and every bonus on the way (7 × 7 + 70) is collected.
        [Fact]
        public void FreeClimbTakesTheFarthestBonus()
        {
            double farmPps = PpPerKill(620) / 2.0;
            ItopodRewards.BonusClaim claim = ItopodRewards.BestBonusClaim(621, 705, farmPps, f => 2.0, PpPerKill);
            Assert.NotNull(claim);
            Assert.Equal(700, claim.Target);
            Assert.Equal(800, claim.Kills);
            Assert.Equal(119.0, claim.BonusPp, 6);
        }

        [Fact]
        public void SlowClimbAgainstARichFarmIsSkipped()
        {
            ItopodRewards.BonusClaim claim = ItopodRewards.BestBonusClaim(621, 700, 10.0, f => 60.0, PpPerKill);
            Assert.Null(claim);
        }

        [Fact]
        public void TargetAboveTheWonFightsIsNeverChosen()
        {
            Assert.Null(ItopodRewards.BestBonusClaim(621, 629, 0.0, f => 2.0, PpPerKill));
        }

        [Fact]
        public void UnkillableFloorStopsTheClimb()
        {
            ItopodRewards.BonusClaim claim = ItopodRewards.BestBonusClaim(621, 705, 0.0,
                f => f >= 650 ? double.PositiveInfinity : 2.0, PpPerKill);
            Assert.NotNull(claim);
            Assert.Equal(650, claim.Target);
        }

        // A 100-kill climb for 7 PP vs. 800 kills for 119 PP: the second wins on net even when the
        // first alone is also positive.
        [Fact]
        public void PicksTheBestNetNotTheNearest()
        {
            double farmPps = PpPerKill(620) / 2.0 + 0.0002;
            ItopodRewards.BonusClaim claim = ItopodRewards.BestBonusClaim(621, 700, farmPps, f => 2.0, PpPerKill);
            Assert.NotNull(claim);
            Assert.Equal(700, claim.Target);
            Assert.True(claim.NetPp > 0.0);
        }

        [Fact]
        public void KillSecondsIsOneCycleWhenTheRotationOneShots()
        {
            ItopodConstants.Fighter f = new ItopodConstants.Fighter
            {
                Attack = 1e30, MaxHp = 1.0, RegularMultiplier = 1.0, GlobalCooldownSeconds = 0.8,
            };
            Assert.Equal(1.0, ItopodConstants.ExpectedKillSeconds(f, 600, 1.0), 6);
        }

        [Fact]
        public void KillSecondsIsInfiniteWhenRegenOutpacesUs()
        {
            ItopodConstants.Fighter f = new ItopodConstants.Fighter
            {
                Attack = 1.0, MaxHp = 1.0, RegularMultiplier = 1.0, GlobalCooldownSeconds = 0.8,
            };
            Assert.True(double.IsPositiveInfinity(ItopodConstants.ExpectedKillSeconds(f, 600, 1.0)));
        }
    }
}
