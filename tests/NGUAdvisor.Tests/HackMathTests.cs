using System;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    // Headless guard for the R3 allocator's arithmetic, in the shape WandoosMathTests uses: pin the game's
    // formulas verbatim, and pin WHERE THE CLAMP BINDS, because that is the load-bearing part.
    //
    // A hack gains at most one level per tick — updateAllHacks sets progress = 0f on overflow rather than
    // decrementing — so the level rate is the staircase 1/ceil(1/ppt) and any R3 past ppt = 1 is discarded.
    // The whole point of HackMath is to find the edge of that step and stop there; these tests exist so a
    // refactor cannot quietly re-derive it, and so the float rounding at the step edge (which can halve a
    // hack's rate for a one-ULP error) is caught here rather than in-game.
    //
    // Cross-checked against reference/blaze-junkyard/Hackalc.tsv, a community sheet written independently of
    // the decompile — agreement between the two is real evidence, not self-agreement.
    public class HackMathTests
    {
        // ---- levelDivider ----

        // Hackalc's "Time Scaling Calculator" row: level 1000 -> 2,367,849.088. The sheet uses 1.0078^L * L
        // where the game uses 1.0078^L * (L+1), so ours is one factor of 1.0078^0 * (1001/1000) higher.
        [Fact]
        public void LevelDivider_matches_the_community_sheet_at_level_1000()
        {
            double sheet = Math.Pow(1.0078, 1000) * 1000;    // Hackalc's form
            Assert.Equal(2367849.088, sheet, 2);
            Assert.Equal(sheet * 1001.0 / 1000.0, HackMath.LevelDivider(1000), 2);
        }

        [Theory]
        [InlineData(0, 1.0)]
        [InlineData(10, 11.888827)]
        [InlineData(100, 219.663)]
        public void LevelDivider_matches_the_game_formula(long level, double expected)
            => Assert.Equal(expected, HackMath.LevelDivider(level), 3);

        [Fact]
        public void LevelDivider_saturates_rather_than_going_infinite()
            => Assert.Equal(float.MaxValue, HackMath.LevelDivider(100000));

        // ---- the growth term ----

        // 500 levels is what one 10s allocation window buys at the ceiling, and it is the offset every other
        // breakpoint funds against. If this ratio were dropped, a clamp would target the CURRENT level and the
        // hack would fall off the cap after one level — the failure this term exists to prevent.
        [Fact]
        public void GrowthOverLevels_is_the_49x_that_one_full_window_costs()
        {
            double g = HackMath.GrowthOverLevels(1000, 500);
            Assert.Equal(Math.Pow(1.0078, 500) * 1501.0 / 1001.0, g, 6);
            Assert.True(g > 70 && g < 75, $"expected ~73x at level 1000, got {g}");
        }

        [Fact]
        public void GrowthOverLevels_is_one_for_no_lookahead()
            => Assert.Equal(1.0, HackMath.GrowthOverLevels(1000, 0));

        // ---- saturation ----

        // ppt(r) = r*K and pptCap = cap*K, so cap/pptCap is the R3 at which ppt == 1 and every other term
        // cancels. This is the reason nothing here needs baseDivider.
        [Theory]
        [InlineData(1000L, 1.0, 1000L)]     // the whole cap is exactly enough
        [InlineData(1000L, 10.0, 100L)]     // 10x over the ceiling -> a tenth of the cap suffices
        [InlineData(1000L, 0.5, 2000L)]     // cannot reach the ceiling even at cap
        public void Saturation_inverts_the_cap_rate(long cap, double pptCap, long expected)
            => Assert.Equal(expected, HackMath.Saturation(cap, pptCap));

        [Theory]
        [InlineData(0L, 1.0)]
        [InlineData(1000L, 0.0)]
        [InlineData(1000L, -1.0)]
        [InlineData(1000L, double.NaN)]
        [InlineData(1000L, double.PositiveInfinity)]
        public void Saturation_returns_zero_when_it_cannot_be_computed(long cap, double pptCap)
            => Assert.Equal(0L, HackMath.Saturation(cap, pptCap));

        // ---- the staircase ----

        // The measured table. Efficiency is 1 only at ppt = 1/n; just under a step is the worst place to be,
        // and 0.9 throwing away 44% is the single most useful number in this file.
        [Theory]
        [InlineData(1.0, 1, 1.0)]
        [InlineData(0.9, 2, 0.5555555)]
        [InlineData(0.75, 2, 0.6666666)]
        [InlineData(0.6, 2, 0.8333333)]
        [InlineData(0.5, 2, 1.0)]
        [InlineData(0.34, 3, 0.9803921)]
        [InlineData(0.25, 4, 1.0)]
        [InlineData(0.01, 100, 1.0)]
        public void The_level_rate_is_a_staircase(double ppt, long ticks, double efficiency)
        {
            Assert.Equal(ticks, HackMath.TicksPerLevel(ppt));
            Assert.Equal(efficiency, HackMath.Efficiency(ppt), 6);
        }

        // Over-feeding past the ceiling never buys more than one level per tick.
        [Fact]
        public void Overfeeding_past_the_ceiling_is_pure_waste()
        {
            Assert.Equal(1L, HackMath.TicksPerLevel(5.0));
            Assert.Equal(1.0 / 5.0, HackMath.Efficiency(5.0), 6);   // 80% of that allocation buys nothing
        }

        // ---- the stall floor ----

        [Fact]
        public void The_stall_floor_is_two_to_the_minus_25()
        {
            Assert.Equal(HackMath.StallFloor, Math.Pow(2, -25), 15);
            Assert.True(HackMath.WillStall(Math.Pow(2, -26)));
            Assert.False(HackMath.WillStall(Math.Pow(2, -24)));
            Assert.Equal(0L, HackMath.TicksPerLevel(Math.Pow(2, -26)));
        }

        // The closed form is a claim about float behaviour, so prove it against actual floats: below the
        // floor the bar sticks at 0.5 forever; a hair above it still climbs.
        [Fact]
        public void A_rate_below_the_floor_never_moves_the_bar_off_one_half()
        {
            float below = (float)(HackMath.StallFloor * 0.9);
            float progress = 0.5f;
            for (int i = 0; i < 200000; i++) progress += below;
            Assert.Equal(0.5f, progress);

            float above = (float)(HackMath.StallFloor * 4);
            progress = 0.5f;
            for (int i = 0; i < 200000; i++) progress += above;
            Assert.True(progress > 0.5f, "a rate above the floor must accumulate");
        }

        // ---- the stair snap ----

        [Fact]
        public void Snapping_spends_less_than_the_budget_for_the_same_rate()
        {
            const double need = 1000;
            long budget = 286;                                  // need/3.5 -> 4 ticks either way
            long alloc = HackMath.SnapToStair(need, budget, out var n);

            Assert.Equal(4, n);
            Assert.True(alloc < budget, $"snap should refund; got {alloc} of {budget}");
            Assert.Equal(HackMath.TicksPerLevel(budget / need), HackMath.TicksPerLevel(alloc / need));
        }

        [Fact]
        public void Snapping_is_capped_by_the_budget_and_overshoots_need_only_by_the_margin()
        {
            Assert.Equal(500L, HackMath.SnapToStair(1000, 500, out var n));   // exactly 1/2, no rounding
            Assert.Equal(2, n);

            // Budget covers the ceiling outright, so the answer is `need` PLUS the deliberate margin —
            // landing exactly on need would let the float cast fall under the stair and cost a whole tick.
            long alloc = HackMath.SnapToStair(1000, 4000, out var n2);
            Assert.Equal(1, n2);
            Assert.InRange(alloc, 1000L, 1002L);
        }

        // The margin exists because progressPerTick casts the allocation to float. One ULP low and
        // ceil(1/ppt) rounds up a whole stair. Replay the game's own accumulate loop in float and check the
        // intended stair is reached.
        //
        // Exact for small n, which is the only place it matters: at n=1 losing a stair HALVES the rate, at
        // n=2 it costs a third. The margin is ~2e-6 relative, so past a few hundred ticks the accumulated
        // float error exceeds it and the loop can need one extra tick — 0.1% at n=1000, and identical to
        // what the shipped AT/NGU/TM breakpoints already do with the same constant.
        [Theory]
        [InlineData(1000.0, 1000L, 1)]
        [InlineData(1000.0, 500L, 2)]
        [InlineData(1000.0, 334L, 3)]
        [InlineData(1000.0, 100L, 10)]
        public void The_snapped_allocation_achieves_its_stair_exactly_at_small_n(double need, long budget, long expected)
        {
            long alloc = HackMath.SnapToStair(need, budget, out var n);
            Assert.Equal(expected, n);
            Assert.True(TicksToFill(alloc / need) <= n, $"stair {n} intended, float needed more");
        }

        [Fact]
        public void At_large_n_the_margin_costs_at_most_one_extra_tick()
        {
            long alloc = HackMath.SnapToStair(1e12, 1000000000L, out var n);
            Assert.Equal(1000, n);
            long ticks = TicksToFill(alloc / 1e12);
            Assert.InRange(ticks, 1, n + 1);
        }

        // The game's accumulator, verbatim: float progress, += ppt each tick, level at >= 1f.
        private static long TicksToFill(double pptExact)
        {
            float ppt = (float)pptExact;
            float progress = 0f;
            long ticks = 0;
            while (progress < 1f && ticks < 10_000_000) { progress += ppt; ticks++; }
            return ticks;
        }

        [Theory]
        [InlineData(0.0, 100L)]
        [InlineData(-5.0, 100L)]
        [InlineData(1000.0, 0L)]
        public void Snapping_declines_nonsense_rather_than_guessing(double need, long budget)
            => Assert.Equal(0L, HackMath.SnapToStair(need, budget, out _));
    }
}
