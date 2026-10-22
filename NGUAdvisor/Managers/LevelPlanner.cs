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
    //        with 10% headroom (TitanAk table), or while the whole titan ladder at this difficulty
    //        is auto-killed; an UNREADABLE objective fails closed and keeps leveling. A new
    //        titan/version target automatically thaws.
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
            //
            // An unreadable objective must NOT read as "stats are sufficient" — that froze P/T on a
            // table miss (SpendPlanner fails the same shape closed for the same reason). But
            // NextObjective() returns Known == false for two different situations and only ONE of them
            // justifies the freeze, so the term cannot simply be dropped either:
            //   LadderComplete  → every titan+version at this difficulty is auto-killed. Nothing is
            //                     left to beat, so freezing P/T is CORRECT — leveling them further
            //                     would spend AT on a fight that does not exist.
            //   !LadderComplete → the requirement could not be read (AK-table miss / exception). Fail
            //                     CLOSED: keep leveling P/T, because a silent skip read as
            //                     "sufficient" is the more expensive mistake.
            bool atSufficient = false;
            string atBasis = "unreadable — objective probe threw (failing closed)";
            try
            {
                var obj = OptimizationAdvisor.NextObjective();
                if (obj.Known)
                {
                    atSufficient = c.totalAdvAttack() >= obj.ReqAttack * 1.1
                                   && c.totalAdvDefense() >= obj.ReqDefense * 1.1;
                    atBasis = $"stats vs T{obj.Index + 1}v{obj.Version} {obj.Stage} x1.1";
                }
                else if (obj.LadderComplete)
                {
                    atSufficient = true;
                    atBasis = "ladder complete — every titan at this difficulty auto-killed";
                }
                else
                {
                    atSufficient = false;
                    atBasis = "unreadable — no AK requirement for an un-AK'd version (failing closed)";
                }
            }
            catch { }

            bool wantAt = marathon && atSufficient;
            if (wantAt && !_frozenAt) FreezeAt(c);
            else if (!wantAt && _frozenAt) ThawAt(c);

            // TM sufficiency: funded and not starving EITHER gold sink.
            //
            // Diggers belong in this test as much as augments do, and leaving them out froze the TM
            // whenever augments happened to be paid up — cutting the gold supply while digger upgrades
            // were still unfunded. That matters because a digger exists for nearly every system: drop
            // chance, adventure stats, the NGUs, PP. Gold is a universal input, so "are the augments
            // fine" is only half the question (user-caught 2026-08-10).
            //
            // BloodPlanner.cs:344 already asks it both ways for the same decision; this now matches it.
            bool goldOk = false;
            try
            {
                goldOk = c.machine.realBaseGold > 0
                         && !OptimizationAdvisor.GoldStarvedForAugs(c, 1.0)
                         && !OptimizationAdvisor.GoldStarvedForDiggers(c, 1.0);
            }
            catch { }

            bool wantTm = marathon && goldOk;
            if (wantTm && !_frozenTm) FreezeTm(c);
            else if (!wantTm && _frozenTm) ThawTm(c);

            Status = _frozenAt || _frozenTm
                ? $"caps: {(_frozenAt ? "AT" : "")}{(_frozenAt && _frozenTm ? "+" : "")}{(_frozenTm ? "TM" : "")} frozen"
                : "caps: none";

            LogCapDbg(c, marathon, atSufficient, atBasis, goldOk);
        }

        // WHICH target is held, and why. Every reason here used to go to ChallengeOverlay.Record only — the
        // in-app feed — so debug.log had nothing at all on the caps, and the inputs (the AK sufficiency test,
        // the two gold-starvation probes, the solved Block/Wandoos stop levels) are all transient.
        //
        // Discipline is [TitanGoldDbg]'s: 60 s cadence cap FIRST — Tick() runs on every advisor tick and this
        // is the one channel where that genuinely matters — then emit only when the rendered line CHANGES.
        // The gold probes are re-read here rather than threaded out of the decision so the `goldOk` expression
        // above keeps its exact short-circuit; they are two small loops over augment/digger costs.
        private static string _lastCapDbg;
        private static DateTime _lastCapDbgAt = DateTime.MinValue;

        private static void LogCapDbg(Character c, bool marathon, bool atSufficient, string atBasis, bool goldOk)
        {
            try
            {
                if ((DateTime.UtcNow - _lastCapDbgAt).TotalSeconds < 60) return;
                _lastCapDbgAt = DateTime.UtcNow;

                bool augStarved = false, digStarved = false;
                try { augStarved = OptimizationAdvisor.GoldStarvedForAugs(c, 1.0); } catch { }
                try { digStarved = OptimizationAdvisor.GoldStarvedForDiggers(c, 1.0); } catch { }

                string slots = "unknown";
                try
                {
                    var t = c.advancedTraining.levelTarget;
                    var l = c.advancedTraining.level;
                    if (t != null && l != null && t.Length >= 5 && l.Length >= 5)
                    {
                        string seg = ChallengeOverlay.Segment;
                        // While a temp loadout is worn the stops are not solved at all (they would be
                        // solved from gear we are not keeping), so the log must not print a number
                        // either — `tempgear` is the honest value.
                        bool tempGear = TempLoadoutWorn();
                        string inE = "", inM = "";
                        string wanE = tempGear ? "tempgear" : StopText(WandoosStopLevel(c, true, out inE));
                        string wanM = tempGear ? "tempgear" : StopText(WandoosStopLevel(c, false, out inM));
                        string wanState = tempGear && (WandoosStopsOwned(seg) || seg == "AT HOUR")
                            ? $"held ({LockManager.GetLockTypeName()} loadout worn)"
                            : WandoosStopsOwned(seg) ? "applied"
                            : seg != "AT HOUR" ? "paused (not this segment)"
                            : _marathonStopE == long.MinValue && _marathonStopM == long.MinValue
                                ? "AT HOUR live (no marathon stop measured yet)"
                                : "AT HOUR min(live, marathon)";
                        slots = $"tough=lvl{l[0]}/tgt{t[0]} power=lvl{l[1]}/tgt{t[1]}"
                              + $" block=lvl{l[2]}/tgt{t[2]}/stop{StopText(BlockStopLevel(c))}"
                              + $" wanE=lvl{l[3]}/tgt{t[3]}/stop{wanE}/marathon{StopText(_marathonStopE)} {inE}"
                              + $" wanM=lvl{l[4]}/tgt{t[4]}/stop{wanM}/marathon{StopText(_marathonStopM)} {inM}"
                              + $" wanCaps={wanState}";
                    }
                }
                catch { }

                string line = $"[CapDbg] seg={ChallengeOverlay.Segment ?? "none"} marathon={marathon}"
                            + $" at={(_frozenAt ? "FROZEN" : "free")} atSufficient={atSufficient} atBasis={atBasis}"
                            + $" tm={(_frozenTm ? "FROZEN" : "free")} goldOk={goldOk}"
                            + $" augStarved={augStarved} digStarved={digStarved}"
                            + $" tmBase={NumberFormatter.Abbrev(c.machine.realBaseGold)}"
                            + $" tmSpeed=lvl{c.machine.levelSpeed}/tgt{c.machine.speedTarget}"
                            + $" tmMulti=lvl{c.machine.levelGoldMulti}/tgt{c.machine.multiTarget}"
                            + $" purpose={(_purposeOn ? "on" : "off")} {slots}";
                if (line == _lastCapDbg) return;
                _lastCapDbg = line;
                Main.LogDebug(line);
            }
            catch (Exception e) { Main.LogDebug($"[CapDbg] failed: {e.Message}"); }
        }

        private static string StopText(long stop)
            => stop == long.MinValue ? "unknown" : stop < 0 ? "hold" : stop.ToString();

        // Guide ch5 Evil day: Normal NGUs most of the run, EVIL NGUs the last hour plus one per T7 version
        // defeated (RunShape.EvilNguStartSec). Replaces the profile's hardcoded NGUDiff switch on the Evil day
        // (long rebirths, the climb to Boss 125 included); elsewhere the profile's NGUDiff owns the track.
        // The T7-version count is read from the bestiary (ZoneHelpers.TitanVersionsBeaten) — the earlier
        // `TitanVersion(6)-1` was the difficulty the player had SELECTED in the zone, not versions
        // defeated, so it granted Evil NGU hours for touching a dropdown and none for actual kills.
        // The NGU track the user pinned in Settings, or null for "Advisor" (profile + Evil day decide).
        public static difficulty? PinnedNguTrack()
        {
            switch (Main.Settings?.NguTrack)
            {
                case "Normal": return difficulty.normal;
                case "Evil": return difficulty.evil;
                case "Sadistic": return difficulty.sadistic;
                default: return null;
            }
        }

        // The game never runs a track above the run's difficulty (decomp Rebirth.startNormalRebirth /
        // startHardRebirth clamp nguLevelTrack), so a pin above it runs at the run's difficulty.
        public static void ApplyPinnedNguTrack(Character c, difficulty pinned)
        {
            var want = pinned > c.settings.rebirthDifficulty ? c.settings.rebirthDifficulty : pinned;
            if (c.settings.nguLevelTrack == want) return;
            c.settings.nguLevelTrack = want;
            try { c.NGUController.refreshMenu(); } catch (Exception e) { Main.LogDebug($"NGU menu refresh: {e.Message}"); }
            ChallengeOverlay.Record("NGU track", $"→ {want} NGUs", "pinned in Settings");
        }

        private static void TickNguTrack(Character c)
        {
            if (PinnedNguTrack().HasValue) return;
            try
            {
                double target = ChallengeOverlay.RebirthTargetSec();
                if (!RunShape.IsEvilDay(ChallengeOverlay.Chapter(), target)) return;

                int t7 = 0;
                try { t7 = ZoneHelpers.TitanVersionsBeaten(6); } catch { }
                double switchAt = RunShape.EvilNguStartSec(target, t7);
                double evilHours = 1 + t7;
                var want = !double.IsNaN(switchAt) && c.rebirthTime.totalseconds >= switchAt
                    ? difficulty.evil : difficulty.normal;

                if (c.settings.nguLevelTrack != want)
                {
                    c.settings.nguLevelTrack = want;
                    try { c.NGUController.refreshMenu(); } catch { }
                    ChallengeOverlay.Record("NGU track", $"→ {(want == difficulty.evil ? "EVIL" : "Normal")} NGUs",
                        $"guide ch5: last {evilHours:0}h evil ({t7} T7 versions beaten)");
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
                // The stops are solved from the worn gear, and a temp loadout is not the gear we keep;
                // ApplyPurpose ignores long.MinValue, so "leave the target alone" needs no extra branch.
                // The game keeps levelTarget across rebirths, hence the explicit pause elsewhere.
                string seg = ChallengeOverlay.Segment;
                bool tempGear = TempLoadoutWorn();
                long stopE = tempGear ? long.MinValue : WandoosStopLevel(c, energy: true);
                long stopM = tempGear ? long.MinValue : WandoosStopLevel(c, energy: false);
                if (WandoosStopsOwned(seg))
                {
                    if (seg == "NGU MARATHON") RememberMarathonStops(stopE, stopM);
                    ApplyPurpose(targets, 3, stopE);
                    ApplyPurpose(targets, 4, stopM);
                }
                else if (seg == "AT HOUR")
                {
                    if (!tempGear)
                    {
                        ApplyPurpose(targets, 3, AtHourStop(stopE, _marathonStopE));
                        ApplyPurpose(targets, 4, AtHourStop(stopM, _marathonStopM));
                    }
                }
                else
                {
                    ApplyPurpose(targets, 3, PausedTarget);
                    ApplyPurpose(targets, 4, PausedTarget);
                }
            }
            catch (Exception e) { Main.LogDebug($"LevelPlanner purpose caps: {e.Message}"); }
        }

        // Is a mode lock's TEMP loadout worn right now? CanSwap() lets the quest lock through, but
        // quest gear IS equipped then (AdvisorApply.ApplyGearRefresh says the same), so both terms are
        // needed. Only the gear-dependent solves care: Block's stop is a pure game-formula solve on
        // block.levelFactor and reads no gear at all, so it keeps running through locks.
        internal static bool TempLoadoutWorn() => !LockManager.CanSwap() || LockManager.HasQuestLock();

        // The game's levelTarget value that pauses an AT slot (0 would mean uncapped).
        private const long PausedTarget = -1;

        private static bool WandoosStopsOwned(string segment)
            => segment == "NGU MARATHON" || segment == "EVIL CLIMB" || segment == "AUGMENTATION";

        // The last stop solved in NGU MARATHON, the segment whose gear the Wandoos dump actually runs on.
        // Static on purpose: it must survive the rebirth into the next run's AT HOUR.
        private static long _marathonStopE = long.MinValue, _marathonStopM = long.MinValue;

        private static void RememberMarathonStops(long stopE, long stopM)
        {
            if (stopE != long.MinValue) _marathonStopE = stopE;
            if (stopM != long.MinValue) _marathonStopM = stopM;
        }

        // AT HOUR feeds AT, but in AT gear: a weaker cap there would inflate the live solve, and AT
        // levels cannot be un-bought. So a known marathon stop bounds it; without one the live solve
        // stands (LevelPlanner.md, "Why AT HOUR is bounded by the marathon").
        private static long AtHourStop(long liveStop, long marathonStop)
        {
            if (marathonStop == long.MinValue) return liveStop;
            if (liveStop == long.MinValue) return marathonStop;
            return Math.Min(liveStop, marathonStop);
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

        // Wandoos ATs stop once the Wandoos cap-speed dump costs <= 1% of max E/M, solved by
        // AtMath.WandoosStopLevel against the fully booted, digger-free speed. Reads CURRENT gear
        // (cap + speed), so never call it while a mode lock's temp loadout is worn (TempLoadoutWorn()).
        private const double WandoosDumpShare = 0.01;

        private static long WandoosStopLevel(Character c, bool energy) => WandoosStopLevel(c, energy, out _);

        private static long WandoosStopLevel(Character c, bool energy, out string inputs)
        {
            inputs = "unreadable";
            try
            {
                if (!c.buttons.wandoos.interactable || c.wandoos98.disabled) return long.MinValue;
                var ctl = energy ? c.advancedTrainingController.wandoosEnergy : c.advancedTrainingController.wandoosMagic;
                float f = ctl.levelFactor;
                long lvl = c.advancedTraining.level[energy ? 3 : 4];
                double speed = energy ? c.totalWandoosEnergySpeed() : c.totalWandoosMagicSpeed();
                double baseTime = energy ? (double)c.wandoos98Controller.baseEnergyTime() : (double)c.wandoos98Controller.baseMagicTime();
                double cap = energy ? (double)c.totalCapEnergy() : (double)c.totalCapMagic();
                double boot = c.wandoos98Controller.bootupSpeedFactor();
                double fullBoot = c.inventory.itemList.wandoosComplete ? 1.1 : 1.0;
                double digger = c.allDiggers.totalWandoosSpeedBonus();
                inputs = $"cap={NumberFormatter.Abbrev(cap)} boot={boot:0.###} digger={digger:0.###}";

                double? baseSpeed = AtMath.WandoosBaseSpeed(speed, 1.0 + f * lvl, boot, fullBoot, digger);
                if (!baseSpeed.HasValue) return long.MinValue;
                long? stop = AtMath.WandoosStopLevel(baseTime, cap, baseSpeed.Value, f, WandoosDumpShare);
                if (!stop.HasValue) return long.MinValue;
                return stop.Value > 0 ? stop.Value : PausedTarget;   // already <= 1% at level 0: hold
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
