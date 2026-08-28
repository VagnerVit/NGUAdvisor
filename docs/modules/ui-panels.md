# UI panel layer (root `*Panel.cs`, `SettingsForm.cs`, `ProfileEditorForm.cs`)

WinForms views. **Panels are views: logic belongs in `Managers/`.** A panel may read manager
state and call manager actions, but should not compute game math itself — when a panel and the
advisor disagree, the fix is to share the manager's struct (as `MoneyPitManager.AdvisorPlan` and
`PitPanel` do) rather than duplicate the rule.

## Cross-cutting rules

- **Main thread only.** Every panel handler runs on the UI thread. A handler must NOT call
  allocation/game code directly — request it (`Main.RequestAllocationReload()`), because allocation
  touches Unity objects and hard-crashes off the Unity thread (user-reported: dashboard Switch).
- **Never rebuild the form synchronously from a settings save** — `Main.UpdateForm` only sets a
  flag; `Update()` coalesces to at most one refresh per second (see Main.md).
- **The RETIRED grid still runs.** Its pages are out of the tab strip but `UpdateFromSettings`
  (`SettingsForm.cs` ~1460+) still assigns every legacy control on every deferred refresh, and it is
  wrapped in ONE try/catch (`Main.cs:423`). So a single throwing assignment aborts the whole pass and
  **silently stops every panel after that line from syncing** — it surfaces only as
  `Deferred form update failed: … Parameter name: SelectedIndex` in `debug.log`.
  A legacy combo may be NARROWER than the live panel editing the same setting: measured 2026-08-10,
  `ITOPODCombatMode` carries 2 items (Idle, Snipe) against `AdventurePanel`'s 4, while `CombatMode`,
  `QuestCombatMode` and `TitanCombatMode` carry 5 and `ITOPODOptimizeMode`/`WishMode` carry 4.
  **So `SelectedIndex` on a legacy combo must be clamped to `Items.Count - 1`, never assigned raw** —
  and a settings validator's range in `SavedSettings.Assign*` describes the LEGACY combo it feeds, not
  the live panel's mode count. Widening a validator without clamping here turns a transient reset into
  a permanent exception on every refresh.
- **Style through `UiTheme`/`UiLayout`**, and reuse `SystemControlBar` for per-system headers, so
  the window reads as one app.
- **DPI: every hand-placed pixel goes through `UiTheme.S(n)`; every box that HOLDS text takes its
  height from `SText`/`SHead`/`SCtl`/`SLines`, never raw `S()`.** Lists are specified in ROWS
  (`ListH`), ComboBox/ListBox/NumericUpDown/CheckBox are owner-drawn because they size themselves
  from the 96-DPI `Font.Height`. Text that can't grow goes through `UiLayout.FitInto` (tooltip keeps
  the full value); prose uses `FitOrGrow`. Section canvases are `ScrollPanel`, children go into
  `Content`, and there is **ONE scroll owner per screen** — a nested page grows to its content.
  **The full contract is in ui-infra.md §DPI calibration — read it before placing a control.**
- **`UiLayout.Audit` is the oracle**: after any UI change, `debug.log` must show zero `UI AUDIT`
  lines (and the first `UI metrics:` line says which calibration branch was taken).
- **Outcome reporting goes through `Activity`** (`Completed/Queued/Warning/Failed`) — the ribbon
  renders the newest single outcome; LOGS is the history. Failures never auto-expire.
- **Numbers via `NumberFormatter.Abbrev`** — the one exception is `GoldPanel`, which tries the
  game's own `Character.display()` first to honor the player's in-game number-display setting.
- Selecting a mode or pressing REFRESH must never equip anything; `CURRENTLY EQUIPPED` snapshots
  update only on page open / REFRESH STATE (documented expected behavior in README).
- Overview scrolls VERTICALLY only — no horizontal scrolling anywhere.

## Layout

`SettingsForm` owns the shell: primary rail → category strip → page host, plus the pinned
`StatusPanel` (the eight status cells) and the activity ribbon. Retired pages are kept out of the
control collection deliberately (the tab strip is hidden but the collection stays clean).
`BasicSettingsPanel` does not scroll itself — it is nested in a scrolling host and **grows to its
content** (the one-scroll-owner-per-screen rule, same as `_xformPage` and `AdventurePanel`). So its
height must be re-derived whenever its content grows; a fixed height would silently clip, since a
non-scrolling panel has no scrollbar to reach the overflow with.

## Panels and their owning managers

| Panel | Reads / drives |
|---|---|
| `StatusPanel` | `ProgressionAnalyzer`, `ChallengeOverlay`, `LockManager`, `GrowthTracker` |
| `AutopilotPanel`, `ActionsPanel` | `OptimizationAdvisor` rec list + AdvisorApply toggles |
| `GrowthPanel` | `GrowthTracker` samples, `NGUAdvisors` predicted rates + `Diagnose` (the NGU tile's sub-line names the CAUSE when measurement diverges from prediction — highlighted, full text in the tooltip, logged once per change as `[GrowthDbg]`) |
| `ChallengesPanel` | `ChallengeOverlay.Feed` / `Block()` / `AllocationStatus` |
| `ProfilePanel` | profile list, switch/apply (via request), `PresetInstaller` output |
| `AdventurePanel`, `TitansPanel` | zone routing, `ZoneHelpers`/`OptimizationAdvisor` titan ladder (`TitansPanel.Abbrev` is reused by AtHourPlanner) |
| `SpendPanel` | `SpendOverview` — one row per currency, each naming the module that owns the ordering (it owns none itself) |
| `GoldPanel`, `PitPanel` | gold snipe state, `MoneyPitManager.AdvisorPlan`/`PredictNext` |
| `YggPanel`, `QuestsPanel`, `BloodPanel`, `LoadoutsPanel`, `InventoryAdvisorPanel`, `LightsPanel` | their same-named managers |
| `BoostsPanel` + `BoostPickerForm` | `InventoryManager.GetBoostSlots` (live readout), `InventoryAdvisor`, `TransformManager`; the NEVER BOOST panel writes `Settings.BoostBlacklist` and sits outside the two exclusive views (visible in both — see `PositionBlacklist`) |
| `SystemIndexPanel`, `BasicSettingsPanel` | `SettingsIndex` (+ the search box), `SystemCatalog` |
| `LogsPanel`, `LogSliver` | `LogTail` / in-memory feeds; the EXPORT STATE chip **requests** a dump (`Main.RequestStateExport`) — see StateExport.md, it must never call the exporter directly |
| `ProfileEditorForm` + `*EditorPanel` (Resource/Gear/List/Misc/WanDiff) | `ProfileModel` + `ProfileValidator` + `PriorityCatalog` |

`SettingsForm.Designer.cs` (233 KB) is designer-generated — edit the form in a designer and let the
build regenerate `SettingsForm.resources` (see CLAUDE.md; the resx→classic-resources step is
mandatory for Mono).

Paste flows (gear IDs) parse+validate first, show the result for confirmation, change nothing on
invalid/empty input, and offer a single-level undo.

## Gear source: Manual / chain / objective (`GearEditorPanel.Card`)

A gear breakpoint has THREE sources, and the GEAR SOURCE dropdown has an entry for each: `Manual
(item IDs)` (index `ManualIndex` = 0), `Custom priority chain (edit below)` (`ChainIndex` = 1), then
every `GearObjectives.Objectives` entry, then every `GearChain.Presets` entry. **All index arithmetic
goes through those two named constants** — objectives start at `ChainIndex + 1`.

- Chain presets need no separate control: `GearChain.Resolve` tries a preset before an objective, so
  they are just more `Optimize: …` entries and `SourceChanged` needs no new branch.
- The chain **supersedes** the single objective (`ProfileModel.ListBreakpoint.Priorities`), so
  `ApplyMode` makes both controls say so: with ≥1 step the dropdown is forced to the chain entry and
  **disabled**, and the info line reads "The priority chain below is in charge (remove every step to
  change this)." Without that, a chain-only breakpoint sat on "Manual (item IDs)" while the card was
  plainly in Optimize mode, and clicking Manual fired no event — a control that looks broken.
- Picking the chain entry must DO something or it is a dead affordance: it clears `Objective` and
  seeds the first step. It is only reachable while there are no steps.
- An objective (or a chain step objective) this build no longer offers is **kept verbatim** and added
  to the combo, so opening and saving a profile cannot quietly rewrite a value the editor did not
  understand.

### PRIORITY CHAIN block

Lives inside `_objPanel` (it means nothing outside Optimize mode). One `ChainRow` per step: objective
combo + a `Slots` numeric + ✕/↑/↓. **Control order IS chain order** — `SyncChain` rebuilds
`_bp.Priorities` from the child collection rather than tracking moves and removals in two places that
can disagree. Removing the last step hands the card back to the gear source above (`ApplyMode`).

- **Leaving the chain restores the dropdown to `ObjectiveIndex()`, not to Manual.** `+ Add step` does
  not clear `_bp.Objective` (nor does a profile that carries both), so an emptied chain can still be an
  objective card. Resetting the combo to Manual there left the body in Optimize mode under a combo
  reading *Manual (item IDs)* — and clicking Manual raised no `SelectedIndexChanged`, because it was
  already selected, so the card was a dead end. `ObjectiveIndex()` is the ONE place that arithmetic
  lives (the constructor calls it too); duplicating it is how the two disagreed.
- **`+ Add step` is disabled at `GearChain.MaxPriorities`** (5). A sixth row would be silently dropped
  at runtime by `GearBreakpoints.ParseSpec`, so it is never offered.
- **`Slots` = 0 means "all remaining"**, said ONCE in the header band (`0 = all remaining`) instead of
  every row repeating it into the icon block. A negative stored `Slots` displays as the 0 it already
  means downstream (`GearBreakpoints.cs`) — the only normalization here, and a visible one.
- **The `Slots` ceiling ADMITS a larger stored value** (`Maximum = Max(MaxChainSlots, entry.Slots)`).
  Clamping only the *display* was a data-integrity bug: the model kept 30, and then editing the
  OBJECTIVE on that row wrote 24 back through `Push` — one field's edit silently rewriting another.
- **The summary line prints `GearChain.Describe`, never a computed slot split.** The optimizer does
  not plan budgets ahead and pinned accessories are frozen before step 0, so no per-step figure could
  be stated truthfully from here, and a number the runtime contradicts is worse than no number.
  `Describe` is also free of game reads, which keeps the label off the Unity thread on every keystroke
  of a slot numeric. See GearChain.md §Describe.
- **Heights**: `_objPanel` grows with the chain (`objH = ObjInfoH + ChainH`) because it has no
  scrollbar to reach an overflow with. `ChainH` and the summary's placement are two halves of ONE
  statement: with zero steps `ChainH == ChainHeadH`, so the summary must be hidden in the same breath
  or it sits past `_chain`'s bottom edge — the PAST PARENT BOTTOM the auditor exists to catch, in the
  commonest state of all (an objective-mode card with no chain).

## ALWAYS EQUIP band (`BasicSettingsPanel`, `Settings.PinnedGearIds`)

The global pinned-item list — "kept in every optimized loadout, ahead of the objective's own picks".
A **fifth heading and deliberately not a fifth column**: the pinned list is a list, not a checkbox, so
it takes a full-width band below the four-column grid rather than being squeezed into the MISC stack.
The four captions above remain the column contract; this one is a band.

- **One surface, one control**: `Reg("PinnedGearIds", "ALWAYS EQUIP", pins)` registers the whole band
  container, so `SettingsIndex` filtering moves it as a unit and there is no way for the list to
  appear without its buttons, or a button without the list it edits.
- The heading is added to `_groups` **before** `Reg`, so `Reg` can find it.
- Paste reuses `GearEditorPanel.AskPaste` verbatim (parse+validate, preview, nothing changes on
  invalid/empty/cancelled input) and offers the same single-level, timer-free undo as the profile
  editor: a plain-data snapshot plus a deadline evaluated when the user reaches for the button —
  these windows have no WinForms message pump, so WM_TIMER never arrives and the deadline is the only
  authority. Every mutation goes through `ApplyPins`, so paste/clear/undo cannot diverge.
- `_pinsSync` refreshes only when the live list disagrees with `_pinsKnown` (what the panel last
  rendered). `UpdateFromSettings` runs up to once a second and our own save flags it, so an
  unconditional refresh would wipe the status message just written; a list that DID change is a
  foreign edit, which also invalidates the undo token.
- The status label gets its **own full-width line** and is not `AutoSize`: trailing the button row it
  cut off exactly the text explaining why a paste was refused (and read as a PAST PARENT EDGE audit
  line). On its own line it goes through `FitOrGrow` and word-wraps. The band's height is fixed by
  construction from two reserved text lines — a band that resized itself later would contradict the
  grid snapshot `CaptureGrid` takes.
- The **validation of what may be pinned is not the panel's**: `SavedSettings` filters
  `PinnedGearIds` through `IsEquipment(id)`.

Note for the LIGHTS page: cell 5 (GEAR) shows `GearBreakpoints.ActiveObjective`, which is the chain's
**canonical lead objective** (`chain[0].Objective.Name`) — so a chain preset's own name is not
preserved there. A breakpoint running `Adventure + Respawn` reports "Adventure set", not the preset
name. `GearChain.Describe(chain)` is the string that names the whole chain (it is what
`AdvisorApply` logs).

## The Profile Editor's DPI debt (fixed 1.2.24) — and why it audits every tab

The editor had the FIRST half of the DPI contract and none of the second: 223 `UiTheme.S()` calls but
zero `SText`/`SCtl`, no `ScaledCheckBox`, no `OwnerDrawTabs`, no `FitOrGrow`/`BtnWidth`. Raw pixels
scaled; nothing that holds text took a floor. On a 200 %-scaling display (measured `line 38, head 33,
scale 1.52`) that produced clipped button captions, a tab strip showing a horizontal slice of its own
labels, and — the one no eye would name — a **54px NumericUpDown inside a 46px time chip**, because
`StyleNum` states `UiTheme.NumH` while the chip was still `S(30)`.

So the standing rules for this window: every container that holds a numeric derives from
`UiTheme.NumH` (chip → header → card), every row places its children **centred**, never at a tuned
`S(4)` top, and button widths come from `UiLayout.BtnWidth` rather than a literal.

`AuditTabs()` runs `UiLayout.Audit` over **all eight tabs**, not just Gear. While only the Gear panel
was audited the reported issue count was a floor — the ✕/↑/↓ row buttons and the numeric rows exist on
every tab, so a clipped caption on Energy or Misc had nothing reporting it. The 137 issues that showed
up in `debug.log` were one tab's worth.

## BloodPanel — the SINKS rows (2026-08-28)

The INPUTS block is split in two. The top half is unchanged (Auto Spell Swap + the three
on-rebirth toggles + `Guff A/B >=`). Below it, **SINKS** is one row per blood sink:

| column | Spaghetti / Counterfeit | NUMBER |
|---|---|---|
| caption | `ScaledCheckBox` = permission (`BloodWantSpaghetti` / `BloodWantCounterfeit`) | plain label — it is the fallback sink, so "off" is not a state |
| `up to` / `floor` | ceiling in %, 0 = none | floor, 0 = none |
| status | `now 19% -> 40%` / `target reached` / `no ceiling` / `off` | `now x500M -> floor 100M` / `floor met` |

Geometry is three scaled column constants (`SinkCapX`/`SinkNumX`/`SinkStatX`); the status labels are
the only per-tick writes and go through `UiLayout.FitInto`.

**Auto Spell Swap is disabled and greyed while `CastBloodSpells` is on**, with `_swapNote` spelling
out why: Main only runs that path when automation is OFF. It used to sit there lit green, doing
nothing — the same class of bug as the two dead % fields (see BloodPlanner.md).
