using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class TitanDropTablesTests
    {
        [Fact]
        public void TableCoversEveryTitanIndex()
        {
            Assert.Equal(14, TitanDropTables.Drops.Length);
            foreach (var drops in TitanDropTables.Drops)
                Assert.NotNull(drops);
        }

        // T13/T14 have no zone{N}Drop method in the game — empty is the recorded truth, not a hole.
        [Fact]
        public void EveryTitanUpToT12HasDrops()
        {
            for (var i = 0; i < 12; i++)
                Assert.NotEmpty(TitanDropTables.Drops[i]);
            Assert.Empty(TitanDropTables.Drops[12]);
            Assert.Empty(TitanDropTables.Drops[13]);
        }

        [Fact]
        public void IdsAreSortedAndDeduped()
        {
            foreach (var drops in TitanDropTables.Drops)
                for (var i = 1; i < drops.Length; i++)
                    Assert.True(drops[i] > drops[i - 1], $"ids must ascend without duplicates, saw {drops[i - 1]} then {drops[i]}");
        }

        [Fact]
        public void ForClampsOutOfRangeIndexesInsteadOfThrowing()
        {
            Assert.Empty(TitanDropTables.For(-1));
            Assert.Empty(TitanDropTables.For(14));
            Assert.Equal(TitanDropTables.Drops[0], TitanDropTables.For(0));
        }
    }
}
