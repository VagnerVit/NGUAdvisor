# ZoneHelpers (`Managers/ZoneHelpers.cs`)

Zone/titan reference data + the titan spawn-snapshot machinery every swap decision reads.

## Game-truth tables

- `ZoneList` — zone index → display name (−1 Safe Zone … 45 THE TRAITOR).
- `ZoneUnlocks` — boss (effectiveBossID) required per zone, sourced 1:1 from decomp
  `AdventureController.constructDropdown`. **History:** zone 38 (ROCK LOBSTER, 826 — the second
  of two consecutive 826 entries) was once omitted, shifting every Sadistic threshold and pushing
  `TitanZones[13]` off the end (titan 14 unreachable). Duplicate boss values are LEGITIMATE —
  see `GetMaxReachableZone` below.
- `TitanZones` = {6,8,11,14,16,19,23,26,30,34,38,42,44,45}; `IsVersionedTitan` = index 5..11
  (T6–T12 have versions via `titan{N}Version` reflection fields).
- LIMITATION: zone 45 also needs the live flag `adventure.ratTitanDefeated`; the static table
  only has the 902 threshold, so `GetMaxReachableZone` can overcount by one zone at Sadistic
  endgame.

## `CurrentHighestBoss(c)` — THE boss read (Evil checklist rule 1)

`highestBoss` is Normal's all-time max and does NOT reset on Evil/Sadistic entry (user-caught:
Evil boss 24 read as Normal 301 → every stage/climb decision acted late-game). Difficulty-aware:
sadistic → `highestSadisticBoss`, evil ("Hard" internally) → `highestHardBoss`, else
`highestBoss`. Use THIS for progression gating; raw `highestBoss` only for permanent unlocks.

## `GetMaxReachableZone`

Linear scan, unlocked = `ZoneUnlocks[i] <= effectiveBossID()`. **Do not "optimize" back to
BinarySearch**: the table has duplicate requirements (58,58 / 66,66 / 826,826…) and
`Array.BinarySearch` returns an ARBITRARY duplicate — the long-standing upstream bug where riddle
titans didn't count as reachable until one boss kill past their unlock.

## Titan snapshot machinery

`RefreshTitanSnapshots()` (called from the automation loop) maintains per-titan
`TitanSnapshot`s and one materialized `TitanSnapshotSummary`:

- "Spawning soon" = `TimeTillTitanSpawn < 20 s` (spawn.totalseconds counts UP to spawnTime and
  holds there until the kill resets it).
- **10-minute stall watchdog**: a titan still spawnable after 10 min means stats can't kill it —
  first try REDUCING the titan's version to the highest AK-able one (`SetTitanVersion`), else
  persistently disable it as a swap/gold target (writes Settings!). Timestamps use **UtcNow, not
  Now** — a DST jump once fired this watchdog spuriously, and its consequence is destructive
  (persisted version downgrade / target removal).
- Kill detection (timestamp → null) with an active gold loadout sets `TitanMoneyDone[i]` and
  banks the AK version (`TitanGoldVersionBanked`) so auto titan-gold can re-bank when a higher
  version becomes killable.
- The summary's three flags (`AnySpawningSoon`, `RunTitanLoadout`, `RunGoldLoadout`) are computed
  ONCE on set, not per read — they sit on the per-frame routing path (SnipeZone → LockManager).

## Autokill checks

`AutokillAvailable(titanIndex, version)`: T1–T5 are hardcoded stat thresholds from the game
(incl. item 135 maxed for T4, 3× boss5Kills for T5); T6–T12 call the game's own
`autokillTitan{N}V{v}Achieved`; T13/T14 never AK (returns false).

`TitanEnemyName` returns the game's own enemy entry (user-reported mislabel fix: WALDERP has no
versions — versioned titans' entries found by V-suffix, unversioned use the zone's last slot,
the same slot the game's autokill path uses).
