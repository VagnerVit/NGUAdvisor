using System.Collections.Generic;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class ChallengeGatesTests
    {
        private static Dictionary<string, int> Have(int tc, int noaug)
            => new Dictionary<string, int> { { "TC", tc }, { "NOAUG", noaug } };

        [Fact]
        public void PicksTheBiggestMultiplierFirst()
        {
            ChallengeGates.Gate? g = ChallengeGates.Next(Have(0, 0));
            Assert.True(g.HasValue);
            Assert.Equal("TC", g.Value.Key);
            Assert.Equal(1, g.Value.Needed);
            Assert.Equal(3.0, g.Value.Multiplier);
        }

        [Fact]
        public void FallsToTheSmallerMultiplierOnceTheBigOneIsMet()
        {
            ChallengeGates.Gate? g = ChallengeGates.Next(Have(1, 0));
            Assert.True(g.HasValue);
            Assert.Equal("NOAUG", g.Value.Key);
        }

        [Fact]
        public void UnlocksComeAfterEveryRateReward()
        {
            ChallengeGates.Gate? g = ChallengeGates.Next(Have(1, 1));
            Assert.True(g.HasValue);
            Assert.Equal(0.0, g.Value.Multiplier);
            Assert.Equal(5, g.Value.Needed);   // nearest unlock rung, not the furthest
        }

        [Fact]
        public void ClimbsTheLadderOneRungAtATime()
        {
            Assert.Equal(6, ChallengeGates.Next(Have(5, 1)).Value.Needed);
            Assert.Equal(7, ChallengeGates.Next(Have(6, 1)).Value.Needed);
        }

        [Fact]
        public void NothingLeftWhenEveryGateIsMet()
        {
            Assert.Null(ChallengeGates.Next(Have(7, 1)));
        }

        [Fact]
        public void UnreadableChallengeIsSkippedNotGuessed()
        {
            // Only NOAUG could be read: the TC rungs must not be advised on a missing count.
            ChallengeGates.Gate? g = ChallengeGates.Next(new Dictionary<string, int> { { "NOAUG", 0 } });
            Assert.True(g.HasValue);
            Assert.Equal("NOAUG", g.Value.Key);
            Assert.Null(ChallengeGates.Next(new Dictionary<string, int> { { "NOAUG", 1 } }));
        }

        [Fact]
        public void NullCompletionsAdviseNothing()
        {
            Assert.Null(ChallengeGates.Next(null));
        }

        [Fact]
        public void EveryGateNamesItsSource()
        {
            foreach (ChallengeGates.Gate g in ChallengeGates.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(g.Source));
                Assert.False(string.IsNullOrWhiteSpace(g.Reward));
                Assert.True(g.Needed > 0);
            }
        }
    }
}
