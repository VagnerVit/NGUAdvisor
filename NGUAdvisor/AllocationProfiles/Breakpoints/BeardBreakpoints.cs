using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using NGUAdvisor.Managers;
using SimpleJSON;
using System.Linq;

namespace NGUAdvisor.AllocationProfiles.Breakpoints
{
    public class BeardBreakpoints : BaseBreakpoints<int[]>
    {
        private readonly DiggerBreakpoints diggerbp;

        // The loaded profile's beard timeline, same contract as DiggerBreakpoints.Current.
        public static BeardBreakpoints Current { get; private set; }

        public BeardBreakpoints(DiggerBreakpoints diggerbp) : base()
        {
            this.diggerbp = diggerbp;
            Current = this;
        }

        public BeardBreakpoints(JSONNode bps, DiggerBreakpoints diggerbp) :
            base(bps, (bp) => bp["List"].AsArray.Children.Select(x => x.AsInt).Where(x => x <= 6).ToArray())
        {
            this.diggerbp = diggerbp;
            Current = this;
        }

        // The profile's beard list for the current rebirth time / active challenge, or null when the
        // profile names no beards — in which case the advisor generates its own set.
        public static int[] ActiveProfileBeards()
        {
            try { return Current?.GetCurrentBreakpoint()?.priorities; }
            catch { return null; }
        }

        protected override bool PerformSwap(Breakpoint bp)
        {
            if (!LockManager.CanSwap())
                return false;

            // Advisor auto-apply (Phase B): while enabled (and not in a challenge), the advisor's
            // goal-aware set replaces the profile's list at every swap.
            var target = bp.priorities;
            if (Main.Settings != null && Main.Settings.AdvisorBeards)
                target = OptimizationAdvisor.CurrentBeardSet() ?? target;

            if (BeardManager.EquipBeards(target))
            {
                Main.Log($"Equipping Beards: {string.Join(", ", target)}");
                current = bp;
                diggerbp.Reset(); // Diggers could turn off due to a deactivation of the Golden Beard
                return true;
            }
            else
            {
                Main.Log($"Failed to equip Beards: {string.Join(", ", target)}");
            }

            return false;
        }
    }
}
