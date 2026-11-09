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
                    // The No-NGU challenge and the Troll "disable NGU" roll set NGU.disabled but leave the button interactable.
                    return _character.buttons.ngu.interactable && !_character.NGU.disabled;
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
        // (NguBbSplit). Runs where NguBbSplit.RebalanceAfter puts it; every token walked before keeps what it took.
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

            // The per-unit rate does not depend on k or on what a lane holds, so it is read once per lane.
            double[] perUnit = lanes.Select(l => l.PerUnit()).ToArray();
            NguBbSplit.Result plan = NguBbSplit.Plan((i, k) => lanes[i].BbAt(k, perUnit[i]), lanes.Count, pool);
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

        public int LaneIndex => Index;

        public long HeldNow => Held();

        // BB at one level per tick with every NGU-speed term scaled by speedScale: the BB this lane
        // would face under another gear set (NguBbGear).
        public double BbWithSpeed(double speedScale) => Bb(NguLaneRank.BbLookaheadLevels, speedScale);

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
        private double BbAt(int k, double perUnit) => Bb(500 / k, 1, perUnit);

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

            var num3 = Bb(offset);
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

        private double Bb(int offset, double speedScale = 1) => Bb(offset, speedScale, PerUnit());

        private double Bb(int offset, double speedScale, double perUnit)
            => NguLaneRank.BbCost(perUnit, NGUAdvisors.Level(_character, Type == ResourceType.Magic, Index), offset, speedScale);

        private double PerUnit() => NGUAdvisors.ProgressPerUnit(_character, Type == ResourceType.Magic, Index);

        private CapCalc GetNGUMagicCapCalc(int offset)
        {
            var ret = new CapCalc(1, 0);

            var num3 = Bb(offset);
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
    }
}
