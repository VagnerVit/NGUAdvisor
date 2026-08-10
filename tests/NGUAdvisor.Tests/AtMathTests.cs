using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class AtMathTests
    {
        [Fact]
        public void LevelAtZeroTimeIsTheStartingLevel()
        {
            Assert.Equal(1000, AtMath.LevelAt(1000, 500, 0), 6);
        }

        [Fact]
        public void LevelIsMonotoneInTime()
        {
            double a = AtMath.LevelAt(100, 250, 10);
            double b = AtMath.LevelAt(100, 250, 20);
            Assert.True(b > a);
        }

        [Fact]
        public void AZeroRateNeverAdvancesTheLevel()
        {
            Assert.Equal(750, AtMath.LevelAt(750, 0, 3600), 6);
        }

        [Fact]
        public void SecondsToLevelInvertsLevelAt()
        {
            const double l0 = 4_758_488, r = 12_345.678;
            double t = 9_000;
            double reached = AtMath.LevelAt(l0, r, t);
            double? back = AtMath.SecondsToLevel(l0, reached, r);
            Assert.NotNull(back);
            Assert.Equal(t, back.Value, 3);
        }

        [Fact]
        public void SecondsToLevelRefusesToAnswerRatherThanInventANumber()
        {
            Assert.Null(AtMath.SecondsToLevel(100, 200, 0));       // no rate
            Assert.Null(AtMath.SecondsToLevel(100, 200, -5));      // negative rate
            Assert.Null(AtMath.SecondsToLevel(100, 100, 10));      // already there
            Assert.Null(AtMath.SecondsToLevel(100, 50, 10));       // target behind us
            Assert.Null(AtMath.SecondsToLevel(100, 200, double.NaN));
            Assert.Null(AtMath.SecondsToLevel(100, 200, double.PositiveInfinity));
        }

        [Fact]
        public void StatMultiplierIsOneAtLevelZeroAndGrowsAsTheFourTenthsPower()
        {
            Assert.Equal(1.0, AtMath.StatMultiplier(0), 9);
            Assert.Equal(1.0, AtMath.StatMultiplier(-5), 9);          // guard, never below 1
            Assert.Equal(1 + 0.1 * System.Math.Pow(10000, 0.4), AtMath.StatMultiplier(10000), 9);
        }

        // ---- LevelForMultiplier: the goal threshold ("past this level AT buys no progress") ----

        // It must be the exact inverse of the multiplier the game applies, so the round trip is the test:
        // ask which level yields StatMultiplier(L) and get L back.
        [Fact]
        public void LevelForMultiplierInvertsStatMultiplier()
        {
            double[] levels = { 1, 100, 10_000, 1_000_000, 4_758_488 };
            foreach (double l in levels)
            {
                double? back = AtMath.LevelForMultiplier(AtMath.StatMultiplier(l));
                Assert.NotNull(back);
                Assert.Equal(l, back.Value, 6);
            }
        }

        // And forwards, through the shipped code both ways: the multiplier of the level it hands back is
        // the multiplier that was asked for. 1.5x more stat is the shape of a real goal need.
        [Fact]
        public void TheLevelItNamesReallyYieldsTheAskedForMultiplier()
        {
            double? level = AtMath.LevelForMultiplier(1.5);
            Assert.NotNull(level);
            Assert.Equal(1.5, AtMath.StatMultiplier(level.Value), 9);

            // 1 + 0.1*L^0.4 = 1.5 => L = 5^2.5, worked by hand off the closed form.
            Assert.Equal(System.Math.Pow(5, 2.5), level.Value, 6);
        }

        [Fact]
        public void LevelForMultiplierRefusesToAnswerRatherThanInventALevel()
        {
            Assert.Null(AtMath.LevelForMultiplier(1.0));       // nothing to reach: already met
            Assert.Null(AtMath.LevelForMultiplier(0.75));      // requirement below current stats
            Assert.Null(AtMath.LevelForMultiplier(0));
            Assert.Null(AtMath.LevelForMultiplier(-2));
            Assert.Null(AtMath.LevelForMultiplier(double.NaN));
            Assert.Null(AtMath.LevelForMultiplier(double.PositiveInfinity));
            Assert.Null(AtMath.LevelForMultiplier(double.NegativeInfinity));
        }

        // The check that validates the whole derivation of the sheet's missing atcalc/bb.
        // The sheet's modifier is (ecap/1000)*sqrt(epow)*(1+gear); the game divides energy by 50, not
        // 1000, so M = 20 * modifier. With the P/T slots' baseTime of 1e7 this must land on the sheet's
        // own "Highest BB level (full ecap)" cell — one lower, because the sheet solves for L+1 and
        // prints it as L (see AtMath.BbCeiling; the decomp is the authority).
        [Fact]
        public void BbCeilingReproducesTheSheetsHighestBbLevel()
        {
            const double sheetModifier = 1.85202591774521e14;
            double ceiling = AtMath.BbCeiling(20 * sheetModifier, 1e7);

            // Exact value implied by that modifier: 20*1.85202591774521e14 / 1e7 - 1.
            Assert.Equal(370405182.549042, ceiling, 3);

            // And it is within one level of the sheet's displayed 370405183 — which is the claim the
            // derivation actually makes, and what would break if either the x20 or the -1 were wrong.
            Assert.True(System.Math.Abs(ceiling - 370405183) <= 1.0,
                $"expected within 1 of the sheet's 370405183, got {ceiling}");
        }

        // End-to-end against the sheet's displayed answer, THROUGH THE SHIPPED CODE PATH: both current
        // and target sit below the BB ceiling, so SecondsToTarget must take its first branch and charge
        // one 0.02 s tick per level. Computing 0.02*(target-current) in the test body instead would only
        // validate arithmetic written in the test — which is how the missing tick cap shipped once.
        [Fact]
        public void TheSheetsWorkedPowerCaseIsOneTickPerLevelBelowTheCeiling()
        {
            const double current = 4_758_488, target = 4_825_398;
            // The ppt that puts this slot on the sheet's own ceiling: M/baseTime / (level+1).
            double ppt = 20 * 1.85202591774521e14 / 1e7 / (current + 1);
            Assert.True(target < AtMath.BbCeiling(ppt * (current + 1), 1.0));

            double? seconds = AtMath.SecondsToTarget(current, target, ppt, 0.02);
            Assert.NotNull(seconds);
            Assert.Equal(1338.2, seconds.Value, 1);
        }

        // The third branch: the target sits ABOVE the ceiling, so the answer is the tick-capped stretch
        // up to the ceiling PLUS the closed form beyond it. ppt = 2 at level 1000 puts the ceiling at
        // 2*1001 - 1 = 2001 and r at 2*1001/0.02 = 100100.
        [Fact]
        public void ATargetPastTheCeilingPaysTicksToItAndTheClosedFormBeyond()
        {
            double? seconds = AtMath.SecondsToTarget(1000, 5000, 2, 0.02);
            Assert.NotNull(seconds);
            // 0.02*(2001-1000) + ((5001^2 - 2002^2) / (2*100100)) = 20.02 + 104.90508
            Assert.Equal(124.925, seconds.Value, 3);

            // And it is SLOWER than either single-regime shortcut, which is the defect this branch fixes:
            // the uncapped closed form starts at r/(L+1) = 100 levels/s, twice what one level per tick
            // allows, so dropping the cap promises a time the game cannot deliver.
            Assert.True(seconds.Value > AtMath.SecondsToLevel(1000, 5000, 100100).Value);
            Assert.True(seconds.Value > 0.02 * (5000 - 1000));
        }

        [Fact]
        public void SecondsToTargetRefusesToAnswerRatherThanInventANumber()
        {
            Assert.Null(AtMath.SecondsToTarget(100, 200, 0, 0.02));      // no rate
            Assert.Null(AtMath.SecondsToTarget(100, 200, -1, 0.02));     // negative rate
            Assert.Null(AtMath.SecondsToTarget(100, 100, 5, 0.02));      // already there
            Assert.Null(AtMath.SecondsToTarget(100, 50, 5, 0.02));       // target behind us
            Assert.Null(AtMath.SecondsToTarget(100, 200, 5, 0));         // no tick length
            Assert.Null(AtMath.SecondsToTarget(100, 200, double.NaN, 0.02));
            Assert.Null(AtMath.SecondsToTarget(100, 200, double.PositiveInfinity, 0.02));
        }

        // ---- LevelAtCapped: the forward projection AtHourPlanner extends the AT segment on ----

        // THE safety property of the AtHourPlanner fix: at ppt <= 1 the ceiling sits at or below the
        // current level, so the capped projection is the SAME arithmetic the planner used before. This
        // is the whole argument that switching the planner over cannot move a non-blitz slot's forecast.
        [Fact]
        public void LevelAtCappedEqualsTheUncappedClosedFormWhenNotBlitzBoosting()
        {
            double[] ppts = { 1.0, 0.75, 0.5, 0.1, 1e-6 };
            double[] levels = { 0, 1, 1000, 4_758_488 };
            double[] times = { 1, 60, 7200, 14400 };
            foreach (double ppt in ppts)
                foreach (double l0 in levels)
                    foreach (double t in times)
                    {
                        double r = ppt * (l0 + 1) / 0.02;
                        double uncapped = System.Math.Sqrt((l0 + 1) * (l0 + 1) + 2 * r * t) - 1;
                        Assert.Equal(uncapped, AtMath.LevelAtCapped(l0, ppt, t, 0.02), 9);
                    }
        }

        // And the other half: above one progress per tick the cap binds, so the projection must come out
        // strictly LOWER than the uncapped form the planner used to trust.
        [Fact]
        public void LevelAtCappedIsStrictlyLowerThanTheUncappedFormWhenBlitzBoosting()
        {
            double[] ppts = { 1.5, 2, 10, 78 };
            double[] times = { 7200, 10800, 14400 };
            foreach (double ppt in ppts)
                foreach (double t in times)
                {
                    const double l0 = 4_758_488;
                    double r = ppt * (l0 + 1) / 0.02;
                    double uncapped = System.Math.Sqrt((l0 + 1) * (l0 + 1) + 2 * r * t) - 1;
                    double capped = AtMath.LevelAtCapped(l0, ppt, t, 0.02);
                    Assert.True(capped < uncapped, $"ppt={ppt} t={t}: {capped} should be < {uncapped}");
                    Assert.True(capped > l0);
                }
        }

        [Fact]
        public void LevelAtCappedAtZeroTimeIsTheStartingLevel()
        {
            Assert.Equal(1000, AtMath.LevelAtCapped(1000, 2, 0, 0.02), 9);
            Assert.Equal(1000, AtMath.LevelAtCapped(1000, 0, 3600, 0.02), 9);      // no rate
            Assert.Equal(1000, AtMath.LevelAtCapped(1000, 2, 3600, 0), 9);         // no tick length
        }

        // Wholly inside the capped region: one level per 0.02 s, nothing else. ppt = 2 at level 1000 puts
        // the ceiling at 2*1001 - 1 = 2001, i.e. 1001 levels = 20.02 s of tick-capped growth.
        [Fact]
        public void LevelAtCappedInsideTheCappedRegionIsOneLevelPerTick()
        {
            Assert.Equal(1500, AtMath.LevelAtCapped(1000, 2, 0.02 * 500, 0.02), 9);
            Assert.Equal(2001, AtMath.LevelAtCapped(1000, 2, 20.02, 0.02), 9);     // exactly at the ceiling
        }

        // The straddle: the capped phase ends inside the window, so the answer is the ceiling carried
        // forward by the closed form for the remainder. Mirrors SecondsToTarget's third branch, and the
        // two must agree — the time this projection needs to reach a level is what SecondsToTarget quotes.
        [Fact]
        public void LevelAtCappedStraddlesIntoTheClosedFormAndAgreesWithSecondsToTarget()
        {
            // The sheet-anchored 124.925 s is itself rounded, so it lands a fraction of a level short.
            Assert.Equal(5000, AtMath.LevelAtCapped(1000, 2, 124.925, 0.02), 2);

            // Exact both ways: the projection and the ETA are inverses across the straddle.
            double? t = AtMath.SecondsToTarget(1000, 5000, 2, 0.02);
            Assert.NotNull(t);
            Assert.Equal(124.925, t.Value, 3);
            Assert.Equal(5000, AtMath.LevelAtCapped(1000, 2, t.Value, 0.02), 6);

            // And the capped phase really is a prefix here: 1001 levels of it, then the closed form.
            Assert.True(AtMath.LevelAtCapped(1000, 2, 20.02, 0.02) < AtMath.LevelAtCapped(1000, 2, 124.925, 0.02));
        }

        // The second branch, in isolation: below one progress per tick there is no ceiling above the
        // current level, so the whole answer is the closed form and nothing is charged per tick.
        [Fact]
        public void ASlotBelowOneProgressPerTickIsPureClosedForm()
        {
            double? seconds = AtMath.SecondsToTarget(1000, 5000, 0.5, 0.02);
            Assert.NotNull(seconds);
            Assert.Equal(AtMath.SecondsToLevel(1000, 5000, 0.5 * 1001 / 0.02).Value, seconds.Value, 6);
        }
    }
}
