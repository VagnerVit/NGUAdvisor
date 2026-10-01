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

        // Farm sets want the hardest-hitting weapon regardless of what the lead objective scores:
        // kills per second is what the loot stat multiplies, and an NGU lead picks a weapon for its
        // energy specs. The chain grammar cannot say "this step owns only the weapon" -- priority 0
        // owns every main slot it wants -- so this rides in as a pin instead, exactly like forceTopRespawn.
        // Read off ANY step (GearOptimizer.WantsTopPowerWeapon); the lead is where it is written.
        public bool PinTopPowerWeapon;
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

        private static GearPriority Step(string objective, int slots, bool pinTopPowerWeapon = false)
            => new GearPriority
            {
                Objective = FindObjective(objective),
                MaxAccessorySlots = slots,
                PinTopPowerWeapon = pinTopPowerWeapon,
            };

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
            // Farm sets: max drop chance on the accessories, real stats everywhere else.
            //
            // These are the ONLY shape that works for a loot stat. No main-slot item in the game
            // carries Drop Chance -- the diagnostic prints "W:- H:- C:- L:- B:-" under a pure
            // "Drop Chance" objective for exactly that reason -- so leading with it leaves the lead
            // priority scoring every helmet, chest and weapon dead equal, and the main slots land
            // wherever the ascent happened to start. The partner objective leads (owning the main
            // slots at budget 0, claiming no accessory), Drop Chance then takes all of them.
            new Preset("Drop Chance + Adventure", new List<GearPriority>
            {
                Step("Adventure", 0, pinTopPowerWeapon: true),
                Step("Drop Chance", Unlimited),
            }),
            new Preset("Drop Chance + NGUs", new List<GearPriority>
            {
                Step("NGUs", 0, pinTopPowerWeapon: true),
                Step("Drop Chance", Unlimited),
            }),
        };

        // ITOPOD floor push (user request): the floor a push reaches is the one the rotation still
        // one-shots, and that solve reads totalAdvAttack — so everything goes to Power. Move Cooldown
        // gets ONE accessory: it brings the ultimate and the buffs round more often, and the push
        // target is priced with the whole buff stack. Past the first item it is weaker than Power.
        //
        // Not in Presets: the ITOPOD floor mode "Push" equips it, so it is not offered as a gear
        // source. FindPreset still resolves it for profiles that already name it.
        public static readonly Preset ItopodPush = new Preset("ITOPOD Push", new List<GearPriority>
        {
            Step("Power", 0),
            Step("Move Cooldown", 1),
            Step("Power", Unlimited),
        });

        // Kill-safe loot gear: the MAIN slots go to Adventure, the ACCESSORIES to the loot stat.
        //
        // The autokill thresholds are live totalAdvAttack/totalAdvDefense reads (ZoneHelpers.
        // AutokillAvailable), so a set that spends Power/Toughness on loot turns an auto-kill into a
        // real fight — the exact trade GoldTargetLosingAutokill() exists to catch after a gold swap.
        // Keeping the main slots on Adventure buys the loot stat out of the accessories only, where
        // it costs the AK margin least.
        //
        // Budget 0 on the lead step is how "main slots only" is spelled: the first priority that WANTS
        // the main slots takes them regardless of its accessory budget (GearOptimizer.RunChain), so a 0
        // there claims no accessory and leaves every one of them to the next step.
        //
        // Returns null when either objective is unknown — refuse, don't guess, same as Resolve.
        public static List<GearPriority> LootChain(string lootObjective)
        {
            var loot = FindObjective(lootObjective);
            var adventure = FindObjective("Adventure");
            if (loot == null || adventure == null || loot == adventure) return null;
            return new List<GearPriority>
            {
                new GearPriority { Objective = adventure, MaxAccessorySlots = 0, PinTopPowerWeapon = true },
                new GearPriority { Objective = loot, MaxAccessorySlots = Unlimited },
            };
        }

        public static Preset FindPreset(string name)
            => Presets.Concat(new[] { ItopodPush })
                      .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

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

        // The chain's objectives in step order, through the same Take(MaxPriorities) + non-null filter
        // GearOptimizer applies when it builds its step list -- so "step k" means the same entry everywhere.
        public static List<GearObjectives.Objective> StepObjectives(IReadOnlyList<GearPriority> chain)
            => chain == null
                ? new List<GearObjectives.Objective>()
                : chain.Take(MaxPriorities)
                       .Where(p => p != null && p.Objective != null)
                       .Select(p => p.Objective)
                       .ToList();

        // Lexicographic over the steps, like the chain itself: the first step whose best/worn ratio
        // leaves [1/bar, bar] decides -- above it the best set improves, below it the worn set is
        // better. -1 = every step inside the bar. A worn score of 0 under a positive best (a base-0 stat
        // like Respawn that nothing worn carries) is an improvement, not a division by zero.
        public static int DecidingStep(IReadOnlyList<double> worn, IReadOnlyList<double> best, double bar,
                                       out bool improves)
        {
            improves = false;
            int steps = Math.Min(worn.Count, best.Count);
            for (int k = 0; k < steps; k++)
            {
                if (worn[k] <= 0)
                {
                    if (best[k] <= 0) continue;
                    improves = true;
                    return k;
                }
                if (best[k] >= worn[k] * bar) { improves = true; return k; }
                if (best[k] * bar <= worn[k]) return k;
            }
            return -1;
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
                // The weapon pin changes which loadout the chain produces, so it belongs in the chain's
                // identity: AdvisorApply treats a changed Describe() as an objective switch.
                parts.Add($"{step.Objective.Name}({slots}){(step.PinTopPowerWeapon ? "+PowerWeapon" : "")}");
            }
            return parts.Count == 0 ? "(no chain)" : string.Join(" > ", parts.ToArray());
        }
    }
}
