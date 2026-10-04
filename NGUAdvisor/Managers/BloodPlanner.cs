using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // Blood Magic planner: replaces hand-picked spell thresholds with breakpoint math.
    //
    // Live inputs (all game reads): blood on hand (bloodMagic.bloodPoints), total blood/sec from the
    // active rituals at the current magic allocation (bloodMagicController.totalBloodGainedPerSecond),
    // Iron Pill cooldown state (bloodSpells.adventureSpellCooldown vs bloodMagic.adventureSpellTime),
    // and the run's remaining time (profile rebirth target via WandoosAdvisor.RunHorizonMinutes).
    //
    // Iron Pill effect = floor(blood^0.25) (game code; DRs-tab breakpoints: 3^4=81, 4^4=256, ...), so
    // casting just below a breakpoint wastes the whole step. The optimal cast is the LAST breakpoint
    // reachable this run: hold while the next breakpoint is reachable before rebirth, cast when it
    // isn't. Blood is lost on rebirth, so the existing force-cast-on-rebirth path stays as the net.
    public static class BloodPlanner
    {
        public struct Plan
        {
            public bool Known;
            public string Text;
            public int Severity;
            public bool Optimal;
            public bool CastIronNow;
            // Desired investment-spell routing (the game's auto-toggles split ALL blood evenly among
            // whichever are on, every second — which also starves the Iron Pill's pool):
            public bool RouteKnown;
            public bool PillWorthwhile;   // false = the pill can't move current adventure stats — never pool/cast
            public bool UnreachableThisRun;   // pill cooldown outlasts the scheduled rebirth — don't pool blood for it
            public bool PoolForPill;   // all autos off while charging the pill
            public bool WantRebirth;   // Blood NUMBER Boost: linear (rebirthPower += blood), no cap — default sink
            public bool WantLoot;      // Spaghetti: log2(invested/min)% drop chance
            public bool WantGold;      // Counterfeit Gold: log2(invested/min)^2 % GPS (needs TM base gold)
            public string RouteReason;
            public BudgetPlan Budget;   // how the run's blood splits between the spells, and why each share stops
            public double CdLeftSec, CdTotalSec;   // Iron Pill cooldown state — the panel bar charges toward ready
            public long PillPowerNow;              // what casting the current pool grants (game formula); 0 = below cast min
        }

        // Magic-cap growth sampler (user ask: time the pill against Magic Cap/Power growth, not just
        // current income). Ritual blood/sec scales with the magic feeding it, which scales with cap
        // as the run grows it — so future pooling is faster than bps-now suggests. EMA of the cap's
        // relative growth per second; statics reset on reload and the sampler re-seeds (growth reads
        // 0 until the first 60s window — conservative, never over-promises a second pill).
        private static double _capSample;
        private static DateTime _capSampleAt = DateTime.MinValue;
        private static double _capGrowthPerSec;

        private static double MagicGrowthPerSec(Character c)
        {
            try
            {
                double cap = c.totalCapMagic();
                var now = DateTime.UtcNow;
                if (_capSampleAt == DateTime.MinValue || _capSample <= 0)
                {
                    _capSample = cap;
                    _capSampleAt = now;
                }
                else
                {
                    double dt = (now - _capSampleAt).TotalSeconds;
                    if (dt >= 60)
                    {
                        double r = Math.Max(0, (cap / _capSample - 1.0) / dt);
                        _capGrowthPerSec = _capGrowthPerSec <= 0 ? r : _capGrowthPerSec * 0.7 + r * 0.3;
                        _capSample = cap;
                        _capSampleAt = now;
                    }
                }
            }
            catch { }
            return _capGrowthPerSec;
        }

        // Cheap cached check for allocation decisions (marathon ritual gating).
        private static bool _pillWorthCache = true;
        private static DateTime _pillWorthAt = DateTime.MinValue;

        // HARD pooling horizon (user rule): never treat the pill as a live blood consumer more than
        // 1 hour before it comes off cooldown. Rituals fed for a pill hours away are pure NGU-magic
        // loss — the pool builds in the final hour instead.
        private const double PillPoolingHorizonSec = 3600;

        public static bool PillWorthPursuing()
        {
            if ((DateTime.UtcNow - _pillWorthAt).TotalSeconds < 60) return _pillWorthCache;
            _pillWorthAt = DateTime.UtcNow;
            try
            {
                var p = Analyze();
                double cdLeft = 0;
                try
                {
                    var c = Main.Character;
                    cdLeft = Math.Max(0, c.bloodSpells.adventureSpellCooldown - c.bloodMagic.adventureSpellTime.totalseconds);
                }
                catch { }
                _pillWorthCache = p.Known && p.PillWorthwhile && !p.UnreachableThisRun
                    && cdLeft <= PillPoolingHorizonSec;
            }
            catch { _pillWorthCache = false; }
            return _pillWorthCache;
        }

        public static Plan Analyze()
        {
            var p = new Plan();
            try
            {
                var c = Main.Character;
                if (c == null) return p;
                var spells = c.bloodSpells;
                if (spells == null) return p;

                double blood = c.bloodMagic.bloodPoints;
                double bps = 0;
                try { bps = c.bloodMagicController.totalBloodGainedPerSecond(); } catch { }
                double runLeft = WandoosAdvisor.RunHorizonMinutes() * 60.0;
                // TRUE remaining time to the scheduled rebirth. RunHorizonMinutes clamps to >=10min for
                // the Wandoos projection, which is too optimistic for pill timing — use the real value
                // here (MaxValue when no rebirth is scheduled: the pill will eventually come off cooldown).
                double trueRunLeft = RunLeftSeconds(c);
                double cdLeft = Math.Max(0, spells.adventureSpellCooldown - c.bloodMagic.adventureSpellTime.totalseconds);
                double minBlood = spells.minAdventureBlood();

                p.Known = true;
                p.CdLeftSec = cdLeft;
                p.CdTotalSec = Math.Max(1.0, spells.adventureSpellCooldown);
                // Display-only cast value (decomp castAdventurePowerupSpell): floor(blood^0.25),
                // ×ironPillBonus on Evil+, capped 1e8. The planner's breakpoint math below keeps
                // using the raw root — the bonus is a flat multiplier that cancels out of timing.
                long powerNow = blood >= minBlood ? (long)Math.Floor(Math.Pow(blood, 0.25)) : 0;
                try
                {
                    if (powerNow > 0 && c.settings.rebirthDifficulty >= difficulty.evil)
                        powerNow = (long)(powerNow * c.adventureController.itopod.ironPillBonus());
                }
                catch { }
                p.PillPowerNow = Math.Min(powerNow, 100000000L);

                // Worth gate: the yardstick is the pre-multiplier summand the pill joins
                // (BloodMagicManager.PillYardstick), not totalAdvAttack, whose multipliers scale the
                // pill equally and cancel out. Evil+ pills are further multiplied by ironPillBonus.
                double advNow = 1;
                try { advNow = BloodMagicManager.PillYardstick(c); } catch { }
                double bestPossible = Math.Pow(Math.Max(blood, blood + bps * runLeft), 0.25);
                try
                {
                    if (c.settings.rebirthDifficulty >= difficulty.evil)
                        bestPossible *= c.adventureController.itopod.ironPillBonus();
                }
                catch { }
                bool pillWorth = bestPossible >= advNow * BloodMagicManager.PillWorthFraction;
                p.PillWorthwhile = pillWorth;
                if (!pillWorth)
                {
                    p.Text = $"Iron Pill skipped: +{bestPossible:0} vs {ExpBalancer.Fmt(advNow)} adv power summand (base + gear + cube) — no meaningful gain at this stage";
                    p.Optimal = true;
                    return p;
                }

                if (cdLeft > 0 && cdLeft >= trueRunLeft)
                {
                    p.UnreachableThisRun = true;
                    p.Text = $"Iron Pill on cooldown ({FmtH(cdLeft)}) — won't be ready before rebirth ({FmtH(trueRunLeft)} left); not pooling blood for it";
                    p.Optimal = true;
                    return p;
                }

                // Effect now and best achievable before the run ends. Audit fix: while any investment
                // auto-spell toggle is on, the game drains the pool every second — blood only actually
                // accumulates once the pooling window opens (15m before the pill is ready), so project
                // growth over that window, not the whole run. bps itself GROWS with magic cap over the
                // run — PoolOver projects pooled blood over [t0, t0+T] with the measured growth rate.
                long eNow = blood >= minBlood ? (long)Math.Floor(Math.Pow(blood, 0.25)) : 0;
                bool autosDraining = AdvisorOwnsBlood() || c.bloodMagic.rebirthAutoSpell || c.bloodMagic.lootAutoSpell || c.bloodMagic.goldAutoSpell;
                double capGrowth = MagicGrowthPerSec(c);
                double PoolOver(double t0, double T) => T <= 0 ? 0 : bps * T * (1.0 + capGrowth * (t0 + T / 2.0));

                double poolStart = autosDraining ? Math.Max(0, cdLeft - 900) : 0;
                double bloodAtEnd = blood + PoolOver(poolStart, Math.Max(0, runLeft - poolStart));
                long eEnd = bloodAtEnd >= minBlood ? (long)Math.Floor(Math.Pow(bloodAtEnd, 0.25)) : 0;

                if (cdLeft > 0)
                {
                    p.Text = cdLeft > PillPoolingHorizonSec
                        ? $"Iron Pill ready in {FmtH(cdLeft)} — too far out to pool for (magic feeds NGUs until the last hour)"
                        : $"Iron Pill ready in {FmtH(cdLeft)} — projected power {eEnd} by rebirth (+{ExpBalancer.Fmt(bps)}/s blood)";
                    p.Severity = 0;
                    return p;
                }

                // Pill is ready. Next breakpoint and whether it's reachable before rebirth.
                if (eNow < 1)
                {
                    p.Text = bps > 0
                        ? $"Iron Pill ready — accruing blood ({ExpBalancer.Fmt(blood)}, +{ExpBalancer.Fmt(bps)}/s)"
                        : "Iron Pill ready but NO blood income — allocate magic to rituals";
                    p.Severity = bps > 0 ? 0 : 1;
                    return p;
                }

                // Mirror the caster fail-safe (BloodMagicManager.IronPill.FailSafeHold): it holds for the
                // first 30 minutes the pill is available (pooling a stronger cast) and refuses any cast under
                // 10% of the adventure power summand. Don't advertise "CAST NOW" for a cast the caster will refuse;
                // keep pooling instead (PoolForPill stays set by FillRouting while cdLeft<900).
                double availableFor = c.bloodMagic.adventureSpellTime.totalseconds - spells.adventureSpellCooldown;
                if (availableFor < BloodMagicManager.PillMinAvailableSec
                    || p.PillPowerNow < advNow * BloodMagicManager.PillWorthFraction)
                {
                    p.Text = availableFor < BloodMagicManager.PillMinAvailableSec
                        ? $"Iron Pill ready — pooling {FmtH(Math.Max(0, BloodMagicManager.PillMinAvailableSec - availableFor))} more (fail-safe holds the first 30m for a stronger cast)"
                        : $"Iron Pill ready — holding: cast {p.PillPowerNow} < 10% of adv power summand {ExpBalancer.Fmt(advNow)}";
                    p.Severity = 0;
                    return p;
                }

                double nextBp = Math.Pow(eNow + 1, 4);
                double toNext = bps > 0 ? (nextBp - blood) / bps : double.MaxValue;

                // Two-plan comparison (user ask: "is NOW the best time to cast?"). Pills grant FLAT
                // base stats, so total gain is the SUM of casts: casting now frees the cooldown to
                // brew a SECOND pill from the growth-projected income; holding grows this one pill
                // toward eEnd. Recommend whichever total is higher.
                double cd = spells.adventureSpellCooldown;
                long pillSecond = 0;
                if (runLeft > cd)
                {
                    double t2 = autosDraining ? Math.Max(0, cd - 900) : 0;
                    double blood2 = PoolOver(t2, Math.Max(0, runLeft - t2));
                    if (blood2 >= minBlood) pillSecond = (long)Math.Floor(Math.Pow(blood2, 0.25));
                }

                if (pillSecond > 0 && eNow + pillSecond > eEnd)
                {
                    p.CastIronNow = true;
                    p.Text = $"Cast Iron Pill NOW at power {eNow} — a second pill (~{pillSecond}) brews by rebirth; {eNow}+{pillSecond} beats holding for {eEnd}";
                    p.Severity = 1;
                }
                else if (toNext > runLeft - 60)
                {
                    p.CastIronNow = true;
                    p.Text = $"Cast Iron Pill NOW at power {eNow} — next breakpoint ({eNow + 1}) is out of reach this run";
                    p.Severity = 1;
                }
                else
                {
                    // Cast on cooldown: the pill is a FLAT permanent add on a ^0.25 curve, so N frequent
                    // casts sum to ~(T/CD)^0.75 MORE than one bigger pooled cast — holding for a higher
                    // single breakpoint is never worth it and it starves the investment sinks meanwhile.
                    p.CastIronNow = true;
                    p.Text = $"Cast Iron Pill NOW at power {eNow} — frequent casts beat holding (flat ^0.25 stat sums)";
                    p.Severity = 1;
                }
                return p;
            }
            catch (Exception e) { Main.LogDebug($"BloodPlanner: {e.Message}"); return p; }
        }

        // Decide the blood budget (BloodRouter.Plan): the run's blood is split equally between the enabled
        // spells, Counterfeit/Spaghetti snapped to whole +1 % steps. Spend() then casts it. While the Iron
        // Pill is charging nothing is cast, so the pill gets the pool.
        public static void FillRouting(ref Plan p)
        {
            try
            {
                Character c = Main.Character;
                if (c == null || c.bossID <= 36) return;   // game gates auto-spells until boss 37

                p.RouteKnown = true;
                RebirthOutlook outlook = Outlook();
                p.Budget = BloodRouter.Plan(BudgetInputs(c, outlook));
                p.WantGold = p.WantLoot = p.WantRebirth = false;

                double cdLeft = Math.Max(0, c.bloodSpells.adventureSpellCooldown - c.bloodMagic.adventureSpellTime.totalseconds);
                double trueRunLeft = RunLeftSeconds(c);
                // Pool ONLY for a pill that can move the needle AND can be cast before rebirth (user-
                // reported: it pooled magic into blood for a pill whose cooldown outlasted the run).
                // The gate mirrors ApplyBlood's own (AdvisorBlood && CastBloodSpells): with
                // CastBloodSpells on but AdvisorBlood off nothing casts on planner timing, so pooling
                // would strand the blood until the rebirth force-cast.
                if (AdvisorOwnsBlood() && p.PillWorthwhile
                    && !p.UnreachableThisRun && cdLeft < Math.Min(trueRunLeft, 900))
                {
                    p.PoolForPill = true;
                    p.RouteReason = "pooling for Iron Pill";
                    return;
                }

                double rebirthPower = Math.Max(1.0, c.bloodMagic.rebirthPower);
                BudgetPlan b = p.Budget;
                switch (b.Route)
                {
                    case BloodRoute.Gold:
                        p.WantGold = true;
                        p.RouteReason = $"Counterfeit gold +{b.Gold.NowPct + 1}% next · pooling {ExpBalancer.Fmt(Math.Min(c.bloodMagic.bloodPoints, b.Gold.NextStepCost))} / {ExpBalancer.Fmt(b.Gold.NextStepCost)}";
                        break;
                    case BloodRoute.Loot:
                        p.WantLoot = true;
                        p.RouteReason = $"Spaghetti +{b.Loot.NowPct + 1}% next · pooling {ExpBalancer.Fmt(Math.Min(c.bloodMagic.bloodPoints, b.Loot.NextStepCost))} / {ExpBalancer.Fmt(b.Loot.NextStepCost)}";
                        break;
                    case BloodRoute.Number:
                        p.WantRebirth = true;
                        p.RouteReason = $"NUMBER (gold/loot at their share · x{ExpBalancer.Fmt(rebirthPower)})";
                        break;
                    default:
                        // Nothing to bank: keep every auto-spell OFF so blood magic doesn't drain the
                        // marathon's magic cap (BR-30 gates on a live sink).
                        p.RouteReason = IdleReason(outlook);
                        break;
                }
            }
            catch (Exception e) { Main.LogDebug($"BloodPlanner routing: {e.Message}"); }
        }

        // Cast the plan with exact amounts (RebirthPowerSpell.castGoldSpell/castLootSpell/castRebirthSpell
        // take one) instead of the auto-spell toggles, which cast the WHOLE pool every second and so could
        // only ever pour blood into a step that is not yet paid for. A step is cast only once the pool
        // covers it; NUMBER takes the pool once both in-run spells hold their share (it is time-
        // indifferent, and the rebirth force-cast banks whatever is still pooled). Main thread only.
        // Returns what was cast, for the log; null when nothing was.
        public static string Spend(Plan p)
        {
            if (!p.RouteKnown || p.PoolForPill) return null;
            Character c = Main.Character;
            List<string> cast = new List<string>();
            for (int guard = 0; guard < 64; guard++)
            {
                BudgetPlan b = p.Budget;
                double pool = c.bloodMagic.bloodPoints;
                if (b.Route == BloodRoute.Gold || b.Route == BloodRoute.Loot)
                {
                    bool gold = b.Route == BloodRoute.Gold;
                    SinkPlan s = gold ? b.Gold : b.Loot;
                    if (!(s.NextStepCost > 0) || s.NextStepCost > pool) break;
                    if (gold) c.bloodSpells.castGoldSpell(s.NextStepCost);
                    else c.bloodSpells.castLootSpell(s.NextStepCost);
                    cast.Add($"{(gold ? "Counterfeit" : "Spaghetti")} +{s.NowPct + 1}% for {ExpBalancer.Fmt(s.NextStepCost)}");
                }
                else if (b.Route == BloodRoute.Number)
                {
                    if (!(pool > 0)) break;
                    c.bloodSpells.castRebirthSpell(pool);
                    cast.Add($"NUMBER +{ExpBalancer.Fmt(pool)}");
                    break;
                }
                else break;
                p = Analyze();
                FillRouting(ref p);
                if (!p.RouteKnown || p.PoolForPill) break;
            }
            return cast.Count > 0 ? string.Join(", ", cast.ToArray()) : null;
        }

        // Is a rebirth going to cash the NUMBER bank — a different question from WHEN it happens. With
        // Auto Rebirth off the player rebirths by hand, which cashes it just the same.
        public enum RebirthOutlook { Coming, NoRebirthChallenge, NothingArmed }

        private static RebirthOutlook Outlook()
        {
            bool norb = false;
            try { norb = ChallengeDetector.Current() == "NORB"; } catch { }
            if (norb) return RebirthOutlook.NoRebirthChallenge;
            SavedSettings s = Main.Settings;
            if (s == null || !s.AutoRebirth) return RebirthOutlook.Coming;
            bool armed = (Main.Profile != null && Main.Profile.RebirthArmed()) || s.MoneyPitRunMode;
            return armed ? RebirthOutlook.Coming : RebirthOutlook.NothingArmed;
        }

        private static string IdleReason(RebirthOutlook o)
        {
            switch (o)
            {
                case RebirthOutlook.NoRebirthChallenge: return "blood idle — NORB: no rebirth to cash a NUMBER multi into";
                default: return "blood idle — the profile arms no rebirth to bank NUMBER for";
            }
        }

        // Horizon when the rebirth has no clock (a Number/Bosses trigger, money-pit runs, or none at
        // all): project one hour of income, the same default BestAug prices augments over.
        private const double UnclockedHorizonSec = 3600;

        private static BudgetInput BudgetInputs(Character c, RebirthOutlook outlook)
        {
            double elapsed = c.rebirthTime.totalseconds;
            double target = Main.Profile != null ? Main.Profile.NextRebirthTargetSeconds() : -1;
            bool clocked = target > 0 && outlook == RebirthOutlook.Coming;
            double horizon = clocked ? Math.Max(0, target - elapsed) : UnclockedHorizonSec;

            double bps = 0;
            try { bps = c.bloodMagicController.totalBloodGainedPerSecond(); } catch { }
            return new BudgetInput
            {
                BloodOnHand = c.bloodMagic.bloodPoints,
                Bps = bps,
                CapGrowthPerSec = MagicGrowthPerSec(c),
                HorizonSec = horizon,
                NumberEligible = outlook == RebirthOutlook.Coming,
                RebirthPower = Math.Max(1.0, c.bloodMagic.rebirthPower),
                Gold = new SinkInput
                {
                    Mode = Mode(true),
                    Invested = c.bloodMagic.goldSpellBlood,
                    MinBlood = c.bloodSpells.minGoldBlood()
                },
                Loot = new SinkInput
                {
                    Mode = Mode(false),
                    Invested = c.bloodMagic.lootSpellBlood,
                    MinBlood = c.bloodSpells.minLootBlood()
                }
            };
        }

        // TRUE seconds to the scheduled rebirth; MaxValue when none is scheduled.
        private static double RunLeftSeconds(Character c)
        {
            try
            {
                double tgt = Main.Profile != null ? Main.Profile.NextRebirthTargetSeconds() : -1;
                if (tgt > 0) return Math.Max(0, tgt - c.rebirthTime.totalseconds);
            }
            catch { }
            return double.MaxValue;
        }

        // Mirrors ApplyBlood's gate (AdvisorApply: AdvisorBlood, then CastBloodSpells). Both must be on
        // for the advisor to own blood routing and pill timing.
        private static bool AdvisorOwnsBlood()
        {
            try
            {
                var s = Main.Settings;
                return s != null && s.AdvisorBlood && s.CastBloodSpells;
            }
            catch { return false; }
        }

        // Does blood have a live consumer worth spending ritual magic on? The auto profile asks this
        // before funding BR-30 (ChallengeOverlay).
        //
        // It must answer with the routing INTENT, not with the toggles ApplyBlood last wrote. The old
        // code read the live flags, which meant the profile could only fund rituals AFTER a sink was
        // already live — and ApplyBlood is throttled to 60s, so the flags lag by up to a tick. Combined
        // with NUMBER being gated behind a threshold that defaults to 0, that was a deadlock: no
        // threshold -> no live toggle -> no rituals -> no blood -> NUMBER stuck at 1.0 forever, which
        // bit hardest late-game once the Iron Pill stopped being worth pursuing and was no longer
        // holding rituals open on its own.
        //
        // When the advisor does NOT own blood, the live toggles ARE the intent (set by the user or by
        // AutoSpellSwap in Main), so read them.
        private static bool _bloodMattersCache = true;
        private static DateTime _bloodMattersAt = DateTime.MinValue;

        public static bool BloodMatters()
        {
            if ((DateTime.UtcNow - _bloodMattersAt).TotalSeconds < 10) return _bloodMattersCache;
            _bloodMattersAt = DateTime.UtcNow;
            try
            {
                var c = Main.Character;
                if (c == null) return _bloodMattersCache = true;
                if (!AdvisorOwnsBlood())
                {
                    var bm = c.bloodMagic;
                    return _bloodMattersCache = bm.rebirthAutoSpell || bm.lootAutoSpell || bm.goldAutoSpell;
                }
                var p = Analyze();
                FillRouting(ref p);
                _bloodMattersCache = (p.RouteKnown && (p.PoolForPill || p.WantRebirth || p.WantLoot || p.WantGold))
                    || PillWorthPursuing();
            }
            catch { _bloodMattersCache = true; }   // fail-safe: keep rituals if the state read throws
            return _bloodMattersCache;
        }

        // The game's own bonus reads — the same values Main's manual AutoSpellSwap path uses, so a
        // ceiling means the same thing in both modes.
        public static int CounterfeitPercentNow(Character c)
        {
            try { return (int)Math.Round((c.bloodMagicController.goldBonus() - 1) * 100); }
            catch { return 0; }
        }

        public static int SpaghettiPercentNow(Character c)
        {
            try { return (int)Math.Round((c.bloodMagicController.lootBonus() - 1) * 100); }
            catch { return 0; }
        }

        public static SinkMode Mode(bool gold)
        {
            var s = Main.Settings;
            if (s == null) return SinkMode.On;
            return (gold ? s.BloodWantCounterfeit : s.BloodWantSpaghetti) ? SinkMode.On : SinkMode.Off;
        }

        // Compact investment status for the expanded detail row: each spell's bonus now against its plan.
        public static string InvestmentDetail(in Plan p)
        {
            try
            {
                if (!p.RouteKnown) return null;
                BudgetPlan b = p.Budget;
                List<string> parts = new List<string>();
                if (b.Gold.Mode != SinkMode.Off)
                    parts.Add($"Gold {ExpBalancer.Fmt(b.Gold.Invested)} blood (+{b.Gold.NowPct}% GPS)");
                if (b.Loot.Mode != SinkMode.Off)
                    parts.Add($"Spaghetti {ExpBalancer.Fmt(b.Loot.Invested)} blood (+{b.Loot.NowPct}% DC)");
                parts.Add($"NUMBER {ExpBalancer.Fmt(b.NumberInvested)} blood · share {ExpBalancer.Fmt(b.Share)} each");
                return string.Join(" · ", parts.ToArray());
            }
            catch { return null; }
        }

        private static string FmtH(double seconds)
        {
            if (seconds < 90) return $"{seconds:0}s";
            if (seconds < 3600) return $"{seconds / 60:0}m";
            return $"{seconds / 3600:0.#}h";
        }
    }
}
