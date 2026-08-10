# Gear priority chain + pinned items (and the ITOPOD attack-mode fix)

Date: 2026-08-10
Status: approved, not yet implemented

Two deliverables in one branch. They are unrelated in code but were reported together and are both
"the advisor does not honor what I chose".

- **A** — `Settings.ITOPODCombatMode` silently resets to Idle on every settings load.
- **B** — the native gear optimizer can express only ONE objective, so it fills every accessory
  slot with the same stat, and there is no way to say "always wear this item".

---

## A. ITOPOD attack mode resets to Idle

### Defect

`SavedSettings.cs:447`

```csharp
AssignValue(ref _itopodCombatMode, other?.ITOPODCombatMode, (mode) => mode >= 0 && mode <= 1);
```

The combo in `AdventurePanel.cs:364` offers four modes (`Idle=0, Snipe=1, Defensive=2, Offensive=3`).
`AssignValue` (`SavedSettings.cs:290`) does **not** fall through to the stored value when the
predicate fails — it assigns `defaultValue`, i.e. `0` = Idle:

```csharp
if (newSetting.HasValue && predicate(newSetting.Value))
    setting = newSetting.Value;
else
    setting = defaultValue;
```

So Defensive/Offensive persist correctly to `settings.json` (the property setter calls
`SaveSettings()`), and are then discarded on the next load. The user sees the dropdown snap back.

### Fix

Widen the predicate to `mode >= 0 && mode <= 4`, matching every sibling combat-mode field
(`CombatMode` :440, `QuestCombatMode` :475, `TitanCombatMode` :436).

`ITOPODOptimizeMode` (:450, `<= 3` for four items) is already correct and is not touched.

### Verification

Build; set ITOPOD combat to Offensive; force a settings reload (the `FileSystemWatcher` path) and
confirm the dropdown holds. There is no test project coverage for `SavedSettings` (it is not one of
the Unity-free linked files), so this is a manual check.

---

## B. Gear: priority chain + pinned items

### Why the current behavior is not a bug

`GearBreakpoints.PerformSwap` (`AllocationProfiles/Breakpoints/GearBreakpoints.cs:51`) resolves ONE
objective name per breakpoint and hands it to `GearOptimizer.Optimize`. With the `Adventure`
objective (`Power × Toughness`) the optimizer correctly packs every free accessory slot with the
highest-Power accessories it owns — Generic Paperweight, Ring of Might. That is the objective doing
exactly what it says.

What is missing is the reference optimizer's two features that produce mixed sets:

1. **Priority chain with per-priority accessory budgets** — `gear-optimizer-comparison.md:38` gap 2.
2. **Locked (pinned) items** — `gear-optimizer-comparison.md:41` gap 3. This is what "Ring of Greed
   should be in every inventory" asks for. Today the only pin is `forceTopRespawn`, which fires
   *only* when the merit loadout carries no respawn at all.

### Reference semantics (what we are porting)

Driver — `external/gear-optimizer/src/sagas/optimize.worker.js:29`:

```js
let base_layout = optimizer.construct_base(state.locked, state.equip);
for (let idx = 0; idx < state.factors.length; idx++) {
    base_layout = optimizer.compute_optimal(base_layout, idx);
}
```

Budget — `Optimizer.js:135 count_accslots`:

```js
let accslots = this.accslots - base_layout.counts['accessory'];
accslots = this.maxslots < accslots ? this.maxslots : accslots;
```

So, verified against the source:

- Pinned items occupy their slots **before** any priority runs.
- Each priority optimizes only slots still free, and takes at most `maxslots[idx]` of the remaining
  **free** accessory slots.
- Priority 0 therefore decides all main slots (nothing else is locked); later priorities get
  accessories only. This is what yields "Power weapon + armour, 3 Power accessories, 2 Energy Power,
  1 Respawn".

### Native design

Scope decision (agreed): port the **chain semantics** — ordering, per-priority accessory budget, slot
locking between priorities — and keep the native search *inside* each priority (coordinate ascent +
greedy fill + local swap). Do NOT port `pareto`/`dominates`/cartesian layout expansion: native runs
on the Unity main thread and has a time budget the browser worker does not.

#### Structural prerequisite

`GearOptimizer.Optimize` is today a single ~285-line method whose closures (`statNames`,
`exponents`, `constVals`, `idToVec`, `scratch`, `ScoreOf`, `PickSlot`, `MainAscent`,
`AccessoryOptimize`) all capture ONE objective. A chain needs per-priority scoring state, so the
scoring closure set is extracted into a per-objective context built over the shared, once-built
candidate pools. `BuildPools` and the `Result` shape are unchanged.

**The `ScoreOf` equivalence argument must survive the extraction.** The long comment at
`GearOptimizer.cs:194–209` documents why the dense-array rewrite is an exact reproduction of
`GearScorer.GetRawVals + ScoreVals` (missing stat = 0, NaN folded at build, literal offhand rule,
cube+base folded into a constant). That comment moves with the code; the arithmetic does not change.

#### New types

```csharp
public class GearPriority
{
    public GearObjectives.Objective Objective;
    public int MaxAccessorySlots;   // int.MaxValue == unlimited
}
```

#### Algorithm

1. Build pools once (`BuildPools`), shared by every priority.
2. **Pins**: for each configured pinned item id present in the pools, place it in its slot
   (accessories append, in configured order, capped at `accessorySpaces()`). Pinned main slots and
   pinned accessory indices are frozen for the whole run.
3. **Priority 0**: build the scoring context for its objective; run the existing alternation
   (`MainAscent` ↔ `AccessoryOptimize`, ≤5 rounds) with `MainAscent` skipping frozen main slots and
   `AccessoryOptimize` capped at `frozenAccCount + min(MaxAccessorySlots, freeAccSlots)`.
4. Freeze all main slots and every accessory index filled so far.
5. **Priority k ≥ 1**: build a new scoring context for `objective[k]`; run accessory-only fill +
   local swap over indices ≥ frozen count, capped as in step 3 against the remaining free slots.
   Frozen items are scored via the normal `AddId` path — they contribute to every candidate's score,
   which is what makes "best marginal accessory *given what is already worn*" correct.
6. **TopRespawn pin**: unchanged in meaning, applied after the whole chain and only when neither
   pins nor chain produced any respawn. Its candidate loop re-runs the full chain per candidate
   (same as it re-runs `RunOptimize` today).

Accessory uniqueness (`accSet`) spans the whole chain — it is a game rule (one copy per accessory
id), not a per-priority constraint.

#### `Result.Score` under a chain

`AdvisorApply` (`AdvisorApply.cs:939`) compares `GearOptimizer.CurrentScore(obj)` against
`Optimize(...).Score` behind a 5 % re-equip bar. A chain has no single score, so **`Result.Score` is
priority 0's objective score**, and `CurrentScore` is called with priority 0's objective. Both sides
of the comparison then measure the same thing, and the bar keeps its meaning. Recorded here because
it is the one place where "chain" leaks out of the optimizer.

### Configuration — both mechanisms

**1. Named chain presets** in `GearObjectives`. Selected exactly like today's objectives, so no
profile-format change and no UI work is needed to use them. Chain preset names live in the same
namespace as objective names (`FindObjective` resolves either), because that name is what profiles
and settings persist — and per `GearObjectives.md`, a name is never renamed once shipped.

**2. Explicit chain in the profile.** `GearSpec` gains an optional array; `Objective` stays for
backward compatibility and `Priorities` wins when both are present:

```json
{
  "Priorities": [
    { "Objective": "Adventure",  "Slots": 3 },
    { "Objective": "Energy NGU", "Slots": 2 },
    { "Objective": "Respawn",    "Slots": 1 }
  ]
}
```

`Slots` omitted = unlimited. Parsed in `GearBreakpoints.ParseSpec`, validated by `ProfileValidator`
(objective name resolves; `Slots` ≥ 0), edited in `GearEditorPanel`.

**3. Pinned items** are **global** (`SavedSettings`), not per-breakpoint: "must have in every
inventory" is one list, not one per breakpoint. Applied to every `Optimize` call, including the
mode-resolution entry points (`ResolveModeGear`, `ResolveTitanGear`, `ResolveGoldGear`).

### Backward compatibility

`Optimize(objective, forceTopRespawn)` keeps its signature and becomes "empty pins + a one-element
chain with unlimited slots". With no pins configured and no `Priorities` in a profile, every existing
profile produces byte-identical loadouts. This is the acceptance criterion for the refactor.

### Risks

- **Pins can starve a chain.** Pinning more accessories than `accessorySpaces()` leaves priority 0
  nothing to work with. Pins are truncated to the available slots and the drop is logged — silent
  truncation would read as "the optimizer ignored my pin".
- **A pinned item the user no longer owns** must be skipped, not crash the pass; pins resolve against
  the live pools, and unresolved ids are logged once.
- **Cost.** Each priority builds its own `idToVec` (pool size × stat count doubles) and runs its own
  sweeps. With a 3-priority chain that is roughly 3× today's accessory work. `Optimize` already runs
  behind AdvisorApply's throttle, but the chain length should stay small; the reference caps at 5 and
  native adopts the same cap.

### Verification

`GearOptimizer` has Unity dependencies and cannot be unit-tested (the test project links only
Unity-free files). So:

1. `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release` — clean.
2. **Regression**: with no pins and no `Priorities`, `GearOptimizerDiagnostic.Run()` output must match
   the pre-change run item-for-item on the same save. This is the backward-compatibility criterion
   above, made checkable.
3. **New behavior**: run a 3-priority chain and compare against the web Gear Optimizer configured
   with the same factors + maxslots over the same save (F3 quicksave writes `NGUSave.json`, which the
   site loads) — the workflow `gear-optimizer-comparison.md:95` already prescribes.
4. `debug.log` shows zero `UI AUDIT` lines after the `GearEditorPanel` change.

### Docs to update when done

- `docs/modules/GearOptimizer.md` — algorithm section, new entry points, the `Result.Score` rule.
- `docs/modules/GearObjectives.md` — chain presets, and the "never rename a name" rule now covering
  chain names.
- `docs/modules/gear-optimizer-comparison.md` — close gaps 2 and 3; state what is still deliberately
  not ported (Pareto machinery, hard caps, alternatives detection).
- `docs/modules/AllocationProfiles.md` + `README.md` — the `Priorities` profile grammar.

---

## Out of scope

The AP purchase advisor, the PP module and the Advanced Training module are separate deliverables,
each getting its own spec. Source material for them is already retrieved and verified:

- AP Tier List (OJ of Steel, build 1.200) — Tiers 0–7.
- PP Purchase Proposal: Pre-Evil — Stages 1–5, QoL and whale tiers.
- AT Calculator (iboj88) — both sheets with formulas. Note: the sheet calls a custom Apps Script
  function `atcalc(targetLevel, modifier, currentLevel)` whose body is not in the export; it must be
  derived from the decompiled `AdvancedTrainingController` rather than guessed. `AtHourPlanner`
  already carries the game-truth AT math (`dL/dt = R/(L+1)`, multiplier `1 + 0.1·L^0.4`).
