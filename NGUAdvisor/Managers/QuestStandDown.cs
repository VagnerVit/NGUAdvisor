namespace NGUAdvisor.Managers
{
    // ── WHETHER A COMMITTED ZONE SNIPE STANDS QUESTING DOWN ───────────────────────────────────────
    //
    // QuestManager.UpdateShouldQuest used to decide this inline, as:
    //
    //     IsZoneUnlocked(Settings.SnipeZone) && !Settings.AdventureTargetITOPOD && !AllowZoneFallback
    //
    // Two things were wrong with that, and both made questing fight the rest of the advisor:
    //
    //   * `!Settings.AdventureTargetITOPOD` was a SECOND COPY of one row of the adventure-routing
    //     cascade, free to drift from Main.ResolveIntentZone, which owns it — and it had drifted.
    //     The copy knew only the toggle and never the GEAR HUNT row above it, so with the hunt on
    //     and Target ITOPOD also on, routing sent the character to the hunted stage while this
    //     predicate answered "not sniping": questing then pre-empted the hunt, one layer above the
    //     row that exists for exactly that user-reported defect (Main.cs, the gear-hunt row).
    //   * Reading Settings.SnipeZone is not the same question as "is the ITOPOD what routes". The
    //     boost farm writes Settings.SnipeZone = 1000 when there is no boost demand
    //     (AdvisorApply.cs:1042), and IsZoneUnlocked(1000) is itopodOn (CombatManager.cs:125) — so a
    //     character the advisor parked in the pod counted as "sniping" and questing was refused
    //     there. The intended rule was always "not farming ITOPOD"; a zone number says that, a
    //     toggle does not.
    //
    // ⚠ THIS CONSUMER WANTS THE INTENT, NOT THE ROUTED ZONE, and that is forced rather than a
    // concession — see the header of Main.ResolveIntentZone for the oscillator it would otherwise
    // close.
    //
    // `!allowZoneFallback` is QuestManager's OWN conservatism, not part of the routing cascade
    // (routing consults AllowZoneFallback only when the zone is locked, this consults it always).
    // Whether that is right is a separate question; it is preserved verbatim so this change is
    // exactly one thing.
    public static class QuestStandDown
    {
        // The ITOPOD's zone number in the routing cascade (Main.ResolveIntentZone, SavedSettings).
        public const int ItopodZone = 1000;

        // intentZone         : Main.ResolveIntentZone() — gear hunt > Target ITOPOD > Settings.SnipeZone.
        // intentZoneUnlocked : CombatManager.IsZoneUnlocked(intentZone), asked about the zone that
        //                      would route rather than always about Settings.SnipeZone. -1 keeps
        //                      reading as before: it is the SavedSettings sentinel and IsZoneUnlocked
        //                      returns true for it (the Safe Zone), which is also where routing would
        //                      send the character, so the two agree.
        // allowZoneFallback  : Settings.AllowZoneFallback, preserved verbatim (see above).
        public static bool IsSniping(int intentZone, bool intentZoneUnlocked, bool allowZoneFallback)
            => intentZoneUnlocked
            && intentZone < ItopodZone
            && !allowZoneFallback;
    }
}
