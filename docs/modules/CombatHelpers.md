# CombatHelpers (`Managers/CombatHelpers.cs`)

Static facade over the game's combat move objects — the vocabulary `CombatAI`/`CombatManager`
build on. Pure reads + `doMove()` calls; no strategy here.

## Semantics worth knowing

- **Unlocked vs Ready vs Available vs Active** are four different things:
  - *Unlocked* = training thresholds (game-truth: attack/defense training index 0..4 at
    5000/10000/15000/20000/25000; Paralyze via challenge reward, HyperRegen via
    `settings.hasHyperRegen`, BeastMode via `hasBeastMode()`, MegaBuff = wish 8 + UltBuff,
    OhShit = wish 58 + Paralyze + Heal + HyperRegen, Move69 usable 69×).
  - *Ready* = the game button is interactable right now.
  - *Available* = ready OR (unlocked AND not `_pc.*Disabled`) — "will come off cooldown", used
    for rotation planning.
  - *Active* = the buff/stance is currently running (`_pc.*Time >= 0`, `isParrying`, …).
- `Cast*` methods are check-and-fire (`Ready` → `doMove()`), returning whether they cast.
  `CastParalyze(useOhShitInstead)` prefers OhShit when asked.
- **Global cooldown**: `BaseGlobalCooldown()` = 0.8 s with Red Liquid maxed
  (`itemList.redLiquidComplete`), else 1.0 s.
- Cooldown remaining = game's `*Cooldown()` minus the move's private timer field — read via the
  `GetFieldValue` reflection extension (field names are decomp-sourced strings; a game update
  renaming them breaks silently → check here first if combat stalls after a patch).
- `MegaBuffCooldown(total: true)` = max over its three prerequisite buffs — mega buff needs
  Def/Off/Ult buff off cooldown too.
- `HasFullHP()` treats ≥ 99 % as full (max HP grows continuously via BEARd/AT).

## Mode flags

`IsCurrentlyGoldSniping / Questing / Adventuring / FightingTitan` are plain mutable statics set
by the routing code (SnipeZone / QuestManager / CombatManager) so cross-module checks don't
re-derive intent. They are NOT game state — stale values after an exception are possible; treat
as hints.
