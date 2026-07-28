using System;
using System.Collections.Generic;
using UnityEngine;
using static NGUAdvisor.Main;
using Random = UnityEngine.Random;

namespace NGUAdvisor.Managers
{
    public static class MoneyPitManager
    {
        public enum Outcomes
        {
            None,
            IronPill,
            Worn,
            Exp,
            Pomegranate,
            Daycare
        }

        private static readonly Character _character = Main.Character;

        public static readonly List<double> moneyPitThresholds = new List<double>
            { 1e5, 1e7, 1e9, 1e11, 1e13, 1e15, 1e18, 1e21, 1e24, 1e27, 1e30, 1e50, 1e55, 1e60, 1e65, 1e70 };

        public static float TimeUntilReady() => Mathf.Max(0f, _character.pitController.currentPitTime() - (float)_character.pit.pitTime.totalseconds);

        public static bool MoneyPitReady() => TimeUntilReady() <= 0f;

        public static double? ShockwaveTier()
        {
            if (!Settings.MoneyPitRunMode)
                return null;

            Outcomes outcome = PredictMoneyPit(1e50);
            if (outcome == Outcomes.Worn || outcome == Outcomes.Daycare)
                return 1e50;

            outcome = PredictMoneyPit(1e18);
            if (outcome == Outcomes.Worn || outcome == Outcomes.Daycare)
                return 1e18;
            if (PredictMoneyPit(1e15) == Outcomes.Worn)
                return 1e15;
            if (PredictMoneyPit(1e13) == Outcomes.Worn)
                return 1e13;

            return null;
        }

        public static bool NeedsLowerTier()
        {
            if (!Settings.MoneyPitRunMode)
                return false;

            if (!MoneyPitReady())
                return false;

            var tier = ShockwaveTier();
            double gold = _character.realGold;
            switch (tier)
            {
                case 1e18 when gold >= 1e50:
                case 1e15 when gold >= 1e18:
                case 1e13 when gold >= 1e15:
                    return true;
            }

            return false;
        }

        public static bool NeedsGold()
        {
            if (!Settings.MoneyPitRunMode)
                return false;

            if (NeedsRebirth())
                return false;

            if (_character.machine.realBaseGold > 0.0)
                return false;

            var tier = ShockwaveTier();
            double gold = _character.realGold;
            var needGold = gold < tier;
            // Cadence pulse for the two lowest "Worn" gear-farm tiers: besides needing gold when below the
            // throw tier, also re-flag "need gold" once per accumulation window so the run keeps topping up a
            // reserve instead of coasting. At tier 1e15 the window is 8e16 wide with a 1e15 (1/80th) low band;
            // at tier 1e13 it is 4e14 wide with a 1e13 (1/40th) low band. These window/band constants are
            // hand-tuned for the Worn run (origin undocumented — do not retune without validating the run live).
            // Precision note: `gold % <window>` only stays exact while gold is small enough to keep sub-window
            // resolution in a double (ULP exceeds 8e16 around gold ~1e33), but these branches only run at the
            // 1e15/1e13 Worn tiers, which in practice coincide with modest gold, so the pulse stays meaningful.
            if (tier == 1e15)
                needGold |= gold % 8e16 < 1e15;
            else if (tier == 1e13)
                needGold |= gold % 4e14 < 1e13;
            return needGold;
        }

        public static bool NeedsRebirth()
        {
            if (!Settings.MoneyPitRunMode)
                return false;

            if (_character.machine.realBaseGold <= 0.0)
                return false;

            return NeedsLowerTier();
        }

        // ONE ENGINE, TWO SETS OF TERMS. The minimum gold to throw at and whether to read the pit RNG first
        // are EXECUTION INPUTS now, not settings reads — the standard caller supplies the user's saved pair,
        // the advisor supplies its own. They used to be the same global, which is why AdvisorThrow had to
        // overwrite the user's configuration to get a different execution; see AdvisorThrow for what that cost.
        //
        // Everything else the throw consults — MoneyPitRunMode, Shockwave, YggdrasilLoadout, ManageMagic —
        // is genuinely configuration and is still read from Settings, by both callers alike.
        public static void CheckMoneyPit() => CheckMoneyPit(Settings.MoneyPitThreshold, Settings.PredictMoneyPit);

        // threshold: the minimum gold THIS execution will throw at. 1e5 is the game's own floor, not "off".
        // predict:   read the pit RNG and equip the predicted outcome's prep loadout before throwing.
        private static void CheckMoneyPit(double threshold, bool predict)
        {
            if (!MoneyPitReady())
                return;

            var predictionEnabled = predict || Settings.MoneyPitRunMode;
            if (!predictionEnabled && _character.realGold < threshold)
                return;

            double gold = _character.realGold;
            if (gold < 1e5)
                return;

            if (Settings.MoneyPitRunMode)
            {
                if (NeedsRebirth() && _character.rebirthTime.totalseconds < 300.0)
                    return;
                if (NeedsGold())
                    return;
            }

            // THE LOCK LEAVES THIS METHOD OR IT DOES NOT LEAVE AT ALL (stage R3).
            //
            // Every acquisition of the MoneyPit lock happens below, and this method is the only scope that
            // ever learns one succeeded — TryMoneyPitSwap acquires and forgets, DoMoneyPit never sees it,
            // ApplyPit never hears about it. So cleanup is owned here, and it is owned by a finally.
            //
            // The try opens BEFORE the acquisition calls, not after, and that is the whole point.
            // TryMoneyPitSwap sets the lock and THEN swaps gear, beards and diggers, so a throw inside the
            // prep leaves the lock held before the call has even returned true. Wrapping only the region
            // after a successful `true` would leave the most dangerous window unprotected.
            //
            // What a stuck MoneyPit lock costs, because it is not "the pit stops working": CanSwap() goes
            // false, and with it go titan/yggdrasil/gold/quest/cooking swaps, the profile's digger, beard
            // and gear timelines, the advisor's diggers/beards/Wandoos/gear refresh — and RebirthAvailable()
            // (BaseRebirth:157), so the RUN CANNOT END. Worst case is a throw at or after DoMoneyPit: the pit
            // is spent, MoneyPitReady() is false for the whole cooldown, every later CheckMoneyPit returns at
            // the first line, and nothing ever reaches a release. Hours, silent, only a reload clears it.
            try
            {
                if (predictionEnabled)
                {
                    switch (PredictMoneyPit())
                    {
                        case Outcomes.IronPill:
                            if (gold < threshold)
                                return;

                            if (!LockManager.TryMoneyPitSwap(null, new int[] { 10 }))
                                return;

                            if (Settings.ManageMagic)
                            {
                                _character.removeMostMagic();
                                _character.bloodMagicController.capAllRituals();
                            }

                            break;
                        case Outcomes.Worn:
                            LoadoutManager.SaveDaycare();
                            if (!LockManager.TryMoneyPitSwap(Settings.Shockwave, null, true))
                                return;

                            break;
                        case Outcomes.Exp:
                            if (gold < threshold)
                                return;

                            if (!LockManager.TryMoneyPitSwap(null, new int[] { 11 }))
                                return;

                            break;
                        case Outcomes.Pomegranate:
                            if (gold < threshold)
                                return;

                            if (!LockManager.TryMoneyPitSwap(Settings.YggdrasilLoadout))
                                return;

                            break;
                        case Outcomes.Daycare:
                            LoadoutManager.SaveDaycare();

                            if (!LockManager.TryMoneyPitSwap())
                                return;

                            LoadoutManager.FillDaycare();

                            break;
                        default:
                            if (gold >= threshold)
                                DoMoneyPit();

                            return;
                    }
                }
                else
                {
                    if (gold < threshold)
                        return;

                    if (gold >= 1e50 && _character.wishes.wishes[4].level > 0)
                    {
                        if (!LockManager.TryMoneyPitSwap(Settings.Shockwave, new[] { 11, 10 }))
                            return;

                        if (Settings.ManageMagic)
                        {
                            _character.removeMostMagic();
                            _character.bloodMagicController.capAllRituals();
                        }
                    }
                }

                DoMoneyPit();

                // Stays INSIDE the try, and ahead of the release: it can throw, and if it does the lock must
                // still come back. The old code ran it and then released on the same straight line, so a
                // daycare fault took the release down with it.
                LoadoutManager.RestoreDaycare();
            }
            finally
            {
                // The ONLY release site now — the normal path no longer releases on its own, so there is one
                // place to be right and one place to look. The guard makes it idempotent and self-selecting:
                // it fires only if a lock is actually held, so the paths that return before acquiring (no
                // outcome worth prepping, gold under the tier, a swap that could not be taken) pass through
                // it untouched, exactly as they always did.
                //
                // Says only what it does: this method ATTEMPTS restoration and release on every exit after
                // acquisition. It cannot promise the lock is never held — LockManager.RestoreConfiguration
                // does its gear restore BEFORE calling ReleaseLock and can itself throw, which would strand
                // the lock in spite of this. That hazard is common to all six lock types and is not the pit's
                // to fix here.
                if (LockManager.HasMoneyPitLock())
                    LockManager.TryMoneyPitSwap();
            }
        }

        // Panel chip + advisor policy read the upcoming outcome.
        public static Outcomes PredictNext() => PredictMoneyPit();

        // The advisor throw policy — shared by ApplyPit (acts on it) and the PIT panel's THROW PLAN
        // chip (displays it), so the UI can never disagree with the behavior.
        public struct PitPlan
        {
            public bool Throw;
            public string Verdict;
            public string Detail;
        }

        public static PitPlan AdvisorPlan()
        {
            var p = new PitPlan();
            try
            {
                var c = _character;
                double gold = c.realGold;

                if (!MoneyPitReady())
                {
                    float t = TimeUntilReady();
                    p.Verdict = t > 3600 ? $"COOLDOWN {t / 3600:0.#}h" : $"COOLDOWN {t / 60:0}m";
                    p.Detail = "PIT NOT READY";
                    return p;
                }
                if (c.machine.realBaseGold <= 0.0)
                {
                    p.Verdict = "HOLD";
                    p.Detail = "TM UNFUNDED — GOLD NEEDED";
                    return p;
                }
                if (OptimizationAdvisor.GoldStarvedForAugs(c, 1.0))
                {
                    p.Verdict = "HOLD";
                    p.Detail = "PROTECTS AUG SPENDING";
                    return p;
                }
                if (gold < 1e13)
                {
                    p.Verdict = "WAIT — below 1e13";
                    p.Detail = "OUTCOME TIERS START AT 1E13";
                    return p;
                }

                // Tier-ETA: hold when the next log10 reward tier is within 15 minutes of net gps.
                double curTier = 0, nextTier = 0;
                foreach (var t in moneyPitThresholds)
                {
                    if (gold >= t) curTier = t;
                    else { nextTier = t; break; }
                }
                double net = 0;
                try { net = c.goldPerSecond(); } catch { }
                if (nextTier > 0 && net > 0)
                {
                    double eta = (nextTier - gold) / net;
                    if (eta >= 0 && eta <= 900)
                    {
                        p.Verdict = $"WAIT — {TierName(nextTier)} in ~{Math.Max(1, eta / 60):0}m";
                        p.Detail = "TIER UP: REWARDS JUMP";
                        return p;
                    }
                }

                p.Throw = true;
                p.Verdict = $"THROW at {TierName(curTier)}";
                p.Detail = "PREDICT + PREP READY";
                return p;
            }
            catch { p.Verdict = "…"; p.Detail = ""; return p; }
        }

        public static string TierName(double t)
        {
            int exp = (int)Math.Round(Math.Log10(t));
            return $"1e{exp}";
        }

        // Advisor throw (and the panel's Throw Now): the policy already decided WHEN — AdvisorApply.ApplyPit
        // consulted AdvisorPlan, or the user clicked. So this execution runs on the advisor's terms: the
        // game's own 1e5 floor rather than the user's manual tier (a "don't throw below 1e30" gate must not
        // veto a decision the advisor already made), and prediction forced on so the outcome's prep loadout
        // is equipped before the throw.
        //
        // These are TRANSIENT EXECUTION INPUTS and are now passed as such. They used to be applied by
        // ASSIGNING Settings.MoneyPitThreshold = 1e5 and Settings.PredictMoneyPit = true and restoring both
        // in a finally — but those are persisted properties whose setters write settings.json (SavedSettings:
        // 203-218: log, IgnoreNextChange, disk write, UpdateForm). So one throw cost FOUR disk writes, four
        // "Saving Settings" lines and four form refreshes, IgnoreNextChange is a single bool and could only
        // swallow one of the four watcher events, and a crash inside the window left 1e5 permanently written
        // over the user's real threshold. Configuration is not a scratch variable. Nothing to restore now.
        public static void AdvisorThrow() => CheckMoneyPit(1e5, true);

        private static Outcomes PredictMoneyPit(double gold = -1.0)
        {
            if (gold < 0.0)
                gold = Main.Character.realGold;
            if (gold >= 1e50 && _character.wishes.wishes[4].level > 0)
            {
                var tempState = Random.state;
                Random.state = _character.pit.pitState;
                int num = Random.Range(1, 6);
                Random.state = tempState;
                return (Outcomes)num;
            }
            else if (gold >= 1e13)
            {
                int num;
                var tempState = Random.state;
                Random.state = _character.pit.pitState;
                if (gold >= 1e18)
                    num = Random.Range(1, 13);
                else if (gold >= 1e15)
                    num = Random.Range(1, 12);
                else
                    num = Random.Range(1, 11);
                Random.state = tempState;
                switch (num)
                {
                    case 4:
                        return Outcomes.Worn;
                    case 12:
                        return Outcomes.Daycare;
                }
            }
            return Outcomes.None;
        }

        private static void DoMoneyPit()
        {
            _character.pitController.CallMethod("engage");
            LogPitSpin($"Money Pit Reward: {_character.pitController.pitText.text}");
        }

        public static void DoDailySpin()
        {
            var controller = _character.dailyController;
            if (_character.daily.spinTime.totalseconds < controller.targetSpinTime())
                return;

            controller.startNoBullshitSpin();
            string result = controller.outcomeText.text;
            LogPitSpin($"Daily Spin Reward: {result}");
        }
    }
}
