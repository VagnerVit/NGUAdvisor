using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // One step of a gear priority chain: an objective plus how many of the still-free accessory
    // slots it is allowed to claim. Ported from the reference optimizer's (factor, maxslots) pair
    // -- external/gear-optimizer/src/Optimizer.js:262.
    public class GearPriority
    {
        public GearObjectives.Objective Objective;
        public int MaxAccessorySlots = GearChain.Unlimited;
    }

    // The chain layer: ordered objectives, each with an accessory budget.
    //
    // Why this exists: GearOptimizer scores ONE objective, so it fills every accessory slot with the
    // same stat (all-Power accessories under "Adventure"). The reference optimizer instead runs its
    // priorities in sequence -- sagas/optimize.worker.js:31 -- each claiming at most maxslots of the
    // remaining free accessory slots, which is what produces mixed sets.
    //
    // Presets live HERE and not in GearObjectives.Objectives on purpose: GearOptimizerDiagnostic
    // iterates that list and optimizes every entry, and it is the regression harness for the
    // optimizer refactor. Adding chains there would change its output.
    //
    // Unity-free (linked into tests) -- keep it that way.
    public static class GearChain
    {
        public const int Unlimited = int.MaxValue;

        // The reference caps its priority list at 5 (state.factors); native adopts the same cap so a
        // runaway chain cannot multiply the per-priority optimize cost without bound.
        public const int MaxPriorities = 5;

        public class Preset
        {
            public readonly string Name;
            public readonly IReadOnlyList<GearPriority> Priorities;
            public Preset(string name, IReadOnlyList<GearPriority> priorities) { Name = name; Priorities = priorities; }
        }

        public static GearObjectives.Objective FindObjective(string name)
            => GearObjectives.Objectives.FirstOrDefault(o =>
                string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

        private static GearPriority Step(string objective, int slots)
            => new GearPriority { Objective = FindObjective(objective), MaxAccessorySlots = slots };

        // Named chains, selectable exactly like an objective. Both repeat their lead objective as the
        // final unlimited step: reserve a couple of slots for the secondary stat, then fill whatever
        // is left with the lead again. Expressing a reserve this way needs no new grammar -- the same
        // objective may appear more than once in a chain.
        public static readonly IReadOnlyList<Preset> Presets = new List<Preset>
        {
            // Adventure that always keeps a respawn accessory. The TopRespawn pin only fires when the
            // loadout has NO respawn at all, so on merit-respawn gear it never engages; this reserves
            // a slot unconditionally.
            new Preset("Adventure + Respawn", new List<GearPriority>
            {
                Step("Adventure", 3),
                Step("Respawn", 1),
                Step("Adventure", Unlimited),
            }),
            // Adventure that keeps energy-support accessories instead of stacking pure Power.
            new Preset("Adventure + Energy", new List<GearPriority>
            {
                Step("Adventure", 3),
                Step("Energy NGU", 2),
                Step("Adventure", Unlimited),
            }),
        };

        public static Preset FindPreset(string name)
            => Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        // One name -> one chain: a named preset first, then a single objective as a one-element
        // unlimited chain, so every caller downstream handles exactly one shape.
        //
        // Refuse, don't guess: an unrecognized name returns null and the caller declines to act. It is
        // never mapped onto a near-match (same rule SpendPlanner applies to perk names) -- guessing here
        // would silently equip gear optimized for something the user did not ask for.
        public static List<GearPriority> Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var preset = FindPreset(name);
            if (preset != null) return preset.Priorities.ToList();
            var objective = FindObjective(name);
            return objective == null
                ? null
                : new List<GearPriority> { new GearPriority { Objective = objective, MaxAccessorySlots = Unlimited } };
        }

        // "Adventure(3) > Respawn(1) > Adventure(all)" -- the DECLARED per-step budget, never a computed
        // one. Two reasons it is declared and not planned: a planned figure ignores pinned accessories
        // (counting them needs the optimizer's item pools) and would print numbers debug.log's reader can
        // catch the optimizer contradicting; and being free of game reads makes this string a stable
        // identity for the chain -- it changes when any step changes, including the tail, and never
        // because an accessory slot was bought.
        public static string Describe(IReadOnlyList<GearPriority> chain)
        {
            if (chain == null || chain.Count == 0) return "(no chain)";
            var parts = new List<string>(chain.Count);
            foreach (var step in chain)
            {
                if (step == null || step.Objective == null) continue;
                var slots = step.MaxAccessorySlots >= Unlimited ? "all" : step.MaxAccessorySlots.ToString();
                parts.Add($"{step.Objective.Name}({slots})");
            }
            return parts.Count == 0 ? "(no chain)" : string.Join(" > ", parts.ToArray());
        }
    }
}
