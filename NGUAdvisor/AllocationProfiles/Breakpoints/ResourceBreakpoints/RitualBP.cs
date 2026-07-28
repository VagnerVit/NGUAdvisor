using System;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    public class RitualBP : ResourceBreakpoint
    {
        protected override bool CorrectResourceType() => Type == ResourceType.Magic;

        protected override bool Unlocked() => Index <= _character.bloodMagicController.ritualsUnlocked() && _character.buttons.bloodMagic.interactable;

        protected override bool TargetMet() => false;

        public override bool Allocate()
        {
            float goldCost = _character.bloodMagicController.bloodMagics[Index].baseCost * _character.totalDiscount();
            if (goldCost > _character.realGold && _character.bloodMagic.ritual[Index].progress <= 0)
            {
                if (_character.bloodMagic.ritual[Index].magic > 0)
                    _character.bloodMagicController.bloodMagics[Index].removeAllMagic();

                return false;
            }

            var cap = GetRitualCap(Index);
            SetInput(Math.Min(cap, MaxAllocation));
            _character.bloodMagicController.bloodMagics[Index].add();
            return true;
        }

        private long GetRitualCap(int index)
        {
            var num = 1 / (_character.totalMagicPower() * (double)_character.bloodMagicController.bloodMagics[index].totalBloodMagicSpeedBonus());

            if (_character.settings.rebirthDifficulty == difficulty.normal)
                num *= 50000.0 * _character.bloodMagicController.normalSpeedDividers[index];
            else if (_character.settings.rebirthDifficulty == difficulty.evil)
                num *= 50000.0 * _character.bloodMagicController.evilSpeedDividers[index];
            else if (_character.settings.rebirthDifficulty == difficulty.sadistic)
                num *= _character.bloodMagicController.bloodMagics[index].sadisticDivider() * _character.bloodMagicController.sadisticSpeedDividers[index];
            else
                return 0L;

            num = Math.Ceiling(num);

            if (num < 1.0)
                num = 1.0;

            var num1 = Math.Ceiling(num / Math.Ceiling(num / MaxAllocation) * 1.00000202655792);

            long num2;
            if (num1 > _character.magic.idleMagic)
                num2 = _character.magic.idleMagic;
            else
                num2 = (long)num1;

            return num2;
        }
    }
}
