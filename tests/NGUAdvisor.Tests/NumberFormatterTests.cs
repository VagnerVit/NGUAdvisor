using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    // Guards the consolidated number abbreviator (review finding #31): one "Qa"-suffix ladder, ~3 significant
    // figures, signed, NaN/Infinity-safe, scientific beyond the ladder. Culture-invariant format strings, so
    // these hold under any locale.
    public class NumberFormatterTests
    {
        [Theory]
        [InlineData(0, "0")]
        [InlineData(5, "5")]
        [InlineData(999, "999")]
        [InlineData(1000, "1K")]
        [InlineData(1500, "1.5K")]
        [InlineData(1_000_000, "1M")]
        [InlineData(1_000_000_000, "1B")]
        [InlineData(1e12, "1T")]
        [InlineData(1e15, "1Qa")]     // Qa (not Q) is the chosen quadrillion suffix
        [InlineData(1e33, "1De")]
        public void Abbreviates_on_the_Qa_ladder(double v, string expected) =>
            Assert.Equal(expected, NumberFormatter.Abbrev(v));

        [Theory]
        [InlineData(1234, "1.23K")]    // < 10 mantissa -> 2 decimals
        [InlineData(12345, "12.3K")]   // 10..100 mantissa -> 1 decimal
        [InlineData(123456, "123K")]   // >= 100 mantissa -> 0 decimals
        public void Keeps_about_three_significant_figures(double v, string expected) =>
            Assert.Equal(expected, NumberFormatter.Abbrev(v));

        [Theory]
        [InlineData(-1500, "-1.5K")]
        [InlineData(-1e12, "-1T")]
        public void Preserves_sign_for_negatives(double v, string expected) =>
            Assert.Equal(expected, NumberFormatter.Abbrev(v));

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Non_finite_values_render_as_zero(double v) =>
            Assert.Equal("0", NumberFormatter.Abbrev(v));

        [Fact]
        public void Falls_back_to_scientific_past_the_ladder()
        {
            var s = NumberFormatter.Abbrev(1e36);
            Assert.Contains("e", s);   // beyond "De" (1e33) -> scientific notation
        }
    }
}
