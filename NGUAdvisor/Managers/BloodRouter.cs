using System;

namespace NGUAdvisor.Managers
{
    public enum BloodRoute { Idle, NumberFloor, Gold, Loot, NumberDefault }

    // What the user asked of one investment sink. The number beside it is a CEILING in %.
    public enum SinkMode
    {
        Off = 0,      // never routed
        Auto = 1,     // the advisor's own gates decide (window, demand, cost-curve knee)
        Push = 2      // the user wants this bonus: the discretionary gates are bypassed up to the ceiling
    }

    // Why a sink is (not) taking blood right now. The panel renders this verbatim, so a sink that loses
    // the routing can say WHICH gate rejected it — the shipped panel showed only the winner, and a user
    // who had set Counterfeit "up to 500 %" had no way to see that the cost-curve knee, not their
    // ceiling, was what kept dropping it back to NUMBER.
    public enum SinkVerdict
    {
        Eligible,
        Off,
        TargetReached,
        NotFeasible,     // hard gate: not unlocked, or nothing for the bonus to multiply
        WindowClosed,    // too late in the run for an in-run bonus to earn its blood back
        NoDemand,        // nothing downstream wants what this sink buys
        PastKnee         // the next step of the cost curve is too far out
    }

    // Unity-free blood routing. The game's autoSpell() splits blood EVENLY among the enabled toggles
    // every second, so enabling several DILUTES them: exactly one sink is chosen.
    //
    // The three sinks are not commensurable (decomp):
    //   NUMBER — rebirthPower += blood: LINEAR, uncapped, a straight multiplier on the whole next-run
    //            multi. The "Number >=" knob is a FLOOR (below it NUMBER outranks everything); it is
    //            also the DEFAULT sink when nothing else fits.
    //   Gold / Loot — in-run LOG sinks, wiped at rebirth, so they must earn their blood back this run.
    //
    // Predicates are lazy on purpose: the floor is decided WITHOUT touching the window, the window is
    // read at most once (the caller memoizes it), and a sink's expensive reads only happen if a
    // cheaper gate hasn't already rejected it.
    public static class BloodRouter
    {
        // A sink's own gates, already evaluated by the caller. `feasible` is the HARD gate (unlocked,
        // something to multiply); `demand` and `belowKnee` are the advisor's discretionary ones, which
        // Push mode is allowed to overrule.
        public static SinkVerdict JudgeSink(SinkMode mode, int target, int now, bool windowOpen,
                                            Func<bool> feasible, Func<bool> demand, Func<bool> belowKnee)
        {
            if (mode == SinkMode.Off) return SinkVerdict.Off;
            if (target > 0 && now >= target) return SinkVerdict.TargetReached;
            if (!feasible()) return SinkVerdict.NotFeasible;
            // The window survives Push: gold and loot investments are wiped by bloodMagicController
            // .reset() at rebirth, so blood poured in past the halfway mark is simply lost. Push
            // overrules the advisor's OPINION about value, not the arithmetic of the wipe.
            if (!windowOpen) return SinkVerdict.WindowClosed;
            if (mode == SinkMode.Push) return SinkVerdict.Eligible;
            if (!demand()) return SinkVerdict.NoDemand;
            if (!belowKnee()) return SinkVerdict.PastKnee;
            return SinkVerdict.Eligible;
        }

        // Priority ladder. A Push sink outranks an Auto one: Push is the user stating the goal, Auto is
        // the advisor picking for them. Gold outranks loot within each tier (gold funds augs and digger
        // upgrades, which gate progression; loot only speeds drops).
        public static BloodRoute DecideRoute(bool numberEligible, double numberFloor, double rebirthPower,
                                             SinkMode goldMode, SinkVerdict gold,
                                             SinkMode lootMode, SinkVerdict loot)
        {
            if (numberEligible && numberFloor > 0 && rebirthPower < numberFloor)
                return BloodRoute.NumberFloor;

            if (gold == SinkVerdict.Eligible && goldMode == SinkMode.Push) return BloodRoute.Gold;
            if (loot == SinkVerdict.Eligible && lootMode == SinkMode.Push) return BloodRoute.Loot;
            if (gold == SinkVerdict.Eligible) return BloodRoute.Gold;
            if (loot == SinkVerdict.Eligible) return BloodRoute.Loot;

            return numberEligible ? BloodRoute.NumberDefault : BloodRoute.Idle;
        }

        // MINIMUM DWELL (user-reported "it keeps farming NUMBER"): the cost-curve knee is self-
        // retriggering — routing gold buys the next +1 %, the step after costs ~2x, its ETA jumps past
        // the knee, gold drops out, blood income then grows until the same step fits again. The shipped
        // build flip-flopped Counterfeit <-> NUMBER about once a minute, so Counterfeit only ever got a
        // ~20 % duty cycle. A sink that wins therefore HOLDS the pool for a while, unless it stops
        // being eligible for a reason that will not fix itself (off / target reached / not feasible).
        public static bool HoldPrevious(BloodRoute held, SinkVerdict gold, SinkVerdict loot, double dwellSeconds, double minDwellSeconds)
        {
            if (dwellSeconds >= minDwellSeconds) return false;
            if (held == BloodRoute.Gold) return Transient(gold);
            if (held == BloodRoute.Loot) return Transient(loot);
            return held == BloodRoute.NumberDefault || held == BloodRoute.NumberFloor;
        }

        private static bool Transient(SinkVerdict v) =>
            v == SinkVerdict.Eligible || v == SinkVerdict.NoDemand || v == SinkVerdict.PastKnee;

        public static string Describe(SinkVerdict v)
        {
            switch (v)
            {
                case SinkVerdict.Eligible: return "eligible";
                case SinkVerdict.Off: return "off";
                case SinkVerdict.TargetReached: return "target reached";
                case SinkVerdict.NotFeasible: return "nothing to multiply yet";
                case SinkVerdict.WindowClosed: return "too late this run (wiped at rebirth)";
                case SinkVerdict.NoDemand: return "no demand for it right now";
                case SinkVerdict.PastKnee: return "next step too expensive";
                default: return "";
            }
        }
    }
}
