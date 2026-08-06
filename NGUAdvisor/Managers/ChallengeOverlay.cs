using System;
using System.Collections.Generic;
using System.Linq;
using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using SimpleJSON;

namespace NGUAdvisor.Managers
{
    // Option B "challenge overlays" — first slice. The user's profile stays the base; while a
    // challenge is active (and Settings.AdvisorChallenges is on) the overlay transforms behavior on
    // top of it. This slice: phase-aware GEAR OBJECTIVE rotation (push = bosses falling -> Adventure
    // gear; growth = boss wall -> NGUs gear) driven through ApplyGearRefresh, NOEC gear suppression,
    // the completions-aware block model, and the ACTION FEED every overlay decision is written to
    // (the Challenges tab renders it; it doubles as the validation log for growing templates toward
    // Option A). Dead-system allocation strips (NONGU/NOTM/NOAUG) are the next slice.
    public static class ChallengeOverlay
    {
        // Newest-first, capped; the Challenges tab renders this verbatim.
        public static readonly List<string> Feed = new List<string>();

        // Consulted by ApplyGearRefresh in place of the profile's objective while set.
        public static string GearObjectiveOverride { get; private set; }
        public static string Phase { get; private set; } = "";

        private static int _lastBoss = -1;
        private static DateTime _lastBossKill = DateTime.MinValue;
        private static string _activeChallenge;

        // Number-growth projection: how many bosses would 30 minutes of number growth cover?
        // Sampled from nextAttackMulti (number-derived, immune to our own gear swaps) over rolling
        // ~10-minute windows; linear projection; boss cost k = 2.0 Normal / 1.5 Evil / ~1.25 Sadistic.
        public static double Bosses30 { get; private set; } = double.NaN;
        private static double _numSample;
        private static DateTime _numSampleAt = DateTime.MinValue;

        private static void UpdateNumberProjection(Character c)
        {
            try
            {
                // First 10 minutes of a run: the multiplier is tiny and volatile — projections
                // whipsawed segments (log-audit: 2.1 -> 0.3 boss/30m within a minute of rebirth).
                // Phase falls back to kill-recency until the run has substance.
                double runSec = 0;
                try { runSec = c.rebirthTime.totalseconds; } catch { }
                if (runSec < 600)
                {
                    Bosses30 = double.NaN;
                    _numSampleAt = DateTime.MinValue;
                    return;
                }

                double cur = c.nextAttackMulti;
                if (cur <= 0) return;

                // First sample, or a rebirth reset (the multiplier collapsed): start a fresh window.
                if (_numSampleAt == DateTime.MinValue || cur < _numSample * 0.5)
                {
                    _numSample = cur;
                    _numSampleAt = DateTime.UtcNow;
                    Bosses30 = double.NaN;   // warming up
                    return;
                }

                double elapsed = (DateTime.UtcNow - _numSampleAt).TotalSeconds;
                if (elapsed < 120) return;   // too short to trust

                double k;
                try
                {
                    k = c.settings.rebirthDifficulty == difficulty.sadistic ? 1.25
                        : c.settings.rebirthDifficulty == difficulty.evil ? 1.5 : 2.0;
                }
                catch { k = 2.0; }

                double rate = (cur - _numSample) / elapsed;
                double projected = cur + rate * 1800.0;
                Bosses30 = projected > cur ? Math.Log(projected / cur) / Math.Log(k) : 0;

                // Roll the window forward every ~10 minutes so the estimate tracks the run.
                if (elapsed >= 600)
                {
                    _numSample = cur;
                    _numSampleAt = DateTime.UtcNow;
                }
            }
            catch (Exception e) { Main.LogDebug($"Number projection: {e.Message}"); }
        }

        public static void Record(string action, string reason) => Record("ALLOC", action, reason);

        // Category-tagged feed entries (Advisors > FEED filters on the [CAT] prefix).
        // Actions are sentence-cased here (capitalization scheme: feed lines are reports).
        public static void Record(string category, string action, string reason)
        {
            try
            {
                if (!string.IsNullOrEmpty(action) && char.IsLower(action[0]))
                    action = char.ToUpperInvariant(action[0]) + action.Substring(1);
                Feed.Insert(0, $"[{category}] {DateTime.Now:HH:mm} {action} — {reason}");
                if (Feed.Count > 50) Feed.RemoveAt(Feed.Count - 1);
                Main.Log($"Overlay: {action} ({reason})");
            }
            catch { }
        }

        // R11: the outer whole-Tick catch was removed so AdvisorApply's RunStep("Challenge overlay", ...)
        // owns the bounded fault report. The narrow boss-read / detector probes keep their own catches.
        public static void Tick()
        {
            var c = Main.Character;
            if (c == null) return;

            var s = Main.Settings;
            bool overlays = s != null && s.AdvisorChallenges;
            bool auto = s != null && s.AutoProfile;
            if (!overlays && !auto)
            {
                GearObjectiveOverride = null;
                Phase = "";
                _lastGenKey.Clear();   // narrate afresh when the auto profile comes back
                return;
            }

            // Phase (user rule, post-challenge recovery): PUSH while the number is still cheap
            // power — i.e. projected number growth over 30 minutes covers MULTIPLE bosses (each
            // boss costs x2 attack on Normal, x1.5 Evil) — or while bosses are actively falling.
            // GROWTH once 30 minutes of number wouldn't move the wall much: pivot to the
            // long-horizon systems (wandoos/NGU).
            int boss = 0;
            try { boss = c.bossID; } catch { }
            if (boss != _lastBoss)
            {
                _lastBoss = boss;
                _lastBossKill = DateTime.UtcNow;
            }
            bool recentKill = (DateTime.UtcNow - _lastBossKill).TotalMinutes < 5;
            UpdateNumberProjection(c);
            bool numberCheap = !double.IsNaN(Bosses30) && Bosses30 >= 2.0;
            Phase = (recentKill || numberCheap) ? "push" : "growth";

            // D1: run segment (the guide's 24h rebirth shape), auto-profile only.
            if (auto) UpdateSegment(c);
            else Segment = "";

            if (!overlays)
            {
                // Auto profile only: no challenge transforms; gear follows the segment
                // outside challenges (a challenge with overlays off = profile rules).
                GearObjectiveOverride = ChallengeDetector.Current() == null ? SegmentGear() : null;
                return;
            }

            string cur = ChallengeDetector.Current();
            if (cur != _activeChallenge)
            {
                if (cur != null) Record("SEGMENT", $"challenge start: {cur}", "overlay active");
                else if (_activeChallenge != null) Record("SEGMENT", $"challenge complete: {_activeChallenge}", "back to profile rules");
                _activeChallenge = cur;
                GearObjectiveOverride = null;
                // Fresh narration state per challenge (else a lingering template flag would
                // suppress the new challenge's "template applied" feed entry). Generation key
                // too: the auto profile re-announces when it resumes after the challenge.
                _lastActive.Clear();
                _templateOn.Clear();
                _fallbackOn.Clear();
                _lastGenKey.Clear();
            }
            if (cur == null)
            {
                GearObjectiveOverride = auto ? SegmentGear() : null;
                return;
            }

            // NOEC: no equipment exists — gear rotation stands down entirely.
            if (cur == "NOEC")
            {
                if (GearObjectiveOverride != null)
                {
                    GearObjectiveOverride = null;
                    Record("GEAR", "gear rotation off", "NOEC: no equipment in this challenge");
                }
                return;
            }

            bool pushing = Phase == "push";
            string want = pushing ? "Adventure" : "NGUs";
            if (want != GearObjectiveOverride)
            {
                GearObjectiveOverride = want;
                Record("GEAR", $"gear → {want}", pushing ? $"push phase: boss {boss} within reach" : "growth phase: at the boss wall");
            }
        }

        // ---- Slice 2: allocation strips. The per-system challenge guards already exist (AugmentBP/
        // BestAug refuse NOAUG, TimeMachineBP refuses NOTM, NGUBP dies with the disabled NGU button),
        // and PerformSwap filters IsValid() BEFORE computing shares — so redistribution is free. The
        // hole this closes: a profile breakpoint whose priorities are ALL dead in the active challenge
        // used to bail out and leave the resource idle for the whole challenge. Now the overlay
        // (1) narrates the filter in the feed, and (2) injects a generic fallback priority list in the
        // all-dead case — itself IsValid-filtered, so whatever the challenge kills drops here too. ----
        private static bool _lscSwordOn;   // narration flag for the LSC sword-first injection
        private static readonly Dictionary<ResourceType, int> _lastActive = new Dictionary<ResourceType, int>();
        private static readonly Dictionary<ResourceType, List<ResourceBreakpoint>> _fallbackParsed = new Dictionary<ResourceType, List<ResourceBreakpoint>>();
        private static readonly Dictionary<ResourceType, bool> _templateOn = new Dictionary<ResourceType, bool>();
        private static readonly Dictionary<ResourceType, bool> _fallbackOn = new Dictionary<ResourceType, bool>();  // narrate fallback injection ONCE per state change, not per tick
        private static readonly Dictionary<string, List<ResourceBreakpoint>> _templateParsed = new Dictionary<string, List<ResourceBreakpoint>>();

        // Slice 3: re-weighting templates for the strip challenges. Stripping alone leaves the
        // SURVIVORS with the dead systems' shares — a 70%-NGU profile in NONGU floods basic training,
        // which is survival, not strategy. When a strip challenge has gutted the profile list (half
        // or more entries inactive), the challenge's template takes over the shape instead. Untouched
        // outside these three challenges; a lightly-degraded list stays the user's own.
        private static readonly Dictionary<string, Dictionary<ResourceType, string[]>> Templates =
            new Dictionary<string, Dictionary<ResourceType, string[]>>
            {
                ["NONGU"] = new Dictionary<ResourceType, string[]>
                {
                    [ResourceType.Energy] = new[] { "CAPTM:10", "CAPWAN:60", "BESTAUG", "CAPALLAT", "ALLBT" },
                    [ResourceType.Magic] = new[] { "CAPTM:10", "CAPWAN:60", "BR-30" },
                },
                ["NOTM"] = new Dictionary<ResourceType, string[]>
                {
                    [ResourceType.Energy] = new[] { "CAPWAN:40", "BESTAUG", "CAPALLAT", "ALLNGU", "ALLBT" },
                    [ResourceType.Magic] = new[] { "CAPWAN:40", "ALLNGU", "BR-30" },
                },
                ["NOAUG"] = new Dictionary<ResourceType, string[]>
                {
                    [ResourceType.Energy] = new[] { "CAPTM:5", "CAPWAN:40", "CAPALLAT", "ALLNGU", "ALLBT" },
                    [ResourceType.Magic] = new[] { "CAPTM:5", "CAPWAN:40", "ALLNGU", "BR-30" },
                },
            };

        public static List<ResourceBreakpoint> TransformPriorities(ResourceBreakpoint[] original, List<ResourceBreakpoint> valid, ResourceType type)
        {
            try
            {
                if (Main.Settings == null) return valid;
                string cur = ChallengeDetector.Current();
                if (cur != "LSC") RestoreLscAugTargets();   // undo the LSC aug caps once the challenge is over
                if (cur == null || !Main.Settings.AdvisorChallenges)
                {
                    _lastActive.Remove(type);
                    _templateOn.Remove(type);
                    _fallbackOn.Remove(type);
                    _lscSwordOn = false;
                    // Option A: outside challenges the auto profile generates the list (challenge
                    // active with overlays off = profile rules, the conservative reading).
                    if (cur == null && Main.Settings.AutoProfile)
                    {
                        var gen = AutoGenerated(type);
                        if (gen.Count > 0) return gen;
                    }
                    return valid;
                }

                int origCount = original?.Length ?? 0;
                if (!_lastActive.TryGetValue(type, out var last) || last != valid.Count)
                {
                    _lastActive[type] = valid.Count;
                    if (valid.Count < origCount)
                        Record($"{type} priorities: {valid.Count}/{origCount} active", $"{cur}: dead/finished systems filtered");
                }

                // LSC disables NOTHING, so it should run the normal segment timeline (TM/AT/BT hours) and
                // merely FOCUS the laser sword aug + upgrade to the challenge target. Two fixes vs the old
                // behavior (user-reported after the first LSC): (a) BASE = the segment allocation when
                // AutoProfile — the old static-profile base ignored TM hour, so magic pooled in wandoos/
                // blood instead of the TM; (b) set the GAME's aug-6 targets to the challenge level so the
                // CAP sword priorities STOP at target (via TargetMet → IsValid=false) instead of filling to
                // their energy cap — otherwise Laser Sword raced to lv8 while the upgrade sat at lv1 (never
                // completing) and Basic Training got nothing. Once both hit target their breakpoints drop
                // and energy flows to the rest of the timeline.
                if (cur == "LSC")
                {
                    var baseList = Main.Settings.AutoProfile ? AutoGenerated(type) : valid;
                    if (baseList.Count == 0) baseList = valid;
                    if (type != ResourceType.Energy) return baseList;

                    SetLscAugTargets();
                    var sword = ParsedList("LSC|sword", new[] { "CAPAUG-12", "CAPAUG-13" }, type);
                    if (sword.Count > 0 && !_lscSwordOn)
                    {
                        _lscSwordOn = true;
                        Record("Energy → laser sword first", "LSC: sword + upgrade to target, then the normal timeline");
                    }
                    else if (sword.Count == 0) _lscSwordOn = false;
                    var merged = new List<ResourceBreakpoint>(sword);
                    foreach (var bp in baseList) if (!merged.Contains(bp)) merged.Add(bp);
                    return merged;
                }

                // Template takeover when a strip challenge gutted the list (>= half inactive — the
                // count includes finished-target entries, an acceptable over-trigger since the
                // template only exists for challenges whose strips are the dominant cause).
                bool gutted = origCount > 0 && valid.Count * 2 <= origCount;
                if ((gutted || valid.Count == 0) && Templates.TryGetValue(cur, out var perType)
                    && perType.TryGetValue(type, out var tokens))
                {
                    var tpl = ParsedList($"{cur}|{type}", tokens, type);
                    if (tpl.Count > 0)
                    {
                        if (!_templateOn.TryGetValue(type, out var on) || !on)
                        {
                            _templateOn[type] = true;
                            Record($"{type} → {cur} template", $"profile list {valid.Count}/{origCount} active — reshaping allocation");
                        }
                        _fallbackOn[type] = false;
                        return tpl;
                    }
                }
                if (_templateOn.TryGetValue(type, out var wasOn) && wasOn)
                {
                    _templateOn[type] = false;
                    Record($"{type} → profile priorities", $"{cur}: profile list healthy again");
                }

                if (valid.Count > 0 || origCount == 0)
                {
                    _fallbackOn[type] = false;
                    return valid;
                }

                var fb = Fallback(type);
                if (fb.Count > 0)
                {
                    if (!_fallbackOn.TryGetValue(type, out var fbOn) || !fbOn)
                    {
                        _fallbackOn[type] = true;
                        Record($"{type} fallback priorities injected", $"{cur}: profile list is entirely inactive");
                    }
                }
                return fb;
            }
            catch (Exception e)
            {
                Main.LogDebug($"TransformPriorities: {e.Message}");
                return valid;
            }
        }

        private static List<ResourceBreakpoint> ParsedList(string key, string[] tokens, ResourceType type)
        {
            if (!_templateParsed.TryGetValue(key, out var parsed))
            {
                var arr = new JSONArray();
                foreach (var t in tokens) arr.Add(t);
                parsed = ResourceBreakpoint.ParseBreakpointArray(arr, type).Where(x => x != null).ToList();
                _templateParsed[key] = parsed;
            }
            return parsed.Where(x => x.IsValid()).ToList();
        }

        // LSC completion = leveling the laser sword aug (augs[6]) AND its upgrade to the CONTROLLER's
        // laserSwordTarget(), which is this difficulty's completions (clamped to max) + 2 — NOT
        // Character.challenges.laserSwordChallenge.curCompletions + 2, which is the normal-difficulty
        // counter and unclamped, and on Evil/Sadistic can sit far above the real requirement. Setting
        // the game's aug-6 "set caps" to that level makes the CAP sword priorities stop there
        // (AugmentBP.TargetMet → IsValid=false), so BOTH reach target instead of Laser Sword racing to
        // its energy cap while the upgrade starves. Snapshot the user's own caps once (persisted, so a
        // save/reload mid-challenge can't strand the override) and restore them when LSC ends.
        private static void SetLscAugTargets()
        {
            try
            {
                var c = Main.Character;
                var a = c.augments.augs[6];
                long target = c.allChallenges.laserSwordChallenge.laserSwordTarget();
                var s = Main.Settings;
                if (s != null && !s.LscTargetsSaved)
                {
                    s.LscSavedAugTarget = a.augmentTarget;
                    s.LscSavedUpgTarget = a.upgradeTarget;
                    s.LscTargetsSaved = true;   // last: a throw above leaves the snapshot un-armed
                }
                a.augmentTarget = target;
                a.upgradeTarget = target;
            }
            catch (Exception e) { Main.LogDebug($"LSC aug targets: {e.Message}"); }
        }

        private static void RestoreLscAugTargets()
        {
            var s = Main.Settings;
            if (s == null || !s.LscTargetsSaved) return;
            try
            {
                var a = Main.Character.augments.augs[6];
                a.augmentTarget = s.LscSavedAugTarget;
                a.upgradeTarget = s.LscSavedUpgTarget;
                s.LscTargetsSaved = false;   // only disarm once the write landed
            }
            catch (Exception e) { Main.LogDebug($"LSC aug restore: {e.Message}"); }
        }

        private static List<ResourceBreakpoint> Fallback(ResourceType type)
        {
            if (!_fallbackParsed.TryGetValue(type, out var parsed))
            {
                string[] tokens;
                switch (type)
                {
                    case ResourceType.Energy: tokens = new[] { "CAPTM:5", "CAPWAN:40", "BESTAUG", "CAPALLAT", "ALLNGU", "ALLBT" }; break;
                    case ResourceType.Magic: tokens = new[] { "CAPTM:5", "CAPWAN:40", "ALLNGU", "BR-30" }; break;
                    case ResourceType.R3: tokens = new[] { "ALLHACK" }; break;
                    default: tokens = new string[0]; break;
                }
                var arr = new JSONArray();
                foreach (var t in tokens) arr.Add(t);
                parsed = ResourceBreakpoint.ParseBreakpointArray(arr, type).Where(x => x != null).ToList();
                _fallbackParsed[type] = parsed;
            }
            // IsValid at call time: unlock state and targets move; the challenge kills its own entries.
            return parsed.Where(x => x.IsValid()).ToList();
        }

        // Challenges tab: one-line allocation status per resource for the current view.
        public static string AllocationStatus(ResourceType type)
        {
            if (_templateOn.TryGetValue(type, out var on) && on) return "template";
            if (_lastActive.TryGetValue(type, out var n)) return $"{n} active";
            return null;
        }

        // ---- Option A: the auto profile. Generates energy/magic/R3 priorities per cycle through
        // the same TransformPriorities hook the challenge transforms use — parameterized by RUN
        // SEGMENT (D1: the guide's 24h shape — RECOVERY while the number is cheap power, then
        // TM HOUR -> AT HOUR -> NGU MARATHON), CHAPTER (D2: targeted NGU lists, guide ch.1-4), and
        // TM state. The profile file is never touched; toggling off resumes the timeline.
        // Rebirth + NGU-diff stay profile-owned. ----
        private static readonly Dictionary<ResourceType, string> _lastGenKey = new Dictionary<ResourceType, string>();

        public static string Segment { get; private set; } = "";

        private static int _chapter;
        private static DateTime _chapterAt = DateTime.MinValue;

        private static int Chapter()
        {
            if ((DateTime.UtcNow - _chapterAt).TotalSeconds > 60)
            {
                _chapterAt = DateTime.UtcNow;
                try { _chapter = StageDetector.Detect().Chapter; } catch { _chapter = 0; }
            }
            return _chapter;
        }

        private static void UpdateSegment(Character c)
        {
            bool tmEmpty = true;
            try { tmEmpty = c.machine.realBaseGold <= 0.0; } catch { }
            double runSec = 0;
            try { runSec = c.rebirthTime.totalseconds; } catch { }
            bool atUnlocked = false;
            try { atUnlocked = c.buttons.advancedTraining.interactable; } catch { }
            bool tmUnlocked = false;
            try { tmUnlocked = c.buttons.brokenTimeMachine.interactable && !c.challenges.timeMachineChallenge.inChallenge; } catch { }

            // TIME-ANCHORED (user-reported: RECOVERY held for 5 hours because kill-recency kept
            // "push" alive — past the boss ceiling, bosses die continuously and would hold the run
            // hostage forever). The wall clock owns the shape; the number rule gets a bounded window:
            //   TM HOUR   — ONLY while TM is unlocked: first hour, and any time TM gold is zero.
            //               Evil (and Sadistic) re-lock the Time Machine, so early-Evil runs skip
            //               this and open on RECOVERY/NGU MARATHON until TM re-unlocks.
            //   AT HOUR   — second hour (if AT is unlocked); AtHourPlanner may extend it to the 4h
            //               mark when projected AT gains cross a titan stage or unlock a farm zone
            //               (one bounded decision per run — the time anchor stays the law)
            //   RECOVERY  — number still cheap (>=2 boss/30m), but never past hour 4
            //   MARATHON  — everything after (the guide's 22h); its start is never delayed
            bool numberCheap = !double.IsNaN(Bosses30) && Bosses30 >= 2.0;

            // EVIL CLIMB (guide ch.5 sub-mode A) — early Evil is a boss RE-CLIMB, not a mature 24h run:
            // entering Evil re-locks TM/AT/Wandoos and resets the Number, so the TM/AT/RECOVERY/MARATHON
            // shape doesn't fit. The WHOLE pre-T7 climb (to Boss 125) pushes bosses + grows Number + levels
            // NGUs (AT feeds the EV-exploder objective as systems re-unlock). The matching short Number-target
            // rebirths + the first Evil Basic entry are profile-owned (EvilStart: Number/100 + BASIC-1).
            // Chapter-gated so Normal is untouched; hands back to the normal chain at Boss 125 / T7 unlock
            // (later slices add the guide's full 24h Evil shape past T7).
            bool evilClimb = false;
            try { evilClimb = Chapter() == 5 && ZoneHelpers.CurrentHighestBoss(c) < 125; } catch { }

            // AUGMENTATION (guide ch.5 sub-mode B, phase 2) — once T7-capable (Boss 125+), the 24h run is
            // TM 0–0.5h → AUGMENTATION 0.5–3h → Normal-NGU+AT → Evil-NGU. This phase runs the best augment
            // (energy) and feeds blood for the Counterfeit-once → Blood Number spells (magic). Chapter+boss-
            // gated; preempts TM HOUR from 0.5h so TM keeps only the guide's first half-hour. Magic→blood
            // routing is tunable (untested until Boss 125+).
            bool augPhase = false;
            try { augPhase = Chapter() == 5 && ZoneHelpers.CurrentHighestBoss(c) >= 125
                             && tmUnlocked && runSec >= 1800 && runSec < 10800; } catch { }

            string seg;
            if (evilClimb) seg = "EVIL CLIMB";
            else if (augPhase) seg = "AUGMENTATION";
            else if (tmUnlocked && (tmEmpty || runSec < 3600)) seg = "TM HOUR";
            else if (atUnlocked && runSec < AtHourPlanner.EndSec(c, runSec)) seg = "AT HOUR";
            else if (numberCheap && runSec < 14400) seg = "RECOVERY";
            else seg = "NGU MARATHON";

            if (seg != Segment)
            {
                Segment = seg;
                if (Main.Settings != null && Main.Settings.AutoProfile)
                    Record("SEGMENT", $"segment → {seg}",
                        $"run {runSec / 3600:0.0}h · {(double.IsNaN(Bosses30) ? Phase : $"≈{Bosses30:0.#} boss/30m")}");
            }
        }

        private static string SegmentGear()
        {
            switch (Segment)
            {
                case "EVIL CLIMB": return "Adventure";   // re-climb: adventure stats push the boss wall
                case "AUGMENTATION": return "Augments";  // guide ch5 phase 2: gear for augment speed
                case "TM HOUR": return "Time Machine";
                case "RECOVERY": return "Adventure";
                // AT HOUR levels the trainings — gear for AT SPEED, not combat stats (user-compared
                // vs the GO site's "AT gains" loadout: "Adventure" here wore Power/Toughness gear).
                case "AT HOUR": return "Advanced Training";
                case "NGU MARATHON": return "NGUs";
                default: return null;
            }
        }

        public static string AutoStatus()
        {
            bool tmEmpty = true;
            try { tmEmpty = Main.Character.machine.realBaseGold <= 0.0; } catch { }
            string ph = string.IsNullOrEmpty(Phase) ? "growth" : Phase;
            string seg = string.IsNullOrEmpty(Segment) ? "" : $"{Segment} · ";
            string num = double.IsNaN(Bosses30) ? "number: measuring" : $"number ≈ {Bosses30:0.#} boss/30m";
            return $"{seg}{ph} phase · TM {(tmEmpty ? "empty" : "funded")} · {num}";
        }

        // D2: chapter NGU CANDIDATES (guide ch.1-3 — which bonuses matter early). Energy indices:
        // 0 Augs, 1 Wandoos, 4 Adventure-a, 6 Drop Chance. Magic: 0 Ygg, 3 Number, 4 TM.
        // From ch.4 on, EVERY NGU is a candidate (user rule 2026-07-11: the old ch.4 group lists
        // excluded E7 Magic / E8 PP / M5 Energy / M6 Adventure-β outright, so they never ran) —
        // NGUAdvisors's exact-value ranking decides which lanes actually get energy/magic.
        public static int[] ChapterNguIds(ResourceType type)
        {
            int ch = Chapter();
            if (type == ResourceType.Energy)
            {
                if (ch == 1) return new int[0];             // pre-NGU era
                if (ch == 2) return new[] { 0, 1 };
                if (ch == 3) return new[] { 4, 6 };
                return new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            }
            if (type == ResourceType.Magic)
            {
                if (ch == 1) return new int[0];
                if (ch == 2) return new[] { 0, 3 };
                if (ch == 3) return new[] { 0 };
                return new[] { 0, 1, 2, 3, 4, 5, 6 };
            }
            return new int[0];
        }

        // The NGUs to actually run: NGUAdvisors's value-ranked pick from the chapter candidates
        // (>=1.05x/hr keeps a lane open; nothing hot = deepen the top two), chapter list fallback.
        private static string[] ChapterNgus(ResourceType type)
        {
            var ids = ChapterNguIds(type);
            if (ids.Length == 0) return new string[0];
            try
            {
                var plan = NGUAdvisors.Compute(ChapterNguIds(ResourceType.Energy), ChapterNguIds(ResourceType.Magic));
                var targets = type == ResourceType.Magic ? plan.MagicTargets : plan.EnergyTargets;
                if (plan.Known && targets.Length > 0) ids = targets;
            }
            catch { }
            return ids.Select(i => $"NGU-{i}").ToArray();
        }

        // Surplus lanes: positive-value NGUs that didn't make the hot set, emitted as CAPNGU so
        // they stay OUT of the equal-share divisor — a CAP token only drinks what's left when its
        // turn comes, so the hot lanes' shares are untouched (allocation walks the list in order,
        // recomputing idle/prioCount per non-cap token; caps take min(need, idle-at-turn)).
        private static string[] ChapterNgusSurplus(ResourceType type)
        {
            try
            {
                var plan = NGUAdvisors.Compute(ChapterNguIds(ResourceType.Energy), ChapterNguIds(ResourceType.Magic));
                var ids = type == ResourceType.Magic ? plan.MagicSurplus : plan.EnergySurplus;
                if (plan.Known && ids != null)
                    return ids.Select(i => $"CAPNGU-{i}").ToArray();
            }
            catch { }
            return new string[0];
        }

        public static string[] AutoTokens(ResourceType type)
        {
            if (type == ResourceType.R3) return new[] { "ALLHACK" };
            if (type != ResourceType.Energy && type != ResourceType.Magic) return new string[0];

            bool e = type == ResourceType.Energy;
            var ngus = ChapterNgus(type);
            var list = new List<string>();
            string seg = string.IsNullOrEmpty(Segment) ? "NGU MARATHON" : Segment;

            switch (seg)
            {
                case "EVIL CLIMB":
                    // Ch.5 re-climb (guide sub-mode A): mirror RECOVERY — push bosses (cheap adventure caps),
                    // a small CAPTM:5 to bootstrap TM GOLD once it re-unlocks (~boss 30) so GOLD DIGGERS can
                    // run (diggers need gold income to cover their drain; user-caught: no TM feed → grossGold
                    // stays 0 → diggers never turn on), best augment → AT (EV-exploder) → NGUs; magic feeds
                    // Wandoos then Number. TM/Wandoos CAP tokens self-skip while still locked.
                    if (e) { list.Add("CAPALLBT"); list.Add("CAPTM:5"); list.Add("CAPWAN:30"); list.Add("BESTAUG"); list.Add("CAPALLAT"); }
                    else { list.Add("CAPWAN:30"); list.Add("NGU-3"); }   // magic: Wandoos (when unlocked) then Number NGU
                    foreach (var t in ngus) if (!list.Contains(t)) list.Add(t);
                    break;
                case "AUGMENTATION":
                    // Guide ch5 phase 2 (0.5–3h): energy → best augment (cheap stat caps first); magic →
                    // blood (fuels the Counterfeit-once → Blood Number spells the BloodPlanner casts). The
                    // BR-30 append in the tail is the same live-consumer-gated routing the marathon uses.
                    if (e) { list.Add("CAPALLBT"); list.Add("BESTAUG"); }
                    else list.Add("BR-30");
                    foreach (var t in ngus) if (!list.Contains(t)) list.Add(t);
                    break;
                case "TM HOUR":
                    // The guide's hour-0 shape (24hr profiles): cap the cheap BTs, fund TM, wandoos,
                    // rest to augs. NO AT here (user-reported: CAPALLAT was draining the TM hour) —
                    // AT has its own hour, and BT energy persists once capped.
                    if (e) list.Add("CAPALLBT");
                    list.Add("CAPTM:30");
                    list.Add("CAPWAN:30");
                    if (e) list.Add("BESTAUG");
                    break;
                case "RECOVERY":
                    if (e) list.Add("CAPALLBT");   // cheap caps first (adventure stats for the push)
                    list.Add("CAPTM:5");
                    list.Add("CAPWAN:30");
                    if (e) { list.Add("BESTAUG"); list.Add("CAPALLAT"); }
                    else list.Add("NGU-3");   // D3: the Number NGU IS the number-growth allocation
                    foreach (var t in ngus) if (!list.Contains(t)) list.Add(t);
                    break;
                case "AT HOUR":
                    if (e) { list.Add("CAPALLAT"); list.Add("CAPWAN:30"); list.Add("BESTAUG"); }
                    else { list.Add("CAPTM:5"); list.Add("CAPWAN:30"); }
                    foreach (var t in ngus) if (!list.Contains(t)) list.Add(t);
                    break;
                default:
                    // NGU MARATHON — hot NGU lanes get their full equal shares (the old plain
                    // BESTAUG/CAPALLAT here stole equal shares from them; augs/AT have their hours).
                    list.Add("CAPTM:5");
                    list.Add("CAPWAN:30");
                    foreach (var t in ngus) if (!list.Contains(t)) list.Add(t);
                    // SURPLUS ABSORBERS (user 2026-07-11: 4.7B of a 5.4B pool sat idle once the
                    // hot lanes took their caps — the game hard-caps each NGU at ONE level per
                    // tick, decomp NGUController.updateNGU resets progress to 0 on level-up, so
                    // hot lanes physically cannot drink more). Every absorber is CAP-type: out of
                    // the equal-share divisor, fed strictly from what the hot lanes leave behind.
                    // Value order: warm NGU lanes (any growth beats idle), AT (costs only energy),
                    // then the aug pair last (aug levels also drain gold).
                    foreach (var t in ChapterNgusSurplus(type)) if (!list.Contains(t)) list.Add(t);
                    if (e) { list.Add("CAPALLAT"); list.Add("CAPBESTAUG"); }
                    break;
            }

            // Rituals cost magic that would otherwise be NGU growth, so run them ONLY while blood is
            // ACTUALLY being converted into something: an auto-spell the advisor has routed on
            // (number/loot/gold) or an Iron Pill worth pursuing. When the routing is idle AND the pill
            // isn't worth it, drop rituals so that magic feeds the current segment (e.g. the marathon's
            // NGUs) instead of pooling blood that never gets cast. Applies to EVERY segment now — this
            // was NGU-MARATHON-only, which left TM/AT/recovery pooling unconditionally.
            // (Caveat: the gold-spell "want" reads live blood/sec, so a cold-start gold-starve can't
            // bootstrap rituals from zero — a pre-existing limit; revisit with potential income if it bites.)
            if (!e)
            {
                bool bloodMatters = true;
                try
                {
                    var bm = Main.Character.bloodMagic;
                    bool spellLive = bm.rebirthAutoSpell || bm.lootAutoSpell || bm.goldAutoSpell;
                    bool pillWorth = Main.Settings != null && Main.Settings.CastBloodSpells
                        && BloodPlanner.PillWorthPursuing();
                    bloodMatters = spellLive || pillWorth;
                }
                catch { bloodMatters = true; }   // fail-safe: keep rituals if the state read throws
                if (bloodMatters) list.Add("BR-30");
            }
            return list.ToArray();
        }

        private static List<ResourceBreakpoint> AutoGenerated(ResourceType type)
        {
            var tokens = AutoTokens(type);
            if (tokens.Length == 0) return new List<ResourceBreakpoint>();
            // The NGU picks vary with live value math — the token list itself is the cache key.
            string key = $"auto|{Segment}|{Chapter()}|{type}|{string.Join(",", tokens)}";
            if (_templateParsed.Count > 64) _templateParsed.Clear();   // bound the variant cache
            var list = ParsedList(key, tokens, type);
            if (list.Count > 0 && (!_lastGenKey.TryGetValue(type, out var last) || last != key))
            {
                _lastGenKey[type] = key;
                Record($"{type} → auto profile", AutoStatus());
            }
            return list;
        }

        // ---- Block model: every challenge with live completions, in game-menu order. ----
        public class Entry
        {
            public string Code;
            public int Cur;
            public int Max;
            public string StripNote;
        }

        private static readonly string[][] Defs =
        {
            new[] { "BASIC", "standard rules" },
            new[] { "NOAUG", "will strip augment priorities" },
            new[] { "24HR", "gear rotation: push/growth" },
            new[] { "100LC", "gear rotation: push/growth" },
            new[] { "NOEC", "no gear churn (nothing to wear)" },
            new[] { "TC", "keeps ALLBT guard" },
            new[] { "NORB", "single run — no rebirth spells" },
            new[] { "LSC", "gear rotation: push/growth" },
            new[] { "BLIND", "standard rules (numbers hidden)" },
            new[] { "NONGU", "will strip NGU priorities" },
            new[] { "NOTM", "will strip TM priorities; zone snipe carries gold" },
        };

        // Completions come from the CONTROLLERS (Character.allChallenges), not the serialized
        // Character.challenges objects: there, maxCompletions is [NonSerialized] and nothing ever
        // assigns it, so every Max read 0 — and ChallengesPanel filters its completed chips and its
        // queue on Max > 0, leaving both permanently empty. The controllers also expose
        // currentCompletions(), i.e. the counter for the difficulty actually being played.
        public static List<Entry> Block()
        {
            var list = new List<Entry>();
            try
            {
                var ac = Main.Character?.allChallenges;
                if (ac == null) return list;
                foreach (var d in Defs)
                {
                    int cur, max;
                    switch (d[0])
                    {
                        case "BASIC": cur = ac.basicChallenge.currentCompletions(); max = ac.basicChallenge.maxCompletions; break;
                        case "NOAUG": cur = ac.noAugsChallenge.currentCompletions(); max = ac.noAugsChallenge.maxCompletions; break;
                        case "24HR": cur = ac.hour24Challenge.currentCompletions(); max = ac.hour24Challenge.maxCompletions; break;
                        case "100LC": cur = ac.level100Challenge.currentCompletions(); max = ac.level100Challenge.maxCompletions; break;
                        case "NOEC": cur = ac.noEquipmentChallenge.currentCompletions(); max = ac.noEquipmentChallenge.maxCompletions; break;
                        case "TC": cur = ac.trollChallenge.currentCompletions(); max = ac.trollChallenge.maxCompletions; break;
                        case "NORB": cur = ac.noRebirthChallenge.currentCompletions(); max = ac.noRebirthChallenge.maxCompletions; break;
                        case "LSC": cur = ac.laserSwordChallenge.currentCompletions(); max = ac.laserSwordChallenge.maxCompletions; break;
                        case "BLIND": cur = ac.blindChallenge.currentCompletions(); max = ac.blindChallenge.maxCompletions; break;
                        case "NONGU": cur = ac.NGUChallenge.currentCompletions(); max = ac.NGUChallenge.maxCompletions; break;
                        case "NOTM": cur = ac.timeMachineChallenge.currentCompletions(); max = ac.timeMachineChallenge.maxCompletions; break;
                        default: continue;
                    }
                    list.Add(new Entry { Code = d[0], Cur = cur, Max = max, StripNote = d[1] });
                }
            }
            catch (Exception e) { Main.LogDebug($"ChallengeOverlay block: {e.Message}"); }
            return list;
        }
    }
}
