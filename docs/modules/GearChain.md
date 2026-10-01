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
  `GearChain.StepObjectives` (behind `GearOptimizer.LeadObjective` and AdvisorApply's per-step bar), and the editor (`GearEditorPanel` disables **+ Add step** at 5, so
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
| `Drop Chance + Adventure` | `Adventure(0)+PowerWeapon > Drop Chance(all)` | Farm set: every accessory on drop chance, Power/Toughness everywhere else. |
| `Drop Chance + NGUs` | `NGUs(0)+PowerWeapon > Drop Chance(all)` | Same, with the NGU stats in the main slots instead. |
| `ITOPOD Push` | `Power(0) > Move Cooldown(1) > Power(all)` | Floor push: the push target is the floor the buffed rotation one-shots, which reads `totalAdvAttack`, so Power owns every slot but ONE Move Cooldown accessory (brings the ultimate and buffs round more often; past the first item it is weaker than Power). **Not in `Presets`** (`GearChain.ItopodPush`): the ITOPOD floor mode Push equips it, so the gear editor does not offer it; `FindPreset` still resolves it for profiles that name it. |

**The two farm presets are named loot-first but LEAD with the partner, and that is not a typo.** No
main-slot item in the game carries Drop Chance — under a pure `Drop Chance` objective the diagnostic
prints `W:- H:- C:- L:- B:-` for exactly that reason. Leading with the loot stat therefore scores
every helmet, chest and weapon dead equal and the main slots land wherever the coordinate ascent
happened to start. The partner leads at budget **0** (owning the main slots, claiming no accessory,
see `LootChain` below), and Drop Chance then takes all of them. To trade some drop chance back for
the partner's stats, insert a middle step — `Adventure(0) > Drop Chance(3) > Adventure(all)` — rather
than reordering the lead.

## `LootChain(lootObjective)` — the kill-safe loot set

Not a preset (it is built from a name the user chose, so it cannot be a fixed list): returns
`Adventure(0) > <loot>(all)` — **main slots Adventure, every accessory the loot stat**.

The reason it is not simply `Drop Chance(all)`: the autokill thresholds are live
`totalAdvAttack`/`totalAdvDefense` reads (`ZoneHelpers.AutokillAvailable`), so a set that spends
Power/Toughness on loot can turn an auto-kill into a real fight — the same trade
`GoldTargetLosingAutokill()` exists to catch *after* a gold swap. Buying the loot stat out of the
accessories only costs the AK margin least.

**`MaxAccessorySlots = 0` on the lead step is how "main slots only" is spelled.** Priority 0 owns
the main slots regardless of its accessory budget (`GearOptimizer.RunChain`), so a 0 there claims no
accessory and leaves every one of them to the next step. `GearOptimizer.ResolveTitanGear` uses this
for AK-trivial spawns; a real fight gets `Adventure(k) > Drop Chance(all)` with `k` as small as the
fight's bar allows (`GearOptimizer.KillSetWithDropChance`).

Returns `null` for an unknown objective, and also when the loot objective IS `Adventure` (the chain
would degenerate to `Adventure(0) > Adventure(all)`) — refuse, don't guess, same as `Resolve`.

"Reserve N slots for a secondary stat, then fill the rest with the lead" needs **no new grammar** —
the same objective may appear more than once in a chain, and the tail step's `Unlimited` mops up
whatever is left.

## `PinTopPowerWeapon` — the weapon the lead objective would not have picked

A farm set wants the hardest-hitting weapon whatever the lead scores: kills per second is what the
loot stat multiplies, and an `NGUs` lead picks a weapon for its energy specs (measured: `Power` and
`Adventure` both want `The Fists of Flubber`, `NGUs` wants `A Giant Bazooka`). The grammar cannot say
"this step owns only the weapon" — the step that claims the main slots claims **every** one of them
(GearOptimizer.md: the first priority that wants them) — so the flag rides in as a
pin instead, the same shape `forceTopRespawn` already uses.

- Set on a step (the lead, by convention); `GearOptimizer` reads it off **any** step in the chain.
- Also reachable **per gear breakpoint**, as profile `"TopPowerWeapon": true` / the editor's "Always
  equip the highest-Power weapon" checkbox. `GearBreakpoints.PerformSwap` rebuilds the resolved chain
  with a fresh lead step carrying the flag — never by assigning into the chain it resolved, which may
  be one of the `static readonly` presets below.
- Resolved ONCE per `Optimize` call, before the chain runs: the single highest-`Power` weapon in the
  pools. It does not depend on the chain's progress, and `forceTopRespawn` re-runs the whole chain per
  candidate — re-scanning the weapon pool inside that loop would be pure waste.
- **The user's own pins are placed first and therefore win the main hand.** An explicit "always wear
  this" outranks a preset's convenience pin.
- `Describe` renders it as `Adventure(0)+PowerWeapon`, and that is load-bearing: the pin changes which
  loadout the chain produces, so it has to change the chain's identity, or `AdvisorApply` would not see
  a switch when a preset gains or loses it.

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
   (`AdvisorApply.cs:951`): a changed chain is an objective switch and bypasses the re-equip bar.
   If the string ever embedded a live game read — an accessory-slot count, a pinned-item count —
   every pass could look like a switch and the advisor would re-equip constantly. It must change
   when a step changes (including the tail, which `chain[0].Objective.Name` would miss) and **never**
   because the player bought an accessory slot.
2. **It cannot contradict the optimizer.** A *planned* per-step figure would be an upper bound at
   best (see the budget rule) and pinned accessories are frozen before step 0 even runs, shifting
   every later step's real share. `GearEditorPanel.UpdateChainSummary` therefore prints `Describe`
   and never a computed split — and being game-read-free also keeps that label off the Unity thread
   on every keystroke of a slot numeric.

## `DecidingStep` — the re-equip verdict, per step

`DecidingStep(worn, best, bar, out improves)`: lexicographic over the steps, the first step whose
best/worn ratio leaves `[1/bar, bar]` decides; `-1` = every step inside the bar. A worn 0 under a
positive best (a base-0 stat nothing worn carries) is an improvement. AdvisorApply feeds it one score
pair per `StepObjectives` entry (AdvisorApply.md rule 2). Tested in `GearChainTests`.

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
