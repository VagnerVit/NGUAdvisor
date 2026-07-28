using System;

namespace NGUAdvisor.Managers
{
    // Laser Sword Challenge opportunity (user insight): LSC does NOT reset the number, and its
    // completion condition — read live from the game — is simply leveling the Laser Sword augment
    // (augs[6]) AND its upgrade to laserSwordTarget(). That makes it finishable inside a normal
    // run's Augs window: free challenge progress while augs get their hour anyway. The advisor
    // estimates time-to-target from the aug speed formula and recommends the run when it fits
    // comfortably inside an hour.
    //
    // All challenge state comes from the CONTROLLER (Character.allChallenges.laserSwordChallenge),
    // never from the serialized Character.challenges.laserSwordChallenge data object: that object's
    // maxCompletions is [NonSerialized] and nothing in the game ever assigns it (its only writer,
    // Challenge.setChallengeStats, has no callers), so it reads 0 forever; and its curCompletions is
    // the NORMAL-difficulty counter, unclamped. The controller's currentCompletions()/maxCompletions/
    // laserSwordTarget() are the numbers the game itself plays by, on every difficulty.
    public static class LscAdvisor
    {
        public class Verdict
        {
            public bool Known;
            public bool Recommended;
            public int Target;
            public double EstMinutes;
            public string Text;
        }

        private static Verdict _cache;
        private static DateTime _cacheAt = DateTime.MinValue;

        private static Verdict Cache(Verdict v)
        {
            _cache = v;
            _cacheAt = DateTime.UtcNow;
            return v;
        }

        // Sum of (L+1) for L = 0..n-1 — the cost weight of leveling from SCRATCH to n, which is what
        // the challenge charges: engaging LSC is a rebirth (Rebirth.engage -> Character.resetAll ->
        // Aug.reset), so the sword's aug and upgrade levels are ZEROED on entry. Discounting the
        // levels it carries in read a maxed sword as a free challenge and auto-entered it.
        private static double LevelSum(double n) => n <= 0 ? 0 : n * (n + 1) / 2.0;

        private static bool Usable(double x) => !double.IsNaN(x) && !double.IsInfinity(x) && x > 0;

        public static Verdict Compute()
        {
            var v = new Verdict();
            try
            {
                var c = Main.Character;
                if (c == null) return v;
                if (!c.challenges.laserSwordChallengeUnlocked) return v;
                if (ChallengeDetector.Current() != null) return v;   // already in one

                var ctl = c.allChallenges?.laserSwordChallenge;
                if (ctl == null) return v;

                int done = ctl.currentCompletions();   // this difficulty's counter, clamped to max
                int max = ctl.maxCompletions;
                if (max > 0 && done >= max) return v;   // LSC finished on this difficulty

                // Only the estimate is expensive enough to cache; the gates above are live reads on
                // every call. Caching THEM (they are the common early-outs) served a Known=false
                // verdict for two minutes — and since completing a challenge does not rebirth, the
                // auto-rebirth right after one landed inside that window, read the stale "no" and
                // started a plain run instead of the LSC run.
                if (_cache != null && (DateTime.UtcNow - _cacheAt).TotalSeconds < 120) return _cache;

                int target = ctl.laserSwordTarget();
                v.Known = true;
                v.Target = target;

                // Speed comes from the GAME'S OWN rate function (the extensions wrap
                // AugmentController.getAugProgressPerTick(energy) / the upgrade equivalent), never a
                // hand-copied formula: the gear Augs bonus, macguffin 12, hack/ITOPOD/card aug-speed
                // bonuses, the noAugs multipliers AND the sadistic /50000000 divider are then in the
                // number by construction. The hand-rolled version omitted all of them — it overstated
                // normal/evil runs by the whole bonus chain and understated sadistic by ~5e7, where
                // every LSC looked instantly free.
                //
                // The rate is linear in 1/(level+1), so seconds for level L = secAtCurrent x
                // (L+1)/(L0+1), and the cost of leveling from 0 to the target is that per-level
                // coefficient x LevelSum(target). Energy = the whole pool (curEnergy): in the
                // challenge the sword owns the Augs hour. EXP-bought energy/power persist into
                // challenges, so current values are a fair basis; x2 safety for start friction + gold.
                var aug = c.augmentsController.augments[6];
                long energy = (long)Math.Max(1, c.curEnergy);
                double augLv = c.augments.augs[6].augLevel;
                double upgLv = c.augments.augs[6].upgradeLevel;

                double augPerLevel = aug.AugTimeLeftEnergyMax(energy) / (augLv + 1.0);
                double upgPerLevel = aug.UpgradeTimeLeftEnergyMax(energy) / (upgLv + 1.0);
                if (!Usable(augPerLevel) || !Usable(upgPerLevel))
                {
                    v.Known = false;   // no energy power / no rate yet — say nothing rather than guess
                    return Cache(v);
                }

                double seconds = (augPerLevel + upgPerLevel) * LevelSum(target) * 2.0;

                v.EstMinutes = seconds / 60.0;
                v.Recommended = v.EstMinutes <= 48;   // fits comfortably inside the Augs hour
                v.Text = v.Recommended
                    ? $"LSC finishable in ~{Math.Max(1, v.EstMinutes):0}m (laser sword to lv {target}) — number is NOT reset; next auto-rebirth enters it"
                    : $"LSC needs ~{v.EstMinutes:0}m for lv {target} — not yet an Augs-hour freebie";
            }
            catch (Exception e) { Main.LogDebug($"LscAdvisor: {e.Message}"); }
            return Cache(v);
        }
    }
}
