using System;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    public class WandoosBP : ResourceBreakpoint
    {
        protected override bool CorrectResourceType() => Type == ResourceType.Energy || Type == ResourceType.Magic;

        protected override bool Unlocked() => _character.buttons.wandoos.interactable && !_character.wandoos98.disabled;

        // This used to be a hardcoded `false`, which is what made the lane a leftovers BLACK HOLE
        // (AllocationProfiles.md, "WAN/CAPWAN"): it never dropped out, never released its share, and
        // the ceil() math below makes it request its ENTIRE ceiling on every pass whenever the
        // 1-level-per-tick allocation is out of reach — the normal case, not the edge case. Retire
        // the lane instead once even its whole ceiling cannot buy one boss (10x A/D) over the rest
        // of the run; the dump levels die at the rebirth, so a lane that slow is pure loss.
        //
        // EXCEPTION: inside a challenge block / NORB / NOAUG / gold-starved run, Wandoos IS the
        // power source — ask the owning module rather than re-deriving that context here.
        protected override bool TargetMet()
        {
            try
            {
                if (Managers.OptimizationAdvisor.WandoosIsPowerSource())
                    return false;
                return !Managers.WandoosAdvisor.DumpWorthwhile(Type == ResourceType.Energy, CeilingAllocation());
            }
            catch
            {
                return false;
            }
        }

        public override bool Allocate()
        {
            if (Type == ResourceType.Energy)
                AllocateEnergy();
            else
                AllocateMagic();
            return true;
        }

        // BB (bar breakpoint) = the allocation at which the bar levels once per TICK, which the game caps
        // at one level per tick — energy past BB buys literally nothing. `capAmountEnergy/Magic()` is the
        // game's own BB (`baseTime/speed + 1`, difficulty-aware), so it is read rather than re-derived.
        //
        // Allocate the HEADROOM to BB, never BB itself, because `addEnergy()`/`addMagic()` only ADD — they
        // do not clamp (decomp: `wandoosEnergy += min(input, idleEnergy)`). A profile carrying two Wandoos
        // lanes — "CAPWAN:50" plus a trailing "WAN", as CBlock1 does — therefore used to stack a second BB
        // onto an already-satisfied bar, and every unit of that second helping was dead.
        //
        // Returning EARLY is the redirect: whatever we leave in the idle pool is what the tokens after us
        // pick up, since `UpdateMaxAllocation` re-reads the live pool per token. Nothing needs to be handed
        // anywhere explicitly. `Allocate()` still reports true — a BB-capped lane has succeeded, and a
        // false here would re-run the whole pass looking for a fix that does not exist.
        private void AllocateEnergy()
        {
            long bb = _character.wandoos98Controller.capAmountEnergy();
            double num = bb - _character.wandoos98.wandoosEnergy;
            if (num < 1.0)
            {
                LogWandoosDbg("energy", "STOOD DOWN", bb, 0);
                return;
            }
            var num1 = Math.Ceiling(num / Math.Ceiling(num / MaxAllocation) * 1.000002f);
            long num2;
            if (num1 > _character.idleEnergy)
                num2 = _character.idleEnergy;
            else
                num2 = (long)num1;
            SetInput(num2);
            _character.wandoos98Controller.addEnergy();
            LogWandoosDbg("energy", "RUNNING", bb, num2);
        }

        private void AllocateMagic()
        {
            long bb = _character.wandoos98Controller.capAmountMagic();
            double num = bb - _character.wandoos98.wandoosMagic;
            if (num < 1.0)
            {
                LogWandoosDbg("magic", "STOOD DOWN", bb, 0);
                return;
            }
            var num1 = Math.Ceiling(num / Math.Ceiling(num / MaxAllocation) * 1.000002f);
            long num2;
            if (num1 > _character.magic.idleMagic)
                num2 = _character.magic.idleMagic;
            else
                num2 = (long)num1;
            SetInput(num2);
            _character.wandoos98Controller.addMagic();
            LogWandoosDbg("magic", "RUNNING", bb, num2);
        }

        // Standing down is INVISIBLE in the game UI (a BB-capped bar looks identical to a funded one), so
        // both outcomes get a line: without the RUNNING case, an empty log cannot be told apart from a lane
        // that never ran. `bb` vs `held` is also the answer to "how far am I from the cap".
        //
        // Deduped per resource AND per verdict — each is a STATE that would otherwise print every pass for
        // the rest of the run; keying on the verdict too means the transition into BB-capped prints at once
        // instead of waiting out the running line's throttle.
        private static readonly System.Collections.Generic.Dictionary<string, DateTime> _lastDbgLog =
            new System.Collections.Generic.Dictionary<string, DateTime>();

        private void LogWandoosDbg(string what, string verdict, long bb, long added)
        {
            try
            {
                string key = what + "|" + verdict;
                if (_lastDbgLog.TryGetValue(key, out var at) && (DateTime.UtcNow - at).TotalSeconds < 300)
                    return;
                _lastDbgLog[key] = DateTime.UtcNow;
                bool e = Type == ResourceType.Energy;
                long held = e ? _character.wandoos98.wandoosEnergy : _character.wandoos98.wandoosMagic;
                long idle = e ? _character.idleEnergy : _character.magic.idleMagic;
                string tail = verdict == "STOOD DOWN"
                    ? $"released={idle} to the tokens after it"
                    : $"added={added} idleLeft={idle} (bb out of reach — this lane is not capped)";
                Main.LogDebug($"[WandoosDbg] {what} {verdict} os={_character.wandoos98.os} bb={bb}"
                            + $" held={held} ceiling={MaxAllocation} {tail}");
            }
            catch { }
        }
    }
}
