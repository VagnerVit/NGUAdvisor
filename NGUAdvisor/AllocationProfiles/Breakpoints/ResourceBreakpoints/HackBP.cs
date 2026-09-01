using System;
using NGUAdvisor.Managers;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    // The R3 lane's per-hack allocator.
    //
    // This was the only resource breakpoint that just handed over MaxAllocation. Every other one — AT, NGU,
    // Time Machine, Augments, Rituals — computes the amount that saturates one level per game tick and stops
    // there, because past that point the resource buys nothing: updateAllHacks sets progress = 0f on overflow
    // instead of decrementing, so a hack banks at most one level per tick and the excess is discarded. Hacks
    // have the identical mechanic (AdvancedTrainingController does the same thing to barProgress), so this is
    // now the seventh copy of an idiom already in production rather than a new algorithm.
    //
    // Two passes, exactly as AdvancedTrainingBP.CalculateATCap does:
    //   1. Fund 500 levels ahead. levelDivider grows 1.0078^L, and 500 levels is what one 10s allocation
    //      window buys at full rate — funding for the level you are ON would drop the hack off the cap after
    //      a single level and leave it idle until the next pass (1.0078^500 is ~49x).
    //   2. If the budget can't reach that, recompute at the offset it CAN reach in one window
    //      (CapCalc.Offset = floor(PPT * 50 * 10)), which is cheaper and therefore lands a better stair.
    public class HackBP : ResourceBreakpoint
    {
        protected override bool CorrectResourceType() => Type == ResourceType.R3;

        // Index >= 0 is load-bearing, not defensive. A token the parser can't read an index out of —
        // "HACK-", "HACK-x", a stray dash — yields Index = -1 (ResourceBreakpoint.ParseBreakpointArray),
        // and -1 passes a bare `<= 14`. hitTarget(-1) returns false, so the breakpoint reports itself
        // VALID; addR3(-1, …) is then a no-op inside the game but Allocate() still returns true. Since
        // R3Breakpoints reclaims the pool before allocating, the whole of R3 was silently parked idle
        // every pass, forever, with nothing in the log to say so.
        //
        // The hard cap matters for the same reason: past hardCapLevel, updateAllHacks still burns the
        // progress bar but skips the level++, so a maxed hack absorbs R3 and returns nothing at all.
        // Dropping it here lets the rest of the list have that R3 instead.
        //
        // buttons.hacks.interactable IS hacks.hacksOn (ButtonShower sets one from the other), so there is
        // no separate unlock check to add.
        protected override bool Unlocked() =>
            Index >= 0 && Index <= 14
            && _character.buttons.hacks.interactable
            && _character.hacks.hacks[Index].level < _character.hacksController.hardCapLevel(Index);

        protected override bool TargetMet() => _character.hacksController.hitTarget(Index);

        public override bool Allocate()
        {
            _character.hacksController.addR3(Index, CalculateHackCap());
            return true;
        }

        private long CalculateHackCap()
        {
            var calcA = HackCap(500);
            if (calcA.Num <= 0) return MaxAllocation;          // not computable — behave as before
            if (calcA.PPT < 1)
            {
                var calcB = HackCap(calcA.Offset);
                if (calcB.Num > 0) return calcB.Num;
            }
            return calcA.Num;
        }

        // `formula` is the R3 that puts progressPerTick at exactly 1.0 once the hack is `offset` levels
        // further on. It comes from the game's own progressPerTickCap rather than from baseDivider: ppt and
        // pptCap differ only in the R3 amount, so their ratio gives the saturating allocation with every
        // other term — baseDivider, res3Power, the whole hack-speed stack — cancelling out. baseDivider is
        // Unity scene data and has no value anywhere in this repository, so this is not merely tidier than
        // NGUBP's hand-rebuilt multiplier stack; it is the only way to get the number at all.
        private CapCalc HackCap(int offset)
        {
            var ret = new CapCalc(1, 0);
            var hc = _character.hacksController;

            long sat = HackMath.Saturation(_character.totalCapRes3(), hc.progressPerTickCap(Index));
            if (sat <= 0) return ret;                          // unreadable rate — caller falls back

            double formula = sat * HackMath.GrowthOverLevels(_character.hacks.hacks[Index].level, offset);
            if (formula <= 0 || double.IsNaN(formula)) return ret;

            long alloc = HackMath.SnapToStair(formula, MaxAllocation, out var ticks);
            if (alloc <= 0) return ret;

            ret.Num = alloc;
            ret.PPT = ticks > 0 ? 1.0 / ticks : 0;
            return ret;
        }
    }
}
