---
name: ngu-references
description: Inventory of the reference material under external/ — ngu-guide, gear-optimizer, sheets — plus the live links. Use when consulting or comparing against the reference implementation of gear scoring, or when looking up strategy content outside docs/NGU-KNOWLEDGE.md.
---

# Reference material in `external/`

Not part of the build. `docs/NGU-KNOWLEDGE.md` remains the source of truth for game strategy;
these are the upstream sources it was distilled from.

- `external/ngu-guide/` — clone of [sayolove/ngu-guide](https://github.com/sayolove/ngu-guide); strategy content in `src/content/docs/en/` (chapters, mechanics, lists).
- `external/gear-optimizer/` — clone of [gmiclotte/gear-optimizer](https://github.com/gmiclotte/gear-optimizer); the reference implementation for gear scoring math: `src/Optimizer.js` (pareto filtering + knapsack), `src/NGU.js`, `src/Augment.js`, `src/Hack.js`, `src/Wish.js`, `src/util.js`, item stat data in `src/assets/ItemAux.js` / `Items.js`.
- `external/sheets/` — CSV/XLSX exports of the community Boost Almanac and PP/EXP-income spreadsheets.

Live references: [guide](https://sayolove.github.io/ngu-guide/en/intro/), [Gear Optimizer](https://gmiclotte.github.io/gear-optimizer/#/), [wiki](https://ngu-idle.fandom.com/wiki/NGU_Idle_Wiki) (Fandom — no clonable repo).

For how the native port relates to this reference, see `docs/modules/reference-gear-optimizer.md`
and `docs/modules/gear-optimizer-comparison.md`.
