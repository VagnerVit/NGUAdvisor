# Main + Loader (`Main.cs`, `Loader.cs`)

`Loader.Init()` is the injection entry point (`smi.exe … -n NGUAdvisor -c Loader -m Init`): it
creates a `GameObject`, adds the `Main` MonoBehaviour, and marks it `DontDestroyOnLoad`.
`Loader.Unload()` calls `Main.Unload()` then destroys it.

## Cached statics — the documented invariant

`Main.Character` and `Main.InventoryController` are `static readonly`, resolved once. NGU keeps ONE
`Character` MonoBehaviour for the whole process and its save/load path
(`Character.saveLoad.loadintoGame`) deserializes INTO that instance rather than reconstructing it —
so the reference never goes stale on an in-game save reload. This is why ~20 managers cache
sub-controllers the same way. Only a full scene teardown / new Character would invalidate it (does
not happen in NGU), and our own reload discards statics wholesale. **Do not "fix" this into live
lookups.**

## Start()

1. AppData dir `%userprofile%/AppData/LocalLow/NGUAdvisor` (+ `logs`, `profiles`).
2. **One-time migration from `LocalLow\NGUInjector`** — merged PER ENTRY, not gated on the new
   folder being absent (the launcher may already have created it holding `injector-path.txt`).
3. Log writers, all `AutoFlush`. `pitspin.log` and `cards.log` open in APPEND mode (not
   overwritten across sessions); the rest truncate. A `debug.log writer alive (vX build Y)` probe
   line is written immediately — if debug.log is empty even of that, the writer itself is broken and
   every "Advisor … failed" message has been invisible.
4. `PresetInstaller.InstallMissing` before profiles are listed; legacy `allocation.json` →
   `profiles/default.json`.
5. Settings load (falling back to defaults via `MassUpdate`), SettingsForm, allocation load,
   then a **normalising round-trip**: `SaveSettings` + explicit `FlushSettings` + `LoadSettings`
   (SaveSettings only marks dirty now, so without the flush the read would see a stale file).
6. Three `FileSystemWatcher`s: `settings.json`, `zoneOverride.json`, `profiles/*.json`.
7. Repeating invokes: **AutomationRoutine 10 s**, MonitorLog 1 s, QuickStuff 0.5 s,
   ShowBoostProgress 60 s, SetResnipe 1 s.

## THREADING — the rule that prevents hard crashes

Watcher events fire on background ThreadPool threads and must NOT touch Unity/WinForms. They set
`volatile bool` flags (`_reloadSettingsPending`, `_reloadAllocationPending`,
`_reloadProfilesPending`) which `Update()` drains on the main thread. Same rule for WinForms
handlers: **never call `LoadAllocation` directly** — use `Main.RequestAllocationReload()`
(user-reported crash: the dashboard Switch button).

`UpdateForm` is likewise request-only: settings saves used to rebuild the entire legacy form
synchronously, running heavy list refreshes mid-click and during Start BEFORE the form existed (a
throw there meant no GUI at all). `Update()` coalesces and refreshes at most once a second;
`_settingsFlushCooldown` drives the coalesced settings write.

`IgnoreNextChange` suppresses exactly ONE watcher event after our own write — see
MoneyPitManager.md for what happens when code writes settings four times in a row.

## AutomationRoutine (10 s)

Gated on `Settings.GlobalEnabled` (the F2 kill switch). Order: inventory pass (skipped while
`InventoryController.midDrag` — never fight the user's drag) → daily-save autosave (at 82800 s,
adds 200 AP and writes a timestamped save) → `ZoneHelpers.RefreshTitanSnapshots` + titan lock
handshake → Yggdrasil harvest/fruits → **`AdvisorApply.Tick()`** (before the AutoBuy block, which
can `return` early) → AutoBuy E/M/adventure.

**Permanent-unlock gates use raw `highestBoss`** (custom purchases ≥ 17, magic resource ≥ 37) — one
of the few correct uses, since these do NOT re-lock on Evil (only Augs/AT/TM/Blood do). Everything
progression-related must use `ZoneHelpers.CurrentHighestBoss` instead.

## Other Main responsibilities

- Log channels: `Log`/`LogLoot`/`LogCombat`/`LogPitSpin`/`LogCard`/`LogDebug`, each stamped with
  date + rebirth seconds. `LootFeed` is an in-memory newest-first ring (400) mirroring loot.log.
- `Version` (hand-bumped SemVer) + `BuildTag` (parsed from the assembly name
  `NGUAdvisor.r<yyMMddHHmmss>` → `yyMMdd-HHmm`).
- Hotkeys via `QuickStuff` (F1 window, F2 pause, F3 quicksave, F5 dump gear, F7 quickload,
  F8 quick swap, F9 profile editor) and the in-game overlay (`OnGUI`, `RefreshOverlayText`).
- `SnipeZone`, `SetResnipe`, `UpdateFurthestZone`, `ResetFurthestZone`: gold-snipe routing.
  Two statics are deliberately seeded to **−1, not 0**: `_furthestZone` (a 0 baseline made
  SetResnipe read any real zone as "new zone fightable" and wipe a completed snipe) and
  `_lastNewZoneTrigger` (each zone arms the re-snipe ONCE — fightability is measured in current
  gear but the snipe runs in gold gear, so a ratchet drop re-fired forever: user-reported infinite
  swap loop). `GoldSnipePays` gates the swap on payoff — the TM only ever converts the run's highest
  drop, so a snipe that can't beat the banked one latches `GoldSnipeComplete` instead of flipping
  gear (GoldDropAdvisor.md); the same check qualifies the "new zone fightable" trigger.
- `Unload()` — every step individually guarded (`Try(...)`); writers close LAST, and nothing may
  escape (a single throw used to abort a reload half-done, and the old catch called `LogDebug`
  AFTER closing DebugWriter).
