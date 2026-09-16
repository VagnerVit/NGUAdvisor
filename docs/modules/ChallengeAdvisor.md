# ChallengeAdvisor (`Managers/ChallengeAdvisor.cs` + `Managers/ChallengeGates.cs`)

Recommends **starting** a challenge — the gap nothing else filled. `LscAdvisor`/`BaseRebirth` can
enter LSC, and `ChallengeOverlay` reshapes the allocation once you are already inside one, but no
module ever said a challenge was worth running.

That silence was expensive. The advisor's own NGU math multiplies by **3** the moment Troll
Challenge has one completion (`NGUBP.cs:152`, which mirrors the game's NGU speed formula, and
`NGUAdvisors.cs:88`). A player at TC 0 therefore reads every projection already discounted to a
third, with nothing on screen saying why — and the same ladder gates the 8th blood ritual, the
Numbers fruit and the golden beard/digger.

## The table is not an opinion

Every row in `ChallengeGates.All` is a gate **this codebase or the decompiled game already reads
somewhere else**, and `Source` names that reader:

| gate | reward | source |
|---|---|---|
| Troll ×1 | ×3 NGU speed (energy + magic) | `NGUBP.cs:152`, `NGUAdvisors.cs:88` |
| No Augs ×1 | ×1.1 augment speed | `AugmentBP.cs:123`, `Extensions.cs:248` |
| Troll ×5 | unlocks the Numbers fruit | `SpendPlanner.cs:515` |
| Troll ×6 | unlocks the 8th ritual (Turn Yourself Inside Out) | `AllBloodMagicController.ritualsUnlocked` (decomp) |
| Troll ×7 | keeps the golden beard/digger | `BeardManager.cs:58`, `OptimizationAdvisor.cs:1101` |

**Ranking key is the game's own multiplier**, biggest first, then the nearest rung — not an
editorial ordering. Rewards the game expresses as an unlock rather than a rate carry `Multiplier = 0`
and are advised only once every numbered gate is met: a rate is a standing loss for every hour it
goes unclaimed, an unlock is not, and ordering two unlocks against each other would need a value
model nothing here has. Adding a row without a `Source` is how this file turns into the opinion
table it was written not to be.

## Rules

- **Advice only.** Entering a challenge is a rebirth; `Analyze()` has no `AutoKey` and nothing acts
  on it. (LSC is the one auto-entry, and it stays `LscAdvisor`'s.)
- **Severity 1, never 2**, whatever the multiplier. The row cannot be cleared by acting now — it asks
  for a rebirth — and a severity-2 row sorts to the top of a slot-limited dashboard, where it would
  sit for weeks pushing out rows the user can act on this minute.
- **Silent inside a challenge** (`ChallengeDetector.Current() != null`) — finish this one first.
- **An unreadable challenge is skipped, never guessed**: the completion read is per-challenge in its
  own try, and a key missing from the map drops those rungs out of the ranking entirely.
- Counts come from `allChallenges.<x>.completions()` — the SAME accessor the reward gates read, not
  `currentCompletions()` (per-difficulty, clamped), which would disagree with the gate it advises.
- `ChallengeGates` is Unity-free and unit-tested (`ChallengeGatesTests`), the same split that moved
  the titan tables out of `OptimizationAdvisor`.
