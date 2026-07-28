using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    // Shape/monotonicity guards for the hand-extracted titan requirement tables (review finding #33). These are
    // magic numbers transcribed from the game's autokill methods + the community guide; a typo or a stale row
    // would silently corrupt titan targeting. These invariants were verified against the current data, and are
    // written to respect the intentional 0.0 sentinels (no regen gate below T4; no idle stage for some titans).
    public class TitanTablesTests
    {
        [Fact]
        public void Both_tables_cover_twelve_titans_with_matching_version_counts()
        {
            Assert.Equal(12, TitanTables.Ak.Length);
            Assert.Equal(12, TitanTables.Guide.Length);
            for (int t = 0; t < 12; t++)
            {
                int expected = t < 5 ? 1 : 4;   // T1-T5 single form; T6-T12 have four versions
                Assert.Equal(expected, TitanTables.Ak[t].Length);
                Assert.Equal(expected, TitanTables.Guide[t].Length);
            }
        }

        [Fact]
        public void Ak_rows_have_three_nonnegative_columns()
        {
            foreach (var titan in TitanTables.Ak)
                foreach (var ver in titan)
                {
                    Assert.Equal(3, ver.Length);
                    Assert.All(ver, x => Assert.True(x >= 0));
                }
        }

        [Fact]
        public void Guide_rows_have_four_nonnegative_columns()
        {
            foreach (var titan in TitanTables.Guide)
                foreach (var ver in titan)
                {
                    Assert.Equal(4, ver.Length);
                    Assert.All(ver, x => Assert.True(x >= 0));
                }
        }

        [Fact]
        public void Ak_attack_and_defense_positive_and_regen_gates_from_T4()
        {
            for (int t = 0; t < TitanTables.Ak.Length; t++)
                foreach (var ver in TitanTables.Ak[t])
                {
                    Assert.True(ver[0] > 0, $"T{t + 1} atk must be > 0");
                    Assert.True(ver[1] > 0, $"T{t + 1} def must be > 0");
                    // Regen (HP-regen gate) is a sentinel 0 for T1-T3, a real positive gate from T4 (index 3) up.
                    if (t < 3) Assert.Equal(0.0, ver[2]);
                    else Assert.True(ver[2] > 0, $"T{t + 1} regen must gate (> 0)");
                }
        }

        [Fact]
        public void Guide_manual_positive_and_idle_is_both_or_neither()
        {
            foreach (var titan in TitanTables.Guide)
                foreach (var ver in titan)
                {
                    Assert.True(ver[0] > 0, "manual atk must be > 0");
                    Assert.True(ver[1] > 0, "manual def must be > 0");
                    // Idle atk/def are a sentinel PAIR: either both 0 (no idle stage) or both positive.
                    Assert.Equal(ver[2] == 0.0, ver[3] == 0.0);
                }
        }

        [Fact]
        public void Ak_requirements_strictly_increase_across_versions()
        {
            for (int t = 0; t < TitanTables.Ak.Length; t++)
            {
                var vers = TitanTables.Ak[t];
                for (int v = 1; v < vers.Length; v++)
                    for (int col = 0; col < 3; col++)
                        Assert.True(vers[v][col] > vers[v - 1][col],
                            $"T{t + 1} Ak col{col}: v{v + 1} ({vers[v][col]}) must exceed v{v} ({vers[v - 1][col]})");
            }
        }

        [Fact]
        public void Ak_first_version_attack_increases_across_titans()
        {
            for (int t = 1; t < TitanTables.Ak.Length; t++)
                Assert.True(TitanTables.Ak[t][0][0] > TitanTables.Ak[t - 1][0][0],
                    $"T{t + 1} v1 atk must exceed T{t} v1 atk");
        }

        [Fact]
        public void Guide_manual_requirements_increase_across_versions()
        {
            for (int t = 0; t < TitanTables.Guide.Length; t++)
            {
                var vers = TitanTables.Guide[t];
                for (int v = 1; v < vers.Length; v++)
                    for (int col = 0; col < 2; col++)   // manual atk/def only; idle columns may be sentinel 0
                        Assert.True(vers[v][col] > vers[v - 1][col],
                            $"Guide T{t + 1} col{col}: v{v + 1} must exceed v{v}");
            }
        }
    }
}
