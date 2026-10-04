# StateExport (`Managers/StateExport.cs`)

Dumps the live game state to one readable text file —
`%UserProfile%\AppData\LocalLow\NGUAdvisor\state-export.txt` — via the **EXPORT STATE** chip on the
LOGS page, or by dropping a **`state-export.request`** file beside it.

## The file request (2026-09-12)

```bash
touch ~/AppData/LocalLow/NGUAdvisor/state-export.request   # wait for it to vanish, then read the dump
```

`StateExport.Requested()` is polled from `Main.Update()`, inside the **same once-a-second budget as
the unload request** (one `File.Exists` per second, not two per frame), and sets the same pending
flag the chip does — so the export itself still runs through the one drain, on the Unity thread.
The request file is deleted as the acknowledgement, exactly as `Loader.UnloadRequested` does it, so
a waiting script can watch it vanish and then read `state-export.txt`.

**Why it was added:** the dump was reachable only by a mouse click, so nothing outside the game could
ask "what does the advisor actually see right now". Every check had to be inferred from log lines,
which is how a wrong chapter went unnoticed long enough to disable the whole quirk plan
(ProgressionAnalyzer.md). The window is Mono WinForms — one HWND, no child handles — so clicking the
chip programmatically is not an option either (see the deploying-advisor skill).

## Why it exists: the names are not in the save

Perk, quirk and fruit labels live in the Unity **scene** (`ItopodPerkController.perkName`,
`BeastQuestPerkController.quirkName`, `YggdrasilController.fruitName`), not in code and not in
`NGUSaveSteam.txt`. An external save reader can therefore only ever print `perk 93 = 1`, with no way
to learn what perk 93 IS — which is exactly where the save-reading approach ran out (2026-08-12).

The advisor is already inside the process with `Character` live, so it is the only thing that can
answer. Everything else in the dump (levels, tiers, balances, allocations) is a convenience; the
names are the reason.

## Main-thread rule

Every read is a live Unity object, so the WinForms chip calls `Main.RequestStateExport()` and
`Main.Update()` drains the flag and runs the write — the same request/drain pattern as
`RequestAllocationReload`. **Never call `Write()` or `Build()` from a WinForms handler or a
`FileSystemWatcher` callback.**

## Section guarding

Each section is wrapped individually and degrades to `(unavailable — <message>)`. A dump that stops
at the first unreadable system is worth far less than one carrying everything else — the point is to
have the numbers in hand. `Character == null` is the one early return.

## Reads worth knowing

- **NGU levels follow the track being leveled** (evil/sadistic/normal columns are separate fields),
  and each lane prints its **allocation** — a zero there is *why* a lane is not moving, the same
  fact `NGUAdvisors.Diagnose` reports.
- **Boss** prints `ZoneHelpers.CurrentHighestBoss` AND the raw `stats.highestBoss` beside it: they
  diverge on Evil and a state dump should show both (the repo's standing rule is that progression
  reads use the former).
- **Beards carry three level numbers** (decomp `Beard`): live `beardLevel`, `permLevel` surviving
  rebirth, and `bankedLevel` waiting to be claimed. Printing one would misread as "the beard is low"
  when the growth is merely banked.
- Digger levels are `curLevel`/`maxLevel`; a digger with `maxLevel == 0` was never unlocked and is
  skipped as noise.
- Digger and beard LABELS come from `OptimizationAdvisor.DiggerNames`/`BeardNames` (made `internal`
  for this) — a second copy would be free to drift from the ones the advice rows use.

Field names above were each verified against the decompiled `Assembly-CSharp.dll`; the first pass
guessed `Beard.level`, `GoldDigger.level`, `Adventure.highestBoss` and `Magic.totalCapMagic()`, and
all four were wrong.

- **Numbers are culture-invariant.** `Build()` swaps `CurrentThread.CurrentCulture` to Invariant for the
  duration (main thread, restored in a `finally`) so the file never reads `1553879,38` / `1,735E+011`.
- **GEAR section**: the gear chain and last `[GearDbg]` verdict AdvisorApply settled on
  (`AdvisorApply.LastGearChain` / `LastGearVerdict`, read-only; empty until the first pass after a
  load), the routing venue (`Main.ResolveIntentZone` + `FarmMode`), cube raw/softcap, and every worn item
  with level and boosts still needed to green.
- **Drop-chance saturation line** follows the routed zone (`Main.ResolveIntentZone`), not
  `SnipeZone`. For the pod it prints that boost rolls are a flat 14 % and drop chance does not apply
  (`ItopodRewards.BoostDropChance`, ItopodFarmAdvisor.md).

## Fields that exist because they were once invisible

- **`titans beaten`** — T5..T12 with the highest VERSION beaten for the versioned ones ("T6 v1" means
  v1 beaten, v2 not). These two reads gate the chapter and the guide's E:M ratio, and a
  `titan{N}Version` misread once made both invisible.
- **BEAST QUIRKS lists EVERY quirk, not just the owned ones** (index, level/max, cost, difficulty
  requirement). `SpendPlanner`'s plans match the game's lists BY NAME and those names live only in
  the Unity scene, so an owned-only dump could confirm what a step bought but never tell you what the
  steps you have not reached are CALLED — which is exactly what checking a plan against the guide
  needs. Four guide ch.4 quirks turned out to be missing from `QuirkPlan` and were only nameable once
  this printed them (SpendPlanner.md).
- **ITOPOD PERKS and YGGDRASIL FRUITS list every entry too** (2026-10-02); perks also carry the
  in-game description, the only in-process statement of what a perk does. The owned-only perk dump
  hid that the ch.4 plan was buying perks no guide list names (SpendPlanner.md).
- **QUESTS** prints the inputs of `BeastQuestController`'s own QP formula — bank, current quest and
  what it pays, base rewards, `questRewardFactor()`, maxed quest items, drop chance, seconds per item
  and the idle divider — so the quest strategy can be priced from game truth instead of the panel.
- **`EXP ... buying toward <phase>`** — which guide ratio the EXP walk is aiming at. It is derived
  from the chapter and the T6 version, both a step removed from anything else in the dump, so without
  it a wrong ratio shows up only as EXP going somewhere surprising.
