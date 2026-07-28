using SimpleJSON;
using Xunit;

namespace NGUAdvisor.Tests
{
    // Regression guard for review findings #1 (culture-sensitive number I/O -> locale data corruption) and
    // #2 (all-double storage -> large-integer / precision loss). Every test that can vary by locale is run in
    // both en-US and de-DE (comma-decimal) so the invariant-culture contract is exercised, not just assumed.
    public class SimpleJsonCultureTests
    {
        [Theory]
        [InlineData("en-US")]
        [InlineData("de-DE")]   // comma-decimal locale: the exact case that used to corrupt profiles (#1)
        public void Decimal_roundtrips_with_dot_separator_regardless_of_culture(string culture)
        {
            using (new CultureScope(culture))
            {
                var node = JSON.Parse("{\"x\":1.5}");
                Assert.True(node["x"].IsNumber);
                Assert.Equal(1.5, node["x"].AsDouble);

                var json = node.ToString();
                Assert.Contains("1.5", json);
                Assert.DoesNotContain("1,5", json);   // never emit a comma decimal — that is invalid JSON
            }
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        public void Dot_decimal_token_parses_even_under_comma_culture(string culture)
        {
            using (new CultureScope(culture))
            {
                var node = JSON.Parse("{\"x\":3.14}");
                Assert.Equal(3.14, node["x"].AsDouble, 10);
            }
        }

        [Fact]
        public void Thousands_separator_is_not_swallowed_by_the_number_parser()
        {
            // The parse path uses NumberStyles.Float (no AllowThousands), so "3.14" must be 3.14, never 314.
            var node = JSON.Parse("{\"x\":3.14}");
            Assert.Equal(3.14, node["x"].AsDouble, 10);
            Assert.NotEqual(314.0, node["x"].AsDouble);
        }

        [Fact]
        public void Large_integer_beyond_double_exactness_roundtrips_byte_for_byte()
        {
            // #2: 2^53 + 1 is not exactly representable as a double; raw-token preservation keeps it verbatim.
            const string big = "9007199254740993";
            var node = JSON.Parse("{\"big\":" + big + "}");
            Assert.Contains(big, node.ToString());
        }

        [Fact]
        public void Parsed_number_reserializes_verbatim_not_G17_expanded()
        {
            // #2: a parsed token keeps its exact source text, so 0.1 stays "0.1" instead of "0.10000000000000001".
            var json = JSON.Parse("{\"v\":0.1}").ToString();
            Assert.Contains("0.1", json);
            Assert.DoesNotContain("0.1000000", json);
        }

        [Fact]
        public void Programmatic_number_serializes_with_roundtrippable_precision()
        {
            // A number created in code (no raw token) serializes via G17, which round-trips exactly for double.
            var obj = new JSONObject();
            obj["v"] = new JSONNumber(0.1);
            var back = JSON.Parse(obj.ToString())["v"].AsDouble;
            Assert.Equal(0.1, back);
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        public void Base_AsDouble_setter_formats_invariantly(string culture)
        {
            // Guards the SimpleJson.cs:336 hardening: the base JSONNode.AsDouble setter (reached on non-number
            // nodes such as JSONString, which does not override AsDouble) must never emit a comma decimal.
            using (new CultureScope(culture))
            {
                var s = new JSONString("");
                s.AsDouble = 1.5;
                Assert.Equal("1.5", s.Value);
            }
        }
    }
}
