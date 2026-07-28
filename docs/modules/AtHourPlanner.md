# AtHourPlanner (`Managers/AtHourPlanner.cs`)

AT HOUR extension: near the segment's stock end (2 h into the run), forecast where AT
Power/Toughness land if feeding continues, and extend the segment when the projection crosses the
next titan kill-ladder stage or makes a new zone idle-farmable. Hard cap `MaxEnd = 18000` s (4 h
of AT = the 5 h mark). Decided ONCE per run in the window [1 h 55 m, 2 h 05 m] — the segment
engine is time-anchored by law: bounded windows, no re-litigation. Both outcomes are logged via
`ChallengeOverlay.Record` so the feed always says what was weighed.

## Persistence is load-bearing

Decision persisted in `Settings.AtHourPlannedEnd/AtHourDecidedRunSec`. Statics don't survive an
advisor reload, and without persistence the past-the-boundary branch forced `NormalEnd` — which
silently CANCELLED an in-flight extension, snapping AT HOUR to MARATHON mid-segment. That
mattered because AT HOUR is AT's ONLY feeding window (the marathon's CAPALLAT sits behind surplus
absorbers — fixed 2026-07-16 in ChallengeOverlay), so a cancelled extension meant AT never fed
again that run. A rebirth/quickload (runSec < decidedRunSec) re-arms the decision. Fresh advisor
past the boundary with no persisted decision → keep stock shape (never surprise-flip
RECOVERY/MARATHON back into AT HOUR).

## Forecast math (decomp `AdvancedTrainingController`)

- Level speed `dL/dt = R/(L+1)` → closed form `L(t) = sqrt((L0+1)² + 2Rt) − 1`; `levelTarget`
  caps it; target −1 = paused (R := 0).
- AT stat multiplier: `1 + 0.1 × L^0.4` (slot 1 = attack, slot 0 = defense) → projected stat =
  reference × ratio of multipliers.
- `Solve` scans in 60 s steps (ratios monotone, first hit wins).

## Reference stats — two attack references (user decision + a real 1.5× bug)

Live P/T projected onto the optimizer's best Power/Toughness gear
(`OptimizationAdvisor.ProjectedBestGear`) — AT HOUR wears AT-speed gear; thresholds are met in
the KILL loadout. Then:

- **Titan ladder**: `refAtk = totalAdvAttack() × atkMult` — beast mode INCLUDED (the guide tables
  and the game's AK gate both compare raw totalAdvAttack).
- **Zone tables**: `zoneAtk = refAtk / beastModeBonus()` — ZoneStatHelper divides beast out.

One shared reference understated attack ~1.5× against the titan ladder and extended the segment
chasing stages the kill loadout already cleared.

## Decision details

- Regen-gated titan stages are declared `blocked` — AT can't raise regen.
- Zone candidates: only the NEXT unreachable zone (higher ones follow on later runs); thresholds
  ×1.0001 mirror FightType's strict `>` (need exactly 1.0 would "solve" at t=0).
- Winner gets a 10 % schedule buffer, clamped to [NormalEnd, MaxEnd] — never earlier than the
  stock boundary (the extension must not CUT the hour it lengthens) — and never past the run's
  scheduled rebirth.
- No-extension paths report the honest near-miss: "X needs +N %; th more AT projects +P %/+T %".
- Requires `AutoProfile` (it owns AT feeding; an extended segment would otherwise allocate
  nothing).
