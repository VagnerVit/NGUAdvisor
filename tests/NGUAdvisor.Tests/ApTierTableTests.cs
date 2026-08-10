using System.Collections.Generic;
using System.Linq;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class ApTierTableTests
    {
        [Fact]
        public void EveryItemHasANameATierAndARank()
        {
            foreach (var i in ApTierTable.Items)
            {
                Assert.False(string.IsNullOrWhiteSpace(i.Name));
                Assert.InRange(i.Tier, 0, 7);
                Assert.True(i.Rank >= 1);
            }
        }

        [Fact]
        public void RanksAreUniqueAndContiguousWithinEachTier()
        {
            foreach (var g in ApTierTable.Items.GroupBy(x => x.Tier))
            {
                var ranks = g.Select(x => x.Rank).OrderBy(x => x).ToList();
                Assert.Equal(ranks.Distinct().Count(), ranks.Count);
                Assert.Equal(Enumerable.Range(1, ranks.Count).ToList(), ranks);
            }
        }

        [Fact]
        public void TiersZeroThroughSevenAreAllPresent()
        {
            var tiers = ApTierTable.Items.Select(x => x.Tier).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(Enumerable.Range(0, 8).ToList(), tiers);
        }

        [Fact]
        public void ShopIdAndHeartRowsCarryAPositiveKeyAndRepeatableRowsDoNot()
        {
            foreach (var i in ApTierTable.Items)
            {
                if (i.Source == ApSource.Repeatable) Assert.Equal(0, i.Key);
                else Assert.True(i.Key > 0, $"{i.Name} has no key");
            }
        }

        [Fact]
        public void KeysAreUniqueWithinEachSource()
        {
            foreach (var g in ApTierTable.Items.Where(x => x.Source != ApSource.Repeatable)
                                               .GroupBy(x => x.Source))
            {
                var keys = g.Select(x => x.Key).ToList();
                Assert.Equal(keys.Distinct().Count(), keys.Count);
            }
        }

        [Fact]
        public void NextUnownedWalksTierThenRank()
        {
            var owned = new HashSet<string> { "ILF (improved loot filter)" };
            var next = ApTierTable.NextUnowned(i => owned.Contains(i.Name));
            Assert.Equal("Yellow Heart", next.Name);
        }

        [Fact]
        public void NextUnownedReturnsNullWhenEverythingIsOwned()
        {
            Assert.Null(ApTierTable.NextUnowned(_ => true));
        }

        [Fact]
        public void ARepeatableRowIsNeverConsideredOwnedSoItCannotBlockTheQueue()
        {
            // The caller reports Repeatable rows as not-owned; the table must still order them
            // normally rather than special-casing them out of the list.
            Assert.Contains(ApTierTable.Items, i => i.Source == ApSource.Repeatable);
            var all = ApTierTable.Unowned(_ => false).ToList();
            Assert.Equal(ApTierTable.Items.Count, all.Count);
        }

        [Fact]
        public void TheYellowHeartNoteRecordsItsDecompEvidence()
        {
            var yh = ApTierTable.Items.Single(i => i.Name == "Yellow Heart");
            Assert.Contains("129", yh.Note);
        }

        [Fact]
        public void EveryRowWhosePriceIsNotItsOwnKeyCarriesAnExplicitCostId()
        {
            // Ownership key and pricing key are different questions. A ShopId row answers both with the
            // same number, so it leaves CostId at 0 and the advisor falls back to Key. Hearts (keyed by
            // item id) and Repeatable rows (keyed by nothing) must name their pricing pod explicitly,
            // or they would silently show no price. This pins that invariant so a new heart or
            // repeatable row cannot be added without one.
            foreach (var i in ApTierTable.Items)
            {
                if (i.Source == ApSource.ShopId)
                    Assert.Equal(0, i.CostId);
                else
                    Assert.True(i.CostId > 0, $"{i.Name} has no CostId, so it would have no price");
            }
        }

        [Fact]
        public void ARowWithNoSourceGuidanceHasAnEmptyNote()
        {
            var accSlot1 = ApTierTable.Items.Single(i => i.Name == "Acc slot 1");
            Assert.Equal("", accSlot1.Note);
        }
    }
}
