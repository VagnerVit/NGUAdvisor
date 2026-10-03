using System;
using NGUAdvisor.AllocationProfiles.RebirthStuff;

namespace NGUAdvisor.Managers
{
    // Route C3 Phase 3.1: reads LIVE progression state (titan kills, boss, difficulty, challenge-block state)
    // to produce an accurate stage, a milestone-based "next goal", and a context-aware profile recommendation
    // — replacing the crude highestBoss+difficulty heuristic. Consumed by StatusPanel/DashboardPanel/overlay.
    // Cached/throttled (heavier optimality math is added in 3.2), guarded, main-thread only.
    //
    // CANONICAL CHAPTER ENGINE — this Chapter (derived from actual TITAN KILLS) is the authoritative "what
    // chapter am I in" for stage/HUD/perk/profile advice, and supersedes the coarse boss-threshold
    // StageDetector.Chapter for those uses. StageDetector is retained ONLY for its two boss-anchored consumers
    // (ChallengeOverlay segment gating, LevelPlanner NGU-track). The two Chapter values intentionally DIVERGE
    // when boss progress leads titan kills (see StageDetector's class note for the full contrast) — do NOT treat
    // them as the same number or substitute one for the other.
    public static class ProgressionAnalyzer
    {
        public struct Progression
        {
            public bool Known;
            public int Chapter;              // 1..8. Canonical titan-kill chapter (see class note) — NOT StageDetector.Chapter (boss-threshold).
            public string Label;             // "Ch.4 T6"
            public string Difficulty;        // Normal / Evil / Sadistic
            public string Activity;          // what we're doing now
            public string NextGoal;          // milestone we're working toward
            public bool GoalIsKill;          // the milestone is a titan/boss KILL push (ask this, never text-match NextGoal)
            public string RecommendedProfile;
            public string RecommendReason;
            public string OptimalFocus;      // GO-style "best gain" advice (filled in 3.2)
        }

        private static readonly Progression Unknown = new Progression
        {
            Known = false, Chapter = 0, Label = "Stage -", Difficulty = "", Activity = "-",
            NextGoal = "-", GoalIsKill = false, RecommendedProfile = "", RecommendReason = "", OptimalFocus = ""
        };

        private static Progression _cache = Unknown;
        private static DateTime _cacheTime = DateTime.MinValue;
        private const double CacheMs = 750;

        public static Progression Detect()
        {
            if ((DateTime.UtcNow - _cacheTime).TotalMilliseconds < CacheMs && _cache.Known)
                return _cache;
            try
            {
                _cache = Compute();
                _cacheTime = DateTime.UtcNow;
                return _cache;
            }
            catch (Exception e)
            {
                Main.LogDebug($"ProgressionAnalyzer failed: {e.Message}");
                return _cache.Known ? _cache : Unknown;
            }
        }

        private static Progression Compute()
        {
            var c = Main.Character;
            if (c == null || c.settings == null) return Unknown;

            var diff = c.settings.rebirthDifficulty;
            string diffName = diff == difficulty.sadistic ? "Sadistic" : diff == difficulty.evil ? "Evil" : "Normal";
            int boss = ZoneHelpers.CurrentHighestBoss(c);

            bool t6 = TitanBeaten(5), t8 = TitanBeaten(7), t9 = TitanBeaten(8);

            // Guide chapters open on the kill that names them: ch6 "Congrats on defeating The
            // Godmother" (T8), ch7 "Congrats on defeating The Exile" (T9). Ch5 Evil-IDP spans T7 too.
            int chapter; string name;
            if (diff == difficulty.sadistic) { chapter = 8; name = "Sadistic"; }
            else if (diff == difficulty.evil)
            {
                if (t9) { chapter = 7; name = "T9"; }
                else if (t8) { chapter = 6; name = "T8-JRPG"; }
                else { chapter = 5; name = "Evil-IDP"; }
            }
            else
            {
                if (t6) { chapter = 4; name = "T6"; }
                else if (boss >= 100) { chapter = 3; name = "T4-BAE"; }
                else if (boss >= 58) { chapter = 2; name = "T1-Mega"; }
                else { chapter = 1; name = "Start-HSB"; }
            }

            var challenge = ChallengeDetector.Current();
            bool inBlock = challenge != null || SafeAnyChallengesValid();
            string mode = LockManager.GetLockTypeName();
            string activity = challenge != null ? "Challenge " + challenge
                : mode != "Default" ? mode
                : inBlock ? "Challenge block" : "Farming / idle";

            bool goalIsKill = false;
            string nextGoal = inBlock ? "Complete challenge block" : MilestoneGoal(chapter, boss, out goalIsKill);
            string focus = GetOptimalFocus(chapter);

            string rec, reason;
            if (inBlock)
            {
                rec = Main.Settings?.AllocationFile ?? "";
                reason = "In a challenge block — stay on this profile.";
            }
            else
            {
                rec = RecommendProfile(diff, chapter, out reason);
            }

            return new Progression
            {
                Known = true,
                Chapter = chapter,
                Label = $"Ch.{chapter} {name}",
                Difficulty = diffName,
                Activity = activity,
                NextGoal = nextGoal,
                GoalIsKill = goalIsKill,
                RecommendedProfile = rec,
                RecommendReason = reason,
                OptimalFocus = focus
            };
        }

        // GO-optimality (3.2): compare the optimizer's best loadout to the currently-equipped one for a
        // stage-appropriate, base-100 objective (never zero-scores). Heavier (runs Optimize) so throttled
        // separately (~10s) and cached. Names the gear-improvement headroom; augment/NGU are auto-optimized
        // by the allocation engine already (BestAug / NGU targets), so they aren't re-recommended here.
        private static string _focus = "";
        private static DateTime _focusTime = DateTime.MinValue;
        private const double FocusMs = 10000;

        private static string GetOptimalFocus(int chapter)
        {
            if ((DateTime.UtcNow - _focusTime).TotalMilliseconds < FocusMs) return _focus;
            _focusTime = DateTime.UtcNow;
            try
            {
                string objName = chapter <= 4 ? "Power" : "NGUs";
                var obj = GearOptimizer.FindObjective(objName);
                if (obj == null) { _focus = ""; return _focus; }
                double cur = GearOptimizer.CurrentScore(obj);
                // No pins on either side of the ratio: CurrentScore does not know about them, so a pin that
                // costs this objective would otherwise read as "your gear is already optimal".
                double opt = GearOptimizer.Optimize(obj, false, new int[0]).Score;
                if (cur > 0 && opt > cur)
                {
                    // Below the call-to-action bar the headroom is still REPORTED, not thrown away: a
                    // full Optimize() ran to produce it, and a few percent of NGU speed compounds over a
                    // 22 h marathon. The 8 % bar decides the wording ("re-optimize" vs "near-optimal"),
                    // never whether the user gets to see the number.
                    double pct = (opt / cur - 1.0) * 100.0;
                    _focus = pct >= 8
                        ? $"Re-optimize gear: +{pct:0}% {objName}"
                        : $"Gear near-optimal ({objName}, +{pct:0.#}% left)";
                }
                else _focus = $"Gear near-optimal ({objName})";
            }
            catch (Exception e) { Main.LogDebug($"OptimalFocus failed: {e.Message}"); _focus = ""; }
            return _focus;
        }

        // Titans T5..T12 (index 4..11) all report the same way: the all-time `boss{N}Kills` counter.
        // Low titans (T1..T4) are inferred from highestBoss in the chapter logic.
        //
        // This used to read `TitanVersion(idx) >= 2` for the versioned titans — but `titan{N}Version`
        // is the V1..V4 difficulty the player has SELECTED, not progress, and it stays 0 no matter how
        // often the titan dies. So killing the Beast never registered: the chapter stuck at 3 and the
        // EXP balancer kept handing out the post-T5 5:1 ratio for the rest of the game (user-reported
        // 2026-09-12).
        public static bool TitanBeaten(int idx)
        {
            try { return idx >= 4 && idx <= 11 && ZoneHelpers.TitanKills(idx) >= 1; }
            catch { return false; }
        }

        // Which VERSION of a versioned titan has been beaten — the game records that per enemy, in the
        // bestiary, not on the titan (see ZoneHelpers.TitanVersionsBeaten).
        private static bool TitanVersionBeaten(int idx, int version)
        {
            try { return ZoneHelpers.TitanVersionsBeaten(idx) >= version; }
            catch { return false; }
        }

        // `isKill` says whether the milestone is a KILL push, so consumers never have to read the label.
        // The labels are display shorthand and have been reworded before (RecommendProfile's note below
        // records the last time a text match on them went wrong); OptimizationAdvisor.Mode read them for
        // "Titan", which no Normal/Evil label contains, so push mode — and with it the Stats/Adv/Blood
        // digger set and the Stats/Adv/Wandoos beard set — was unreachable outside chapter 8.
        private static string MilestoneGoal(int chapter, int boss, out bool isKill)
        {
            isKill = true;
            switch (chapter)
            {
                // Compact hints — sized to fit the status strip's NEXT GOAL cell (the full guide detail
                // lives in the chapter's Goal line + NGU-KNOWLEDGE.md). Standard NGU shorthand: T# = Titan,
                // B# = Boss.
                case 1: return "Kill T1 (GRB)";
                case 2: return "B100 → kill T4";
                case 3: return "Beards → kill T6";
                case 4:
                    // Name the NEXT version, not the chapter's last one: ch.4 is the sequence
                    // v1 -> v2 -> CBlock2 -> v3 -> v4, each a week or more apart, and "Kill T6 v4"
                    // read as the goal while still on v1 skips every step that actually comes next.
                    int t6v = ZoneHelpers.TitanVersionsBeaten(5);
                    if (t6v < 4) return $"Kill T6 v{t6v + 1}";
                    isKill = false;
                    if (boss < 300) return "Reach B300";
                    return "Atk boost → Evil";
                case 5:
                    if (!TitanBeaten(6)) return "B125 → kill T7";
                    if (boss < 166) { isKill = false; return "B166 → T8 puzzle"; }
                    return "Kill T8";
                case 6: return "R3 → kill T9";
                case 7:
                    isKill = false;
                    return "24 AK → Rad set";
                case 8: return "Sadistic titans";
                default: isKill = false; return "-";
            }
        }

        // Stage/state -> best installed preset for the not-in-a-block case. NEVER text-matches the
        // milestone label (user-reported: every Normal milestone names a titan, so the old
        // goal-contains-"Titan" rule recommended the no-rebirth LRB push essentially always). The
        // Normal steady state is the guide's 24h cadence — every run pushes the number, harvests
        // fruits at the 24h tier (seeds), banks ~24h beard growth, and spends the bulk of the day
        // in the NGU marathon. Normal-LRB (RebirthTime -1) is a deliberate one-shot push, only
        // recommended when the next titan kill is actually in reach (see TitanPushInReach).
        // Evil/Sadistic default to NGU-focused until difficulty-specific presets are authored
        // (they'll be added as the user reaches those stages, where they're testable).
        // THE PRESET IS THE FALLBACK, NOT THE ANSWER. The preset below decides WHICH KIND of run this
        // is (a no-rebirth push vs. the timed cadence); ProfileScout then looks for a file on disk of
        // that same kind that funds more of the plan's NGU lanes, and its name wins when it finds one.
        // Before that existed, a user running their own LRB profile read "Recommended: Normal-24hr" as
        // a verdict ON that profile when it had never been considered at all (user-reported
        // 2026-08-12); the caveat that used to ride along here was a stand-in for this.
        private static string RecommendProfile(difficulty diff, int chapter, out string reason)
        {
            string preset;
            bool wantLrb = false;
            if (diff != difficulty.normal)
            {
                reason = "Best-fit farm preset for your stage.";
                preset = "Goal-NGU";
            }
            else if (TitanPushInReach(out var target))
            {
                reason = $"{target} in reach — one long push, no auto-rebirth; rebirth manually after the kill.";
                preset = "Normal-LRB";
                wantLrb = true;
            }
            else if (chapter <= 2)
            {
                reason = "Early game: push adventure zones and boss EXP.";
                preset = "Goal-Adventure";
            }
            else
            {
                reason = "Daily cadence: number push + fruit/seed harvest + beard banking + NGU marathon.";
                preset = "Normal-24hr";
            }

            // The scout answers only when a file on disk BEATS this preset at funding the plan's NGU
            // lanes; a tie or a miss leaves the preset standing.
            var scouted = ProfileScout.Best(wantLrb, diff, preset, out string why);
            if (scouted == null) return preset;

            reason = $"{reason} {why}";
            return scouted.Name;
        }

        // Kill-readiness gate for the LRB recommendation. In reach = we CAN'T clear the next
        // titan's staged requirement right now, but the optimizer's best Power/Toughness gear
        // projects to >= 70% of it — close enough that one long run of stat building (BT/AT/TM
        // compounding on top of the gear swing) plausibly crosses the line. If current stats
        // already clear it, the 24h cadence takes the kill in stride (titan sniping runs either
        // way); if projected stats are far off, 24h compounding beats a stalled long run. The
        // factor is an approximation — tune against reality like the kill ladder was. Throttled
        // (~10s) like GetOptimalFocus: NextObjective + ProjectedBestGear lean on optimizer runs.
        private const double LrbReachFactor = 0.70;
        private static bool _pushInReach;
        private static string _pushTarget = "";
        private static DateTime _pushAt = DateTime.MinValue;

        private static bool TitanPushInReach(out string target)
        {
            if ((DateTime.UtcNow - _pushAt).TotalMilliseconds < FocusMs)
            {
                target = _pushTarget;
                return _pushInReach;
            }
            _pushAt = DateTime.UtcNow;
            _pushInReach = false;
            _pushTarget = "";
            try
            {
                var o = OptimizationAdvisor.NextObjective();
                if (o.Known && o.ReqAttack > 0)
                {
                    double atk = Main.Character.totalAdvAttack();
                    double def = Main.Character.totalAdvDefense();
                    bool killableNow = atk >= o.ReqAttack && def >= o.ReqDefense;
                    if (atk > 0 && !killableNow)
                    {
                        OptimizationAdvisor.ProjectedBestGear(out var atkMult, out var defMult);
                        if (atk * atkMult >= o.ReqAttack * LrbReachFactor &&
                            def * defMult >= o.ReqDefense * LrbReachFactor)
                        {
                            _pushInReach = true;
                            _pushTarget = $"T{o.Index + 1} {o.Stage}";
                        }
                    }
                }
            }
            catch (Exception e) { Main.LogDebug($"TitanPushInReach failed: {e.Message}"); }
            target = _pushTarget;
            return _pushInReach;
        }

        private static bool SafeAnyChallengesValid()
        {
            try { return BaseRebirth.AnyChallengesValid(); }
            catch { return false; }
        }
    }
}
