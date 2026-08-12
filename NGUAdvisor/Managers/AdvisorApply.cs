using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // Route C3 Phase B: opt-in auto-apply. When a system's toggle is on, the advisor's goal-aware
    // recommendation is applied instead of only being displayed. Runs from Main's 10s loop
    // (main thread), guarded, throttled, and logs every change it makes.
    //
    // Safety rules:
    //  - Master automation (GlobalEnabled) must be on.
    //  - Never acts while LockManager holds a titan/ygg/pit/gold/cooking lock (mode swaps own the sets).
    //  - Diggers/beards: never acts inside a challenge (challenge-tagged profile breakpoints own them);
    //    while enabled, profile digger/beard timelines are substituted with the advisor set.
    //  - Wandoos OS: switching wipes E/M Dump levels, so we only switch when one projected HOUR on the
    //    better OS (from zero levels) still beats 1.5x the bonus you currently have — i.e. the switch
    //    pays for itself within the hour. Throttled to one switch per 10 minutes.
    public static class AdvisorApply
    {
        private static DateTime _lastTick = DateTime.MinValue;
        private static DateTime _lastOsSwitch = DateTime.MinValue;

        // ---- fault containment (stage R2) ----
        //
        // ONE APPLIER'S EXCEPTION USED TO KILL EVERY LATER ONE. All of these ran under a single outer catch,
        // so a throw in ApplyPerks — position five of nineteen — silently skipped EXP buys, titan gold, gold,
        // PIT, quests, blood, boost priority, zones, titans and transforms for that tick. And five of them
        // (Diggers, Beards, Perks, Quirks, Ygg buys) have NO throttle: they run every 30s tick, so a
        // persistent fault in any of them starved the entire tail permanently. The throttled ones were worse
        // to diagnose, not better — their throttle short-circuits on the alternate ticks, so the starvation
        // was INTERMITTENT, and automation that half-works reads like a game bug rather than an exception.
        //
        // The fix is containment, not a framework: catch per step, name the step, keep going. Twelve of the
        // nineteen operations get this. The other seven already catch their own complete bodies and are left
        // strictly alone — wrapping them again would just double-log.
        private sealed class Fault
        {
            public int Consecutive;      // failures in this episode, total
            public int SinceReport;      // failures since we last said anything
            public DateTime FirstAt;
            public DateTime LastAt;      // last EXCEPTION — the quiet window is measured from here
            public DateTime LastReportAt;
            public string Type;
            public string Message;
        }

        private static readonly Dictionary<string, Fault> _faults = new Dictionary<string, Fault>();

        // The step is the rate-limiting boundary, NOT the exception message: messages carry ids, values and
        // object descriptions that change every throw, so keying on them would let a "new" signature flood
        // both logs at 30-second intervals — which is the noise this exists to prevent.
        private static readonly TimeSpan ReportEvery = TimeSpan.FromMinutes(10);

        // The quiet-window message states the ACTUAL interval, derived from the constant above rather than
        // typed into the string. A probe build that shortens ReportEvery to two minutes must SAY two minutes;
        // a message that hardcodes "10" would be a lie in exactly the build you are using to check that the
        // messages tell the truth.
        private static string ReportWindowText
        {
            get
            {
                double m = ReportEvery.TotalMinutes;
                return m == 1 ? "1 minute" : $"{m:0} minutes";
            }
        }

        private const string TickStep = "Advisor tick";

        // Runs one applier. The GATE stays outside: a disabled or throttled step never reaches here as a
        // failure, and a step that returns early because it has nothing to do is a SKIP, not a success worth
        // announcing. Silence is the normal case and the only correct one.
        //
        // A NONTHROWING RETURN IS NOT NECESSARILY COMPLETED WORK. This helper sees "the call came back"; it
        // cannot see whether the applier did anything, because every applier's throttle, disabled-feature and
        // nothing-to-do exits are plain `return`s indistinguishable from a full successful run. Everything
        // downstream of here is named for what is actually observable, not for what we would like it to mean.
        private static void RunStep(string name, Action action)
        {
            try { action(); }
            catch (Exception e) { OnStepFailed(name, e); return; }
            ObserveStepReturn(name);
        }

        private static void OnStepFailed(string name, Exception e)
        {
            var now = DateTime.UtcNow;
            Fault f;

            if (!_faults.TryGetValue(name, out f))
            {
                // First failure of a healthy step: say so at once, with the one full stack for this episode.
                f = new Fault
                {
                    Consecutive = 1,
                    SinceReport = 0,
                    FirstAt = now,
                    LastAt = now,
                    LastReportAt = now,
                    Type = e.GetType().Name,
                    Message = e.Message
                };
                _faults[name] = f;
                Main.Log($"Advisor: {name} failed — {f.Type}: {f.Message}");
                Main.LogDebug($"Advisor step {name} failed:\n{e}");
                return;
            }

            f.Consecutive++;
            f.SinceReport++;
            f.LastAt = now;
            f.Type = e.GetType().Name;      // latest signature, for the next periodic report
            f.Message = e.Message;

            if (now - f.LastReportAt < ReportEvery) return;   // suppressed: no session line, no stack

            Main.Log($"Advisor: {name} still failing — {f.SinceReport} failure(s) since the last report; latest {f.Type}: {f.Message}");
            Main.LogDebug($"Advisor step {name} still failing ({f.Consecutive} consecutive since {f.FirstAt:HH:mm:ss}):\n{e}");
            f.LastReportAt = now;
            f.SinceReport = 0;
        }

        // WE DO NOT KNOW THAT IT RECOVERED. We know it stopped throwing. Those are different claims, and this
        // method is careful to make only the second one.
        //
        // Clearing the fault on the first nonthrowing return cannot work, because a THROTTLED SKIP is a
        // nonthrowing return: ApplyExpBuys comes back clean on every tick inside its 60s window without
        // touching the failing path at all. Clear-on-return would therefore read that skip as a recovery,
        // close the episode, and let the NEXT real attempt open a fresh one and report as a first failure —
        // fail, "recovered", fail, "recovered", twice a minute forever, each with a failure count of one. A
        // worse flood than the one this exists to fix, and a lying one.
        //
        // So the episode is measured from the last EXCEPTION, not the first return: it clears once the step
        // has gone a full reporting interval without throwing again. One rule, both problems — a throttled
        // skip cannot forge a clearance (the next failure is at most one throttle away, well inside the
        // window), and a step that has genuinely stopped failing says so once and goes quiet.
        //
        // What the message may claim: no exception for ten minutes. What it must NOT claim: that the applier
        // did any work, that the failing path was exercised at all, or that the defect is fixed. It may have
        // been throttled, disabled mid-episode, or had nothing to do for the whole window. The wording is
        // observational on purpose. The cost — the line can lag a real fix by up to the interval — is the
        // price of never asserting something we cannot see.
        private static void ObserveStepReturn(string name)
        {
            Fault f;
            if (!_faults.TryGetValue(name, out f)) return;          // healthy: silent, as it is ~always
            if (DateTime.UtcNow - f.LastAt < ReportEvery) return;   // episode still open

            _faults.Remove(name);
            Main.Log($"Advisor: {name} fault quiet for {ReportWindowText} after {f.Consecutive} failure(s).");
            Main.LogDebug($"Advisor step {name} fault state cleared after {ReportWindowText} without another exception; "
                        + $"{f.Consecutive} failure(s) observed; latest was {f.Type}: {f.Message}");
        }

        public static void Tick()
        {
            try
            {
                if (Main.Settings == null || !Main.Settings.GlobalEnabled) return;
                var c = Main.Character;
                if (c == null) return;
                if ((DateTime.UtcNow - _lastTick).TotalSeconds < 30) return;
                _lastTick = DateTime.UtcNow;

                // Challenge overlay first: it sets the gear-objective override the gear refresh
                // below consults (and clears itself outside challenges / when toggled off).
                // Routed through RunStep now (R11): its own outer whole-Tick catch was removed so the
                // bounded reporter owns the fault — nested sub-catches inside it stay.
                RunStep("Challenge overlay", ChallengeOverlay.Tick);
                // Level caps ride the segment the overlay just computed (self-gates on AutoProfile).
                RunStep("Level planner", LevelPlanner.Tick);

                // Set/gear appliers must not fight a mode lock's temporary swaps; the purchase and
                // routing appliers below touch nothing a lock owns, so they keep running during locks
                // (audit fix: previously a titan wait stalled perk/EXP/blood automation for no reason).
                //
                // CanSwap() is evaluated ONCE, exactly where it always was — the containment goes around each
                // applier, never around the policy that decides whether it runs, and never re-checks the lock
                // between them.
                if (LockManager.CanSwap())
                {
                    if (Main.Settings.AdvisorDiggers) RunStep("Diggers", () => ApplyDiggers(c));
                    if (Main.Settings.AdvisorBeards) RunStep("Beards", () => ApplyBeards(c));
                    if (Main.Settings.AdvisorWandoosOS) RunStep("Wandoos OS", () => ApplyWandoosOs(c));
                    if (Main.Settings.AdvisorGearRefresh) RunStep("Gear refresh", ApplyGearRefresh);
                }

                if (Main.Settings.AdvisorPerks) RunStep("Perks", ApplyPerks);
                if (Main.Settings.AdvisorQuirks) RunStep("Quirks", ApplyQuirks);
                if (Main.Settings.AdvisorYggBuys) RunStep("Ygg buys", ApplyYggBuys);
                if (Main.Settings.AdvisorExpBuys) RunStep("EXP buys", ApplyExpBuys);
                // ApplyTitanGold's inner try guards only the version lookup — the body, including two
                // persisted writes, was exposed. The whole call is contained now.
                if (Main.Settings.AutoTitanGold || Main.Settings.AdvisorGold) RunStep("Titan gold", ApplyTitanGold);
                if (Main.Settings.AdvisorGold || Main.Settings.SnipeOnGoldStarved) ApplyGold();      // gated: reports via OnStepFailed
                if (Main.Settings.AdvisorPit) ApplyPit();                                            // gated: reports via OnStepFailed
                if (Main.Settings.AdvisorQuests) ApplyQuests();                                      // gated: reports via OnStepFailed
                if (Main.Settings.AdvisorBlood) RunStep("Blood", ApplyBlood);
                if (Main.Settings.AutoBoostPriority) RunStep("Boost priority", ApplyBoostPriority);
                // Gear Hunt routes the zone even in MANUAL ZONE mode — the toggle is the intent.
                if (Main.Settings.AdvisorZones || GearHunter.Active) RunStep("Zones", ApplyZones);
                if (Main.Settings.AdvisorTitans) ApplyTitans();                                      // gated: reports via OnStepFailed
                RunStep("Transforms", TransformManager.Tick);

                ObserveStepReturn(TickStep);
            }
            // FINAL SAFETY BOUNDARY, and now a bounded one. Nothing a RunStep caught can reach here, so this
            // only fires for orchestration itself — a gate getter, CanSwap(), the helper. That is a more
            // serious fault than any single applier (the whole tick died), so it is session-visible, and it
            // goes through the same per-step interval rather than writing a stack to debug.log every 30s.
            catch (Exception e) { OnStepFailed(TickStep, e); }
        }

        // Advisor-driven boost priority (Boosts tab, ADVISOR ACTIVE): recompute the ranked list every
        // 10 minutes (it runs the full objective sweep) and write it into the existing priority-boost
        // pipeline. Manual mode leaves Settings.PriorityBoosts entirely alone.
        private static DateTime _lastBoostPrio = DateTime.MinValue;

        private static void ApplyBoostPriority()
        {
            if (!Main.Settings.ManageInventory) return;
            if ((DateTime.UtcNow - _lastBoostPrio).TotalMinutes < 10) return;
            _lastBoostPrio = DateTime.UtcNow;

            var v = InventoryAdvisor.Compute();
            var ids = InventoryAdvisor.AutoBoostPriority(v);
            var cur = Main.Settings.PriorityBoosts ?? new int[0];
            if (!ids.SequenceEqual(cur))
            {
                Main.Settings.PriorityBoosts = ids;
                Main.Log($"Advisor: boost priority -> {(ids.Length > 0 ? string.Join(", ", ids.Select(x => x.ToString()).ToArray()) : "(equipped only)")}");
            }
        }

        private static void ApplyDiggers(Character c)
        {
            var set = OptimizationAdvisor.CurrentDiggerSet();
            if (set == null || set.Length == 0) return;

            // Converge membership in place (obsolete off, affordable-missing on, correct left alone), then
            // ALWAYS re-level whatever ended up active. Leveling must NOT be gated on the full
            // recommendation activating: a recommended digger that can't afford even level 1 (the Evil
            // Blood digger, base drain ~1e24 >> gross ~5e21) can NEVER activate, so the old "recap only on
            // a complete set" gate never opened and froze the whole set at level 1 the entire run (user-
            // caught on the Evil climb; the diagnostic showed d10 unaffordable, set never completed, so
            // RecapDiggers never ran — except the rare titan-window tick that displaced d10 with d0).
            // ReconcileAdvisorDiggers activates every affordable member at level 1 BEFORE we level, so
            // leveling can't shut out a member that could have run; the only members left off are ones that
            // couldn't afford level 1 regardless. Recap every pass so levels also track gross as it climbs.
            bool complete = DiggerManager.ReconcileAdvisorDiggers(set, out bool membershipChanged);
            // Pass the recommendation order explicitly: reconcile is membership-only and never updates
            // _curDiggers, so the parameterless RecapDiggers would level the greedy budget in a stale/null
            // order and discard the ranking (Adventure-leads / Stats-on-push / DC-on-titan). With `set`,
            // the greedy allocation levels high-priority diggers first — critical on a tight Evil budget.
            if (c.diggers.activeDiggers.Count > 0)
                DiggerManager.RecapDiggers(set);
            // Log only a real, complete equip (membership actually changed AND the whole set is live) —
            // the incomplete-but-leveling passes stay quiet so an unaffordable member can't spam the log.
            if (complete && membershipChanged)
                Main.Log($"Advisor: equipped diggers {string.Join(", ", set.Select(i => i.ToString()).ToArray())}");
        }

        private static void ApplyBeards(Character c)
        {
            var set = OptimizationAdvisor.CurrentBeardSet();
            if (set == null || set.Length == 0) return;
            var active = c.beards.activeBeards;
            if (active.Count == set.Length && set.All(active.Contains)) return;

            if (BeardManager.EquipBeards(set))
                Main.Log($"Advisor: equipped beards {string.Join(", ", set.Select(i => i.ToString()).ToArray())}");
        }

        // Guide-ordered spending (SpendPlanner): buy the next perk/quirk/fruit-tier in the guide's
        // chapter order whenever points/seeds cover it. Bounded per tick; every purchase is logged.
        private static void ApplyPerks()
        {
            int n = SpendPlanner.BuyPerks(50);
            if (n > 0)
            {
                var next = SpendPlanner.NextPerk();
                Main.Log($"Advisor: bought {n} perk level(s) toward the guide order{(next.Known ? $"; next: {next.Name}" : "")}");
            }
        }

        private static void ApplyQuirks()
        {
            int n = SpendPlanner.BuyQuirks(50);
            if (n > 0)
            {
                var next = SpendPlanner.NextQuirk();
                Main.Log($"Advisor: bought {n} quirk level(s) toward the guide order{(next.Known ? $"; next: {next.Name}" : "")}");
            }
        }

        private static void ApplyYggBuys()
        {
            var b = SpendPlanner.NextFruit();
            if (b.Known && b.Affordable && SpendPlanner.BuyFruitTier())
                Main.Log($"Advisor: bought {b.Name} tier {b.CurLevel + 1} for {b.Cost} seeds (guide order)");
        }

        // Blood planner auto: cast Iron Pill at the breakpoint-optimal moment (BloodPlanner decides;
        // the threshold path in CastBloodSpells is disabled for the pill while this is on).
        private static DateTime _lastBloodCheck = DateTime.MinValue;

        private static void ApplyBlood()
        {
            if (!Main.Settings.CastBloodSpells) return;
            if ((DateTime.UtcNow - _lastBloodCheck).TotalSeconds < 60) return;
            _lastBloodCheck = DateTime.UtcNow;

            var plan = BloodPlanner.Analyze();
            if (plan.Known && plan.CastIronNow)
                BloodMagicManager.ironPill.CastPlanned();

            // Route the investment auto-spells (the game splits blood evenly among enabled toggles;
            // pooling turns them all off so the Iron Pill can actually charge).
            BloodPlanner.FillRouting(ref plan);
            if (plan.RouteKnown)
            {
                var bm = Main.Character.bloodMagic;
                bool r = !plan.PoolForPill && plan.WantRebirth;
                bool l = !plan.PoolForPill && plan.WantLoot;
                bool g = !plan.PoolForPill && plan.WantGold;
                if (bm.rebirthAutoSpell != r || bm.lootAutoSpell != l || bm.goldAutoSpell != g)
                {
                    bm.rebirthAutoSpell = r;
                    bm.lootAutoSpell = l;
                    bm.goldAutoSpell = g;
                    Main.Log($"Advisor: blood routing -> {(plan.PoolForPill ? "pooling for Iron Pill (all auto-spells off)" : plan.RouteReason)}");
                }
            }
        }

        // Data-driven titan gold: target the most PROFITABLE autokill-able titan for the next gold bank,
        // and re-bank when its AK version rises. Replaces hand-picking TitanGoldTargets checkboxes; the
        // existing snapshot/lock machinery does the actual gold-gear swap on the AK cycle.
        private static DateTime _lastTitanGold = DateTime.MinValue;

        // Cached: AutokillAvailable for titans 6+ goes through reflection, and this is consulted by
        // the advisor's Power and Gold rows (2s cadence) as well as the titan-gold applier. AK status
        // changes on the scale of minutes, so 30s staleness is free performance.
        private static int _akTitan = -1;
        private static DateTime _akTitanAt = DateTime.MinValue;
        private static List<int> _akTitans = new List<int>();

        private static void RefreshAkTitans()
        {
            if ((DateTime.UtcNow - _akTitanAt).TotalSeconds < 30) return;
            _akTitanAt = DateTime.UtcNow;
            var ak = new List<int>();
            for (int i = 0; i < ZoneHelpers.TitanZones.Length; i++)
            {
                try { if (ZoneHelpers.AutokillAvailable(i)) ak.Add(i); }
                catch { }
            }
            _akTitans = ak;
            _akTitan = ak.Count > 0 ? ak[ak.Count - 1] : -1;
        }

        public static int HighestAkTitan()
        {
            RefreshAkTitans();
            return _akTitan;
        }

        // Which AK titan is worth the most gold — NOT the highest one. Titan gold is not monotone in
        // titan index (GoldDropTables, extracted from the decomp): T2 in zone 8 drops 400 000 while T3
        // in zone 11 drops only 300 000, and zones 44/45 (BEAST, THE TRAITOR) call no goldDrop at all.
        // Targeting by height therefore banked 300k where 400k was on offer, and would have banked
        // nothing at all if a zero-gold titan were ever the top AK one (user-reported: "sniping when it
        // shouldn't").
        //
        // Eligibility is unchanged and still owned by TitanKillWorthGoldGear (auto-killable, not inside
        // its DenyGoldSwap cooldown, drops gold at all) — this only decides WHICH of the eligible
        // candidates to target. There is deliberately still no payoff gate against the bank: the
        // auto-kill happens whether the gold set is equipped or not, so ranking picks which titan, never
        // whether to go at all (see docs/modules/GoldDropAdvisor.md).
        public static int BestGoldTitan(out double predicted, out int runnerUp, out double runnerUpPredicted)
        {
            RefreshAkTitans();
            int best = -1;
            predicted = 0;
            runnerUp = -1;
            runnerUpPredicted = 0;
            for (int k = 0; k < _akTitans.Count; k++)
            {
                int i = _akTitans[k];
                double p, bank;
                if (!GoldDropAdvisor.TitanKillWorthGoldGear(i, out p, out bank))
                    continue;
                if (best < 0 || p > predicted)
                {
                    runnerUp = best;
                    runnerUpPredicted = predicted;
                    best = i;
                    predicted = p;
                }
                else if (p > runnerUpPredicted)
                {
                    runnerUp = i;
                    runnerUpPredicted = p;
                }
            }
            if (best < 0)
                predicted = 0;
            return best;
        }

        // The gold-bank target ApplyTitanGold last picked, for the panels and the advice list: they run on
        // the WinForms thread and must never re-rank, because PredictedDrop reaches the gear optimizer.
        // Before the first pass (or with nothing eligible) it degrades to the height pick, which is what
        // those callers used to show anyway.
        private static int _goldTitan = -1;
        private static bool _goldTitanKnown;

        public static int GoldTitanTarget() => _goldTitanKnown && _goldTitan >= 0 ? _goldTitan : HighestAkTitan();

        // Why no gold gear on that autokill? Every input of the decision in one debug line, emitted only
        // when the answer changes — "the titan was auto-killed in the wrong gear" is otherwise invisible
        // after the fact: the deciding values (AK titan, ranked pick, done latch, predicted drop, TM bank)
        // are all transient. Same idea as [DiggerDbg].
        private static string _lastTitanGoldDbg;

        private static void LogTitanGoldState(int best, int ver, double predicted, bool worth, int runnerUp, double runnerUpPredicted)
        {
            try
            {
                double bank = GoldDropAdvisor.Banked();
                var done = Main.Settings.TitanMoneyDone;
                bool isDone = done != null && best >= 0 && best < done.Length && done[best];
                string beat = runnerUp >= 0
                    ? $"T{runnerUp + 1}@{NumberFormatter.Abbrev(runnerUpPredicted)}"
                    : "nothing";
                string line = $"[TitanGoldDbg] ak={(_akTitan >= 0 ? "T" + (_akTitan + 1) : "none")}"
                            + $" pick={(best >= 0 ? "T" + (best + 1) + " v" + ver : "none")} beat={beat}"
                            + $" done={isDone} pred={NumberFormatter.Abbrev(predicted)} bank={NumberFormatter.Abbrev(bank)}"
                            + $" vsBank={GoldDropAdvisor.PctVsBank(predicted, bank)}"
                            + $" worth={worth} gearx={GoldDropAdvisor.GoldGearFactor():0.##}"
                            + $" spawning={ZoneHelpers.SpawningSoonRawCount()}/targeted={ZoneHelpers.SpawningSoonCount()}"
                            + $" goldSwap={ZoneHelpers.ShouldRunGoldLoadout()} lock={LockManager.GetLockTypeName()}"
                            // The SnipeZone gates too: it owns the per-run reset of TitanMoneyDone, and if it
                            // returns early (paused / adventure locked) that reset silently never happens —
                            // which leaves last run's "already banked" latch blocking this run's gold.
                            + $" snipeComplete={Main.Settings.GoldSnipeComplete} adv={Main.Character.buttons.adventure.interactable}"
                            + $" global={Main.Settings.GlobalEnabled} tmOn={Main.Character.buttons.brokenTimeMachine.interactable}";
                if (line == _lastTitanGoldDbg) return;
                _lastTitanGoldDbg = line;
                Main.LogDebug(line);
            }
            catch (Exception e) { Main.LogDebug($"[TitanGoldDbg] failed: {e.Message}"); }
        }

        private static void ApplyTitanGold()
        {
            if (!Main.Settings.ManageGoldLoadouts) return;
            if ((DateTime.UtcNow - _lastTitanGold).TotalSeconds < 60) return;
            _lastTitanGold = DateTime.UtcNow;

            double predicted, runnerUpPredicted;
            int runnerUp;
            int best = BestGoldTitan(out predicted, out runnerUp, out runnerUpPredicted);
            bool worth = best >= 0;
            _goldTitan = best;
            _goldTitanKnown = true;
            // Nothing eligible (every AK titan denied, or none drops gold): fall back to the height pick so
            // the clearing pass below still runs — it is what removes a denied titan from TitanGoldTargets.
            if (!worth)
                best = HighestAkTitan();

            int ver = 1;
            if (best >= 0)
                try { ver = ZoneHelpers.TitanVersion(best); } catch { }
            LogTitanGoldState(best, ver, predicted, worth, runnerUp, runnerUpPredicted);
            if (best < 0) return;

            double bank = GoldDropAdvisor.Banked();

            // The "already banked this run" latch does NOT survive here any more (user-reported: an
            // auto-killed titan went down in loot gear because it had banked once already). The kill is
            // free, so every AK cycle is another chance at a bigger drop as the gold bonus grows — the
            // latch's real job is telling the kill-detection in ZoneHelpers that a bank was collected, and
            // it gets re-armed for the next spawn right here.
            var done = Main.Settings.TitanMoneyDone;
            if (worth && done != null && best < done.Length && done[best])
            {
                done[best] = false;
                Main.Settings.TitanMoneyDone = done;
                int akVer = 1;
                var banked = Main.Settings.TitanGoldVersionBanked;
                if (banked != null && best < banked.Length) akVer = banked[best];
                string why = akVer > 0 && akVer < ver ? $"AK version rose to v{ver}" : "the kill is free";
                Main.Log($"Advisor: re-banking gold on the next Titan {best + 1} kill ({why}; ~{NumberFormatter.Abbrev(predicted)} vs {NumberFormatter.Abbrev(bank)} banked)");
            }

            var targets = new bool[ZoneHelpers.TitanZones.Length];
            targets[best] = worth;
            var cur = Main.Settings.TitanGoldTargets;
            bool differs = cur == null || cur.Length != targets.Length;
            if (!differs)
                for (int i = 0; i < targets.Length; i++)
                    if (cur[i] != targets[i]) { differs = true; break; }
            if (differs)
            {
                Main.Settings.TitanGoldTargets = targets;
                if (targets[best])
                    Main.Log($"Advisor: targeting Titan {best + 1} (v{ver}) for the next gold bank (~{NumberFormatter.Abbrev(predicted)})");
            }
        }

        // Advisor gold (E1 pipeline): auto-CBlock while a challenge is active (challenge runs live on
        // zone sniping), and the gold-starvation snipe trigger (throttled — it re-runs the snipe when
        // augments can't be afforded despite TM holding gold).
        private static DateTime _lastGoldCheck = DateTime.MinValue;

        private static void ApplyGold()
        {
            if ((DateTime.UtcNow - _lastGoldCheck).TotalMinutes < 2) return;
            _lastGoldCheck = DateTime.UtcNow;

            try
            {
                var c = Main.Character;
                if (Main.Settings.AdvisorGold)
                {
                    string challenge = null;
                    try { challenge = ChallengeDetector.Current(); } catch { }
                    bool wantCBlock = challenge != null;
                    if (Main.Settings.GoldCBlockMode != wantCBlock && !Main.Settings.MoneyPitRunMode)
                    {
                        Main.Settings.GoldCBlockMode = wantCBlock;
                        Main.Log($"Advisor: gold snipe mode -> {(wantCBlock ? $"challenge ({challenge})" : "normal")}");
                    }
                }

                // Gold-drop growth trigger: the gold bonus keeps climbing during a run (NGU Gold, cube,
                // better gear), so a snipe that was pointless against the bank can become worth it again
                // without any new zone. Demands RebankMargin so this cannot flip on rounding noise.
                var bestZone = ZoneStatHelper.GetBestZone();
                if (Main.Settings.ManageGoldLoadouts && Main.Settings.GoldSnipeComplete && bestZone != null
                    && c.machine.realBaseGold > 0
                    && GoldDropAdvisor.BeatsBank(bestZone.Zone, true, GoldDropAdvisor.RebankMargin, out var pred, out var bank))
                {
                    Main.Settings.GoldSnipeComplete = false;
                    Main.LastSnipeTrigger = "gold drop improved";
                    Main.Log($"Re-snipe: gold drop improved (~{NumberFormatter.Abbrev(pred)} vs {NumberFormatter.Abbrev(bank)} banked)");
                    Main.LogGoldDecision(bestZone.Zone, pred, bank, "RE-ARM",
                        $"gold drop improved past RebankMargin x{GoldDropAdvisor.RebankMargin:0.##}");
                }

                // Starvation trigger: advisor always; manual mode via its S3 toggle.
                if (!Main.Settings.AdvisorGold && !Main.Settings.SnipeOnGoldStarved) return;
                if (c.machine.realBaseGold > 0 && Main.Settings.GoldSnipeComplete
                    && OptimizationAdvisor.GoldStarvedForAugs(c, 1.0))
                {
                    // Starving for augments is a reason to re-snipe only if a snipe can actually raise the
                    // TM: with the bank already above every reachable zone the gold has to come from a
                    // titan (or from spending less), never from re-fighting the zone in gold gear.
                    double predicted = 0, banked = GoldDropAdvisor.Banked();
                    bool beats = bestZone == null || GoldDropAdvisor.ZoneSnipeBeatsBank(bestZone.Zone, out predicted, out banked);
                    Main.LogGoldDecision(bestZone != null ? bestZone.Zone : -1, predicted, banked,
                        beats ? "RE-ARM" : "SKIP",
                        bestZone == null
                            ? "gold starvation, no candidate zone"
                            : beats ? "gold starvation (augments unaffordable)" : "gold starvation, but no zone beats the bank");
                    if (beats)
                    {
                        Main.Settings.GoldSnipeComplete = false;
                        Main.LastSnipeTrigger = "gold starvation";
                        Main.Log("Re-snipe: gold starvation (augments unaffordable)");
                    }
                }
            }
            catch (Exception ex) { OnStepFailed("Gold", ex); return; }

            // Reached only after the throttle gate passed and the body ran without throwing — the
            // 2-minute gate above is the eligibility check, so getting here is a real exercise.
            ObserveStepReturn("Gold");
        }

        // Advisor quest strategy: majors whenever banked, bank-overfill guard on, abandon minors
        // under 30%, butter majors only, minors idle. AutoQuest itself stays the user's master
        // switch; the 50-item rule follows the perk that enables it. Applied once, logged once.
        private static DateTime _lastQuestCheck = DateTime.MinValue;

        private static void ApplyQuests()
        {
            if ((DateTime.UtcNow - _lastQuestCheck).TotalSeconds < 60) return;
            _lastQuestCheck = DateTime.UtcNow;

            try
            {
                var s = Main.Settings;
                if (!s.AutoQuest) return;
                var changed = new List<string>();
                if (!s.AllowMajorQuests) { s.AllowMajorQuests = true; changed.Add("majors on"); }
                if (!s.QuestsFullBank) { s.QuestsFullBank = true; changed.Add("bank guard on"); }
                if (s.ManualMinors) { s.ManualMinors = false; changed.Add("minors idle"); }
                if (!s.AbandonMinors) { s.AbandonMinors = true; changed.Add("abandon minors"); }
                if (s.MinorAbandonThreshold != 30) { s.MinorAbandonThreshold = 30; changed.Add("abandon <30%"); }
                if (!s.UseButterMajor) { s.UseButterMajor = true; changed.Add("butter majors"); }
                if (s.UseButterMinor) { s.UseButterMinor = false; changed.Add("no minor butter"); }
                bool fifty = false;
                try { fifty = Main.Character.adventure.itopod.perkLevel[94] >= 610; } catch { }
                if (s.FiftyItemMinors != fifty) { s.FiftyItemMinors = fifty; changed.Add(fifty ? "50-item minors" : "54-item minors"); }
                if (changed.Count > 0)
                    Main.Log($"Advisor: quest strategy -> {string.Join(", ", changed.ToArray())}");
            }
            catch (Exception ex) { OnStepFailed("Quests", ex); return; }

            // Reached only when AutoQuest was on and the strategy pass completed without throwing. The
            // !AutoQuest return inside the try exits the method before here, so a disabled invocation
            // never counts as a successful exercise.
            ObserveStepReturn("Quests");
        }

        // Advisor Money Pit: act on the shared plan (tier-ETA + safety gates in MoneyPitManager).
        private static DateTime _lastPitCheck = DateTime.MinValue;

        private static void ApplyPit()
        {
            if ((DateTime.UtcNow - _lastPitCheck).TotalSeconds < 60) return;
            _lastPitCheck = DateTime.UtcNow;

            try
            {
                var plan = MoneyPitManager.AdvisorPlan();
                if (plan.Throw)
                {
                    Main.Log($"Advisor: money pit -> {plan.Verdict} (predicted: {MoneyPitManager.PredictNext()})");
                    MoneyPitManager.AdvisorThrow();
                }
            }
            catch (Exception ex) { OnStepFailed("Pit", ex); return; }

            // Reached after the 60s gate passed and AdvisorPlan (plus any throw) ran without throwing.
            // The "Pit" key is automatic-only — the manual Throw Now path (R10) reports via Activity and
            // never touches this fault state.
            ObserveStepReturn("Pit");
        }

        // Advisor titan targeting (Titans hero card): target every reachable titan below auto-kill —
        // riddle titans (6/7/8) only once their quest flags unlock. Drops targets the moment AK lands.
        private static DateTime _lastTitanTargets = DateTime.MinValue;

        private static void ApplyTitans()
        {
            if (!Main.Settings.ManageTitans) return;
            if ((DateTime.UtcNow - _lastTitanTargets).TotalSeconds < 60) return;
            _lastTitanTargets = DateTime.UtcNow;

            try
            {
                var c = Main.Character;

                // During any challenge, below-AK titans are unviable (reduced stats, constant resets);
                // AK'd titans die automatically regardless. Clear targets and stand down.
                string challenge = null;
                try { challenge = ChallengeDetector.Current(); } catch { }
                if (challenge != null)
                {
                    var curT = Main.Settings.TitanSwapTargets;
                    if (curT != null && curT.Any(x => x))
                    {
                        Main.Settings.TitanSwapTargets = new bool[14];
                        Main.Log($"Advisor: challenge active ({challenge}) — titan targeting paused (only AK'd titans viable)");
                        ChallengeOverlay.Record("TITAN", "titan targeting paused", "challenge stats can't push AK");
                    }
                    return;
                }

                // Objective + attempt-readiness FIRST — both the target list and the spawn version
                // depend on it. A "first kill" objective is only ATTEMPTED once the projected
                // best-gear stats actually cover the staged manual requirement (user-reported: the
                // advisor chased a freshly-AK'd titan's next version nowhere near a manual attempt —
                // wasted fights, and the spawn was parked off the AK version that pays gold/drops).
                var objv = OptimizationAdvisor.NextObjective();
                int primary = objv.Known ? objv.Index : -1;
                bool attemptReady = true;
                if (objv.Known && objv.Stage == "first kill")
                {
                    try
                    {
                        OptimizationAdvisor.ProjectedBestGear(out var am, out var dm);
                        attemptReady = c.totalAdvAttack() * am >= objv.ReqAttack
                                    && c.totalAdvDefense() * dm >= objv.ReqDefense;
                    }
                    catch { attemptReady = false; }
                }

                int maxZone = ZoneHelpers.GetMaxReachableZone(true);
                var targets = new bool[14];
                for (int i = 0; i < ZoneHelpers.TitanZones.Length && i < 14; i++)
                {
                    if (ZoneHelpers.TitanZones[i] > maxZone) continue;
                    bool riddleLocked = false;
                    try
                    {
                        if (i == 5) riddleLocked = !c.adventure.titan6Unlocked;
                        else if (i == 6) riddleLocked = !c.adventure.titan7Unlocked;
                        else if (i == 7) riddleLocked = !c.adventure.titan8Unlocked;
                    }
                    catch { }
                    if (riddleLocked) continue;
                    bool ak = false;
                    try { ak = ZoneHelpers.AutokillAvailable(i); } catch { }
                    if (!ak) targets[i] = true;
                }
                // Not ready for the first-kill attempt: don't attend its spawns in kill gear at all.
                // (The version parking below keeps the AK-able version spawning for gold/drops.)
                if (!attemptReady && primary >= 0 && primary < targets.Length)
                    targets[primary] = false;

                var cur = Main.Settings.TitanSwapTargets ?? new bool[14];
                bool differs = cur.Length != targets.Length;
                if (!differs)
                    for (int i = 0; i < targets.Length; i++)
                        if (cur[i] != targets[i]) { differs = true; break; }
                if (differs)
                {
                    Main.Settings.TitanSwapTargets = targets;
                    var names = new List<string>();
                    for (int i = 0; i < targets.Length; i++)
                        if (targets[i])
                            names.Add(ZoneHelpers.ZoneList.TryGetValue(ZoneHelpers.TitanZones[i], out var n) ? n : $"Titan {i + 1}");
                    Main.Log($"Advisor: titan targets -> {(names.Count > 0 ? string.Join(", ", names.ToArray()) : "(none — everything auto-kills)")}");
                }

                // The advisor owns titan killing: the kill-gear swap master must be on or the
                // snapshot machinery never equips the P/T set (user-reported death loop).
                if (!Main.Settings.SwapTitanLoadouts)
                {
                    Main.Settings.SwapTitanLoadouts = true;
                    Main.Log("Advisor: titan kill-gear swaps enabled (advisor manages titans)");
                }

                // Force the objective titan's SPAWN version to the version being chased — spawn
                // version is user-selected and never auto-advances (user: AK'd v1, 22 kills of v2,
                // spawn still parked on the wrong version blocks AK progress).
                // EXCEPTION (user-reported death loop): while a gold bank is pending on this titan,
                // the gold swap needs the AK-able spawn version (the kill is free in gold gear) —
                // forcing v2 turned that into a real fight fought in drop gear. Bank first, then push.
                if (primary >= 5 && primary <= 11)
                {
                    bool goldPending = false;
                    try
                    {
                        var gt = Main.Settings.TitanGoldTargets;
                        var md = Main.Settings.TitanMoneyDone;
                        goldPending = Main.Settings.ManageGoldLoadouts
                            && gt != null && primary < gt.Length && gt[primary]
                            && (md == null || primary >= md.Length || !md[primary]);
                    }
                    catch { }
                    try
                    {
                        int spawn = ZoneHelpers.TitanVersion(primary);
                        if (goldPending || !attemptReady)
                        {
                            // Park the spawn on the highest AK-able version: while a gold bank is
                            // pending it completes there for free, and while the next version's
                            // first-kill stats are out of reach even in best gear, the AK version
                            // keeps paying gold/drops instead of feeding doomed attempts.
                            int akVer = 0;
                            for (int vv = 1; vv < objv.Version; vv++)
                                try { if (ZoneHelpers.AutokillAvailable(primary, vv)) akVer = vv; } catch { }
                            if (akVer > 0 && spawn != akVer)
                            {
                                ZoneHelpers.SetTitanVersion(primary, akVer);
                                if (goldPending)
                                {
                                    Main.Log($"Advisor: titan spawn version -> v{akVer} (gold bank pending — free AK kill first)");
                                    ChallengeOverlay.Record("TITAN", $"titan version → v{akVer}", "gold bank pending — banking before the push");
                                }
                                else
                                {
                                    Main.Log($"Advisor: titan spawn version -> v{akVer} (v{objv.Version} first-kill stats out of reach — farming the AK version meanwhile)");
                                    ChallengeOverlay.Record("TITAN", $"titan version → v{akVer}", $"v{objv.Version} first kill needs {objv.ReqAttack:0.#e0} atk — not there yet even in best gear");
                                }
                            }
                        }
                        else if (spawn != objv.Version)
                        {
                            ZoneHelpers.SetTitanVersion(primary, objv.Version);
                            Main.Log($"Advisor: titan spawn version -> v{objv.Version} (chasing its {objv.Stage})");
                            ChallengeOverlay.Record("TITAN", $"titan version → v{objv.Version}", $"objective is v{objv.Version} {objv.Stage}");
                        }
                    }
                    catch (Exception ex) { Main.LogDebug($"Titan version set: {ex.Message}"); }
                }

                if (primary >= 0 && targets.Length > primary && targets[primary])
                {
                    double reqA = objv.ReqAttack, reqD = objv.ReqDefense;
                    double atk = c.totalAdvAttack();
                    double def = c.totalAdvDefense();
                    // Posture from the kill ladder, FIELD-CALIBRATED (user cleared the v2 fight only
                    // on Defensive — Offensive at half the defense requirement was still too greedy):
                    //   Defensive  — the default for every real fight; block/dodge wins marginal ones
                    //   Offensive  — both stats fully cover the stage requirement
                    //   Idle       — auto-kill stage only (the fight is trivially won)
                    int mode;
                    if (objv.Stage == "auto-kill" && atk >= reqA && def >= reqD) mode = 0;
                    else if (reqA > 0 && reqD > 0 && atk >= reqA && def >= reqD) mode = 3;
                    else mode = 2;
                    // Beast cuts defense for damage: only past 1.25x the stage bar on a proven kill.
                    bool beast = reqD > 0 && def / reqD >= 1.25 && objv.Stage != "first kill";
                    if (Main.Settings.TitanCombatMode != mode || Main.Settings.TitanBeastMode != beast)
                    {
                        Main.Settings.TitanCombatMode = mode;
                        Main.Settings.TitanBeastMode = beast;
                        string[] modes = { "Idle", "Snipe", "Defensive", "Offensive" };
                        Main.Log($"Advisor: titan combat -> {modes[mode]}, beast {(beast ? "on" : "off")}");
                    }
                }
            }
            catch (Exception ex) { OnStepFailed("Titans", ex); return; }

            // Reached after the full targeting pass ran without throwing. The challenge-active stand-down
            // returns from inside the try (titan targeting is paused, not exercised), so it does not clear
            // the fault — health is observed only when the real targeting logic completed.
            ObserveStepReturn("Titans");
        }

        // Advisor zone routing (Adventure > ZONES, ADVISOR ACTIVE): point the farm zone at the best
        // boost-farm location. Deliberately NOT active while CBlock/pit-run gold logic owns zones —
        // those modes drive SnipeZone dynamically and must win.
        private static DateTime _lastZoneCheck = DateTime.MinValue;

        // WHICH LAYER routed the zone, and what lost. The user-facing "Advisor: farm zone -> …" lines go to
        // the advisor output log, name only the winner, and never appear at all on the paths that decline to
        // route — so the precedence between gear hunt, gear farm, boost farm, gold modes and the ITOPOD was
        // invisible after the fact, and every input of it (rates, demand gate, viability) is transient.
        //
        // Discipline is [TitanGoldDbg]'s, unchanged: cadence cap FIRST (so the render never runs on a
        // suppressed tick), then emit only when the rendered line CHANGES. ApplyZones is throttled to 10
        // minutes on the farm paths, but the exits ahead of that throttle (combat off, gold modes, gear hunt,
        // AdvisorZones off) run on every 30 s tick — and a line repeated every 10 minutes for hours buries the
        // transitions just as effectively, so the change check earns its keep on both.
        private static string _lastZoneDbg;
        private static DateTime _lastZoneDbgAt = DateTime.MinValue;

        private static string ZoneDbgName(int zone) => zone == 1000
            ? "ITOPOD"
            : ZoneHelpers.ZoneList.TryGetValue(zone, out string zn) ? zn : $"Zone {zone}";

        private static void LogZoneDbg(string layer, Func<string> render)
        {
            try
            {
                if ((DateTime.UtcNow - _lastZoneDbgAt).TotalSeconds < 60) return;
                _lastZoneDbgAt = DateTime.UtcNow;

                // zone= is where Main.Update() will actually send the character, NOT what this layer
                // picked: the advisor only writes SnipeZone, and Target ITOPOD / the gear hunt / a
                // locked zone override it downstream. The line used to report the pick and stayed
                // silent about the override, so it named a zone nobody was farming (user-caught).
                int advised = Main.Settings.SnipeZone;
                int zone = Main.ResolveAdventureZone(out string overriddenBy);
                string line = $"[ZoneDbg] layer={layer} zone={zone} ({ZoneDbgName(zone)})"
                            + (zone == advised
                                ? ""
                                : $" advised={advised} ({ZoneDbgName(advised)}) overriddenBy={overriddenBy}")
                            + $" combat={BoostFarmAdvisor.ModeName(Main.Settings.CombatMode)} {render()}";
                if (line == _lastZoneDbg) return;
                _lastZoneDbg = line;
                Main.LogDebug(line);
            }
            catch (Exception e) { Main.LogDebug($"[ZoneDbg] failed: {e.Message}"); }
        }

        // Adventure combat mode for the farm park the advisor is about to route to. Idle pays a
        // full attackSpeed of spawn latency on EVERY kill (AdventureController zeroes
        // idleAttackTimer when the enemy spawns and only advances it mid-fight), while a manual
        // mode lands the opening swing on the spawn frame because moveTimer keeps running through
        // the respawn — up to 2x the kills per hour (ZoneCadence.md). The farm advisors return the
        // mode their rate was costed at; honouring it is what makes the recommendation real.
        private static void ApplyFarmCombatMode(int mode, string forWhat)
        {
            if (mode < 0 || mode > 3) return;
            if (Main.Settings.CombatMode == mode) return;
            Main.Settings.CombatMode = mode;
            Main.Log($"Advisor: adventure combat -> {BoostFarmAdvisor.ModeName(mode)} (fastest for {forWhat})");
        }

        private static void ApplyZones()
        {
            if (!Main.Settings.CombatEnabled)
            {
                LogZoneDbg("none", () => "why=combat automation off");
                return;
            }
            if (Main.Settings.GoldCBlockMode || Main.Settings.MoneyPitRunMode)
            {
                LogZoneDbg("gold", () => $"why=gold logic owns zones (cblock={Main.Settings.GoldCBlockMode}"
                                       + $" pitrun={Main.Settings.MoneyPitRunMode})");
                return;
            }

            // GEAR HUNT: the user-picked stage outranks the automatic farms. Cheap and outside the
            // 10-minute throttle so flipping the toggle acts on the next tick; an unreachable stage
            // leaves routing alone until it unlocks.
            if (GearHunter.Active)
            {
                if (!GearHunter.ZoneReachable())
                {
                    LogZoneDbg("gearhunt", () => $"why=hunt zone {Main.Settings.GearHuntZone} unreachable"
                                               + " — routing left alone until it unlocks");
                    return;
                }
                int hz = Main.Settings.GearHuntZone;
                string hn = ZoneHelpers.ZoneList.TryGetValue(hz, out var n) ? n : $"Zone {hz}";

                // A hunt exists purely to farm drops, so its kill rate IS the point — and this layer
                // used to set no mode at all, so a hunt inherited whatever the previous layer left
                // (Idle, if the advisor had been parked in the pod), halving the drops it was routed
                // for. Ask the same source the farm layers ask, so the mode stays MEASURED for the
                // hunted zone rather than assumed.
                int hm = ZoneCadence.FastestMode(hz);
                string hmSrc = "measured (ZoneCadence)";
                if (hm < 0)
                {
                    // No usable cadence estimate for this zone (unreadable, unkillable or not
                    // survivable at either candidate mode). Manual is never slower than Idle and up
                    // to 2x faster (ZoneCadence.md), so Offensive is the safe default — but only
                    // where the regular attack exists at all; before that, Idle is the only mode.
                    bool manual = false;
                    try { manual = CombatHelpers.RegularAttackUnlocked(); } catch { }
                    hm = manual ? 3 : 0;
                    hmSrc = manual
                        ? "fallback Offensive (no cadence estimate; manual is never slower)"
                        : "fallback Idle (regular attack not unlocked)";
                }
                ApplyFarmCombatMode(hm, $"the gear hunt in {hn}");

                if (Main.Settings.SnipeZone != hz)
                {
                    Main.Settings.SnipeZone = hz;
                    Main.Log($"Advisor: farm zone -> {hn} (gear hunt)");
                }
                LogZoneDbg("gearhunt", () => $"pick={hz} beat=gear/boost farms (user-picked stage outranks them)"
                                           + $" wantMode={BoostFarmAdvisor.ModeName(hm)} modeSrc={hmSrc}"
                                           + $" advisorZones={Main.Settings.AdvisorZones}");
                return;
            }
            if (!Main.Settings.AdvisorZones)
            {
                LogZoneDbg("none", () => "why=AdvisorZones off and no gear hunt active");
                return;
            }

            if ((DateTime.UtcNow - _lastZoneCheck).TotalMinutes < 10) return;
            _lastZoneCheck = DateTime.UtcNow;

            // Farm Gear Zones outranks the boost farm: every capped item is a PERMANENT item-list
            // bonus, and only zones that finish inside the advisor's time budget qualify.
            // Why the gear farm did NOT take the routing — carried into the boost line below so one line
            // explains the whole precedence chain instead of two.
            string gearFarmWhy = Main.Settings.AdvisorFarmGear ? "?" : "off";
            if (Main.Settings.AdvisorFarmGear)
            {
                var g = GearFarmAdvisor.Analyze();
                if (g.Known && g.Best != null)
                {
                    ApplyFarmCombatMode(g.Best.Mode, g.Best.ZoneName);
                    if (Main.Settings.SnipeZone != g.Best.Zone)
                    {
                        Main.Settings.SnipeZone = g.Best.Zone;
                        Main.Log($"Advisor: farm zone -> {g.Best.ZoneName} (gear: {g.Best.MissingItems.Count} uncapped, ~{g.Best.HoursToCap:0.#}h to cap)");
                    }
                    var plan = g.Best;
                    var near = g.Nearest;
                    LogZoneDbg("gearfarm", () => $"pick={plan.ZoneName} uncapped={plan.MissingItems.Count}"
                                              + $" hrs={plan.HoursToCap:0.#} wantMode={BoostFarmAdvisor.ModeName(plan.Mode)}"
                                              + $" beat={(near != null ? $"{near.ZoneName} (needs ~{near.ReqLootFactor * 100:#,0}% DC)" : "boost farm (no non-viable runner-up)")}");
                    return;
                }
                gearFarmWhy = !g.Known
                    ? "verdict unknown"
                    : g.Nearest != null
                        ? $"no zone caps in budget, closest {g.Nearest.ZoneName} needs ~{g.Nearest.ReqLootFactor * 100:#,0}% DC"
                        : "nothing uncapped in budget";
            }

            var v = BoostFarmAdvisor.Analyze();
            if (!v.Known)
            {
                LogZoneDbg("none", () => $"why=boost verdict unknown gearfarm={gearFarmWhy}");
                return;
            }
            int target = v.BestZone == -1000 ? 1000 : v.BestZone;
            string name = v.BestName;
            string detail = $"{v.BestRate:0.###} boost/s";
            int farmMode = v.BestMode;
            // Farm Best Boost: boost zones only beat the ITOPOD while something consumes boosts.
            bool routedForBoosts = true;
            if (Main.Settings.AdvisorFarmBoost && target != 1000 && !BoostFarmAdvisor.BoostDemandExists(out var why))
            {
                target = 1000;
                name = "ITOPOD";
                detail = $"no boost demand — {why}";
                routedForBoosts = false;
            }

            // Parking in the pod because nothing consumes boosts means the boost rate is exactly the
            // wrong thing to pick its combat mode on. PP is the currency nothing else in the game
            // produces, so it decides — and the other rates go in the log, because the four do not
            // peak at the same floor (boosts stop improving at floor 1150, AP at 950, EXP never).
            if (target == 1000 && !routedForBoosts)
            {
                ItopodFarmAdvisor.Rates rates = ItopodFarmAdvisor.Best(r => r.PpPerSecond, BoostSinks.Current());
                if (rates.Known)
                {
                    farmMode = rates.CombatMode;
                    detail += $" · {rates.PpPerSecond:0.####} PP/s, {rates.ExpPerSecond:0.##} EXP/s"
                            + $" (floors {rates.DefaultFloor}-{rates.PeakFloor})";
                }
            }
            ApplyFarmCombatMode(farmMode, name);
            if (Main.Settings.SnipeZone != target)
            {
                Main.Settings.SnipeZone = target;
                Main.Log($"Advisor: farm zone -> {name} ({detail})");
            }
            LogZoneDbg(target == 1000 ? "itopod" : "boostfarm",
                () => $"pick={name} rate={detail} wantMode={BoostFarmAdvisor.ModeName(farmMode)}"
                    + $" beat={(v.BestZone == -1000 ? "every farmable zone" : $"ITOPOD @{v.ItopodRate:0.###} boost/s")}"
                    + $" boostDemand={routedForBoosts} gearfarm={gearFarmWhy}");
        }

        // EXP balancing (guide ratios): one walk step per minute, waterfilling up to 10% of banked EXP
        // across the lagging stats — raises the lowest levels first, converging on the ratio in gentle
        // chunks, then maintains it with proportional buys.
        private static DateTime _lastExpBuy = DateTime.MinValue;

        private static void ApplyExpBuys()
        {
            if ((DateTime.UtcNow - _lastExpBuy).TotalSeconds < 60) return;
            _lastExpBuy = DateTime.UtcNow;
            var what = ExpBalancer.BuyTick(0.10);
            if (what != null)
                Main.Log($"Advisor: bought {what}");
        }

        // Phase C: gear auto-refresh. When the active gear breakpoint is objective-driven, periodically
        // re-optimize the same objective and re-equip if a new drop/merge made a meaningfully better
        // loadout available (>= 5%). Optimize is heavy, so this is throttled well beyond the 30s tick.
        private static DateTime _lastGearCheck = DateTime.MinValue;
        // What the last resolved pass equipped for: the rendered chain (GearChain.Describe) on the
        // optimizer path, the bare name on the gear-hunt path. Only ever compared for equality.
        private static string _lastGearObjective;
        // False on every payload load. A reload can leave a lock's TEMP loadout equipped with the
        // restore set lost (Unload doesn't release locks; statics wipe — user-reported: gear stayed
        // swapped after a reload and never returned to the segment loadout, because the score
        // early-outs below read "scores about as well" as "nothing to do"). The first pass after a
        // load therefore equips the objective's best set UNCONDITIONALLY, re-asserting known-good
        // gear; the anti-churn thresholds apply from then on.
        private static bool _gearAsserted;

        // Called by LockManager when a mode lock restores its saved gear: that gear is whatever was
        // worn at ACQUISITION — stale if the segment/objective moved while the lock was held
        // (user-reported: AT gear restored into the NGU MARATHON). Clearing the objective marker
        // re-arms the changed-objective bypass, and clearing the throttle makes the very next
        // advisor tick re-evaluate instead of waiting out the 120s window.
        public static void GearRestored()
        {
            _lastGearObjective = null;
            _lastGearCheck = DateTime.MinValue;
        }

        // WHY gear was or was not re-equipped. The user-facing lines only ever announce an equip, so the far
        // more common outcome — the 5% bar held the current loadout — left no trace at all, and neither did
        // the two scores it was measured on. Both sides of the bar and the switch bypass go on one line.
        //
        // Same discipline as [TitanGoldDbg]: cadence cap before the render, then emit only on CHANGE. The
        // scores below are already computed by the decision itself, so the render adds no optimizer work —
        // but the exits ahead of the 120 s throttle (ManageGear off, quest lock, NOEC, no objective) run on
        // every 30 s tick and need the cap regardless.
        private static string _lastGearDbg;
        private static DateTime _lastGearDbgAt = DateTime.MinValue;

        private static void LogGearDbg(string verdict, Func<string> render)
        {
            try
            {
                if ((DateTime.UtcNow - _lastGearDbgAt).TotalSeconds < 60) return;
                _lastGearDbgAt = DateTime.UtcNow;
                string line = $"[GearDbg] {verdict} {render()}";
                if (line == _lastGearDbg) return;
                _lastGearDbg = line;
                Main.LogDebug(line);
            }
            catch (Exception e) { Main.LogDebug($"[GearDbg] failed: {e.Message}"); }
        }

        private static void ApplyGearRefresh()
        {
            if (!Main.Settings.ManageGear)
            {
                LogGearDbg("OFF", () => "why=ManageGear off");
                return;
            }
            // CanSwap() allows the quest lock through, but quest gear is equipped then — don't fight it.
            if (LockManager.HasQuestLock())
            {
                LogGearDbg("HELD", () => "why=quest lock owns the loadout");
                return;
            }
            // NOEC: there is no equipment — don't churn.
            try { if (ChallengeDetector.Current() == "NOEC") { LogGearDbg("HELD", () => "why=NOEC challenge, no equipment"); return; } } catch { }
            // The challenge overlay's rotation outranks the profile objective — this is what un-freezes
            // gear during challenges even when the profile's challenge breakpoints are static ID lists.
            // GEAR HUNT sits between them: it replaces the SEGMENT objective (the user is deliberately
            // camping a stage) but yields to challenge rotation. NOTE: GearObjectiveOverride is the
            // SEGMENT gear whenever AutoProfile is on (not just challenge rotation), so the hunt must
            // be checked FIRST outside challenges — `override ?? hunt` never fell through and the
            // Loot Hunter loadout was never equipped (user-reported).
            bool inChallenge = false;
            try { inChallenge = ChallengeDetector.Current() != null; } catch { }
            string overrideName = !inChallenge && GearHunter.Active
                ? "LOOT HUNTER"
                : ChallengeOverlay.GearObjectiveOverride;
            // The profile's own name is the one its chain was RESOLVED FROM, not the chain's lead objective:
            // the preset "Adventure + Respawn" leads with "Adventure", and reporting that as the profile's
            // name is what let a plain "Adventure" override inherit the profile's three-step chain.
            string objName = overrideName
                ?? AllocationProfiles.Breakpoints.GearBreakpoints.ActiveChainSource
                ?? AllocationProfiles.Breakpoints.GearBreakpoints.ActiveObjective;
            if (string.IsNullOrEmpty(objName))
            {
                LogGearDbg("HELD", () => $"why=no active objective (override={overrideName ?? "none"}"
                                       + $" chainSource={AllocationProfiles.Breakpoints.GearBreakpoints.ActiveChainSource ?? "none"}"
                                       + $" objective={AllocationProfiles.Breakpoints.GearBreakpoints.ActiveObjective ?? "none"})");
                return;
            }
            if ((DateTime.UtcNow - _lastGearCheck).TotalSeconds < 120) return;
            _lastGearCheck = DateTime.UtcNow;

            if (objName == "LOOT HUNTER")
            {
                // Hybrid set (pool accessories + best P/T): no single objective score exists, so the
                // anti-churn test is set-membership — re-equip only when the resolved set isn't worn.
                var huntIds = GearHunter.ResolveLoadout(out var what);
                if (huntIds.Length == 0)
                {
                    LogGearDbg("HELD", () => "obj='LOOT HUNTER' why=hunt loadout resolved to nothing");
                    return;
                }
                bool huntChanged = objName != _lastGearObjective;
                var worn = new HashSet<int>(LoadoutManager.CurrentGearIds());
                if (_gearAsserted && !huntChanged && huntIds.All(worn.Contains))
                {
                    _lastGearObjective = objName;
                    LogGearDbg("HELD", () => $"obj='LOOT HUNTER' set='{what}' switch=false"
                                           + " why=resolved hunt set already worn (membership test, no 5% bar)");
                    return;
                }
                bool firstHunt = !_gearAsserted;
                _gearAsserted = true;
                _lastGearObjective = objName;
                LoadoutManager.ChangeGear(huntIds);
                Main.InventoryController.assignCurrentEquipToLoadout(0);
                Main.Log($"Advisor: gear hunt loadout equipped — {what}{(firstHunt ? " (startup/reload assert)" : "")}");
                LogGearDbg("EQUIP", () => $"obj='LOOT HUNTER' set='{what}' switch={huntChanged}"
                                        + $" assert={firstHunt} why=hunt set not worn (membership test, no 5% bar)");
                return;
            }

            // The profile's own chain wins whenever nothing OVERRIDES it -- it can carry an explicit
            // Priorities list that no name can express. An override/hunt name resolves on its own (preset
            // first, then a plain objective); an unresolved name is refused, not guessed. The test is the
            // absence of an override and NOT a name comparison: no name can tell "the override asked for
            // Adventure" apart from "the profile's chain happens to lead with Adventure".
            var activeChain = AllocationProfiles.Breakpoints.GearBreakpoints.ActiveChain;
            var chain = overrideName == null && activeChain != null
                ? activeChain
                : GearChain.Resolve(objName);
            if (chain == null || chain.Count == 0)
            {
                LogGearDbg("HELD", () => $"obj='{objName}' why=name resolved to no chain (refused, not guessed)");
                return;
            }
            // Objective switches (segment/rotation changes) bypass the 5% bar: "wrong gear that's
            // within 5% on the NEW objective" is still wrong gear (user-reported: TM HOUR wearing
            // the push loadout). The threshold only applies to same-objective drop improvements.
            // _lastGearObjective commits ONLY when a pass actually resolves the switch (equip, or
            // verified already-optimal) — a no-op pass must NOT consume the bypass (user-reported:
            // segment flipped during a titan lock; the first post-release pass fizzled and the
            // stale AT gear then sat inside the 5% bar forever).
            //
            // A CHAIN switch is an objective switch, so the marker is the rendered chain, not
            // chain[0].Objective.Name: swapping only the tail of a chain leaves the lead objective
            // unchanged and would otherwise never clear the 5% bar. (The hunt path above stores the bare
            // "LOOT HUNTER" instead, which no rendered chain can equal — so hunt<->chain always counts.)
            string chainKey = GearChain.Describe(chain);
            bool objectiveChanged = chainKey != _lastGearObjective;
            // Both sides of the bar measure the same thing: the chain's own lead objective.
            double cur = GearOptimizer.CurrentScore(chain);
            var best = GearOptimizer.Optimize(chain, null, AllocationProfiles.Breakpoints.GearBreakpoints.ActiveForceRespawn);
            if (best == null)
            {
                LogGearDbg("HELD", () => $"obj='{objName}' chain='{chainKey}' switch={objectiveChanged}"
                                       + " why=optimizer returned no loadout");
                return;
            }
            // One renderer for every outcome of the bar, so a single line carries both scores it was
            // measured on, their ratio, the bar itself, and whether this pass was a SWITCH (which bypasses
            // the bar). Reads only locals already computed above — no second optimizer run.
            bool wasAsserted = _gearAsserted;
            Func<string, string> gearLine = why =>
                $"obj='{objName}' chain='{chainKey}' switch={objectiveChanged} asserted={wasAsserted}"
              + $" cur={cur:0.###e0} best={best.Score:0.###e0}"
              + $" ratio={(cur > 0 ? (best.Score / cur).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "n/a")}"
              + $" bar=x1.05 why={why}";
            if (_gearAsserted)
            {
                if (!objectiveChanged && (cur <= 0 || best.Score < cur * 1.05))
                {
                    LogGearDbg("HELD", () => gearLine(cur <= 0
                        ? "no score for the worn set"
                        : "same objective and inside the 5% re-equip bar"));
                    return;
                }
                if (objectiveChanged && cur > 0 && best.Score <= cur)
                {
                    _lastGearObjective = chainKey;   // verified: equipped gear IS optimal for the new objective
                    LogGearDbg("HELD", () => gearLine("objective switch, but the worn set is already optimal for it"));
                    return;
                }
            }

            var ids = best.AllIds().Where(x => x > 0).Distinct().ToArray();
            if (ids.Length == 0)
            {
                LogGearDbg("HELD", () => gearLine("winning loadout had no equippable ids"));
                return;
            }
            bool firstAssert = !_gearAsserted;
            _gearAsserted = true;
            _lastGearObjective = chainKey;
            LoadoutManager.ChangeGear(ids);
            Main.InventoryController.assignCurrentEquipToLoadout(0);
            string label = $"{objName} [{chainKey}]";
            Main.Log(firstAssert
                ? $"Advisor: gear asserted for '{label}' (startup/reload — known-good loadout re-equipped)"
                : objectiveChanged
                    ? $"Advisor: gear switched to '{label}' loadout (objective change)"
                    : $"Advisor: re-optimized gear for '{label}' (+{(best.Score / cur - 1) * 100:0.#}% from new drops)");
            LogGearDbg("EQUIP", () => gearLine(firstAssert
                ? "startup/reload assert — bar not applied"
                : objectiveChanged
                    ? "objective/chain switch — bar bypassed"
                    : "cleared the 5% bar on the same objective"));
        }

        private static void ApplyWandoosOs(Character c)
        {
            if (!c.wandoos98.installed && c.wandoos98.OSlevel <= 0) return;
            if ((DateTime.UtcNow - _lastOsSwitch).TotalMinutes < 10) return;

            // Project over the RUN's remaining length (short runs favor cheap fast OSs) and act on the
            // same >=1.25x threshold at which the advisor row turns red — the row and the auto agree.
            int horizon = WandoosAdvisor.RunHorizonMinutes();
            var v = WandoosAdvisor.Compare(horizon);
            if (!v.Known || v.BestOs == v.CurrentOs || v.Advantage < 1.25) return;

            // Current REAL bonus (with the levels we'd be throwing away) vs the projected horizon
            // on the better OS starting from zero: the switch must pay for itself within the run.
            double actualNow = c.wandoos98Controller.wandoosBonus();
            if (v.Cases[v.BestOs].Bonus < actualNow * 1.5) return;

            string from = v.CurrentName;
            c.wandoos98.changeOS((OSType)v.BestOs);
            _lastOsSwitch = DateTime.UtcNow;
            Main.Log($"Advisor: switched Wandoos OS {from} -> {v.BestName} (~{WandoosAdvisor.FmtX(v.Advantage)} better at your cap)");
        }
    }
}
