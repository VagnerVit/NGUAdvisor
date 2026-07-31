using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    // Guards ProfileValidator.Warnings — the additive-stacking advice. The interesting case is the one
    // that must stay SILENT: AUG token indices run over a flat 0-13 where even is an augment and odd is
    // that augment's upgrade, so "AUG-8, AUG-9" is one augment's two halves. Every shipped profile is
    // written that way and a warning there would be pure noise.
    public class ProfileValidatorWarningTests
    {
        private static string Profile(string priorities) => @"{
  ""Breakpoints"": {
    ""Energy"": [
      { ""Time"": { ""m"": 30 }, ""Priorities"": [ " + priorities + @" ] }
    ]
  }
}";

        [Theory]
        [InlineData(@"""AUG-8"", ""AUG-9""")]                       // one pair: augment + its upgrade
        [InlineData(@"""AUG-8:20"", ""AUG-9:20""")]                 // same, with percentage caps
        [InlineData(@"""BESTAUG""")]                                // the auto-picker alone
        [InlineData(@"""CAPALLNGU"", ""CAPWAN"", ""AUG-12""")]      // one augment among other systems
        [InlineData(@"""AUG-2"", ""AUG-3"", ""AUG-2""")]            // one augment, token repeated
        [InlineData(@"""CAPAUG-12:80"", ""BESTAUG""")]              // forced sword (LSC) + the picker
        [InlineData(@"""CAPAUG-12:80"", ""AUG-13""")]               // forced sword half + its upgrade
        [InlineData(@"""CAPAUG-0"", ""CAPAUG-4""")]                 // two bounded reservations, no split
        public void QuietWhenOneAugmentIsFunded(string priorities)
        {
            Assert.Empty(ProfileValidator.Warnings(Profile(priorities)));
        }

        [Fact]
        public void WarnsWhenTwoAugmentsShareABreakpoint()
        {
            var warnings = ProfileValidator.Warnings(Profile(@"""AUG-8"", ""AUG-10"""));

            var warning = Assert.Single(warnings);
            Assert.Contains("0:30", warning);
            Assert.Contains("Energy Buster", warning);
            Assert.Contains("Advanced Exoskeleton", warning);
        }

        [Fact]
        public void WarnsWhenBestAugIsMixedWithAnExplicitAugment()
        {
            var warnings = ProfileValidator.Warnings(Profile(@"""BESTAUG"", ""AUG-2"""));

            var warning = Assert.Single(warnings);
            Assert.Contains("BESTAUG", warning);
            Assert.Contains("Milk Infusion", warning);
        }

        [Fact]
        public void CountsBothHalvesOfEachPairAsOneAugmentEach()
        {
            var warnings = ProfileValidator.Warnings(Profile(@"""AUG-8"", ""AUG-9"", ""AUG-12"", ""AUG-13"""));

            var warning = Assert.Single(warnings);
            Assert.Contains("funds 2 augments", warning);
        }

        [Fact]
        public void ReportsEveryOffendingBreakpointSeparately()
        {
            const string json = @"{
  ""Breakpoints"": {
    ""Energy"": [
      { ""Time"": 0, ""Priorities"": [ ""AUG-0"", ""AUG-2"" ] },
      { ""Time"": { ""h"": 1 }, ""Priorities"": [ ""AUG-8"", ""AUG-9"" ] },
      { ""Time"": { ""h"": 2, ""m"": 30 }, ""Priorities"": [ ""AUG-4"", ""AUG-12"" ] }
    ]
  }
}";

            var warnings = ProfileValidator.Warnings(json);

            Assert.Equal(2, warnings.Count);
            Assert.Contains("0:00", warnings[0]);
            Assert.Contains("2:30", warnings[1]);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("{ not json at all")]
        [InlineData(@"{ ""Breakpoints"": { } }")]
        public void NeverThrowsOnInputItCannotRead(string json)
        {
            Assert.Empty(ProfileValidator.Warnings(json));
        }

        // A typo'd index is the structural validator's business; the advice pass must not guess at it.
        [Fact]
        public void IgnoresTokensWhoseAugmentCannotBeResolved()
        {
            Assert.Empty(ProfileValidator.Warnings(Profile(@"""AUG-X"", ""AUG-99"", ""AUG-8""")));
        }
    }
}
