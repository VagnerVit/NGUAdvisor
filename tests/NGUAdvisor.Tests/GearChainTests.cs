using System.Collections.Generic;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class GearChainTests
    {
        private static GearPriority Step(string objective, int slots)
            => new GearPriority { Objective = GearChain.FindObjective(objective), MaxAccessorySlots = slots };

        [Fact]
        public void FindPreset_IsCaseInsensitiveAndReturnsNullForUnknown()
        {
            Assert.NotNull(GearChain.FindPreset("adventure + respawn"));
            Assert.Null(GearChain.FindPreset("no such chain"));
        }

        [Fact]
        public void EveryPresetResolvesItsObjectivesAndRespectsTheLengthCap()
        {
            foreach (var preset in GearChain.Presets)
            {
                Assert.NotEmpty(preset.Priorities);
                Assert.True(preset.Priorities.Count <= GearChain.MaxPriorities);
                foreach (var priority in preset.Priorities)
                    Assert.NotNull(priority.Objective);
            }
        }

        [Fact]
        public void PresetNamesDoNotCollideWithObjectiveNames()
        {
            foreach (var preset in GearChain.Presets)
                Assert.Null(GearChain.FindObjective(preset.Name));
        }

        // Describe is AdvisorApply's anti-churn identity key (AdvisorApply.cs:951): a chain that rendered
        // differently on two consecutive passes would re-equip every 120s forever.
        [Fact]
        public void Describe_IsTheSameStringEveryTimeForTheSameChain()
        {
            var chain = new List<GearPriority>
            {
                Step("Adventure", 3),
                Step("Respawn", 1),
                Step("Adventure", GearChain.Unlimited),
            };

            var first = GearChain.Describe(chain);
            Assert.Equal("Adventure(3) > Respawn(1) > Adventure(all)", first);
            Assert.Equal(first, GearChain.Describe(chain));
            // A second, equal chain must render identically -- the key is the chain's CONTENT, not its
            // object identity (PerformSwap stores a copy of the profile's list).
            Assert.Equal(first, GearChain.Describe(new List<GearPriority>
            {
                Step("Adventure", 3),
                Step("Respawn", 1),
                Step("Adventure", GearChain.Unlimited),
            }));
        }

        [Fact]
        public void Describe_RendersUnlimitedAsAllAndDistinguishesItFromABudget()
        {
            Assert.Equal("Adventure(all)", GearChain.Describe(new[] { Step("Adventure", GearChain.Unlimited) }));
            Assert.Equal("Adventure(2)", GearChain.Describe(new[] { Step("Adventure", 2) }));
        }

        [Fact]
        public void Describe_HandlesNullEmptyAndUnusableSteps()
        {
            Assert.Equal("(no chain)", GearChain.Describe(null));
            Assert.Equal("(no chain)", GearChain.Describe(new GearPriority[0]));
            // A step with no objective is skipped by Optimize, so it must not appear in the key either.
            Assert.Equal("(no chain)", GearChain.Describe(new[] { new GearPriority { MaxAccessorySlots = 2 } }));
            Assert.Equal("Adventure(1)", GearChain.Describe(new[] { null, new GearPriority(), Step("Adventure", 1) }));
        }

        // Two different presets must not collapse onto the same key, or swapping between them would never
        // clear AdvisorApply's 5% bar.
        [Fact]
        public void Describe_GivesEveryPresetItsOwnKey()
        {
            var keys = new HashSet<string>();
            foreach (var preset in GearChain.Presets)
                Assert.True(keys.Add(GearChain.Describe(preset.Priorities)), $"duplicate key for '{preset.Name}'");
        }

        [Fact]
        public void Resolve_PrefersAPresetAndCopiesItsSteps()
        {
            var preset = GearChain.FindPreset("Adventure + Respawn");
            var chain = GearChain.Resolve("adventure + respawn");

            Assert.NotNull(chain);
            Assert.Equal(GearChain.Describe(preset.Priorities), GearChain.Describe(chain));
            // A new list every call: callers (GearBreakpoints, AdvisorApply) hold on to it.
            Assert.NotSame(preset.Priorities, chain);
        }

        [Fact]
        public void Resolve_FallsBackToASingleUnlimitedObjective()
        {
            var chain = GearChain.Resolve("respawn");

            Assert.NotNull(chain);
            var step = Assert.Single(chain);
            Assert.Equal("Respawn", step.Objective.Name);
            Assert.Equal(GearChain.Unlimited, step.MaxAccessorySlots);
        }

        // Refuse, don't guess: an unresolved name must never be mapped onto a near-match, because the
        // caller equips whatever comes back.
        [Fact]
        public void Resolve_ReturnsNullForAnUnknownOrEmptyName()
        {
            Assert.Null(GearChain.Resolve("Advent"));
            Assert.Null(GearChain.Resolve("no such objective"));
            Assert.Null(GearChain.Resolve(""));
            Assert.Null(GearChain.Resolve(null));
        }
    }
}
