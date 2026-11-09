# DriftProbe (`Managers/DriftProbe.cs`)

Once a minute (self-throttled, ticked from `Main.Update` beside `GrowthTracker.Tick`, main thread) it
compares the advisor's ports of game formulas with the game's own methods on the live character and
writes `DRIFT [name]: ours X vs game Y` to `debug.log`. A pass with no finding logs `DRIFT: clean`.
Relative tolerance 1e-4 (the game computes in float). A pass whose findings equal the previous
pass's logs nothing, so a standing drift is one line, not one per minute. Every group is wrapped
separately; a throw becomes a `probe read failed` finding and never reaches the tick.

## Pairs

| Name | Ours | Game |
|---|---|---|
| NGU Wandoos, Gold, Adventure alpha, Drop chance, Magic NGU, PP, Yggdrasil, EXP, Energy NGU | `NguGain.Bonus` multiplied over the tracks the rebirth difficulty enables, each on its own level and factor | `AllNGUController.<x>Bonus()` |
| Wandoos bonus | `WandoosAdvisor.BonusFor` (private, reached by reflection) at the live OS and levels | `wandoos98Controller.wandoosBonus()` |
| Hack N levelDivider | `HackMath.LevelDivider` | `hacksController.levelDivider(id)` |
| AT attack / defense multiplier | `AtMath.StatMultiplier` | `1 + adventurePowerBonus(0)` / `adventureToughnessBonus(0)` |
| Cube tier and loot/gold/hack/wish | `BoostValueMath.CubeTier`, `Cube*Bonus(tier)` | `infinityCubeTier()`, `cube*Bonus()` |
| Blood gold % / loot % | `BloodRouter.GoldPct` / `LootPct` | `(goldBonus() - 1) * 100`, same for loot |
| Zone max reachable | `ZoneHelpers.GetMaxReachableZone(false)` (the `ZoneUnlocks` boss table) | `zoneDropdown.options.Count - 2`, stepped off a titan zone as `ZoneForwardClick.goToMaxZone` does |

The zone pair reports only when the disagreement survives two passes: the dropdown rebuilds lazily
after a boss kill. `ZoneUnlocks` stays the table every module reads; the probe is what notices a
game patch that moves a threshold (or the rat-titan flag on zone 45, see ZoneHelpers).

## Skipped (no clean comparison)

- **Respawn**: `NGUAdvisors.RespawnRatio` is a before/after ratio on the current track only; the game's
  `respawnBonus()` is an absolute product over all tracks.
- **Number NGU**: `timeMulti` is folded into the factor by the caller; `numberBonus()` is double-valued.
- **Augments, Power alpha/beta, Time Machine**: double-valued getters with other scaling.

## Related changes it rides with

- `GameGearAdapter.BuildCubeItem` reads `cubeLootBonus/GoldBonus/HackBonus/WishBonus()` from the game.
- `BoostValueMath.CubeTier` computes in float like `infinityCubeTier()`, so a raw sum on a tier boundary
  lands on the same side; the probe checks it against the game on the live cube.
