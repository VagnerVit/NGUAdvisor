using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // Per-zone base gold, extracted 1:1 from decomp LootDrop.zone{N}Drop — the `goldDrop(x)` argument for
    // each enemy type. The actual drop is `x * Random.Range(4f, 5f) * totalGoldbonus()`
    // (LootDrop.goldDrop). Game-independent on purpose: this half is unit-tested, GoldDropAdvisor holds
    // everything that needs the live character.
    public static class GoldDropTables
    {
        private struct ZoneGold
        {
            public readonly double Normal;
            public readonly double Boss;

            public ZoneGold(double normal, double boss)
            {
                Normal = normal;
                Boss = boss;
            }
        }

        // Titan zones (6, 8, 11, 14, 16, 19, 23, 26, 30, 34, 38, 42) carry ONE value for the whole zone,
        // shared by every version of the titan — stored in both fields so a boss-only caller gets it too.
        // Zones 44/45 (BEAST, THE TRAITOR) call no goldDrop at all: they drop NO gold.
        private static readonly Dictionary<int, ZoneGold> ZoneBase = new Dictionary<int, ZoneGold>
        {
            { 0,  new ZoneGold(100, 200) },
            { 1,  new ZoneGold(400, 600) },
            { 2,  new ZoneGold(900, 1500) },
            { 3,  new ZoneGold(2200, 3000) },
            { 4,  new ZoneGold(4000, 6000) },
            { 5,  new ZoneGold(10000, 16000) },
            { 6,  new ZoneGold(250000, 250000) },
            { 7,  new ZoneGold(30000, 40000) },
            { 8,  new ZoneGold(400000, 400000) },
            { 9,  new ZoneGold(65000, 90000) },
            { 10, new ZoneGold(100000, 140000) },
            { 11, new ZoneGold(300000, 300000) },
            { 12, new ZoneGold(180000, 240000) },
            { 13, new ZoneGold(220000, 290000) },
            { 14, new ZoneGold(500000, 500000) },
            { 15, new ZoneGold(220000, 400000) },
            { 16, new ZoneGold(1000000, 1000000) },
            { 17, new ZoneGold(220000, 500000) },
            { 18, new ZoneGold(280000, 600000) },
            { 19, new ZoneGold(5e6, 5e6) },
            { 20, new ZoneGold(600000, 900000) },
            { 21, new ZoneGold(2.8e8, 6e8) },
            { 22, new ZoneGold(1e9, 5e9) },
            { 23, new ZoneGold(1e10, 1e10) },
            { 24, new ZoneGold(5e9, 1e10) },
            { 25, new ZoneGold(1e10, 3e10) },
            { 26, new ZoneGold(1e11, 1e11) },
            { 27, new ZoneGold(3e10, 5e10) },
            { 28, new ZoneGold(6e10, 1e11) },
            { 29, new ZoneGold(1e11, 1.3e11) },
            { 30, new ZoneGold(1e12, 1e12) },
            { 31, new ZoneGold(2e11, 3e11) },
            { 32, new ZoneGold(1.5e14, 1.7e14) },
            { 33, new ZoneGold(3e14, 4e14) },
            { 34, new ZoneGold(2e15, 2e15) },
            { 35, new ZoneGold(1.2e15, 2e15) },
            { 36, new ZoneGold(2.5e15, 3e15) },
            { 37, new ZoneGold(5e15, 6e15) },
            { 38, new ZoneGold(2e16, 2e16) },
            { 39, new ZoneGold(1e16, 1.2e16) },
            { 40, new ZoneGold(2e16, 2.4e16) },
            { 41, new ZoneGold(4e16, 5e16) },
            { 42, new ZoneGold(1.5e17, 1.5e17) },
            { 43, new ZoneGold(8e16, 1.6e17) },
            { 44, new ZoneGold(0, 0) },
            { 45, new ZoneGold(0, 0) },
        };

        // Base gold of one kill in `zone`. A gold snipe kills bosses (CombatManager runs boss-only while
        // IsCurrentlyGoldSniping), so bossOnly callers get the boss value. Unknown zones return 0 =
        // "no data", and every caller treats that as "do not block the snipe".
        public static double BaseGold(int zone, bool bossOnly)
        {
            ZoneGold gold;
            if (!ZoneBase.TryGetValue(zone, out gold))
                return 0;
            return bossOnly && gold.Boss > 0 ? gold.Boss : gold.Normal;
        }

        public static bool HasZone(int zone) => ZoneBase.ContainsKey(zone);
    }
}
