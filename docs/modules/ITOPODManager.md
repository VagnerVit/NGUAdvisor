# ITOPODManager + ItopodConstants (`Managers/ITOPODManager.cs`, `ItopodConstants.cs`)

ITOPOD (zone 1000) is handled entirely here — `CombatManager.DoZone` explicitly defers.
Two modes: **Farm** (sit on the optimal one-shot floor) and **Push** (climb
`itopodStart..itopodEnd`, full CombatAI mode 2). `UpdateMaxFloor()` (periodic) decides the mode;
`Update()` (per tick) runs zone check + quick actions.

## Game-truth constants (`ItopodConstants`)

Floor difficulty: mob HP grows ~5 %/floor. Best one-shot floor =
`log_1.05(attack × attackPower / normalizer)`:

- `FloorHpNormalizer = 771.375` (idle/regular/strong/ultimate branches)
- `PiercingHpNormalizer = 769.25` (piercing branch only)
- `FloorGrowthBase = 1.05`

Sourced from the game's ITOPOD mob-HP scaling; NOT yet read from a live game field — if the game
patches the scaling, this is the one-line update point.

## Optimize modes (`Settings.ITOPODOptimizeMode`)

| Mode | Behavior |
|---|---|
| 0 | No floor optimization |
| 1 | "Lazy shifter": best floor for regular/idle attack; defers to the game's own Lazy ITOPOD when bought+on |
| 2 | Best floor for the strongest attack available within the respawn window, buff-aware; maxFloor rounded down to 10s |
| 3 | AP-cycle optimizer (see below); maxFloor rounded down to 50s |

Mode 2/3 plan a buff queue (`PlanBuffs` → `nextBuffs`) and re-optimize the floor after every kill
(`OptimizeFloor` runs between fights only). The floor picked accounts for buffs about to be cast
(`multi` from queue head + active buff durations vs remaining respawn).

### Mode 3 — the AP-kill cycle

ITOPOD awards AP every N kills (`lootDrop.killsUntilAP`); tiers are 50-floor bands
(`lootDrop.itopodTier`). Mode 3 farms at the regular-attack default floor, and when the AP kill
is 3 kills away, schedules a buff burst (`Buff.None, None, <buff>` = "two plain kills, then the
buffed one") to one-shot a HIGHER tier floor exactly on the AP kill — then returns to the default
floor. `threshold = 1.05^maxFloor / maxAttack` picks the cheapest sufficient combo in escalating
order: Charge → OffBuff(×1.2) → UltBuff(×1.3) → combinations → MegaBuff(×1.2·1.2·1.3) → Charge ×
combos (`chargePower()` is the game read). Floors ≥ 1550 or tier ≥ 20 with fast respawn skip the
dance.

## Push mode

Entered when `maxFloor > highestItopodLevel − 1` and `ITOPODAutoPush`: sets range
`(highest−1, maxFloor+1)` and fights with CombatAI mode 2 (full defense). **A death during push
(itopodLevel dropped below highest−1) permanently flips `Settings.ITOPODAutoPush` off** — the
advisor won't retry a push it died in.

## Gotchas

- `UpdateMaxFloor` force-disables the game's `lazyITOPODOn` in modes ≥ 1 (they'd fight over the
  floor) — mode 1 is the exception that respects it.
- Beast-mode enable in idle combat briefly toggles `autoattacking` off/on around the cast
  (`CheckBeastMode`) — the game blocks the cast while auto-attacking.
- `haveCast` gates Fight() so exactly one buff cast happens per respawn window before attacking.
- Farm-mode fighting uses CombatAI mode 4 (one-shot: regular attack spam); Move 69 is weaved
  between fights when not pushing.
