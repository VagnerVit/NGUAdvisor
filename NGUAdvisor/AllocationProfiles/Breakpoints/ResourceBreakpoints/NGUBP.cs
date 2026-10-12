using NGUAdvisor.Managers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    public class NGUBP : ResourceBreakpoint
    {
        protected override bool Unlocked()
        {
            switch (Type)
            {
                case ResourceType.Magic when Index <= 6:
                case ResourceType.Energy when Index <= 8:
                    return _character.buttons.ngu.interactable;
            }

            return false;
        }

        protected override bool TargetMet()
        {
            var track = _character.settings.nguLevelTrack;
            var ngus = Type == ResourceType.Energy ? _character.NGU.skills : _character.NGU.magicSkills;
            long target;
            long level;
            switch (track)
            {
                case difficulty.normal:
                    target = ngus[Index].target;
                    level = ngus[Index].level;
                    break;
                case difficulty.evil:
                    target = ngus[Index].evilTarget;
                    level = ngus[Index].evilLevel;
                    break;
                default:
                    target = ngus[Index].sadisticTarget;
                    level = ngus[Index].sadisticLevel;
                    break;
            }

            if (target < 0)
                return true;

            return target > 0 && level >= target;
        }

        public override bool Allocate()
        {
            if (Type == ResourceType.Energy)
                AllocateEnergy();
            else
                AllocateMagic();

            return true;
        }

        private void AllocateMagic()
        {
            var alloc = CalculateNGUMagicCap();
            SetInput(alloc);
            _character.NGUController.NGUMagic[Index].add();
        }

        private void AllocateEnergy()
        {
            var alloc = CalculateNGUEnergyCap();
            SetInput(alloc);
            _character.NGUController.NGU[Index].add();
        }

        protected override bool CorrectResourceType() => Type == ResourceType.Energy || Type == ResourceType.Magic;

        // Re-splits what the equal-share NGU lanes hold, plus the idle pool, along BB breakpoints
        // (NguBbSplit). Runs after the whole walk, so every other token keeps what it took.
        public static void Rebalance(IEnumerable<ResourceBreakpoint> walked)
        {
            List<NGUBP> lanes = walked.OfType<NGUBP>().Where(x => !x.IsCap)
                .GroupBy(x => x.Index).Select(g => g.First()).ToList();
            if (lanes.Count == 0)
                return;

            bool magic = lanes[0].Type == ResourceType.Magic;
            double pool = magic ? _character.magic.idleMagic : _character.idleEnergy;
            foreach (NGUBP lane in lanes)
                pool += lane.Held();

            NguBbSplit.Result plan = NguBbSplit.Plan((i, k) => lanes[i].BbAt(k), lanes.Count, pool);
            foreach (NGUBP lane in lanes)
                lane.Release();
            for (int i = 0; i < lanes.Count; i++)
            {
                if (plan.Amounts[i] <= 0)
                    continue;
                lanes[i].SetInput(plan.Amounts[i]);
                if (magic)
                    _character.NGUController.NGUMagic[lanes[i].Index].add();
                else
                    _character.NGUController.NGU[lanes[i].Index].add();
            }
        }

        private long Held() => Type == ResourceType.Magic
            ? _character.NGU.magicSkills[Index].magic
            : _character.NGU.skills[Index].energy;

        private void Release()
        {
            if (Type == ResourceType.Magic)
                _character.NGUController.NGUMagic[Index].removeAllMagic();
            else
                _character.NGUController.NGU[Index].removeAllEnergy();
        }

        // BB at one level per k ticks, with the same 10 s lookahead the cap calc uses at that speed.
        private double BbAt(int k)
        {
            int offset = 500 / k;
            return Type == ResourceType.Magic ? MagicBb(offset) : EnergyBb(offset);
        }

        private long CalculateNGUEnergyCap()
        {
            var calcA = GetNGUEnergyCapCalc(500);
            if (calcA.PPT < 1)
            {
                var calcB = GetNGUEnergyCapCalc(calcA.Offset);
                return calcB.Num;
            }

            return calcA.Num;
        }

        private long CalculateNGUMagicCap()
        {
            var calcA = GetNGUMagicCapCalc(500);
            if (calcA.PPT < 1)
            {
                var calcB = GetNGUMagicCapCalc(calcA.Offset);
                return calcB.Num;
            }

            return calcA.Num;
        }

        private CapCalc GetNGUEnergyCapCalc(int offset)
        {
            var ret = new CapCalc(1, 0);

            var num3 = EnergyBb(offset);
            var num4 = Math.Ceiling(num3 / Math.Ceiling(num3 / MaxAllocation) * 1.00000202655792);
            long num;
            if (num4 > _character.idleEnergy)
                num = _character.idleEnergy;
            else
                num = (long)num4;

            var ppt = num4 / num3;
            ret.Num = num;
            ret.PPT = ppt;
            return ret;
        }

        private double EnergyBb(int offset)
        {
            var num1 = 0.0f;
            if (_character.settings.nguLevelTrack == difficulty.normal)
                num1 = _character.NGU.skills[Index].level + 1L + offset;
            else if (_character.settings.nguLevelTrack == difficulty.evil)
                num1 = _character.NGU.skills[Index].evilLevel + 1L + offset;
            else if (_character.settings.nguLevelTrack == difficulty.sadistic)
                num1 = _character.NGU.skills[Index].sadisticLevel + 1L + offset;

            var num2 = _character.totalEnergyPower() * (double)_character.totalNGUSpeedBonus();
            num2 *= _character.adventureController.itopod.totalEnergyNGUBonus() * _character.inventory.macguffinBonuses[4];
            num2 *= _character.NGUController.energyNGUBonus() * _character.allDiggers.totalEnergyNGUBonus();
            num2 *= _character.hacksController.totalEnergyNGUBonus() * _character.beastQuestPerkController.totalEnergyNGUSpeed();
            num2 *= _character.wishesController.totalEnergyNGUSpeed() * _character.cardsController.getBonus(cardBonus.energyNGUSpeed);
            if (_character.allChallenges.trollChallenge.sadisticCompletions() >= 1)
                num2 *= 3.0;
            if (_character.settings.nguLevelTrack >= difficulty.sadistic)
                num2 /= _character.NGUController.NGU[0].sadisticDivider();
            var num3 = Math.Ceiling(_character.NGUController.energySpeedDivider(Index) * (double)num1 / num2);
            return num3 < 1.0 ? 1.0 : num3;
        }

        private CapCalc GetNGUMagicCapCalc(int offset)
        {
            var ret = new CapCalc(1, 0);

            var num3 = MagicBb(offset);
            var num4 = Math.Ceiling(num3 / Math.Ceiling(num3 / MaxAllocation) * 1.00000202655792);
            long num;
            if (num4 > _character.magic.idleMagic)
                num = _character.magic.idleMagic;
            else
                num = (long)num4;

            var ppt = num4 / num3;
            ret.Num = num;
            ret.PPT = ppt;
            return ret;
        }

        private double MagicBb(int offset)
        {
            var num1 = 0.0f;
            if (_character.settings.nguLevelTrack == difficulty.normal)
                num1 = _character.NGU.magicSkills[Index].level + 1L + offset;
            else if (_character.settings.nguLevelTrack == difficulty.evil)
                num1 = _character.NGU.magicSkills[Index].evilLevel + 1L + offset;
            else if (_character.settings.nguLevelTrack == difficulty.sadistic)
                num1 = _character.NGU.magicSkills[Index].sadisticLevel + 1L + offset;

            var num2 = _character.totalMagicPower() * (double)_character.totalNGUSpeedBonus();
            num2 *= _character.adventureController.itopod.totalMagicNGUBonus() * _character.inventory.macguffinBonuses[5];
            num2 *= _character.NGUController.magicNGUBonus() * _character.allDiggers.totalMagicNGUBonus();
            num2 *= _character.hacksController.totalMagicNGUBonus() * _character.beastQuestPerkController.totalMagicNGUSpeed();
            num2 *= _character.wishesController.totalMagicNGUSpeed() * _character.cardsController.getBonus(cardBonus.magicNGUSpeed);
            if (_character.allChallenges.trollChallenge.completions() >= 1)
                num2 *= 3.0;
            if (_character.settings.nguLevelTrack >= difficulty.sadistic)
                num2 /= _character.NGUController.NGUMagic[0].sadisticDivider();
            var num3 = Math.Ceiling(_character.NGUController.magicSpeedDivider(Index) * (double)num1 / num2);
            return num3 < 1.0 ? 1.0 : num3;
        }
    }
}
