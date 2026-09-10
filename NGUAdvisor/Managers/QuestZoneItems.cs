using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // Static game data for QuestManager's capstone hold, in its own Unity-free file so the test
    // project can link it (the same reason TitanTables and GoldDropTables live apart from their
    // managers) -- QuestManager itself reaches Main.Character and cannot be linked headless.
    public static class QuestZoneItems
    {
        // EQUIPMENT item ids droppable per MAJOR-QUEST zone. CapstoneHold is the only consumer: it
        // holds a finished quest while any of these is still un-maxed.
        //
        // THE EXTRACTION RULE:
        //   ids = { every makeLoot(id) AND makeLevelledLoot(id, ...) in LootDrop.zone<N>Drop }
        //         filtered to type[id] in { Head, Chest, Legs, Boots, Weapon, Accessory }
        //         [DECOMP] ItemNameDesc.cs (type[] assignments), part enum
        //
        // The old table broke that rule twice, and both ways defeated the feature:
        //
        //   1. It captured only makeLevelledLoot(...) and MISSED EVERY makeLoot(...) gear id. Zone 9
        //      held one id where the game drops eight; zones 2/5/12/13 each missed a whole boss set,
        //      so the hold ended early or never started in half the zones it can run in.
        //   2. It included part.Misc ids -- 66, 339, 367, 369, 370. Misc items are never equipped, so
        //      the advisor never merges them (InventoryManager.cs:231-243), Equipment.level stays 0,
        //      and itemMaxxed is only set from checkItemTransform at level >= 100
        //      [DECOMP] InventoryController.cs:2374. itemMaxxed[id] could therefore NEVER become
        //      true, and because CapstoneHold breaks on the FIRST un-maxed id, zones 12 and 13 could
        //      never finish a hold on gear at all -- they always burned the full 180-minute budget.
        //
        // ONLY THESE TEN ZONES CAN EVER BE ASKED FOR: curQuestZone() maps questID -> zone and returns
        // exactly { 1, 2, 5, 9, 12, 13, 15, 20, 21, 22 } or -100
        // [DECOMP] BeastQuestController.cs:997-1013. The 24 further rows the old table carried were
        // unreachable, so they are gone rather than left to rot un-exercised.
        //
        // Every row below was re-extracted from the shipped Assembly-CSharp and cites its source
        // line; QuestZoneItemsTests pins it to keep it from drifting.
        public static readonly Dictionary<int, int[]> ZoneItems = new Dictionary<int, int[]>
        {
            // [DECOMP] LootDrop.cs:158  zone1Drop   (quest 278)
            { 1, new[] { 40,41,42,43,44,45,46,77 } },
            // [DECOMP] LootDrop.cs:252  zone2Drop   (quest 281) -- 53 was the missing makeLoot id
            { 2, new[] { 47,48,49,50,51,52,53,135,432 } },
            // [DECOMP] LootDrop.cs:614  zone5Drop   (quest 283) -- 68-74 were missing; 66 was Misc
            { 5, new[] { 53,68,69,70,71,72,73,74,435 } },
            // [DECOMP] LootDrop.cs:1114 zone9Drop   (quest 279) -- 95-101 were missing; row held only 437
            { 9, new[] { 95,96,97,98,99,100,101,437 } },
            // [DECOMP] LootDrop.cs:1492 zone12Drop  (quest 282) -- 122-126 were missing; 66 was Misc
            { 12, new[] { 122,123,124,125,126,127,439 } },
            // [DECOMP] LootDrop.cs:1602 zone13Drop  (quest 287) -- 76,130-134 were missing; 339 was Misc
            { 13, new[] { 76,130,131,132,133,134,440 } },
            // [DECOMP] LootDrop.cs:1780 zone15Drop  (quest 285) -- 367 (a cooking item) was Misc
            { 15, new[] { 76,143,144,145,146,147,148,441 } },
            // [DECOMP] LootDrop.cs:2467 zone20Drop  (quest 280) -- 369 (a cooking item) was Misc
            { 20, new[] { 142,221,222,223,224,225,226,227,444 } },
            // [DECOMP] LootDrop.cs:2624 zone21Drop  (quest 284) -- already correct
            { 21, new[] { 142,213,214,215,216,217,218,219,220,445 } },
            // [DECOMP] LootDrop.cs:2771 zone22Drop  (quest 286) -- 370 (a cooking item) was Misc
            { 22, new[] { 142,231,232,233,234,235,236,446 } },
        };
    }
}
