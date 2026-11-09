using System;
using System.Linq;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class WishValueMathTests
    {
        [Theory]
        [InlineData(5)]
        [InlineData(9)]
        [InlineData(46)]
        [InlineData(61)]
        [InlineData(1)]
        public void Product_wishes_are_scored(int id) => Assert.True(HackMath.IsProductWish(id));

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(7)]
        [InlineData(79)]
        [InlineData(164)]
        public void Flat_and_unlock_wishes_are_not_scored(int id) => Assert.False(HackMath.IsProductWish(id));

        [Fact]
        public void Respawn_wish_uses_the_reducer_form()
        {
            Assert.Equal(HackMath.ReducerGainPerLevel(0.01, 5), HackMath.WishGainPerLevel(46, 0.01, 5), 12);
            Assert.Equal(HackMath.RelativeGainPerLevel(0.01, 5), HackMath.WishGainPerLevel(9, 0.01, 5), 12);
        }

        [Fact]
        public void Split_fractions_sum_to_one_and_favour_the_heavier_wish()
        {
            double[] f = HackMath.WishSplitFractions(new[] { 4.0, 1.0 });
            Assert.Equal(1.0, f.Sum(), 12);
            Assert.True(f[0] > f[1]);
            Assert.Equal(Math.Pow(4.0, HackMath.WishSplitExponent), f[0] / f[1], 9);
        }

        [Fact]
        public void Split_exponent_is_the_inverse_of_one_minus_three_biases()
        {
            Assert.Equal(1.0 / 0.49, HackMath.WishSplitExponent, 12);
        }

        [Fact]
        public void Split_fractions_are_the_optimum_of_the_value_sum()
        {
            double[] w = { 3.0, 1.0 };
            double Value(double f0) => w[0] * Math.Pow(f0, 0.51) + w[1] * Math.Pow(1.0 - f0, 0.51);
            double best = HackMath.WishSplitFractions(w)[0];
            Assert.True(Value(best) >= Value(best - 0.02));
            Assert.True(Value(best) >= Value(best + 0.02));
        }

        [Fact]
        public void Split_fractions_fall_back_to_equal_without_weights()
        {
            double[] f = HackMath.WishSplitFractions(new[] { 0.0, 0.0, 0.0 });
            Assert.All(f, x => Assert.Equal(1.0 / 3.0, x, 12));
        }

        [Fact]
        public void Wish_speed_value_scales_with_what_it_speeds_up()
        {
            Assert.Equal(0.0, HackMath.WishSpeedValueRate(0.01, 0.0, 1e-5));
            Assert.Equal(2 * HackMath.WishSpeedValueRate(0.01, 1.0, 1e-5), HackMath.WishSpeedValueRate(0.01, 2.0, 1e-5), 15);
        }
    }
}
