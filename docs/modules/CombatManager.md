# CombatManager (`Managers/CombatManager.cs`)

Zone-level combat orchestration: moving between zones, heal/buff parking in the Safe Zone,
enemy filtering, and dispatching per-fight decisions to `CombatAI`. Called from the routing loop
with the target zone (`DoZone`).

## Combat modes

Per context (`Settings.CombatMode` / `QuestCombatMode` / `TitanCombatMode`, chosen by the
`IsCurrently*` flags):

| Mode | Meaning |
|---|---|
| 0 | Idle attack (auto-attack toggle) — forced to 3 on Walderp/Godmother zones (they punish idle) |
| 1 | Snipe: full pre-cast (all cooldowns ready + Charge/Parry/Beast) before entering |
| 2 | Manual combat, enter at ≥ 0.8 HP |
| 3 | Manual, enter at ≥ 0.6 HP; Move 69 weaved between fights |
| 4 | One-shot: regular attack only + Beast Mode on |

## Recovery rules (each fixed a real stall)

- **`ZoneEntryHpThreshold(zone, fallback)`**: entry HP scales with what the zone can do to us —
  one-shottable (`attack > OPower`, beast-mode-free attack) needs only 0.2; idle-stats zone 0.6;
  unknown zones/titans keep the caller's strict threshold. The old unconditional full-heal parked
  the character in the Safe Zone for minutes after every titan fight.
- **`TryRecoveryRegen`**: cast Hyper Regen when remaining recovery ≥ 10 s of natural regen
  (regen ×10 with GRB set complete, else ×5). Shared by every heal-park.
- Mode 1 pre-cast deliberately EXCLUDES Heal/HyperRegen cooldowns — they're recovery moves, not
  part of the snipe burst; waiting out their long post-titan cooldowns was pure loss.

## Beast mode ownership

`DesiredBeastMode()` is the single truth for the current context: Adventuring →
`Settings.BeastMode`, Gold sniping → keep current state, Questing/Titan → their settings.
`CombatAI`'s enable check consults THIS — it used to force-cast beast mode in offensive mode,
silently overriding the titan advisor (user-reported death loop). Toggling waits in the Safe Zone
in manual mode until the button is pressable (`CheckBeastModeToggle`).

## Other behaviors

- **Titan spawn wait**: parked in Safe Zone until `TimeTillTitanSpawn` < respawn time − 0.05 s.
- **Buff scheduling between spawns** (`DoBuffs`): backward-scheduling — repeatedly takes the
  longest-cooldown buff and rewinds `remainingTime` by one global cooldown; casts only when the
  schedule says it's time. MegaBuff replaces Off+Ult when unlocked.
- **Enemy filtering** (`CheckEnemy`): blacklisted sprite IDs → retreat to Safe Zone; bossOnly
  (snipe/gold contexts) skips non-boss enemies outside titan zones.
- Kill logging: `"{enemy} killed in {t}s"` to combat.log once a fight ends (fightTimer ≥ 1 s).
- Gold-lock handshake: when a fight ends while `HasGoldLock()`, `IsCurrentlyGoldSniping` is
  cleared; a mismatch between the flag and the lock triggers `TryGoldDropSwap()` (restore path).
- ITOPOD (zone ≥ 1000) is explicitly NOT handled here — `ITOPODManager` owns it.
