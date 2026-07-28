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

                bool evil = (int)c.settings.rebirthDifficulty >= 1;
                double[] baseTimes = evil
                    ? new[] { 1e21, 1e27, 1e33 }
                    : new[] { 1e9, 1e12, 1e15 };

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
