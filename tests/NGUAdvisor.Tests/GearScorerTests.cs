using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class GearScorerTests
    {
        private static GearScorer.Item Item(string stat, double value, bool weapon = false)
        {
            var it = new GearScorer.Item { IsWeapon = weapon };
            it.Stats[stat] = value;
            return it;
        }

        [Theory]
        [InlineData("Respawn", 0.0)]
        [InlineData("Power", 0.0)]
        [InlineData("Toughness", 0.0)]
        [InlineData("Drop Chance", 100.0)]
        [InlineData("Raw NGU Speed", 100.0)]
        public void Base_zero_stats_are_the_three_additive_ones(string stat, double expected) =>
            Assert.Equal(expected, GearScorer.BaseValue(stat));

        // The game floors the respawn factor at 0.2 (decomp AdventureController.respawnTime), so a
        // gear Respawn total past 80% buys NOTHING. Scoring it linearly told the optimizer that the
        // 81st point was worth as much as the first.
        [Fact]
        public void Respawn_total_is_capped_at_the_games_floor()
        {
            var stats = new[] { "Respawn" };
            var atCap = new[] { Item("Respawn", 80) };
            var overCap = new[] { Item("Respawn", 80), Item("Respawn", 40) };

            Assert.Equal(GearScorer.ScoreRaw(atCap, stats, null, 100),
                         GearScorer.ScoreRaw(overCap, stats, null, 100));
        }

        [Fact]
        public void Respawn_below_the_cap_still_scores_linearly()
        {
            var stats = new[] { "Respawn" };
            double half = GearScorer.ScoreRaw(new[] { Item("Respawn", 40) }, stats, null, 100);
            double full = GearScorer.ScoreRaw(new[] { Item("Respawn", 80) }, stats, null, 100);
            Assert.Equal(2.0, full / half, 9);
        }

        // The cap is a property of the STAT (a game rule), not of an objective, so it must not leak
        // into stats the game does not floor.
        [Fact]
        public void Other_stats_are_not_capped()
        {
            var stats = new[] { "Drop Chance" };
            double small = GearScorer.ScoreRaw(new[] { Item("Drop Chance", 500) }, stats, null, 100);
            double big = GearScorer.ScoreRaw(new[] { Item("Drop Chance", 1500) }, stats, null, 100);
            Assert.True(big > small);
        }

        // Offhand rule: the FIRST weapon is the mainhand and every later weapon is discounted, and
        // the flag flips on the first weapon even when it does not carry the scored stat.
        [Fact]
        public void Second_weapon_is_discounted_by_the_offhand_percent()
        {
            var stats = new[] { "Power" };
            var vals = GearScorer.GetRawVals(
                new[] { Item("Power", 100, weapon: true), Item("Power", 100, weapon: true) }, stats, 50);
            Assert.Equal(150.0, vals[0]);
        }

        [Fact]
        public void A_lone_offhand_stat_carrier_after_a_bare_weapon_is_still_discounted()
        {
            var stats = new[] { "Power" };
            var bareWeapon = new GearScorer.Item { IsWeapon = true };
            var vals = GearScorer.GetRawVals(
                new[] { bareWeapon, Item("Power", 100, weapon: true) }, stats, 50);
            Assert.Equal(50.0, vals[0]);
        }
    }
}
