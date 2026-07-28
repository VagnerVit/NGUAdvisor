using System;

namespace NGUAdvisor.Managers
{
    // Level caps (user-approved plan): the advisor manages the game's own target fields
    // (advancedTraining.levelTarget, machine.speedTarget/multiTarget), which the allocation
    // breakpoints already respect (a met target drops out and its share redistributes — the
    // ALLAT waterfill sends it to the remaining slots) and which display in the game's own
    // AT/TM target boxes.
    //
    // AT slots are PURPOSE-DRIVEN (user rule): Toughness (0) and Power (1) are the titan push
    // and stay uncapped; Block (2) stops at 99% damage reduction; the wandoos ATs (3/4) stop
    // once Wandoos' cap-speed dump costs <= 1% of max E/M. Past their stops, those slots'
    // shares flow to Toughness/Power.
    //
    // Sufficiency freezes (marathon only):
    //   Power/Toughness — frozen only while adventure stats beat the NEXT titan AK requirement
    //        with 10% headroom (TitanAk table); a new titan/version target automatically thaws.
    //   TM — frozen only while the TM holds gold AND augments are affordable (the drain
    //        ledger's starvation check); gold trouble thaws it.
    // User targets are snapshotted before the first override and restored on auto-profile off.
    public static class LevelPlanner
    {
        private static long[] _atSnapshot;        // slots 0..1 (sufficiency freeze)
        private static long[] _purposeSnapshot;   // slots 2..4 (purpose caps)
        private static long _speedSnapshot, _multiSnapshot;
        private static bool _frozenAt, _frozenTm, _purposeOn;

        public static string Status { get; private set; } = "";
        public static bool AtFrozen => _frozenAt;
        public static bool TmFrozen => _frozenTm;

        // R11: the outer whole-Tick catch was removed so AdvisorApply's RunStep("Level planner", ...) owns
        // the bounded fault report. The narrow NextObjective / gold probes keep their own catches.
        public static void Tick()
        {
            var c = Main.Character;
            var s = Main.Settings;
            if (c == null || s == null) return;

            if (!s.AutoProfile)
            {
                ThawAll(c);
                Status = "";
                return;
            }

            TickPurposeTargets(c);
            TickNguTrack(c);

            bool marathon = ChallengeOverlay.Segment == "NGU MARATHON";

            // Power/Toughness sufficiency vs the REALISTIC objective: the next un-AK'd
            // titan+version at THIS difficulty (never Evil content while on Normal — the
            // T7 overreach bug).
            bool atSufficient = false;
            try
            {
                var obj = OptimizationAdvisor.NextObjective();
                atSufficient = !obj.Known
                    || (c.totalAdvAttack() >= obj.ReqAttack * 1.1 && c.totalAdvDefense() >= obj.ReqDefense * 1.1);
            }
            catch { }

            bool wantAt = marathon && atSufficient;
            if (wantAt && !_frozenAt) FreezeAt(c);
            else if (!wantAt && _frozenAt) ThawAt(c);

            // TM sufficiency: funded and not starving the augment budget.
            bool goldOk = false;
            try { goldOk = c.machine.realBaseGold > 0 && !OptimizationAdvisor.GoldStarvedForAugs(c, 1.0); } catch { }

            bool wantTm = marathon && goldOk;
            if (wantTm && !_frozenTm) FreezeTm(c);
            else if (!wantTm && _frozenTm) ThawTm(c);

            Status = _frozenAt || _frozenTm
                ? $"caps: {(_frozenAt ? "AT" : "")}{(_frozenAt && _frozenTm ? "+" : "")}{(_frozenTm ? "TM" : "")} frozen"
                : "caps: none";
        }

        // Guide ch5 24h structure: Normal NGUs most of the run, EVIL NGUs the LAST N hours (N = T7 versions
        // defeated; 1h post-T7v1, 2h post-T7v2 …). Replaces the profile's hardcoded ~22h NGUDiff switch with
        // a dynamic one — but ONLY in the Ch.5 24h shape (T7-capable, Boss 125+) with a TIME-based rebirth
        // target; elsewhere the profile's NGUDiff owns the track. UNTESTED until Boss 125+ (T7-version read
        // via TitanVersion(6)-1 is a first cut).
        private static void TickNguTrack(Character c)
        {
            try
            {
                int chapter = 0;
                try { chapter = StageDetector.Detect().Chapter; } catch { }
                double target = -1;
                try { target = Main.Profile != null ? Main.Profile.NextRebirthTargetSeconds() : -1; } catch { }
                if (chapter != 5 || ZoneHelpers.CurrentHighestBoss(c) < 125 || target <= 0) return;

                int t7 = 0;
                try { t7 = Math.Max(0, ZoneHelpers.TitanVersion(6) - 1); } catch { }
                if (t7 < 1) return; // no T7 version defeated → no Evil NGU hours (guide ch5 rule: N = versions defeated)
                double evilHours = t7;
                double switchAt = target - evilHours * 3600.0;
                var want = c.rebirthTime.totalseconds >= switchAt ? difficulty.evil : difficulty.normal;

                if (c.settings.nguLevelTrack != want)
                {
                    c.settings.nguLevelTrack = want;
                    try { c.NGUController.refreshMenu(); } catch { }
                    ChallengeOverlay.Record("NGU track", $"→ {(want == difficulty.evil ? "EVIL" : "Normal")} NGUs",
                        $"guide ch5: last {evilHours:0}h evil (T7 v{t7} done)");
                }
            }
            catch (Exception e) { Main.LogDebug($"LevelPlanner NGU track: {e.Message}"); }
        }

        // ---- Purpose-driven AT caps (slots 2..4), live every tick while the auto profile runs. ----

        private static void TickPurposeTargets(Character c)
        {
            try
            {
                if (!c.buttons.advancedTraining.interactable) return;
                var targets = c.advancedTraining.levelTarget;
                if (targets == null || targets.Length < 5) return;

                if (!_purposeOn)
                {
                    _purposeSnapshot = new[] { targets[2], targets[3], targets[4] };
                    _purposeOn = true;
                    ChallengeOverlay.Record("AT purpose caps on",
                        "block → 99% reduction · wandoos ATs → 1% dump cost · rest to Power/Toughness");
                }

                ApplyPurpose(targets, 2, BlockStopLevel(c));
                // Wandoos-AT stops are measured against the ACTIVE loadout's E/M caps, so they run only in
                // segments where Wandoos is the INTENDED sink: NGU MARATHON, plus on Evil the EVIL CLIMB /
                // AUGMENTATION phases (Wandoos is a primary early-Evil E/M sink). NOT AT HOUR — its weaker
                // caps inflate the targets and steal AT from Power/Toughness (user-caught). Extending to the
                // Evil climb fixes a STALE -1: the profile-owned marathon never runs during the climb, so the
                // Wandoos ATs kept the Normal-era -1 and never boosted E/M Wandoos (user-caught 2026-07-17).
                // (ApplyPurpose sets, doesn't ratchet, so a brief overshoot self-corrects next tick.)
                string wanSeg = ChallengeOverlay.Segment;
                if (wanSeg == "NGU MARATHON" || wanSeg == "EVIL CLIMB" || wanSeg == "AUGMENTATION")
                {
                    ApplyPurpose(targets, 3, WandoosStopLevel(c, energy: true));
                    ApplyPurpose(targets, 4, WandoosStopLevel(c, energy: false));
                }
            }
            catch (Exception e) { Main.LogDebug($"LevelPlanner purpose caps: {e.Message}"); }
        }

        private static void ApplyPurpose(long[] targets, int slot, long stop)
        {
            if (stop == long.MinValue) return;   // unknown — leave the current target alone
            if (targets[slot] != stop) targets[slot] = stop;
        }

        // Block stops at 99% damage reduction. The game's blockBonus = 0.5 / (1 + f·L) (remaining
        // damage fraction; tooltip shows reduction = 1 − that), so 99% needs f·L >= 49.
        private static long BlockStopLevel(Character c)
        {
            try
            {
                float f = c.advancedTrainingController.block.levelFactor;
                if (f <= 0) return long.MinValue;
                return (long)Math.Ceiling(49.0 / f);
            }
            catch { return long.MinValue; }
        }

        // Wandoos ATs stop once the Wandoos cap-speed dump costs <= 1% of max E/M. The dump cost
        // is baseTime / totalWandoosSpeed, and speed scales with (1 + f·L) — solve for L. The
        // stop moves as the OS levels raise baseTime during the run; recomputed live. Reads
        // CURRENT gear (cap + speed) — callers must invoke this during the marathon only.
        private static long WandoosStopLevel(Character c, bool energy)
        {
            try
            {
                if (!c.buttons.wandoos.interactable || c.wandoos98.disabled) return long.MinValue;
                var ctl = energy ? c.advancedTrainingController.wandoosEnergy : c.advancedTrainingController.wandoosMagic;
                float f = ctl.levelFactor;
                if (f <= 0) return long.MinValue;

                long lvl = c.advancedTraining.level[energy ? 3 : 4];
                double speed = energy ? c.totalWandoosEnergySpeed() : c.totalWandoosMagicSpeed();
                double baseTime = energy ? (double)c.wandoos98Controller.baseEnergyTime() : (double)c.wandoos98Controller.baseMagicTime();
                double cap = energy ? (double)c.totalCapEnergy() : (double)c.totalCapMagic();
                if (speed <= 0 || cap <= 0 || baseTime <= 0) return long.MinValue;

                double sOther = speed / (1.0 + f * lvl);          // speed without this AT's factor
                double needFactor = baseTime / (0.01 * cap * sOther);   // required (1 + f·L)
                double levels = (needFactor - 1.0) / f;
                if (levels <= 0) return -1;                       // already <= 1% at level 0: hold
                return (long)Math.Ceiling(levels);
            }
            catch { return long.MinValue; }
        }

        private static void ThawPurpose(Character c)
        {
            try
            {
                if (_purposeSnapshot != null)
                {
                    var targets = c.advancedTraining.levelTarget;
                    if (targets != null && targets.Length >= 5)
                    {
                        targets[2] = _purposeSnapshot[0];
                        targets[3] = _purposeSnapshot[1];
                        targets[4] = _purposeSnapshot[2];
                    }
                }
            }
            catch { }
            _purposeSnapshot = null;
            _purposeOn = false;
            ChallengeOverlay.Record("AT purpose caps off", "auto profile off — user targets restored");
        }

        // ---- Sufficiency freeze: Power/Toughness (slots 0..1) only — 2..4 are purpose-owned. ----

        private static void FreezeAt(Character c)
        {
            var targets = c.advancedTraining.levelTarget;
            _atSnapshot = new long[2];
            for (int i = 0; i < 2 && i < targets.Length; i++)
            {
                _atSnapshot[i] = targets[i];
                long lvl = c.advancedTraining.level[i];
                targets[i] = lvl > 0 ? lvl : -1;   // -1 = hold at zero (target 0 means uncapped)
            }
            _frozenAt = true;
            ChallengeOverlay.Record("Power/Toughness capped at current levels", "AK requirement beaten ×1.1 — energy to the marathon");
        }

        private static void ThawAt(Character c)
        {
            if (_atSnapshot != null)
            {
                var targets = c.advancedTraining.levelTarget;
                for (int i = 0; i < 2 && i < targets.Length && i < _atSnapshot.Length; i++)
                    targets[i] = _atSnapshot[i];
            }
            _atSnapshot = null;
            _frozenAt = false;
            ChallengeOverlay.Record("Power/Toughness caps released", "push/AK target needs stats again");
        }

        private static void FreezeTm(Character c)
        {
            _speedSnapshot = c.machine.speedTarget;
            _multiSnapshot = c.machine.multiTarget;
            c.machine.speedTarget = Math.Max(1, c.machine.levelSpeed);
            c.machine.multiTarget = Math.Max(1, c.machine.levelGoldMulti);
            _frozenTm = true;
            ChallengeOverlay.Record("TM capped at current levels", "gold funded — energy/magic to the marathon");
        }

        private static void ThawTm(Character c)
        {
            c.machine.speedTarget = _speedSnapshot;
            c.machine.multiTarget = _multiSnapshot;
            _frozenTm = false;
            ChallengeOverlay.Record("TM caps released", "gold needs levels again");
        }

        private static void ThawAll(Character c)
        {
            if (_frozenAt) ThawAt(c);
            if (_frozenTm) ThawTm(c);
            if (_purposeOn) ThawPurpose(c);
        }
    }
}
