using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using NGUAdvisor.Managers;
using SimpleJSON;
using System.Linq;

namespace NGUAdvisor.AllocationProfiles.Breakpoints
{
    // A gear breakpoint is either a manual item-ID list ("ID") or an optimizer objective ("Objective").
    // When an objective is set, the native gear optimizer computes the best loadout live (route C3) instead
    // of using a fixed ID list - so gear stays optimal as it improves. Optimization runs in PerformSwap,
    // which BaseBreakpoints only invokes when the active breakpoint changes (naturally throttled).
    public class GearSpec
    {
        public int[] Ids;
        public string Objective;
        public bool ForceRespawn;
    }

    // ActiveObjective/ActiveForceRespawn mirror the objective of the last-applied gear breakpoint
    // (null when the active breakpoint is a manual ID list) so AdvisorApply can periodically
    // re-optimize the same objective as drops improve (Phase C gear auto-refresh).
    public class GearBreakpoints : BaseBreakpoints<GearSpec>
    {
        public GearBreakpoints() : base() { }

        public GearBreakpoints(JSONNode bps) : base(bps, ParseSpec) { }

        private static GearSpec ParseSpec(JSONNode bp)
        {
            var spec = new GearSpec();
            var obj = bp["Objective"];
            if (obj != null && !string.IsNullOrEmpty(obj.Value))
                spec.Objective = obj.Value;
            var resp = bp["TopRespawn"];
            if (resp != null)
                spec.ForceRespawn = resp.AsBool;
            var id = bp["ID"];
            if (id != null && id.IsArray)
                spec.Ids = id.AsArray.Children.Select(x => x.AsInt).ToArray();
            return spec;
        }

        public static string ActiveObjective { get; private set; }
        public static bool ActiveForceRespawn { get; private set; }

        protected override bool PerformSwap(Breakpoint bp)
        {
            if (!LockManager.CanSwap())
                return false;

            string objectiveName = bp.priorities.Objective;
            bool forceRespawn = bp.priorities.ForceRespawn;

            // Smart default: if this breakpoint has no explicit objective and isn't itself challenge-tagged,
            // but a challenge is active, optimize for the built-in objective for that challenge (if any).
            if (string.IsNullOrEmpty(objectiveName) && string.IsNullOrEmpty(bp.challenge))
            {
                var ch = Managers.ChallengeDetector.Current();
                if (ch != null)
                {
                    var def = Managers.ChallengeDetector.DefaultGear(ch);
                    if (def != null) { objectiveName = def.Objective; forceRespawn = def.ForceRespawn; }
                }
            }

            int[] ids;
            if (!string.IsNullOrEmpty(objectiveName))
            {
                var objective = GearOptimizer.FindObjective(objectiveName);
                if (objective == null)
                {
                    Main.LogDebug($"Gear breakpoint objective '{objectiveName}' not recognized.");
                    return false;
                }
                ids = GearOptimizer.OptimizeIds(objective, forceRespawn);
                if (ids.Length == 0)
                    return false;
                Main.Log($"Optimized gear for '{objective.Name}'{(forceRespawn ? " (+top respawn)" : "")}: {ids.Length} items.");
                ActiveObjective = objectiveName;
                ActiveForceRespawn = forceRespawn;
            }
            else
            {
                ids = bp.priorities.Ids ?? new int[0];
                ActiveObjective = null;
                ActiveForceRespawn = false;
            }

            current = bp;
            LoadoutManager.ChangeGear(ids);
            Main.InventoryController.assignCurrentEquipToLoadout(0);

            return true;
        }
    }
}
