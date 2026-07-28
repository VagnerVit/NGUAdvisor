namespace NGUAdvisor.Managers
{
    // Hand-extracted titan requirement tables. Kept in this Unity-free class (pure double[][][] data, no game or
    // Unity dependency) so they can be shape/monotonicity-tested (review finding #33) WITHOUT loading the game.
    // OptimizationAdvisor consumes them via its local TitanAk/TitanGuide aliases.
    public static class TitanTables
    {
        // Autokill attack/defense/HP-REGEN requirements per titan index (0-based) and version (1-4),
        // extracted from the game's autokillTitan{N}V{V}Achieved methods (reference/decomp-full/
        // AdventureController.cs). Regen (third column, 0 = no check) is a REAL gate from T4 up —
        // omitting it let the UI claim AK-ready while the game refused to fire. T4 additionally
        // needs a maxxed item 135 and T5 needs boss5Kills >= 3; T9+ can alternatively be unlocked
        // by kill counts. Those non-stat gates live in ZoneHelpers.AutokillAvailable — this table
        // is the stat path you can push toward.
        public static readonly double[][][] Ak =
        {
            new[] { new[] { 3000.0, 2500.0, 0.0 } },
            new[] { new[] { 9000.0, 7000.0, 0.0 } },
            new[] { new[] { 25000.0, 15000.0, 0.0 } },
            new[] { new[] { 8e5, 4e5, 1.4e4 } },
            new[] { new[] { 1.3e7, 7e6, 1.5e5 } },
            new[] { new[] { 2.5e9, 1.6e9, 2.5e7 }, new[] { 2.5e10, 1.6e10, 2.5e8 }, new[] { 2.5e11, 1.6e11, 2.5e9 }, new[] { 2.5e12, 1.6e12, 2.5e10 } },
            new[] { new[] { 5e14, 2.5e14, 5e12 }, new[] { 1e16, 5e15, 1e14 }, new[] { 2e17, 1e17, 2e15 }, new[] { 5e18, 2.5e18, 5e16 } },
            new[] { new[] { 5e18, 2.5e18, 5e16 }, new[] { 1e20, 5e19, 1e18 }, new[] { 2e21, 1e21, 2e19 }, new[] { 5e22, 2.5e22, 5e20 } },
            new[] { new[] { 1e23, 5e22, 1e21 }, new[] { 2e24, 1e24, 2e22 }, new[] { 4e25, 2e25, 4e23 }, new[] { 7.5e26, 3.7e26, 7.5e24 } },
            new[] { new[] { 4e28, 2e28, 4e26 }, new[] { 3.2e29, 1.6e29, 1.6e27 }, new[] { 2e30, 1e30, 1e28 }, new[] { 1e31, 5e30, 5e28 } },
            new[] { new[] { 1.8e31, 6e30, 1.2e29 }, new[] { 9e31, 3e31, 6e29 }, new[] { 3.6e32, 1.2e32, 2.5e30 }, new[] { 1.1e33, 3.6e32, 7.5e30 } },
            new[] { new[] { 3e33, 1e33, 2e31 }, new[] { 1.2e34, 4e33, 8e31 }, new[] { 3.6e34, 1.2e34, 2.4e32 }, new[] { 7.2e34, 2.4e34, 4.8e32 } },
        };

        // Guide-recommended kill-ladder stats per titan/version { manual atk, manual def, idle atk,
        // idle def } — the community guide's hand-tuned numbers (reference/ngu-guide/titan-list.md).
        // NOT derivable from Ak: the old 45%/80%-of-AK scalars were calibrated on T1 and
        // overstated Beast first-kill by ~60% and idle ~2x (user report). Idle 0/0 = the guide lists
        // no idle numbers (Walderp, Godmother, T10-T12): those are fought manually until AK, so the
        // ladder skips the idle stage. Manual numbers assume max move-cooldown items + Beast Mode on.
        // Walderp's manual is the FINAL form (first form is 800K/400K).
        public static readonly double[][][] Guide =
        {
            new[] { new[] { 1350.0, 1350.0, 2300.0, 2100.0 } },
            new[] { new[] { 5000.0, 4000.0, 6000.0, 5000.0 } },
            new[] { new[] { 1.4e4, 1.2e4, 2.2e4, 1.4e4 } },
            new[] { new[] { 4e5, 3e5, 6e5, 4e5 } },
            new[] { new[] { 4e6, 3e6, 0.0, 0.0 } },
            new[] { new[] { 7e8, 5e8, 1e9, 7e8 }, new[] { 7e9, 5e9, 1e10, 7e9 }, new[] { 7e10, 5e10, 1e11, 7e10 }, new[] { 7e11, 5e11, 1e12, 7e11 } },
            new[] { new[] { 1.4e14, 9e13, 3e14, 2e14 }, new[] { 3.2e15, 1.6e15, 6e15, 4e15 }, new[] { 5.5e16, 3.5e16, 1.2e17, 8e16 }, new[] { 1.3e18, 7.5e17, 2.5e18, 1.5e18 } },
            new[] { new[] { 1.7e18, 7e17, 0.0, 0.0 }, new[] { 3.9e19, 1.5e19, 0.0, 0.0 }, new[] { 6.6e20, 3.5e20, 0.0, 0.0 }, new[] { 1.5e22, 6.4e21, 0.0, 0.0 } },
            new[] { new[] { 2.5e22, 1.3e22, 6e22, 3e22 }, new[] { 3.8e23, 1.6e23, 1.5e24, 7e23 }, new[] { 7.5e24, 3.5e24, 3e25, 1.5e25 }, new[] { 2e26, 1e26, 7e26, 3.5e26 } },
            new[] { new[] { 1.55e28, 3e27, 0.0, 0.0 }, new[] { 1.3e29, 3.6e28, 0.0, 0.0 }, new[] { 7.8e29, 1.6e29, 0.0, 0.0 }, new[] { 4e30, 9e29, 0.0, 0.0 } },
            new[] { new[] { 1.1e31, 4e30, 0.0, 0.0 }, new[] { 6e31, 1.8e31, 0.0, 0.0 }, new[] { 2.5e32, 8.2e31, 0.0, 0.0 }, new[] { 7.5e32, 2.5e32, 0.0, 0.0 } },
            new[] { new[] { 1.47e33, 4.7e32, 0.0, 0.0 }, new[] { 5.6e33, 2.1e33, 0.0, 0.0 }, new[] { 2.1e34, 6e33, 0.0, 0.0 }, new[] { 4.1e34, 1e34, 0.0, 0.0 } },
        };
    }
}
