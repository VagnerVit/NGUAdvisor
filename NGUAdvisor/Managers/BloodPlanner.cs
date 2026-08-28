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

                // Worth gate (user rule): the pill grants FLAT blood^0.25 to the BASE Adventure
                // Power/Toughness (game: character.adventure.attack/defense += num — the summand
                // gear multipliers then scale). So the yardstick is the BASE stat, not
                // totalAdvAttack: measuring against the gear-inflated total made the pill look
                // worthless long after it stopped being so (user-caught). Evil+ pills are further
                // multiplied by the ITOPOD ironPillBonus.
                double advNow = 1;
                try { advNow = Math.Max(1, c.adventure.attack); } catch { }
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
                    p.Text = $"Iron Pill skipped: +{bestPossible:0} vs {ExpBalancer.Fmt(advNow)} BASE adv power — no meaningful gain at this stage";
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
                bool autosDraining = c.bloodMagic.rebirthAutoSpell || c.bloodMagic.lootAutoSpell || c.bloodMagic.goldAutoSpell;
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
                // 10% of base adventure power. Don't advertise "CAST NOW" for a cast the caster will refuse;
                // keep pooling instead (PoolForPill stays set by FillRouting while cdLeft<900).
                double availableFor = c.bloodMagic.adventureSpellTime.totalseconds - spells.adventureSpellCooldown;
                if (availableFor < BloodMagicManager.PillMinAvailableSec
                    || p.PillPowerNow < advNow * BloodMagicManager.PillWorthFraction)
                {
                    p.Text = availableFor < BloodMagicManager.PillMinAvailableSec
                        ? $"Iron Pill ready — pooling {FmtH(Math.Max(0, BloodMagicManager.PillMinAvailableSec - availableFor))} more (fail-safe holds the first 30m for a stronger cast)"
                        : $"Iron Pill ready — holding: cast {p.PillPowerNow} < 10% of base adv power {ExpBalancer.Fmt(advNow)}";
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

        // Decide the investment-spell routing. The game's autoSpell() splits blood evenly among the
        // enabled toggles each second, so "setting the spells properly" means choosing WHICH are on:
        //  - While the Iron Pill is charging (ready or ready soon), everything is OFF to pool.
        //  - Counterfeit Gold only helps if the Time Machine has base gold to multiply, and matters
        //    most while gold-starved for augments.
        //  - Spaghetti (drop chance) while the farm zone's recommended DC isn't met.
        //  - NUMBER boost is the default sink — dead only in NORB (no rebirth = the banked multi is
        //    never cashed) and when no rebirth is scheduled.
        public static void FillRouting(ref Plan p)
        {
            try
            {
                var c = Main.Character;
                if (c == null || c.bossID <= 36) return;   // game gates auto-spells until boss 37

                p.RouteKnown = true;

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

                // SINGLE-SINK routing (user pick): the game splits blood EVENLY among enabled toggles,
                // so enabling several DILUTES them — pick exactly one.
                //
                // The three sinks are NOT commensurable (decomp):
                //   NUMBER  rebirthPower += blood  — LINEAR and uncapped (RebirthPowerSpell), and a
                //           straight multiplier on the WHOLE next-run attack/defense multi
                //           (Rebirth.calculateNextMultis), re-based to 1.0 every rebirth.
                //   Gold    1 + floor((log2(b/min)+1)^2)/100   — LOG.
                //   Loot    +1% drop chance per DOUBLING of b  — LOG.
                // All three are wiped by bloodMagicController.reset() at rebirth — an earlier comment
                // here claimed they persist; they do not. Only NUMBER leaves anything behind, as the
                // multi banked a moment earlier by setNewMultis().
                //
                // So gold/loot buy IN-RUN throughput whose value decays with the time left to earn it
                // back, while NUMBER banks the same blood at a linear rate whenever it is spent. Order:
                // NUMBER floor > gold > loot (while the investment window is open) > NUMBER.
                bool norb = false;
                try { norb = ChallengeDetector.Current() == "NORB"; } catch { }
                bool rebirthOn = Main.Profile != null && Main.Profile.NextRebirthTargetSeconds() > 0;
                double bps = 0;
                try { bps = c.bloodMagicController.totalBloodGainedPerSecond(); } catch { }
                double rebirthPower = 1;
                try { rebirthPower = c.bloodMagic.rebirthPower; } catch { }
                double numTarget = Main.Settings != null ? Main.Settings.BloodNumberThreshold : 0;

                p.WantGold = p.WantLoot = p.WantRebirth = false;

                bool numberEligible = !norb && rebirthOn;

                // The "Number ≥" knob is a FLOOR, not a ceiling: while under it NUMBER outranks the
                // in-run sinks. The old code stopped routing at the target, which capped a linear
                // uncapped multiplier at a hand-typed number — and because the auto profile funds
                // rituals only while a sink is live (ChallengeOverlay), stopping also cut blood income
                // for the rest of the run.
                if (numberEligible && numTarget > 0 && rebirthPower < numTarget)
                {
                    p.WantRebirth = true;
                    p.RouteReason = $"NUMBER (below floor {ExpBalancer.Fmt(numTarget)} · now x{ExpBalancer.Fmt(rebirthPower)})";
                    return;
                }

                // With NUMBER off the table (NORB / no rebirth scheduled) there is nothing to bank, so
                // the in-run sinks are the only use for blood and the window never closes.
                bool windowOpen = !numberEligible || InvestmentWindowOpen(c);

                // Gold demand = augs OR diggers (user rule): once augs are funded the digger max-level
                // upgrades are the next gold sink, and they need a far HIGHER gold cap — Counterfeit
                // stays credited until those are funded too.
                bool goldDemand = OptimizationAdvisor.GoldStarvedForAugs(c, 2.0)
                    || OptimizationAdvisor.GoldStarvedForDiggers(c);
                if (windowOpen && SinkWanted(true) && TargetOpen(GoldTarget(), CounterfeitPercentNow(c))
                    && c.machine.realBaseGold > 0 && goldDemand && GoldBelowKnee(c, bps, out var goldReason))
                {
                    p.WantGold = true;
                    p.RouteReason = goldReason + (OptimizationAdvisor.GoldStarvedForAugs(c, 2.0) ? " · augs unfunded" : " · digger upgrades unfunded")
                        + TargetSuffix(GoldTarget());
                }
                else if (windowOpen && SinkWanted(false) && TargetOpen(LootTarget(), SpaghettiPercentNow(c))
                    && DcBelowZoneRec(c, out var dcReason))
                {
                    p.WantLoot = true;
                    p.RouteReason = dcReason + TargetSuffix(LootTarget());
                }
                else if (numberEligible)
                {
                    // DEFAULT SINK. Any blood still pooled at rebirth is force-cast here anyway
                    // (BaseRebirth.CastBloodSpellsForRebirth), so routing it now costs nothing and
                    // starts compounding immediately.
                    p.WantRebirth = true;
                    bool ceiling = false;
                    try { ceiling = c.bossID - 1 >= OptimizationAdvisor.BossUnlockCeiling(); } catch { }
                    string why = windowOpen ? "default sink" : "banking the run's tail";
                    p.RouteReason = ceiling
                        ? $"NUMBER (boss EXP push · {why} · now x{ExpBalancer.Fmt(rebirthPower)})"
                        : $"NUMBER ({why} · now x{ExpBalancer.Fmt(rebirthPower)})";
                }
                else
                {
                    // Genuinely nothing to bank: keep every auto-spell OFF so blood magic doesn't drain
                    // the marathon's magic cap (BR-30 gates on a live sink).
                    p.RouteReason = norb
                        ? "blood idle — NORB: no rebirth to cash a NUMBER multi into"
                        : "blood idle — no rebirth scheduled to bank NUMBER for";
                }
            }
            catch (Exception e) { Main.LogDebug($"BloodPlanner routing: {e.Message}"); }
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

        // Gold/loot only outrank NUMBER while enough of the run remains for their LOG-scaled, in-run
        // bonus to earn back the blood it costs — both are wiped at rebirth and must be re-earned, so
        // their value decays to nothing as the deadline approaches. NUMBER is time-indifferent, so it
        // always owns the tail of the run.
        private const double InvestmentWindowFraction = 0.5;

        private static bool InvestmentWindowOpen(Character c)
        {
            try
            {
                double tgt = Main.Profile != null ? Main.Profile.NextRebirthTargetSeconds() : -1;
                if (tgt <= 0) return true;
                return Math.Max(0, tgt - c.rebirthTime.totalseconds) > tgt * InvestmentWindowFraction;
            }
            catch { return true; }
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

        // USER TARGETS (Systems > BLOOD inputs). Neither log sink is capped by the game, so without a
        // ceiling Counterfeit/Spaghetti would hold the pool for the rest of the run once they win the
        // routing. The checkbox is a permission and the number is a ceiling; inside what they allow the
        // advisor's own gates (investment window, gold demand, the knee, zone DC) still decide. These
        // read the same bonuses as the MANUAL AutoSpellSwap path (Main.cs), so a target means the same
        // thing in both modes. 0 = no ceiling, matching BloodNumberThreshold's "0 = no floor".
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

        public static bool TargetOpen(int target, int now) => target <= 0 || now < target;

        private static bool SinkWanted(bool gold)
        {
            var s = Main.Settings;
            if (s == null) return true;
            return gold ? s.BloodWantCounterfeit : s.BloodWantSpaghetti;
        }

        private static int GoldTarget() => Main.Settings != null ? Main.Settings.CounterfeitThreshold : 0;
        private static int LootTarget() => Main.Settings != null ? Main.Settings.SpaghettiThreshold : 0;

        private static string TargetSuffix(int target) => target > 0 ? $" · to {target}%" : "";

        // Counterfeit gold cost-curve knee. Game: +N% GPS needs goldSpellBlood = minGold x 2^(sqrt(N)-1).
        // There is NO game cap (goldBonus = 1 + floor((log2(blood/min)+1)^2)/100 — user-corrected; the
        // old "<100%" cutoff here discredited Counterfeit far too early). The only knee is the cost
        // curve itself: eligible while the next +1% is reachable within ~20min of the FULL blood
        // income (single-sink → no sharing). Past that the step is too slow to be worth the pool.
        private static bool GoldBelowKnee(Character c, double bps, out string reason)
        {
            reason = null;
            try
            {
                double gb = Math.Max(0, c.bloodMagic.goldSpellBlood);
                double gm = c.bloodSpells.minGoldBlood();
                if (gm <= 0) return false;
                double cur = gb >= gm ? Math.Floor(Math.Pow(Math.Log(gb / gm, 2.0) + 1.0, 2.0)) : 0;
                double nextInvest = gm * Math.Pow(2.0, Math.Sqrt(cur + 1.0) - 1.0);
                double eta = bps > 0 ? (nextInvest - gb) / bps : double.MaxValue;
                if (eta > 20 * 60) return false;
                reason = $"Counterfeit gold +{cur:0}% GPS (next +1% in ~{Math.Max(1, eta / 60):0}m)";
                return true;
            }
            catch { return false; }
        }

        // Spaghetti drop chance: worth it only while zone-farming a zone whose recommended drop chance
        // isn't met yet. Cost DOUBLES per +1%, so there's no reason to push past the zone target.
        private static bool DcBelowZoneRec(Character c, out string reason)
        {
            reason = null;
            try
            {
                if (Main.Settings == null || !Main.Settings.GoldCBlockMode) return false;
                int zone = ZoneStatHelper.GetBestZone()?.Zone ?? -1;
                if (zone < 0 || !ZoneStatHelper.RecommendedDcPercent.TryGetValue(zone, out var rec)) return false;
                double cur = c.lootFactor() * 100.0;
                if (cur >= rec) return false;
                reason = $"Spaghetti DC — {cur:0}% < zone rec {rec:0}%";
                return true;
            }
            catch { return false; }
        }

        // Compact investment status for the expanded detail row.
        public static string InvestmentDetail()
        {
            try
            {
                var c = Main.Character;
                var s = c.bloodSpells;
                var parts = new List<string>();
                double lb = c.bloodMagic.lootSpellBlood, lm = s.minLootBlood();
                if (lb >= lm && lm > 0)
                {
                    int cur = (int)Math.Floor(Math.Log(lb / lm, 2.0));
                    parts.Add($"Spaghetti +{cur}% DC (next at {ExpBalancer.Fmt(lm * Math.Pow(2, cur + 1))})");
                }
                double gb = c.bloodMagic.goldSpellBlood, gm = s.minGoldBlood();
                if (gb >= gm && gm > 0)
                {
                    // Exact game formula: floor((log2(invested/min)+1)^2).
                    double cur = Math.Floor(Math.Pow(Math.Log(gb / gm, 2.0) + 1.0, 2.0));
                    double nextAt = gm * Math.Pow(2.0, Math.Sqrt(cur + 1.0) - 1.0);
                    parts.Add($"Gold +{cur:0}% GPS (next at {ExpBalancer.Fmt(nextAt)})");
                }
                if (c.bloodMagic.rebirthPower > 1)
                    parts.Add($"NUMBER x{ExpBalancer.Fmt(c.bloodMagic.rebirthPower)}");
                return parts.Count > 0 ? string.Join(" · ", parts.ToArray()) : null;
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
