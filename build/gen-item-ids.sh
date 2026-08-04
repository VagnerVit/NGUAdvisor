#!/usr/bin/env bash
# Generates docs/ITEM-IDS.md: item id -> name lookup.
# Sources: boost ladder from Managers/BoostFarmAdvisor.cs, gear from external/gear-optimizer/data/Items.js
set -euo pipefail

REPO="C:/Users/vvagner/Desktop/NGUAdvisor-Reiryn"
OUT="$REPO/docs/ITEM-IDS.md"
ITEMS="$REPO/external/gear-optimizer/data/Items.js"

BOOST_VALUES=(1 2 5 10 20 50 100 200 500 1000 2000 5000 10000)

{
  cat <<'HEADER'
# Item ID -> name lookup

Two disjoint id ranges, two different sources. Neither is in the main project as a name table —
the game supplies names at runtime via `itemInfo.itemName[id]` (`Extensions.cs:144`), so this doc
exists to read the id-keyed drop tables without the game running.

Regenerate with `build/gen-item-ids.sh`.

## 1-39: boosts (NOT gear)

Reconstructed from the boost ladder in `Managers/BoostFarmAdvisor.cs:79` plus the id anchors in the
comment at `:54-55` ("id 8 = Power Boost 200, id 9 = 500", "id stays 13/26/39" for the 10K ceiling):
13 value tiers x 3 boost types, `id = tierIndex + 1` for Power, `+13` Toughness, `+26` Special.

Cross-checked against `GearFarmAdvisor`'s Normal-branch rolls, which carry identical chance and cap
values per zone: Forest (id 1/2 = value 1/2), The Sky (id 3 = 5), The 2D Universe (id 4/5 = 10/20),
Mega Lands (id 6/7 = 50/100).

These ids appear inside `GearFarmAdvisor.Table` as `Normal = true` rolls with `Span = 3` -- the span
IS the three boost types. They are filtered out of gear farming at runtime by
`itemInfo.type[id] <= 5` (`GearFarmAdvisor.cs:296`), so they never count toward a gear verdict.

| value | Power | Toughness | Special |
|---|---|---|---|
HEADER

  for i in "${!BOOST_VALUES[@]}"; do
    v="${BOOST_VALUES[$i]}"
    p=$((i + 1))
    printf '| %s | %d | %d | %d |\n' "$v" "$p" "$((p + 13))" "$((p + 26))"
  done

  cat <<'MID'

## 40+: gear

Extracted verbatim from `external/gear-optimizer/data/Items.js` (`new Item(id, 'name', Slot, SetName, ...)`).
That file starts at id 40 and holds 369 items -- it models only gear worth optimizing, which is why
the boost range above is absent from it entirely.

Ids referenced by `GearFarmAdvisor.Table` but missing here (66, 339, and the titan/quest specials the
table deliberately omits) are not optimizable equipment, so the reference never listed them.

Flat and strictly id-ordered, because that is the lookup direction this doc is for. Sets are NOT
contiguous in id space -- most sets' armour sits in one block while their accessory lands in the 43x
range or later, so a set-grouped layout would list the same set twice.

| id | name | slot | set |
|---|---|---|---|
MID

  grep -oE "new Item\(([0-9]+), '([^']*)', Slot\.([A-Z_]+), SetName\.([A-Za-z0-9_]+)" "$ITEMS" \
    | sed -E "s/new Item\(([0-9]+), '([^']*)', Slot\.([A-Z_]+), SetName\.([A-Za-z0-9_]+)/\1|\2|\3|\4/" \
    | sort -t'|' -k1 -n \
    | awk -F'|' '{ printf "| %s | %s | %s | %s |\n", $1, $2, tolower($3), $4 }'
} > "$OUT"

echo "wrote $OUT ($(wc -l < "$OUT") lines)"
