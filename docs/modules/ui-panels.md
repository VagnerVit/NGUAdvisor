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
| `GrowthPanel` | `GrowthTracker` samples, `NGUAdvisors` predicted rates |
| `ChallengesPanel` | `ChallengeOverlay.Feed` / `Block()` / `AllocationStatus` |
| `ProfilePanel` | profile list, switch/apply (via request), `PresetInstaller` output |
| `AdventurePanel`, `TitansPanel` | zone routing, `ZoneHelpers`/`OptimizationAdvisor` titan ladder (`TitansPanel.Abbrev` is reused by AtHourPlanner) |
| `GoldPanel`, `PitPanel` | gold snipe state, `MoneyPitManager.AdvisorPlan`/`PredictNext` |
| `YggPanel`, `QuestsPanel`, `BloodPanel`, `LoadoutsPanel`, `InventoryAdvisorPanel`, `LightsPanel` | their same-named managers |
| `BoostsPanel` + `BoostPickerForm` | `InventoryManager.GetBoostSlots` (live readout), `InventoryAdvisor`, `TransformManager` |
| `SystemIndexPanel`, `BasicSettingsPanel` | `SettingsIndex` (+ the search box), `SystemCatalog` |
| `LogsPanel`, `LogSliver` | `LogTail` / in-memory feeds |
| `ProfileEditorForm` + `*EditorPanel` (Resource/Gear/List/Misc/WanDiff) | `ProfileModel` + `ProfileValidator` + `PriorityCatalog` |

`SettingsForm.Designer.cs` (233 KB) is designer-generated — edit the form in a designer and let the
build regenerate `SettingsForm.resources` (see CLAUDE.md; the resx→classic-resources step is
mandatory for Mono).

Paste flows (gear IDs) parse+validate first, show the result for confirmation, change nothing on
invalid/empty input, and offer a single-level undo.

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
