# AtMath + AtPanel (`Managers/AtMath.cs`, `AtPanel.cs`)

"When does that AT level land, where is the blitz-boost ceiling right now, and how much cap would it
take to blitz-boost for an hour?" — the Combat > ADVANCED TRAINING readout, a port of iboj88's
community AT Calculator. `AtMath` is the Unity-free arithmetic (linked into
`tests/NGUAdvisor.Tests`); `AtPanel` is the view. Same pure/live split as `PpEta` + `PpPanel`.

> **Nothing in this module has been run against the game.** Every runtime check — live levels, live
> `getProgressPerTick`, the gate lines actually firing, the `UI AUDIT` pass, where the panel sits in
> the Combat column — was deferred by the owner. What is proven: the project compiles against the
> game's `Assembly-CSharp.dll` (so every member named below exists with the stated type), 11 unit
> tests pin `AtMath`, and the Release build is clean at 0 warnings. Treat a first live run as the
> missing verification step, not as a regression hunt.

## The derivation IS the module

This is the one doc in `docs/modules/` whose subject is a piece of reasoning rather than a piece of
policy. The source spreadsheet does its AT arithmetic inside three Apps Script functions — `atcalc`,
`bb`, `bbtrue` — **whose bodies are in no export of that sheet**. Both the current sheet and its "Old
Version" tab call them, and the old tab renders `#NAME?`. There was nothing to copy.

So the formulas were reconstructed from the decompiled `AdvancedTrainingController`
(`reference/decomp-full/AdvancedTrainingController.cs`, the same source `AtHourPlanner` cites):

```
getDivisor()      = baseTime * (level + 1)
progressPerTick() = (energy/50) * sqrt(totalEnergyPower()) * totalAdvancedTrainingSpeedBonus()
                    / getDivisor()
```

A level lands when `barProgress >= 1`, and the game ticks **50/s**. Write

```
M = (energy/50) * sqrt(epow) * atSpeedBonus        so  progressPerTick = M / (baseTime * (L+1))
r = 50 * progressPerTick * (L+1) = 50 * M / baseTime          <-- CONSTANT in L
```

`r` being free of `L` is the whole trick: `dL/dt = r/(L+1)` then integrates exactly, and the panel
needs no simulation. That gives the four functions in `AtMath.cs`:

| function | closed form | the sheet's name for it |
|---|---|---|
| `LevelAt(l0, r, t)` | `sqrt((l0+1)² + 2rt) − 1` | — (`AtHourPlanner` uses the same form) |
| `SecondsToLevel(l0, l1, r)` | `((l1+1)² − (l0+1)²) / 2r` | `atcalc` |
| `StatMultiplier(level)` | `1 + 0.1·L^0.4` | — (slot 1 = attack, 0 = defense) |
| `LevelForMultiplier(m)` | `((m − 1)/0.1)^2.5` — the exact inverse of `StatMultiplier` | — (the goal threshold) |
| `BbCeiling(m, baseTime)` | `m / baseTime − 1` | `bb` / `bbtrue` |
| `SecondsToTarget(level, target, ppt, tickSeconds)` | the **piecewise** answer built on the two above | the sheet's three-branch ETA |
| `LevelAtCapped(l0, ppt, t, tickSeconds)` | the piecewise **forward** projection — `SecondsToTarget`'s twin | — (`AtHourPlanner` uses it) |

**Do not replace these with a tick loop or a "safer" numeric solve.** They are not approximations;
they are the exact integral of the game's own per-tick rule, and that is only true because `r` is
constant. But the closed form alone is **only half the answer** — see the next section, which is the
one fact the whole calculation turns on.

## The overflow is DISCARDED — which is why the ETA is piecewise

`updateAdvancedTraining()` does, per tick:

```csharp
barProgress[id] += progressPerTick();
if (barProgress[id] >= 1f) { barProgress[id] = 0f; if (canLevel()) level++; }
```

**The bar is set to zero, not decremented by 1.** The overflow is thrown away. So a slot with
`progressPerTick >= 1` gains **exactly one level per tick** — 0.02 s per level, hard — however much
energy is assigned to it. Ten times the energy buys nothing there.

That is the fact everything on this panel turns on. The closed form `dL/dt = r/(L+1)` is the *uncapped*
rate; it is valid **only above the blitz-boost ceiling**. Below the ceiling the game is not slower than
`r/(L+1)` — it is *capped*, and the cap is the binding constraint. `AtMath.SecondsToTarget`
(`AtMath.cs:57-75`) therefore has three branches off `ceiling = BbCeiling(ppt·(level+1), 1.0)`:

| case | answer |
|---|---|
| `target <= ceiling` — never leaves the capped region | `tickSeconds · (target − level)` |
| `level >= ceiling` — already past it | the pure closed form, `SecondsToLevel(level, target, r)` |
| straddling | `tickSeconds · (ceiling − level)` **+** `SecondsToLevel(ceiling, target, r)` |

with `r = ppt·(level+1)/tickSeconds` (which is the constant `50·M/baseTime`). The two regimes are one
prefix and one suffix and can never interleave: `r` is constant while `progressPerTick` falls as the
level rises, so a slot below the ceiling stays below it until it crosses, once.

### The source spreadsheet had this right; the first native port flattened it

This is recorded so the three branches do not later read as over-engineering somebody could simplify
away. The sheet is a **three-branch** calculation — that is exactly what `bb`/`bbtrue` are for. The
first implementation here used `SecondsToLevel` unconditionally, and it shipped.

**The direction of the error is the memorable part: capping at one level per tick makes an ETA
*slower*, not faster, so the flattened version *under-quoted*.** The worked case pinned in the tests —
level 1000 → 5000 at `ppt = 2`, ceiling 2001, `r = 100100` — is **124.925 s** piecewise against
**119.925 s** uncapped. The uncapped form starts at `r/(L+1) = 100` levels/s, twice what one level per
tick allows, so dropping the cap promises a time the game physically cannot deliver.

### Wish 190 stays a separate flat-rate path, deliberately

`AtPanel.cs:283-285` keeps the wish on its own `(target − level) · TickSeconds` branch instead of
folding it into `SecondsToTarget`. **`SecondsToTarget` cannot be told the difference.** The wish pins
`progressPerTick` at `1f` at *every* level, so the slot never falls out of one-level-per-tick — but
from a single sample, `ppt == 1` is indistinguishable from a slot sitting exactly on its ceiling and
about to slow down. Folded in, it would take the straddle or closed-form branch and quote a
deceleration that will never happen. Do not merge these two paths.

## How the derivation was validated — backwards, against the sheet's own displayed cells

The functions could not be diffed against their source, so they were checked against the **outputs**
the sheet still prints. Two independent checks, both pinned in `AtMathTests.cs`:

**1. The BB ceiling cell.** The sheet's `modifier` is `(ecap/1000)·sqrt(epow)·(1+gear)` — it divides
energy by **1000** where the game divides by **50**, so `M = 20 × modifier`. With the Power/Toughness
`baseTime` of `1e7` and the sheet's own `modifier = 1.85202591774521e14`:

```
M            = 20 × 1.85202591774521e14 = 3 704 051 835 490 420
M / baseTime = 370 405 183.549042        <-- the sheet displays 370405183
BbCeiling    = 370 405 182.549042        <-- one lower; see the off-by-one below
```

`BbCeilingReproducesTheSheetsHighestBbLevel` asserts the exact `370405182.549042` **and** that it
lands within one level of the sheet's displayed `370405183`. That second assertion is the claim the
derivation actually makes: it breaks if either the ×20 or the −1 is wrong.

> Historical note, because it looks like a bug in the record: this test failed on its first run. The
> defect was the **plan document's literal** (`370405183.5`, which straddles
> `Assert.Equal(_,_,0)`'s banker's-rounding boundary), not `AtMath`. `AtMath.cs` was never changed.
> The implementer refused to bend the formula to the test and reported instead — which is the
> behaviour this module wants.

**2. The worked Power case.** Current level 4 758 488 → target 4 825 398, both **below** the ceiling,
so every level costs exactly one tick: `0.02 × (4 825 398 − 4 758 488) = 1338.2 s`, which is what the
sheet displays. Pinned as `TheSheetsWorkedPowerCaseIsOneTickPerLevelBelowTheCeiling`, which calls
`AtMath.SecondsToTarget` and asserts `1338.2`.

> **The test-gap lesson, worth more than the case itself.** That assertion originally computed
> `0.02 * (target - current)` **in the test body** and compared it to itself — so it validated
> arithmetic written in the test, not the shipped code, and stayed green while `AtPanel` quoted the
> uncapped closed form for exactly this scenario. That is how the missing tick cap shipped past a full
> suite. **An expected value that re-implements the calculation is not a test.** When adding cases
> here, call the shipped function and assert a number derived independently — from the sheet, from the
> decomp, or worked by hand.

## The off-by-one: the decomp wins, the sheet is wrong

`progressPerTick >= 1` ⇔ `L + 1 <= M / baseTime`. The **highest level still blitz-boosting** is
therefore `M/baseTime − 1`.

The sheet's "Highest BB level (full ecap)" cell solves for `L+1` and prints it as `L`, so it reads
**one higher** than the truth. `AtMath.BbCeiling` subtracts the 1 (`AtMath.cs:50-54`).

**This is recorded so nobody "corrects" the code toward the spreadsheet later.** A future reader who
opens the sheet, sees `370405183` next to the advisor's `370405182`, and deletes the `− 1` will have
reintroduced the source document's arithmetic slip. The decompiled controller is the authority here;
the sheet is the thing that is off by one.

## `baseTime` is read per slot, never hardcoded

`baseTime` is a **serialized field on each `AdvancedTrainingController` instance**. The sheet bakes it
in as its `500000` (Power/Toughness, i.e. `baseTime = 1e7`) and `1000000` (the Wandoos pair, `2e7`)
constants. `AtPanel` reads the live field off the slot's own controller (`AtPanel.cs:293`), so it
cannot go stale if a slot is retuned, and the Power/Toughness-vs-Wandoos split comes out for free.

That is also why **cap-to-blitz-boost is one row per slot** carrying both horizons, rather than one
row per horizon (`AtPanel.cs:99-110`): a shared row would have to pick some slot's `baseTime`
arbitrarily.

## The panel asks the game for the rate; it does not re-derive it

Every number on the panel except `baseTime` comes from the game.
`advancedTrainingController.getProgressPerTick(id)` is read directly — energy, energy power, AT speed
bonus, wishes and all — exactly as `AtHourPlanner.ReadSlot` does (`AtHourPlanner.cs:279-282`). Two
identities from the derivation turn it into what the panel needs:

| identity | used for |
|---|---|
| `r = ppt · (level+1) / tickSeconds` (`= ppt · 50 · (level+1)`) | the rate inside `AtMath.SecondsToTarget` (`AtMath.cs:65`) |
| `M / baseTime == ppt · (level+1)` | the BB ceiling, hence `BbCeiling(ppt·(level+1), 1.0)` (`AtPanel.cs:256`, `AtMath.cs:64`) |

The `1.0` in that second call is **not** a placeholder `baseTime` — it is the collapsed ratio. `M` and
`baseTime` only ever appear as their quotient, and `ppt·(level+1)` *is* that quotient, so the divisor
is 1. Passing the real `baseTime` there would divide it out twice.

The only raw input the panel reads is `baseTime` itself, because it is the one thing
`getProgressPerTick` cannot give back — it is needed for "what cap would blitz-boost this for an
hour": `energy = 50·baseTime·(L+1) / (sqrt(epow)·atSpeedBonus)` (`AtPanel.cs:311-315`).

## Three states in which every number on this panel would be a lie

Each one is checked *before* anything is quoted, and each one is said out loud.

1. **The 25 000 basic-training floor.** `updateAdvancedTraining()` returns early unless
   `training.attackTraining[4] >= 25000` **and** `training.defenseTraining[4] >= 25000`. Below that
   **AT does not progress at all**, whatever energy is in it. `AtLocked` (`AtPanel.cs:360-372`) reads
   both as one answer, and **an unreadable gate counts as locked** — quoting a time is the failure
   that matters. When locked, `_state` goes `Danger`, every slot row drops its ceiling and its ETA,
   and every cap row reads `n/a while AT is locked`. The Time Machine section stays live: it is
   independent of this gate.
2. **Wish 190.** `wishes[190].level >= 1` forces `progressPerTick()` to a flat `1f` — **one level
   every tick regardless of energy**. This is a different regime, not a faster one: `dL/dt` is a
   constant 50/s rather than `r/(L+1)`, so the closed form would understate it by orders of
   magnitude. The panel switches the ETA to a flat `(target − level) · 0.02 s` (`AtPanel.cs:283-285`,
   and see "Wish 190 stays a separate flat-rate path" above for why it is not folded into
   `SecondsToTarget`) and replaces the ceiling with "blitz-boosting at every level" — with `ppt` pinned
   at 1, `BbCeiling` would evaluate to exactly the current level, a true but useless number that reads
   as a limit.
3. **`levelTarget[id] == -1`** is the game's own **pause** marker (`AtHourPlanner.ReadSlot` zeroes
   the rate for it). The row prints `held at target -1 — no rate` and **no duration at all**; the
   `target > 0` ETA branch is unreachable for such a slot by construction, so no ETA is computed and
   discarded.

**Locked is checked before the wish**: nothing progresses while locked, wish or no wish.

## The `null` return is the contract for every "don't know" case

`AtMath.SecondsToLevel` (`AtMath.cs:35-41`) returns `null` when `r` is `NaN`/infinite/`<= 0`, when
either level is `NaN`, or when `l1 <= l0`. `SecondsToTarget` (`AtMath.cs:59-62`) enforces the same
contract on its own inputs — `ppt` `NaN`/infinite/`<= 0`, either level `NaN`, `tickSeconds <= 0`, or
`target <= level` — and propagates a null out of the closed-form leg of its straddle branch rather
than adding a partial answer to nothing. **The null is the contract, not a placeholder** — same rule
as `PpEta`. A rendered `0s` or an `∞` in an ETA slot reads to the user as a real prediction, and this
module's entire value is that its numbers can be trusted; an ETA that is sometimes fiction is worse
than no ETA. `RenderSlot` honours it literally: a null prints `— no rate` with no duration
(`AtPanel.cs:286-287`). Do not "improve" this with a clamped zero, a large sentinel, or a dash
rendered as a time.

## The Time Machine section: Normal only, and bonus-aware on purpose

The sheet's TM tab is ported as `levels = T/0.02`, `cap = levels · unitCost · 1000 / (0.02 · power)`,
with `unitCost` being `timeMachineController.baseSpeedDivider()` (energy → TM speed) and
`baseGoldMultiDivider()` (magic → gold multi). `1000/0.02 == 50000` is literally the constant in
`TimeMachineBP.cs:71`/`99` — that identity is how the sheet's formula was matched to game truth.

**Normal only, because the sheet's own author disowns the Evil column** ("The evil portion of this is
broken :( "). Shipping arithmetic its source document marks as broken would be worse than shipping
nothing, so the header says `TIME MACHINE — NORMAL ONLY` and `_tmNote` says why. Note the precise
scope of what is skipped: `timeMachineController.sadisticDivider()`, which `TimeMachineBP` applies at
`rebirthDifficulty >= sadistic` (`TimeMachineBP.cs:77-78`), is deliberately **not** applied here.

**And a deliberate improvement over the source sheet, which must not be "fixed" back.** The panel
divides by the game's real TM speed bonuses — `hacksController.totalTMSpeedBonus()`, the TM
challenge's `TMSpeedBonus()`, and `cardsController.getBonus(cardBonus.TMSpeed)` — exactly the three
divisors at `TimeMachineBP.cs:73-75` (`AtPanel.cs:325-327`). The sheet's cell is bonus-free, so it
**overstates** the cap needed by whatever the player has already earned. This is the repo's standing
"game-truth first, ask the owning module" rule beating a source document; it is not a divergence for
someone to reconcile toward the spreadsheet. Each bonus is read through `Read(…, 1.0)`, so an
unavailable one degrades to a neutral factor.

**Known limitation, faithful to the sheet:** the TM rows substitute the horizon's level count for the
game's `(1 + machine.levelSpeed)` term, i.e. they answer "the cap that holds blitz boost through `T`
of one-per-tick gains" from level 0, and they do **not** add the player's current TM level. The row
prints the level count next to the cap so the meaning is on screen.

## The duplication with `AtHourPlanner` — half resolved, and the deferral was hiding a bug

`AtHourPlanner` kept **private copies** of the `LevelAt` closed form and of the `1 + 0.1·L^0.4`
multiplier. The switch was deferred on the grounds that `AtHourPlanner` decides an AT run
**segment's length** and that a behaviour-preserving refactor with **no runtime verification
available** is the wrong trade.

**That deferral cost real accuracy: the private copy was the UNCAPPED closed form.** Deferring the
consolidation deferred the bug fix with it. The planner over-projected blitz-boosting slots — up to
**+233 %/+331 %** across its own 2–4 h window at a realistic `ppt ≈ 78` — and so extended the
segment chasing thresholds the run could not reach. Details in
`docs/modules/AtHourPlanner.md` ("The uncapped projection was a bug").

Fixed 2026-08-10 by adding `AtMath.LevelAtCapped` (the forward twin of `SecondsToTarget`, same
branches off the same ceiling) and pointing `AtHourPlanner.LevelAt` at it. The lack of runtime
verification was answered with a **provable equivalence** instead of a delay: at `ppt <= 1` the
ceiling is at or below the current level, the first branch fires, and the arithmetic is identical to
before — so only blitz-boosting slots can move. Two tests pin both halves of that
(`LevelAtCappedEqualsTheUncappedClosedFormWhenNotBlitzBoosting`,
`LevelAtCappedIsStrictlyLowerThanTheUncappedFormWhenBlitzBoosting`).

**Still duplicated:** the `1 + 0.1·L^0.4` multiplier, inlined in `AtHourPlanner.Ratio` rather than
calling `AtMath.StatMultiplier`. One formula, not three, and it carries no known defect — folding it
in remains a queued follow-up.

**The lesson worth keeping:** "defer the consolidation until runtime verification returns" is safe
only for a copy that is *known equal*. This one was not, and nobody had checked. When a private copy
is found, diff it against the canonical version before deciding the duplication is harmless.

## The GOAL & HELD TARGET section — the threshold is measured against the GOAL, not the curve

Added 2026-08-10 on the owner's decision. The two questions it answers: *which target is the advisor
holding and why*, and *up to which level does more AT still buy progress*.

**There is no stopping point in the AT curve, so the threshold had to be defined against something.**
`1 + 0.1·L^0.4` has marginal gain `0.04·L^-0.6` — decaying, never zero — against a cost linear in
`L+1`. The owner's rule: measure it against the **next objective**, i.e. the level at which the titan
stage's staged requirement is already met and further AT buys a bigger number but no progress. That is
the same rule `LevelPlanner` freezes P/T on; this section shows it as a level.

**The levels are asked of `AtHourPlanner.GoalLevels`, never computed in the panel.** That module
derives two non-interchangeable attack references (beast mode included for the titan ladder, divided
out for the zone tables) and conflating them was a real ~1.5× understatement — see
`docs/modules/AtHourPlanner.md`. The panel is a view.

| state | the row prints |
|---|---|
| accessor returns a level | `Toughness — goal: level 1.2M (T3 v1 idle stats)` |
| accessor reports the need met (both slots, or NaN for this slot) | `goal: already met` |
| no objective / unreadable requirement or levels | `goal: unavailable` — **never a fabricated number** |

The held-target row prints the game's own semantics, not levels: `levelTarget == 0` is `uncapped`,
`-1` is `paused`, anything else is that level; and the *why* is `LevelPlanner.Status` verbatim
(`caps: AT frozen`, `caps: none`), or `level planner off — your own targets` when it is empty because
the auto profile is off. A `_goalNote` line states that the goal level is **not a cap**, so nobody
reads it as one.

## The Time Machine section is answered in GOLD, because that chain is real

The first cut of this section was going to say the TM has no goal-based answer. **It does** — the
owner corrected it: TM raises GPS, GPS buys digger upgrades, digger bonuses feed adventure stats,
adventure stats kill titans. The answer is simply denominated in gold rather than in levels, so the
section prints:

- **gold demand** — `OptimizationAdvisor.GoldStarvedForDiggers/ForAugs(c, 1.0)`. Gold's two consumers
  are digger upgrades and augments, so being starved for either *is* the "more TM still buys progress"
  signal. Asked of the modules that own those budgets.
- **gold rate** — `grossGoldPerSecond()` and `goldPerSecond()`, labelled separately: the second is net
  of the diggers' own drain, a different question.
- **the freeze** — `LevelPlanner.TmFrozen`, plus its condition. It is gated on the marathon segment
  (`LevelPlanner.cs:69-75`), so outside `NGU MARATHON` the row says the targets are **not managed in
  this segment** instead of implying the advisor is holding something.

**No gold→titan-stat threshold and no single "TM worth" number, deliberately.** The chain's exchange
rate is phase-dependent, which is the same reason this repo refuses a single scalar across gold /
boosts / PP / EXP (`ItopodFarmAdvisor.md` §Open). The flags plus the rate give the same decision
without a conversion that would be wrong for half a run.

## Read-only: it never feeds energy and never writes a target

There is no button, numeric or combo on this panel; `SyncFromSettings` only reads, and the sole
handler is `VisibleChanged` calling that same read-only refresh. `AdvancedTrainingBP` owns the feed
and `LevelPlanner` owns the level targets. A second writer on an advice surface would be two owners
for one allocation — and **AT levels cannot be un-bought**, so there is no undo to fall back on.

## Layout, threading, registration

- **Height is derived, never tuned.** Every `y` chains off the previous control's `Bottom`;
  `ContentHeight = provenance.Bottom + UiTheme.S(10)`, and `SettingsForm` places the panel with
  `_atPanel.ContentHeight`, never a literal (`SettingsForm.cs:682-683`). The panel does not scroll —
  it is hosted in a scrolling section — so a hand-tuned height would clip its last lines at real DPI.
- **`ContentHeight` is reported once, in the ctor**, so constant prose (`provenance`, `_tmNote`) is
  filled there via `FitOrGrow`/`WrapInto`, and every *refreshed* string uses a fixed-height fit —
  `FitInto` for value lines, `WrapInto` into the reserved boxes. **Nothing in `SyncFromSettings` may
  call `FitOrGrow`**: it would resize a label after the height was handed to `SettingsForm`.
- **All five slots always render**, unfed ones labelled rather than hidden. A row that vanishes from a
  five-row readout reads as "there is no such slot", and a row count that changes with the feed
  cannot have a ctor-derived `ContentHeight`.
- **`_state` always says something**, including the healthy case. A gate line that is blank when fine
  gives the user no way to tell "no gate" from "the panel did not refresh".
- **Main-thread rule**: the only live reads are in `SyncFromSettings()`, called from the deferred
  ≤1/s Unity-main-thread pass (`SettingsForm.cs:1611`) and from `VisibleChanged`. The whole body is
  one `try/catch` that logs, because a throwing read in that pass aborts every panel after it, and
  each individual read additionally goes through the guarded `Read<T>` helper so a bad read degrades
  **its own row** only — the same contract `AtHourPlanner.ReadSlot` honours.
- `Controller(c, id)` is a five-case switch duplicated from `AdvancedTrainingBP.ControllerFor`
  (private there), needed only for `baseTime`. Slot ids are `AtHourPlanner`'s: 0 = Toughness,
  1 = Power, 2 = Block, 3 = Wandoos Energy, 4 = Wandoos Magic.
- `Duration()` is a private local formatter taking **seconds**, not hours (`PpPanel`'s takes hours):
  a blitz-boosted level lands in 0.02 s, so an hours-based formatter would collapse every short ETA
  to `<1m`.
- Placed in **Combat**, not Economy: AT's whole output is the adventure Power/Toughness/Block
  multipliers plus the Wandoos speeds, and `AtHourPlanner` — the module that owns AT feeding — is a
  combat/segment concern.
- **Registered as a `SettingsIndex` Reference with EMPTY fields** (`SettingsIndex.cs:439-443`), routed
  to `Destinations.AdvancedTraining` (= `"Combat"`, `Destinations.cs:53`). A `Ref` and not a `Sys` for
  the same reason `PerkPoints` and `ApPurchases` are: the panel is a pure readout, so it owns no
  setting and has no automation or advisor/manual gate that a System entry would promise. The fields
  column stays empty — naming a setting this panel cannot write would put a duplicate surface behind
  it and trip the catalogue's duplicate audit.

## Tests, and the invalid audit baseline

`tests/NGUAdvisor.Tests/AtMathTests.cs`, 16 facts: `LevelAt(l0,r,0) == l0`; monotone in `t`; `r == 0`
stays at `l0`; `SecondsToLevel` round-trips `LevelAt`; `SecondsToLevel` returns null for zero,
negative, `NaN` and `+∞` rates and for a target at or behind the current level; `StatMultiplier` is 1
at 0 and at negatives and matches `1 + 0.1·L^0.4` at a known level; the two sheet-validation cases
above; and one case per `SecondsToTarget` branch — the straddle (`124.925 s`, asserted **slower** than
either single-regime shortcut), the pure closed-form branch below one progress per tick, and the null
contract. Then five for `LevelAtCapped`: the **`ppt <= 1` equivalence with the uncapped form** and the
**strict-inequality half above it** (the pair that made the `AtHourPlanner` fix reviewable), `t = 0`
and the no-rate/no-tick guards, the pure one-level-per-tick region, and the straddle round-tripping
against `SecondsToTarget`. Then three for `LevelForMultiplier`: the round trip against
`StatMultiplier` across five levels, the forward check (the level it names really yields the asked-for
multiplier, plus the hand-worked `5^2.5` for `m = 1.5`), and the null contract (`m <= 1`, negatives,
`NaN`, ±∞). `AtPanel` is Unity-dependent — build only. Suite: 218 passing.

> **MEASURED 2026-08-10 against build `260810-1616`: `UI AUDIT` reports ZERO issues across all 26
> audited panels**, this module's included. The docs' standing "the audit must be zero" rule holds —
> earlier notes in this repo claiming a non-zero baseline of 70 were wrong, and are corrected here.
>
> Those 70 came from a run that calibrated at `scale 1.00` (`UI metrics: … line 25, head 22`), where
> every single finding was the same class: `CONTROL TOO SHORT FOR TEXT 'ComboBox:<text>' h=24 < 25`.
> On the real display calibration (`scale 1.52`, `line 38, head 33`) they all fit and the audit is
> clean. **So read the `UI metrics` line BEFORE believing a dirty audit** — a wall of
> `CONTROL TOO SHORT` lines is far more likely to mean the advisor calibrated on an unscaled context
> than that the panels are broken.

## Known limitation

The TM rows are from-zero figures — see "Known limitation, faithful to the sheet" under the Time
Machine section. Everything else on this panel is bounded only by the fact that none of it has been
seen running.
