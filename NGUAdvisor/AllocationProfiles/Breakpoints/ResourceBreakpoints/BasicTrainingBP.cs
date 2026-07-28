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

        public override bool Allocate()
        {
            if (Index <= 5)
            {
                var cap = _character.training.attackCaps[Index % 6];
                SetInput(Math.Min(cap, MaxAllocation));
                _character.allOffenseController.trains[Index % 6].addEnergy();
            }
            else
            {
                var cap = _character.training.defenseCaps[Index % 6];
                SetInput(Math.Min(cap, MaxAllocation));
                _character.allDefenseController.trains[Index % 6].addEnergy();
            }

            return true;
        }
    }
}
