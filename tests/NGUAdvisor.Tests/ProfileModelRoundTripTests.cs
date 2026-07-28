using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    // End-to-end guard for the profile load/edit/save round-trip — the "no silent data loss" contract, and the
    // real-world exercise of SimpleJSON number formatting behind findings #1/#2 (via Rebirth.Target, a double?).
    public class ProfileModelRoundTripTests
    {
        // A representative profile: a modeled system (Energy) with an {h,m} time, a comment key that MUST be
        // dropped, a data-bearing "PrioritiesDefault" extra that MUST survive, a numeric Rebirth.Target, a
        // Challenges array, and a completely unmodeled system that must pass through verbatim.
        private const string Sample = @"{
  ""Breakpoints"": {
    ""Energy"": [
      {
        ""Time"": { ""h"": 1, ""m"": 30 },
        ""Priorities"": [ ""E1"", ""E2"" ],
        ""Comment"": ""drop me on save"",
        ""PrioritiesDefault"": [ ""E9"" ]
      }
    ],
    ""Rebirth"": [
      { ""Type"": ""Time"", ""Time"": 3600, ""Target"": 1500000000000.0 }
    ],
    ""Challenges"": [ ""100LevelChallenge"" ],
    ""FutureSystem"": { ""keep"": ""verbatim"", ""n"": 42 }
  }
}";

        [Fact]
        public void Load_parses_modeled_systems()
        {
            var m = ProfileModel.Load(Sample);

            Assert.Single(m.Energy);
            Assert.Equal(5400, m.Energy[0].TimeSeconds);           // 1h30m
            Assert.Equal(new[] { "E1", "E2" }, m.Energy[0].Priorities.ToArray());
            Assert.Equal("", m.Energy[0].Challenge);

            Assert.Single(m.Rebirth);
            Assert.True(m.Rebirth[0].Target.HasValue);
            Assert.Equal(1.5e12, m.Rebirth[0].Target.Value);

            Assert.Equal(new[] { "100LevelChallenge" }, m.Challenges.ToArray());
        }

        [Fact]
        public void Save_drops_comment_keys_but_preserves_data_extras_and_unmodeled_systems()
        {
            var json = ProfileModel.Load(Sample).ToJson();

            Assert.DoesNotContain("drop me on save", json);   // comment key removed
            Assert.Contains("PrioritiesDefault", json);        // data extra survives (not a comment)
            Assert.Contains("FutureSystem", json);             // unmodeled system passed through
            Assert.Contains("verbatim", json);                 // ...verbatim, contents intact
        }

        [Fact]
        public void Roundtrip_is_idempotent()
        {
            var once = ProfileModel.Load(Sample).ToJson();
            var twice = ProfileModel.Load(once).ToJson();
            Assert.Equal(once, twice);
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("de-DE")]   // the numeric Rebirth.Target must survive a comma-decimal locale losslessly
        public void Numeric_target_survives_roundtrip_under_any_culture(string culture)
        {
            using (new CultureScope(culture))
            {
                var saved = ProfileModel.Load(Sample).ToJson();
                Assert.Contains("1500000000000", saved);   // formatted with a dot/no-decimal, never "1,5..."

                var reloaded = ProfileModel.Load(saved);
                Assert.Equal(1.5e12, reloaded.Rebirth[0].Target.Value);
            }
        }

        [Fact]
        public void Load_rejects_json_without_a_breakpoints_object()
        {
            Assert.ThrowsAny<System.Exception>(() => ProfileModel.Load(@"{ ""NotBreakpoints"": 1 }"));
        }
    }
}
