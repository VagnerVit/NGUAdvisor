using System;
using System.Collections.Generic;
using UnityEngine;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    public class AdvancedTrainingBP : ResourceBreakpoint
    {
        // Set on the 5 BPs yielded by ALLAT/CAPALLAT (user-reported: allocation order dumped the
        // whole pool into Defense/Attack and the wandoos ATs got nothing). When true, this slot
        // takes at most an even waterfill share of the pool across the group members that still
        // want energy, instead of its full cap.
        public bool GroupSpread { get; set; }

        protected override bool CorrectResourceType() => Type == ResourceType.Energy;

        protected override bool Unlocked() => UnlockedAt(Index);

        protected override bool TargetMet() => TargetMetAt(Index);

        private bool UnlockedAt(int index) =>
            index <= _character.advancedTrainingController.length && _character.buttons.advancedTraining.interactable;

        private bool TargetMetAt(int index)
        {
            long target = _character.advancedTraining.levelTarget[index];
            if (target < 0L)
                return true;

            return target != 0L && _character.advancedTraining.level[index] >= target;
        }

        private AdvancedTrainingController ControllerFor(int index)
        {
            var allController = _character.advancedTrainingController;

            switch (index)
            {
                case 0:
                    return allController.defense;
                case 1:
                    return allController.attack;
                case 2:
                    return allController.block;
                case 3:
                    return allController.wandoosEnergy;
                case 4:
                    return allController.wandoosMagic;
            }

            return null;
        }

        public override bool Allocate()
        {
            if (_character.wishes.wishes[190].level >= 1)
                return true;
            long amount = CalculateATCap();
            if (GroupSpread)
                amount = Math.Min(amount, GroupShare(amount));
            SetInput(amount);
            ControllerFor(Index).addEnergy();

            return true;
        }

        // Even spread across the ALLAT/CAPALLAT group: waterfill the pool over the members that
        // still want energy (this slot and the ones allocating after it — earlier slots already
        // took their share out of idleEnergy, so front-to-back shares stay consistent). A slot
        // never gets more than its need; slack from cheap slots flows to expensive ones.
        private long GroupShare(long myNeed)
        {
            try
            {
                var needs = new List<long> { myNeed };
                for (int j = Index + 1; j < 5; j++)
                {
                    if (!UnlockedAt(j) || TargetMetAt(j)) continue;
                    long n = NeedFor(j);
                    if (n > 0) needs.Add(n);
                }
                if (needs.Count <= 1) return myNeed;

                long avail = _character.idleEnergy;
                needs.Sort();
                int remaining = needs.Count;
                foreach (var n in needs)
                {
                    long share = avail / remaining;
                    if (n > share) return Math.Min(myNeed, share);   // waterlevel reached
                    avail -= n;
                    remaining--;
                }
                return myNeed;   // pool covers every member's full need
            }
            catch
            {
                return myNeed;
            }
        }

        // A group member's cap need (same formula CalculateATCap uses): funded 500 levels ahead
        // when the pool plausibly covers it, at current level otherwise.
        private long NeedFor(int index)
        {
            double f = FormulaFor(index, 500);
            if (f <= 0) return 0;
            if (f > _character.idleEnergy) f = FormulaFor(index, 0);
            if (f > long.MaxValue) return long.MaxValue;
            return (long)f;
        }

        private double FormulaFor(int index, int offset)
        {
            var divisor = GetDivisor(index, offset);
            if (divisor == 0.0)
                return 0;

            double formula = Math.Ceiling(50.0 * divisor /
                (Mathf.Sqrt(_character.totalEnergyPower()) * _character.totalAdvancedTrainingSpeedBonus()));
            return formula < 1.0 ? 1.0 : formula;
        }

        private long CalculateATCap()
        {
            var calcA = CalculateATCap(500);
            if (calcA.PPT < 1)
            {
                var calcB = CalculateATCap(calcA.Offset);
                return calcB.Num;
            }

            return calcA.Num;
        }

        private CapCalc CalculateATCap(int offset)
        {
            var ret = new CapCalc(1, 0);
            double formula = FormulaFor(Index, offset);
            if (formula == 0.0)
                return ret;

            double num = Math.Ceiling(formula / Math.Ceiling(formula / MaxAllocation) * 1.00000202655792);
            long num1;
            if (num > _character.idleEnergy)
                num1 = _character.idleEnergy;
            else
                num1 = (long)num;

            ret.Num = num1;
            ret.PPT = (double)num / formula;
            return ret;
        }

        private float GetDivisor(int index, int offset) => ControllerFor(index).baseTime * (_character.advancedTraining.level[index] + offset + 1f);
    }
}
