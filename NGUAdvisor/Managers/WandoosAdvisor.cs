using System;

namespace NGUAdvisor.Managers
{
    // Route C3 Phase A: EXACT Wandoos OS comparator, mirroring the game's own math
    // (decompiled Wandoos98Controller, verified against reference/decomp/Wandoos98Controller.cs):
    //   level rate on an OS = min(alloc * totalWandoosSpeed / baseTime, 1) per tick, 50 ticks/sec
    //   baseTime   normal: 98=1e9, MEH=1e12, XL=1e15   evil+: 1e21 / 1e27 / 1e33
    //   bonus      98: ((1+E/100)(1+M/25))^0.8   MEH: (1+E/5)(1+2M)   XL: ((1+6E)(1+40M))^1.05
    // The projection assumes: allocation = whole current E/M cap (how CAPWAN behaves when it can),
    // current live speed (includes OS level + bootup + gear/AT/beard/digger bonuses — identical for
    // all three OS types, so the comparison is fair), the CURRENT OS keeping its already-banked
    // levels while the other two OSs start from level 0 (switching to them wipes their levels),
    // over a fixed time window.
    public static class WandoosAdvisor
    {
        public struct OsCase
        {
            public int Os;
            public string Name;
            public bool Unlocked;
            public double Bonus;    // projected A/D multiplier after the window
            public double LevelsE;
            public double LevelsM;
        }

        public struct Verdict
        {
            public bool Known;
            public int CurrentOs;
            public int BestOs;
            public string CurrentName;
            public string BestName;
            public double Advantage;   // best projected bonus / current-OS projected bonus
            public OsCase[] Cases;
        }

        private static readonly string[] Names = { "98", "MEH", "XL" };

        // Projection window matched to the RUN, not a fixed hour: remaining time to the profile's
        // time-based rebirth target (clamped 10m-4h); 120m when rebirth is off/unset (NORB, LRB).
        public static int RunHorizonMinutes()
        {
            try
            {
                double target = Main.Profile != null ? Main.Profile.NextRebirthTargetSeconds() : -1;
                if (target <= 0) return 120;
                double remainingMin = (target - Main.Character.rebirthTime.totalseconds) / 60.0;
                return (int)Math.Min(Math.Max(remainingMin, 10), 240);
            }
            catch { return 120; }
        }

        public static Verdict Compare(int minutes)
        {
            var v = new Verdict { Known = false };
            try
            {
                var c = Main.Character;
                if (c == null) return v;

                double[] baseTimes = BaseTimes(c);

                bool[] unlocked = { true, false, false };
                try { unlocked[1] = c.inventory.itemList.jakeComplete; } catch { }
                try { unlocked[2] = c.wandoos98.XLLevels > 0; } catch { }

                // Project at FULL-BOOT speed: right after a rebirth the bootup factor is ~0, which
                // would zero every OS's projection and make the comparison garbage (and silently
                // block the auto-switch at exactly the moment switching is free). Divide the live
                // speed by the current bootup factor; if wandoos has barely booted the numbers are
                // unstable, so report unknown and let the next tick (a minute later) decide.
                double boot = 1.0;
                try { boot = c.wandoos98Controller.bootupSpeedFactor(); } catch { }
                if (boot < 0.02) return v;
                double speedE = c.totalWandoosEnergySpeed() / boot;
                double speedM = c.totalWandoosMagicSpeed() / boot;
                double capE = c.totalCapEnergy();
                double capM = c.totalCapMagic();
                double seconds = minutes * 60.0;

                int curOs = (int)c.wandoos98.os;
                if (curOs < 0 || curOs > 2) curOs = 0;

                var cases = new OsCase[3];
                for (int os = 0; os < 3; os++)
                {
                    // 1 level per tick max (the game adds progress once per 0.02s tick)
                    double rateE = Math.Min(capE * speedE / baseTimes[os], 1.0) * 50.0;
                    double rateM = Math.Min(capM * speedM / baseTimes[os], 1.0) * 50.0;
                    double lE = rateE * seconds, lM = rateM * seconds;
                    // The current OS keeps its already-banked levels; the other two correctly
                    // start from 0 because switching to them wipes their levels (changeOS zeroes
                    // energyLevel/magicLevel). Without this the current OS is understated and the
                    // advantage ratio is biased toward recommending a switch.
                    if (os == curOs)
                    {
                        lE += c.wandoos98.energyLevel;
                        lM += c.wandoos98.magicLevel;
                    }
                    cases[os] = new OsCase
                    {
                        Os = os,
                        Name = Names[os],
                        Unlocked = unlocked[os],
                        LevelsE = lE,
                        LevelsM = lM,
                        Bonus = BonusFor(os, lE, lM)
                    };
                }

                int cur = curOs;
                int best = 0;
                for (int os = 1; os < 3; os++)
                    if (cases[os].Unlocked && cases[os].Bonus > cases[best].Bonus) best = os;

                v.Known = true;
                v.CurrentOs = cur;
                v.BestOs = best;
                v.CurrentName = Names[cur];
                v.BestName = Names[best];
                v.Advantage = cases[cur].Bonus > 0 ? cases[best].Bonus / cases[cur].Bonus : 1.0;
                v.Cases = cases;
            }
            catch (Exception e) { Main.LogDebug($"WandoosAdvisor: {e.Message}"); }
            return v;
        }

        private static double[] BaseTimes(Character c)
        {
            bool evil = (int)c.settings.rebirthDifficulty >= 1;
            return evil ? new[] { 1e21, 1e27, 1e33 } : new[] { 1e9, 1e12, 1e15 };
        }

        // Whole-run length, not the remainder: the run-level question below has ONE answer per run.
        // Same 10m-4h clamp and 120m no-target default as RunHorizonMinutes().
        private static double RunSeconds()
        {
            try
            {
                double target = Main.Profile != null ? Main.Profile.NextRebirthTargetSeconds() : -1;
                if (target > 0) return Math.Min(Math.Max(target, 600.0), 14400.0);
            }
            catch { }
            return 7200.0;
        }

        // Is a Wandoos dump lane holding `alloc` of ONE resource worth the allocation it occupies?
        // Bosses gained = log10(A/D multiplier) — boss requirements grow ~10x per boss (bossAttack
        // 1.98e72 at boss 74 vs 1.98e77 at boss 79) — so the 10x default asks for ONE boss, the
        // smallest unit of progress the whole A/D lever exists to buy.
        //
        // Judged over the WHOLE run and from level 0, NOT marginally over what is already banked.
        // Both follow from the dump being wiped at every rebirth: the real question is "should this
        // run carry a Wandoos lane at all?". A marginal read retires the lane ~30 s after the
        // rebirth on any concave bonus (at 1805 levels/run the break-even sits at 8 banked levels),
        // however well the lane pays across the run — measured, not assumed.
        //
        // Why this can't be a fixed allocation/rate threshold: levels-per-10x differs by three
        // orders of magnitude across the OSs (98 needs ~1678 energy levels, MEH ~45, XL ~2), so a
        // constant tuned on 98 would silently retire a perfectly good MEH/XL dump.
        //
        // Unknowable inputs answer TRUE — never retire a lane on a failed read.
        public static bool DumpWorthwhile(bool energy, long alloc, double minMultiplier = 10.0)
        {
            try
            {
                var c = Main.Character;
                if (c == null || alloc <= 0) return true;

                int os = (int)c.wandoos98.os;
                if (os < 0 || os > 2) os = 0;

                // Same full-boot projection Compare() uses: the live speed right after a rebirth is
                // ~0, which would make every lane look worthless exactly when the levels are
                // cheapest. Under-booted reads are unstable, so keep the lane.
                double boot = 1.0;
                try { boot = c.wandoos98Controller.bootupSpeedFactor(); } catch { }
                if (boot < 0.02) return true;

                double speed = (energy ? c.totalWandoosEnergySpeed() : c.totalWandoosMagicSpeed()) / boot;
                double rate = Math.Min(alloc * speed / BaseTimes(c)[os], 1.0) * 50.0;
                double levels = rate * RunSeconds();

                double bonus = energy ? BonusFor(os, levels, 0.0) : BonusFor(os, 0.0, levels);
                return bonus >= minMultiplier;
            }
            catch (Exception e)
            {
                Main.LogDebug($"WandoosAdvisor.DumpWorthwhile: {e.Message}");
                return true;
            }
        }

        // The game's Wandoos98Controller.wandoosBonus(), with levels as inputs.
        private static double BonusFor(int os, double levelsE, double levelsM)
        {
            switch (os)
            {
                case 0: return Math.Pow((1.0 + levelsE / 100.0) * (1.0 + levelsM / 25.0), 0.8);
                case 1: return (1.0 + levelsE / 5.0) * (1.0 + levelsM * 2.0);
                default: return Math.Pow((1.0 + levelsE * 6.0) * (1.0 + levelsM * 40.0), 1.05);
            }
        }

        public static string FmtX(double ratio)
        {
            if (ratio >= 1e6) return (ratio / 1e6).ToString("0.#") + "M×";
            if (ratio >= 1000) return (ratio / 1000).ToString("0.#") + "K×";
            if (ratio >= 10) return ratio.ToString("0") + "×";
            return ratio.ToString("0.0") + "×";
        }
    }
}
