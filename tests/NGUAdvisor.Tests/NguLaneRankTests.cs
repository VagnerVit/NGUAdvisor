using System;
using System.Collections.Generic;
using System.Linq;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class NguLaneRankTests
    {
        // Live energy tree 2026-10-29 (cap 1.08T): BB per lane from the auto profile, ratios at BB.
        private static readonly NguLaneRank.Lane[] LiveEnergy =
        {
            Lane(8, 1.030, 688e9),   // PP
            Lane(7, 1.025, 56e9),    // Magic NGU
            Lane(6, 1.010, 4.34e9),  // Drop Chance
            Lane(3, 1.004, 64e6),    // Gold
            Lane(4, 1.003, 89e6),    // Adv-α
            Lane(0, 1.002, 64e6),    // Augs
            Lane(5, 1.002, 64e6),    // Power-α
            Lane(1, 1.001, 64e6),    // Wandoos
        };

        private static NguLaneRank.Lane Lane(int id, double ratio, double bb)
            => new NguLaneRank.Lane { Id = id, RatioAtBb = ratio, BbCost = bb };

        private struct Token
        {
            public int Id;
            public bool Cap;
        }

        // The rating-only plan this replaces: the top two by ratio run as NGU-*, the rest trail as CAPNGU-*.
        private static List<Token> RatingTokens(NguLaneRank.Lane[] lanes)
        {
            int[] byRatio = lanes.OrderByDescending(l => l.RatioAtBb).Select(l => l.Id).ToArray();
            return byRatio.Select((id, i) => new Token { Id = id, Cap = i >= 2 }).ToList();
        }

        // ChallengeOverlay.ChapterNguLanes: every lane in value-per-BB order, the hot pair as NGU-*.
        private static List<Token> ValueTokens(NguLaneRank.Lane[] lanes)
        {
            int[] hot = NguLaneRank.Top(lanes, 2, null);
            return NguLaneRank.Order(lanes).Select(id => new Token { Id = id, Cap = !hot.Contains(id) }).ToList();
        }

        private static long Need(double bb) => (long)Math.Ceiling(bb * NguBbSplit.RoundingHeadroom);

        // EnergyBreakpoints.PerformSwap in miniature: CAP tokens take min(BB, idle) at their turn, the
        // non-cap lanes are re-split by NguBbSplit right after the last of them.
        private static Dictionary<int, long> Walk(List<Token> tokens, NguLaneRank.Lane[] lanes, double pool)
        {
            Dictionary<int, double> bb = lanes.ToDictionary(l => l.Id, l => l.BbCost);
            Dictionary<int, long> held = new Dictionary<int, long>();
            double left = pool;
            int prioCount = tokens.Count(t => !t.Cap);
            int rebalanceAt = NguBbSplit.RebalanceAfter(tokens.Count, i => tokens[i].Cap, i => true);
            for (int i = 0; i < tokens.Count; i++)
            {
                Token t = tokens[i];
                long take = t.Cap ? Math.Min(Need(bb[t.Id]), (long)left) : Math.Min(Need(bb[t.Id]), (long)(left / prioCount));
                held[t.Id] = take;
                left -= take;
                if (!t.Cap) prioCount--;
                if (i != rebalanceAt) continue;

                List<int> split = tokens.Take(i + 1).Where(x => !x.Cap).Select(x => x.Id).ToList();
                double splitPool = left + split.Sum(id => held[id]);
                NguBbSplit.Result plan = NguBbSplit.Plan((lane, k) => bb[split[lane]], split.Count, splitPool);
                for (int j = 0; j < split.Count; j++) held[split[j]] = plan.Amounts[j];
                left = splitPool - plan.Amounts.Sum();
            }
            return held;
        }

        [Fact]
        public void BbCostIsTheGameBreakpointAtTheAllocationLookahead()
        {
            // NGUBP.EnergyBb(500): ceil(divider × (level + 1 + 500) / (power × speed stack)), floored at 1.
            Assert.Equal(Math.Ceiling(3e6 * 1500.0 / (2e5 * 4.0)), NguLaneRank.BbCost(3e6, 999, 2e5, 4.0));
            Assert.Equal(1.0, NguLaneRank.BbCost(1, 0, 1e9, 1e9));
        }

        [Fact]
        public void LaneWithoutGrowthScoresNothing()
        {
            Assert.Equal(0.0, NguLaneRank.Score(Lane(2, 1.0, 10)));
            Assert.True(NguLaneRank.Score(Lane(3, 1.001, 64e6)) > NguLaneRank.Score(Lane(8, 1.03, 688e9)));
        }

        [Fact]
        public void CheapLanesRankAheadOfTenThousandTimesDearerOnes()
        {
            int[] order = NguLaneRank.Order(LiveEnergy);
            Assert.Equal(new[] { 3, 4, 0, 5, 1, 6, 7, 8 }, order);
        }

        [Fact]
        public void PoolCoveringEveryLaneAllocatesExactlyAsTheRatingOrderDid()
        {
            double pool = LiveEnergy.Sum(l => (double)Need(l.BbCost)) + 50e9;
            Dictionary<int, long> before = Walk(RatingTokens(LiveEnergy), LiveEnergy, pool);
            Dictionary<int, long> after = Walk(ValueTokens(LiveEnergy), LiveEnergy, pool);
            foreach (NguLaneRank.Lane l in LiveEnergy)
            {
                Assert.Equal(Need(l.BbCost), before[l.Id]);
                Assert.Equal(before[l.Id], after[l.Id]);
            }
        }

        [Fact]
        public void ShortPoolBbsTheCheapLanesInsteadOfStarvingThem()
        {
            const double afterRebirth = 400e6;
            int[] cheap = { 3, 4, 0, 5, 1 };
            Dictionary<int, long> before = Walk(RatingTokens(LiveEnergy), LiveEnergy, afterRebirth);
            Dictionary<int, long> after = Walk(ValueTokens(LiveEnergy), LiveEnergy, afterRebirth);
            Assert.All(cheap, id => Assert.Equal(0, before[id]));
            Assert.All(cheap, id => Assert.Equal(Need(LiveEnergy.First(l => l.Id == id).BbCost), after[id]));
        }

        [Fact]
        public void CapLaneWalkedAheadOfAHotLaneKeepsItsBb()
        {
            NguLaneRank.Lane[] lanes = { Lane(0, 1.002, 64e6), Lane(1, 1.002, 64e6), Lane(8, 1.03, 688e9) };
            List<Token> tokens = new List<Token>
            {
                new Token { Id = 0 }, new Token { Id = 1, Cap = true }, new Token { Id = 8 },
            };
            Assert.Equal(2, NguBbSplit.RebalanceAfter(tokens.Count, i => tokens[i].Cap, i => true));
            Dictionary<int, long> held = Walk(tokens, lanes, 200e6);
            Assert.Equal(Need(64e6), held[1]);
            Assert.Equal(Need(64e6), held[0]);
        }

        [Fact]
        public void NearEqualChallengerDoesNotTakeAnIncumbentSlot()
        {
            NguLaneRank.Lane[] lanes = { Lane(0, 1.0021, 64e6), Lane(1, 1.0020, 64e6), Lane(5, 1.0020, 64e6) };
            Assert.Equal(new[] { 0, 1 }, NguLaneRank.Top(lanes, 2, null));
            Assert.Equal(new[] { 0, 5 }, NguLaneRank.Top(lanes, 2, new[] { 0, 5 }));
        }

        [Fact]
        public void ClearlyBetterChallengerTakesTheSlot()
        {
            NguLaneRank.Lane[] lanes = { Lane(0, 1.002, 64e6), Lane(1, 1.002, 64e6), Lane(5, 1.002, 32e6) };
            Assert.Equal(new[] { 5, 0 }, NguLaneRank.Top(lanes, 2, new[] { 0, 1 }));
        }
    }
}
