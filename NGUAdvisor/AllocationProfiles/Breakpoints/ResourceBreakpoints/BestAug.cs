using NGUAdvisor.Managers;
using System;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    // Picks the augment pair worth funding right now. Ranking is a HORIZON PROJECTION: for each aug,
    // how much stat boost would this energy share actually buy over the next `Horizon()` seconds?
    // That is a true gain-per-second (the horizon is shared, so the division is a no-op) and it lets
    // an expensive-but-steep aug beat a cheap shallow one on its merits.
    //
    // It replaces the old `if (time > 300) continue;` cutoff, which abandoned any aug whose NEXT LEVEL
    // cost more than five minutes — a pure cost test with no reference to what the level was worth.
    // In practice that dropped the laser sword at ~lv 8 every run regardless of value, because aug 6
    // has both the largest baseBoost and the largest augTierBonus exponent and therefore also the
    // steepest cost curve. Cost still matters, but now only through how many levels the horizon buys.
    public class BestAug : AugmentBP
    {
        private bool _useUpgrades;

        // How far ahead the projection looks when the profile says nothing about the phase length. An
        // hour is long enough that a slow, steep aug can show its value and short enough that the
        // linear cost model below stays honest.
        private const double DefaultHorizon = 3600.0;

        // Hard ceiling on the projection. Boost grows as time^(1 + tier/2), so the horizon must track
        // the real phase (Ch.5 runs augments 0:30-3:00) instead of a fixed hour — but beyond a few
        // hours the linear cost model drifts too far to trust, so an open-ended phase is clamped.
        private const double MaxHorizon = 3.0 * 3600.0;

        protected override bool Unlocked() => _character.buttons.augmentation.interactable && !_character.challenges.noAugsChallenge.inChallenge;

        protected override bool TargetMet() => false;

        public override bool Allocate()
        {
            if (Main.Settings.MoneyPitRunMode && _character.machine.realBaseGold <= 0.0 && MoneyPitManager.NeedsLowerTier())
                return false;

            _useUpgrades = _character.bossID >= 37;
            return AllocatePairs() > 0;
        }

        // Seconds of run to project over: whichever comes first of the end of the augment phase and the
        // scheduled rebirth, clamped to MaxHorizon. Both are read from the LIVE profile (Main.Profile,
        // as BloodPlanner and WandoosAdvisor do): the breakpoint parser never populated a RebirthTime on
        // BESTAUG, so the property this used to read was always 0 and its guard — the one the rewrite
        // claimed replaced the old `time > 300` cutoff — could never fire. Cf. BR.cs, unwired the same way.
        //
        // hardStop means funding really ENDS at the horizon, which is what makes a level still in flight
        // there worth nothing (see LevelsInHorizon): the rebirth wipes it, and the phase end freezes it
        // with no later breakpoint to finish it. Past the rebirth deadline the rebirth can still be
        // blocked — NUMBER/BOSSNUM targets are floors, not deadlines, and locks or the No-Rebirth
        // challenge can hold it — so the run continues on the full horizon rather than going dark.
        private double Horizon(out bool hardStop)
        {
            hardStop = false;
            double horizon = DefaultHorizon;

            double phase = -1;
            try { phase = Main.Profile != null ? Main.Profile.AugmentPhaseSecondsLeft() : -1; } catch { }
            if (phase > 0)
            {
                horizon = Math.Min(MaxHorizon, phase);
                hardStop = phase <= MaxHorizon;
            }

            if (Main.Settings.AutoRebirth)
            {
                double target = -1;
                try { target = Main.Profile != null ? Main.Profile.NextRebirthTargetSeconds() : -1; } catch { }
                if (target > 0)
                {
                    double left = target - _character.rebirthTime.totalseconds;
                    if (left > 0 && left < horizon)
                    {
                        horizon = left;
                        hardStop = true;
                    }
                }
            }

            return horizon;
        }

        // Levels this half gains in `horizon` seconds. The level in flight lands after `secLeft` (its
        // progress is already banked); every level after it costs c x (L+1), because the game's cost is
        // linear in the level (getAugProgressPerTick divides by level+1). With c = secPerLevel/(level+1)
        // the time for n more levels is c * (n*(level+1) + n(n+1)/2); invert for n.
        //
        // Fractions are kept here and floored by the caller once the gold ceiling has been applied: the
        // game pays stat boost per COMPLETED level (augLevel is an integer; augProgress only carries
        // within a run), so at a hard stop a level still in flight is wiped and worth nothing — funding
        // it is the waste the old cutoff crudely bounded. Mid-phase the fraction is real: the progress is
        // banked and the next pass resumes it, so it is priced as-is.
        private static double LevelsInHorizon(double secPerLevel, double secLeft, double level, double horizon)
        {
            if (secPerLevel <= 0 || horizon <= 0) return 0;
            if (secLeft <= 0 || secLeft > secPerLevel) secLeft = secPerLevel;   // no/odd progress data

            double n;
            if (horizon <= secLeft)
            {
                n = horizon / secLeft;   // still inside the level in flight
            }
            else
            {
                double c = secPerLevel / (level + 1.0);
                double b = 2.0 * (level + 1.0) + 1.0;
                double t = horizon - secLeft;
                n = 1.0 + (-b + Math.Sqrt(b * b + 8.0 * t / c)) / 2.0;
            }
            return n > 0 ? n : 0;
        }

        // Levels this half can PAY for out of `budget`. The energy clock above is only half the price:
        // the game also charges gold per level — base x (L+1) for an augment, base x (L+1)^2 for an
        // upgrade — so the aug half integrates to base x L^2/2 while the upgrade half integrates to
        // base x U^3/3. That cubic is why the upgrade half, not the augment, is what gold actually stops,
        // and why an aug that looks fast can still be the wrong pick. Levelling a whole horizon without
        // this ceiling is what the old one-second-of-gold gate tried and failed to bound.
        //
        // The base is derived from the LIVE next-level cost, so every discount in the chain (the No Augs
        // challenge's -50% above all) is included by construction rather than reimplemented.
        private static double LevelsAffordable(double nextLevelCost, double level, double budget, bool squared)
        {
            if (nextLevelCost <= 0) return double.MaxValue;
            if (budget <= 0) return 0;

            // Midpoint form of the sum, exact for the linear case and within a fraction of a level for
            // the quadratic one.
            double m = level + 0.5;
            if (squared)
            {
                double b = nextLevelCost / ((level + 1.0) * (level + 1.0));
                double n = Math.Pow(m * m * m + 3.0 * budget / b, 1.0 / 3.0) - m;
                return n > 0 ? n : 0;
            }

            double a = nextLevelCost / (level + 1.0);
            double lin = -m + Math.Sqrt(m * m + 2.0 * budget / a);
            return lin > 0 ? lin : 0;
        }

        // Which halves of the pair can still take energy. An aug is a candidate if EITHER half is live:
        // the old test skipped the whole aug the moment one target was met (and `_useUpgrades &&
        // upgradeLocked() || hitUpgradeTarget()` bound as `(_useUpgrades && upgradeLocked()) ||
        // hitUpgradeTarget()`, so a met UPGRADE target starved the aug half too, even pre-boss-37).
        private void LiveHalves(AugmentController aug, out bool augLive, out bool upgLive)
        {
            augLive = !aug.augLocked() && !aug.hitAugmentTarget();
            upgLive = _useUpgrades && !aug.upgradeLocked() && !aug.hitUpgradeTarget();
        }

        // Energy split by elasticity: boost goes as augLevel^tier x upgradeLevel^2, so the exponents
        // tier and 2 are the shares. A dead half yields its share to the live one.
        private static void Split(double tier, bool augLive, bool upgLive, out float augRatio, out float upgRatio)
        {
            if (augLive && upgLive)
            {
                augRatio = (float)(tier / (2.0 + tier));
                upgRatio = (float)(2.0 / (2.0 + tier));
            }
            else
            {
                augRatio = augLive ? 1f : 0f;
                upgRatio = upgLive ? 1f : 0f;
            }
        }

        private long Share(float ratio) => ratio <= 0 ? 0 : Math.Max(1, (long)(MaxAllocation * ratio));

        // One half of a pair, priced at a given energy share.
        private struct Half
        {
            public bool Live;
            public double Level;
            public float Progress;
            public double Seconds;    // seconds for a whole level at this share
            public double Left;       // seconds left on the level in flight
            public double ByEnergy;   // levels the clock buys inside the horizon
            public double ByGold;     // levels the gold share pays for
            public double Levels;     // what actually lands
        }

        private Half Price(AugmentController aug, bool upgrade, bool live, float ratio,
            double horizon, double goldShare, bool hardStop)
        {
            Half half = new Half();
            half.Live = live;
            if (!live || ratio <= 0f)
                return half;

            long share = Share(ratio);
            half.Level = upgrade
                ? _character.augments.augs[aug.id].upgradeLevel
                : _character.augments.augs[aug.id].augLevel;
            half.Progress = upgrade ? aug.UpgradeProgress() : aug.AugProgress();
            // TimeLeftEnergy is just TimeLeftEnergyMax x (1 - progress), so derive it instead of paying
            // the game's rate call a second time per half.
            half.Seconds = Math.Max(0.01, upgrade
                ? aug.UpgradeTimeLeftEnergyMax(share)
                : aug.AugTimeLeftEnergyMax(share));
            half.Left = half.Seconds * (1.0 - half.Progress);
            half.ByEnergy = LevelsInHorizon(half.Seconds, half.Left, half.Level, horizon);
            half.ByGold = LevelsAffordable(upgrade ? (double)aug.getUpgradeCost() : (double)aug.getAugCost(),
                half.Level, goldShare, upgrade);
            half.Levels = Gained(half.ByEnergy, half.ByGold, hardStop);
            return half;
        }

        // Fraction of its energy share a half can actually convert. A gold-starved half fills its bar and
        // then waits for gold (the reference optimizer models the same wait, Augment.js:113-136) — the
        // energy behind that wait becomes nothing. Levels grow as sqrt(energy), so a half held to n_G of
        // the n_E levels its share would clock needs only (n_G/n_E)^2 of that share.
        private static float Usable(Half half)
        {
            if (!half.Live || half.ByEnergy <= 0 || half.ByGold >= half.ByEnergy)
                return 1f;
            double f = half.ByGold / half.ByEnergy;
            return (float)(f * f);
        }

        private float AllocatePairs()
        {
            double horizon = Horizon(out bool hardStop);

            double gold = _character.realGold;
            // Net GPS (after digger drain), as DiggerManager and MoneyPitManager read it: what the run
            // will actually have banked by the end of the horizon is what the levels get paid from.
            double gps = 0;
            try { gps = _character.goldPerSecond(); } catch { }
            double budget = gold + Math.Max(0.0, gps) * horizon;

            var bestAugment = -1;
            var bestValue = 0.0;
            bool bestAugLive = false, bestUpgLive = false;
            float bestAugRatio = 0f, bestUpgRatio = 0f;

            for (var i = 0; i < 7; i++)
            {
                var aug = _character.augmentsController.augments[i];
                LiveHalves(aug, out bool augLive, out bool upgLive);
                if (!augLive && !upgLive)
                    continue;

                double tier = aug.augTierBonus();
                Split(tier, augLive, upgLive, out float augRatio, out float upgRatio);

                // The gold budget is split by the ELASTICITY shares and then left alone. Only energy is
                // rebalanced below; letting the gold split follow the corrected energy split would feed
                // the correction back into its own input and chase its tail.
                double augBudget = budget * augRatio;
                double upgBudget = budget * upgRatio;

                Half augHalf = Price(aug, false, augLive, augRatio, horizon, augBudget, hardStop);
                Half upgHalf = Price(aug, true, upgLive, upgRatio, horizon, upgBudget, hardStop);

                // Hand the share a gold-starved half cannot convert to the half that still can — in
                // practice the aug half, because the upgrade's cost integrates cubically (see
                // LevelsAffordable) and it is the one gold stops first. If both halves are capped the
                // surplus changes no hands and stays idle for the other priorities in the breakpoint.
                // Single pass: the corrected share moves each half's clock, but re-pricing a second time
                // shifts the result by a fraction of a level.
                float augUsable = Usable(augHalf);
                float upgUsable = Usable(upgHalf);
                if (augLive && upgLive && (augUsable < 1f || upgUsable < 1f))
                {
                    float freed = augRatio * (1f - augUsable) + upgRatio * (1f - upgUsable);
                    augRatio *= augUsable;
                    upgRatio *= upgUsable;
                    if (augUsable >= 1f)
                        augRatio += freed;
                    else if (upgUsable >= 1f)
                        upgRatio += freed;

                    augHalf = Price(aug, false, augLive, augRatio, horizon, augBudget, hardStop);
                    upgHalf = Price(aug, true, upgLive, upgRatio, horizon, upgBudget, hardStop);
                }

                // Gold gate on the half we would actually start. A level already in progress, or one
                // about to land, is worth waiting on; a cold one we cannot pay for is not.
                double time = Math.Max(augHalf.Seconds, upgHalf.Seconds);
                double cost = Math.Max(1, 1.0 / time) * (upgLive ? (double)aug.getUpgradeCost() : (double)aug.getAugCost());
                float progress = upgLive ? upgHalf.Progress : augHalf.Progress;
                double timeRemaining = upgLive ? upgHalf.Left : augHalf.Left;
                if (cost > gold && (progress == 0f || timeRemaining < 10))
                    continue;

                double value = ProjectedGain(aug, augHalf, upgHalf, tier);
                if (value > bestValue)
                {
                    bestAugment = i;
                    bestValue = value;
                    bestAugLive = augLive;
                    bestUpgLive = upgLive;
                    bestAugRatio = augRatio;
                    bestUpgRatio = upgRatio;
                }
            }

            if (bestAugment == -1)
                return 0;

            var best = _character.augmentsController.augments[bestAugment];
            var totalAllocated = 0f;
            var index = bestAugment * 2;
            // A share rebalanced to zero means gold cannot convert a single level there — skip the half
            // instead of feeding it energy that would sit on a waiting bar.
            if (bestAugLive && bestAugRatio > 0f)
            {
                long alloc = CalculateAugCap(index, Share(bestAugRatio));
                SetInput(alloc);
                best.addEnergyAug();
                totalAllocated += alloc;
            }
            if (bestUpgLive && bestUpgRatio > 0f)
            {
                long alloc = CalculateAugCap(index + 1, Share(bestUpgRatio));
                SetInput(alloc);
                best.addEnergyUpgrade();
                totalAllocated += alloc;
            }
            return totalAllocated;
        }

        // Levels actually banked: whichever of the energy clock and the gold budget runs out first, and
        // only whole levels when funding stops at the horizon.
        private static double Gained(double byEnergy, double byGold, bool hardStop)
        {
            double n = Math.Min(byEnergy, byGold);
            if (hardStop) n = Math.Floor(n);
            return n > 0 ? n : 0;
        }

        // Stat boost this pair would hold at the end of the horizon, minus what it holds now. The boost
        // formula is the game's own (AugmentController.getTotalStatBoost):
        //     baseBoost x (upgradeLevel^2 + 1) x augLevel^augTierBonus
        private double ProjectedGain(AugmentController aug, Half augHalf, Half upgHalf, double tier)
        {
            double augLv = _character.augments.augs[aug.id].augLevel;
            double upgLv = _character.augments.augs[aug.id].upgradeLevel;

            double newAug = augLv + augHalf.Levels;
            double newUpg = upgLv + upgHalf.Levels;

            double projected = (double)aug.baseBoost * (Math.Pow(newUpg, 2.0) + 1.0) * Math.Pow(newAug, tier);
            return projected - aug.getTotalStatBoost();
        }
    }
}
