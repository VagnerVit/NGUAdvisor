using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using SimpleJSON;
using System.Linq;

namespace NGUAdvisor.AllocationProfiles.Breakpoints
{
    public class R3Breakpoints : BaseBreakpoints<ResourceBreakpoint[]>
    {
        public R3Breakpoints() : base() { }

        public R3Breakpoints(JSONNode bps) :
            base(bps, (bp) => ResourceBreakpoint.ParseBreakpointArray(bp["Priorities"], ResourceType.R3).ToArray()) { }

        protected override bool PerformSwap(Breakpoint bp)
        {
            var valid = bp.priorities.Where(x => x.IsValid()).ToList();
            // Challenge overlay: narrate dead-system filtering; inject fallback if the list is all-dead.
            valid = Managers.ChallengeOverlay.TransformPriorities(bp.priorities, valid, ResourceType.R3);
            var prio = valid.FirstOrDefault();
            if (prio != null)
            {
                RemoveR3();

                prio.UpdateMaxAllocation();
                prio.Allocate();

                _character.hacksController.refreshMenu();
            }

            return false;
        }

        private void RemoveR3() => _character.hacksController.removeAllR3();
    }
}
