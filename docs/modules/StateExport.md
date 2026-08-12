# StateExport (`Managers/StateExport.cs`)

Dumps the live game state to one readable text file —
`%UserProfile%\AppData\LocalLow\NGUAdvisor\state-export.txt` — via the **EXPORT STATE** chip on the
LOGS page.

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
