using System;

namespace NGUAdvisor.Managers
{
    // Unity-free hack arithmetic, so the numbers that decide R3 allocation can be tested headlessly the way
    // DiggerMath and WandoosMath are. Everything here is a port of reference/decomp-full/HacksController.cs.
    //
    // THE ONE FACT THIS FILE EXISTS FOR. updateAllHacks (L227-271) does:
    //
    //     progress += progressPerTick(i);
    //     if (progress >= 1f) { progress = 0f; if (level < hardCap) level++; }
    //
    // progress is RESET, not decremented, so the overflow is thrown away. A hack therefore gains at most one
    // level per tick (50/sec), and every unit of R3 that pushes progressPerTick above 1.0 buys nothing. The
    // game agrees with this reading in its own offline catch-up, which computes ticks-per-level as
    // Mathf.CeilToInt(1f / ppt) (Character.cs:4105) rather than dividing.
    //
    // That makes the level rate a STAIRCASE, not a ramp: 1/ceil(1/ppt) levels per tick. Efficiency against
    // the ideal is 1/(ppt*ceil(1/ppt)), which is 1 only when ppt is exactly 1/n. Just under a step is the
    // worst place to be — ppt 0.9 runs at 55.6%, i.e. it wastes 44% of that hack's allocation for nothing.
    // AdvancedTrainingBP/NGUBP already solve this for their own systems; see SnapToStair.
    //
    // Nothing here reads baseDivider. It is Unity-inspector data with no value anywhere in this repository,
    // so the saturation point is recovered from the game's own progressPerTickCap instead (see Saturation).
    public static class HackMath
    {
        // progress is a float. Across [0.5,1) its ULP is 2^-24, so round-to-nearest swallows any increment
        // below half of that and the bar sticks at 0.5 forever. WishManager guards the same constant for
        // wishes, but with the RELATIVE test ppt/progress ("is it moving now"); the question here is "will it
        // ever finish", which is absolute, because progress must cross that binade to reach 1.
        public const double StallFloor = 1.0 / 33554432.0;   // 2^-25 == 2.98023223876953e-8

        // ── MARGINAL VALUE DENSITY: which hack should the R3 pool be pointed at RIGHT NOW ────────
        //
        // hackBonus(id) = (1 + L*b) * m^floor(L/T)   (HacksController.cs:415-428), b/m/T being
        // baseEffectPerLevel / milestoneEffect / milestoneThreshold.
        //
        // THE MILESTONE FACTOR CANCELS, which is the whole reason this is one cheap line. The
        // RELATIVE gain from one more level is
        //
        //     d(bonus)/bonus = [ b * m^k ] / [ (1 + L*b) * m^k ] = b / (1 + L*b)
        //
        // — independent of how many milestones the hack has banked. So a hack's value rate does not
        // need the milestone term at all, except as the discrete step it adds when a level crossing
        // actually lands (see MilestoneStep).
        //
        // COST. progressPerTick = res3 * res3Power * hackSpeed / (baseDivider * levelDivider), and
        // Saturation(cap, ppt) = cap * baseDivider * levelDivider / (cap * res3Power * hackSpeed),
        // so SATURATION IS THE COST TERM ITSELF, already carrying baseDivider, the 1.0078^L ladder
        // and the (L+1) factor, and already scaled by the player's own res3 power and hack speed.
        // Ticks to the next level with the whole pool in one lane = saturation / pool, which is why
        // this needs no constant table: the game computes the price for us.
        //
        // Verified against the live board 2026-08-18: Adventure at L153 reported sat=19.2e9 with a
        // 100K pool, predicting 192,000 ticks/level against the game's own reported 191,511.
        //
        // ⚠ WHAT THIS DOES NOT KNOW. The result is a percentage of THAT HACK'S OWN bonus, and the
        // fifteen bonuses multiply different things — hack 11 lands on nextAttackMulti
        // (Rebirth.cs:782-783), hack 1 on the adventure stat stack (Character.cs:1513), hack 7 on
        // blood magic (BloodMagicController.cs:43). Ranking by this treats one percent as one
        // percent everywhere. That is the right default when the alternative is a hand-guessed
        // weight table, and the ordering it produces is dominated by cost, which IS commensurable:
        // on the live board the top lane beat the incumbent by 73x on pure rate (49.8x once the
        // amortised milestone step is folded in), far outside any weighting argument. Do not read a
        // 5% gap between two lanes as meaningful.
        // THE SHARED LAW. Both hacks and wishes grow a bonus LINEARLY in level off a per-level
        // coefficient, so the relative worth of one more level is the same expression for both:
        //
        //     hackBonus(id) = (1 + L*b) * m^floor(L/T)   -> d/bonus = b / (1 + L*b)   (m^k cancels)
        //     wishEffect(id) = 1 + L*e                   -> d/bonus = e / (1 + L*e)
        //
        // ([DECOMP] HacksController.cs:415-428, WishesController.cs:1108-1120. Wishes have no
        // milestone term at all, so theirs is the same formula with the cancelling factor absent.)
        //
        // Divide by a COST to rank hacks (saturation is the cost term); multiply by a RATE to rank
        // wishes (progressPerTick is levels/tick). Same law, two denominators.
        public static double RelativeGainPerLevel(double effectPerLevel, long level)
        {
            if (effectPerLevel <= 0 || level < 0) return 0.0;
            double denom = 1.0 + level * effectPerLevel;
            if (denom <= 0 || double.IsNaN(denom) || double.IsInfinity(denom)) return 0.0;
            double g = effectPerLevel / denom;
            return double.IsNaN(g) || double.IsInfinity(g) || g < 0 ? 0.0 : g;
        }

        // THE REDUCER FORM. Wish 46 (respawn time) is the one WISH whose multiplier subtracts:
        // respawn1() = 1 - L*e ([DECOMP] WishesController.cs:1373). Feeding it to
        // RelativeGainPerLevel uses the wrong denominator - that formula assumes 1 + L*e.
        //
        // ⚠ SCOPE, STATED NARROWLY ON PURPOSE. This is not "the only subtracting bonus in the
        // game". Wish 20 subtracts too (Rebirth.cs:299) but is integer SECONDS, not a multiplier,
        // so this expression does not describe it; and hacks 76/77/78 (milestoneThreshold reducers,
        // HacksController.cs:509-524) and BeastQuest 80/81 subtract in their own units. None of
        // those are ranked by WishValueRate. Only wish 46 is both a wish AND a 1 - L*e multiplier.
        //
        // ⚠ THE 0.9 FLOOR IS DELIBERATELY NOT MODELLED. The game clamps respawn1 at 0.9, but
        // audit/16 §F2 ruled that clamp unreachable: effectPerLevel[46] ~ 0.01 against
        // maxLevel[46] = 10, and GetValidWishes filters level < maxLevel, so L <= 9 and the
        // multiplier never falls below ~0.91. A guard here would be dead code pretending to be a
        // safeguard - the same call the audit made about minimumWishTime().
        public static double ReducerGainPerLevel(double effectPerLevel, long level)
        {
            if (effectPerLevel <= 0 || level < 0) return 0.0;
            double m = 1.0 - level * effectPerLevel;
            if (m <= 0) return 0.0;                        // degenerate input, not the game's clamp
            double g = effectPerLevel / m;
            return double.IsNaN(g) || double.IsInfinity(g) || g < 0 ? 0.0 : g;
        }

        public static double ReducerValueRate(double effectPerLevel, long level, double progressPerTick)
        {
            if (progressPerTick <= 0 || double.IsNaN(progressPerTick) || double.IsInfinity(progressPerTick))
                return 0.0;
            double r = ReducerGainPerLevel(effectPerLevel, level) * progressPerTick;
            return double.IsNaN(r) || double.IsInfinity(r) || r < 0 ? 0.0 : r;
        }

        // Wish form: value per TICK. progressPerTick is already levels-per-tick, so this multiplies
        // where MarginalDensity divides.
        public static double WishValueRate(double effectPerLevel, long level, double progressPerTick)
        {
            if (progressPerTick <= 0 || double.IsNaN(progressPerTick) || double.IsInfinity(progressPerTick))
                return 0.0;
            double r = RelativeGainPerLevel(effectPerLevel, level) * progressPerTick;
            return double.IsNaN(r) || double.IsInfinity(r) || r < 0 ? 0.0 : r;
        }

        public static double MarginalDensity(double baseEffectPerLevel, long level, long saturation)
        {
            if (saturation <= 0) return 0.0;
            double d = RelativeGainPerLevel(baseEffectPerLevel, level) / saturation;
            return double.IsNaN(d) || double.IsInfinity(d) || d < 0 ? 0.0 : d;
        }

        // The one-off fractional bump a milestone crossing adds, amortised over the levels still
        // needed to reach it — the term MarginalDensity legitimately drops. Kept separate because it
        // is a STEP, not a rate: folding it into the density above would smear a discrete event
        // across levels that do not receive it. Callers that want the milestone to influence the
        // ranking add this; callers that want pure rate do not.
        public static double MilestoneStep(double milestoneEffect, long levelsToNextMilestone, long saturation)
        {
            if (saturation <= 0 || levelsToNextMilestone <= 0 || milestoneEffect <= 1.0) return 0.0;
            double step = (milestoneEffect - 1.0) / levelsToNextMilestone;
            double d = step / saturation;
            return double.IsNaN(d) || double.IsInfinity(d) || d < 0 ? 0.0 : d;
        }

        // levelDivider(id) = 1.0078^level * (level+1)  (HacksController.cs:150-158).
        // Returns float.MaxValue past the float range, as the game does.
        public static double LevelDivider(long level)
        {
            if (level < 0) return 1.0;
            double d = Math.Pow(1.0078, level) * (level + 1.0);
            return d > float.MaxValue ? float.MaxValue : d;
        }

        // How much levelDivider grows over `offset` more levels — the ratio that turns "enough R3 to saturate
        // right now" into "enough to still be saturated `offset` levels from now".
        //
        // This is why a clamp cannot simply target the current level: 1.0078^500 is ~49x, and 500 levels is
        // exactly what one 10s allocation window buys at full rate. Clamp to the level you are on and the
        // hack drops off the cap after a single level and idles until the next pass.
        public static double GrowthOverLevels(long level, int offset)
        {
            if (offset <= 0 || level < 0) return 1.0;
            double g = Math.Pow(1.0078, offset) * ((double)level + offset + 1.0) / (level + 1.0);
            return g > float.MaxValue ? float.MaxValue : g;
        }

        // The allocation at which progressPerTick hits exactly 1.0 — one level per tick, the ceiling.
        //
        // progressPerTick and progressPerTickCap share every term except the R3 amount, so their ratio is
        // pure arithmetic and baseDivider, res3Power and the whole hack-speed stack all cancel:
        //     ppt(r) = r * K,  pptCap = cap * K   =>   r_saturating = cap / pptCap
        // Reading pptCap off the live controller is therefore both simpler and more accurate than
        // reconstructing the multiplier stack the way NGUBP has to.
        //
        // pptCap <= 0 means "not computable" (unreadable, or so slow it underflowed); returns 0 so callers
        // fall back to their existing behaviour rather than clamping to a garbage number.
        public static long Saturation(long capRes3, double pptCap)
        {
            if (capRes3 <= 0 || pptCap <= 0 || double.IsNaN(pptCap) || double.IsInfinity(pptCap)) return 0;
            double r = capRes3 / pptCap;
            if (r >= long.MaxValue) return long.MaxValue;
            return r < 1.0 ? 1L : (long)Math.Ceiling(r);
        }

        // Ticks to finish one level at this rate: ceil(1/ppt), matching Character.cs:4105.
        // 0 means "never" — either no rate at all, or below the float stall floor.
        public static long TicksPerLevel(double ppt)
        {
            if (ppt <= 0 || double.IsNaN(ppt) || ppt < StallFloor) return 0;
            if (ppt >= 1.0) return 1;
            double t = Math.Ceiling(1.0 / ppt);
            return t >= long.MaxValue ? 0 : (long)t;
        }

        // Fraction of this allocation that actually turns into levels: 1/(ppt*ceil(1/ppt)).
        // 1.0 exactly on a stair (ppt = 1/n); worst just below one (0.9 -> 0.556).
        public static double Efficiency(double ppt)
        {
            long n = TicksPerLevel(ppt);
            if (n <= 0) return 0;
            double e = 1.0 / (ppt * n);
            return e > 1.0 ? 1.0 : e;
        }

        // Below this a hack accumulates nothing at all, forever. Distinct from "slow": R3 movement never
        // clears progress (removeAllR3 touches only .res3), so a slow hack keeps what it banked.
        public static bool WillStall(double ppt) => !(ppt >= StallFloor);

        // "Get the first milestone, move on" — the guide ch.5 sweep's stopping rule (audit/10 §A1.1
        // row 3, amendment 09 §1), as a predicate MileHackBP can report TargetMet() with.
        //
        // `threshold` is the game's own hacksController.milestoneThreshold(id): the serialized
        // per-hack spacing minus the perk/quirk reducers, i.e. the level of the FIRST milestone.
        // Reducers only ever LOWER it, so a level at-or-past the threshold stays past it — terminal
        // stays terminal, no flap. No zero guard on purpose: the reducer caps keep the threshold >= 8
        // (audit/08 §0 capture; the divide-by-zero ruling in amendment 07), and if it ever were 0 the
        // `>=` reads "done", which fails safe — the lane drops out rather than absorbing forever.
        public static bool FirstMilestoneMet(long level, long threshold) => level >= threshold;

        // Spend as little as possible for the rate you were going to get anyway.
        //
        // With n = ceil(need/budget) chunks, need/n lands ppt on exactly 1/n — a stair peak — so the levels
        // per tick are identical to spending the whole budget while the remainder goes to the next priority.
        // At budget = need/3.5 that is a 12.5% refund for no loss at all.
        //
        // The 1.00000202655792 margin is the one AdvancedTrainingBP:143, NGUBP:122,160, TimeMachineBP:83,111,
        // AugmentBP:137 and RitualBP:48 already ship. It matters because progressPerTick casts the allocation
        // to float: land one ULP low and ceil(1/ppt) rounds up a whole stair, which at n=1 halves the rate —
        // the exact loss this is meant to prevent. Note it therefore returns slightly MORE than need/n; that
        // overshoot is the point, and it is bounded by the margin.
        //
        // The margin is ~2e-6 relative, so it covers the float error exactly at small n — the only place the
        // error is expensive (a lost stair costs 50% at n=1, 33% at n=2). Past a few hundred ticks accumulated
        // error exceeds it and a level can take one extra tick: 0.1% at n=1000, and identical to what the
        // shipped breakpoints above already accept with the same constant.
        public static long SnapToStair(double need, long budget, out long ticksPerLevel)
        {
            ticksPerLevel = 0;
            if (need <= 0 || budget <= 0) return 0;
            if (double.IsNaN(need) || double.IsInfinity(need)) return budget;

            double n = Math.Ceiling(need / budget);
            if (n < 1.0) n = 1.0;
            ticksPerLevel = n >= long.MaxValue ? 0 : (long)n;

            double alloc = Math.Ceiling(need / n * 1.00000202655792);
            if (alloc >= budget) return budget;
            return alloc < 1.0 ? 1L : (long)alloc;
        }
    }
}
