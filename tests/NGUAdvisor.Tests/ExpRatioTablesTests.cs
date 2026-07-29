using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class ExpRatioTablesTests
    {
        private static ExpRatioTables.Targets Ch(int chapter) =>
            ExpRatioTables.For(chapter, true, 1, false, true);

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void EarlyChapters_BuyEnergyOnly(int chapter)
        {
            ExpRatioTables.Targets t = Ch(chapter);
            Assert.Equal(1.0, t.PoolE);
            Assert.Equal(0.0, t.PoolM);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void EarlyChapters_UseGuidesOneTo37kRatio(int chapter)
        {
            ExpRatioTables.Targets t = Ch(chapter);
            // 1:37.5k:1 units -> 150 : 37500/250 : 80 EXP
            Assert.Equal(150.0 / 380, t.ShareP, 10);
            Assert.Equal(150.0 / 380, t.ShareC, 10);
            Assert.Equal(80.0 / 380, t.ShareB, 10);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(6)]
        public void MidChapters_Use5To160kTo4Ratio(int chapter)
        {
            ExpRatioTables.Targets t = Ch(chapter);
            Assert.Equal(750.0 / 1710, t.ShareP, 10);
            Assert.Equal(640.0 / 1710, t.ShareC, 10);
            Assert.Equal(320.0 / 1710, t.ShareB, 10);
        }

        [Fact]
        public void Chapter7_SwitchesTo4To150kTo1Ratio()
        {
            ExpRatioTables.Targets t = Ch(7);
            Assert.Equal(600.0 / 1280, t.ShareP, 10);
            Assert.Equal(600.0 / 1280, t.ShareC, 10);
            Assert.Equal(80.0 / 1280, t.ShareB, 10);
        }

        [Fact]
        public void Chapter3_BeforeT5_IsEnergyOnly()
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(3, false, 1, false, true);
            Assert.Equal(1.0, t.PoolE);
        }

        [Fact]
        public void Chapter3_AfterT5_Targets5To1Values()
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(3, true, 1, false, true);
            Assert.Equal(0.625, t.PoolE, 10);
            Assert.Equal(0.375, t.PoolM, 10);
        }

        [Fact]
        public void Chapter4_Targets3To1Values_AsAnEvenExpSplit()
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(4, true, 1, false, true);
            Assert.Equal(0.5, t.PoolE, 10);
            Assert.Equal(0.5, t.PoolM, 10);
        }

        [Theory]
        [InlineData(2, false)]   // T6v2 reached
        [InlineData(1, true)]    // ...or the CBlock2 proxy
        [InlineData(3, false)]
        public void PostCBlock2_Targets2To1Values(int t6Version, bool cblock2Done)
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(4, true, t6Version, cblock2Done, true);
            Assert.Equal(0.4, t.PoolE, 10);
            Assert.Equal(0.6, t.PoolM, 10);
        }

        [Fact]
        public void T6v4_RevertsTo3To1Values()
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(4, true, 4, true, true);
            Assert.Equal(0.5, t.PoolE, 10);
        }

        [Fact]
        public void MagicLocked_ForcesEnergyOnly_EvenLate()
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(5, true, 3, true, false);
            Assert.Equal(1.0, t.PoolE);
            Assert.Equal(0.0, t.PoolM);
        }

        [Fact]
        public void UnknownChapter_FallsBackToMidGameRatio()
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(0, true, 1, false, true);
            Assert.Equal(750.0 / 1710, t.ShareP, 10);
            Assert.Equal(0.5, t.PoolE, 10);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(7)]
        public void SharesAlwaysSumToOne(int chapter)
        {
            ExpRatioTables.Targets t = Ch(chapter);
            Assert.Equal(1.0, t.ShareP + t.ShareC + t.ShareB, 10);
            Assert.Equal(1.0, t.PoolE + t.PoolM, 10);
        }
    }
}
