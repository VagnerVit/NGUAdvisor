using System.Linq;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class NguBbSplitTests
    {
        private static System.Func<int, int, double> Flat(params double[] bb) => (lane, k) => bb[lane];

        private static double Rate(int[] ticks) => ticks.Where(k => k > 0).Sum(k => 1.0 / k);

        [Fact]
        public void PoolOfOneAndAHalfBbPutsTheFirstLaneAtBb()
        {
            // The equal split runs both lanes at one level per 2 ticks and idles a third of the pool;
            // BB first is 1 + 1/3 levels per tick instead of 1/2 + 1/2.
            var plan = NguBbSplit.Plan(Flat(17.48e9, 17.56e9), 2, 26.22e9);
            Assert.Equal(new[] { 1, 3 }, plan.Ticks);
            Assert.True(Rate(plan.Ticks) > Rate(new[] { 2, 2 }));
            Assert.True(plan.Amounts.Sum() <= 26.22e9);
        }

        [Fact]
        public void PoolFarBelowBbKeepsTheGuideSplit()
        {
            var plan = NguBbSplit.Plan(Flat(100e9, 100e9), 2, 26e9);
            Assert.Equal(new[] { 8, 8 }, plan.Ticks);
        }

        [Fact]
        public void PoolAboveEveryBbFundsAllAtBbAndLeavesTheRest()
        {
            var plan = NguBbSplit.Plan(Flat(10e9, 12e9), 2, 40e9);
            Assert.Equal(new[] { 1, 1 }, plan.Ticks);
            Assert.True(plan.Amounts.Sum() < 23e9);
        }

        [Fact]
        public void SplitLeftoverBuysTheNextBreakpointInLaneOrder()
        {
            // Equal shares of 12.5 reach one level per 3 ticks (10 each); the 5 left over
            // lifts the first lane to one per 2 ticks.
            var plan = NguBbSplit.Plan(Flat(30e9, 30e9), 2, 25.1e9);
            Assert.Equal(new[] { 2, 3 }, plan.Ticks);
        }

        [Fact]
        public void BbPassStopsAtTheFirstLaneItCannotFund()
        {
            // The first lane cannot reach BB, so nothing jumps the queue: equal split, then the
            // leftover upgrades the first lane.
            var plan = NguBbSplit.Plan(Flat(30e9, 10e9), 2, 25.1e9);
            Assert.Equal(new[] { 2, 1 }, plan.Ticks);
        }

        [Fact]
        public void BbThatGrowsWithLookaheadIsPricedAtTheTargetSpeed()
        {
            // A faster lane looks further ahead (more levels in its window), so its BB is higher.
            System.Func<int, int, double> bb = (lane, k) => k == 1 ? 20e9 : 18e9;
            var plan = NguBbSplit.Plan(bb, 1, 19e9);
            Assert.Equal(new[] { 2 }, plan.Ticks);
        }

        [Fact]
        public void PoolFarBelowBbFindsTheSpeedWithoutWalkingEveryTick()
        {
            // Right after a rebirth the pool is a sliver of BB, so lanes land at millions of ticks per
            // level, and each bbAt call reads dozens of game getters.
            int calls = 0;
            System.Func<int, int, double> bb = (lane, k) => { calls++; return 1e12 * (1 + lane) * (1 + 500 / k); };
            var plan = NguBbSplit.Plan(bb, 3, 1e7);
            Assert.True(calls < 1000, $"{calls} bbAt calls");
            Assert.All(plan.Ticks, k => Assert.True(k > 100000));
            Assert.True(plan.Amounts.Sum() <= 1e7);
        }

        [Fact]
        public void EmptyPoolFundsNothing()
        {
            var plan = NguBbSplit.Plan(Flat(10e9, 10e9), 2, 0);
            Assert.Equal(new[] { 0, 0 }, plan.Ticks);
            Assert.Equal(new[] { 0L, 0L }, plan.Amounts);
        }
    }
}
