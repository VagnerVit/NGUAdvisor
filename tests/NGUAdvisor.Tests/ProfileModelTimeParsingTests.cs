using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    // The Time field is polymorphic: a bare number means seconds; an object sums h/m/(anything-else=seconds).
    // On save it is re-emitted as 0 for empty, else an object carrying only the non-zero h/m/s components.
    public class ProfileModelTimeParsingTests
    {
        private static string Compact(string s) =>
            s.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "");

        private static int TimeSecondsOf(string timeJson)
        {
            var json = "{ \"Breakpoints\": { \"Energy\": [ { \"Time\": " + timeJson + ", \"Priorities\": [] } ] } }";
            return ProfileModel.Load(json).Energy[0].TimeSeconds;
        }

        [Theory]
        [InlineData("45", 45)]                             // bare number = seconds
        [InlineData("5400", 5400)]
        [InlineData("{ \"h\": 1, \"m\": 30 }", 5400)]
        [InlineData("{ \"h\": 1, \"m\": 2, \"s\": 3 }", 3723)]
        [InlineData("{ \"h\": 2 }", 7200)]
        [InlineData("{ \"m\": 5 }", 300)]
        [InlineData("0", 0)]
        public void ParseTime_handles_number_and_object_forms(string timeJson, int expected) =>
            Assert.Equal(expected, TimeSecondsOf(timeJson));

        [Fact]
        public void Zero_time_emits_bare_number_zero()
        {
            var m = new ProfileModel();
            m.Energy.Add(new ProfileModel.PriorityBreakpoint { TimeSeconds = 0 });
            Assert.Contains("\"Time\":0", Compact(m.ToJson()));
        }

        [Fact]
        public void Nonzero_time_emits_only_nonzero_components()
        {
            var m = new ProfileModel();
            m.Energy.Add(new ProfileModel.PriorityBreakpoint { TimeSeconds = 5400 });   // exactly 1h30m0s
            var c = Compact(m.ToJson());
            Assert.Contains("\"h\":1", c);
            Assert.Contains("\"m\":30", c);
            Assert.DoesNotContain("\"s\":", c);   // the zero seconds component is omitted
        }

        [Fact]
        public void Seconds_only_time_emits_s_component()
        {
            var m = new ProfileModel();
            m.Energy.Add(new ProfileModel.PriorityBreakpoint { TimeSeconds = 45 });
            var c = Compact(m.ToJson());
            Assert.Contains("\"s\":45", c);
            Assert.DoesNotContain("\"h\":", c);
            Assert.DoesNotContain("\"m\":", c);
        }
    }
}
