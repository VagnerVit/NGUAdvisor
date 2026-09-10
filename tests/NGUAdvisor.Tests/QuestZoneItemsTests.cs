using System.Collections.Generic;
using System.Linq;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    // QuestZoneItems drives the capstone hold: a finished major quest is held while any of the zone's
    // gear is still un-maxed, so the free forced-farming time keeps producing merges.
    //
    // It broke the way every hand-transcribed decomp table breaks -- no detector under it -- and in
    // two directions at once:
    //   1. The extraction captured makeLevelledLoot(...) and missed every makeLoot(...) gear id. Five
    //      of the ten reachable rows were incomplete; zone 9 listed one id where the game drops
    //      eight, so the hold ended early or never started.
    //   2. It carried part.Misc ids, which can never be maxed (never equipped -> never merged ->
    //      level stays 0, and itemMaxxed needs level >= 100). The consumer breaks on the FIRST
    //      un-maxed id, so those rows pinned the hold for its whole 180-minute budget.
    //
    // The figures below are CAPTURED from a decompile of the shipped Assembly-CSharp, which lives
    // outside this repository; each row names the LootDrop.cs line it came from.
    public class QuestZoneItemsTests
    {
        // curQuestZone()'s switch returns exactly these, or -100.
        // [DECOMP] BeastQuestController.cs:997-1013
        private static readonly int[] ReachableZones = { 1, 2, 5, 9, 12, 13, 15, 20, 21, 22 };

        // Every makeLoot / makeLevelledLoot id in LootDrop.zone<N>Drop whose type[id] is an equipment
        // part (Head/Chest/Legs/Boots/Weapon/Accessory).
        public static IEnumerable<object[]> Expected => new[]
        {
            new object[] { 1,  new[] { 40,41,42,43,44,45,46,77 } },
            new object[] { 2,  new[] { 47,48,49,50,51,52,53,135,432 } },
            new object[] { 5,  new[] { 53,68,69,70,71,72,73,74,435 } },
            new object[] { 9,  new[] { 95,96,97,98,99,100,101,437 } },
            new object[] { 12, new[] { 122,123,124,125,126,127,439 } },
            new object[] { 13, new[] { 76,130,131,132,133,134,440 } },
            new object[] { 15, new[] { 76,143,144,145,146,147,148,441 } },
            new object[] { 20, new[] { 142,221,222,223,224,225,226,227,444 } },
            new object[] { 21, new[] { 142,213,214,215,216,217,218,219,220,445 } },
            new object[] { 22, new[] { 142,231,232,233,234,235,236,446 } },
        };

        [Theory]
        [MemberData(nameof(Expected))]
        public void Zone_row_matches_the_decomp_equipment_drops(int zone, int[] expected)
        {
            Assert.True(QuestZoneItems.ZoneItems.ContainsKey(zone),
                $"zone {zone} has no row, but curQuestZone() can return it");
            Assert.Equal(expected.OrderBy(i => i).ToArray(),
                QuestZoneItems.ZoneItems[zone].OrderBy(i => i).ToArray());
        }

        [Fact]
        public void The_table_holds_exactly_the_zones_curQuestZone_can_return()
        {
            Assert.Equal(ReachableZones.OrderBy(z => z).ToArray(),
                QuestZoneItems.ZoneItems.Keys.OrderBy(z => z).ToArray());
        }

        // The three cooking items (367 zone 15, 369 zone 20, 370 zone 22), the busted Wandoos copy
        // (66, zones 5 and 12) and the Buster of the Exile (339, zone 13) are part.Misc: the advisor
        // never merges them, so itemMaxxed can never be set and a hold on one never ends. Zones 12
        // and 13 listed a Misc id FIRST, so those two could never finish a hold on gear at all.
        [Theory]
        [InlineData(66)]
        [InlineData(339)]
        [InlineData(367)]
        [InlineData(369)]
        [InlineData(370)]
        public void No_row_carries_an_un_maxable_misc_id(int miscId)
        {
            int[] rows = QuestZoneItems.ZoneItems
                .Where(kv => kv.Value.Contains(miscId))
                .Select(kv => kv.Key)
                .ToArray();
            Assert.True(rows.Length == 0,
                $"Misc id {miscId} can never be maxed, but zone(s) {string.Join(",", rows)} hold for it");
        }

        // Ids 1-39 are BOOSTS and 278-287 are the quest items themselves; both drop in these zones
        // and neither is equipment, so neither belongs in a table about capping gear.
        [Fact]
        public void No_row_carries_a_boost_or_a_quest_item()
        {
            foreach (KeyValuePair<int, int[]> row in QuestZoneItems.ZoneItems)
                foreach (int id in row.Value)
                    Assert.True(id >= 40 && (id < 278 || id > 287),
                        $"zone {row.Key} holds for id {id}, which is not equipment");
        }
    }
}
