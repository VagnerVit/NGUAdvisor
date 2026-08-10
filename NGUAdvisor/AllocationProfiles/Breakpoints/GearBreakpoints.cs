using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using NGUAdvisor.Managers;
using SimpleJSON;
using System.Collections.Generic;
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
        // An explicit priority chain ("Priorities"). When non-empty it supersedes Objective.
        public List<GearPriority> Priorities;
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
            var chain = bp["Priorities"];
            if (chain != null && chain.IsArray)
            {
                spec.Priorities = new List<GearPriority>();
                // Truncate BEFORE filtering, exactly as GearOptimizer.Optimize does (Take then Where), so
                // ProfileValidator's "only the first 5 are used" message describes what actually happens.
                foreach (var step in chain.AsArray.Children.Take(GearChain.MaxPriorities))
                {
                    var name = step["Objective"]?.Value ?? "";
                    var objective = GearChain.FindObjective(name);
                    // Refuse, don't guess: an unresolved name is SKIPPED and logged, never mapped onto a
                    // near-match. ProfileValidator surfaces the same names as warnings in the editor.
                    if (objective == null)
                    {
                        Main.LogDebug($"Gear priority objective '{name}' not recognized; step skipped.");
                        continue;
                    }
                    // Profile-side convention: Slots == 0 (or absent) means "all remaining accessory
                    // slots", which GearPriority spells as GearChain.Unlimited. THIS is the only place the
                    // two conventions meet.
                    //
                    // A NEGATIVE Slots claims nothing (0), agreeing with the optimizer's own
                    // Math.Max(0, MaxAccessorySlots) clamp (GearOptimizer.cs:585) and with the
                    // ProfileValidator warning the user is shown. Mapping it to
                    // Unlimited instead would make a typo'd "-1" swallow every accessory slot and starve
                    // the rest of the chain -- the loudest possible failure from the quietest typo.
                    var slots = step["Slots"]?.AsInt ?? 0;
                    spec.Priorities.Add(new GearPriority
                    {
                        Objective = objective,
                        MaxAccessorySlots = slots == 0 ? GearChain.Unlimited : System.Math.Max(0, slots),
                    });
                }
            }
            return spec;
        }

        public static string ActiveObjective { get; private set; }
        public static bool ActiveForceRespawn { get; private set; }
        // The chain the last-applied gear breakpoint resolved to (null when it was a manual ID list).
        // Always a chain, even for a plain objective, so AdvisorApply's refresh has one shape to handle.
        public static IReadOnlyList<GearPriority> ActiveChain { get; private set; }
        // The NAME ActiveChain was resolved FROM -- a chain preset or a plain objective -- or null when the
        // breakpoint carried an explicit Priorities list, which no name can express.
        //
        // This is NOT ActiveObjective. That one is the chain's LEAD objective, so the preset
        // "Adventure + Respawn" reports "Adventure" and is indistinguishable from the plain objective of
        // the same name: a challenge/segment override asking for "Adventure" would silently be handed the
        // profile's whole three-step chain. AdvisorApply matches on this instead.
        public static string ActiveChainSource { get; private set; }

        // Profile reload / rebirth: the Active* mirror describes a breakpoint that is no longer applied, and
        // a stale ActiveChain outranks the name AdvisorApply resolves for itself.
        public override void Reset()
        {
            base.Reset();
            ActiveChain = null;
            ActiveChainSource = null;
            ActiveObjective = null;
            ActiveForceRespawn = false;
        }

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

            // Resolution order: explicit Priorities -> a named chain preset -> a single objective ->
            // (the challenge default above already folded into objectiveName) -> the manual ID list.
            List<GearPriority> chain = null;
            string chainSource = null;
            if (bp.priorities.Priorities != null && bp.priorities.Priorities.Count > 0)
                chain = bp.priorities.Priorities;
            else if (!string.IsNullOrEmpty(objectiveName))
            {
                chain = GearChain.Resolve(objectiveName);
                if (chain == null)
                {
                    Main.LogDebug($"Gear breakpoint objective '{objectiveName}' not recognized.");
                    return false;
                }
                chainSource = objectiveName;
            }

            int[] ids;
            if (chain != null)
            {
                ids = GearOptimizer.OptimizeIds(chain, null, forceRespawn);
                if (ids.Length == 0)
                    return false;
                Main.Log($"Optimized gear for '{GearChain.Describe(chain)}'{(forceRespawn ? " (+top respawn)" : "")}: {ids.Length} items.");
                // Copy: ActiveChain is a static and must never alias the live profile structure.
                ActiveChain = chain.ToList();
                ActiveChainSource = chainSource;
                ActiveObjective = chain[0].Objective.Name;
                ActiveForceRespawn = forceRespawn;
            }
            else
            {
                ids = bp.priorities.Ids ?? new int[0];
                ActiveChain = null;
                ActiveChainSource = null;
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
