# GameGearAdapter (`Managers/GameGearAdapter.cs`)

Phase 1b: bridges live game data into the scorer — reads a game `Equipment` into a
`GearScorer.Item` stat map. This is the layer that replaces the reference's static item database
(`external/gear-optimizer/src/assets/Items.js`) with game-truth values. **Main thread only**
(reads live game objects).

## Stat extraction (`BuildItem`)

Uses the item's MAX (boosted-to-cap) values scaled to its level — the advisor boosts gear to cap,
matching how the site optimizes for maxed gear:

- `CalcCap(cap, level) = floor(cap * (1 + level/100))` — same maxing formula as the game.
- **Power/Toughness** = `CalcCap(capAttack/capDefense, level)` (raw, base-0 stats).
- **Specs 1–3** = `getBonusFactor(CalcCap(speciCap, level), specType) * 100` — the game's own
  method applies the correct per-stat divisor, so percentages match the site's item DB exactly.
  The spec value is added to every stat its `specType` feeds (`GearObjectives.SpecTypeToStats`).

## Fixed pseudo-items (present in every loadout)

- **`BuildCubeItem`** — Infinity Cube: Power/Toughness from `cubePower()/cubeToughness()`;
  Drop/Gold/Hack/Wish from tier formulas ported verbatim from the reference's
  `cubeBaseItemData` (`util.js` ~line 256). If the game changes cube tiers, update BOTH the
  formula here and the comparison doc.
- **`BuildBaseItem`** — nude adventure Power/Toughness from
  `adventureAttackBonus()/adventureDefenseBonus()` (the site makes users type these in).

The reference models these as items id 1000/1001 in an `other` slot; native passes them as extra
list entries with `IsWeapon = false`.

## Notes

- The header comment "NOT YET INCLUDED: set bonuses" is stale — NGU has no gear set bonuses
  (see `GearOptimizer.cs` header); there is nothing to include.
- Values that don't map to a scored stat (specType 0/10/46) are silently dropped — correct, the
  site doesn't score them either.
- Validation: `GearOptimizerDiagnostic.Run()` dumps each equipped item's finished stat map to
  compare against the site's per-item numbers.
