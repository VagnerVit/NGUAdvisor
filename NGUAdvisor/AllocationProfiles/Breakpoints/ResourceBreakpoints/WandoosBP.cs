using System;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    public class WandoosBP : ResourceBreakpoint
    {
        protected override bool CorrectResourceType() => Type == ResourceType.Energy || Type == ResourceType.Magic;

        protected override bool Unlocked() => _character.buttons.wandoos.interactable && !_character.wandoos98.disabled;

        protected override bool TargetMet() => false;

        public override bool Allocate()
        {
            if (Type == ResourceType.Energy)
                AllocateEnergy();
            else
                AllocateMagic();
            return true;
        }

        private void AllocateEnergy()
        {
            var num = Math.Ceiling((double)_character.wandoos98Controller.baseEnergyTime() / _character.totalWandoosEnergySpeed());
            if (num < 1.0)
                num = 1.0;
            var num1 = Math.Ceiling(num / Math.Ceiling(num / MaxAllocation) * 1.000002f);
            long num2;
            if (num1 > _character.idleEnergy)
                num2 = _character.idleEnergy;
            else
                num2 = (long)num1;
            SetInput(num2);
            _character.wandoos98Controller.addEnergy();
        }

        private void AllocateMagic()
        {
            var num = Math.Ceiling((double)_character.wandoos98Controller.baseMagicTime() / _character.totalWandoosMagicSpeed());
            if (num < 1.0)
                num = 1.0;
            var num1 = Math.Ceiling(num / Math.Ceiling(num / MaxAllocation) * 1.000002f);
            long num2;
            if (num1 > _character.magic.idleMagic)
                num2 = _character.magic.idleMagic;
            else
                num2 = (long)num1;
            SetInput(num2);
            _character.wandoos98Controller.addMagic();
        }
    }
}
