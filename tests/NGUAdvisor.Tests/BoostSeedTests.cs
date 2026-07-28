using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class BoostSeedTests
    {
        [Fact]
        public void Appends_equipped_then_locked_after_the_existing_list()
        {
            int[] result = BoostSeed.SeedPriorityBoosts(
                new[] { 110, 126 },
                new[] { 124, 123 },
                new[] { 122 });

            Assert.Equal(new[] { 110, 126, 124, 123, 122 }, result);
        }

        [Fact]
        public void Never_duplicates_an_id_already_in_the_list()
        {
            int[] result = BoostSeed.SeedPriorityBoosts(
                new[] { 124 },
                new[] { 124, 123 },
                new[] { 124, 122 });

            Assert.Equal(new[] { 124, 123, 122 }, result);
        }

        [Fact]
        public void Never_duplicates_an_id_present_in_both_groups()
        {
            int[] result = BoostSeed.SeedPriorityBoosts(
                new int[0],
                new[] { 200 },
                new[] { 200 });

            Assert.Equal(new[] { 200 }, result);
        }

        [Fact]
        public void Drops_non_positive_ids()
        {
            int[] result = BoostSeed.SeedPriorityBoosts(
                new int[0],
                new[] { 0, -1, 300 },
                new int[0]);

            Assert.Equal(new[] { 300 }, result);
        }

        [Fact]
        public void Handles_null_inputs_as_empty()
        {
            Assert.Equal(new[] { 400 }, BoostSeed.SeedPriorityBoosts(null, new[] { 400 }, null));
            Assert.Empty(BoostSeed.SeedPriorityBoosts(null, null, null));
        }

        [Fact]
        public void Preserves_the_existing_order_exactly()
        {
            int[] result = BoostSeed.SeedPriorityBoosts(
                new[] { 3, 1, 2 },
                new[] { 1 },
                new int[0]);

            Assert.Equal(new[] { 3, 1, 2 }, result);
        }
    }
}
