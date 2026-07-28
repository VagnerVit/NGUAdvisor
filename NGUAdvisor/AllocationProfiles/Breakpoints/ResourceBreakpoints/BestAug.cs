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

        // How far ahead the projection looks. An hour is long enough that a slow, steep aug can show
        // its value and short enough that the linear cost model below stays honest.
        private const double MaxHorizon = 3600.0;

        protected override bool Unlocked() => _character.buttons.augmentation.interactable && !_character.challenges.noAugsChallenge.inChallenge;

        protected override bool TargetMet() => false;

        public override bool Allocate()
        {
            if (Main.Settings.MoneyPitRunMode && _character.machine.realBaseGold <= 0.0 && MoneyPitManager.NeedsLowerTier())
                return false;

            _useUpgrades = _character.bossID >= 37;
            return AllocatePairs() > 0;
        }

        // Seconds of run to project over, capped at MaxHorizon and by the rebirth when the profile
        // schedules one. The deadline is read from the LIVE profile (Main.Profile, as BloodPlanner and
        // WandoosAdvisor do): the breakpoint parser never populated a RebirthTime on BESTAUG, so the
        // property this used to read was always 0 and its guard — the one the rewrite claimed replaced
        // the old `time > 300` cutoff — could never fire. Cf. BR.cs, whose copy is unwired the same way.
        //
        // toRebirth means the horizon ENDS at the rebirth, which is what makes a level still in flight
        // there worth nothing (see LevelsInHorizon). Past the deadline the rebirth can still be blocked
        // — NUMBER/BOSSNUM targets are floors, not deadlines, and locks or the No-Rebirth challenge can
        // hold it — so the run continues and we keep funding on the full horizon rather than going dark.
        private double Horizon(out bool toRebirth)
        {
            toRebirth = false;
            if (!Main.Settings.AutoRebirth) return MaxHorizon;

            double target = -1;
            try { target = Main.Profile != null ? Main.Profile.NextRebirthTargetSeconds() : -1; } catch { }
            if (target <= 0) return MaxHorizon;

            double left = target - _character.rebirthTime.totalseconds;
            if (left <= 0 || left >= MaxHorizon) return MaxHorizon;
            toRebirth = true;
            return left;
        }

        // Levels this half gains in `horizon` seconds. The level in flight lands after `secLeft` (its
        // progress is already banked); every level after it costs c x (L+1), because the game's cost is
        // linear in the level (getAugProgressPerTick divides by level+1). With c = secPerLevel/(level+1)
        // the time for n more levels is c * (n*(level+1) + n(n+1)/2); invert for n.
        //
        // completedOnly FLOORS the result. The game pays stat boost per COMPLETED level (augLevel is an
        // integer; augProgress only carries within a run), so at the rebirth a level still in flight is
        // wiped and worth nothing — funding it is the waste the old cutoff crudely bounded. Mid-run the
        // fraction is real: the progress is banked and the next pass resumes it, so it is priced as-is.
        private static double LevelsInHorizon(double secPerLevel, double secLeft, double level, double horizon, bool completedOnly)
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
            if (completedOnly) n = Math.Floor(n);
            return n > 0 ? n : 0;
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

        private float AllocatePairs()
        {
            double horizon = Horizon(out bool toRebirth);

            double gold = _character.realGold;
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

                // Full-level cost, and what is left of the level in flight. TimeLeftEnergy is just
                // TimeLeftEnergyMax x (1 - progress), so derive it instead of paying the game's rate
                // call a second time per half.
                float augProgress = aug.AugProgress();
                float upgProgress = aug.UpgradeProgress();
                double augSec = augLive ? Math.Max(0.01, aug.AugTimeLeftEnergyMax(Share(augRatio))) : 0;
                double upgSec = upgLive ? Math.Max(0.01, aug.UpgradeTimeLeftEnergyMax(Share(upgRatio))) : 0;
                double augLeft = augSec * (1.0 - augProgress);
                double upgLeft = upgSec * (1.0 - upgProgress);

                // Gold gate on the half we would actually start. A level already in progress, or one
                // about to land, is worth waiting on; a cold one we cannot pay for is not.
                double time = Math.Max(augSec, upgSec);
                double cost = Math.Max(1, 1.0 / time) * (upgLive ? (double)aug.getUpgradeCost() : (double)aug.getAugCost());
                float progress = upgLive ? upgProgress : augProgress;
                double timeRemaining = upgLive ? upgLeft : augLeft;
                if (cost > gold && (progress == 0f || timeRemaining < 10))
                    continue;

                double value = ProjectedGain(aug, augLive, upgLive, augSec, augLeft, upgSec, upgLeft, tier, horizon, toRebirth);
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
            if (bestAugLive)
            {
                long alloc = CalculateAugCap(index, Share(bestAugRatio));
                SetInput(alloc);
                best.addEnergyAug();
                totalAllocated += alloc;
            }
            if (bestUpgLive)
            {
                long alloc = CalculateAugCap(index + 1, Share(bestUpgRatio));
                SetInput(alloc);
                best.addEnergyUpgrade();
                totalAllocated += alloc;
            }
            return totalAllocated;
        }

        // Stat boost this pair would hold at the end of the horizon, minus what it holds now. The boost
        // formula is the game's own (AugmentController.getTotalStatBoost):
        //     baseBoost x (upgradeLevel^2 + 1) x augLevel^augTierBonus
        private double ProjectedGain(AugmentController aug, bool augLive, bool upgLive,
            double augSec, double augLeft, double upgSec, double upgLeft, double tier, double horizon, bool toRebirth)
        {
            double augLv = _character.augments.augs[aug.id].augLevel;
            double upgLv = _character.augments.augs[aug.id].upgradeLevel;

            double newAug = augLive ? augLv + LevelsInHorizon(augSec, augLeft, augLv, horizon, toRebirth) : augLv;
            double newUpg = upgLive ? upgLv + LevelsInHorizon(upgSec, upgLeft, upgLv, horizon, toRebirth) : upgLv;

            double projected = (double)aug.baseBoost * (Math.Pow(newUpg, 2.0) + 1.0) * Math.Pow(newAug, tier);
            return projected - aug.getTotalStatBoost();
        }
    }
}
