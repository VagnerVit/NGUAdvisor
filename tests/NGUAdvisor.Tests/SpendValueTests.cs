using System.Collections.Generic;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class SpendValueTests
    {
        private static SpendValue.Candidate Perk(int id, string group, long level, double e, long cost)
        {
            return new SpendValue.Candidate { Id = id, Group = group, Name = "p" + id, Level = level, EffectPerLevel = e, Cost = cost };
        }

        [Fact]
        public void GainIsEffectOverCurrentFactorTimesCost()
        {
            Assert.Equal(1e-5, SpendValue.Gain(0, 0.001, 100), 12);
            Assert.Equal(5e-6, SpendValue.Gain(1000, 0.001, 100), 12);   // factor already 2.0
        }

        [Fact]
        public void ZeroEffectOrCostIsNoGain()
        {
            Assert.Equal(0, SpendValue.Gain(5, 0, 100));
            Assert.Equal(0, SpendValue.Gain(5, 0.01, 0));
        }

        // Live catalog (state export): Rich Perks I 100 PP +0.1%, II 1K PP +0.1%, III 10K PP +0.05%.
        [Fact]
        public void AdventureOneBeatsTwoAndThreeAtEveryLevelItCanReach()
        {
            double one0 = SpendValue.Gain(0, 0.001, 100);
            double oneMax = SpendValue.Gain(999, 0.001, 100);
            double two0 = SpendValue.Gain(0, 0.001, 1000);
            double three0 = SpendValue.Gain(0, 0.0005, 10000);
            Assert.Equal(200.0, one0 / three0, 6);   // 100x the cost for half the effect
            Assert.Equal(10.0, one0 / two0, 6);
            Assert.True(oneMax > two0);              // even the last I level beats the first II level
            Assert.True(two0 > three0);
        }

        [Fact]
        public void RatioIsTakenWithinTheGuideBuysGroupOnly()
        {
            List<SpendValue.Candidate> all = new List<SpendValue.Candidate>
            {
                Perk(1, "E power", 0, 0.01, 100),        // guide: 1e-4
                Perk(2, "E power", 0, 0.01, 10),         // best of the group: 1e-3
                Perk(3, "adventure", 0, 0.5, 1),         // far better, other group
            };
            SpendValue.View v = SpendValue.Compare(all, true, 1, "p1");
            Assert.Equal("E power", v.GuideGroup);
            Assert.Equal("p2", v.BestName);
            Assert.Equal(10.0, v.Ratio, 9);
            Assert.Equal("p3", v.OverallName);
        }

        [Fact]
        public void GuideBuyThatIsTheGroupBestHasRatioOne()
        {
            List<SpendValue.Candidate> all = new List<SpendValue.Candidate> { Perk(1, "E cap", 0, 0.01, 10), Perk(2, "E cap", 0, 0.01, 50) };
            SpendValue.View v = SpendValue.Compare(all, true, 1, "p1");
            Assert.Equal(1.0, v.Ratio, 9);
            Assert.StartsWith("value: guide buy p1 is the best E cap buy", SpendValue.Describe(v, "PP"));
        }

        [Fact]
        public void GuideBuyOutsideTheModelHasNoRatio()
        {
            List<SpendValue.Candidate> all = new List<SpendValue.Candidate> { Perk(2, "E cap", 0, 0.01, 50) };
            SpendValue.View v = SpendValue.Compare(all, true, 99, "bank");
            Assert.Null(v.GuideGroup);
            Assert.True(double.IsNaN(v.Ratio));
            Assert.Contains("feeds no ranked product", SpendValue.Describe(v, "PP"));
        }

        [Fact]
        public void LinesReadAsPercentPerThousandPoints()
        {
            Assert.Equal("1%/1K", SpendValue.PerThousand(1e-5));
        }

        [Fact]
        public void TableHoldsNoIdTwiceInOneGroupSet()
        {
            foreach (IEnumerable<KeyValuePair<string, int[]>> table in new[] { SpendValue.PerkIds(), SpendValue.QuirkIds() })
            {
                HashSet<int> seen = new HashSet<int>();
                foreach (KeyValuePair<string, int[]> g in table)
                    foreach (int id in g.Value) Assert.True(seen.Add(id), "id " + id + " listed twice");
            }
        }
    }
}
