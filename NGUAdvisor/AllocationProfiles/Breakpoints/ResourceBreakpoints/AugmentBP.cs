using NGUAdvisor.Managers;
using System;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    public class AugmentBP : ResourceBreakpoint
    {
        protected override bool CorrectResourceType() => Type == ResourceType.Energy;

        protected override bool Unlocked()
        {
            if (!_character.buttons.augmentation.interactable)
                return false;

            if (_character.challenges.noAugsChallenge.inChallenge)
                return false;

            if (Index > 13)
                return false;

            if (Index % 2 == 0)
                return _character.bossID > _character.augmentsController.augments[Index / 2].augBossRequired;

            return _character.bossID > _character.augmentsController.augments[Index / 2].upgradeBossRequired;
        }

        protected override bool TargetMet()
        {
            if (Index % 2 == 0)
            {
                long target = _character.augments.augs[Index / 2].augmentTarget;
                return target != 0 && _character.augments.augs[Index / 2].augLevel >= target;
            }
            else
            {
                long target = _character.augments.augs[Index / 2].upgradeTarget;
                return target != 0 && _character.augments.augs[Index / 2].upgradeLevel >= target;
            }
        }

        public override bool Allocate()
        {
            if (Main.Settings.MoneyPitRunMode && _character.machine.realBaseGold <= 0.0 && MoneyPitManager.NeedsLowerTier())
                return false;

            double gold = _character.realGold;
            var aug = _character.augmentsController.augments[Index / 2];

            long alloc = CalculateAugCap(Index, MaxAllocation);
            SetInput(alloc);

            float progress = Index % 2 == 0 ? aug.AugProgress() : aug.UpgradeProgress();

            // If aug has no progress check the cost before allocating energy
            if (progress == 0f)
            {
                double time = Index % 2 == 0 ? aug.AugTimeLeftEnergyMax(alloc) : aug.UpgradeTimeLeftEnergyMax(alloc);
                if (time < 0.01) { time = 0.01d; }
                // the cost is a rough estimate of running for 1 second, min of basecost, max of basecost*100;
                // does not consider the rising price of augments nor the gold that will be gained until the next energy allocation
                double cost = (double)Math.Max(1, 1d / time) * (Index % 2 == 0 ? (double)aug.getAugCost() : (double)aug.getUpgradeCost());

                if (cost > gold)
                    return false;
            }

            if (Index % 2 == 0)
                aug.addEnergyAug();
            else
                aug.addEnergyUpgrade();

            return true;
        }

        public long CalculateAugCap(int index, float allocation)
        {
            var calcA = CalculateAugCapCalc(500, index, allocation);
            if (calcA.PPT < 1)
            {
                var calcB = CalculateAugCapCalc(calcA.Offset, index, allocation);
                return calcB.Num;
            }

            return calcA.Num;
        }

        public CapCalc CalculateAugCapCalc(int offset, int index, float allocation)
        {
            int augIndex;
            var ret = new CapCalc(1, 0);
            double num1;

            if (index % 2 == 0)
            {
                augIndex = index / 2;
                num1 = 1 / (_character.totalEnergyPower() / (_character.augments.augs[augIndex].augLevel + 1.0 + offset));
                if (_character.settings.rebirthDifficulty == difficulty.normal)
                    num1 *= 50000.0 * _character.augmentsController.normalAugSpeedDividers[augIndex];
                else if (_character.settings.rebirthDifficulty == difficulty.evil)
                    num1 *= 50000.0 * _character.augmentsController.evilAugSpeedDividers[augIndex];
                else if (_character.settings.rebirthDifficulty == difficulty.sadistic)
                    num1 *= _character.augmentsController.sadisticAugSpeedDividers[augIndex];
            }
            else
            {
                augIndex = (index - 1) / 2;
                num1 = 1 / (_character.totalEnergyPower() / (_character.augments.augs[augIndex].upgradeLevel + 1.0 + offset));
                if (_character.settings.rebirthDifficulty == difficulty.normal)
                    num1 *= 50000.0 * _character.augmentsController.normalUpgradeSpeedDividers[augIndex];
                else if (_character.settings.rebirthDifficulty == difficulty.evil)
                    num1 *= 50000.0 * _character.augmentsController.evilUpgradeSpeedDividers[augIndex];
                else if (_character.settings.rebirthDifficulty == difficulty.sadistic)
                    num1 *= _character.augmentsController.sadisticUpgradeSpeedDividers[augIndex];
            }

            num1 /= 1.0 + _character.inventoryController.bonuses[specType.Augs];
            num1 /= _character.inventory.macguffinBonuses[12];
            num1 /= _character.hacksController.totalAugSpeedBonus();
            num1 /= _character.adventureController.itopod.totalAugSpeedBonus();
            num1 /= _character.cardsController.getBonus(cardBonus.augSpeed);
            num1 /= 1.0 + _character.allChallenges.noAugsChallenge.evilCompletions() * 0.05;

            if (_character.allChallenges.noAugsChallenge.completions() >= 1)
                num1 /= 1.1000000238418579;

            if (_character.allChallenges.noAugsChallenge.evilCompletions() >= _character.allChallenges.noAugsChallenge.maxCompletions)
                num1 /= 1.25;

            if (_character.settings.rebirthDifficulty >= difficulty.sadistic)
                num1 *= _character.augmentsController.augments[augIndex].sadisticDivider();

            num1 = Math.Ceiling(num1);

            if (num1 < 1.0)
                num1 = 1.0;

            double num = Math.Ceiling(num1 / Math.Ceiling(num1 / allocation) * 1.00000202655792);

            long num2;
            if (num > _character.idleEnergy)
                num2 = _character.idleEnergy;
            else
                num2 = (long)num;

            double ppt = num / num1;

            ret.Num = num2;
            ret.PPT = ppt;

            return ret;
        }
    }
}
