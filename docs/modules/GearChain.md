# GearChain (`Managers/GearChain.cs`)

The chain layer above `GearOptimizer`: an **ordered list of objectives**, each claiming a budget of
the accessory slots that are still free when its turn comes. Pure data + name resolution, no game
reads.

**Why it exists:** `GearOptimizer` maximizes ONE scoring objective, and a product objective is
near-separable per slot — so every accessory slot converges on the same stat (an "Adventure" set is
all-Power accessories, with no respawn and no energy support). The reference optimizer does not have
that problem because it runs its priorities *sequentially*
(`external/gear-optimizer/src/sagas/optimize.worker.js:24-40`), each priority allowed at most
`maxslots` of the remaining free accessory slots. That sequencing — not the search inside a single
priority — is what produces mixed accessory sets.

## Types

- **`GearPriority`** — one step: `Objective` + `MaxAccessorySlots`. The port of the reference's
  `(factorslist[idx], maxslotslist[idx])` pair (`external/gear-optimizer/src/Optimizer.js:261-262`).
- **`GearChain.Unlimited` = `int.MaxValue`** — "all remaining accessory slots". This is the
  in-memory spelling; the profile JSON spells the same thing as `Slots: 0`/absent, and the two
  conventions meet in **exactly one place**, `GearBreakpoints.ParseSpec` (see AllocationProfiles.md).
- **`GearChain.MaxPriorities` = 5** — the reference caps its factor list at 5 (`state.factors`), and
  native adopts the same cap so a runaway chain cannot multiply the per-priority optimize cost
  without bound. Every consumer truncates with the **same** `Take(MaxPriorities)`:
  `GearBreakpoints.ParseSpec` (before filtering unresolved steps, so the validator's "only the first
  5 are used" message describes what actually happens), `GearOptimizer.Optimize`,
  `GearOptimizer.LeadObjective`, and the editor (`GearEditorPanel` disables **+ Add step** at 5, so
  a sixth row is never offered rather than silently dropped).

## The budget rule (never precompute it)

The per-priority accessory budget is computed **inside the chain run, from the slots ACTUALLY filled
so far** (`GearOptimizer.cs:588-590`), mirroring `count_accslots`
(`external/gear-optimizer/src/Optimizer.js:135`) being called from inside `compute_optimal`
(`:260`, the call at `:269`) against the current `base_layout`:

```
accslots = this.accslots - base_layout.counts['accessory'];
accslots = this.maxslots < accslots ? this.maxslots : accslots;
```

A priority **routinely fills fewer slots than it asked for**. The clearest case: a `Respawn` step
for a player who owns no Respawn accessory — `GearScorer.BaseValue("Respawn") == 0` and no candidate
carries the stat, so every candidate scores dead equal, the greedy fill finds no improvement and
stops immediately. Charging that step for the slot it never took would strand the slot **empty for
the whole run**.

This is why a helper that planned the whole split up front (`GearChain.SlotBudget`, written and
unit-tested during this feature's development) was **deleted by owner ruling**: any precomputed
split is a lie the runtime contradicts, and it strands slots. Do not reinvent it. The only budget
arithmetic that may exist is the two clamped `Math.Min`/`Math.Max` lines inside `RunChain`.

## Presets live HERE, not in `GearObjectives.Objectives`

`GearOptimizerDiagnostic` iterates `GearObjectives.Objectives` and optimizes every entry — it is the
regression harness used to validate the optimizer refactor (see GearOptimizerDiagnostic.md). Adding
chains to that list would change its output and destroy the baseline. So chains have their own
`GearChain.Presets` list.

Shipped presets, both leading with `Adventure` and both repeating the lead as an **unlimited tail**
step:

| Preset | Chain | Why |
|---|---|---|
| `Adventure + Respawn` | `Adventure(3) > Respawn(1) > Adventure(all)` | Unconditionally reserves one respawn accessory. The TopRespawn pin only fires when the loadout has NO respawn at all, so on merit-respawn gear it never engages. |
| `Adventure + Energy` | `Adventure(3) > Energy NGU(2) > Adventure(all)` | Keeps energy-support accessories instead of stacking pure Power. |

"Reserve N slots for a secondary stat, then fill the rest with the lead" needs **no new grammar** —
the same objective may appear more than once in a chain, and the tail step's `Unlimited` mops up
whatever is left.

## One namespace, and nothing in it is ever renamed

`GearChain.Resolve(name)` maps ONE name onto ONE chain: a **preset first**, then a single objective
as a one-element unlimited chain. Downstream therefore handles exactly one shape (a chain), never
two. Consequences:

- **A preset name and an objective name share one namespace.** A preset must never be given the
  name of an objective (it would shadow it); `GearChainTests.PresetNamesDoNotCollideWithObjectiveNames`
  is the guard.
- **Neither is ever renamed.** Both are persisted verbatim in `settings.json` and in profile JSON
  (`"Objective": "Adventure + Respawn"`), so a rename silently breaks saved configs. The rule
  already stated for objectives in GearObjectives.md now covers chain names too.
- **Refuse, don't guess.** An unrecognized name returns `null` and the caller declines to act; it is
  never mapped onto a near-match (the same rule `SpendPlanner` applies to perk names). Guessing here
  would equip gear optimized for something the user did not ask for.

## `Describe` — DECLARED data only

`Describe(chain)` renders `Adventure(3) > Respawn(1) > Adventure(all)`. It reads the chain and
nothing else, and that is load-bearing in two directions:

1. **`AdvisorApply.ApplyGearRefresh` uses the rendered string as the chain's identity**
   (`AdvisorApply.cs:951`): a changed chain is an objective switch and bypasses the 5 % re-equip bar.
   If the string ever embedded a live game read — an accessory-slot count, a pinned-item count —
   every pass could look like a switch and the advisor would re-equip constantly. It must change
   when a step changes (including the tail, which `chain[0].Objective.Name` would miss) and **never**
   because the player bought an accessory slot.
2. **It cannot contradict the optimizer.** A *planned* per-step figure would be an upper bound at
   best (see the budget rule) and pinned accessories are frozen before step 0 even runs, shifting
   every later step's real share. `GearEditorPanel.UpdateChainSummary` therefore prints `Describe`
   and never a computed split — and being game-read-free also keeps that label off the Unity thread
   on every keystroke of a slot numeric.

## Unity-free — keep it that way

`GearChain.cs` is linked into the net9.0 test assembly
(`tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj`, alongside `GearObjectives.cs`), which is what
makes `GearChainTests` possible without an NGU install. Do not add a `UnityEngine`, `Main`, or
`Character` reference to this file; put anything that needs the live game in `GearOptimizer` or
`GearBreakpoints`.

## Reference counterpart

- `external/gear-optimizer/src/sagas/optimize.worker.js:24-40` — the driver (`construct_base` then
  one `compute_optimal` per priority).
- `external/gear-optimizer/src/Optimizer.js:26` (`construct_base` — the pins), `:135`
  (`count_accslots`), `:260` (`compute_optimal`), `:261-262` (the per-priority factor + maxslots
  pair).
