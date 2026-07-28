# Small managers: BeardManager · CookingManager · ChallengeDetector

## BeardManager (`Managers/BeardManager.cs`)

Beard set executor. Two stashes (`_savedBeards` for lock save/restore, `_tempBeards` for the F8
quick swap). Mode presets: Titan {5,1,6}, Ygg {6}, Pit {6}. `EquipBeards(int[])`:

- No-op when the beards button isn't interactable; empty array = clear all.
- **Golden Beard (6) special-case**: with Troll ≥ 7 (Golden unlocked) and beard 6 both requested
  and active, the OTHER beards are deactivated individually instead of `clearActiveBeards()` —
  clearing would drop Golden's accumulated bonus. Without Troll ≥ 7, beard 6 is stripped from the
  request. Returns whether EVERYTHING requested fit (`capBeards` truncation → false).

## CookingManager (`Managers/CookingManager.cs`)

Runs when `cookTimer >= eatRate()` (a dish is ready). If current score < optimal: per ingredient
pair (1–4), brute-force the two ingredient levels (0..maxIngredientLevel²) maximizing
localScore+pairedScore — writes `curLevel` directly. Then optionally acquires the Cooking lock to
equip the cooking loadout before `consumeDish()`; a failed lock waits a cycle (dish is not
consumed without the gear). The trailing `HasCookingLock → TryCookingSwap` is the restore path.

## ChallengeDetector (`Managers/ChallengeDetector.cs`)

`Current()` → active challenge code or null. Codes match the profile "Challenges" vocabulary
(BaseRebirth.RCTarget): BASIC, NOAUG, 24HR, 100LC, NOEC, TC, NORB, LSC, BLIND, NONGU, NOTM.
Reads `challenges.<x>.inChallenge` flags (main thread, guarded). Check order is
most-restrictive-first; only one challenge is active at a time.

`DefaultGear(code)` — built-in gear objective when the profile has no challenge-specific gear:
almost everything → "Adventure" + TopRespawn; NOTM → "Gold Drops" (no TM to make gold); NOEC →
null (no gear allowed); 24HR → null (normal timeline fine). Null = fall through to the profile.
