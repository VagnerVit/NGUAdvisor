using System;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    public class TimeMachineBP : ResourceBreakpoint
    {
        protected override bool CorrectResourceType() => Type == ResourceType.Energy || Type == ResourceType.Magic;

        protected override bool Unlocked() => _character.buttons.brokenTimeMachine.interactable && !_character.challenges.timeMachineChallenge.inChallenge;

        protected override bool TargetMet()
        {
            long target = Type == ResourceType.Energy ? _character.machine.speedTarget : _character.machine.multiTarget;
            long level = Type == ResourceType.Energy ? _character.machine.levelSpeed : _character.machine.levelGoldMulti;

            return target != 0 && level >= target;
        }

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
            var toAllocate = CalculateTMEnergyCap();
            SetInput(toAllocate);
            _character.timeMachineController.addEnergy();
        }

        private void AllocateMagic()
        {
            var toAllocate = CalculateTMMagicCap();
            SetInput(toAllocate);
            _character.timeMachineController.addMagic();
        }

        private long CalculateTMMagicCap()
        {
            var calcA = CalculateMagicTM(500);
            if (calcA.PPT < 1)
            {
                var calcB = CalculateMagicTM(calcA.Offset);
                return calcB.Num;
            }

            return calcA.Num;
        }

        private long CalculateTMEnergyCap()
        {
            var calcA = CalculateEnergyTM(500);
            if (calcA.PPT < 1)
            {
                var calcB = CalculateEnergyTM(calcA.Offset);
                return calcB.Num;
            }

            return calcA.Num;
        }

        #region Hidden
        private CapCalc CalculateEnergyTM(int offset)
        {
            var ret = new CapCalc(1, 0);

            var formula = 50000.0 * _character.timeMachineController.baseSpeedDivider() * (1f + _character.machine.levelSpeed + offset);
            formula /= _character.totalEnergyPower();
            formula /= _character.hacksController.totalTMSpeedBonus();
            formula /= _character.allChallenges.timeMachineChallenge.TMSpeedBonus();
            formula /= _character.cardsController.getBonus(cardBonus.TMSpeed);

            if (_character.settings.rebirthDifficulty >= difficulty.sadistic)
                formula *= _character.timeMachineController.sadisticDivider();
            formula = Math.Ceiling(formula);
            if (formula < 1.0)
                formula = 1.0;

            var num1 = Math.Ceiling(formula / Math.Ceiling(formula / MaxAllocation) * 1.00000202655792);
            long num;
            if (num1 > _character.idleEnergy)
                num = _character.idleEnergy;
            else
                num = (long)num1;

            ret.Num = num;
            ret.PPT = num1 / formula;
            return ret;
        }

        private CapCalc CalculateMagicTM(int offset)
        {
            var ret = new CapCalc(1, 0);

            var formula = 50000.0 * _character.timeMachineController.baseGoldMultiDivider() * (1f + _character.machine.levelGoldMulti + offset);
            formula /= _character.totalMagicPower();
            formula /= _character.hacksController.totalTMSpeedBonus();
            formula /= _character.allChallenges.timeMachineChallenge.TMSpeedBonus();
            formula /= _character.cardsController.getBonus(cardBonus.TMSpeed);

            if (_character.settings.rebirthDifficulty >= difficulty.sadistic)
                formula *= _character.timeMachineController.sadisticDivider();
            formula = Math.Ceiling(formula);
            if (formula < 1.0)
                formula = 1.0;

            var num1 = Math.Ceiling(formula / Math.Ceiling(formula / MaxAllocation) * 1.00000202655792);
            long num;
            if (num1 > _character.magic.idleMagic)
                num = _character.magic.idleMagic;
            else
                num = (long)num1;

            ret.Num = num;
            ret.PPT = num1 / formula;
            return ret;
        }
        #endregion

    }
}
