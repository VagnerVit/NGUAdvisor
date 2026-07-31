using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using SimpleJSON;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.AllocationProfiles.Breakpoints
{
    public class EnergyBreakpoints : BaseBreakpoints<ResourceBreakpoint[]>
    {
        public EnergyBreakpoints() : base() { }

        public EnergyBreakpoints(JSONNode bps) :
            base(bps, (bp) => ResourceBreakpoint.ParseBreakpointArray(bp["Priorities"], ResourceType.Energy).ToArray()) { }

        // Seconds until the augment phase ends: the earliest LATER breakpoint on the ACTIVE timeline
        // (challenge-tagged when one is selected, exactly as GetCurrentBreakpoint chooses) that funds
        // no augment at all. -1 when the phase runs to the end of the run.
        //
        // BestAug needs this because augment boost grows as time^(1 + tier/2) — a fixed one-hour
        // horizon underprices the steep augs in Ch.5's 2.5h phase and overprices them in a 30m run.
        // Past the phase end a level still in flight can never complete (nothing funds it again before
        // the rebirth wipes the levels), so the phase end is as hard a stop as the rebirth itself.
        public double AugmentPhaseSecondsLeft()
        {
            if (breakpoints == null)
                return -1;

            double t = _character.rebirthTime.totalseconds;
            string cur = Managers.ChallengeDetector.Current();
            bool tagged = cur != null && breakpoints.Any(b => b.challenge == cur && t > b.time);

            foreach (var b in breakpoints.Where(b => tagged ? b.challenge == cur : b.challenge == null)
                                         .OrderBy(b => b.time))
            {
                if (b.time <= t)
                    continue;
                if (!b.priorities.Any(p => p is AugmentBP))
                    return b.time - t;
            }

            return -1;
        }

        protected override bool PerformSwap(Breakpoint bp)
        {
            var temp = bp.priorities.Where(x => x.IsValid()).ToList();
            // Challenge overlay: narrate dead-system filtering; inject fallback if the list is all-dead.
            temp = Managers.ChallengeOverlay.TransformPriorities(bp.priorities, temp, ResourceType.Energy);
            if (temp.Count == 0)
                return false;

            var shouldRetry = true;
            while (shouldRetry)
            {
                var successList = new List<ResourceBreakpoint>();
                shouldRetry = false;
                var prioCount = temp.Count(x => !x.IsCap);

                RemoveEnergy(temp.Exists(x => x is BasicTrainingBP));

                foreach (var prio in temp)
                {
                    prio.UpdateMaxAllocation(prioCount);
                    if (prio.Allocate())
                        successList.Add(prio);
                    else
                        shouldRetry = true;

                    if (!prio.IsCap)
                        prioCount--;
                }
                temp = successList;
                shouldRetry &= temp.Count > 0;
            }

            _character.NGUController.refreshMenu();
            _character.wandoos98Controller.refreshMenu();
            _character.advancedTrainingController.refresh();
            _character.timeMachineController.updateMenu();
            _character.allOffenseController.refresh();
            _character.allDefenseController.refresh();
            _character.augmentsController.updateMenu();

            return false;
        }

        private void RemoveEnergy(bool removeBT)
        {
            _character.wandoos98Controller.removeAllEnergy();
            _character.augmentsController.removeAllEnergy();
            _character.timeMachineController.removeAllEnergy();
            _character.advancedTrainingController.removeAllEnergy();
            _character.NGUController.removeAllEnergy();
            if (removeBT)
            {
                _character.allOffenseController.removeAllEnergy();
                _character.allDefenseController.removeAllEnergy();
            }
        }
    }
}
