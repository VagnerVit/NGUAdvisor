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

        [Fact]
        public void RollTableCoversEveryTitanIndex()
            => Assert.Equal(TitanDropTables.Drops.Length, TitanDropTables.Rolls.Length);

        // A roll can only hand out what the titan's drop list says it drops.
        [Fact]
        public void EveryRolledItemIsInTheTitansDropList()
        {
            for (int t = 0; t < TitanDropTables.Rolls.Length; t++)
                foreach (TitanDropTables.TitanRoll r in TitanDropTables.Rolls[t])
                    foreach (int id in r.Items)
                        Assert.Contains(id, TitanDropTables.Drops[t]);
        }

        // Rooted-era titans (T7+) cap every roll at 25 %; the unrooted era has no cap.
        [Fact]
        public void CapsFollowTheEra()
        {
            for (int t = 0; t < TitanDropTables.Rolls.Length; t++)
                foreach (TitanDropTables.TitanRoll r in TitanDropTables.Rolls[t])
                    Assert.Equal(TitanDropTables.Rooted(t) ? 0.25 : 1.0, r.Cap);
        }

        // T6's rarest rolls only exist on later versions: v1 caps at 1 / 0.0002 (item 189), v4 at 1 / 1e-6.
        [Theory]
        [InlineData(1, 5000)]
        [InlineData(2, 50000)]
        [InlineData(3, 200000)]
        [InlineData(4, 1000000)]
        public void NeedFactorTracksTheSpawningVersion(int version, double need)
            => Assert.Equal(need, TitanDropTables.NeedFactor(5, version, r => true), 6);

        [Fact]
        public void NothingCountedMeansDropChanceBuysNothing()
            => Assert.Equal(0, TitanDropTables.NeedFactor(5, 4, r => false));
    }
}
