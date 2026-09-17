# FarmMode

`Managers/FarmMode.cs` — the single exclusive answer to **"what am I farming?"**, plus the
`FARM MODE` selector that `AdventurePanel` renders for it.

## Why it exists

The answer was always exclusive in the CODE:

* `Main.ResolveIntentZone` ([Main.cs:1604](../../NGUAdvisor/Main.cs)) picks exactly one of
  gear hunt → Target ITOPOD → `SnipeZone`.
* `AdvisorApply.ApplyZones` picks exactly one of gear farm → boost farm → ITOPOD.

But it was SPELLED across five independent booleans on two different sub-tabs:

| Flag | Lived on |
|---|---|
| `GearHuntEnabled` (+ `GearHuntZone`) | Combat › ZONES |
| `AdvisorFarmGear` | Combat › ZONES |
| `AdvisorFarmBoost` | Combat › ZONES |
| `AdvisorZones` | the ADVENTURE control bar's DECISIONS half |
| `AdventureTargetITOPOD` | Combat › **ITOPOD** |

So the UI could show two farms "on" while only one routed, and the page that owned the WINNER of
the cascade was not the page that owned the flag you wanted to switch to. The reported symptom:
farming the ITOPOD and wanting boosts meant clicking Farm Best Boost on ZONES, then walking to the
ITOPOD tab to clear Target ITOPOD — two tabs for one decision.

## The contract

`FarmModeKind` has five values, in the cascade's own precedence order:

| Mode | `GearHuntEnabled` | `AdventureTargetITOPOD` | `AdvisorFarmGear` | `AdvisorFarmBoost` | `AdvisorZones` |
|---|---|---|---|---|---|
| `Hunt` | ✓ | – | – | – | – |
| `Itopod` | – | ✓ | – | – | – |
| `Gear` | – | – | ✓ | – | ✓ |
| `Boosts` | – | – | – | ✓ | ✓ |
| `Zone` | – | – | – | – | – |

* **`AdvisorZones` ON is the whole advisor answer.** `ApplyZones` falls THROUGH to
  `BoostFarmAdvisor` when `AdvisorFarmGear` is off, whatever `AdvisorFarmBoost` says — that flag only
  adds the "no boost demand → park in the pod" override. So `Current()` reads `AdvisorZones` first
  and only then asks which farm. Requiring `AdvisorFarmBoost` read a legacy `settings.json` with
  `AdvisorZones` on and both farm flags off as `Zone`, while the advisor overwrote `SnipeZone` every
  ten minutes.
* **`Current()` DERIVES the mode, never caches it**, and reads the flags in `ResolveIntentZone`'s
  own order. A cached copy would go stale on a settings reload, a profile switch or the PP panel's
  shortcut, and then show a mode nobody was farming — the exact failure this replaces.
* **`Set()` writes settings and nothing else.** Every caller is a WinForms handler, so it must not
  touch Unity objects; the next `Main.Update()` reads the flags and routes. The one extra call is
  `AdvisorApply.GearRestored()` when the hunt flips, which the old hunt toggle already did.
* **`Previous`** is the mode before the current one, so a shortcut can hand routing BACK instead of
  guessing. Session-scoped on purpose: after a reload there is no "before".
* **No new persisted setting.** A `settings.json` written before this still resolves.

## Every writer goes through it

There is no fourth path. Three surfaces write this choice and all three call `FarmMode.Set`:

| Surface | Notes |
|---|---|
| `AdventurePanel` FARM MODE row | the owner; panel-level, visible on all three segments |
| `PpPanel` pod shortcut | on → `Itopod` (+ `ITOPODOptimizeMode = PP`), off → `FarmMode.Previous` |
| `SettingsForm.TargetITOPOD` (retired grid) | same on/off pair |

`LoadoutsPanel.GateText` and `GrowthPanel` READ the flags and must stay read-only.
`SystemIndexPanel`'s Adventure row uses `RowState.FarmMode` — the right chip is the MODE, not an
ADVISOR/MANUAL bit, because advisor-vs-manual was only one row of a five-way choice.

## UI invariants

* The FARM MODE row sits **above** the `[ZONES] [ITOPOD] [BLACKLIST]` segment bar, so switching
  farms is one click from any page. `_pageTop` is derived from it — do not hard-code it back.
* The ADVENTURE `SystemControlBar` has **no DECISIONS half** (`getAdvisor: null`), because
  `AdvisorZones` is one of the flags this selector writes. It passes `decisionsChip: "FARM MODE"`
  so the chip names where the choice moved instead of claiming `MANUAL ONLY`, which would be false.
* The zone picker and the hunt stage picker stay **visible in every mode** and only go `Enabled =
  false` when they are not in play. Hiding them reflowed the page on every mode click, and a
  control that vanishes reads as "this setting is gone".
* The ITOPOD page is **HOW** the pod is farmed, never **WHETHER**.
