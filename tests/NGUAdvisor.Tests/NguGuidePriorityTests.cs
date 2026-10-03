using System.Collections.Generic;
using System.Linq;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class NguGuidePriorityTests
    {
        private static readonly int[] AllEnergy = Enumerable.Range(0, 9).ToArray();
        private static readonly int[] AllMagic = Enumerable.Range(0, 7).ToArray();

        private static System.Func<int, double> Ratios(Dictionary<int, double> r)
            => id => r.TryGetValue(id, out double v) ? v : 1.0;

        [Fact]
        public void NothingOverItsBarSplitsEnergyIntoAdvAndDropChance()
        {
            // The 2026-10-03 case: Augs/Wandoos/Gold rotated as "top two" while Adv-α/DC idled.
            var r = Ratios(new Dictionary<int, double> { { 0, 1.03 }, { 1, 1.03 }, { 3, 1.03 }, { 7, 1.02 } });
            Assert.Equal(new[] { 4, 6 }, NguGuidePriority.Hot(false, AllEnergy, r, null));
        }

        [Fact]
        public void NothingOverItsBarSplitsMagicIntoYggAndExp()
        {
            var r = Ratios(new Dictionary<int, double> { { 2, 1.5 }, { 3, 1.5 }, { 5, 1.04 } });
            Assert.Equal(new[] { 0, 1 }, NguGuidePriority.Hot(true, AllMagic, r, null));
        }

        [Fact]
        public void AugsWandoosAndPowerAlphaNeverRunHoweverHighTheyRate()
        {
            var r = Ratios(new Dictionary<int, double> { { 0, 3.0 }, { 1, 3.0 }, { 5, 3.0 } });
            var hot = NguGuidePriority.Hot(false, AllEnergy, r, null);
            var surplus = NguGuidePriority.Surplus(false, AllEnergy, r, hot);
            Assert.DoesNotContain(0, hot.Concat(surplus));
            Assert.DoesNotContain(1, hot.Concat(surplus));
            Assert.DoesNotContain(5, hot.Concat(surplus));
        }

        [Fact]
        public void HigherPriorityTierWinsOverABetterRatedLowerTier()
        {
            var r = Ratios(new Dictionary<int, double> { { 7, 1.06 }, { 8, 1.5 }, { 3, 2.0 } });
            var hot = NguGuidePriority.Hot(false, AllEnergy, r, null);
            Assert.Equal(new[] { 7 }, hot);
            Assert.Equal(new[] { 8, 3, 4, 6 }, NguGuidePriority.Surplus(false, AllEnergy, r, hot));
        }

        [Fact]
        public void GoldNeedsOnePointTwo()
        {
            Assert.Equal(new[] { 4, 6 }, NguGuidePriority.Hot(false, AllEnergy, Ratios(new Dictionary<int, double> { { 3, 1.19 } }), null));
            Assert.Equal(new[] { 3 }, NguGuidePriority.Hot(false, AllEnergy, Ratios(new Dictionary<int, double> { { 3, 1.21 } }), null));
        }

        [Fact]
        public void RespawnRunsBelowPointNineFiveBonusChange()
        {
            // Respawn ratio is old/new time: 0.94x time == 1/0.94 here.
            Assert.Equal(new[] { 2 }, NguGuidePriority.Hot(false, AllEnergy, Ratios(new Dictionary<int, double> { { 2, 1.0 / 0.94 } }), null));
            Assert.Equal(new[] { 4, 6 }, NguGuidePriority.Hot(false, AllEnergy, Ratios(new Dictionary<int, double> { { 2, 1.0 / 0.96 } }), null));
        }

        [Fact]
        public void RunningLaneKeepsItsSlotInsideTheExitBand()
        {
            var r = Ratios(new Dictionary<int, double> { { 7, 1.045 } });
            Assert.Equal(new[] { 4, 6 }, NguGuidePriority.Hot(false, AllEnergy, r, null));
            Assert.Equal(new[] { 7 }, NguGuidePriority.Hot(false, AllEnergy, r, new[] { 7 }));
        }

        [Fact]
        public void LockedNgusAreSkipped()
        {
            var r = Ratios(new Dictionary<int, double> { { 7, 2.0 } });
            Assert.Equal(new[] { 4 }, NguGuidePriority.Hot(false, new[] { 0, 1, 4 }, r, null));
        }
    }
}
