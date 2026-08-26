# ZoneCadence + BoostValueMath + BoostSinks

The farm-rate substrate shared by `BoostFarmAdvisor` and `GearFarmAdvisor`: how fast a zone kills
(`ZoneCadence`), what a boost drop is worth (`BoostSinks`), and the Unity-free math both sit on
(`BoostValueMath`, linked into `tests/NGUAdvisor.Tests`).

These exist because both farm advisors used to assume "kill cadence is ~equal across one-shottable
zones, ~800 kills/h, 77% normal / 10% boss". Every part of that sentence was wrong in a way that
changed the ranking.

## The cadence rule that drives everything (decomp game-truth)

`AdventureController`, spawn branch:

```csharp
currentEnemy = spawnEnemy(character.adventure.zone);
respawnTimer = 0f;
idleAttackTimer = 0f;      // <-- zeroed on EVERY spawn
fightInProgress = !fightInProgress;
```

and the idle swing fires on `idleAttackTimer >= character.adventure.attackSpeed`, where the timer only
advances **while a fight is in progress** (`if (autoattacking && playerController.moveCheck())`).
Meanwhile `PlayerController.moveTimer` counts down unconditionally in `Update()`.

Consequence, and the single most valuable fact in this module:

| mode | seconds per kill |
|---|---|
| Idle | `respawn + hits × attackSpeed` |
| Manual | `max(respawn, gcd) + (hits − 1) × gcd` |

Idle pays a full `attackSpeed` of spawn latency on every kill; a manual mode lands the opening swing on
the spawn frame because the advisor runs in `LateUpdate` with `moveTimer` already at zero. Manual is
therefore **never slower and up to 2× faster** (the ceiling, hit when `respawn ≤ gcd`).

`FastestMode(zone)` is that comparison for ONE zone — the fastest killable *and* survivable of the
same Idle/Offensive pair the farm advisors rank, on `SecondsPerSpawn`, or −1 when neither mode has a
usable estimate. It exists so a layer that parks the character without a farm verdict (the gear hunt
in `AdvisorApply.ApplyZones`) can still pick a MEASURED mode instead of inheriting one.

`attackSpeed` and the global cooldown are the same number, always: `AllItemListController` calls
`adventure.setFasterIdleAttack()` (1 → 0.8) and sets `itemList.redLiquidComplete` (which `usedMove()`
reads for the 1 → 0.8 gcd) from the **same** maxxed-Mysterious-Red-Liquid branch. Do not model them as
independent.

### Two damage numbers, two jobs

Do not collapse these — they answer different questions and were conflated once already:

| | damage used | why |
|---|---|---|
| "do we one-shot this?" | **regular attack**, **worst** roll (×0.8) | `CombatAI.CombatAttacks` checks the regular attack first and only escalates, and a guaranteed kill must not rest on a lucky roll. Feeds the gates and the entry-HP relaxation. |
| "how long does this fight take?" | **sustained rotation**, **mean** roll (×1.0) | A multi-swing fight really does get Strong/Piercing/Ultimate as their cooldowns come up. Feeds the farm rates only. |

Piercing's multiplier is **`strongAttackPower()` (= `strongAttackMulti`)**, not `pierceAttackPower()`:
`PlayerController.pierceAttack()` reads `adventureController.strongAttackMulti`, so
`Character.pierceAttackPower()` (`pierceAttackMulti`) is dead code as far as damage is concerned.
`ZoneCadence` used the dead one until this was traced, which meant zones and ITOPOD were compared with
two different piercing multipliers.

`SustainedDamagePerSlot` models the rotation as: each big move fires at most once per its own cooldown,
one move per global cooldown, every remaining slot a regular attack — which is what `CombatAI`'s
gain/loss scheduler converges to, without reimplementing its scheduler. Piercing is priced against
`defense/3`, the others against `defense/2` (`CombatAI`'s `oneShotDamage`).

`ExpectedHits` is **fractional on purpose**. The earlier `ceil(maxHP / (0.8 × mean))` overstated
multi-swing kill times by up to 25 % — a 10-swing enemy came out as 13 — which is exactly the bias that
would keep the newly-eligible multi-swing zones losing. Three regimes on `r = maxHP / net`:
`r ≤ 0.8` → 1 (exact); `0.8 < r ≤ 1.2` → `2 − (1.2−r)/0.4` (exact at both ends: 1 and 2, since a failed
first swing leaves at most 0.4× a swing); `r > 1.2` → `max(2, r + 0.5)` (renewal: the overshoot past
`maxHP` averages half a swing).

The one-shot FLAGS on `Estimate` are keyed off the guaranteed number, never off `hits` — the rotation
can average to a single swing while the regular attack still needs luck.

### Skipped spawns are a cost, not an absence

`CombatManager.CheckEnemy()` retreats to the Safe Zone instead of fighting a blacklisted sprite id or,
under `SnipeBossOnly`, any non-boss. Those spawns are modelled as a round trip (`respawn + 2 × swing`)
that yields nothing.

That makes one interaction explicit which used to be invisible: **`SnipeBossOnly` zeroes boost farming
outright**, because boost rolls only fire on `enemyType.normal`. The verdict says so rather than
reporting a mysteriously small rate. ITOPOD is unaffected (`CheckEnemy` skips the bossOnly branch for
zone ≥ 1000), so routing there is the correct answer while that toggle is on.

`AI.paralyze` costs 2 s but only after **two** landed enemy attacks (`EnemyAI.paralyzeAI`:
`paralyzeEffect` 1 warns, 2 freezes), and non-titan zones delay the enemy's first strike by +50% of its
attack rate. A kill that ends inside that window pays nothing — which is why one-shot farming never
sees a paralyzer.

## Enemy facts are readable, not guessable

`AdventureController.createEnemyTable()` builds `enemyList` in **code**, not serialized scene data, and
`public List<List<Enemy>> enemyList` never mutates afterwards. So name / `maxHP` / `defense` / `regen` /
`attackRate` / `enemyType` / `AI` are all live-readable, and `ZoneCadence.Facts()` caches them for the
process lifetime.

`spawnEnemy` picks **uniformly** from `enemyList[zone]`, which makes the type shares exact:

| | normal share |
|---|---|
| hardcoded constant we used to use | 0.77 |
| real range | 0.7143 (Ancient Battlefield, 5/7) — 0.8125 (Cave of Many Things, 13/16) |

Boss share is `1 / spawnTableSize`, not the old flat 0.10.

Enemy `regen` is real (`currentEnemy.curHP += regen * Time.deltaTime` while fighting), so
`BoostValueMath.HitsToKill` returns `+inf` when damage per swing cannot outpace `regen × cadence`.
That is the honest "this zone is not farmable" answer and it replaces a magnitude threshold.

## Why OPower is the wrong gate for boost farming

`ZoneStatHelper`'s OPower is calibrated on the zone **BOSS**:

```
zone 0: OPower 129.5 == bossHP 100 / 0.8 + bossDef 9 / 2
zone 1: OPower 194.0 == bossHP 150 / 0.8 + bossDef 13 / 2
zone 3: OPower 3811  == bossHP 3000 / 0.8 + bossDef 122 / 2
```

(the `/0.8` is the worst `Random.Range(0.8f, 1.2f)` roll, i.e. a *guaranteed* one-shot).

But **boost rolls fire only inside the `enemyType.normal` branch of `LootDrop.zone{N}Drop` — bosses drop
no boosts at all.** Gating boost farming on one-shotting the boss demanded 1.79× more attack than zone 0
actually needs (72.25 for the hardest normal vs 129.5 for the boss). Boost farming therefore gates on
normals via `ZoneCadence`, and OPower is left to the callers for which the boss genuinely is the
question.

### The OPower column is three different quantities

Measured against the live enemy table, `ZoneStatHelper`'s shipped OPower values fall into three regimes
with sharp boundaries — it was filled in three sittings and never reconciled:

| zones | what OPower actually is | error |
|---|---|---|
| 0–9 | `hardestHP / 0.8 + def/2` — **guaranteed** one-shot (worst `Random.Range(0.8f,1.2f)` roll) | correct, ratio 1.0000 across 7 zones |
| 10–28 | `hardestHP / 1.2 + def/2` — the **luckiest-roll** one-shot | exactly 1.5× too low, ratio 1.0000 across 12 zones |
| 29, 31–41 | ≈ `IPower` (the idle-**survival** column) — not a one-shot number at all | 10–18× too low; zone 29 is 80× IPower, a stray digit |

The middle regime is subtle (at that attack you one-shot only on a maximum roll), the third is dangerous:
in The Rad-Lands the table claims a one-shot at ~1/27 of the real requirement.

So nothing gates on the column any more. `ZoneCadence.RawOneShotPower(zone)` computes it live —
`max over spawns of (maxHP / (0.8 × multiplier) + defense/2)`, derived from
`(attack − defense/2) × multiplier × roll ≥ maxHP` — and `ZoneStatHelper.OneShotPower(zone)` prefers it,
falling back to the column only when the enemy table cannot be read. `ZoneStats.FightType` therefore
takes the one-shot power as a **parameter** instead of reading `OPower`; do not "simplify" that back.

`OPower` stays in `ZoneStats` and in `zoneOverride.json` for schema compatibility and as that fallback —
a user override still wins, which is the point of the file.

Consumers now on the live value: `ZoneStatHelper.ZoneFightType` / `GetBestZone`, `AtHourPlanner`'s
idle-farm ETA, and `CombatManager.ZoneEntryHpThreshold` (which additionally goes through
`OneShotsEverySpawn`, so a manual mode gets credit for `regAttackMulti` and the offensive buffs while
still measuring against the beast-mode-free `EffectiveAdvAttack()` — beast triples incoming damage, so
leaning on it to justify a relaxed entry-HP threshold would be backwards).

## What a boost drop is worth (`BoostSinks`)

**Which items count as sinks (`TargetIds`).** Equipped gear (`LoadoutManager.CurrentGearIds`) plus
`Settings.PriorityBoosts`, **minus `Settings.BoostBlacklist`**. Equipped gear is taken independently of
the priority list on purpose, which is why the blacklist has to be subtracted explicitly: a blacklisted
item receives no boost, so its headroom is a channel that does not exist. Added 2026-08-26 with the
blacklist restore, and it fixes two things at once — this set feeds both `ValueOfDrop` (the farm-rate
price of a drop) and `BestType` (TransformManager's auto-transform pick, which otherwise keeps making
Special for a blacklisted item that wants Special and has nowhere to put it).

A flat "tier value" over-priced high tiers badly. `Equipment.boostEquip` decides the real value:

- **Power / Toughness** land on one gear channel and are then CLAMPED at
  `floor(cap × (1 + level/100))` — **the overflow is destroyed**. So a boost is worth
  `min(tierValue, best SINGLE item's headroom)`, never the loadout total. A 10K boost dropped when the
  hungriest item needs 300 is worth 300.
- **Special** CASCADES spec1 → spec2 → spec3 on one item, so its usable headroom is the **sum** of the
  three slots (which is what `GetNeededBoosts().special` already returns).
- The **Infinity Cube** is a SOFT sink, not a capped one: `InventoryController.cubePower()` returns
  `softcap + sqrt(raw − softcap)` past the cap. It never becomes worthless, only sharply diminishing.
  The old binary "cube under softcap" demand gate threw that gradient away.
- A boost goes wherever it helps most, hence `Delivered` takes the `max` of the gear and cube deliveries
  (both are adventure-stat points, so they are directly comparable).
- **Recycling**: `InventoryController.boostRecycle` returns the next tier DOWN with probability
  `Character.totalRecycleBonus()`, recursively, never below tier 1 (ids 1/14/27 excluded). The returned
  boost keeps its type, so the chain re-prices against the *same* channel — a tight channel truncates
  the whole chain, which is why `WithRecycling` takes a per-tier pricing callback instead of a scalar.

Drop type is uniform 1/3 over Power / Toughness / Special (`Random.Range(1, 4)` over the three item ids
of the tier), so `ValueOfDrop` averages the three channel prices.

## Boost ladder (verified, no longer reconstructed)

`ItemNameDesc.itemName[]`: id 1 = "Power Boost 1" … id 13 = "Power Boost 10K", id 14 = "Toughness
Boost 1", id 26 = "Defense boost 10K", id 27 = "Special Boost 1", id 39 = "Special boost 10K",
id 40 = "Crappy Helmet". Ladder `{1,2,5,10,20,50,100,200,500,1000,2000,5000,10000}`, `id = tier` for
Power, `+13` Toughness, `+26` Special. See `docs/ITEM-IDS.md`.

## Invariants

- `ZoneCadence.Facts` caches per zone forever — correct only because `createEnemyTable()` runs once and
  nothing rescales `enemyList` (ITOPOD's `powerUp()` clones, and ITOPOD is not in `enemyList`).
- `BoostValueMath` must stay Unity-free: it is linked into the test project, which builds without an NGU
  install. No `Mathf`, no `Main`, no `Character`.
- `SwingSeconds`/`DamagePerSwing` are the only place the idle-vs-manual difference is encoded. Both farm
  advisors and their mode recommendations derive from them; do not re-derive a cadence locally.
- Damage uses the **worst** roll (×0.8), matching `CombatAI`'s own one-shot safety factor, so
  "one-shot" here means guaranteed, not lucky.
- `Survivable` short-circuits to true when a manual mode one-shots every normal (nothing ever gets a
  turn); otherwise it falls back to the zone table's `IPower`/`IToughness`.
