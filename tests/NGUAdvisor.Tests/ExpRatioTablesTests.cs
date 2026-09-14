using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class ExpRatioTablesTests
    {
        private static ExpRatioTables.Targets Ch(int chapter) =>
            ExpRatioTables.For(chapter, true, 1, true);

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
            ExpRatioTables.Targets t = ExpRatioTables.For(3, false, 1, true);
            Assert.Equal(1.0, t.PoolE);
        }

        [Fact]
        public void Chapter3_AfterT5_Targets5To1Values()
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(3, true, 1, true);
            Assert.Equal(0.625, t.PoolE, 10);
            Assert.Equal(0.375, t.PoolM, 10);
        }

        [Fact]
        public void Chapter4_Targets3To1Values_AsAnEvenExpSplit()
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(4, true, 1, true);
            Assert.Equal(0.5, t.PoolE, 10);
            Assert.Equal(0.5, t.PoolM, 10);
        }

        // Guide ch.4: "After T6v2, focus Magic to get up to a 2:1 E:M ratio".
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void T6v2ToV3_Targets2To1Values(int t6VersionsBeaten)
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(4, true, t6VersionsBeaten, true);
            Assert.Equal(0.4, t.PoolE, 10);
            Assert.Equal(0.6, t.PoolM, 10);
        }

        // Beating T6 v1 is what moves the CHAPTER to 4; it does not by itself move the E:M ratio.
        // Nor does anything else: the old "CBlock2 done" proxy handed 2:1 to players still on v1.
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void BeforeT6v2_StaysOn3To1Values(int t6VersionsBeaten)
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(4, true, t6VersionsBeaten, true);
            Assert.Equal(0.5, t.PoolE, 10);
        }

        [Fact]
        public void T6v4_RevertsTo3To1Values()
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(4, true, 4, true);
            Assert.Equal(0.5, t.PoolE, 10);
        }

        [Fact]
        public void MagicLocked_ForcesEnergyOnly_EvenLate()
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(5, true, 3, false);
            Assert.Equal(1.0, t.PoolE);
            Assert.Equal(0.0, t.PoolM);
        }

        [Fact]
        public void UnknownChapter_FallsBackToMidGameRatio()
        {
            ExpRatioTables.Targets t = ExpRatioTables.For(0, true, 1, true);
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
