# GameGearAdapter (`Managers/GameGearAdapter.cs`)

Phase 1b: bridges live game data into the scorer — reads a game `Equipment` into a
`GearScorer.Item` stat map. This is the layer that replaces the reference's static item database
(`external/gear-optimizer/src/assets/Items.js`) with game-truth values. **Main thread only**
(reads live game objects).

## Stat extraction (`BuildItem(equip, isWeapon, maxed)`)

`maxed` is a **required** parameter — there is no default, because getting it wrong is silent and
the two answers genuinely differ (see below).

| `maxed` | Power / Toughness | Spec *i* | Question it answers |
|---|---|---|---|
| `false` | `curAttack` / `curDefense` | `getBonusFactor(specNCur, type) * 100` | what the item gives **now** |
| `true` | `CalcCap(capAttack/capDefense, level)` | `getBonusFactor(CalcCap(specNCap, level), type) * 100` | what it gives **once boosted to cap** |

- `CalcCap(cap, level) = floor(cap * (1 + level/100))` — the game's own maxing formula.
- `getBonusFactor` applies the correct per-stat divisor either way, so the `maxed: true` percentages
  match the site's item DB exactly. The spec value is added to every stat its `specType` feeds
  (`GearObjectives.SpecTypeToStats`).

### Why the split exists (2026-09-07)

**Boosts and item level are independent.** Applying a boost raises `curAttack`/`curDefense`/
`specNCur` and **never** `level` (`docs/modules/TransformManager.md`, and the `boostEquip` clamp in
`docs/NGU-KNOWLEDGE.md`); level comes from merging and daycare, and a merge raises
`cap × (1 + level/100)` while leaving `cur` where it was. A level-100 item can therefore sit at a
fraction of its cap indefinitely, and an item on the boost blacklist never leaves it.

`BuildItem` used to return cap values unconditionally, on the assumption "the advisor boosts gear to
cap". The optimizer consequently ranked a freshly merged, barely-boosted item above a genuinely
maxed one — **and equipped it**, dropping the character's live stats. User-reported.

### Which valuation each caller wants

| Caller | `maxed` | Why |
|---|---|---|
| `GearOptimizer.BuildPools` (default of `Optimize`/`OptimizeIds`) | `false` | it EQUIPS the result |
| `GearOptimizer.CurrentScore` | `false` | it is the other side of AdvisorApply's re-equip bar |
| `GearHunter.OwnedAccessories` | `false` | it equips, and "best of two copies" is the boost question |
| `InventoryAdvisor.Compute` (KEEP/TRASH + `AutoBoostPriority`) | `true` | a verdict is about future value; the boost list exists to fill exactly those items |
| `GearOptimizerDiagnostic` | both | `MAXED` is the site oracle, `NOW` is live behaviour |

Anything new that **wears** gear takes `false`; anything that decides what to **keep, boost or hunt**
takes `true`.

`BuildItemAtLevel(equip, isWeapon, level)` is the `maxed: true` valuation at a hypothetical level — the
diagnostic's level-debt column only. Nothing that decides gear may call it.

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
