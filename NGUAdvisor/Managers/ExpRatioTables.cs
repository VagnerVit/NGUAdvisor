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
            public string Phase;    // guide phase this came from, for the advisor readout
        }

        private static Targets Build(double poolE, double shP, double shC, double shB, string phase)
        {
            double sum = shP + shC + shB;
            return new Targets
            {
                PoolE = poolE,
                PoolM = 1.0 - poolE,
                ShareP = shP / sum,
                ShareC = shC / sum,
                ShareB = shB / sum,
                Phase = phase
            };
        }

        // EXP split for a target E:M VALUE ratio of r:1 (see conversion 2 in the class note).
        private static double PoolFromValueRatio(double r) => r / (r + 3.0);

        /// <param name="chapter">ProgressionAnalyzer.Chapter (titan-kill chapter, 1..8; 0 = unknown).</param>
        /// <param name="t5Beaten">T5 killed — the guide's CBlock1 gate inside chapter 3.</param>
        /// <param name="t6Version">ZoneHelpers.TitanVersion(5), i.e. version+1.</param>
        /// <param name="cblock2Done">CBlock2-done proxy (24HR challenge completions >= 3).</param>
        /// <param name="magicUnlocked">Magic resource available (all-time highestBoss >= 37).</param>
        public static Targets For(int chapter, bool t5Beaten, int t6Version, bool cblock2Done, bool magicUnlocked)
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

            // D4: post-CBlock2 / T6v2 the target value ratio drops to 2:1, reverting at T6v4.
            if ((t6Version >= 2 || cblock2Done) && t6Version < 4)
                return Build(PoolFromValueRatio(2), shP, shC, shB, $"post-CBlock2 2:1 E:M ({pcb})");

            return Build(PoolFromValueRatio(3), shP, shC, shB, $"3:1 E:M ({pcb})");
        }
    }
}
