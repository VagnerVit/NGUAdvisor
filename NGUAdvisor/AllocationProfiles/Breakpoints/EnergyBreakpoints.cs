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
