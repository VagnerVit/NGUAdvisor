# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

NGU Advisor is a **DLL injected into the Steam build of NGU Idle** (Unity 2019.4 / Mono). It reads live game state and automates allocation, gear, combat, gold, and other systems. Fork lineage: NGUInjector (rvazarkar) → rus9384 → this.

## Build

```
dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release
```

- Requires a local NGU Idle install — the csproj auto-locates `NGUIdle_Data\Managed\` in common Steam roots. Elsewhere: `-p:NGUManagedDir="X:\...\NGUIdle_Data\Managed"` or a `Directory.Build.props` (see BUILD.md).
- Output: `NGUAdvisor/bin/Release/net48/NGUAdvisor.r<timestamp>.dll` — the timestamped name is parsed into the build id shown in the UI; deploy renames it to `NGUAdvisor.dll`.
- **net48 is mandatory — never "upgrade" the TargetFramework.** The game's Mono runtime cannot load modern .NET assemblies.

### WinForms resources caveat

`.resx` files are NOT compiled by the SDK. `SettingsForm.resources` (classic format, checked in) is what gets embedded — the SDK's "preserialized" format needs `System.Resources.Extensions.dll`, which doesn't exist in the game's Mono domain. The `ConvertResx` MSBuild target regenerates it automatically via `build/convert-resx.ps1`, which must run under **Windows PowerShell 5.1** (`powershell.exe`, not `pwsh`).

### Deploy / release

Copy the built DLL over `injector/NGUAdvisor.dll` in a runnable folder and run `Run NGU Advisor.bat` with NGU Idle open (injects via `smi.exe`, entry point `NGUAdvisor.Loader.Init`). Release zips are produced by `package-release.sh` (maintainer machine only) — `./package-release.sh` to zip, `--inject` to build and inject into the running game without zipping, `--no-zip` to stage only. It needs no environment variables; injector tools live in the git-ignored `tools/injector/`. See BUILD.md.

## Tests

```
dotnet test tests/NGUAdvisor.Tests
dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~NumberFormatterTests"
```

xUnit on net9.0. The test project **links individual Unity-free source files** (`SimpleJson.cs`, `ProfileModel.cs`, `NumberFormatter.cs`, `TitanTables.cs`) instead of referencing the main csproj — so it builds without an NGU install. Only pure-logic files can be tested this way; code under test must pin `CultureInfo.InvariantCulture` on number paths so net9.0 results match net48/Mono.

## Architecture

Single project, three layers:

- **`Loader.cs` → `Main.cs`** — injection entry point; `Main` is a MonoBehaviour driving the update/automation loops, log writers, and `FileSystemWatcher`s for settings/profiles.
- **`Managers/`** — all advisor/domain logic (~60 files): combat AI, gear optimizer (native reimplementation of the web Gear Optimizer's scoring), stage detection, planners, per-system managers. `SavedSettings.cs` is the persisted settings model.
- **`AllocationProfiles/`** — execution of user profile JSON: `CustomAllocation` + `Breakpoints/` (energy/magic/R3/gear/diggers/beards/wandoos/rebirth/consumables). Profile grammar is documented in README.md.
- **Root `*Panel.cs` + `SettingsForm.cs`** — WinForms UI. Panels are views; logic belongs in `Managers/`.
- **`Presets/*.json`** — embedded goal/profile presets, auto-installed to the runtime profiles dir by `PresetInstaller`.
- JSON parsing is `SimpleJson.cs` (vendored) — no Newtonsoft/System.Text.Json available in the game domain.

Runtime data (settings.json, profiles, logs incl. `debug.log`) lives in `%UserProfile%\AppData\LocalLow\NGUAdvisor`.

### Threading invariants (violations hard-crash the game)

- **Main-thread rule:** Unity objects may only be touched from the Unity main thread. WinForms handlers and `FileSystemWatcher` callbacks must NOT call allocation/game code directly — they set pending flags (e.g. `Main.RequestAllocationReload()`) that `Main.Update()` drains.
- **Static `Character` caching is intentional and safe** — NGU keeps one `Character` instance for the whole process; save-load deserializes into it. Do not "fix" cached statics into live lookups (see the invariant comment at the top of `Main.cs`).

### Evil-difficulty correctness checklist

Any new advisor calculation or gate must clear the four cross-cutting patterns in `docs/NGU-KNOWLEDGE.md` ("Evil-era correctness — standing checklist"): use `ZoneHelpers.CurrentHighestBoss()` not `highestBoss`; gate re-locked systems on `buttons.<x>.interactable` not boss numbers; handle the Evil low-boss re-climb; prefer the game's difficulty-aware methods over Normal-tuned magnitude thresholds.

## Module docs — READ BEFORE TOUCHING A MODULE

`docs/modules/<Name>.md` documents almost every module. **Before reading or modifying
`Managers/<Name>.cs`, read `docs/modules/<Name>.md` first** — the docs carry the invariants,
game-truth formulas, decomp provenance, and the user-reported bugs whose fixes must not be
regressed. They exist because most of the non-obvious code in this repo encodes a hard-won rule
that looks removable.

Naming: a doc matches its `.cs` file name. Exceptions (grouped docs):

| Doc | Covers |
|---|---|
| `AllocationProfiles.md` | all of `AllocationProfiles/` (breakpoint engine, token engine, rebirth types) |
| `Main.md` | `Main.cs` + `Loader.cs` |
| `ui-panels.md` | root `*Panel.cs`, `SettingsForm`, `ProfileEditorForm` |
| `ui-infra.md` | SettingsIndex, Activity/ActivityRibbon, Destinations, SystemCatalog, PriorityCatalog, **UiTheme/UiLayout/ScrollPanel/ScaledCheckBox (the DPI contract — read before placing any control)**, SystemControlBar, LogTail, PresetInstaller |
| `small-managers.md` | BeardManager, CookingManager, ChallengeDetector |
| `ProfileModel.md` | ProfileModel + ProfileValidator + GrowthTracker |
| `TitanTables.md` | TitanTables + NumberFormatter |
| `ZoneCadence.md` | ZoneCadence + BoostValueMath + BoostSinks (**farm-rate substrate: kill cadence, idle-vs-manual truth, boost pricing — read before touching either farm advisor**) |
| `ExpBalancer.md` | ExpBalancer + ExpRatioTables (guide EXP-ratio phase table) |
| `GoldDropAdvisor.md` | GoldDropAdvisor + GoldDropTables (gold-kill payoff vs. the TM's banked drop) |
| `reference-gear-optimizer.md` | `external/gear-optimizer/` (the JS oracle) |
| `gear-optimizer-comparison.md` | native vs reference: validated ports, deliberate divergences, gaps |

If a module has no doc yet, write one when you finish working on it.

### Cross-cutting invariants the docs keep repeating

- **Boss reads**: `ZoneHelpers.CurrentHighestBoss(c)` for anything progression-related; raw
  `highestBoss` ONLY for permanent unlocks (custom purchases ≥ 17, magic resource ≥ 37).
- **Mode locks must never outlive an exception** — a stranded lock makes `CanSwap()` false and
  `RebirthAvailable()` never return, so the run cannot end. See LockManager.md,
  MoneyPitManager.md, YggdrasilManager.md.
- **Ask the owning module, don't string-match its output** (e.g. `BloodPlanner.BloodMatters()`
  instead of grepping token lists).
- **Game-truth first**: prefer the game's own difficulty-aware methods over reimplemented formulas;
  where a formula IS ported, the doc names the decomp source.

## Domain knowledge

- **`docs/NGU-KNOWLEDGE.md`** — the source of truth for game strategy (chapter progression, ratios, spend orders, stage detection map). Read it before touching advisor heuristics.
- **`docs/AUGMENTS.md`** — the augment efficiency math (boost/cost formulas, tier crossovers, energy split, gold vs. energy limits). Read before touching `BestAug`/`AugmentBP`.
- **`docs/ITEM-IDS.md`** — item id → name lookup for reading the id-keyed drop tables. **Ids 1–39 are BOOSTS, not gear** (13 value tiers × Power/Toughness/Special) — the `Normal = true` rolls in `GearFarmAdvisor.Table` are boost drops, filtered out of gear verdicts at runtime. Gear starts at id 40. Regenerate with `build/gen-item-ids.sh`.
- **`external/`** — reference material (not part of the build): clones of the community guide, the
  web Gear Optimizer (the oracle for gear scoring math), and spreadsheet exports. The inventory and
  the live links live in the `ngu-references` skill — invoke it when you actually need them.

Do not search inside `external/`, `bin/`, `obj/`, `dist/` unless the task is specifically about that reference material.
