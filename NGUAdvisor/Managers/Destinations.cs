namespace NGUAdvisor.Managers
{
    // WHERE the caller means to go — never HOW the current UI gets there.
    //
    // Navigation is addressed by rail-caption paths ("Systems/Quests", "Economy"), which SettingsForm
    // resolves through _sectionNav. Those strings describe the navigation structure as it stands today,
    // and slice 8 is about to change it: sections get renamed (STATUS / ADVICE), a PROFILE section
    // appears, Loadouts modes stop being rail children. Every literal path sitting in feature code would
    // have to be found and re-typed — and the ones that were missed would fail silently, because
    // NavigateTo swallows an unknown path.
    //
    // So feature code names the DESTINATION and this table names the route. Slice 8 edits the values
    // here and nothing else moves.
    //
    // Deliberately NOT a routing framework: no service, no registration, no history, no URIs. The app
    // already has working navigation — this only centralises the one piece of knowledge that is about
    // to become wrong.
    public static class Destinations
    {
        // The Advisors landing section's two pages. UI2 named them Overview (state, growth, active
        // profile, challenges) and Priorities (the ranked recommendation list). Until now these were the
        // only navigable pages reachable ONLY by literal rail keys; they join the table so any future
        // cross-link names the destination and never the path — the whole reason this file exists.
        public const string Overview = "Advisors/Overview";
        public const string Priorities = "Advisors/Priorities";

        // UI4 — the dedicated PROFILE section: a top-level rail destination of its own (not an Advisors
        // child), the canonical mutable home for allocation source + selected file + recommendation.
        public const string Profile = "Profile";

        // Two destinations may share a route today and still be distinct intentions. Combat shows Titans
        // and Adventure side by side; Economy shows Gold and Pit. Slice 8 may split them, so they stay
        // separate names — collapsing them now would just recreate the problem this file exists to solve.
        public const string Titans = "Combat";
        public const string Adventure = "Combat";

        public const string Gold = "Economy";
        public const string Pit = "Economy";

        public const string Quests = "Systems/Quests";
        public const string Blood = "Systems/Blood";
        public const string Boosts = "Systems/Boosts";
        public const string Yggdrasil = "Systems/Yggdrasil";

        public const string Loadouts = "Loadouts";

        // Slice 7.6C3 — routes for the three destinations that are NOT among the nine systems. They own
        // Cooking, Wishes and Cast Cards now, so the catalogue has to be able to point at them.
        //
        // These are deep links of exactly the same shape as the four above, and they resolve by exactly the
        // same machinery: RailChildren (SettingsForm:151,154) declares "Systems/Cooking", "Cards/Cards" and
        // "Cards/Wishes" as rail children, the rail loop registers each one in _sectionNav (SettingsForm:872),
        // and NavigateTo looks the path up there. Nothing new was built to make these reachable — they were
        // always reachable, and until now the catalogue simply had no name for them.
        public const string Cooking = "Systems/Cooking";
        public const string Cards = "Cards/Cards";
        public const string Wishes = "Cards/Wishes";

        // The SESSION source specifically: Main.Log() writes inject.log, which LOGS shows as SESSION.
        // The ADVISOR source is the ChallengeOverlay decision feed and does not contain it.
        public const string LogsSession = "Logs/Session";
    }
}
