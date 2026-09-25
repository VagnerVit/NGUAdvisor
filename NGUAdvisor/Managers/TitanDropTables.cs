namespace NGUAdvisor.Managers
{
    // What each titan can drop, extracted from the game's own LootDrop.zone{N}Drop(Enemy) bodies
    // (every makeTitanLoot / makeTitanLevelledLoot id in the method, deduped). Kept in this
    // Unity-free class (pure int[][] data) for the same reason as TitanTables and GoldDropTables:
    // it can be shape-tested without loading the game.
    //
    // GearFarmAdvisor.Table deliberately holds only the NORMAL zones (it skips titan zones
    // outright), so it is not a source for this — a titan's loot lives nowhere else in the repo.
    //
    // The ids are raw and include BOOSTS (ids 1-39, see docs/ITEM-IDS.md): T2 drops boost ids
    // 4-7/17-20/30-33 alongside its gear. Filtering those out needs the live itemInfo.type read,
    // so it belongs to the caller (GearFarmAdvisor.WantedTitanNeedFactor), not to this table.
    //
    // T13/T14 (zones 44/45) have no zone{N}Drop method in the decompile — their entries are empty,
    // which reads downstream as "nothing to farm here" rather than as a missing table.
    public static class TitanDropTables
    {
        // Indexed by titan index (0-based), matching ZoneHelpers.TitanZones.
        public static readonly int[][] Drops =
        {
            new[] { 53, 66, 78, 79, 80, 81, 82, 83, 84, 102 },                                              // T1  zone 6
            new[] { 4, 5, 6, 7, 17, 18, 19, 20, 30, 31, 32, 33, 53, 66, 92, 93 },                           // T2  zone 8
            new[] { 7, 20, 33, 76, 111, 112, 113, 114, 115, 116, 117, 118, 197 },                           // T3  zone 11
            new[] { 53, 136, 137, 138, 139, 140, 141, 149 },                                                // T4  zone 14
            new[] { 76, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 163 },                  // T5  zone 16
            new[] { 142, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 292 },                 // T6  zone 19
            new[] { 170, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 294 },            // T7  zone 23
            new[] { 169, 170, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275, 276, 277, 288, 343 },  // T8  zone 26
            new[] { 169, 170, 322, 323, 324, 325, 326, 327, 328, 329, 330, 331, 332, 333, 334, 335, 336,
                    341, 342, 391 },                                                                        // T9  zone 30
            new[] { 229, 230, 373, 374, 375, 376, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386 },       // T10 zone 34
            new[] { 295, 296, 416, 417, 418, 419, 420, 421, 422, 423, 424, 425, 426, 427, 428, 429 },       // T11 zone 38
            new[] { 388, 389, 469, 470, 471, 472, 473, 474, 475, 476, 477, 478, 479, 483, 484, 489, 493 },  // T12 zone 42
            new int[0],                                                                                     // T13 zone 44
            new int[0],                                                                                     // T14 zone 45
        };

        public static int[] For(int titanIndex)
            => titanIndex >= 0 && titanIndex < Drops.Length ? Drops[titanIndex] : new int[0];

        // Rolls whose chance a drop-chance factor raises, per titan index (LootDrop.zone{N}Drop). Each
        // roll draws its own Random.value: P = min(Chance x dc, Cap), dc = lootFactor() for T1-T6 and
        // lootFactorRooted() for T7+ (Rooted). Left out on purpose: guaranteed drops, fixed-chance rolls
        // (T5's 1 % 159, T9's exile 341/336) and T1/T2's Wandoos roll, whose DC only picks the level of
        // a drop that always happens — none of them is something drop chance can buy.
        public enum TitanGate { None, UugRing, Waldo, AntiWaldo, Titan9Special }

        public struct TitanRoll
        {
            public double Chance, Cap;
            public int MinVersion;       // V2+/V3+/V4 rolls fire only for that titan version
            public TitanGate Gate;
            public int[] Items;
        }

        private static TitanRoll R(double chance, int id, int minVersion = 1, TitanGate gate = TitanGate.None)
            => new TitanRoll { Chance = chance, Cap = 1.0, MinVersion = minVersion, Gate = gate, Items = new[] { id } };

        private static TitanRoll R(double chance, int[] ids)
            => new TitanRoll { Chance = chance, Cap = 1.0, MinVersion = 1, Items = ids };

        // The rooted era caps every titan roll at 25 % (Mathf.Min(chance, 0.25f)).
        private static TitanRoll Q(double chance, int id, int minVersion = 1, TitanGate gate = TitanGate.None)
            => new TitanRoll { Chance = chance, Cap = 0.25, MinVersion = minVersion, Gate = gate, Items = new[] { id } };

        public static bool Rooted(int titanIndex) => titanIndex >= 6;

        public static readonly TitanRoll[][] Rolls =
        {
            // T1 zone6Drop, LootDrop.cs:730-864
            new[] { R(0.5, new[] { 78, 79, 80, 81, 82 }), R(0.15, 78), R(0.15, 79), R(0.15, 80), R(0.15, 81),
                    R(0.15, 82), R(0.15, 83), R(0.15, 84), R(0.1, 53) },
            // T2 zone8Drop, LootDrop.cs:980-1112
            new[] { R(0.1, 4), R(0.08, 5), R(0.05, 6), R(0.05, 7), R(0.1, 17), R(0.08, 18), R(0.05, 19), R(0.05, 20),
                    R(0.1, 30), R(0.08, 31), R(0.08, 32), R(0.05, 33), R(0.01, 93), R(0.1, 53) },
            // T3 zone11Drop, LootDrop.cs:1355-1490
            new[] { R(0.6, new[] { 111, 112, 113, 114, 115 }), R(0.1, 111), R(0.1, 112), R(0.1, 113), R(0.1, 114),
                    R(0.1, 115), R(0.25, new[] { 116, 117 }), R(0.1, 76), R(0.1, 33), R(0.1, 20), R(0.1, 7), R(0.02, 118) },
            // T4 zone14Drop, LootDrop.cs:1711-1778
            new[] { R(0.02, 136), R(0.02, 137), R(0.02, 138), R(0.02, 139), R(0.02, 140), R(0.02, 53),
                    R(0.001, 149, 1, TitanGate.UugRing) },
            // T5 zone16Drop, LootDrop.cs:1917-2069 (bigBoss5 branch)
            new[] { R(0.005, 150), R(0.005, 151), R(0.005, 152), R(0.005, 153), R(0.005, new[] { 159, 154 }),
                    R(0.005, 163), R(0.005, 155), R(0.005, 156), R(0.005, 157), R(0.005, 158), R(0.005, 76),
                    R(0.0001, 160, 1, TitanGate.Waldo), R(0.0001, 161, 1, TitanGate.AntiWaldo) },
            // T6 zone19Drop, LootDrop.cs:2341-2465
            new[] { R(0.0005, 184), R(0.0005, 185), R(0.0005, 186), R(0.0005, 187), R(0.0005, 188), R(0.0005, 142),
                    R(0.0002, 189), R(5E-05, 190, 2), R(2E-05, 191, 2), R(1E-05, 192, 3), R(5E-06, 193, 3),
                    R(2E-06, 194, 4), R(1E-06, 195, 4) },
            // T7 zone23Drop, LootDrop.cs:2917-3075
            new[] { Q(0.00035, 237), Q(0.00035, 238), Q(0.00035, 239), Q(0.00035, 240), Q(0.00035, 241),
                    Q(0.00023, 242), Q(0.00035, 243), Q(0.00035, 170), Q(0.00027, 244, 2), Q(0.00027, 245, 2),
                    Q(0.00022, 246, 3), Q(0.00022, 247, 3), Q(0.00017, 248, 4), Q(0.00017, 249, 4) },
            // T8 zone26Drop, LootDrop.cs:3347-3520
            new[] { Q(0.0001, 265), Q(0.0001, 266), Q(0.0001, 267), Q(0.0001, 268), Q(0.0001, 269), Q(0.0001, 270),
                    Q(0.0001, 271), Q(0.0001, 170), Q(0.0001, 169), Q(7.5E-05, 272, 2), Q(7.5E-05, 273, 2),
                    Q(6E-05, 274, 3), Q(6E-05, 275, 3), Q(4.5E-05, 276, 4), Q(4.5E-05, 277, 4) },
            // T9 zone30Drop, LootDrop.cs:3927-4104
            new[] { Q(2E-05, 322), Q(2E-05, 323), Q(2E-05, 324), Q(2E-05, 325), Q(2E-05, 326), Q(2E-05, 327),
                    Q(2E-05, 328), Q(1.5E-05, 170), Q(1.5E-05, 169), Q(1E-05, 329, 2), Q(1E-05, 330, 2),
                    Q(6E-06, 331, 3), Q(6E-06, 332, 3), Q(4E-06, 333, 4), Q(4E-06, 334, 4),
                    Q(1E-06, 342, 1, TitanGate.Titan9Special) },
            // T10 zone34Drop, LootDrop.cs:4503-4633
            new[] { Q(1E-06, 373), Q(1E-06, 374), Q(1E-06, 375), Q(1E-06, 376), Q(1E-06, 377), Q(1E-06, 378),
                    Q(1E-06, 379), Q(1E-06, 380), Q(1E-06, 229), Q(1E-06, 230), Q(6E-07, 381, 2), Q(6E-07, 382, 2),
                    Q(4E-07, 383, 3), Q(4E-07, 384, 3), Q(3E-07, 385, 4), Q(3E-07, 386, 4) },
            // T11 zone38Drop, LootDrop.cs:5025-5155
            new[] { Q(1E-07, 416), Q(1E-07, 417), Q(1E-07, 418), Q(1E-07, 419), Q(1E-07, 420), Q(1E-07, 421),
                    Q(1E-07, 422), Q(1E-07, 423), Q(1E-07, 295), Q(1E-07, 296), Q(6.5E-08, 424, 2), Q(6.5E-08, 425, 2),
                    Q(4E-08, 426, 3), Q(4E-08, 427, 3), Q(3E-08, 428, 4), Q(3E-08, 429, 4) },
            // T12 zone42Drop, LootDrop.cs:5547-5683
            new[] { Q(1.4E-08, 469), Q(1.4E-08, 470), Q(1.4E-08, 471), Q(1.4E-08, 472), Q(1.4E-08, 473),
                    Q(1.4E-08, 474), Q(1.4E-08, 475), Q(1.4E-08, 476), Q(1.4E-08, 388), Q(1.4E-08, 389),
                    Q(1.4E-08, 483), Q(1E-08, 477, 2), Q(1E-08, 489, 2), Q(8E-09, 478, 3), Q(8E-09, 493, 3),
                    Q(6E-09, 479, 4), Q(6E-09, 484, 4) },
            new TitanRoll[0],   // T13 zone44Drop: no titan loot
            new TitanRoll[0],   // T14 zone45Drop: no titan loot
        };

        public static TitanRoll[] RollsFor(int titanIndex)
            => titanIndex >= 0 && titanIndex < Rolls.Length ? Rolls[titanIndex] : new TitanRoll[0];

        // The drop-chance factor (in the titan's own domain) at which every roll passing `counts` caps.
        // 0 when no such roll exists — drop chance buys nothing from this titan.
        public static double NeedFactor(int titanIndex, int version, System.Func<TitanRoll, bool> counts)
        {
            double need = 0;
            foreach (TitanRoll r in RollsFor(titanIndex))
            {
                if (r.Chance <= 0 || r.MinVersion > version || !counts(r)) continue;
                need = System.Math.Max(need, r.Cap / r.Chance);
            }
            return need;
        }
    }
}
