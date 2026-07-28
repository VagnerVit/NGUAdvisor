using System;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    public class BR : ResourceBreakpoint
    {
        protected override bool CorrectResourceType() => Type == ResourceType.Magic;

        protected override bool Unlocked() => _character.buttons.bloodMagic.interactable;

        protected override bool TargetMet() => false;

        public override bool Allocate() => CastRituals(Index) > 0;

        // The run's planned end, read from the LIVE profile (as BestAug/BloodPlanner/WandoosAdvisor do).
        // The old `RebirthTime` property was never populated: ParseBreakpointArray took a rebirthTime
        // argument that no caller ever passed, so it stayed 0, the guard below could never fire, and
        // rituals that cannot finish before the rebirth were funded anyway. -1 = no scheduled rebirth.
        private double RebirthDeadline()
        {
            if (!Main.Settings.AutoRebirth) return -1;
            try { return Main.Profile != null ? Main.Profile.NextRebirthTargetSeconds() : -1; } catch { return -1; }
        }

        private long CastRituals(int secondsToRun)
        {
            long allocationLeft = MaxAllocation;
            var totalAllocated = 0L;
            double deadline = RebirthDeadline();

            for (var i = _character.bloodMagic.ritual.Count - 1; i >= 0; i--)
            {
                if (allocationLeft <= 0)
                    break;
                if (_character.magic.idleMagic == 0)
                    break;
                if (i >= _character.bloodMagicController.ritualsUnlocked())
                    continue;

                float goldCost = _character.bloodMagicController.bloodMagics[i].baseCost * _character.totalDiscount();
                var shouldSkip = false;

                // Ritual costs too much gold
                if (goldCost > _character.realGold && _character.bloodMagic.ritual[i].progress <= 0.0)
                {
                    shouldSkip = true;
                }
                else
                {
                    float tLeft = RitualTimeLeft(i, allocationLeft);
                    double completeTime = _character.rebirthTime.totalseconds + tLeft;

                    // Ritual will not finish before rebirth
                    if (deadline > 0 && completeTime > deadline)
                    {
                        shouldSkip = true;
                    }
                    else
                    {
                        // If the runtime is explicitly set, ignore the breakpoint logic
                        if (secondsToRun > 0)
                        {
                            // The time left is more than the configured number of seconds to run
                            if (tLeft > secondsToRun)
                                shouldSkip = true;
                        }
                        // The time left is more than an hour
                        else if (tLeft > 3600)
                        {
                            shouldSkip = true;
                        }
                    }
                }

                if (shouldSkip)
                {
                    if (_character.bloodMagic.ritual[i].magic > 0)
                        _character.bloodMagicController.bloodMagics[i].removeAllMagic();

                    continue;
                }

                var cap = CalculateMaxAllocation(i, allocationLeft);
                SetInput(cap);
                _character.bloodMagicController.bloodMagics[i].add();
                totalAllocated += cap;
                allocationLeft -= cap;
            }

            return totalAllocated;
        }

        private float RitualProgressPerTick(int id, long remaining)
        {
            var num1 = remaining * (double)_character.totalMagicPower();

            if (_character.settings.rebirthDifficulty == difficulty.normal)
                num1 /= 50000.0 * _character.bloodMagicController.normalSpeedDividers[id];
            else if (_character.settings.rebirthDifficulty == difficulty.evil)
                num1 /= 50000.0 * _character.bloodMagicController.evilSpeedDividers[id];
            else if (_character.settings.rebirthDifficulty == difficulty.sadistic)
                num1 /= _character.bloodMagicController.sadisticSpeedDividers[id];
            if (_character.settings.rebirthDifficulty >= difficulty.sadistic)
                num1 /= _character.bloodMagicController.bloodMagics[id].sadisticDivider();

            var num2 = num1 * _character.bloodMagicController.bloodMagics[id].totalBloodMagicSpeedBonus();

            if (num2 <= 0.0)
                num2 = 0.0;

            if (num2 >= float.MaxValue)
                num2 = float.MaxValue;

            return (float)num2;
        }

        public float RitualTimeLeft(int id, long remaining)
        {
            return (float)((1.0 - _character.bloodMagic.ritual[id].progress) / RitualProgressPerTick(id, remaining) / 50.0);
        }

        private long CalculateMaxAllocation(int id, long remaining)
        {
            long num1 = _character.bloodMagicController.bloodMagics[id].capValue();
            if (remaining > num1)
                return num1;

            var num2 = (long)(num1 / Math.Ceiling(num1 / (double)remaining)) + 1L;
            return num2;
        }
    }
}
