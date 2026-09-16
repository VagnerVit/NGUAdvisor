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
    // so it belongs to the caller (ZoneHelpers.TitanHasWantedDrops), not to this table.
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
    }
}
