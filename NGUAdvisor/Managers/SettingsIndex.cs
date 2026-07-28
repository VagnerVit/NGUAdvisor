using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    public enum EntryKind
    {
        System,    // the state lives in a panel; Settings shows it and points at the owner
        Setting,   // Settings IS the canonical owner; it stays editable here
        Reference  // neither: a searchable concept whose owner is elsewhere, or nowhere
    }

    // One row of the Settings index: identity, discoverability, destination. Nothing else.
    //
    // No controls, no delegates, no SavedSettings. Matching must not depend on live state — a search for
    // "Blood" finds Blood whether blood automation is on, off or mid-rebirth. Live state is rendered by a
    // later stage; this stage decides only what EXISTS and how it can be FOUND.
    //
    // (That independence is also why this is the first code in the project that can be executed and
    //  tested outside the game.)
    public class IndexEntry
    {
        public string Id;            // STABLE identity — never the display title, which slice 8 may change
        public EntryKind Kind;
        public string Title;         // the canonical label the user sees
        public string Blurb;         // one line: what it is / what it governs
        public string Destination;   // Destinations.* — systems only
        public string Group;         // the VISIBLE heading a setting sits under; searchable, never displayed
        public string Fields;        // persisted field name(s): hidden, searchable, a developer's escape hatch
        public string Aliases;       // legacy + colloquial terms: hidden, searchable, NEVER displayed

        // TWO MATCHING DOMAINS, deliberately not one haystack.
        //
        // Human text is matched by SUBSTRING; raw field identifiers are matched EXACTLY. Mixing them let
        // the developer escape hatch leak into ordinary search: "managed" hit Diggers, because the field
        // name ManageDiggers lowercases to "managediggers", which contains "managed". Nobody searching
        // "managed" means diggers. Identifiers are addresses, not words — so they answer only to their
        // whole name.
        internal string Human;       // title + blurb + group + aliases, lowercased
        internal string[] Ids;       // field identifiers, lowercased
    }

    // What exists in Settings, who owns it, and how to find it by any name it has ever had.
    //
    // ONE ENTRY PER SYSTEM. Blood matching on five different terms must still produce ONE result, or the
    // list degenerates into "Blood Automation / Blood Decisions / Blood Managed / Blood Cast Spells" —
    // four rows, one destination, no information. Terms are how you FIND an entry; they are not entries.
    //
    // ONE ENTRY PER SETTINGS ROW — and that is NOT the same rule (slice 7.6C1). Aggregating is right for a
    // system because a system has ONE destination: every term you can think of should land you on the same
    // panel. A Setting has no destination; the result IS the control. So an entry that folds three rows can
    // only ever render one of them, and the other two become unreachable from search while a query for the
    // shared word ("digger") silently returns whichever one the fold happened to name. Diggers and
    // Consumables each folded three rows into one entry; both are now split. Systems aggregate, settings
    // enumerate, and the reason is the destination, not a preference about list length.
    //
    // THE CANONICAL-OWNER RULE (accepted at the 6C preflight). A dedicated surface may be named canonical
    // owner only if it is UNCONDITIONALLY REACHABLE, EDITABLE, TWO-WAY SYNCED and COMPLETE enough to stand
    // alone. "Reachable" alone is too weak, and AutoTitanGold is why: its second surface is a chip on an
    // advisor RECOMMENDATION CARD that only renders while ManageGoldLoadouts is on and a titan is within
    // auto-kill reach (OptimizationAdvisor:439-452) — while the field itself gates AdvisorApply:55
    // unconditionally. A switch whose only off button disappears with the advice is not an owner.
    //
    // LEGACY NAMES ARE ALIASES, NOT LABELS. Slice 6 proved several were lies: "Cast Blood Spells" was the
    // automation gate, "Combat" never gated combat, "MANAGE" hid a permission layer. Someone who still
    // remembers the old word must be able to FIND the thing and then be shown what it actually is. So the
    // old words search and never display.
    //
    // ALIAS POLICY (deliberate, per entry — not one global blob):
    //   managed / unmanaged  -> Yggdrasil + Blood ONLY. They are the only two panels that ever used those
    //                           words (YggPanel and BloodPanel both had MANAGED/UNMANAGED buttons). A
    //                           search for "managed" returning BOTH is correct: both wore that label.
    //   advisor active / manual mode -> Boosts + Loadouts (both used the pair), and Advisor master
    //                           (the global toggle still says ADVISOR ACTIVE / ADVISOR PAUSED today).
    //   advisor mode         -> Titans only ("ENABLE ADVISOR MODE" was its button).
    //   auto / manage        -> NOT assigned to anyone. They are substrings of "automation", "AutoQuest",
    //                           "ManageGear" and so on, so they already match broadly by construction. A
    //                           broad query deserves a broad answer; forcing them to one destination
    //                           would be arbitrary.
    public static class SettingsIndex
    {
        // The stable identity contract, in one authoritative place, inside the type that owns identity.
        // Presentation code names a system by its ID and never by a string it typed itself — nine literals
        // scattered through a panel is the kind of thing that survives a rename in eight places and dies
        // quietly in the ninth. Not a registry, not reflection: nine consts and the catalogue built FROM
        // them, so a lookup key cannot disagree with the entry it was meant to find.
        public static class SystemIds
        {
            public const string Titans = "System.Titans";
            public const string Yggdrasil = "System.Yggdrasil";
            public const string Quests = "System.Quests";
            public const string Blood = "System.Blood";
            public const string Gold = "System.Gold";
            public const string Boosts = "System.Boosts";
            public const string Adventure = "System.Adventure";
            public const string Loadouts = "System.Loadouts";
            public const string Pit = "System.Pit";
        }

        private static IReadOnlyList<IndexEntry> _all;

        public static IReadOnlyList<IndexEntry> All => _all ?? (_all = Build().AsReadOnly());

        public static IndexEntry ById(string id)
        {
            foreach (var e in All)
                if (e.Id == id) return e;
            return null;
        }

        // Deterministic by construction: trim, invariant lowercase, split on WHITESPACE ONLY, AND across
        // tokens. A token matches an entry when it is a substring of the entry's human text OR exactly
        // equals one of its field identifiers. No ranking, no fuzzy matching, no stemming, no scoring, no
        // punctuation parsing, no quoted phrases — results return in catalogue order, so the same query
        // always yields the same rows in the same order. With ~40 entries, anything cleverer is a
        // maintenance liability with no user benefit.
        public static IReadOnlyList<IndexEntry> Search(string query)
        {
            var all = All;
            var hits = new List<IndexEntry>();

            if (string.IsNullOrEmpty(query) || query.Trim().Length == 0)
            {
                hits.AddRange(all);            // empty/null query = browse the whole index
                return hits.AsReadOnly();
            }

            var tokens = query.ToLowerInvariant()
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);   // null = all whitespace

            foreach (var e in all)
            {
                bool every = true;
                foreach (var t in tokens)
                {
                    if (!Matches(e, t)) { every = false; break; }
                }
                if (every) hits.Add(e);
            }
            return hits.AsReadOnly();
        }

        // Human text: substring. Field identifier: whole-name only. The OR is per token; the AND is across
        // tokens — so "blood CastBloodSpells" matches Blood on a word and an address at once.
        private static bool Matches(IndexEntry e, string token)
        {
            if (e.Human.IndexOf(token, StringComparison.Ordinal) >= 0) return true;
            foreach (var id in e.Ids)
                if (id == token) return true;
            return false;
        }

        // Takes the const, not a fragment to be concatenated: the ID a caller looks up is character-for-
        // character the ID the catalogue was built with, because they are the same symbol.
        private static IndexEntry Sys(string id, string title, string dest, string blurb, string fields, string aliases)
            => Seal(new IndexEntry { Id = id, Kind = EntryKind.System, Title = title, Destination = dest, Blurb = blurb, Fields = fields, Aliases = aliases });

        private static IndexEntry Set(string id, string title, string group, string blurb, string fields, string aliases = "")
            => Seal(new IndexEntry { Id = "Setting." + id, Kind = EntryKind.Setting, Title = title, Group = group, Blurb = blurb, Fields = fields, Aliases = aliases });

        // A Reference is what is left when a concept is searchable but is neither a Settings row nor one of
        // the nine systems. Its DESTINATION IS OPTIONAL and defaults to none, which is the whole point:
        // Hotkeys has no owner to be sent to — the answer to "what is F7" is the blurb itself, and inventing
        // a route just to keep the field non-null would be fabricating navigation to satisfy a type.
        private static IndexEntry Ref(string id, string title, string blurb, string fields, string aliases = "", string dest = null)
            => Seal(new IndexEntry { Id = "Reference." + id, Kind = EntryKind.Reference, Title = title, Destination = dest, Blurb = blurb, Fields = fields, Aliases = aliases });

        private static IndexEntry Seal(IndexEntry e)
        {
            // Field names are NOT in the human haystack — that leak is the whole point of the split.
            e.Human = $"{e.Title} {e.Blurb} {e.Group} {e.Aliases}".ToLowerInvariant();
            e.Ids = (e.Fields ?? "").ToLowerInvariant()
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return e;
        }

        // Order IS the contract: systems in information-architecture order, then Settings-owned entries by
        // category, then references. Nothing sorts at query time — the extension for the new kind is simply
        // that references come last, so every result list that existed before 7.6C1 keeps the order it had.
        private static List<IndexEntry> Build()
        {
            var e = new List<IndexEntry>();

            // ---------- systems: state lives in a panel; Settings shows it and links to the owner ----------

            // "automation decisions advisor manual" is the CANONICAL LAYER VOCABULARY, and it belongs in
            // human metadata for every system that genuinely has both layers. It used to reach these
            // entries by accident, through field names like AdvisorBlood — and when field names stopped
            // being substring-searchable, "blood advisor" stopped finding Blood. That was a metadata gap
            // wearing an algorithm's clothes. Yggdrasil does NOT get "decisions": it has no strategy layer,
            // and pretending otherwise in the search index is how the original lie started.
            const string layers = "automation decisions advisor manual";

            // "combat" and "economy" are the RAIL SECTIONS these systems live in (Destinations.Titans and
            // .Adventure resolve to "Combat"; .Gold and .Pit to "Economy"), so they are names the user can
            // actually see on screen. 7.6C4D moved those words OFF the Setting entries, where they named
            // groups that do not exist in Settings — but the sections are real, and a query for one must
            // land on the systems that are in it rather than on nothing at all. Adventure already carried
            // "combat"; Titans, Gold and Pit now say where they live too.
            e.Add(Sys(SystemIds.Titans, "Titans", Destinations.Titans,
                "Kill ladder, spawn targeting and the kill loadout.",
                "ManageTitans AdvisorTitans TitanSwapTargets TitanCombatMode",
                layers + " combat advisor mode enable advisor mode titan autokill ak kill ladder spawn"));

            e.Add(Sys(SystemIds.Yggdrasil, "Yggdrasil", Destinations.Yggdrasil,
                "Fruit harvest and harvest-time swaps. No advisor strategy layer exists for it.",
                "ManageYggdrasil ActivateFruits SwapYggdrasilLoadouts YggSwapThreshold",
                "automation managed unmanaged ygg fruit orchard harvest seeds"));

            e.Add(Sys(SystemIds.Quests, "Quests", Destinations.Quests,
                "Majors, minors, banking, butter and the abandon rules.",
                "AutoQuest AdvisorQuests AllowMajorQuests ManageQuestLoadouts PoolMajorQuests",
                layers + " auto quest advisor runs quests manual rules quest gear majors minors bank butter"));

            e.Add(Sys(SystemIds.Blood, "Blood", Destinations.Blood,
                "Iron pill timing and which spell the blood pool feeds.",
                "CastBloodSpells AdvisorBlood AutoSpellSwap BloodNumberThreshold",
                layers + " managed unmanaged cast blood spells blood magic iron pill spaghetti counterfeit number rituals"));

            e.Add(Sys(SystemIds.Gold, "Gold", Destinations.Gold,
                "Zone snipe, the time machine and the titan gold banks.",
                "ManageGoldLoadouts AdvisorGold SnipeOnNewZone SnipeOnRebirth SnipeOnGoldStarved SnipeOnTimer",
                layers + " economy advisor manages gold manual snipe gold loadouts re-snipe resnipe time machine tm titan bank"));

            // Discoverability is not ownership: "merges" and "convertibles" find Boosts because its
            // AUTOMATION (ManageInventory) genuinely gates them — but its DECISIONS (AutoBoostPriority)
            // does NOT control them. The index helps you find the switch; the panel tells the truth about
            // what each layer does.
            e.Add(Sys(SystemIds.Boosts, "Boosts", Destinations.Boosts,
                "Boost priority — and the inventory automation that carries it: merges, filters, convertibles.",
                "ManageInventory AutoBoostPriority PriorityBoosts BoostBlacklist CubePriority FavoredMacguffin",
                layers + " advisor active manual mode inventory automation boost priority merge filter convertibles cube macguffin transforms climb keep max"));

            // Indexed for what CombatEnabled actually does (adventure routing), never as though it
            // disabled all combat — titan and quest zones run regardless.
            //
            // SnipeBossOnly and BeastMode FOLD IN HERE (7.6C1). AdventurePanel owns them — "Bosses Only"
            // (:291) and "Beast Mode" (:290) — and 6B removed their Settings twins, which left two Setting
            // entries advertising controls that no longer exist. They need no entries of their own: Adventure
            // already has an entry AND a destination, and one result that lands on the owner beats two that
            // land nowhere. Fields carry the identifiers, aliases carry the words.
            //
            // The blurb gains "combat style" because it must now answer for them. This panel governs HOW it
            // fights as well as WHERE it goes, and a beast-mode search that returned a row reading only
            // "routing" would be answering a different question than the one asked.
            e.Add(Sys(SystemIds.Adventure, "Adventure", Destinations.Adventure,
                "Adventure routing and combat style: the farm zone, ITOPOD, gear hunt, and how it fights. Titan and quest zones run regardless.",
                "CombatEnabled AdvisorZones SnipeZone GearHuntEnabled GearHuntZone AdventureTargetITOPOD ITOPODAutoPush AllowZoneFallback SnipeBossOnly BeastMode",
                layers + " adventure combat combat enabled advisor routes zones manual zone farm zone target itopod gear hunt blacklist boss ceiling snipe boss only bosses only beast mode"));

            // No synthetic panel-wide decisions boolean, and — corrected in slice 7.5C — not seven
            // decision sources either. A mode's source is `!IsNullOrEmpty(GetObj())`, and LOOT HUNTER and
            // SHOCKWAVE have INERT objective lambdas (LoadoutsPanel:134,144 — `GetObj = () => ""`), so
            // they can never read as anything but MANUAL. Counting them would have manufactured a
            // statistic: only FIVE modes actually choose. The blurb said seven; the code said five.
            e.Add(Sys(SystemIds.Loadouts, "Loadouts", Destinations.Loadouts,
                "Five objective modes pick ADVISOR or MANUAL independently; Loot Hunter and Shockwave have no source choice. No single gear switch.",
                "TitanObjective GoldObjective QuestObjective YggdrasilObjective CookingObjective LootHunterAccessories Shockwave",
                // "automation" is a HUMAN alias only, and it earns its place: the row visibly says
                // AUTOMATION | PER MODE, so a search for the word the user can SEE must find the system
                // showing it. It is not a claim that a gate exists — the FIELDS deliberately still carry
                // no ManageGear, because slice 6.6 proved that gates none of the seven mode swaps.
                "advisor active manual mode automation per mode decision sources gear sets loadout titan gold quest yggdrasil cooking loot hunter shockwave accessory pool objective"));

            // STILL not normalised into automation/decisions, but no longer unresolved. AdvisorPit and the
            // standard AUTO THROW path are two RIVAL execution paths, and 6C2A settled which one wins:
            // the advisor owns automatic throw timing and AUTO THROW yields to it (Main.cs:821), keeping its
            // configuration rather than being rewritten. Priority between rivals is not permission + strategy,
            // so Pit stays the exceptional system — and AutoSpin and MoneyPitRunMode answer to neither.
            //
            // 7.6C2B also makes this the SOLE owner of AutoMoneyPit: the control lives on the Pit panel as
            // AUTO THROW, so "auto throw" is an alias here and this entry is the only result for it.
            e.Add(Sys(SystemIds.Pit, "Money pit", Destinations.Pit,
                "Independent Pit controls: advisor-priority throws, auto throw, daily spin, pit-run and daycare.",
                "AutoMoneyPit AdvisorPit MoneyPitThreshold AutoSpin MoneyPitRunMode DaycareThreshold",
                "economy advisor throws gold manual pit money pit auto throw pit throw gold toss daycare daily spin"));

            // ---------- settings: Settings IS the canonical owner. No panel governs these. ----------
            // Order below IS the panel's order — master, AUTOMATION, AUTO, SWAP GEAR FOR, MISC — so the
            // catalogue reads the way the screen does.

            // GROUP IS NOW THE HEADING THE USER CAN SEE (slice 7.6C4D). It used to be a private taxonomy —
            // Allocation, Systems, Economy, This install — that named groups appearing nowhere in the product,
            // and Group is searchable, so "economy" returned four settings that live under no ECONOMY heading.
            // Harmless while Settings had no rows; a lie the moment 6C4C2 gave them rows. The rule now:
            //
            //     searching a group name returns controls visibly located under that group.
            //
            // The Group field has exactly ONE consumer (Seal, which folds it into the search text). It never
            // rendered anything and still doesn't — BasicSettingsPanel owns its own headings and does not read
            // this. So these are search-truth edits, not layout edits.
            //
            // Terms displaced by the change were kept ONLY where they truthfully describe the individual
            // setting: "allocation" survives in three TITLES that already said it, AutoFight keeps "combat"
            // because fighting bosses is combat, and Settings Folder keeps "install". "Economy", "Systems" and
            // "This install" are gone as blanket group words — a MISC row does not inherit every term once
            // attached to a group it was never visibly in.
            const string gMaster = "";                 // the master has no heading: it is what the headings obey
            const string gAutomation = "AUTOMATION";
            const string gAuto = "AUTO";
            const string gSwap = "SWAP GEAR FOR";
            const string gMisc = "MISC";

            e.Add(Set("GlobalEnabled", "Advisor master", gMaster,
                "The global kill switch. Everything below obeys it. (F2)",
                "GlobalEnabled", "advisor active advisor paused pause master global enable disable"));
            // ---- AUTOMATION (9) ----
            // "allocation" is not lost with the old gAlloc group: these three TITLES already carry it.
            e.Add(Set("ManageEnergy", "Energy allocation", gAutomation, "The tool may allocate energy.", "ManageEnergy", ""));
            e.Add(Set("ManageMagic", "Magic allocation", gAutomation, "The tool may allocate magic.", "ManageMagic", ""));
            e.Add(Set("ManageR3", "R3 allocation", gAutomation, "The tool may allocate resource 3.", "ManageR3", "res3 hacks"));
            // Slice 6.6 left ManageGear HOMELESS: it does NOT gate the loadout swaps. It gates the advisor's
            // segment/objective gear refresh, the Loot Hunter equip and the profile's gear timeline. No panel
            // owns it, so it stays in Settings — under a name that says what it does, findable by its old one.
            e.Add(Set("ManageGear", "Advisor gear refresh", gAutomation,
                "The advisor may equip gear: segment objectives, the Loot Hunter set, the profile's gear timeline. It does NOT control the loadout swaps — each mode has its own switch.",
                "ManageGear", "manage gear gear automation"));
            // THREE ROWS, THREE ENTRIES (7.6C1) — but they no longer sit in one group: the panel puts Diggers
            // under AUTOMATION, Digger Upgrades under AUTO and Digger cap under MISC, and the catalogue now
            // says so. "digger" still finds all four digger rows; each one now admits where it actually lives.
            e.Add(Set("ManageDiggers", "Diggers", gAutomation, "The tool may manage diggers.", "ManageDiggers", ""));
            e.Add(Set("ManageBeards", "Beards", gAutomation, "The tool may manage beards.", "ManageBeards", ""));
            e.Add(Set("ManageWandoos", "Wandoos", gAutomation, "The tool may manage Wandoos.", "ManageWandoos", "os"));
            e.Add(Set("ManageNGUDiff", "NGU difficulty", gAutomation, "The tool may switch NGU difficulty.", "ManageNGUDiff", "difficulty"));
            // Wishes, Cooking and Cards left in 7.6C3 — their dedicated pages own them now, and they live at
            // the bottom of this catalogue as References.
            e.Add(Set("ManageConsumables", "Consumables", gAutomation, "The tool may use consumables.", "ManageConsumables", ""));

            // ---- AUTO (10) ----
            // AutoFight keeps "combat" as an alias, not as a group: no COMBAT heading exists in Settings, but
            // fighting bosses IS combat, so the word describes the setting and earns its place.
            e.Add(Set("AutoFight", "Fight bosses", gAuto, "The tool may fight bosses.", "AutoFight", "combat boss fight"));
            e.Add(Set("AutoRebirth", "Rebirth", gAuto, "The tool may rebirth on the profile's schedule.", "AutoRebirth", ""));
            e.Add(Set("AutoConvertBoosts", "Convert boosts", gAuto, "Convert boosts automatically.", "AutoConvertBoosts", ""));
            e.Add(Set("AutoTitanGold", "Titan gold", gAuto, "Bank titan gold drops.", "AutoTitanGold", ""));
            e.Add(Set("UpgradeDiggers", "Digger upgrades", gAuto, "The tool may buy digger upgrades.", "UpgradeDiggers", "upgrade diggers"));
            e.Add(Set("AutoBuyEM", "Buy E/M with EXP", gAuto, "Spend EXP on energy and magic.", "AutoBuyEM", "exp energy magic"));
            e.Add(Set("AutoBuyAdventure", "Buy adventure with EXP", gAuto, "Spend EXP on adventure stats.", "AutoBuyAdventure", "exp power toughness"));
            e.Add(Set("Autosave", "Daily save", gAuto, "Save the game daily for the AP reward.", "Autosave", ""));
            // The third one's BLURB is doing real work: "Consume mid-run" is the only one of the three whose
            // TITLE does not contain the word "consumable", so without it in the text a search for
            // "consumables" would return two of the three rows and look complete while being wrong.
            e.Add(Set("AutoBuyConsumables", "Buy consumables", gAuto, "Spend AP to restock consumables when they run out.", "AutoBuyConsumables", "buy ap restock"));
            e.Add(Set("ConsumeIfAlreadyRunning", "Consume mid-run", gAuto, "Use consumables even while one of the same kind is already running.", "ConsumeIfAlreadyRunning", "consume if already running overlap refresh"));

            // ---- SWAP GEAR FOR (3) ----
            // The per-mode execution gates slice 6.6 exposed. They exist ONLY here: Loadouts shows the SETS,
            // not the switches that let them swap.
            e.Add(Set("SwapTitanLoadouts", "Titan gear swap", gSwap, "Equip the TITAN set when a titan spawns.", "SwapTitanLoadouts", "titan gear"));
            e.Add(Set("SwapTitanDiggers", "Titan digger swap", gSwap, "Swap diggers for titan kills.", "SwapTitanDiggers", ""));
            e.Add(Set("SwapTitanBeards", "Titan beard swap", gSwap, "Swap beards for titan kills.", "SwapTitanBeards", ""));

            // ---- MISC (5) ----
            // SnipeBossOnly and BeastMode are long gone — folded into the Adventure system entry, which is
            // where their controls live. Setting.AutoMoneyPit likewise: PitPanel owns it as AUTO THROW.
            //
            // "install" survives on SETTINGS FOLDER alone, because that is the one row it truthfully
            // describes. The old "This install" group put the word on the daily save and the overlay toggle
            // too, which is precisely the indiscriminate inheritance this reconciliation exists to end.
            e.Add(Set("DisableOverlay", "Disable overlay", gMisc, "Hide the in-game overlay.", "DisableOverlay", ""));
            e.Add(Set("DiggerCap", "Digger cap", gMisc, "The share of gross gold/sec that digger upkeep may spend.", "DiggerCap", "digger cap % cap percent budget"));
            e.Add(Set("SettingsFolder", "Settings folder", gMisc, "Open the settings and profiles folder.", "", "folder profiles logs install installation config configuration"));
            // The three ACTIONS stay Setting-kind, because Settings genuinely owns them. A button is not an
            // ownership category — it is a widget, and which widget a search result renders is the rendering
            // contract's problem, not identity's.
            //
            // UNLOAD IS ONE ENTRY FOR THE ARMED PAIR, and 6C4C1 made that structural rather than a promise:
            // BasicSettingsPanel registers [Arm unload checkbox + Unload button] as ONE SettingSurface, and
            // the filter moves surfaces whole. So the two controls show together or not at all, and no query
            // can produce a lone Unload button to click. Splitting this into two entries would be the first
            // step back toward a search box that can fire it unarmed.
            e.Add(Set("UnloadAdvisor", "Unload advisor", gMisc, "Detach the advisor from the running game.", "", "detach"));

            // ---------- references: not a Settings row, not one of the nine systems ----------

            // Hotkeys was never a control. It sat in the catalogue as a Setting because Setting was the only
            // thing that wasn't a System — and a Setting entry promises "you can edit this here", which for a
            // list of F-keys is a promise nothing can keep. It has no destination and needs none: the ANSWER
            // is the blurb. First entry of the kind that exists to stop that lie.
            e.Add(Ref("Hotkeys", "Hotkeys",
                "F1 window · F2 pause · F3 quicksave · F5 dump gear · F7 quickload · F8 loadout swap · F9 profile editor.",
                "", "hotkey hotkeys keys shortcut f1 f2 f3 f5 f7 f8 f9"));

            // 7.6C3 — the three destinations that took ownership from Settings. They are References and not
            // Settings because Settings can no longer edit them; they are References and not Systems because
            // they are not among the nine. This is precisely the gap the kind was invented for.
            //
            // A Reference addresses a DESTINATION, not a row — which is why Cooking is ONE entry carrying TWO
            // fields. ManageCooking and ManageCookingLoadouts are two switches on one page, and two results
            // that both say "go to Systems/Cooking" would be two rows, one destination, no information: the
            // same degeneracy the System entries have always refused. Settings enumerate because the result
            // IS the control; References aggregate because the result is a ROUTE.
            e.Add(Ref("Cooking", "Cooking",
                "Cooking and its gear swap are managed on the Cooking page.",
                "ManageCooking ManageCookingLoadouts",
                "cooking manage cooking cooking loadout swap loadout for cooking cooking gear food",
                Destinations.Cooking));

            e.Add(Ref("Wishes", "Wishes",
                "Wish spending and priorities are managed on the Wishes page.",
                "ManageWishes",
                "wishes manage wishes wish automation wish priority spend",
                Destinations.Wishes));

            e.Add(Ref("Cards", "Cards",
                "Automatic card casting is configured on the Cards page.",
                "AutoCastCards",
                "cards cast cards auto cast cards automatic card casting card casting",
                Destinations.Cards));

            return e;
        }
    }
}
