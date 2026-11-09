using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class HackPlanTests
    {
        // Unreduced levels-per-milestone and hardcaps for hacks 0..14 (guide mechanics/hacks.md).
        private static readonly long[] Thresholds = { 10, 50, 50, 40, 20, 30, 30, 50, 50, 45, 75, 40, 25, 100, 50 };
        private static readonly long[] HardCaps = { 7720, 7632, 7544, 7544, 7456, 7340, 7340, 7252, 7164, 7048, 6960, 6873, 6757, 6757, 6262 };

        private static long[] Levels(params long[] firstSeven)
        {
            long[] levels = new long[15];
            for (int i = 0; i < firstSeven.Length; i++) levels[i] = firstSeven[i];
            return levels;
        }

        [Fact]
        public void BeforeCBlock3_OnlyAttackDefense()
        {
            Assert.Equal(new[] { 0 }, HackPlan.Order(false, true, Levels(59), Thresholds, HardCaps));
        }

        [Fact]
        public void AfterCBlock3_WithoutMaxxedEvidence_OnlyAdventure()
        {
            Assert.Equal(new[] { 1 }, HackPlan.Order(true, false, Levels(59), Thresholds, HardCaps));
        }

        [Fact]
        public void AfterCBlock3_WithMaxxedEvidence_FirstMilestonesOnTmThroughMagicNgu_ThenAdventure()
        {
            Assert.Equal(new[] { 2, 3, 4, 5, 6, 1 }, HackPlan.Order(true, true, Levels(59), Thresholds, HardCaps));
        }

        [Fact]
        public void AHackAtItsFirstMilestoneDropsOut()
        {
            long[] levels = Levels(59, 0, 50, 39, 20, 31, 0);
            Assert.Equal(new[] { 3, 6, 1 }, HackPlan.Order(true, true, levels, Thresholds, HardCaps));
        }

        [Fact]
        public void AllFirstMilestonesReached_FallsBackToAdventure()
        {
            long[] levels = Levels(59, 0, 50, 40, 20, 30, 30);
            Assert.Equal(new[] { 1 }, HackPlan.Order(true, true, levels, Thresholds, HardCaps));
        }

        [Fact]
        public void ReducedThresholdMovesTheTarget()
        {
            long[] reduced = (long[])Thresholds.Clone();
            reduced[2] = 45;   // TM Hack Milestone Reducer at max
            long[] levels = Levels(0, 0, 45, 40, 20, 30, 30);
            Assert.Equal(new[] { 1 }, HackPlan.Order(true, true, levels, reduced, HardCaps));
        }

        [Fact]
        public void UnreadableThresholdIsSkipped()
        {
            long[] broken = (long[])Thresholds.Clone();
            broken[4] = 0;
            long[] levels = Levels(0, 0, 50, 40, 0, 30, 30);
            Assert.Equal(new[] { 1 }, HackPlan.Order(true, true, levels, broken, HardCaps));
        }

        private static HackPlan.Lane MakeLane(int id, long level, long saturation, double b = 0.01,
            double m = 1.0, long threshold = 50, long hardCap = 7000)
        {
            return new HackPlan.Lane
            {
                Id = id, Level = level, HardCap = hardCap, BaseEffectPerLevel = b,
                MilestoneEffect = m, MilestoneThreshold = threshold, Saturation = saturation,
            };
        }

        [Fact]
        public void RankByValue_CheaperLaneWins()
        {
            HackPlan.Lane[] lanes = { MakeLane(1, 100, 1000), MakeLane(2, 100, 10) };
            Assert.Equal(new[] { 2, 1 }, HackPlan.RankByValue(lanes));
        }

        [Fact]
        public void RankByValue_TieKeepsIdOrder()
        {
            HackPlan.Lane[] lanes = { MakeLane(3, 100, 500), MakeLane(2, 100, 500) };
            Assert.Equal(new[] { 2, 3 }, HackPlan.RankByValue(lanes));
        }

        [Fact]
        public void RankByValue_DropsCappedAndUnreadableLanes()
        {
            HackPlan.Lane[] lanes =
            {
                MakeLane(1, 7000, 10), MakeLane(2, 10, 0), MakeLane(3, 10, 10, b: 0.0), MakeLane(4, 10, 10),
            };
            Assert.Equal(new[] { 4 }, HackPlan.RankByValue(lanes));
        }

        [Fact]
        public void LaneValue_MilestoneStepRaisesValueNearTheMilestone()
        {
            double far = HackPlan.LaneValue(MakeLane(2, 1, 100, m: 1.5, threshold: 50));
            double near = HackPlan.LaneValue(MakeLane(2, 48, 100, m: 1.5, threshold: 50));
            Assert.True(near > far);
        }

        [Fact]
        public void ValueRatio_BestOverGuide()
        {
            HackPlan.Lane[] lanes = { MakeLane(1, 100, 1000), MakeLane(2, 100, 10) };
            Assert.Equal(100.0, HackPlan.ValueRatio(lanes, 2, 1), 6);
        }

        [Fact]
        public void ValueRatio_GuideLaneWithoutValueIsZero()
        {
            HackPlan.Lane[] lanes = { MakeLane(1, 7000, 1000), MakeLane(2, 100, 10) };
            Assert.Equal(0.0, HackPlan.ValueRatio(lanes, 2, 1));
        }

        [Fact]
        public void ShortArraysAreSkippedNotThrown()
        {
            Assert.Equal(new[] { 2, 3, 1 }, HackPlan.Order(true, true, new long[4], Thresholds, HardCaps));
        }
    }
}
