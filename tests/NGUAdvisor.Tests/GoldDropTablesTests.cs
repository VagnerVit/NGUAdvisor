using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    // Shape guards for the base-gold table transcribed from decomp LootDrop.zone{N}Drop. The advisor uses it
    // to decide whether a gold snipe can beat what the Time Machine already runs on, so a typo'd magnitude
    // would either stop a worthwhile snipe or keep swapping gear for nothing — silently, in both directions.
    public class GoldDropTablesTests
    {
        [Fact]
        public void Every_zone_up_to_the_traitor_is_covered()
        {
            for (int zone = 0; zone <= 45; zone++)
                Assert.True(GoldDropTables.HasZone(zone), $"zone {zone} missing from the base-gold table");
        }

        [Fact]
        public void Bosses_never_drop_less_than_regular_enemies()
        {
            for (int zone = 0; zone <= 45; zone++)
                Assert.True(GoldDropTables.BaseGold(zone, true) >= GoldDropTables.BaseGold(zone, false),
                    $"zone {zone}: boss gold below normal gold");
        }

        // BEAST and THE TRAITOR call no goldDrop at all in the decomp — they must never look like a gold
        // target, or the advisor would swap to gold gear for a kill that banks nothing.
        [Fact]
        public void Beast_and_traitor_drop_no_gold()
        {
            Assert.Equal(0, GoldDropTables.BaseGold(44, true));
            Assert.Equal(0, GoldDropTables.BaseGold(45, true));
        }

        // Titan zones store one value for the whole zone (all versions drop the same), so the boss-only
        // lookup a snipe uses must not fall back to a different number.
        [Theory]
        [InlineData(6, 250000.0)]
        [InlineData(19, 5e6)]
        [InlineData(30, 1e12)]
        [InlineData(42, 1.5e17)]
        public void Titan_zones_report_the_same_gold_for_both_enemy_kinds(int zone, double expected)
        {
            Assert.Equal(expected, GoldDropTables.BaseGold(zone, false));
            Assert.Equal(expected, GoldDropTables.BaseGold(zone, true));
        }

        // The whole point of the gate: the highest titan out-drops the best non-titan zone by orders of
        // magnitude, which is why a banked titan drop makes zone sniping pointless.
        [Fact]
        public void Highest_titan_out_drops_the_highest_regular_zone()
        {
            Assert.True(GoldDropTables.BaseGold(42, true) > GoldDropTables.BaseGold(41, true));
        }

        // Titan gold is NOT monotone in titan index, and a picker that assumed it was banked the smaller
        // drop (user-reported): T2 (zone 8) pays 400 000, T3 (zone 11) only 300 000. The live factors
        // PredictedDrop applies — totalGoldbonus, the gold-gear ratio — are the same for every candidate,
        // so this table ordering IS the ranking AdvisorApply.BestGoldTitan produces.
        [Fact]
        public void Titan2_out_drops_titan3()
        {
            Assert.True(GoldDropTables.BaseGold(8, true) > GoldDropTables.BaseGold(11, true));
        }

        // Ranking the titan zones by gold must not reproduce their index order, otherwise "pick the highest"
        // and "pick the most profitable" would be the same choice and the bug would be untestable.
        [Fact]
        public void Highest_titan_index_is_not_always_the_most_gold()
        {
            int[] titanZones = { 6, 8, 11, 14, 16, 19, 23, 26, 30, 34, 38, 42, 44, 45 };
            int bestByGold = -1, bestByIndex = -1;
            double best = -1;
            for (int i = 0; i < titanZones.Length; i++)
            {
                double gold = GoldDropTables.BaseGold(titanZones[i], true);
                bestByIndex = i;
                if (gold > best) { best = gold; bestByGold = i; }
            }
            Assert.NotEqual(bestByIndex, bestByGold);   // T12 (zone 42) pays; THE TRAITOR (zone 45) pays nothing
            Assert.Equal(42, titanZones[bestByGold]);
        }

        [Fact]
        public void Unknown_zones_report_no_data_rather_than_zero_gold_certainty()
        {
            Assert.False(GoldDropTables.HasZone(1000));   // ITOPOD: no gold path in the decomp
            Assert.Equal(0, GoldDropTables.BaseGold(1000, true));
        }
    }
}
