using System;
using System.Collections.Generic;
using System.Linq;
using NGUAdvisor.AllocationProfiles.RebirthStuff;

namespace NGUAdvisor.Managers
{
    // Route C3: the ADVISORY optimization layer, "gaps + actions" model. Every rec answers "where do we
    // need to GO" for the current goal/activity — a target and the action that closes it — never a bare
    // status readout (the status strip covers "where we are"). Systems with no gap are flagged Optimal
    // and collapse into one "✓ optimal: ..." line on the dashboard. AutoKey names the auto-apply toggle
    // (AdvisorApply) that can act on the row. Read-only itself; cached/throttled, guarded, main-thread.
    public static class OptimizationAdvisor
    {
        public struct Rec
        {
            public string System;
            public string Text;      // the gap/action (or the "why it's fine" when Optimal)
            public int Severity;     // 0 = ok/info, 1 = suggest, 2 = act
            public bool Optimal;     // no gap — dashboard collapses it unless its auto toggle is on
            public string AutoKey;   // "gear" | "wandoos" | "diggers" | "beards" | null
        }

        // internal, not private: StateExport labels the same indices and a second copy of these lists
        // would be free to drift from the ones the advice rows use.
        internal static readonly string[] DiggerNames =
            { "Drops", "Wandoos", "Stats", "Adv", "E-NGU", "M-NGU", "E-Beard", "M-Beard", "PP", "Daycare", "Blood", "EXP" };
        internal static readonly string[] BeardNames =
            { "Stats", "Drops", "Number", "NGU", "Wandoos", "Adv", "Golden" };
        private static readonly string[] WandoosOsNames = { "98", "MEH", "XL" };

        // Titan requirement tables now live in the Unity-free TitanTables class so they can be shape-tested
        // (finding #33). Aliased here so the rest of this class keeps referencing TitanAk/TitanGuide unchanged.
        //   TitanAk:    [titanIndex][version-1] = { autokill atk, def, HP-regen (0 = no gate) }
        //   TitanGuide: [titanIndex][version-1] = { manual atk, manual def, idle atk, idle def (0/0 = no idle stage) }
        private static readonly double[][][] TitanAk = TitanTables.Ak;
        private static readonly double[][][] TitanGuide = TitanTables.Guide;

        private static List<Rec> _cache = new List<Rec>();
        private static DateTime _cacheTime = DateTime.MinValue;
        private const double CacheMs = 2000;

        public static List<Rec> Analyze()
        {
            if ((DateTime.UtcNow - _cacheTime).TotalMilliseconds < CacheMs && _cache.Count > 0)
                return _cache;
            try
            {
                _cache = Compute();
                _cacheTime = DateTime.UtcNow;
            }
            catch (Exception e) { Main.LogDebug($"OptimizationAdvisor failed: {e.Message}"); }
            return _cache;
        }

        // Wandoos is the PRIMARY power source when the usual sources are unavailable: inside a challenge
        // block (challenges reset augs/number and you build power in one run), No-Rebirth (the Number
        // multiplier is dead), No-Augs, or gold too low to maintain augments. In those contexts AT
        // Wandoos (AT-3 = Wandoos-Energy, AT-4 = Wandoos-Magic) is what to level for power — and the
        // direct Wandoos dump earns its allocation lane no matter how slow it looks, which is why
        // WandoosBP asks this before retiring itself.
        public static bool WandoosIsPowerSource()
        {
            try
            {
                var c = Main.Character;
                if (c == null) return false;
                bool inBlock = SafeCurrentChallenge() != null || SafeAnyChallengesValid();
                return inBlock || GoldStarvedForAugs(c);
            }
            catch { return false; }
        }

        private static List<Rec> Compute()
        {
            var list = new List<Rec>();
            var c = Main.Character;
            if (c == null) return list;

            var prog = ProgressionAnalyzer.Detect();
            string mode = Mode(prog);

            string ch = SafeCurrentChallenge();
            bool inBlock = ch != null || SafeAnyChallengesValid();
            bool goldStarved = !inBlock && GoldStarvedForAugs(c);
            bool wandoosIsPower = WandoosIsPowerSource();
            string wReason = ch == "NORB" ? "No Rebirth: no Number mult"
                : ch == "NOAUG" ? "No Augs"
                : inBlock ? "challenge block"
                : goldStarved ? "gold too low for Augs" : "";

            // POWER — only during a titan push, and only as a gap to the next autokill target.
            // (In a challenge or while farming there is no power target, so no row at all.)
            if (mode == "push")
            {
                try
                {
                    int idx = NextTitanIndex();
                    if (idx >= 0 && TryAkRequirement(idx, out var reqA, out var reqD, out var reqR))
                    {
                        double atk = c.totalAdvAttack(), def = c.totalAdvDefense();
                        double rgn = 0;
                        try { rgn = c.totalAdvHPRegen(); } catch { }
                        if (atk >= reqA && def >= reqD && (reqR <= 0 || rgn >= reqR))
                            list.Add(new Rec { System = "Power", Text = $"Titan {idx + 1} autokill ready", Optimal = true });
                        else
                        {
                            double pct = Math.Min(atk / reqA, def / reqD);
                            if (reqR > 0) pct = Math.Min(pct, rgn / reqR);
                            pct *= 100.0;
                            string regenPart = reqR > 0 && rgn < reqR ? $" / {Fmt(rgn)} of {Fmt(reqR)} regen" : "";
                            list.Add(new Rec
                            {
                                System = "Power",
                                Text = $"{Fmt(atk)} of {Fmt(reqA)} atk / {Fmt(def)} of {Fmt(reqD)} def{regenPart} for Titan {idx + 1} AK ({pct:0}%)",
                                Severity = pct >= 80 ? 1 : 2
                            });
                        }
                    }
                }
                catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }
            }

            // GEAR — a gap only when re-optimizing would gain meaningfully (ProgressionAnalyzer computes it).
            try
            {
                string focus = prog.Known ? prog.OptimalFocus : null;
                bool gap = !string.IsNullOrEmpty(focus) && focus.StartsWith("Re-optimize");
                list.Add(new Rec
                {
                    System = "Gear",
                    AutoKey = "gear",
                    Text = gap ? focus : "Near-optimal",
                    Severity = gap ? 1 : 0,
                    Optimal = !gap
                });
            }
            catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }

            // WANDOOS — a gap only when the exact comparator says another OS wins at your caps.
            try
            {
                if (c.wandoos98.installed || c.wandoos98.OSlevel > 0)
                {
                    long cap = Math.Min(c.totalCapEnergy(), c.totalCapMagic());
                    var w = WandoosAdvisor.Compare(WandoosAdvisor.RunHorizonMinutes());
                    if (w.Known && w.BestOs != w.CurrentOs)
                    {
                        bool big = w.Advantage >= 1.25;
                        list.Add(new Rec
                        {
                            System = "Wandoos",
                            AutoKey = "wandoos",
                            Text = $"Switch OS {w.CurrentName} -> {w.BestName}: ~{WandoosAdvisor.FmtX(w.Advantage)} more A/D bonus at your cap ({Fmt(cap)})",
                            Severity = big ? 2 : 1
                        });
                    }
                    else
                    {
                        list.Add(new Rec
                        {
                            System = "Wandoos",
                            AutoKey = "wandoos",
                            Text = $"OS {WandoosOsNames[Clamp((int)c.wandoos98.os)]} is best for your cap",
                            Optimal = true
                        });
                    }
                }
            }
            catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }

            // ADV TRAINING — an action only when context demands one (Wandoos-as-power or a push).
            try
            {
                bool autoWish = AtAutoByWish(c);
                if (autoWish)
                    list.Add(new Rec { System = "Adv Training", Text = "Auto-managed by wish 190", Optimal = true });
                else if (wandoosIsPower)
                {
                    // Concrete target instead of "push HIGH": ~25% above the current E/M Wandoos AT level
                    // (rounded to 500) so allocation still cycles energy to other priorities, raised as reached.
                    long wanHi = Math.Max(AtLevel(c, 3), AtLevel(c, 4));
                    long target = Math.Max(1000, (long)Math.Ceiling(wanHi * 1.25 / 500.0) * 500);
                    list.Add(new Rec
                    {
                        System = "Adv Training",
                        Text = $"E/M Wan is your power source ({wReason}) — target ~L{target:#,##0}, raise as reached; keep AT-0/1/2 low",
                        Severity = 1
                    });
                }
                else if (mode == "push")
                    list.Add(new Rec { System = "Adv Training", Text = "Raise AT Attack/Defense targets for the titan run", Severity = 1 });
                else
                    list.Add(new Rec { System = "Adv Training", Text = "Minimal AT is right for farming", Optimal = true });
            }
            catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }

            // DIGGERS — gaps: recommended-but-missing (with live affordability) and recap headroom.
            try
            {
                var active = ToIntList(c.diggers.activeDiggers);
                int slots = Math.Max(1, c.allDiggers.maxDiggerSlots());
                var recommended = RecommendedDiggers(mode).Where(IsDiggerUnlocked).Take(slots).ToList();
                if (mode == "challenge")
                    list.Add(new Rec { System = "Diggers", AutoKey = "diggers", Text = "Set by the challenge block", Optimal = true });
                else
                {
                    double netGps = c.goldPerSecond();
                    var addable = new List<string>();
                    var broke = new List<string>();
                    foreach (var d in recommended.Where(r => !active.Contains(r)))
                    {
                        long lvl = AffordableDiggerLevel(c, d, netGps);
                        if (lvl >= 1) addable.Add($"{Name(DiggerNames, d)} (L{lvl} affordable)");
                        else broke.Add(Name(DiggerNames, d));
                    }
                    int recappable = 0;
                    foreach (var d in active)
                    {
                        long cur = c.diggers.diggers[d].curLevel;
                        long aff = AffordableDiggerLevel(c, d, netGps + c.allDiggers.drain(d));
                        if (aff > cur && aff - cur >= Math.Max(5, cur / 10)) recappable++;
                    }
                    if (addable.Count == 0 && broke.Count == 0 && recappable == 0)
                        list.Add(new Rec { System = "Diggers", AutoKey = "diggers", Text = "Ideal set is running", Optimal = true });
                    else
                    {
                        string advice = (addable.Count > 0 ? "Add " + string.Join("/", addable) : "")
                            + (broke.Count > 0 ? (addable.Count > 0 ? " " : "") + "(" + string.Join("/", broke) + ": GPS too low)" : "");
                        if (recappable > 0) advice += (advice == "" ? "Recap" : " | recap") + $": {recappable} digger(s) can run higher levels";
                        list.Add(new Rec { System = "Diggers", AutoKey = "diggers", Text = advice, Severity = addable.Count > 0 || recappable > 0 ? 1 : 0 });
                    }
                }
            }
            catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }

            // BEARDS — gap: recommended-but-missing for the goal.
            try
            {
                var active = ToIntList(c.beards.activeBeards);
                int slots = Math.Max(1, c.allBeards.capBeards());
                var recommended = RecommendedBeards(mode).Where(b => b != 6 || GoldenUnlocked()).Take(slots).ToList();
                if (mode == "challenge")
                    list.Add(new Rec { System = "Beards", AutoKey = "beards", Text = "Set by the challenge block", Optimal = true });
                else
                {
                    var missing = recommended.Where(r => !active.Contains(r)).Select(i => Name(BeardNames, i)).ToList();
                    if (missing.Count == 0)
                        list.Add(new Rec { System = "Beards", AutoKey = "beards", Text = "Ideal set is running", Optimal = true });
                    else
                        list.Add(new Rec { System = "Beards", AutoKey = "beards", Text = "Add " + string.Join("/", missing), Severity = 1 });
                }
            }
            catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }

            // YGGDRASIL — next fruit-tier purchase in the GUIDE's order (SpendPlanner; the guide's ch3/ch4
            // sequence with its eat/harvest reasoning), with live cost + affordability.
            try
            {
                var fb = SpendPlanner.NextFruit();
                if (fb.Known)
                    list.Add(new Rec
                    {
                        System = "Yggdrasil",
                        AutoKey = "yggbuys",
                        Text = $"Buy next: {fb.Name} tier {fb.CurLevel + 1} — {FmtSeeds(fb.Cost)} seeds"
                            + (fb.Affordable ? "" : $" (save up - {FmtSeeds(c.yggdrasil.seeds)} now)"),
                        Severity = fb.Affordable ? 1 : 0
                    });
                else if (c.yggdrasil != null && c.yggdrasil.fruits != null && c.yggdrasil.fruits.Any(f => f.maxTier > 0))
                    list.Add(new Rec { System = "Yggdrasil", AutoKey = "yggbuys", Text = "Guide fruit plan complete for this chapter", Optimal = true });
            }
            catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }

            // PERKS / QUIRKS — next buy in the guide's chapter order (SpendPlanner), live costs/levels.
            try
            {
                var pb = SpendPlanner.NextPerk();
                long pp = 0; try { pp = c.adventure.itopod.perkPoints; } catch { }
                if (pb.Known)
                    list.Add(new Rec
                    {
                        System = "Perks",
                        AutoKey = "perks",
                        Text = $"Buy next: {pb.Name} L{pb.CurLevel}->{pb.TargetLevel} ({FmtSeeds(pb.Cost)} PP each, have {FmtSeeds(pp)})",
                        Severity = pb.Affordable ? 1 : 0
                    });
                else
                {
                    // Mirror the quirks row (user-reported: "plan complete" showed while the guide
                    // still had queued buys — chapter/difficulty-gated steps must read as BANKING,
                    // and "complete" is only true when the WHOLE plan is exhausted).
                    var fp = SpendPlanner.NextPerkPlanned();
                    if (fp.Known)
                        list.Add(new Rec
                        {
                            System = "Perks",
                            AutoKey = "perks",
                            Text = $"Bank PP — next guide buy at ch.{fp.MinChapter}"
                                + (fp.DifficultyGated ? " (needs next difficulty)" : "")
                                + $": {fp.Name} ({FmtSeeds(fp.Cost)} PP, have {FmtSeeds(pp)})",
                            Optimal = pp < fp.Cost,
                            Severity = 0
                        });
                    else if (prog.Known && (pp > 0 || Chapter3Plus(prog)))
                        list.Add(new Rec { System = "Perks", AutoKey = "perks", Text = "Guide perk plan complete", Optimal = true });
                }
            }
            catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }

            try
            {
                var qb = SpendPlanner.NextQuirk();
                long qp = 0; try { qp = c.beastQuest.quirkPoints; } catch { }
                if (qb.Known)
                    list.Add(new Rec
                    {
                        System = "Quirks",
                        AutoKey = "quirks",
                        Text = $"Buy next: {qb.Name} L{qb.CurLevel}->{qb.TargetLevel} ({FmtSeeds(qb.Cost)} QP each, have {FmtSeeds(qp)})",
                        Severity = qb.Affordable ? 1 : 0
                    });
                else
                {
                    // Nothing buyable NOW. The old "plan complete" text hid that the plan is usually
                    // just WAITING (on Normal the guide's next buys are ch.5/Evil) — say what banked
                    // QP is for; once the bank covers it, surface as a visible card, not collapsed.
                    var fq = SpendPlanner.NextQuirkPlanned();
                    if (fq.Known)
                        list.Add(new Rec
                        {
                            System = "Quirks",
                            AutoKey = "quirks",
                            Text = $"Bank QP — next guide buy at ch.{fq.MinChapter}"
                                + (fq.DifficultyGated ? " (needs next difficulty)" : "")
                                + $": {fq.Name} ({FmtSeeds(fq.Cost)} QP, have {FmtSeeds(qp)})",
                            Optimal = qp < fq.Cost,
                            Severity = 0
                        });
                    else if (prog.Known && qp > 0)
                        list.Add(new Rec { System = "Quirks", AutoKey = "quirks", Text = "Guide quirk plan complete", Optimal = true });
                }
            }
            catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }

            // BEARD PERM — rebirth-timing target for permanent beard levels ("shavings"):
            // gain = floor(sqrt(beardLevel) × timeFactor), timeFactor = min(rebirthTime/3h × 24/(24-perk21), 8).
            // Meaningless inside a challenge block (no free rebirth timing there).
            try
            {
                if (!inBlock)
                {
                    var actives = ToIntList(c.beards.activeBeards);
                    if (actives.Count > 0)
                    {
                        double tf = c.allBeards.timeFactor();
                        double perk = 0;
                        try { perk = c.adventure.itopod.perkLevel[21]; } catch { }
                        long totalGain = 0; double nextHours = double.MaxValue;
                        foreach (var b in actives)
                        {
                            long lvl = c.beards.beards[b].beardLevel;
                            if (lvl <= 0) continue;
                            double sq = Math.Sqrt(lvl);
                            long gain = (long)Math.Floor(sq * tf);
                            if (gain > lvl) gain = lvl;
                            totalGain += gain;
                            if (tf < 8.0 && gain < lvl)
                            {
                                double tfNeeded = (gain + 1) / sq;
                                if (tfNeeded <= 8.0)
                                {
                                    double tSec = tfNeeded * 10800.0 * (24.0 - perk) / 24.0;
                                    double hrs = (tSec - c.rebirthTime.totalseconds) / 3600.0;
                                    if (hrs > 0 && hrs < nextHours) nextHours = hrs;
                                }
                            }
                        }
                        if (totalGain <= 0 && tf <= 0)
                            list.Add(new Rec { System = "Beard perm", Text = "Rebirthing before the 1h mark banks NO permanent beard levels — wait", Severity = 1 });
                        else if (nextHours != double.MaxValue && nextHours <= 12)
                            list.Add(new Rec { System = "Beard perm", Text = $"Rebirth now banks +{totalGain} permanent beard levels; +1 more if you hold ~{FmtH(nextHours)}", Severity = 0 });
                        else if (totalGain > 0)
                            list.Add(new Rec { System = "Beard perm", Text = $"Rebirth banks +{totalGain} permanent beard levels", Optimal = true });
                    }
                }
            }
            catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }

            // EXP — next best EXP purchase toward the guide's spend ratios for the CURRENT phase (see
            // ExpRatioTables). ExpBalancer works in EXP-space with the game's unit costs (magic units
            // cost 3x energy), so the pool split and the stat balance are decided together.
            try
            {
                // No chapter floor: ExpRatioTables covers ch.1-2 (energy-only, 1:37.5k:1) too, and
                // AdvisorApply has been buying in those chapters all along — the row should say so.
                if (prog.Known && c.highestBoss >= 17)   // custom purchases: permanent unlock
                {
                    var xb = ExpBalancer.Analyze();
                    if (xb.Known && !xb.Balanced)
                        list.Add(new Rec
                        {
                            System = "EXP",
                            AutoKey = "exp",
                            Text = $"Walking toward guide ratio {xb.Phase} — {xb.BalancePct:0}% balanced; next EXP → {xb.NextNames}",
                            Severity = 1
                        });
                    else if (xb.Known)
                        list.Add(new Rec { System = "EXP", AutoKey = "exp", Text = $"Purchases match the guide ratio for this phase ({xb.Phase})", Optimal = true });
                }
            }
            catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }

            // GOLD — titan gold banking status. Auto mode ranks the AK-able titans by predicted gold
            // (titan gold is NOT monotone in index) and re-banks when the AK version rises.
            try
            {
                if (Main.Settings != null && Main.Settings.ManageGoldLoadouts)
                {
                    int best = AdvisorApply.GoldTitanTarget();
                    if (best >= 0)
                    {
                        int ver = 1;
                        try { ver = ZoneHelpers.TitanVersion(best); } catch { }
                        var done = Main.Settings.TitanMoneyDone;
                        bool banked = done != null && best < done.Length && done[best];
                        if (banked)
                            list.Add(new Rec { System = "Gold", AutoKey = "titangold", Text = $"Titan {best + 1} (v{ver}) gold banked this run", Optimal = true });
                        else
                            list.Add(new Rec { System = "Gold", AutoKey = "titangold", Text = $"Bank gold on the next Titan {best + 1} (v{ver}) auto-kill", Severity = 1 });
                    }
                }
            }
            catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }

            // BLOOD — Iron Pill breakpoint plan (BloodPlanner): cast timing from live blood/sec, the
            // pill's cooldown, and the run's remaining time — replaces hand-picked power thresholds.
            try
            {
                if (Main.Settings != null && Main.Settings.CastBloodSpells)
                {
                    var bp2 = BloodPlanner.Analyze();
                    if (bp2.Known)
                    {
                        string text = bp2.Text;
                        BloodPlanner.FillRouting(ref bp2);
                        if (bp2.RouteKnown && !bp2.PoolForPill && !string.IsNullOrEmpty(bp2.RouteReason))
                            text += $" | route: {bp2.RouteReason}";
                        var detail = BloodPlanner.InvestmentDetail();
                        if (detail != null)
                            text += $" | {detail}";
                        list.Add(new Rec { System = "Blood", AutoKey = "blood", Text = text, Severity = bp2.Severity, Optimal = bp2.Optimal });
                    }
                }
            }
            catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }

            // ITOPOD — beacon vs the idle-best floor (ItopodConstants.BestFloor, the same game-truth
            // one-shot solve the optimizer uses). Auto-managed when optimize mode is on.
            try
            {
                int highest = c.adventure.highestItopodLevel;
                if (highest > 0)
                {
                    int optimal = ItopodConstants.BestFloor(c.totalAdvAttack(), c.idleAttackPower(), false);
                    int maxL = c.adventureController.maxItopodLevel();
                    if (optimal > maxL) optimal = maxL;
                    if (optimal < 0) optimal = 0;
                    int reachable = Math.Min(optimal, highest - 1);
                    bool canPushHigher = optimal > highest - 1;
                    // A fixed floor is managed too, but by the USER's number — reporting the solved
                    // idle-best there would describe a floor the advisor is deliberately not using.
                    bool fixedFloor = Main.Settings != null && Main.Settings.ITOPODFloorMode == 1;
                    bool managed = Main.Settings != null && (Main.Settings.ITOPODOptimizeMode > 0 || fixedFloor);
                    int start = c.adventure.itopodStart;
                    if (fixedFloor)
                        list.Add(new Rec { System = "ITOPOD", Text = $"Floor fixed at {Main.Settings.ITOPODTargetFloor} (idle-best would be ~{reachable})", Optimal = true });
                    else if (managed)
                        list.Add(new Rec { System = "ITOPOD", Text = $"Floor auto-optimized (idle-best ~{reachable}{(canPushHigher ? ", can push higher" : "")})", Optimal = true });
                    else if (Math.Abs(start - reachable) >= 10)
                        list.Add(new Rec { System = "ITOPOD", Text = $"Set beacon to ~{reachable} (now {start}){(canPushHigher ? " and push floors" : "")}, or enable ITOPOD optimization", Severity = 1 });
                    else
                        list.Add(new Rec { System = "ITOPOD", Text = $"Beacon near the idle-best floor (~{reachable})", Optimal = true });
                }
            }
            catch (Exception ex) { Main.LogDebug($"Advisor rec failed: {ex.Message}"); }

            // NGU value row (user request): the live x/hr math the auto profile allocates by.
            try
            {
                var plan = NGUAdvisors.Compute(
                    ChallengeOverlay.ChapterNguIds(AllocationProfiles.BreakpointTypes.ResourceType.Energy),
                    ChallengeOverlay.ChapterNguIds(AllocationProfiles.BreakpointTypes.ResourceType.Magic));
                if (plan.Known)
                {
                    int spare = plan.EnergySurplus.Length + plan.MagicSurplus.Length;
                    list.Add(new Rec
                    {
                        System = "NGUs",
                        Text = $"{plan.Summary} — running {plan.EnergyTargets.Length}E/{plan.MagicTargets.Length}M"
                            + (spare > 0 ? $" (+{spare} surplus lanes)" : ""),
                        Severity = 1
                    });
                }
            }
            catch (Exception ex) { Main.LogDebug($"NGU rec failed: {ex.Message}"); }

            // Boss-ceiling row: past the last unlock at this difficulty, boss pushing = EXP only,
            // and the NUMBER ritual (power through time) is what moves it.
            try
            {
                int ceiling = BossUnlockCeiling();
                int boss = Math.Max(0, c.bossID - 1);
                if (ceiling > 0 && boss >= ceiling)
                    list.Add(new Rec
                    {
                        System = "Boss push",
                        Text = $"Unlocks done at this difficulty (last: boss {ceiling}) — NUMBER ritual + boss EXP are the push",
                        Severity = 1
                    });
            }
            catch (Exception ex) { Main.LogDebug($"Boss ceiling rec failed: {ex.Message}"); }

            // LSC opportunity (user rule): the Laser Sword Challenge never resets the number and its
            // goal is just leveling the laser sword aug — when it fits inside an Augs window it's
            // free challenge progress.
            try
            {
                var lsc = LscAdvisor.Compute();
                if (lsc.Known && lsc.Recommended)
                    list.Add(new Rec { System = "LSC", Text = lsc.Text, Severity = 1 });
            }
            catch (Exception ex) { Main.LogDebug($"LSC rec failed: {ex.Message}"); }

            return list;
        }

        private static bool Chapter3Plus(ProgressionAnalyzer.Progression prog)
        {
            try { return prog.Known && prog.Chapter >= 3; }
            catch { return false; }
        }


        private static string FmtH(double hours)
        {
            if (hours < 1) return $"{hours * 60:0}m";
            int h = (int)hours;
            return $"{h}h{(hours - h) * 60:00}m";
        }

        // The lowest titan we can't yet autokill — the push target. Derived from AdvisorApply's cached
        // highest-AK scan (audit fix: this used to duplicate the 12-titan reflection sweep every 2s).
        // Public: the Titans panel's hero card shows the same target.
        // Titans available per difficulty (guide: T7 unlocks at Boss 125 IN EVIL, T9 is late Evil,
        // T10+ are Sadistic). The old NextTitanIndex (= highest AK'd + 1) ignored this AND versions:
        // on Normal with T6 partially AK'd it chased T7's stats — unreachable content — instead of
        // T6's next version (user-reported: whole rebirths wasted pushing impossible numbers).
        public static int DifficultyMaxTitanIndex()
        {
            try
            {
                var d = Main.Character.settings.rebirthDifficulty;
                if (d >= difficulty.sadistic) return 13;
                if (d >= difficulty.evil) return 8;
            }
            catch { }
            return 5;   // Normal: T6 is the end of the line
        }

        public class TitanObjective
        {
            public bool Known;      // false = no objective was produced — see LadderComplete for WHY
            // Only meaningful when Known == false, and the two cases are NOT interchangeable:
            //   true  = the whole ladder was walked and every titan+version at this difficulty is
            //           already auto-killed. There is genuinely nothing left to beat.
            //   false = the answer could not be read (an AK-table miss on a still-un-AK'd version, or
            //           an exception). "No objective" here means "unknown", not "done".
            // Callers that treat Known == false as "requirements met" (LevelPlanner's P/T freeze) MUST
            // branch on this, or a lookup failure silently reads as sufficiency.
            public bool LadderComplete;
            public int Index;
            public int Version;     // the first un-AK'd version — the actual chase
            public string Stage;    // "first kill" -> "idle" -> "auto-kill" (user's kill ladder)
            public double ReqAttack, ReqDefense;   // the STAGE's requirement, not blindly AK
            public double ReqRegen;                // AK stage only (game gates on HP regen from T4 up); 0 otherwise
        }

        // Has this titan+version been killed at least once? The titan6V1Kills..V4Kills save fields
        // are DEAD — the game never increments them (only ImportExport zeroes them), so reading them
        // always yields 0 (user report: chip stuck on FIRST KILL (v2) after a confirmed v2 kill).
        // The real per-version record for T6 is achievements 148..151 (Beast v1..v4), marked in
        // AdventureController.killedTitan (zone 19) and the AK path alike. T7+ keep NO per-version
        // record at all (only a V4 achievement survives) — spawn-version proxy stays there.
        private static bool VersionKilled(int i, int v)
        {
            try
            {
                var adv = Main.Character.adventure;
                if (i == 5)
                {
                    try { return Main.Character.achievements.achievementComplete[147 + v]; }
                    catch (Exception e)
                    {
                        Main.LogDebug($"VersionKilled achievement {147 + v} read failed: {e.Message}");
                        return ZoneHelpers.TitanVersion(i) - 1 >= v;
                    }
                }
                if (i >= 6)
                    return ZoneHelpers.TitanVersion(i) - 1 >= v;
                switch (i)
                {
                    case 0: return adv.titan1Kills >= 1;
                    case 1: return adv.titan2Kills >= 1;
                    case 2: return adv.titan3Kills >= 1;
                    case 3: return adv.titan4Kills >= 1;
                    case 4: return adv.titan5Kills >= 1;
                }
            }
            catch { }
            return false;
        }

        // Would the optimizer's best Power/Toughness gear clear a requirement current gear can't?
        // Attack scales ~linearly with the Power stat, so best/current objective-score ratios are a
        // sound projection multiplier. Cached: two optimizer runs are heavy.
        private static double _projAtkMult = 1, _projDefMult = 1;
        private static DateTime _projAt = DateTime.MinValue;

        public static void ProjectedBestGear(out double atkMult, out double defMult)
        {
            if ((DateTime.UtcNow - _projAt).TotalSeconds > 120)
            {
                _projAt = DateTime.UtcNow;
                try
                {
                    var pow = GearOptimizer.FindObjective("Power");
                    var tou = GearOptimizer.FindObjective("Toughness");
                    double curP = GearOptimizer.CurrentScore(pow);
                    double curT = GearOptimizer.CurrentScore(tou);
                    // No pins: these are projection MULTIPLIERS against CurrentScore, which knows nothing
                    // about pins, so both sides must measure the same unconstrained best.
                    var bestP = GearOptimizer.Optimize(pow, false, new int[0]);
                    var bestT = GearOptimizer.Optimize(tou, false, new int[0]);
                    _projAtkMult = curP > 0 && bestP != null && bestP.Score > curP ? bestP.Score / curP : 1;
                    _projDefMult = curT > 0 && bestT != null && bestT.Score > curT ? bestT.Score / curT : 1;
                }
                catch { _projAtkMult = 1; _projDefMult = 1; }
            }
            atkMult = _projAtkMult;
            defMult = _projDefMult;
        }

        // Kill-ladder requirement (user rule): never killed -> the guide's MANUAL first-kill stats;
        // killed -> the guide's IDLE stats until met (skipped where the guide lists none); then the
        // game's exact AK stats, which from T4 up include the HP-regen gate (reqR; 0 elsewhere).
        public static void StagedRequirementFor(int i, int v, out double reqA, out double reqD, out double reqR, out string stage)
        {
            double atk = 0, def = 0;
            try { atk = Main.Character.totalAdvAttack(); def = Main.Character.totalAdvDefense(); } catch { }
            StagedRequirementFor(i, v, atk, def, out reqA, out reqD, out reqR, out stage);
        }

        // Stats-parameterized core: the AT-hour planner stages the ladder against PROJECTED
        // best-gear stats — at its decision point the character wears AT-speed gear, whose live
        // stats say nothing about what the kill loadout can clear.
        public static void StagedRequirementFor(int i, int v, double atk, double def, out double reqA, out double reqD, out double reqR, out string stage)
        {
            TryAkRequirementFor(i, v, out var akA, out var akD, out var akR);
            var g = i >= 0 && i < TitanGuide.Length && v >= 1 && v <= TitanGuide[i].Length ? TitanGuide[i][v - 1] : null;
            if (!VersionKilled(i, v))
            {
                stage = "first kill";
                reqA = g != null ? g[0] : akA;
                reqD = g != null ? g[1] : akD;
                reqR = 0;
                return;
            }
            if (g != null && g[2] > 0 && (atk < g[2] || def < g[3]))
            {
                stage = "idle";
                reqA = g[2];
                reqD = g[3];
                reqR = 0;
            }
            else
            {
                stage = "auto-kill";
                reqA = akA;
                reqD = akD;
                reqR = akR;
            }
        }

        // The first titan+version at THIS difficulty that isn't auto-killed yet: the realistic goal,
        // with the requirement staged along the kill ladder.
        public static TitanObjective NextObjective()
        {
            var o = new TitanObjective();
            try
            {
                // A skipped candidate is NOT the same as no candidate: a version that is still
                // un-AK'd but has no readable AK requirement is an unknown, and the loop running out
                // afterwards must not be reported as "the ladder is finished".
                bool lookupFailed = false;
                int ceiling = Math.Min(DifficultyMaxTitanIndex(), TitanAk.Length - 1);
                for (int i = 0; i <= ceiling; i++)
                {
                    int versions = AkVersionCount(i);
                    for (int v = 1; v <= versions; v++)
                    {
                        bool ak = false;
                        try { ak = ZoneHelpers.AutokillAvailable(i, v); } catch { }
                        if (ak) continue;
                        if (!TryAkRequirementFor(i, v, out _, out _, out _)) { lookupFailed = true; continue; }
                        StagedRequirementFor(i, v, out var ra, out var rd, out var rr, out var stage);
                        o.Known = true;
                        o.Index = i;
                        o.Version = v;
                        o.Stage = stage;
                        o.ReqAttack = ra;
                        o.ReqDefense = rd;
                        o.ReqRegen = rr;
                        return o;
                    }
                }
                // Walked the entire ladder without returning: every version was auto-killed unless a
                // requirement lookup failed on the way. An exception leaves LadderComplete false.
                o.LadderComplete = !lookupFailed;
            }
            catch { }
            return o;
        }

        public static int NextTitanIndex()
        {
            var o = NextObjective();
            return o.Known ? o.Index : -1;
        }

        // Highest boss that still unlocks something at this difficulty: zones up to the ceiling
        // titan's zone (later zones are next-era content). Past it, bosses are pure EXP.
        public static int BossUnlockCeiling()
        {
            try
            {
                int ceilingTitan = DifficultyMaxTitanIndex();
                int maxZone = ceilingTitan < ZoneHelpers.TitanZones.Length
                    ? ZoneHelpers.TitanZones[ceilingTitan]
                    : ZoneHelpers.ZoneUnlocks.Length - 1;
                int best = 0;
                for (int z = 0; z <= maxZone && z < ZoneHelpers.ZoneUnlocks.Length; z++)
                    best = Math.Max(best, ZoneHelpers.ZoneUnlocks[z]);
                return best;
            }
            catch { return 0; }
        }

        // Guide rules of thumb: rebirth length scales with Yggdrasil — 30-60m before fruits exist,
        // ~1h per highest fruit tier once they do, full 24h runs from the first tier-24 fruit.
        public static string RecommendedRunLength()
        {
            try
            {
                var c = Main.Character;
                long topTier = 0;
                var fruits = c.yggdrasil?.fruits;
                if (fruits != null)
                    foreach (var f in fruits)
                        if (f.maxTier > topTier) topTier = f.maxTier;
                if (topTier >= 24) return "24h runs (tier-24 fruit: max Yggdrasil gains)";
                if (topTier >= 2) return $"~{topTier}h runs (scale to highest fruit tier {topTier})";
                if (topTier >= 1) return "1-2h runs (Yggdrasil just started)";
                return "30-60m runs (pre-Yggdrasil: farm boss EXP)";
            }
            catch { return null; }
        }

        public static bool TryAkRequirement(int titanIndex, out double reqAttack, out double reqDefense, out double reqRegen)
        {
            int version = 1;
            try { version = ZoneHelpers.TitanVersion(titanIndex); } catch { }
            return TryAkRequirementFor(titanIndex, version, out reqAttack, out reqDefense, out reqRegen);
        }

        // Requirement for a SPECIFIC version (Titans hero card's NEXT VERSION box).
        public static bool TryAkRequirementFor(int titanIndex, int version, out double reqAttack, out double reqDefense, out double reqRegen)
        {
            reqAttack = 0; reqDefense = 0; reqRegen = 0;
            if (titanIndex < 0 || titanIndex >= TitanAk.Length) return false;
            var versions = TitanAk[titanIndex];
            if (version < 1 || version > versions.Length) return false;
            var req = versions[version - 1];
            reqAttack = req[0]; reqDefense = req[1]; reqRegen = req[2];
            return true;
        }

        // How many versions this titan has in the AK table (1 for unversioned).
        public static int AkVersionCount(int titanIndex)
        {
            if (titanIndex < 0 || titanIndex >= TitanAk.Length) return 1;
            return TitanAk[titanIndex].Length;
        }

        // Public set queries for AdvisorApply (Phase B). Return null when the advisor should NOT
        // drive the set (no character, or in a challenge where profile/challenge-tagged breakpoints own it).
        public static int[] CurrentDiggerSet()
        {
            try
            {
                var c = Main.Character;
                if (c == null) return null;
                int slots = Math.Max(1, c.allDiggers.maxDiggerSlots());

                // "Floor-restricted" (the profile's own term): there are still content-unlocking bosses
                // left to beat, so stats are the bottleneck to progress. Reuses the same ceiling signal
                // the EXP-promote uses. ceiling0 == true means the climb is DONE (farming).
                bool ceiling0 = false;
                try { ceiling0 = c.bossID - 1 >= BossUnlockCeiling(); } catch { }

                // HYBRID membership (user rule): when a MANUAL profile names diggers for this phase, THAT
                // list is the candidate pool — the advisor adds no fill-every-slot filler on top of it. It
                // only reorders (the DIGGER LAWS below, for leveling priority) and levels WITHIN the pool;
                // poolFilter strips any digger a law transiently introduced. This is what keeps the NGU
                // diggers (4/5) off before the profile's ALLNGU phase and lets the profile's phase choices
                // win. AutoProfile has no digger breakpoints, so it always falls through to the advisor's
                // own goal-aware fill-every-slot set (guarded on !AutoProfile so a stale manual profile
                // left loaded while AutoProfile drives can't leak its list in).
                HashSet<int> poolFilter = null;
                List<int> order;
                int[] pool = null;
                try
                {
                    if (Main.Settings != null && !Main.Settings.AutoProfile)
                        pool = AllocationProfiles.Breakpoints.DiggerBreakpoints.ActiveProfileDiggers();
                }
                catch { }

                if (pool != null && pool.Length > 0)
                {
                    order = new List<int>(pool.Where(d => d >= 0 && d < 12).Distinct());
                    poolFilter = new HashSet<int>(order);
                }
                else
                {
                    string mode = Mode(ProgressionAnalyzer.Detect());
                    // Defer to the profile only when a MANUAL profile owns diggers via challenge-tagged
                    // breakpoints. AutoProfile has none — so it must drive diggers (DIGGER LAWS + gold gate)
                    // during challenges too, else the whole set sits off (user-caught: diggers never fired
                    // in a BASIC challenge on the Evil climb, since the profile set was applied once at
                    // rebirth at 0 gold and never retried). RecommendedDiggers maps a challenge to a set.
                    if (mode == "challenge" && !Main.Settings.AutoProfile) return null;
                    // Fill EVERY slot (user rule: level drain is capped by RecapDiggers, so an empty
                    // slot is pure waste): mode heads lead, everything else follows; the EXP digger is
                    // promoted while farming (user-reported: EXP digger off in farm mode).
                    // DIGGER LAWS (user-corrected semantics, 3rd revision — the deep-dive model):
                    //   Digger 3 "Adv"   -> ADVENTURE stats: titans, zones, ITOPOD survival. ALWAYS ON.
                    //   Digger 2 "Stats" -> BOSS-FIGHT stats: only while stats gate progress (boss push /
                    //                       floor-restricted / challenge).
                    //   Digger 0 "DC" / 8 "PP" -> near-interchangeable utility pair, picked by VENUE:
                    //                       ITOPOD pays PP but has FLAT drop rolls (no DC scaling);
                    //                       zones + titan kills pay drops but no PP. Titan window ->
                    //                       DC in, PP out.
                    //   Digger 10 "Blood" -> only while rituals actually cast (live-consumer rule).
                    // Branches set the growth/mode base; the laws then override in priority order.
                    if (ChallengeOverlay.Segment == "NGU MARATHON")
                    {
                        order = new List<int> { 4, 5, 11, 6, 7, 8, 1, 0 };   // growth multipliers first
                    }
                    else
                    {
                        order = new List<int>(RecommendedDiggers(mode));
                        if (mode == "farm" || ceiling0)
                        {
                            int exp = FindDiggerByBonus("exp");
                            if (exp >= 0)
                            {
                                order.Remove(exp);
                                order.Insert(Math.Min(2, order.Count), exp);
                            }
                        }
                    }
                    for (int i = 0; i <= Consts.MAX_DIGGER_ID; i++)
                        if (!order.Contains(i)) order.Add(i);
                }

                // Context for the laws (shared by the Hybrid pool and the fallback fill-every-slot set).
                bool hunting = false;
                try { hunting = GearHunter.Active && GearHunter.ZoneReachable(); } catch { }
                bool itopod = false;
                try { itopod = !hunting && (Main.Settings.AdventureTargetITOPOD || Main.Settings.SnipeZone >= 1000); } catch { }
                // Window sized to the machinery, not the event (user-reported: diggers swapped ~8min
                // before a titan/gold snipe and lingered after): the digger applier ticks every 30s
                // and the titan gear lock engages <20s before spawn, so 60s guarantees exactly one
                // swap pass lands just ahead of the lock — seconds before the event, and the set
                // reverts on the first tick after the kill releases the lock.
                bool titanWindow = false;
                try
                {
                    var o = NextObjective();
                    if (o.Known)
                    {
                        float? t = ZoneHelpers.TimeTillTitanSpawn(o.Index);
                        titanWindow = t.HasValue && t.Value < 60;
                    }
                }
                catch { }
                bool bossPushing = ChallengeOverlay.Phase == "push" || ChallengeOverlay.Segment == "RECOVERY"
                    || ChallengeOverlay.Segment == "EVIL CLIMB";   // the Evil re-climb IS a boss push (user-caught: stats digger stayed off)
                // Stats earns priority whenever stats gate progress: an explicit boss-push segment, a
                // challenge (stats are suppressed), OR "floor-restricted" — still content-unlocking bosses
                // to beat (!ceiling0). This is the profile's "1+2 needed if held back by floor restrictions
                // or during challenges", read from the live boss ceiling rather than a static list.
                bool inChallenge = false;
                try { inChallenge = ChallengeDetector.Current() != null; } catch { }
                bool statsWanted = bossPushing || !ceiling0 || inChallenge;
                // Ask the owning planner, don't string-match another module's output. This used to be
                // Array.IndexOf(ChallengeOverlay.AutoTokens(Magic), "BR-30"), which was wrong three ways:
                // AutoTokens has no AutoProfile self-gate (allocation only uses it when AutoProfile is
                // on), so with a hand-written profile this keyed the digger off a token list nothing was
                // using; any other spelling (BR, BR-300, BR-30:20) silently failed the match and demoted
                // the blood digger for no reason; and it built the entire magic token list — NGUAdvisors
                // .Compute, ChapterNgusSurplus, BloodMatters — to read one bit, on the digger path.
                bool ritualsLive = false;
                try { ritualsLive = BloodPlanner.BloodMatters(); }
                catch { }

                // LAW: Stats digger earns priority while stats gate progress; else it drops to the tail.
                order.Remove(2);
                if (statsWanted) order.Insert(Math.Min(1, order.Count), 2);
                else order.Add(2);

                // LAW: Blood digger needs a live ritual caster.
                if (!ritualsLive) { order.Remove(10); order.Add(10); }

                // LAW: DC/PP by venue. Titan window: DC for the kill drops, PP has nothing to earn.
                // GEAR HUNT (user rule): a deliberate drop farm — DC in, PP benched, and it outranks
                // the ITOPOD read (Target ITOPOD may still be toggled while the hunt owns routing).
                // ITOPOD: PP earns, DC is dead (flat rolls). Zone farming: DC earns, PP idles along.
                // Each reorder is guarded on its OWN digger being present: in the fallback all 12 are, so
                // this is unchanged there; in Hybrid it lifts whichever the pool actually contains and
                // never demotes the venue earner just because its swap-partner isn't in the pool.
                if (titanWindow || hunting)
                {
                    if (order.Contains(0)) { order.Remove(0); order.Insert(Math.Min(1, order.Count), 0); }
                    if (order.Contains(0) && order.Contains(8)) { order.Remove(8); order.Add(8); }
                }
                else if (itopod)
                {
                    if (order.Contains(8)) { order.Remove(8); order.Insert(Math.Min(2, order.Count), 8); }
                    if (order.Contains(0) && order.Contains(8)) { order.Remove(0); order.Add(0); }
                }

                // LAW: the Adventure digger always leads.
                order.Remove(3);
                order.Insert(0, 3);

                // LAW: an active gear hunt outranks even Adventure — the hunt exists to farm drops.
                // At one digger slot the Adventure lead pushed the DC digger out of Take(slots)
                // entirely, so the hunt ran with no drop chance at all (user-caught).
                if (hunting && order.Contains(0)) { order.Remove(0); order.Insert(0, 0); }

                // Membership: unlocked, and (Hybrid) restricted to the profile pool — no law-introduced
                // filler leaks in. Leveling priority is the law-ranked order that survives the filter.
                var ranked = order.Where(IsDiggerUnlocked);
                if (poolFilter != null) ranked = ranked.Where(poolFilter.Contains);
                return ranked.Take(slots).ToArray();
            }
            catch { return null; }
        }

        // Locate a digger by the game's own bonus label (names live in runtime lists).
        private static int FindDiggerByBonus(string needle)
        {
            try
            {
                var names = Main.Character.allDiggers.bonusName;
                for (int i = 0; i < names.Count && i < 12; i++)
                    if (!string.IsNullOrEmpty(names[i]) && names[i].ToLowerInvariant().Contains(needle))
                        return i;
            }
            catch { }
            return -1;
        }

        public static int[] CurrentBeardSet()
        {
            try
            {
                var c = Main.Character;
                if (c == null) return null;
                string mode = Mode(ProgressionAnalyzer.Detect());
                // Same as diggers: AutoProfile has no challenge-tagged beard breakpoints, so drive beards in
                // challenges too (they cost nothing) rather than leaving the set to the profile's one-shot.
                if (mode == "challenge" && !Main.Settings.AutoProfile) return null;
                int slots = Math.Max(1, c.allBeards.capBeards());
                // Beards cost nothing — fill EVERY slot (user rule): mode heads lead, rest follow.
                var order = new List<int>(RecommendedBeards(mode));
                for (int i = 0; i <= Consts.MAX_BEARD_ID; i++)
                    if (!order.Contains(i)) order.Add(i);
                return order.Where(b => b != Consts.MAX_BEARD_ID || GoldenUnlocked()).Take(slots).ToArray();
            }
            catch { return null; }
        }

        // Goal/activity classification from the progression state.
        private static string Mode(ProgressionAnalyzer.Progression prog)
        {
            if (prog.Activity != null && prog.Activity.StartsWith("Challenge")) return "challenge";
            if (prog.NextGoal != null && prog.NextGoal.IndexOf("Titan", StringComparison.OrdinalIgnoreCase) >= 0) return "push";
            return "farm";
        }

        // 2 Stats, 3 Adv, 8 PP, 10 Blood, 1 Wandoos (push) ; 3 Adv, 8 PP, 0 Drops, 9 Daycare (farm).
        private static int[] RecommendedDiggers(string mode) =>
            mode == "push" ? new[] { 2, 3, 8, 10, 1 } : new[] { 3, 8, 0, 9, 2 };

        // 0 Stats, 5 Adv, 4 Wandoos (push) ; 5 Adv, 1 Drops, 3 NGU (farm).
        private static int[] RecommendedBeards(string mode) =>
            mode == "push" ? new[] { 0, 5, 4 } : new[] { 5, 1, 3 };

        private static bool IsDiggerUnlocked(int i)
        {
            try { return Main.Character.diggers.diggers[i].maxLevel > 0; }
            catch { return true; }
        }

        // Highest level whose drain fits in gps — the game's setLevelMaxAffordable math:
        // drain(L) = baseGPSDrain × growth^(L-1), affordable L = log(gps/base)/log(growth).
        private static long AffordableDiggerLevel(Character c, int id, double gps)
        {
            try
            {
                double baseDrain = c.allDiggers.baseGPSDrain[id];
                double growth = c.allDiggers.gpsGrowthRate[id];
                long maxLevel = c.diggers.diggers[id].maxLevel;
                if (baseDrain <= 0 || growth <= 1 || gps < baseDrain) return 0;
                long lvl = (long)(Math.Log(gps / baseDrain) / Math.Log(growth));
                if (lvl < 0) lvl = 0;
                return Math.Min(lvl, maxLevel);
            }
            catch { return 0; }
        }

        private static bool GoldenUnlocked()
        {
            try { return Main.Character.allChallenges.trollChallenge.completions() >= 7; }
            catch { return false; }
        }

        private static bool AtAutoByWish(Character c)
        {
            try { return c.wishes.wishes[190].level >= 1; }
            catch { return false; }
        }

        private static long AtLevel(Character c, int i)
        {
            try { return c.advancedTraining.level[i]; }
            catch { return 0; }
        }

        // Seeds are small human numbers early on — show them plain below a million.
        private static string FmtSeeds(long v) => v < 1000000 ? v.ToString("#,##0") : Fmt(v);

        private static string SafeCurrentChallenge()
        {
            try { return ChallengeDetector.Current(); } catch { return null; }
        }

        private static bool SafeAnyChallengesValid()
        {
            try { return BaseRebirth.AnyChallengesValid(); } catch { return false; }
        }

        // True if the player can't afford `factor` x the cheapest augment upgrade (augs are the gold
        // sink that drives Power/Toughness). factor > 1 gives callers hysteresis (e.g. the adventure
        // router farms gold until TWO upgrades are affordable, so it doesn't flap at the boundary).
        public static bool GoldStarvedForAugs(Character c, double factor = 1.0)
        {
            try
            {
                var ac = c.augmentsController;
                if (ac == null) return false;
                double gold = c.realGold;
                double minCost = double.MaxValue;
                for (int i = 0; i < 7; i++)
                {
                    double cost = ac.augments[i].getUpgradeCost();
                    if (cost > 0 && cost < minCost) minCost = cost;
                }
                return minCost != double.MaxValue && gold < minCost * factor;
            }
            catch { return false; }
        }

        // Digger gold demand (user rule): digger MAX-LEVEL upgrades are the gold sink that outlasts
        // augments — upgradeCost = baseGoldCost x growthRate^maxLevel (decomp AllGoldDiggerController),
        // so they need a far higher gold cap. Starved while the CHEAPEST next upgrade (over the money
        // pit reserve) is unaffordable; diggers already at their hard cap are excluded.
        public static bool GoldStarvedForDiggers(Character c, double factor = 1.0)
        {
            try
            {
                if (!c.buttons.diggers.interactable) return false;
                var dc = c.allDiggers;
                double gold = c.realGold;
                double reserve = 0;
                try { reserve = Main.Settings != null ? Main.Settings.MoneyPitThreshold : 0; } catch { }
                double minCost = double.MaxValue;
                for (int i = 0; i < c.diggers.diggers.Count && i < 12; i++)
                {
                    if (c.diggers.diggers[i].maxLevel >= dc.hardCapLevel(i)) continue;
                    double cost = dc.upgradeCost(i);
                    if (cost > 0 && cost < minCost) minCost = cost;
                }
                return minCost != double.MaxValue && gold < (minCost + reserve) * factor;
            }
            catch { return false; }
        }

        private static List<int> ToIntList(System.Collections.IEnumerable src)
        {
            var l = new List<int>();
            if (src == null) return l;
            foreach (var o in src) l.Add(Convert.ToInt32(o));
            return l;
        }

        private static string Name(string[] names, int i) => i >= 0 && i < names.Length ? names[i] : i.ToString();
        private static int Clamp(int i) => i < 0 ? 0 : (i >= WandoosOsNames.Length ? WandoosOsNames.Length - 1 : i);

        private static string Fmt(double v) => NumberFormatter.Abbrev(v);   // consolidated (finding #31)
    }
}
