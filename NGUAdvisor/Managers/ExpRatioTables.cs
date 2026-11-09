namespace NGUAdvisor.Managers
{
    // Target EXP-purchase ratios per guide phase, transcribed from external/ngu-guide
    // (chapters 1-7, "EXP Spending" / "E/M/R3 Ratios" sections). Game-independent on purpose:
    // this half is unit-tested, ExpBalancer holds everything that needs the live character.
    //
    // TWO CONVERSIONS happen here, and both are the ones people get wrong:
    //
    // 1. P:C:B is a UNIT ratio; the balancer works in EXP-space. Unit costs (energy): power 150,
    //    cap 1 per 250, bars 80. So 1:37.5k:1 -> 150 : 37500/250 : 80 = 150:150:80, and
    //    5:160k:4 -> 750:640:320, and 4:150k:1 -> 600:600:80. Magic costs are all exactly 3x, so
    //    the shares within the magic pool are identical.
    // 2. E:M is a ratio of PURCHASED VALUES, not of EXP. Since a magic unit costs 3x an energy
    //    unit, a target value ratio of r:1 needs an EXP split of r:3 — i.e. PoolE = r/(r+3).
    //    Hence 3:1 -> 50/50, 2:1 -> 0.4/0.6, 5:1 -> 0.625/0.375. Reading "3:1 E:M" as an EXP
    //    split would drive the values to 9:1 and waste EXP.
    public static class ExpRatioTables
    {
        public struct Targets
        {
            public double PoolE;    // fraction of EXP for the energy pool
            public double PoolM;    // ... and the magic pool (0 when the guide says energy-only)
            public double ShareP;   // within a pool: power / cap / bars shares, EXP-space, summing to 1
            public double ShareC;
            public double ShareB;
            public double PoolR;    // fraction of EXP for Resource 3 (E/M pools are scaled down by it)
            public string Phase;    // guide phase this came from, for the advisor readout
        }

        private static Targets Build(double poolE, double shP, double shC, double shB, string phase)
        {
            double sum = shP + shC + shB;
            return new Targets
            {
                PoolE = poolE,
                PoolM = 1.0 - poolE,
                PoolR = 0.0,
                ShareP = shP / sum,
                ShareC = shC / sum,
                ShareB = shB / sum,
                Phase = phase
            };
        }

        // Guide ch.6 "E/M/R3 Ratios": R3 4/150k/1, in EXP-space 600:600:80 (same conversion as above).
        public const double R3ShareP = 600.0 / 1280.0;
        public const double R3ShareC = 600.0 / 1280.0;
        public const double R3ShareB = 80.0 / 1280.0;

        // Guide ch.6 "EXP Spending": Hack Day 1 / Hack Day 2 completed. The game stores no such flag.
        public enum HackDayPhase { Unknown, BeforeHd1, AfterHd1, AfterHd2 }

        // Guide ch.6: post-HD1 E/M up to 3M/1M power, post-HD2 doubled to 6M/2M.
        public static bool EmFloor(HackDayPhase hd, out double energyPower, out double magicPower)
        {
            energyPower = hd == HackDayPhase.AfterHd2 ? 6e6 : 3e6;
            magicPower = hd == HackDayPhase.AfterHd2 ? 2e6 : 1e6;
            return hd == HackDayPhase.AfterHd1 || hd == HackDayPhase.AfterHd2;
        }

        // Cap/bars count as "in ratio" at this fraction of what the floor's power implies.
        private const double FloorBandTolerance = 0.75;

        /// <param name="spent">EXP-space spent per stat, order ePow,eCap,eBar,mPow,mCap,mBar;
        /// PositiveInfinity for a stat that cannot be bought (locked, gated, at the hard cap).</param>
        public static bool EmFloorReached(HackDayPhase hd, double[] spent, double ePowUnit, double mPowUnit, Targets t)
        {
            double ePow, mPow;
            if (!EmFloor(hd, out ePow, out mPow)) return false;
            double eNeed = ePow * ePowUnit;
            double mNeed = mPow * mPowUnit;
            return spent[0] >= eNeed && spent[1] >= FloorBandTolerance * eNeed * t.ShareC / t.ShareP
                && spent[2] >= FloorBandTolerance * eNeed * t.ShareB / t.ShareP
                && spent[3] >= mNeed && spent[4] >= FloorBandTolerance * mNeed * t.ShareC / t.ShareP
                && spent[5] >= FloorBandTolerance * mNeed * t.ShareB / t.ShareP;
        }

        // Guide Rich Jerks (attack/defense boost, % of the permanent stat-boost shop) milestones, or 0:
        // ch.3 "After getting CCoD from T5 ... buy 1k% Rich Jerks", ch.4 CBlock 2 Prep "buy 10k%",
        // ch.4 Evil Prep (T6v4 defeated) "100k Rich Jerks".
        public static double RichJerksTargetPct(int chapter, bool t5Beaten, int t6VersionsBeaten)
        {
            if (t6VersionsBeaten >= 4) return 100000;
            if (chapter >= 4) return 10000;
            if (t5Beaten) return 1000;
            return 0;
        }

        // Same phase, with the R3 pool carved out of the E/M pools (poolR 0..1).
        public static Targets WithR3Pool(Targets t, double poolR, string phase)
        {
            t.PoolE *= 1.0 - poolR;
            t.PoolM *= 1.0 - poolR;
            t.PoolR = poolR;
            t.Phase = phase;
            return t;
        }

        /// <summary>E/M ratio for the chapter, then the ch.6 Hack Day split on top of it.</summary>
        /// <param name="emFloorReached">EmFloorReached() for the current hack-day phase.</param>
        public static Targets For(int chapter, bool t5Beaten, int t6VersionsBeaten, bool magicUnlocked,
            HackDayPhase hd, bool emFloorReached)
        {
            Targets t = For(chapter, t5Beaten, t6VersionsBeaten, magicUnlocked);
            switch (hd)
            {
                case HackDayPhase.BeforeHd1:
                    return WithR3Pool(t, 1.0, "R3 only until HD1 (4:150k:1)");
                case HackDayPhase.AfterHd1:
                case HackDayPhase.AfterHd2:
                    string hdName = hd == HackDayPhase.AfterHd1 ? "HD1" : "HD2";
                    return emFloorReached
                        ? WithR3Pool(t, 1.0, $"R3 only after {hdName} E/M floor (4:150k:1)")
                        : WithR3Pool(t, 0.0, $"E/M floor after {hdName}, {t.Phase}");
            }
            return t;
        }

        // EXP split for a target E:M VALUE ratio of r:1 (see conversion 2 in the class note).
        private static double PoolFromValueRatio(double r) => r / (r + 3.0);

        /// <param name="chapter">ProgressionAnalyzer.Chapter (titan-kill chapter, 1..8; 0 = unknown).</param>
        /// <param name="t5Beaten">T5 killed — the guide's CBlock1 gate inside chapter 3.</param>
        /// <param name="t6VersionsBeaten">Highest T6 version actually BEATEN, 0 = none
        /// (ZoneHelpers.TitanVersionsBeaten(5)). NOT the version selected in the zone: `titan6Version`
        /// is the difficulty dropdown and never moves on its own.</param>
        /// <param name="magicUnlocked">Magic resource available (all-time highestBoss >= 37).</param>
        public static Targets For(int chapter, bool t5Beaten, int t6VersionsBeaten, bool magicUnlocked)
        {
            // Unknown chapter: the mid-game ratio is the safest default — it is what the guide uses
            // for the longest stretch (ch.3 through ch.6).
            if (chapter <= 0)
                chapter = 4;

            double shP, shC, shB;
            string pcb;
            if (chapter <= 2) { shP = 150; shC = 150; shB = 80; pcb = "1:37.5k:1"; }
            else if (chapter <= 6) { shP = 750; shC = 640; shB = 320; pcb = "5:160k:4"; }
            else { shP = 600; shC = 600; shB = 80; pcb = "4:150k:1"; }

            // Ch.1-2 and pre-T5 ch.3: "almost all your EXP should go into Energy, only invest in
            // Magic for Ygg auto-activations" — those Ygg cap buys are a manual, one-off call, so
            // the balancer treats the phase as energy-only rather than reserving a share for it.
            if (!magicUnlocked)
                return Build(1.0, shP, shC, shB, $"energy only ({pcb})");
            if (chapter <= 2)
                return Build(1.0, shP, shC, shB, $"ch.1-2 energy only ({pcb})");
            if (chapter == 3 && !t5Beaten)
                return Build(1.0, shP, shC, shB, $"pre-T5 energy only ({pcb})");
            if (chapter == 3)
                return Build(PoolFromValueRatio(5), shP, shC, shB, $"post-T5 5:1 E:M ({pcb})");

            // Guide ch.4 "EXP Spending", verbatim: 3:1 base, "After T6v2, focus Magic to get up to a
            // 2:1 E:M ratio", "After T6v4 accs and BB NGU Ygg/EXP, return to 3:1". The T6 VERSION is
            // the whole trigger.
            //
            // There used to be a second trigger here, a "CBlock2 done" proxy reading 24HR challenge
            // completions >= 3. It was wrong twice over (user-caught 2026-09-12): the guide's EXP
            // section never names CBlock2 as the trigger, and CBlock2 is the block that explicitly
            // leaves the 24H challenges UNfinished ("Finish all challenges aside from the last 5 NoTM
            // and 24 H challenges"), so the count it read was not a proxy for it either. Its only
            // effect was to hand out 2:1 to players still on T6 v1.
            if (t6VersionsBeaten >= 2 && t6VersionsBeaten < 4)
                return Build(PoolFromValueRatio(2), shP, shC, shB, $"post-CBlock2 2:1 E:M ({pcb})");

            return Build(PoolFromValueRatio(3), shP, shC, shB, $"3:1 E:M ({pcb})");
        }
    }
}
