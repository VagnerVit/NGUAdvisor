# ProfileModel + ProfileValidator (`Managers/ProfileModel.cs`, `ProfileValidator.cs`)

Editable in-memory model of a profile's `Breakpoints`, used by the Profile Editor (F9). Parses with
`SimpleJSON` and re-serializes to clean indented JSON. **Zero UI/game dependencies** — the
load→edit→save round-trip is unit-tested (`ProfileModelRoundTripTests`,
`ProfileModelTimeParsingTests`).

## SAFETY MODEL — pass through everything you don't model

Only the systems the editor actually edits are typed data. EVERY other system (Gear, Diggers,
Beards, Wandoos, NGUDiff, Consumables, Rebirth, Challenges, and anything unknown) is passed through
**VERBATIM** so a round-trip can never lose or corrupt it. New systems get modeled one at a time,
each re-verified by the round-trip test.

**Key ordering** (top-level `_systemOrder` and nested key order) is preserved by re-emitting in
SimpleJSON's load enumeration order. ⚠️ `JSONObject` is backed by a plain
`Dictionary<string, JSONNode>`, whose enumeration order is NOT a contract — it equals file order
here only because the load→edit→save path never removes or clears keys (a never-shrunk Dictionary
enumerates in insertion order on Mono/.NET Framework). **If SimpleJSON is ever swapped for a
hash-randomizing map, back JSONObject with an insertion-ordered structure.**

## "GUI owns the file" — comment dropping

Within a MODELED breakpoint, human-comment fields are dropped on save. The decision is made purely
by KEY NAME (`IsCommentKey`: `CommentExact` denylist + prefix rules — `Comment*`, `Note*`,
`Thresholds`, `Priorities1..9` doc lines), **never by value type**. Every other extra key is
preserved verbatim into `Extras` regardless of type — including named alternate priority/gear sets
(arrays like `AdvDC`/`PrioritiesDefault`) AND string-valued backup loadouts
(`"Default (MeepleMolotovEMPC)": "[ 326, ... ]"`), which are user data.

## Breakpoint shapes

| Type | Payload | Extra fields |
|---|---|---|
| `PriorityBreakpoint` | `Priorities` token list (Energy/Magic/R3) | `Challenge` tag, `Extras` |
| `ListBreakpoint` | `Items` int list (Diggers/Beards/Gear) | Gear only: `Objective` + `ForceRespawn`; `Challenge`, `Extras` |
| `StringListBreakpoint` | `Items` token list (Consumables) | `Challenge`, `Extras` |
| `RebirthEntry` | `Type` + optional time + `Target` | other keys preserved |

`Challenge != ""` makes the runtime prefer that breakpoint while the named challenge is active
(`BaseBreakpoints` challenge-aware selection). `TimeSeconds` exposes `Hours/Minutes/Seconds` for
the editor; the JSON accepts both a plain number and the `{h,m,s}` object form.

`ProfileValidator.Validate` is a strict near-RFC-8259 JSON parse with a line/column, run before a save
and on load so the editor can refuse a malformed profile rather than letting SimpleJSON's very lenient
parse misread it silently. It reports the FIRST structural problem; it does not check token grammar.

`ProfileValidator.Warnings` is a separate, semantic pass that returns **advice and never blocks**: today
it flags an energy breakpoint funding more than one augment out of the shared pool (augment boosts stack
additively — `docs/AUGMENTS.md`). It skips CAP tokens, which are bounded reservations rather than splits,
and it treats `AUG-8`/`AUG-9` as one augment because the index is flat over 0-13 (even = augment, odd =
upgrade). Surfaced by `CustomAllocation.ReloadAllocation` (log) and `ProfileEditorForm.Load` (status
line). Covered by `ProfileValidatorWarningTests`.

---

# GrowthTracker (`Managers/GrowthTracker.cs`)

60 s sampler on the status pump feeding a ~2.5 h ring buffer (150 samples). Read-only — every value
is something the UI reads anyway. Powers the GROWTH tiles/graph.

**Chips track GAINS (user rule)**: spending EXP/AP/PP must never count a rate DOWN, so each sample
carries cumulative positive deltas (`G*` fields) and the rate walks read those. A failed read
carries the PREVIOUS value, not 0 — with cumulative gains a one-tick dip to 0 would register the
whole balance as a fresh gain next minute.

**Rebirth is the only reset the chips see**: NGU levels reset on rebirth, so `Rate(...)` and
`RunDelta(...)` stop walking at a run boundary (detected as `RunSec` going backwards) for per-run
metrics and RUN windows — a rate across a reset is meaningless. `Rate` needs ≥ 30 s of history in
the window, else returns false.
