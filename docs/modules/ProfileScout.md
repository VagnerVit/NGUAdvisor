# ProfileScout (`Managers/ProfileScout.cs`)

Scores the profile FILES ON DISK against what the plan currently wants to feed, so
`ProgressionAnalyzer`'s recommendation can name the user's own profile instead of only ever naming a
shipped preset.

## Why it exists

`RecommendProfile` picked from installed presets only and said so in a caveat. A user running their
own LRB profile read "Recommended: Normal-24hr" as a verdict ON that profile when it had never been
considered at all (user-reported 2026-08-12). The caveat (`PresetOnlyCaveat`) was a stand-in; this
module is the thing it stood in for, and the constant is gone.

## Two stages, because neither alone decides

1. **Hard filter on rebirth style.** An LRB profile and a cadence profile are not interchangeable at
   any score, so the wrong kind is *excluded*, never ranked. The caller supplies which kind it wants
   (`ProgressionAnalyzer.TitanPushInReach`).
2. **Ranking by lane overlap** — how many of `NGUAdvisors.Compute`'s `EnergyTargets`/`MagicTargets`
   the profile's `Priorities` actually fund, across ALL breakpoints (not just time 0). `NGU-<n>` and
   `CAPNGU-<n>` both count; a CAP lane drinks what is left when its turn comes, which is still
   funding. The filter alone cannot choose: the user has FOUR profiles with no auto-rebirth.

## "Auto-rebirth", not "timed rebirth"

`HasAutoRebirth` asks whether **anything ends the run on its own** — which is the wording the
recommendation itself uses ("one long push, no auto-rebirth"). A `NUMBER`/`BOSS` target rebirths the
run as surely as a timer: the user's `CBlock1` carries only `Number/target=1000` and no `Time` entry,
and a clock-only test filed it as an LRB candidate. It mirrors `BreakpointWrapper`'s reading of both
shapes (`CustomAllocation.cs:371`) — a typed `Rebirth` array, or the legacy scalar `RebirthTime`
where `-1` and a missing key both mean "never".

## It must BEAT the fallback, not tie with it

`Best(wantLrb, fallback, out reason)` returns null unless a file outscores the preset the caller
would otherwise name. **A tie changes the answer without improving it**: `Goal-AdvDC` and
`Normal-24hr` fund exactly the same lanes, and choosing between them on lane count would be a coin
toss dressed as a verdict. Lane overlap is the only thing this module measures, so it is the only
thing allowed to overrule the caller. A profile funding NOTHING of the plan (`Matched == 0`) is never
an answer either.

The return carries a REASON, never a bare name — the failure it fixes was a name with no visible
basis. `ProgressionAnalyzer` appends it to its own reason string.

**"Nothing better" is logged too.** Every null return goes through `Nothing(...)`, which writes why
(`keeping Normal-24hr — Normal-24hr already funds 3/3; best on disk is Goal-AdvDC at 3/3`). The first
live run logged nothing at all, which left no way to tell "the scout ran and the preset held" from
"the scout never ran" — the exact class of invisible precedence this work exists to remove.

## Cost

Throttled 10 s (the interval `GetOptimalFocus` uses, for the same reason): it sweeps the profiles
directory and reads every file, and its caller sits behind `Detect()`'s 750 ms cache — which would
otherwise be a directory sweep more than once a second. `[ProfileDbg]` logs the pick, the score and
every candidate, **only when the pick changes**.

MAIN THREAD ONLY — `NGUAdvisors.Compute` reads the live `Character`.
