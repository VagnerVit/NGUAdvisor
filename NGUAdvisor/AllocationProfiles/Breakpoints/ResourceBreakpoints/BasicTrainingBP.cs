using System;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    // NOTE on the "Insta Training Cap" AP purchase (full decompile, Rebirth.instaTrain()): its ONLY
    // effect is a one-time seed at rebirth — +12 energy, 6 into the first attack and defense
    // trainings. It does NOT insta-complete bars; training speed is energy/cap per tick. So ALLBT's
    // full-cap allocation is optimal with or without the purchase, and the earlier "seed 6" special
    // case (built on a wrong model of the purchase) was reverted — it throttled training to a crawl
    // and visibly dripped 6 energy into the newest training every cycle.
    public class BasicTrainingBP : ResourceBreakpoint
    {
        protected override bool CorrectResourceType() => Type == ResourceType.Energy;

        protected override bool Unlocked()
        {
            if (Index > 11)
                return false;

            if (Index % 6 == 0)
                return true;

            long[] trainings = Index <= 5 ? _character.training.attackTraining : _character.training.defenseTraining;

            return trainings[Index % 6 - 1] >= 5000 * (Index % 6);
        }

        protected override bool TargetMet() => false;

        // The parameterless addEnergy() honours the game's syncTraining toggle, which mirrors this
        // slot's amount into the opposite tree and halves the input on top of that. Attack and
        // defense caps drift apart with levels, so the mirrored amount is wrong for the receiving
        // slot. The addEnergy(long) overload skips the mirror; every slot is allocated explicitly.
        public override bool Allocate()
        {
            int slot = Index % 6;

            if (Index <= 5)
            {
                long cap = _character.training.attackCaps[slot];
                _character.allOffenseController.trains[slot].addEnergy(Math.Min(cap, MaxAllocation));
            }
            else
            {
                long cap = _character.training.defenseCaps[slot];
                _character.allDefenseController.trains[slot].addEnergy(Math.Min(cap, MaxAllocation));
            }

            return true;
        }
    }
}
