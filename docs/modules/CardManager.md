# CardManager (`Managers/CardManager.cs`)

Card casting, trashing, mana-generator routing and deck sorting.

## Static setup

The static ctor precomputes a reference effect per `cardBonus`
(`generateCardEffect(bonus, 6, 1, 1, false)` — a rarity-6 baseline) into `_cardValues`, and builds
`sortList`: the seven sort keys (RARITY, TIER, COST, PROTECTED, CHANGE, VALUE, NORMALVALUE) each
with an `-ASC` variant, plus `TYPE:<bonus>` / `TYPE-ASC:<bonus>` for every bonus type. That array
is what the Cards panel offers; `Settings.CardSortOrder` holds the chosen priority chain.

## Trash rules (`WouldBeTrashed`, `TrashCards`)

Per bonus type the user configures a rarity floor (`Settings.CardRarities`) and a cost ceiling
(`CardCosts`); a card is trashed when `rarity <= floor` ("Rarity") or `sum(manaCosts) <= ceiling`
("Cost"). Protected cards are skipped unless `TrashProtectedCards`. **END cards** are special:
keep exactly one, and none at all if the END piece (item 492) is already owned. Iterated
backwards (trashing mutates the list). Every trash is logged to cards.log with its reason.

## Casting (`CastCards`)

Trashes first (so only castable cards remain). Walks the deck casting anything affordable;
`reservedMayo[]` marks a mana type as reserved once a card couldn't afford it, so later cards
can't drain the mana a queued card is waiting for. `isProtected` is cleared right before
`tryConsumeCard` (the game refuses protected consumption). Count is decremented on a successful
cast because the list shrinks. An END card is skipped (not logged, protection kept) while `inventoryController.freeSpace()` is false, as `tryConsumeCard` refuses it.

## Mana routing (`CheckManas`)

Sums the mana each castable card needs. If the total exceeds what's held (`needMayo`): switch
generators OFF for satisfied types and ON for deficient ones, respecting
`curManaToggleCount()/maxManaGenSize()`. If everything is affordable: run ONLY the generator with
the lowest `amount + progress` (fill the weakest). Protected cards are excluded from the demand
sum unless `CastProtectedCards`.

## Sort metrics

`GetCardChange` = `(currentBonus + effectAmount) / currentBonus` (relative gain);
`GetCardValue` = `(change − 1) / totalManaCost` (gain per mana); `GetCardNormalValue` = value ÷ the
type's reference value (cross-type comparable). `CompareCards` applies `Settings.CardSortOrder`
in order, falling back to card name.

## Tag advice (`TagAdvice`)

Card effect is `a*cost + 0.001*cost*tierFactor*variance` (decomp `CardsController.calculate*Effect`),
so effect per mana does not depend on cost: the trash rule's cost ceiling is value-blind, but it is a
user setting and stays as is. The advisor never sets tags (`cards.taggedBonuses` is the player's
choice, `maxTagSize()` slots, each tag biases `generateBonusType`). `TagAdvice()` ranks bonus types by
`generateCardEffect(type, generateCardTier(type), 1, 1, false) / cards.bonuses[type]` (relative gain
per mana at the perk-allowed tier). Types within 1 % share a bucket, ordered by the guide default
Adv / Hack / Wish. It is a within-formula ranking: it does not weigh what +x % NGU speed is worth against
+x % PP. Printed in state-export's CARDS section.
