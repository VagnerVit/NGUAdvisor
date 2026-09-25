using System;

namespace NGUAdvisor.Managers
{
    // THE SINGLE EXCLUSIVE ANSWER TO "WHAT AM I FARMING?".
    //
    // The answer has always been exclusive — Main.ResolveIntentZone picks ONE of gear hunt / ITOPOD /
    // SnipeZone, and AdvisorApply.ApplyZones picks ONE of gear farm / boost farm — but it was spelled
    // across five independent booleans on two different sub-tabs, so the UI could show two farms "on"
    // while only one of them routed. That is how a user farming the ITOPOD had to go to the ZONES tab,
    // switch on the boost farm, then go to the ITOPOD tab to switch Target ITOPOD back off: the tab that
    // owned the loser of the cascade could not turn off the winner.
    //
    // This type owns the exclusivity and NOTHING else. It writes the same five flags the routing has
    // always read; it introduces no new routing layer and no new persisted setting, so a settings.json
    // written before it still resolves (Current() reads the flags in the cascade's own order).
    public enum FarmModeKind
    {
        Zone = 0,   // your SnipeZone, advisor stays out of it
        Boosts,     // AdvisorApply picks the best boost farm
        Gear,       // AdvisorApply picks the zone that caps the most gear
        Hunt,       // camp GearHuntZone for its drops
        Itopod      // park in the pod
    }

    public static class FarmMode
    {
        // The mode we were in before the current one, so a shortcut elsewhere (the PP panel's "park in
        // the pod for PP" switch) can hand routing back to what the user had rather than guessing. It is
        // a SESSION convenience, deliberately not persisted: after a reload there is no "before".
        private static FarmModeKind _previous = FarmModeKind.Zone;
        public static FarmModeKind Previous { get { return _previous; } }

        // Read in the SAME order Main.ResolveIntentZone resolves (hunt > ITOPOD > advisor), so the mode
        // shown is the one that actually routes — never the loser of the cascade. Hunt reads the ENABLE
        // flag, not GearHunter.Active: a hunt with no stage picked yet is still the user's intent, and
        // the stage line on the ZONES page is what says the stage is missing.
        public static FarmModeKind Current()
        {
            SavedSettings s = Main.Settings;
            if (s == null) return FarmModeKind.Zone;
            if (s.GearHuntEnabled) return FarmModeKind.Hunt;
            if (s.AdventureTargetITOPOD) return FarmModeKind.Itopod;
            // AdvisorZones ON is already the whole answer to "who picks the zone?" — ApplyZones falls
            // THROUGH to BoostFarmAdvisor when AdvisorFarmGear is off, whatever AdvisorFarmBoost says
            // (that flag only adds the "no boost demand → park in the pod" override). Requiring it here
            // read a settings.json with AdvisorZones on and both farm flags off — a state the old UI
            // could produce — as ZONE, while the advisor was overwriting SnipeZone every ten minutes.
            if (s.AdvisorZones) return s.AdvisorFarmGear ? FarmModeKind.Gear : FarmModeKind.Boosts;
            return FarmModeKind.Zone;
        }

        // WRITES SETTINGS ONLY (plus the gear-pass re-arm the hunt toggle has always done). Callers are
        // WinForms handlers, so nothing here may touch Unity objects — the next Main.Update() reads the
        // flags and does the routing.
        public static void Set(FarmModeKind mode)
        {
            SavedSettings s = Main.Settings;
            if (s == null) return;
            try
            {
                FarmModeKind was = Current();
                if (was != mode) _previous = was;

                bool huntWas = s.GearHuntEnabled;
                s.GearHuntEnabled = mode == FarmModeKind.Hunt;
                s.AdventureTargetITOPOD = mode == FarmModeKind.Itopod;
                s.AdvisorFarmGear = mode == FarmModeKind.Gear;
                s.AdvisorFarmBoost = mode == FarmModeKind.Boosts;
                // Only the two advisor farms hand zone choice to AdvisorApply. Hunt and ITOPOD are the
                // user's own pick and outrank it anyway; leaving AdvisorZones on under them would keep
                // ApplyZones rewriting a SnipeZone nobody reads.
                s.AdvisorZones = mode == FarmModeKind.Gear || mode == FarmModeKind.Boosts;

                if (s.GearHuntEnabled != huntWas)
                    AdvisorApply.GearRestored();   // swap on the next tick, not after the 120s throttle
            }
            catch (Exception e) { Main.LogDebug($"FarmMode.Set({mode}): {e.Message}"); }
        }

        public static string Caption(FarmModeKind mode)
        {
            switch (mode)
            {
                case FarmModeKind.Boosts: return "BOOSTS";
                case FarmModeKind.Gear: return "GEAR";
                case FarmModeKind.Hunt: return "HUNT";
                case FarmModeKind.Itopod: return "ITOPOD";
                default: return "ZONE";
            }
        }

        // What the selected mode does, in the user's terms — including the one thing each mode does NOT
        // decide, because every mode here is still outranked by titan, quest and gold routing upstream.
        public static string Explain(FarmModeKind mode)
        {
            switch (mode)
            {
                case FarmModeKind.Boosts:
                    return "Best boost zone, or ITOPOD when nothing needs boosts.";
                case FarmModeKind.Gear:
                    return "Zone that caps the most gear per time budget.";
                case FarmModeKind.Hunt:
                    return "Your stage's drops, in the Loot Hunter set.";
                case FarmModeKind.Itopod:
                    return "Floor and combat are on the ITOPOD tab.";
                default:
                    return "Your pick below — advisor stays out of it.";
            }
        }
    }
}
