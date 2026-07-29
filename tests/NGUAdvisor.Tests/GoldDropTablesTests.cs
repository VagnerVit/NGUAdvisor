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

        [Fact]
        public void Unknown_zones_report_no_data_rather_than_zero_gold_certainty()
        {
            Assert.False(GoldDropTables.HasZone(1000));   // ITOPOD: no gold path in the decomp
            Assert.Equal(0, GoldDropTables.BaseGold(1000, true));
        }
    }
}
