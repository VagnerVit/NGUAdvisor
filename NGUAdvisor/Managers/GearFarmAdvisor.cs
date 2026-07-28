using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // Gear-farm advisor (Farm Gear Zones): find zones whose droppable EQUIPMENT is not yet
    // level-100 (each drop merges +1 toward the permanent item-max bonus) and rank them by
    // time-to-cap at the CURRENT drop chance; when no zone caps inside the time budget, report
    // the drop chance that would.
    //
    // The roll table is extracted VERBATIM from the game's LootDrop.zoneNDrop functions
    // (scratchpad extract-geardrops.js against the decomp): each roll is
    //   P(per kill) = min(Base + Chance * dcFactor, Cap), then 1-of-Span outcomes
    // where dcFactor = lootFactor() for Normal zones and lootFactorRooted() = lootFactor^(1/3)
    // for Evil+ zones, and the roll fires only for its enemy-type branch (Normal/Boss/any).
    // Consumable IDs ride along in some pools (junk cases count toward Span — that's why Span
    // is stored, not items.Length) and are filtered out at runtime via itemInfo.type[id] <= 5,
    // the same equipment test SavedSettings uses. Items with no roll here (guaranteed early
    // drops, quest/titan specials like the Buster of the Exile, dead rolls like item 66 in
    // zones 5/7 whose in-game chance multiplies a zeroed variable) are deliberately absent:
    // they have no farmable rate.
    //
    // Rate model mirrors BoostFarmAdvisor: kill cadence ~equal across one-shottable zones
    // (respawn ~4.5s -> ~800 kills/h), enemy-type mix ~77% normal / ~10% boss. Only zones the
    // character one-shots (attack >= OPower) and has boss-unlocked compete.
    public static class GearFarmAdvisor
    {
        private class Roll
        {
            public double Chance;          // per-kill chance scale on the DC factor
            public double Base;            // flat component (rare: pendant rolls)
            public double Cap = 1.0;       // the game's Mathf.Min ceiling on the roll
            public int Span = 1;           // switch outcomes the roll splits into
            public bool Boss;              // fires on boss kills only
            public bool Normal;            // fires on normal-enemy kills only
            public int[] Items;
        }

        private const double KillsPerHour = 800.0;
        private const double NormalShare = 0.77;
        private const double BossShare = 0.10;
        // A zone is "worth farming now" if its slowest uncapped item finishes inside this budget
        // (same hours-scale ruling as the quest capstone hold: forced farm time is cheap).
        private const double TargetHours = 3.0;

        private static readonly Dictionary<int, Roll[]> Table = new Dictionary<int, Roll[]>
        {
            { 0, new[] {
                new Roll { Chance = 0.25, Span = 1, Normal = true, Items = new[] { 75 } },
                new Roll { Chance = 0.15, Span = 3, Normal = true, Items = new[] { 1, 14, 27 } } } },
            { 1, new[] {
                new Roll { Chance = 0.15, Span = 3, Normal = true, Items = new[] { 1, 14, 27 } },
                new Roll { Chance = 0.65, Span = 7, Boss = true, Items = new[] { 40, 41, 42, 43, 44, 45, 46 } },
                new Roll { Chance = 0.1, Span = 1, Boss = true, Items = new[] { 77 } } } },
            { 2, new[] {
                new Roll { Chance = 0.008, Span = 1, Items = new[] { 135 } },
                new Roll { Chance = 0.12, Span = 3, Normal = true, Items = new[] { 1, 14, 27 } },
                new Roll { Chance = 0.08, Span = 3, Normal = true, Items = new[] { 2, 15, 28 } },
                new Roll { Chance = 0.5, Span = 7, Boss = true, Items = new[] { 47, 48, 49, 50, 51, 52, 53 } },
                new Roll { Chance = 0.013, Span = 1, Items = new[] { 432 } } } },
            { 3, new[] {
                new Roll { Chance = 0.13, Span = 3, Normal = true, Items = new[] { 1, 14, 27 } },
                new Roll { Chance = 0.12, Span = 3, Normal = true, Items = new[] { 2, 15, 28 } },
                new Roll { Chance = 0.75, Span = 9, Boss = true, Items = new[] { 54, 55, 56, 57, 58, 59, 60, 61, 53 } },
                new Roll { Chance = 0.0125, Span = 1, Items = new[] { 433 } } } },
            { 4, new[] {
                new Roll { Chance = 0.08, Span = 3, Normal = true, Items = new[] { 3, 16, 29 } },
                new Roll { Chance = 0.08, Span = 3, Normal = true, Items = new[] { 2, 15, 28 } },
                new Roll { Chance = 0.003, Span = 1, Boss = true, Items = new[] { 66 } },
                new Roll { Chance = 0.01, Span = 1, Boss = true, Items = new[] { 67 } },
                new Roll { Chance = 0.01, Span = 1, Boss = true, Items = new[] { 172 } },
                new Roll { Chance = 0.4, Span = 1, Boss = true, Items = new[] { 53 } },
                new Roll { Chance = 0.01, Span = 1, Items = new[] { 434 } } } },
            { 5, new[] {
                new Roll { Chance = 0.015, Span = 3, Normal = true, Items = new[] { 3, 16, 29 } },
                new Roll { Chance = 0.06, Span = 3, Normal = true, Items = new[] { 2, 15, 28 } },
                new Roll { Chance = 0.4, Span = 8, Boss = true, Items = new[] { 68, 69, 70, 71, 72, 73, 74, 53 } },
                new Roll { Chance = 0.007, Span = 1, Items = new[] { 435 } } } },
            { 7, new[] {
                new Roll { Chance = 0.03, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 3, 16, 29 } },
                new Roll { Chance = 0.03, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 4, 17, 30 } },
                new Roll { Chance = 0.3, Span = 7, Boss = true, Items = new[] { 85, 86, 87, 88, 89, 90, 91 } },
                new Roll { Chance = 0.005, Span = 1, Items = new[] { 436 } } } },
            { 9, new[] {
                new Roll { Chance = 0.07, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 4, 17, 30 } },
                new Roll { Chance = 0.07, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 5, 18, 31 } },
                new Roll { Chance = 0.32, Span = 7, Boss = true, Items = new[] { 95, 96, 97, 98, 99, 100, 101 } },
                new Roll { Chance = 0.005, Span = 1, Items = new[] { 437 } } } },
            { 10, new[] {
                new Roll { Chance = 0.06, Cap = 0.2, Span = 3, Normal = true, Items = new[] { 4, 17, 30 } },
                new Roll { Chance = 0.06, Cap = 0.2, Span = 3, Normal = true, Items = new[] { 5, 18, 31 } },
                new Roll { Chance = 0.3, Span = 7, Boss = true, Items = new[] { 103, 104, 105, 106, 107, 108, 109 } },
                new Roll { Chance = 0.0015, Span = 1, Boss = true, Items = new[] { 110 } },
                new Roll { Chance = 0.002, Span = 1, Boss = true, Items = new[] { 66 } },
                new Roll { Chance = 0.0045, Span = 1, Items = new[] { 438 } } } },
            { 12, new[] {
                new Roll { Chance = 0.03, Cap = 0.25, Span = 3, Normal = true, Items = new[] { 5, 18, 31 } },
                new Roll { Chance = 0.03, Cap = 0.25, Span = 3, Normal = true, Items = new[] { 6, 19, 32 } },
                new Roll { Chance = 0.2, Span = 5, Boss = true, Items = new[] { 122, 123, 124, 125, 126 } },
                new Roll { Chance = 0.0015, Span = 1, Boss = true, Items = new[] { 127 } },
                new Roll { Chance = 0.0025, Span = 1, Boss = true, Items = new[] { 66 } },
                new Roll { Chance = 0.004, Span = 1, Items = new[] { 439 } } } },
            { 13, new[] {
                new Roll { Chance = 0.011, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 6, 19, 32 } },
                new Roll { Chance = 0.011, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 7, 20, 33 } },
                new Roll { Chance = 0.08, Span = 5, Boss = true, Items = new[] { 130, 131, 132, 133, 134 } },
                new Roll { Chance = 0.01, Span = 1, Boss = true, Items = new[] { 76 } },
                new Roll { Chance = 0.002, Span = 1, Items = new[] { 440 } } } },
            { 15, new[] {
                new Roll { Chance = 0.0035, Cap = 0.25, Span = 3, Normal = true, Items = new[] { 6, 19, 32 } },
                new Roll { Chance = 0.0035, Cap = 0.25, Span = 3, Normal = true, Items = new[] { 7, 20, 33 } },
                new Roll { Chance = 0.01, Span = 5, Boss = true, Items = new[] { 143, 144, 145, 146, 147 } },
                new Roll { Chance = 0.0002, Span = 1, Boss = true, Items = new[] { 148 } },
                new Roll { Chance = 0.006, Span = 1, Boss = true, Items = new[] { 76 } },
                new Roll { Chance = 0.0002, Span = 1, Items = new[] { 441 } } } },
            { 17, new[] {
                new Roll { Chance = 0.001, Cap = 0.2, Span = 3, Normal = true, Items = new[] { 7, 20, 33 } },
                new Roll { Chance = 0.001, Cap = 0.2, Span = 3, Normal = true, Items = new[] { 8, 21, 34 } },
                new Roll { Chance = 0.00006, Cap = 0.05, Span = 5, Normal = true, Items = new[] { 164, 165, 166, 167, 168 } },
                new Roll { Chance = 0.00018, Cap = 0.15, Span = 5, Boss = true, Items = new[] { 164, 165, 166, 167, 168 } },
                new Roll { Chance = 0.0005, Cap = 0.1, Span = 1, Boss = true, Items = new[] { 67 } },
                new Roll { Chance = 0.00001, Cap = 0.01, Span = 1, Boss = true, Items = new[] { 128 } },
                new Roll { Chance = 0.0001, Cap = 0.01, Span = 1, Boss = true, Items = new[] { 94 } },
                new Roll { Chance = 0.00005, Cap = 0.01, Span = 1, Boss = true, Items = new[] { 163 } },
                new Roll { Chance = 0.000012, Cap = 0.03, Span = 1, Items = new[] { 442 } } } },
            { 18, new[] {
                new Roll { Chance = 0.00012, Cap = 0.2, Span = 3, Normal = true, Items = new[] { 8, 21, 34 } },
                new Roll { Chance = 0.00012, Cap = 0.2, Span = 3, Normal = true, Items = new[] { 9, 22, 35 } },
                new Roll { Chance = 0.00003, Cap = 0.04, Span = 5, Normal = true, Items = new[] { 173, 174, 175, 176, 177 } },
                new Roll { Chance = 0.00009, Cap = 0.1, Span = 5, Boss = true, Items = new[] { 173, 174, 175, 176, 177 } },
                new Roll { Chance = 0.00007, Cap = 0.01, Span = 1, Boss = true, Items = new[] { 94 } },
                new Roll { Chance = 0.00003, Cap = 0.01, Span = 1, Boss = true, Items = new[] { 163 } },
                new Roll { Chance = 0.000007, Cap = 0.01, Span = 1, Boss = true, Items = new[] { 128 } },
                new Roll { Chance = 0.000001, Cap = 0.005, Span = 1, Boss = true, Items = new[] { 178 } },
                new Roll { Chance = 0.000006, Cap = 0.02, Span = 1, Items = new[] { 443 } } } },
            { 20, new[] {
                new Roll { Chance = 0.00055, Cap = 0.1, Span = 3, Normal = true, Items = new[] { 8, 21, 34 } },
                new Roll { Chance = 0.00055, Cap = 0.1, Span = 3, Normal = true, Items = new[] { 9, 22, 35 } },
                new Roll { Chance = 0.00018, Cap = 0.08, Span = 5, Normal = true, Items = new[] { 221, 222, 223, 224, 225 } },
                new Roll { Chance = 0.00055, Cap = 0.12, Span = 5, Boss = true, Items = new[] { 221, 222, 223, 224, 225 } },
                new Roll { Chance = 0.00018, Cap = 0.12, Span = 2, Boss = true, Items = new[] { 226, 227 } },
                new Roll { Chance = 1E-9, Base = 0.001, Cap = 0.01, Span = 1, Boss = true, Items = new[] { 142 } },
                new Roll { Chance = 0.00008, Cap = 0.016, Span = 1, Items = new[] { 444 } } } },
            { 21, new[] {
                new Roll { Chance = 0.00012, Cap = 0.1, Span = 3, Normal = true, Items = new[] { 8, 21, 34 } },
                new Roll { Chance = 0.00012, Cap = 0.1, Span = 3, Normal = true, Items = new[] { 9, 22, 35 } },
                new Roll { Chance = 0.00007, Cap = 0.08, Span = 7, Normal = true, Items = new[] { 213, 214, 215, 216, 217, 218, 219 } },
                new Roll { Chance = 0.00021, Cap = 0.12, Span = 7, Boss = true, Items = new[] { 213, 214, 215, 216, 217, 218, 219 } },
                new Roll { Chance = 0.000018, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 220 } },
                new Roll { Chance = 1E-10, Base = 0.0015, Cap = 0.015, Span = 1, Boss = true, Items = new[] { 142 } },
                new Roll { Chance = 0.00002, Cap = 0.011, Span = 1, Items = new[] { 445 } } } },
            { 22, new[] {
                new Roll { Chance = 0.0001, Cap = 0.08, Span = 3, Normal = true, Items = new[] { 9, 22, 35 } },
                new Roll { Chance = 0.0001, Cap = 0.06, Span = 3, Normal = true, Items = new[] { 10, 23, 36 } },
                new Roll { Chance = 0.00003, Cap = 0.08, Span = 6, Normal = true, Items = new[] { 231, 232, 233, 234, 235, 236 } },
                new Roll { Chance = 0.0001, Cap = 0.12, Span = 6, Boss = true, Items = new[] { 231, 232, 233, 234, 235, 236 } },
                new Roll { Chance = 2E-11, Base = 0.0015, Cap = 0.02, Span = 1, Boss = true, Items = new[] { 142 } },
                new Roll { Chance = 0.000012, Cap = 0.013, Span = 1, Items = new[] { 446 } } } },
            { 24, new[] {
                new Roll { Chance = 0.00005, Cap = 0.07, Span = 3, Normal = true, Items = new[] { 10, 23, 36 } },
                new Roll { Chance = 0.00005, Cap = 0.07, Span = 3, Normal = true, Items = new[] { 11, 24, 37 } },
                new Roll { Chance = 0.000015, Cap = 0.04, Span = 7, Normal = true, Items = new[] { 251, 252, 253, 254, 255, 256, 257 } },
                new Roll { Chance = 0.00005, Cap = 0.12, Span = 7, Boss = true, Items = new[] { 251, 252, 253, 254, 255, 256, 257 } },
                new Roll { Chance = 0.00005, Cap = 0.03, Span = 1, Boss = true, Items = new[] { 142 } },
                new Roll { Chance = 0.000012, Cap = 0.03, Span = 1, Boss = true, Items = new[] { 128 } },
                new Roll { Chance = 0.000006, Cap = 0.017, Span = 1, Items = new[] { 447 } } } },
            { 25, new[] {
                new Roll { Chance = 0.00003, Cap = 0.08, Span = 3, Normal = true, Items = new[] { 10, 23, 36 } },
                new Roll { Chance = 0.00003, Cap = 0.08, Span = 3, Normal = true, Items = new[] { 11, 24, 37 } },
                new Roll { Chance = 0.000011, Cap = 0.04, Span = 7, Normal = true, Items = new[] { 258, 259, 260, 261, 262, 263, 264 } },
                new Roll { Chance = 0.000035, Cap = 0.12, Span = 7, Boss = true, Items = new[] { 258, 259, 260, 261, 262, 263, 264 } },
                new Roll { Chance = 0.000035, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 142 } },
                new Roll { Chance = 0.00001, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 128 } },
                new Roll { Chance = 0.000014, Cap = 0.017, Span = 1, Items = new[] { 448 } } } },
            { 27, new[] {
                new Roll { Chance = 0.000022, Cap = 0.09, Span = 3, Normal = true, Items = new[] { 10, 23, 36 } },
                new Roll { Chance = 0.000022, Cap = 0.09, Span = 3, Normal = true, Items = new[] { 11, 24, 37 } },
                new Roll { Chance = 0.000009, Cap = 0.04, Span = 7, Normal = true, Items = new[] { 301, 302, 303, 304, 305, 306, 307 } },
                new Roll { Chance = 0.000025, Cap = 0.12, Span = 7, Boss = true, Items = new[] { 301, 302, 303, 304, 305, 306, 307 } },
                new Roll { Chance = 0.000025, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 142 } },
                new Roll { Chance = 0.000006, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 128 } },
                new Roll { Chance = 0.000004, Cap = 0.017, Span = 1, Items = new[] { 449 } } } },
            { 28, new[] {
                new Roll { Chance = 0.000018, Cap = 0.1, Span = 3, Normal = true, Items = new[] { 11, 24, 37 } },
                new Roll { Chance = 0.000018, Cap = 0.1, Span = 3, Normal = true, Items = new[] { 12, 25, 38 } },
                new Roll { Chance = 0.000007, Cap = 0.04, Span = 7, Normal = true, Items = new[] { 308, 309, 310, 311, 312, 313, 314 } },
                new Roll { Chance = 0.000021, Cap = 0.12, Span = 7, Boss = true, Items = new[] { 308, 309, 310, 311, 312, 313, 314 } },
                new Roll { Chance = 0.000021, Cap = 0.08, Span = 1, Boss = true, Items = new[] { 142 } },
                new Roll { Chance = 0.000007, Cap = 0.08, Span = 1, Boss = true, Items = new[] { 128 } },
                new Roll { Chance = 0.0000025, Cap = 0.017, Span = 1, Items = new[] { 450 } } } },
            { 29, new[] {
                new Roll { Chance = 0.000015, Cap = 0.1, Span = 3, Normal = true, Items = new[] { 11, 24, 37 } },
                new Roll { Chance = 0.000015, Cap = 0.1, Span = 3, Normal = true, Items = new[] { 12, 25, 38 } },
                new Roll { Chance = 0.0000055, Cap = 0.04, Span = 7, Normal = true, Items = new[] { 315, 316, 317, 318, 319, 320, 321 } },
                new Roll { Chance = 0.000018, Cap = 0.12, Span = 7, Boss = true, Items = new[] { 315, 316, 317, 318, 319, 320, 321 } },
                new Roll { Chance = 0.000018, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 142 } },
                new Roll { Chance = 0.0000055, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 128 } },
                new Roll { Chance = 0.000002, Cap = 0.017, Span = 1, Items = new[] { 451 } } } },
            { 31, new[] {
                new Roll { Chance = 6E-7, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 11, 24, 37 } },
                new Roll { Chance = 6E-7, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 12, 25, 38 } },
                new Roll { Chance = 2E-7, Cap = 0.05, Span = 7, Normal = true, Items = new[] { 345, 346, 347, 348, 349, 350, 351 } },
                new Roll { Chance = 6E-7, Cap = 0.15, Span = 7, Boss = true, Items = new[] { 345, 346, 347, 348, 349, 350, 351 } },
                new Roll { Chance = 0.0000012, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 170 } },
                new Roll { Chance = 4E-7, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 169 } },
                new Roll { Chance = 8E-8, Cap = 0.017, Span = 1, Items = new[] { 452 } } } },
            { 32, new[] {
                new Roll { Chance = 4E-7, Cap = 0.1, Span = 3, Normal = true, Items = new[] { 12, 25, 38 } },
                new Roll { Chance = 4E-7, Cap = 0.1, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 1.5E-7, Cap = 0.05, Span = 7, Normal = true, Items = new[] { 352, 353, 354, 355, 356, 357, 358 } },
                new Roll { Chance = 4.5E-7, Cap = 0.15, Span = 7, Boss = true, Items = new[] { 352, 353, 354, 355, 356, 357, 358 } },
                new Roll { Chance = 4.5E-7, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 229 } },
                new Roll { Chance = 1.5E-7, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 230 } } } },
            { 33, new[] {
                new Roll { Chance = 2.5E-7, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 12, 25, 38 } },
                new Roll { Chance = 2.5E-7, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 1E-7, Cap = 0.04, Span = 7, Normal = true, Items = new[] { 359, 360, 361, 362, 363, 364, 365 } },
                new Roll { Chance = 2E-8, Cap = 0.12, Span = 1, Normal = true, Items = new[] { 366 } },
                new Roll { Chance = 3E-7, Cap = 0.15, Span = 7, Boss = true, Items = new[] { 359, 360, 361, 362, 363, 364, 365 } },
                new Roll { Chance = 0.000001, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 229 } },
                new Roll { Chance = 6E-8, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 366 } },
                new Roll { Chance = 3E-7, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 230 } } } },
            { 35, new[] {
                new Roll { Chance = 1E-7, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 12, 25, 38 } },
                new Roll { Chance = 1E-7, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 4E-8, Cap = 0.04, Span = 8, Normal = true, Items = new[] { 392, 393, 394, 395, 396, 397, 398, 399 } },
                new Roll { Chance = 1.2E-7, Cap = 0.15, Span = 8, Boss = true, Items = new[] { 392, 393, 394, 395, 396, 397, 398, 399 } },
                new Roll { Chance = 4E-7, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 229 } },
                new Roll { Chance = 1.2E-7, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 230 } } } },
            { 36, new[] {
                new Roll { Chance = 6E-8, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 6E-8, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 2.5E-8, Cap = 0.04, Span = 8, Normal = true, Items = new[] { 400, 401, 402, 403, 404, 405, 406, 407 } },
                new Roll { Chance = 8E-8, Cap = 0.15, Span = 8, Boss = true, Items = new[] { 400, 401, 402, 403, 404, 405, 406, 407 } },
                new Roll { Chance = 2.5E-7, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 229 } },
                new Roll { Chance = 8E-8, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 230 } } } },
            { 37, new[] {
                new Roll { Chance = 4E-8, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 4E-8, Cap = 0.15, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 1.6E-8, Cap = 0.04, Span = 8, Normal = true, Items = new[] { 408, 409, 410, 411, 412, 413, 414, 415 } },
                new Roll { Chance = 5E-8, Cap = 0.15, Span = 8, Boss = true, Items = new[] { 408, 409, 410, 411, 412, 413, 414, 415 } },
                new Roll { Chance = 1.6E-7, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 229 } },
                new Roll { Chance = 6E-8, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 230 } } } },
            { 39, new[] {
                new Roll { Chance = 2.5E-8, Cap = 0.16, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 2.5E-8, Cap = 0.16, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 1E-8, Cap = 0.04, Span = 8, Normal = true, Items = new[] { 453, 454, 455, 456, 457, 458, 459, 460 } },
                new Roll { Chance = 3E-8, Cap = 0.15, Span = 8, Boss = true, Items = new[] { 453, 454, 455, 456, 457, 458, 459, 460 } },
                new Roll { Chance = 1E-7, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 295 } },
                new Roll { Chance = 4E-8, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 296 } } } },
            { 40, new[] {
                new Roll { Chance = 2E-8, Cap = 0.17, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 2E-8, Cap = 0.17, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 8E-9, Cap = 0.05, Span = 8, Normal = true, Items = new[] { 496, 497, 498, 499, 500, 501, 502, 503 } },
                new Roll { Chance = 2.4E-8, Cap = 0.15, Span = 8, Boss = true, Items = new[] { 496, 497, 498, 499, 500, 501, 502, 503 } },
                new Roll { Chance = 8E-8, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 295 } },
                new Roll { Chance = 3E-8, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 296 } } } },
            { 41, new[] {
                new Roll { Chance = 1.6E-8, Cap = 0.17, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 1.6E-8, Cap = 0.17, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 6E-9, Cap = 0.05, Span = 8, Normal = true, Items = new[] { 461, 462, 463, 464, 465, 466, 467, 468 } },
                new Roll { Chance = 1.8E-8, Cap = 0.15, Span = 8, Boss = true, Items = new[] { 461, 462, 463, 464, 465, 466, 467, 468 } },
                new Roll { Chance = 6E-8, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 295 } },
                new Roll { Chance = 2.4E-8, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 296 } } } },
            { 43, new[] {
                new Roll { Chance = 1E-8, Cap = 0.17, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 1E-8, Cap = 0.17, Span = 3, Normal = true, Items = new[] { 13, 26, 39 } },
                new Roll { Chance = 4E-9, Cap = 0.05, Span = 8, Normal = true, Items = new[] { 507, 508, 509, 510, 511, 512, 513, 514 } },
                new Roll { Chance = 1.2E-8, Cap = 0.15, Span = 8, Boss = true, Items = new[] { 507, 508, 509, 510, 511, 512, 513, 514 } },
                new Roll { Chance = 4E-8, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 295 } },
                new Roll { Chance = 1.8E-8, Cap = 0.12, Span = 1, Boss = true, Items = new[] { 296 } } } },

        };

        public class ZonePlan
        {
            public int Zone;
            public string ZoneName;
            public List<int> MissingItems = new List<int>();
            public double HoursToCap;      // slowest missing item at current DC
            public double ReqLootFactor;   // lootFactor that brings HoursToCap inside TargetHours (0 = already there, -1 = no DC can)
            public bool Viable;            // HoursToCap <= TargetHours
        }

        public class Verdict
        {
            public bool Known;
            public ZonePlan Best;              // best viable zone (min hours), null if none
            public ZonePlan Nearest;           // best non-viable fallback for the "need X% DC" line
            public string Text;
        }

        private static bool IsEquipment(int id)
        {
            try { return id >= 0 && id <= Consts.MAX_GEAR_ID && (int)Main.Character.itemInfo.type[id] <= 5; }
            catch { return false; }
        }

        // Drops still needed to cap: 100 - highest owned level (a fresh drop is level 1; each
        // merge is +1). Unowned items need the full 100.
        private static int DropsNeeded(int id)
        {
            try
            {
                var slot = LoadoutManager.FindItemSlot(id);
                if (slot == null) return 100;
                return Math.Max(1, 100 - slot.level);
            }
            catch { return 100; }
        }

        // Per-hour drop rate of one roll at the given (already zone-adjusted) DC factor.
        private static double RollRate(Roll r, double dcFactor)
        {
            double p = Math.Min(r.Base + r.Chance * dcFactor, r.Cap) / r.Span;
            double share = r.Boss ? BossShare : r.Normal ? NormalShare : 1.0;
            return KillsPerHour * share * p;
        }

        // Zones whose LootDrop function uses lootFactorRooted() (Evil+): the DC factor is the
        // cube root of lootFactor. Extracted alongside the roll constants.
        private static readonly HashSet<int> RootedZones = new HashSet<int>
            { 20, 21, 22, 24, 25, 27, 28, 29, 31, 32, 33, 35, 36, 37, 39, 40, 41, 43 };

        private static double DcFactor(int zone, double lootFactor)
            => RootedZones.Contains(zone) ? Math.Pow(lootFactor, 1.0 / 3.0) : lootFactor;

        // Hours until every missing item in the zone is capped, at the given lootFactor.
        private static double HoursToCap(int zone, Roll[] rolls, List<int> missing, double lootFactor)
        {
            double dc = DcFactor(zone, lootFactor);
            double worst = 0;
            foreach (var id in missing)
            {
                double perHour = 0;
                foreach (var r in rolls)
                    if (Array.IndexOf(r.Items, id) >= 0)
                        perHour += RollRate(r, dc);
                if (perHour <= 0) return double.PositiveInfinity;
                worst = Math.Max(worst, DropsNeeded(id) / perHour);
            }
            return worst;
        }

        public static Verdict Analyze()
        {
            var v = new Verdict();
            try
            {
                var c = Main.Character;
                if (c == null) return v;

                double lootFactor = c.lootFactor();
                double attack = ZoneStatHelper.EffectiveAdvAttack();
                var il = c.inventory.itemList;

                var plans = new List<ZonePlan>();
                foreach (var kv in Table)
                {
                    int zone = kv.Key;
                    try
                    {
                        if (ZoneHelpers.ZoneIsTitan(zone)) continue;
                        if (zone >= ZoneHelpers.ZoneUnlocks.Length || c.bossID <= ZoneHelpers.ZoneUnlocks[zone]) continue;
                        // Only one-shottable zones farm at full cadence (same gate as the boost advisor).
                        if (ZoneStatHelper.UserOverrides != null && ZoneStatHelper.UserOverrides.TryGetValue(zone, out var st))
                            if (st.OPower > 0 && attack < st.OPower) continue;

                        var missing = new List<int>();
                        foreach (var id in kv.Value.SelectMany(r => r.Items).Distinct())
                        {
                            if (!IsEquipment(id)) continue;
                            if (id >= il.itemMaxxed.Count || il.itemMaxxed[id]) continue;
                            bool filtered = false;
                            try { filtered = id < il.itemFiltered.Count && il.itemFiltered[id]; } catch { }
                            if (filtered) continue;   // a loot-filtered item never drops
                            missing.Add(id);
                        }
                        if (missing.Count == 0) continue;

                        var plan = new ZonePlan
                        {
                            Zone = zone,
                            ZoneName = ZoneHelpers.ZoneList.TryGetValue(zone, out var n) ? n : $"Zone {zone}",
                            MissingItems = missing,
                            HoursToCap = HoursToCap(zone, kv.Value, missing, lootFactor)
                        };
                        plan.Viable = plan.HoursToCap <= TargetHours;

                        // Required lootFactor for the budget: rates are monotonic in DC, so binary
                        // search; if even a huge DC can't cap in budget (roll caps), report -1.
                        if (plan.Viable) plan.ReqLootFactor = 0;
                        else if (double.IsInfinity(HoursToCap(zone, kv.Value, missing, lootFactor * 1e9))
                            || HoursToCap(zone, kv.Value, missing, lootFactor * 1e9) > TargetHours)
                            plan.ReqLootFactor = -1;
                        else
                        {
                            double lo = lootFactor, hi = lootFactor * 1e9;
                            for (int i = 0; i < 60; i++)
                            {
                                double mid = Math.Sqrt(lo * hi);   // geometric: the range spans decades
                                if (HoursToCap(zone, kv.Value, missing, mid) <= TargetHours) hi = mid;
                                else lo = mid;
                            }
                            plan.ReqLootFactor = hi;
                        }
                        plans.Add(plan);
                    }
                    catch { }
                }

                v.Known = true;
                v.Best = plans.Where(p => p.Viable).OrderBy(p => p.HoursToCap).FirstOrDefault();
                v.Nearest = plans.Where(p => !p.Viable && p.ReqLootFactor > 0)
                    .OrderBy(p => p.ReqLootFactor).FirstOrDefault();
                if (v.Best != null)
                {
                    v.Text = $"Gear farm: {v.Best.ZoneName} — {v.Best.MissingItems.Count} item(s) uncapped, ~{FmtHours(v.Best.HoursToCap)} to cap";
                }
                else if (v.Nearest != null)
                {
                    v.Text = $"No gear zone caps within {TargetHours:0}h — closest is {v.Nearest.ZoneName} (needs ~{v.Nearest.ReqLootFactor * 100:#,0}% drop chance)";
                }
                else if (plans.Count > 0)
                {
                    // Uncapped gear exists but the game's per-roll chance CAPS keep every zone past
                    // the budget no matter the DC — honest answer: show the floor; partial levels
                    // accumulated by the boost-farm routing shrink it over time.
                    var fastest = plans.OrderBy(p => p.HoursToCap).First();
                    v.Text = $"Gear uncapped in {plans.Count} zone(s), but roll caps hold them past {TargetHours:0}h — fastest is {fastest.ZoneName} (~{FmtHours(fastest.HoursToCap)})";
                }
                else
                {
                    v.Text = "All farmable zone gear is capped";
                }
                return v;
            }
            catch (Exception e) { Main.LogDebug($"GearFarmAdvisor: {e.Message}"); return v; }
        }

        private static string FmtHours(double h)
            => h >= 1 ? $"{h:0.#}h" : $"{h * 60:0}m";
    }
}
