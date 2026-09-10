using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    // QuestManager used to decide "is a committed snipe running" from Settings.SnipeZone plus a hand
    // copy of the routing cascade's Target ITOPOD row. The copy could not see the rows above it, so
    // it answered the wrong way on exactly the cases those rows exist for.
    public class QuestStandDownTests
    {
        // The expression as it stood, so every claim below is a comparison against real behaviour
        // rather than an assertion about it. Note what it takes: a toggle, standing in for "what
        // routes".
        //
        //   IsZoneUnlocked(Settings.SnipeZone) && !Settings.AdventureTargetITOPOD && !AllowZoneFallback
        private static bool Old(bool snipeZoneUnlocked, bool targetItopod, bool allowZoneFallback)
            => snipeZoneUnlocked && !targetItopod && !allowZoneFallback;

        private const int HuntZone = 33;
        private const int FarmZone = 20;

        // THE GEAR HUNT outranks Target ITOPOD in the cascade (Main.cs carries the note "user-
        // reported: Target ITOPOD silently overrode the hunted stage"). With both on, routing sends
        // the character to the hunted stage -- but the old copy knew only the toggle, answered "not
        // sniping", and questing pre-empted the hunt one layer above the row that protects it.
        [Fact]
        public void A_gear_hunt_that_won_the_cascade_is_a_snipe_even_with_target_itopod_on()
        {
            Assert.True(QuestStandDown.IsSniping(HuntZone, intentZoneUnlocked: true, allowZoneFallback: false));
            Assert.False(Old(snipeZoneUnlocked: true, targetItopod: true, allowZoneFallback: false));
        }

        // A zone the advisor picked for drops is a committed snipe for the same reason: what matters
        // is the zone that would route, not which toggle happens to be set.
        [Fact]
        public void An_advisor_farm_zone_is_a_snipe_even_with_target_itopod_on()
        {
            Assert.True(QuestStandDown.IsSniping(FarmZone, intentZoneUnlocked: true, allowZoneFallback: false));
            Assert.False(Old(snipeZoneUnlocked: true, targetItopod: true, allowZoneFallback: false));
        }

        // The other direction, which the toggle never covered: the boost farm writes
        // Settings.SnipeZone = 1000 when there is no boost demand, and IsZoneUnlocked(1000) is
        // itopodOn -- so a character parked in the pod counted as "sniping" and questing was refused
        // there, against the stated rule ("not farming ITOPOD").
        [Fact]
        public void The_itopod_is_never_a_snipe_however_it_was_routed()
        {
            Assert.False(QuestStandDown.IsSniping(QuestStandDown.ItopodZone,
                intentZoneUnlocked: true, allowZoneFallback: false));
            Assert.True(Old(snipeZoneUnlocked: true, targetItopod: false, allowZoneFallback: false));
        }

        // The two terms that are kept verbatim.
        [Fact]
        public void A_locked_zone_is_not_a_snipe()
        {
            Assert.False(QuestStandDown.IsSniping(FarmZone, intentZoneUnlocked: false, allowZoneFallback: false));
        }

        [Fact]
        public void Zone_fallback_disables_the_stand_down_entirely()
        {
            Assert.False(QuestStandDown.IsSniping(FarmZone, intentZoneUnlocked: true, allowZoneFallback: true));
            Assert.False(Old(snipeZoneUnlocked: true, targetItopod: false, allowZoneFallback: true));
        }
    }
}
