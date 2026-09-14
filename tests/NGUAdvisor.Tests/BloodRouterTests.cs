using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class BloodRouterTests
    {
        private static SinkVerdict Judge(SinkMode mode, int target = 0, int now = 0, bool windowOpen = true,
                                         bool feasible = true, bool demand = true, bool belowKnee = true)
            => BloodRouter.JudgeSink(mode, target, now, windowOpen, () => feasible, () => demand, () => belowKnee);

        [Fact]
        public void OffOutranksEveryOtherGate()
            => Assert.Equal(SinkVerdict.Off, Judge(SinkMode.Off));

        [Fact]
        public void CeilingReachedDropsTheSink()
            => Assert.Equal(SinkVerdict.TargetReached, Judge(SinkMode.Push, target: 500, now: 500));

        [Fact]
        public void ZeroCeilingMeansNoCeiling()
            => Assert.Equal(SinkVerdict.Eligible, Judge(SinkMode.Auto, target: 0, now: 999999));

        // The user-reported bug: Counterfeit set to 500 %, current bonus far below it, and the cost-curve
        // knee alone kept handing the pool back to NUMBER. Push is the answer to exactly that.
        [Fact]
        public void PushOverrulesDemandAndKnee()
        {
            Assert.Equal(SinkVerdict.NoDemand, Judge(SinkMode.Auto, demand: false));
            Assert.Equal(SinkVerdict.PastKnee, Judge(SinkMode.Auto, belowKnee: false));
            Assert.Equal(SinkVerdict.Eligible, Judge(SinkMode.Push, demand: false, belowKnee: false));
        }

        // Gold and loot are wiped by bloodMagicController.reset() at rebirth, so the window is arithmetic,
        // not opinion — Push does not get to overrule it.
        [Fact]
        public void PushDoesNotOverruleTheInvestmentWindowOrFeasibility()
        {
            Assert.Equal(SinkVerdict.WindowClosed, Judge(SinkMode.Push, windowOpen: false));
            Assert.Equal(SinkVerdict.NotFeasible, Judge(SinkMode.Push, feasible: false));
        }

        [Fact]
        public void NumberFloorOutranksEverySink()
            => Assert.Equal(BloodRoute.NumberFloor, BloodRouter.DecideRoute(true, 100, 50,
                SinkMode.Push, SinkVerdict.Eligible, SinkMode.Push, SinkVerdict.Eligible));

        [Fact]
        public void FloorIsIgnoredOnceReached()
            => Assert.Equal(BloodRoute.Gold, BloodRouter.DecideRoute(true, 100, 100,
                SinkMode.Auto, SinkVerdict.Eligible, SinkMode.Auto, SinkVerdict.Eligible));

        [Fact]
        public void PushSinkOutranksAnAutoSink()
            => Assert.Equal(BloodRoute.Loot, BloodRouter.DecideRoute(true, 0, 1,
                SinkMode.Auto, SinkVerdict.Eligible, SinkMode.Push, SinkVerdict.Eligible));

        [Fact]
        public void GoldOutranksLootWithinATier()
            => Assert.Equal(BloodRoute.Gold, BloodRouter.DecideRoute(true, 0, 1,
                SinkMode.Auto, SinkVerdict.Eligible, SinkMode.Auto, SinkVerdict.Eligible));

        [Fact]
        public void NumberIsTheDefaultSinkAndIdleOnlyWithoutARebirth()
        {
            Assert.Equal(BloodRoute.NumberDefault, BloodRouter.DecideRoute(true, 0, 1,
                SinkMode.Auto, SinkVerdict.NoDemand, SinkMode.Auto, SinkVerdict.NoDemand));
            Assert.Equal(BloodRoute.Idle, BloodRouter.DecideRoute(false, 0, 1,
                SinkMode.Auto, SinkVerdict.NoDemand, SinkMode.Auto, SinkVerdict.NoDemand));
        }

        // The flip-flop fix: a sink that lost on a gate which will fix itself keeps the pool until the
        // dwell expires, but one switched off or finished hands it over immediately.
        [Fact]
        public void DwellHoldsAcrossASelfHealingGateOnly()
        {
            Assert.True(BloodRouter.HoldPrevious(BloodRoute.Gold, SinkVerdict.PastKnee, SinkVerdict.Off, 60, 300));
            Assert.True(BloodRouter.HoldPrevious(BloodRoute.Gold, SinkVerdict.NoDemand, SinkVerdict.Off, 60, 300));
            Assert.False(BloodRouter.HoldPrevious(BloodRoute.Gold, SinkVerdict.Off, SinkVerdict.Off, 60, 300));
            Assert.False(BloodRouter.HoldPrevious(BloodRoute.Gold, SinkVerdict.TargetReached, SinkVerdict.Off, 60, 300));
            Assert.False(BloodRouter.HoldPrevious(BloodRoute.Gold, SinkVerdict.WindowClosed, SinkVerdict.Off, 60, 300));
        }

        [Fact]
        public void DwellExpires()
            => Assert.False(BloodRouter.HoldPrevious(BloodRoute.Gold, SinkVerdict.PastKnee, SinkVerdict.Off, 301, 300));
    }
}
