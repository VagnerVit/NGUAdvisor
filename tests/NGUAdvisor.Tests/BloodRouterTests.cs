using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class BloodRouterTests
    {
        private const double MinGold = 1e6, MinLoot = 1e4;   // RebirthPowerSpell.minGoldBlood / minLootBlood

        private static SinkInput Sink(SinkMode mode, double invested = 0, bool gold = true)
            => new SinkInput { Mode = mode, Invested = invested, MinBlood = gold ? MinGold : MinLoot };

        private static BudgetInput Run(SinkInput gold, SinkInput loot, double onHand = 0, double bps = 0,
                                       double horizon = 0, bool eligible = true, double rebirthPower = 1)
            => new BudgetInput
            {
                BloodOnHand = onHand,
                Bps = bps,
                HorizonSec = horizon,
                NumberEligible = eligible,
                RebirthPower = rebirthPower,
                Gold = gold,
                Loot = loot
            };

        [Theory]
        [InlineData(1e6, 1)]
        [InlineData(2e6, 4)]
        [InlineData(4e6, 9)]
        [InlineData(999999, 0)]
        public void GoldPercentIsTheGameFormula(double invested, int pct)
            => Assert.Equal(pct, BloodRouter.GoldPct(invested, MinGold));

        [Theory]
        [InlineData(1e4, 1)]
        [InlineData(2e4, 2)]
        [InlineData(8e4, 4)]
        [InlineData(9999, 0)]
        public void LootPercentIsTheGameFormula(double invested, int pct)
            => Assert.Equal(pct, BloodRouter.LootPct(invested, MinLoot));

        [Fact]
        public void FreshRunSplitsIntoThirds()
        {
            BudgetPlan p = BloodRouter.Plan(Run(Sink(SinkMode.On), Sink(SinkMode.On, gold: false), bps: 1e9, horizon: 3000));
            Assert.Equal(3, p.Spells);
            Assert.Equal(1e12, p.Share, 0);
            Assert.Equal(BloodRouter.GoldBloodFor(BloodRouter.GoldPct(1e12, MinGold), MinGold), p.Gold.TargetBlood, 0);
            Assert.Equal(BloodRouter.LootBloodFor(BloodRouter.LootPct(1e12, MinLoot), MinLoot), p.Loot.TargetBlood, 0);
            Assert.Equal(3e12 - p.Gold.TargetBlood - p.Loot.TargetBlood, p.NumberTarget, 0);
        }

        // Blood already cast cannot be taken back: a spell above the level keeps it, the others level up.
        [Fact]
        public void OvershootingSpellKeepsItsBloodAndTheRestLevels()
        {
            // The 2026-09-23 run: NUMBER holds 125T, Counterfeit 17.1T, Spaghetti 0.8T, 30T still to come.
            BudgetPlan p = BloodRouter.Plan(Run(Sink(SinkMode.On, 17.1e12), Sink(SinkMode.On, 0.8e12, gold: false),
                                                onHand: 30e12, rebirthPower: 1 + 125e12));
            Assert.Equal((17.1e12 + 0.8e12 + 30e12) / 2, p.Share, 0);
            Assert.Equal(BloodRouter.GoldPct(p.Share, MinGold), p.Gold.TargetPct);
            Assert.Equal(BloodRouter.LootPct(p.Share, MinLoot), p.Loot.TargetPct);
            Assert.True(p.Gold.TargetBlood <= p.Share && p.Loot.TargetBlood <= p.Share);
            Assert.Equal(125e12 + 30e12 - p.Gold.Deficit - p.Loot.Deficit, p.NumberTarget, 0);
        }

        [Fact]
        public void TooLittleBloodFillsTheEmptiestFirst()
        {
            BudgetPlan p = BloodRouter.Plan(Run(Sink(SinkMode.On, 10e12), Sink(SinkMode.On, 0, gold: false),
                                                onHand: 4e12, rebirthPower: 1 + 20e12));
            Assert.Equal(10e12, p.Gold.TargetBlood, 0);
            Assert.Equal(BloodRouter.LootBloodFor(BloodRouter.LootPct(4e12, MinLoot), MinLoot), p.Loot.TargetBlood, 0);
            Assert.Equal(BloodRoute.Loot, p.Route);
        }

        [Fact]
        public void OffSpellIsLeftOutOfTheSplit()
        {
            BudgetPlan p = BloodRouter.Plan(Run(Sink(SinkMode.On), Sink(SinkMode.Off, 5e12, gold: false), onHand: 8e12));
            Assert.Equal(2, p.Spells);
            Assert.Equal(4e12, p.Share, 0);
            Assert.Equal(5e12, p.Loot.TargetBlood, 0);
            Assert.Equal(0, p.Loot.Deficit);
            Assert.Equal(0, p.Loot.NextStepCost);
        }

        [Fact]
        public void InRunSpellsAreFilledBeforeNumber()
        {
            BudgetPlan p = BloodRouter.Plan(Run(Sink(SinkMode.On), Sink(SinkMode.On, gold: false), onHand: 3e12));
            Assert.NotEqual(BloodRoute.Number, p.Route);
            BudgetPlan level = BloodRouter.Plan(Run(Sink(SinkMode.On, 1e12), Sink(SinkMode.On, 1e12, gold: false),
                                                    onHand: 1e12, rebirthPower: 1));
            Assert.Equal(BloodRoute.Number, level.Route);
        }

        [Fact]
        public void CheaperNextStepGoesFirst()
        {
            // Both short of their share; Spaghetti's first step (1e4) is cheaper than Counterfeit's (1e6).
            BudgetPlan p = BloodRouter.Plan(Run(Sink(SinkMode.On), Sink(SinkMode.On, gold: false), onHand: 3e12));
            Assert.True(p.Gold.Deficit > 0 && p.Loot.Deficit > 0);
            Assert.Equal(BloodRoute.Loot, p.Route);
        }

        // The user rule: no blood into a spell until it buys a whole +1 %. Between two thresholds the
        // target stays on the lower one and the rest banks into NUMBER.
        [Fact]
        public void ShareBetweenStepsSnapsDownToTheLastWholePercent()
        {
            double at27 = BloodRouter.LootBloodFor(27, MinLoot);
            double at28 = BloodRouter.LootBloodFor(28, MinLoot);
            // A share between 27 % and 28 % buys nothing more: no deficit, no step, NUMBER takes it.
            BudgetPlan p = BloodRouter.Plan(Run(Sink(SinkMode.Off), Sink(SinkMode.On, at27, gold: false),
                                                onHand: 2 * (at27 + at28) / 2 - at27, rebirthPower: 1));
            Assert.Equal(27, p.Loot.TargetPct);
            Assert.Equal(0, p.Loot.Deficit);
            Assert.Equal(BloodRoute.Number, p.Route);
        }

        [Fact]
        public void NextStepCostIsTheBloodToTheNextWholePercent()
        {
            double at27 = BloodRouter.LootBloodFor(27, MinLoot);
            BudgetPlan p = BloodRouter.Plan(Run(Sink(SinkMode.Off), Sink(SinkMode.On, at27, gold: false), onHand: 1e15));
            Assert.Equal(BloodRouter.LootBloodFor(28, MinLoot) - at27, p.Loot.NextStepCost, 0);
        }

        [Fact]
        public void NextStepNeverAsksForLessThanTheGameMinimum()
        {
            BudgetPlan p = BloodRouter.Plan(Run(Sink(SinkMode.On), Sink(SinkMode.Off, gold: false), onHand: 1e12));
            Assert.True(p.Gold.NextStepCost >= MinGold);
        }

        // No rebirth to cash NUMBER: it takes no share, and with nothing else on blood stays idle.
        [Fact]
        public void WithoutARebirthNumberTakesNoShare()
        {
            BudgetPlan p = BloodRouter.Plan(Run(Sink(SinkMode.On), Sink(SinkMode.On, gold: false), onHand: 6e12, eligible: false));
            Assert.Equal(2, p.Spells);
            Assert.Equal(3e12, p.Share, 0);
            BudgetPlan idle = BloodRouter.Plan(Run(Sink(SinkMode.Off), Sink(SinkMode.Off, gold: false), onHand: 6e12, eligible: false));
            Assert.Equal(BloodRoute.Idle, idle.Route);
        }

        [Fact]
        public void TargetPercentIsWhatTheShareBuys()
        {
            BudgetPlan p = BloodRouter.Plan(Run(Sink(SinkMode.On), Sink(SinkMode.On, gold: false), onHand: 3 * 17.1e12));
            Assert.Equal(BloodRouter.GoldPct(17.1e12, MinGold), p.Gold.TargetPct);
            Assert.Equal(BloodRouter.LootPct(17.1e12, MinLoot), p.Loot.TargetPct);
        }
    }
}
