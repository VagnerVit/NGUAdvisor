using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using SimpleJSON;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.AllocationProfiles.Breakpoints
{
    public class MagicBreakpoints : BaseBreakpoints<ResourceBreakpoint[]>
    {
        public MagicBreakpoints() : base() { }

        public MagicBreakpoints(JSONNode bps) :
            base(bps, (bp) => ResourceBreakpoint.ParseBreakpointArray(bp["Priorities"], ResourceType.Magic).ToArray()) { }

        protected override bool PerformSwap(Breakpoint bp)
        {
            var temp = bp.priorities.Where(x => x.IsValid()).ToList();
            // Challenge overlay: narrate dead-system filtering; inject fallback if the list is all-dead.
            temp = Managers.ChallengeOverlay.TransformPriorities(bp.priorities, temp, ResourceType.Magic);
            if (temp.Count == 0)
                return false;

            var shouldRetry = true;
            bool rebalanced = false;
            while (shouldRetry)
            {
                var successList = new List<ResourceBreakpoint>();
                shouldRetry = false;
                rebalanced = false;

                var prioCount = temp.Count(x => !x.IsCap);
                int rebalanceAt = Managers.NguBbSplit.RebalanceAfter(temp.Count, i => temp[i].IsCap, i => temp[i] is NGUBP);

                RemoveMagic();

                for (int i = 0; i < temp.Count; i++)
                {
                    var prio = temp[i];
                    prio.UpdateMaxAllocation(prioCount);
                    if (prio.Allocate())
                        successList.Add(prio);
                    else
                        shouldRetry = true;

                    if (!prio.IsCap)
                        prioCount--;

                    if (i == rebalanceAt)
                    {
                        NGUBP.Rebalance(successList);
                        rebalanced = true;
                    }
                }
                temp = successList;
                shouldRetry &= temp.Count > 0;
            }

            if (!rebalanced)
                NGUBP.Rebalance(temp);

            _character.timeMachineController.updateMenu();
            _character.bloodMagicController.updateMenu();
            _character.NGUController.refreshMenu();
            _character.wandoos98Controller.refreshMenu();

            return false;
        }

        private void RemoveMagic()
        {
            _character.wandoos98Controller.removeAllMagic();
            _character.timeMachineController.removeAllMagic();
            _character.bloodMagicController.removeAllMagic();
            _character.NGUController.removeAllMagic();
        }
    }
}
