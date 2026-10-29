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

        private static readonly string[] AllCodes = { "BASIC", "NOAUG", "24HR", "100LC", "NOEC", "TC", "NORB", "LSC", "BLIND", "NONGU", "NOTM" };

        // Every challenge maxed on Normal, the guide's CBlock 3 Evil list met exactly.
        private static Dictionary<string, ChallengeGates.Completions> CBlock3Done()
        {
            Dictionary<string, ChallengeGates.Completions> all = new Dictionary<string, ChallengeGates.Completions>();
            foreach (string code in AllCodes)
                all[code] = new ChallengeGates.Completions { Evil = 0, Normal = 10, Max = 10 };
            foreach (string code in new[] { "BASIC", "NOAUG", "100LC" }) Set(all, code, 10);
            Set(all, "TC", 1);
            Set(all, "NONGU", 1);
            Set(all, "NOTM", 1);
            Set(all, "NORB", 6);
            Set(all, "BLIND", 1);
            return all;
        }

        private static void Set(Dictionary<string, ChallengeGates.Completions> all, string code, int evil)
        {
            ChallengeGates.Completions c = all[code];
            c.Evil = evil;
            all[code] = c;
        }

        [Fact]
        public void CBlock3_CompleteWhenTheGuideListIsMet()
        {
            Assert.True(ChallengeGates.CBlock3Complete(CBlock3Done()));
        }

        [Theory]
        [InlineData("BASIC", 9)]
        [InlineData("100LC", 9)]
        [InlineData("NOAUG", 9)]
        [InlineData("TC", 0)]
        [InlineData("NORB", 5)]
        [InlineData("BLIND", 0)]
        [InlineData("NONGU", 0)]
        [InlineData("NOTM", 0)]
        public void CBlock3_IncompleteWhenAnEvilRowIsShort(string code, int evil)
        {
            Dictionary<string, ChallengeGates.Completions> all = CBlock3Done();
            Set(all, code, evil);
            Assert.False(ChallengeGates.CBlock3Complete(all));
        }

        [Fact]
        public void CBlock3_IncompleteWhileANormalChallengeIsUnfinished()
        {
            Dictionary<string, ChallengeGates.Completions> all = CBlock3Done();
            all["LSC"] = new ChallengeGates.Completions { Evil = 0, Normal = 9, Max = 10 };
            Assert.False(ChallengeGates.CBlock3Complete(all));
        }

        [Fact]
        public void CBlock3_UnreadMaxOrMissingRowIsIncomplete()
        {
            Dictionary<string, ChallengeGates.Completions> unread = CBlock3Done();
            unread["24HR"] = new ChallengeGates.Completions { Evil = 0, Normal = 0, Max = 0 };
            Assert.False(ChallengeGates.CBlock3Complete(unread));

            Dictionary<string, ChallengeGates.Completions> missing = CBlock3Done();
            missing.Remove("NORB");
            Assert.False(ChallengeGates.CBlock3Complete(missing));
            Assert.False(ChallengeGates.CBlock3Complete(null));
        }

        [Fact]
        public void CBlock3_FixedCountIsClampedToTheChallengeMax()
        {
            Dictionary<string, ChallengeGates.Completions> all = CBlock3Done();
            all["NORB"] = new ChallengeGates.Completions { Evil = 4, Normal = 4, Max = 4 };
            Assert.True(ChallengeGates.CBlock3Complete(all));
        }
    }
}
