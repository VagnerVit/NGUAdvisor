using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // Guide-ordered spend planner for ITOPOD perks (PP), Beast quirks (QP) and Yggdrasil fruit tiers
    // (seeds), following the community guide's chapter recommendations (sayolove.github.io/ngu-guide,
    // chapters 2-8; orders recorded in docs/NGU-KNOWLEDGE.md). Each plan is an ordered list of steps;
    // the next buy is the FIRST step that is unlocked, below target, and allowed at the current
    // difficulty. Names are matched against the game's LIVE name lists (exact first, contains as a
    // fallback) so ID drift between game versions can't mis-buy. Levels/costs/points are all live reads.
    // The plans cover guide ch2-8. Step names are checked against the live catalog in the state export
    // (ITOPOD PERKS / BEAST QUIRKS list every entry with its index), never against guide shorthand.
    public static class SpendPlanner
    {
        public struct Buy
        {
            public bool Known;
            public int Id;
            public string Name;
            public long CurLevel;
            public long TargetLevel;   // level/tier the plan wants
            public long Cost;          // cost of the NEXT single level/tier
            public bool Affordable;
        }

        private struct Step
        {
            public string Match;    // matched against live name list
            public long Target;     // 0 = max level (perks/quirks); tier for fruits
            public int MinChapter;  // step ignored before this chapter
            // Fruits only: tier cap the step needs (0 = ungated). The guide schedules the tier-24
            // push for ch4, but the GAME gate is AllYggdrasil.capTier() -- 10 until Troll Challenge
            // 3x, then 24. Those come apart (cap earned before T6), and gating on the chapter alone
            // stalled the plan with seeds banked and nowhere to spend them.
            public int MinCap;
            // Last chapter the step applies in (0 = no end): the guide's "X until T8" would otherwise
            // block every later chapter's steps behind a 1000-level perk.
            public int UntilChapter;
            // Which exact-name match to take when the game lists one name twice ("Another MacGuffin
            // Slot!" is perk 67 AND 88); 0 = the name must resolve on its own.
            public int Nth;
            // Quirks only: false = an unaffordable step is passed over instead of banking for it
            // (null = always bank). Steps are otherwise strictly sequential.
            public Func<bool> WaitWhenShort;
            public Step(string m, long t, int ch) { Match = m; Target = t; MinChapter = ch; MinCap = 0; UntilChapter = 0; Nth = 0; WaitWhenShort = null; }
            public Step(string m, long t, int ch, int cap) { Match = m; Target = t; MinChapter = ch; MinCap = cap; UntilChapter = 0; Nth = 0; WaitWhenShort = null; }

            public bool InWindow(int chapter) =>
                chapter >= MinChapter && (UntilChapter == 0 || chapter <= UntilChapter);

            // Planned-buy lookups report gates instead of applying them, but a step whose window has
            // already closed is not queued at all. Unknown chapter (0) closes nothing.
            public bool WindowPassed(int chapter) => UntilChapter > 0 && chapter > UntilChapter;
        }

        // ---- ITOPOD perk order (guide ch2-8) ----
        private static readonly Step[] PerkPlan =
        {
            new Step("The Newbie Energy Perk", 0, 2),
            new Step("The Newbie Magic Perk", 0, 2),
            new Step("The Newbie Adventure Perk", 0, 2),
            new Step("The Newbie Drop Chance Perk", 0, 2),
            new Step("The Newbie Stat Perk", 0, 2),
            new Step("Instant Advanced Training Levels", 2, 2),  // guide ch2: 2x after the Newbies
            new Step("Generic Energy Power Perk I", 0, 2),       // guide ch2: alternate with Cap I
            new Step("Generic Energy Cap Perk I", 0, 2),
            new Step("Bonus Titan EXP!", 1, 3),          // 1 level: online-AK EXP bonus
            new Step("What a Crappy Perk", 0, 3),
            new Step("A Digger Slot!", 0, 3),
            new Step("Boosted Boosts I", 10, 3),
            new Step("Faster NGU Energy", 0, 3),
            new Step("I want your seeds ;)", 0, 3),      // post-CBlock1 Yggdrasil block
            new Step("The First Harvest's The Best", 0, 3),
            new Step("\"Fruit of Knowledge sucks 1/5\"", 0, 3),
            new Step("\"Fruit of Knowledge STILL sucks 1/5\"", 0, 3),
            // guide ch4 "Perk Order", in its order. Its v2 / CBlock2 / Evil-prep sub-blocks have no gate
            // the planner can read, so the sequence alone carries them.
            new Step("Boosted Boosts I", 0, 4),          // "Finish Boosted Boosts 1"
            new Step("Generic Magic Power Perk I", 0, 4),
            new Step("Generic Magic Cap Perk I", 0, 4),
            new Step("Faster NGU Magic", 0, 4),
            new Step("Generic Energy Bar Perk I", 0, 4),
            new Step("Stat Boost for Rich Perks I", 40, 4),   // "Stat Boost for Rich Jerks 1 (40 levels)"
            new Step("Generic Magic Bar Perk I", 0, 4),
            new Step("Improved Cube Boosting!", 0, 4),        // "get ICB before BB2 if gear is greened"
            new Step("Boosted Boosts II", 0, 4),
            new Step("You'll Really Want This", 0, 4),        // Evil Prep: accessory slot
            new Step("Double Basic Training", 0, 4),
            new Step("Quicker Power Fruit Beta Activation", 0, 4),
            new Step("Quicker Fruit of Numbers Bonus Activation", 0, 4),
            // "Optional Evil Prep", minus Wandoos Lover (+2 OS levels; the run feeds no Wandoos).
            new Step("Beard Temp Level Bank I", 0, 4),
            new Step("Beard Temp Level Bank II", 0, 4),
            new Step("Adv. Training Level Bank I", 0, 4),
            new Step("Adv. Training Level Bank II", 0, 4),
            new Step("Golden Showers", 0, 4),
            new Step("Beard Temp Level Bank III", 0, 4),
            new Step("Adv. Training Level Bank III", 0, 4),
            new Step("Ooh, Another Digger Slot!", 0, 4),
            // guide ch5 "Perk Order". "EM Pow/Cap/NGU" = Generic E/M Power+Cap II and Faster NGU E/M II.
            new Step("Adv. Training Level Bank III", 0, 5),   // "Beard / AT Banks 3+4"
            new Step("Adv. Training Level Bank IV", 0, 5),
            new Step("Beard Temp Level Bank III", 0, 5),
            new Step("Beard Temp Level Bank IV", 0, 5),
            new Step("The Fibonacci Perk", 1, 5),             // "Fib 1"
            new Step("Generic Energy Power Perk II", 10, 5),  // "EM Pow/Cap/NGU 2 - Lvl 10"
            new Step("Generic Energy Cap Perk II", 10, 5),
            new Step("Generic Magic Power Perk II", 10, 5),
            new Step("Generic Magic Cap Perk II", 10, 5),
            new Step("Faster NGU Energy II", 10, 5),
            new Step("Faster NGU Magic II", 10, 5),
            new Step("The Fibonacci Perk", 3, 5),             // "Fib 3"
            new Step("Adv. Training Level Bank V", 0, 5),     // "Beard / AT Banks 5"
            new Step("Beard Temp Level Bank V", 0, 5),
            new Step("The Fibonacci Perk", 34, 5),            // "Fib 34"
            new Step("Generic Energy Power Perk II", 0, 5),   // "Finish EM Pow/Cap/NGU"
            new Step("Generic Energy Cap Perk II", 0, 5),
            new Step("Generic Magic Power Perk II", 0, 5),
            new Step("Generic Magic Cap Perk II", 0, 5),
            new Step("Faster NGU Energy II", 0, 5),
            new Step("Faster NGU Magic II", 0, 5),
            new Step("Generic Energy Bar Perk II", 0, 5),     // "Energy Bars 2"
            new Step("Generic Magic Bar Perk II", 0, 5),      // "Magic Bars 2 when cheap"
            // Boss-gated ch5 perks placed LAST so a still-locked one never stalls the earlier plan (there's
            // no perk boss-req field to guard on). The guide's ch6 list numbers the adventure perk (55),
            // i.e. Rich Perks I — not III, which costs 100x as much for half the effect.
            new Step("Welcome to Evil Difficulty", 0, 5),         // guide "Welcome to Evil" (~B120)
            new Step("Adventure Boost For Rich Perks I", 0, 5) { UntilChapter = 5 }, // "Adventure Perk until T8"
            // guide ch6 "Perk Order" — the guide numbers each entry, and every number matches the live
            // index. Truly Idle Questing (104) is left out: the advisor already hands in and restarts quests.
            new Step("Better QP Rewards!", 0, 6),                 // (89)
            new Step("Improved Quest Looting", 0, 6),             // (90)
            new Step("Gooder Idle Questing!", 0, 6),              // (105)
            new Step("Another Gooder Idle Questing!", 0, 6),      // (106)
            new Step("Generic Resource 3 Power Perk I", 0, 6),    // (95, 97) R3 Gens 1
            new Step("Generic Resource 3 Cap Perk I", 0, 6),
            new Step("The Fibonacci Perk", 89, 6),                // (94) Fib 89
            new Step("Generic Energy Power Perk III", 0, 6),      // (74, 76, 77, 79) EM Gens 3
            new Step("Generic Energy Cap Perk III", 0, 6),
            new Step("Generic Magic Power Perk III", 0, 6),
            new Step("Generic Magic Cap Perk III", 0, 6),
            new Step("Adventure Boost For Rich Perks I", 0, 6),   // (55) "until Typo/max"
            new Step("Faster NGU Energy III", 0, 6),              // (80, 81) NGU Generics
            new Step("Faster NGU Magic III", 0, 6),
            new Step("Another MacGuffin Slot!", 0, 6) { Nth = 1 }, // (67)
            new Step("The Fibonacci Perk", 233, 6),               // (94) Fib 233
            new Step("SPAWN FASTER DAMMIT", 0, 6),                // (93)
            new Step("Not So Minor Anymore", 0, 6),               // (87)
            new Step("Generic Energy Bar Perk III", 0, 6),        // (75, 78, 96) bars
            new Step("Generic Magic Bar Perk III", 0, 6),
            new Step("Generic Resource 3 Bar Perk I", 0, 6),
            new Step("Generic Resource 3 Power Perk II", 0, 6),   // (98, 100) R3 Gen 2s
            new Step("Generic Resource 3 Cap Perk II", 0, 6),
            new Step("Another MacGuffin Slot!", 0, 6) { Nth = 2 }, // (88) the 40k one
            new Step("Daycare Slot! c:", 0, 6),                   // (86)
            new Step("Adventure Boost For Rich Perks II", 0, 6),  // (83) "LRB to T9"
            new Step("Advanced Gooder Idle Questing", 0, 6),      // (91)
            new Step("Even More Advanced Gooder Idle Questing", 0, 6), // (92)
            // guide ch7 "Perks" — guidelines, not a strict order: the three expensive priorities, then
            // the generics it calls "always generally good", which close at Sadistic so they never
            // hold up the 500k PP Welcome-to-Sadistic perk.
            new Step("Wishes Card Tier Up I", 0, 7),
            new Step("The Fibonacci Perk", 987, 7),
            new Step("Boosted Boosts III", 0, 7),
            new Step("Generic Resource 3 Power Perk III", 0, 7) { UntilChapter = 7 }, // "focus R3 Generics"
            new Step("Generic Resource 3 Cap Perk III", 0, 7) { UntilChapter = 7 },
            new Step("Generic Energy Power Perk IV", 0, 7) { UntilChapter = 7 },
            new Step("Generic Energy Cap Perk IV", 0, 7) { UntilChapter = 7 },
            new Step("Generic Magic Power Perk IV", 0, 7) { UntilChapter = 7 },
            new Step("Generic Magic Cap Perk IV", 0, 7) { UntilChapter = 7 },
            new Step("Generic Resource 3 Power Perk IV", 0, 7) { UntilChapter = 7 },
            new Step("Generic Resource 3 Cap Perk IV", 0, 7) { UntilChapter = 7 },
            // guide ch8 "Perks", numbered.
            new Step("Welcome to Sadistic Difficulty", 0, 8),     // (144)
            new Step("QP Card Tier Up I", 0, 8),                  // (167)
            new Step("Card Recycling: Card Spawn", 0, 8),         // (216)
            new Step("Adventure Stats Card Tier Up I", 0, 8),     // (172)
        };

        // ---- Beast quirk order (guide ch4-8) ----
        //
        // The CHAPTERS here were wrong until 2026-09-13 (user-reported: "144 QP, the planner shows a
        // quirk I can afford, and it never buys it"). Everything but the Adventure baby quirk sat at
        // MinChapter 5, so through the whole of chapter 4 `NextQuirk` matched nothing, `BuyQuirks`
        // bought nothing, and the panel showed the harmless-looking "banking for X (chapter 5)" card —
        // which is the FUTURE-buy card, not a recommendation. Guide ch.4, Questing section, verbatim:
        // "Quirks: Baby Quirks, Beast's Seed, Beasted Boosts 1, MPow/MCap 1, Gold, EPow/ECap 1", and
        // guide ch.5 "Quirk Order": "Finish EM Pow/Cap 1 / Beard / AT Banks 1 / Beasted Boosts 2 /
        // Adventure Quirk in LRB to T8". So the baby quirks and Beasted Boosts I belong to ch.4.
        private static readonly Step[] QuirkPlan =
        {
            new Step("Baby's First Quirk: Adventure", 0, 4),  // guide ch4: 25% adventure for 300 QP
            new Step("Baby's First Quirk: Energy Power", 0, 4),
            new Step("Baby's First Quirk: Energy Cap", 0, 4),
            new Step("Baby's First Quirk: Energy Bars", 0, 4),
            new Step("Baby's First Quirk: Magic Power", 0, 4),
            new Step("Baby's First Quirk: Magic Cap", 0, 4),
            new Step("Baby's First Quirk: Magic Bars", 0, 4),
            // The rest of the ch.4 sentence, in the order it lists them. These four were missing from
            // the plan entirely, so once the baby quirks were done it went quiet again until ch.5.
            // Names verified against the live list (state-export, BEAST QUIRKS) — they are scene data,
            // so they cannot be checked from the decompile and a typo here just silently skips a step.
            new Step("The Beast's Seed ;)", 0, 4),            // guide ch4: "Beast's Seed"
            new Step("Beasted Boosts I", 0, 4),               // guide ch4: "Beasted Boosts 1"
            new Step("Generic Magic Power Quirk I", 0, 4),    // guide ch4: "MPow/MCap 1"
            new Step("Generic Magic Cap Quirk I", 0, 4),
            new Step("GOOOOOLLLLLLLLLLLD!", 0, 4),            // guide ch4: "Gold"
            new Step("Generic Energy Power Quirk I", 0, 4),   // guide ch4: "EPow/ECap 1"
            new Step("Generic Energy Cap Quirk I", 0, 4),
            // Guide ch5 "Quirk Order" opens with "Finish EM Pow/Cap 1" — MinChapter is a FLOOR and the
            // steps are sequential, so the four Generic I steps above carry into ch.5 and finish there
            // before the banks, which is exactly what that line asks for.
            new Step("Adv. Training Level Bank I", 0, 5),     // guide ch5: "Beard / AT Banks 1"
            new Step("Beard Temp Level Bank I", 0, 5),
            new Step("Beasted Boosts II", 0, 5),              // guide ch5: "Beasted Boosts 2"
            new Step("Adventure Boost For Rich Quirks I", 0, 5) { UntilChapter = 5 }, // ch5: "Adventure Quirk in LRB to T8"
            // guide ch6 "Quirk Order", numbered like the perks. (14): without the Extended Quest Bank the
            // guide banks QP for it from the T8 kill; with it the 15k arrives as banked Majors after HD1,
            // so the quirks below keep buying meanwhile. The Beast's Fertilizer (13) is rebirth-timing
            // QoL the automation does not need.
            new Step("The Beast NGU Quirk Ever", 0, 6) { WaitWhenShort = () => !OwnsExtendedQuestBank() }, // (14)
            new Step("Adventure Boost For Rich Quirks I", 400, 6), // (8) "to ~400"
            new Step("Adv. Training Level Bank I", 0, 6),         // (20, 30) Banks 1
            new Step("Beard Temp Level Bank I", 0, 6),
            new Step("Beasted Boosts II", 0, 6),                  // (53)
            new Step("Adv. Training Level Bank II", 0, 6),        // (21, 31) Banks 2
            new Step("Beard Temp Level Bank II", 0, 6),
            new Step("Adventure Boost For Rich Quirks I", 0, 6),  // (8) max
            new Step("Generic Resource 3 Power Quirk I", 0, 6),   // (47, 48) R3 Gens 1
            new Step("Generic Resource 3 Cap Quirk I", 0, 6),
            new Step("Adv. Training Level Bank III", 0, 6),       // (22, 32) Banks 3
            new Step("Beard Temp Level Bank III", 0, 6),
            new Step("Accessory Slot!", 0, 6),                    // (18)
            new Step("Beasted Boosts III", 0, 6),                 // (72)
            new Step("MacGuffin Slot!", 0, 6),                    // (19)
            new Step("Adv. Training Level Bank IV", 0, 6),        // (23, 24, 33, 34) Banks 4 and 5
            new Step("Adv. Training Level Bank V", 0, 6),
            new Step("Beard Temp Level Bank IV", 0, 6),
            new Step("Beard Temp Level Bank V", 0, 6),
            // guide ch7 "Quirks", in its listed order; the E/M generics it calls "always generally
            // good" close at Sadistic, and the Wish Slot waits for it ("Don't buy ... until Sad").
            new Step("Wishes Card Tier Up I", 0, 7),
            new Step("Extra Tag Slot!", 0, 7),
            new Step("Super Advanced Beast Training!", 0, 7),     // "BT Quirk (4k)"
            new Step("Atk/Def Hack Milestone Reducer I", 0, 7),   // "A/D MS Reducer (4k)"
            new Step("Adventure Stats Card Tier Up I", 0, 7),
            new Step("Hacks Card Tier Up I", 0, 7),
            new Step("PP Hack Milestone Reducer I", 0, 7),        // "towards the end of Evil"
            new Step("Generic Energy Power Quirk II", 0, 7) { UntilChapter = 7 },
            new Step("Generic Energy Cap Quirk II", 0, 7) { UntilChapter = 7 },
            new Step("Generic Magic Power Quirk II", 0, 7) { UntilChapter = 7 },
            new Step("Generic Magic Cap Quirk II", 0, 7) { UntilChapter = 7 },
            // guide ch8 "Quirks", numbered and in order.
            new Step("Improved Base ITOPOD PPP!", 0, 8),          // (70)
            new Step("BIG CHONKER CARDS", 0, 8),                  // (149)
            new Step("Card Recycling: Mayo", 0, 8),               // (156)
            new Step("Wishes Card Tier Up II", 0, 8),             // (114)
            new Step("PP Card Tier Up I", 0, 8),                  // (110)
            new Step("Adventure Stats Card Tier Up II", 0, 8),    // (119)
            new Step("Beasted Boosts IV", 0, 8),                  // (73)
            new Step("A Wish Slot!", 0, 8),
        };

        // ---- Yggdrasil fruit tier order (guide ch3 + ch4 sequence, ch5 Evil order) ----
        private static readonly Step[] FruitPlan =
        {
            new Step("Gold", 10, 3),
            new Step("Pomegranate", 5, 3),
            new Step("Knowledge", 1, 3),
            new Step("Luck", 1, 3),
            new Step("Pomegranate", 10, 3),
            new Step("Luck", 5, 3),
            new Step("Gold", 24, 3, 24),          // post-TC3 (cap 24): FoG -> Pom -> FoK -> FoL ->
            new Step("Pomegranate", 24, 3, 24),   // FoPa/FoA -> FoAP -> FoPb/FoN -> FoR
            new Step("Knowledge", 24, 3, 24),
            new Step("Luck", 24, 3, 24),
            new Step("Power α", 24, 3, 24),
            new Step("Adventure", 24, 3, 24),
            new Step("Arbitrariness", 24, 3, 24),
            new Step("Power β", 24, 3, 24),
            new Step("Numbers", 24, 3, 24),
            new Step("Rage", 24, 3, 24),
            // guide ch5 "Yggdrasil Order": early Evil, then mid-late Evil.
            new Step("Fruit of MacGuffin α", 12, 5, 24),
            new Step("Fruit of MacGuffin β", 1, 5, 24),
            new Step("Fruit of Quirks", 4, 5, 24),
            new Step("Watermelon", 24, 5, 24),
            new Step("Fruit of Quirks", 12, 5, 24),
            new Step("Fruit of MacGuffin α", 24, 5, 24),
            new Step("Fruit of Quirks", 24, 5, 24),
            new Step("Fruit of Power δ", 24, 5, 24),
            new Step("Fruit of MacGuffin β", 24, 5, 24),
            // guide ch7: "Finish last non-mayo fruits, then level mayo fruits equally" — in rungs of 6.
            new Step("Fruit of Angry Mayo", 6, 7, 24), new Step("Fruit of Sad Mayo", 6, 7, 24),
            new Step("Fruit of Moldy Mayo", 6, 7, 24), new Step("Fruit of Ayy Lmayo", 6, 7, 24),
            new Step("Fruit of Cinco De Mayo", 6, 7, 24), new Step("Fruit of Pretty Mayo", 6, 7, 24),
            new Step("Fruit of Angry Mayo", 12, 7, 24), new Step("Fruit of Sad Mayo", 12, 7, 24),
            new Step("Fruit of Moldy Mayo", 12, 7, 24), new Step("Fruit of Ayy Lmayo", 12, 7, 24),
            new Step("Fruit of Cinco De Mayo", 12, 7, 24), new Step("Fruit of Pretty Mayo", 12, 7, 24),
            new Step("Fruit of Angry Mayo", 18, 7, 24), new Step("Fruit of Sad Mayo", 18, 7, 24),
            new Step("Fruit of Moldy Mayo", 18, 7, 24), new Step("Fruit of Ayy Lmayo", 18, 7, 24),
            new Step("Fruit of Cinco De Mayo", 18, 7, 24), new Step("Fruit of Pretty Mayo", 18, 7, 24),
            new Step("Fruit of Angry Mayo", 24, 7, 24), new Step("Fruit of Sad Mayo", 24, 7, 24),
            new Step("Fruit of Moldy Mayo", 24, 7, 24), new Step("Fruit of Ayy Lmayo", 24, 7, 24),
            new Step("Fruit of Cinco De Mayo", 24, 7, 24), new Step("Fruit of Pretty Mayo", 24, 7, 24),
        };

        // 0 = stage unknown: every chapter-gated step then skips, so NextPerk/NextQuirk go un-Known
        // and NOTHING is bought — the planned-buy fallback still names what's next. (The old
        // "unknown = chapter 1" default made a transient detection failure read as "plan complete".)
        private static int Chapter()
        {
            try { var p = ProgressionAnalyzer.Detect(); return p.Known ? p.Chapter : 0; }
            catch { return 0; }
        }

        // A plan step whose name doesn't resolve is SKIPPED — silently, it looks like "plan
        // complete" (user-reported: P&Q said end-of-guide while the guide's chapter list had buys
        // left). Log each miss once so a name drift is visible in debug.log instead of invisible.
        private static readonly HashSet<string> _reportedMisses = new HashSet<string>();

        private static int FindByName(List<string> names, Step step, string kind)
        {
            int found = step.Nth > 0 ? FindNthExact(names, step.Match, step.Nth) : FindByNameCore(names, step.Match);
            if (found < 0 && _reportedMisses.Add(kind + "|" + step.Match + "|" + step.Nth))
                Main.LogDebug($"SpendPlanner: {kind} step '{step.Match}'{(step.Nth > 0 ? $" #{step.Nth}" : "")} not found in the game's name list — step skipped (name drift?)");
            return found;
        }

        private static int FindNthExact(List<string> names, string match, int nth)
        {
            for (int i = 0, seen = 0; i < names.Count; i++)
                if (string.Equals(names[i]?.Trim(), match, StringComparison.OrdinalIgnoreCase) && ++seen == nth) return i;
            return -1;
        }

        private static int FindByNameCore(List<string> names, string match)
        {
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i]?.Trim(), match, StringComparison.OrdinalIgnoreCase)) return i;
            // fallback 1: punctuation-insensitive exact (live names quote/punctuate differently than
            // the community catalogs — debug.log caught the quoted '"Fruit of Knowledge sucks 1/5"'
            // steps never resolving). Unique required, refuse ambiguity rather than mis-buy.
            string normMatch = Normalize(match);
            int found = -1;
            if (normMatch.Length > 0)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    if (names[i] == null || Normalize(names[i]) != normMatch) continue;
                    if (found >= 0) { found = -1; break; }
                    found = i;
                }
                if (found >= 0) return found;
            }
            // fallback 2: unique contains
            found = -1;
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == null || names[i].IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (found >= 0) return -1;   // ambiguous — refuse rather than mis-buy
                found = i;
            }
            return found;
        }

        // Resolves every step of every plan once, at load: a typo in a step the run reaches months
        // from now would otherwise surface only then. Misses log through FindByName as usual.
        public static void ValidatePlans()
        {
            try
            {
                var c = Main.Character;
                if (c == null) return;
                int steps = 0, misses = 0;
                foreach (var step in PerkPlan) { steps++; if (FindByName(c.adventureController.itopod.perkName, step, "perk") < 0) misses++; }
                foreach (var step in QuirkPlan) { steps++; if (FindByName(c.beastQuestPerkController.quirkName, step, "quirk") < 0) misses++; }
                foreach (var step in FruitPlan) { steps++; if (FindByName(c.yggdrasilController.fruitName, step, "fruit") < 0) misses++; }
                Main.LogDebug($"SpendPlanner: plan names checked — {steps} steps, {misses} unresolved");
            }
            catch (Exception e) { Main.LogDebug($"SpendPlanner validate: {e.Message}"); }
        }

        private static bool OwnsExtendedQuestBank()
        {
            foreach (var item in ApTierTable.Items)
                if (item.Name == "Extended Quest Bank") return ApPurchaseAdvisor.Owned(item);
            return false;
        }

        // Lowercase letters/digits only — quotes, punctuation and spacing drift can't break a match.
        private static string Normalize(string s)
        {
            if (s == null) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s)
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        // ---------- PERKS ----------

        public static Buy NextPerk()
        {
            var b = new Buy();
            try
            {
                var c = Main.Character;
                if (c == null) return b;
                var ipc = c.adventureController.itopod;
                var levels = c.adventure.itopod.perkLevel;
                int chapter = Chapter();
                var diff = c.settings.rebirthDifficulty;

                foreach (var step in PerkPlan)
                {
                    if (!step.InWindow(chapter)) continue;
                    int id = FindByName(ipc.perkName, step, "perk");
                    if (id < 0 || id >= levels.Count || id >= ipc.maxLevel.Count) continue;
                    if (ipc.perkDifficultyReq[id] > diff) continue;
                    long max = ipc.maxLevel[id] > 0 ? ipc.maxLevel[id] : long.MaxValue;
                    long target = step.Target > 0 ? Math.Min(step.Target, max) : max;
                    if (levels[id] >= target) continue;

                    b.Known = true; b.Id = id; b.Name = ipc.perkName[id];
                    b.CurLevel = levels[id]; b.TargetLevel = target;
                    b.Cost = ipc.perkCost(id);
                    b.Affordable = c.adventure.itopod.perkPoints >= b.Cost;
                    return b;
                }
            }
            catch (Exception e) { Main.LogDebug($"SpendPlanner perks: {e.Message}"); }
            return b;
        }

        // The first perk buy the guide still has QUEUED but which is gated by chapter or difficulty
        // — what banked PP is FOR (mirrors NextQuirkPlanned; user-reported: NextPerk()=unknown
        // surfaced as "plan complete" while later-chapter steps were still queued).
        public static PlannedBuy NextPerkPlanned()
        {
            var f = new PlannedBuy();
            try
            {
                var c = Main.Character;
                if (c == null) return f;
                var ipc = c.adventureController.itopod;
                var levels = c.adventure.itopod.perkLevel;
                if (levels == null) return f;
                var diff = c.settings.rebirthDifficulty;

                int chapter = Chapter();

                foreach (var step in PerkPlan)
                {
                    if (step.WindowPassed(chapter)) continue;
                    int id = FindByName(ipc.perkName, step, "perk");
                    if (id < 0 || id >= levels.Count || id >= ipc.maxLevel.Count) continue;
                    long max = ipc.maxLevel[id] > 0 ? ipc.maxLevel[id] : long.MaxValue;
                    long target = step.Target > 0 ? Math.Min(step.Target, max) : max;
                    if (levels[id] >= target) continue;

                    f.Known = true;
                    f.Name = ipc.perkName[id]?.Trim();
                    f.Cost = ipc.perkCost(id);
                    f.MinChapter = step.MinChapter;
                    f.DifficultyGated = ipc.perkDifficultyReq[id] > diff;
                    return f;
                }
            }
            catch (Exception e) { Main.LogDebug($"SpendPlanner planned perk: {e.Message}"); }
            return f;
        }

        // Buys toward the current perk step, up to maxBuys levels per call. Mirrors the game's own
        // per-click buy path ItopodPerkController.doLevelUp(id): deduct perkPoints, increment
        // perkLevel[id], then doEffect(id) (the derived-stat recompute). The ONLY things doLevelUp does
        // that we deliberately skip are its three UI-refresh calls -- showTooltip(id), updateText(),
        // changePage(page) -- none of which touch game state; skipping them avoids UI churn/redraw on the
        // advisor loop. There are no achievement or unlock hooks in that path. (Verified vs Assembly-CSharp.)
        public static int BuyPerks(int maxBuys)
        {
            int bought = 0;
            try
            {
                var c = Main.Character;
                if (c == null) return 0;
                var ipc = c.adventureController.itopod;
                for (; bought < maxBuys; )
                {
                    var b = NextPerk();
                    if (!b.Known || !b.Affordable) break;
                    c.adventure.itopod.perkPoints -= ipc.perkCost(b.Id);
                    c.adventure.itopod.perkLevel[b.Id]++;
                    ipc.doEffect(b.Id);
                    bought++;
                }
            }
            catch (Exception e) { Main.LogDebug($"SpendPlanner buy perks: {e.Message}"); }
            return bought;
        }

        // ---------- QUIRKS ----------

        public static Buy NextQuirk()
        {
            var b = new Buy();
            try
            {
                var c = Main.Character;
                if (c == null) return b;
                var qc = c.beastQuestPerkController;
                var levels = c.beastQuest.quirkLevel;
                if (qc == null || levels == null) return b;
                int chapter = Chapter();
                var diff = c.settings.rebirthDifficulty;

                foreach (var step in QuirkPlan)
                {
                    if (!step.InWindow(chapter)) continue;
                    int id = FindByName(qc.quirkName, step, "quirk");
                    if (id < 0 || id >= levels.Count || id >= qc.maxLevel.Count) continue;
                    if (qc.quirkDifficultyReq[id] > diff) continue;
                    long max = qc.maxLevel[id] > 0 ? qc.maxLevel[id] : long.MaxValue;
                    long target = step.Target > 0 ? Math.Min(step.Target, max) : max;
                    if (levels[id] >= target) continue;
                    long cost = qc.quirkCost(id);
                    bool affordable = c.beastQuest.quirkPoints >= cost;
                    if (!affordable && step.WaitWhenShort != null && !step.WaitWhenShort()) continue;

                    b.Known = true; b.Id = id; b.Name = qc.quirkName[id];
                    b.CurLevel = levels[id]; b.TargetLevel = target;
                    b.Cost = cost;
                    b.Affordable = affordable;
                    return b;
                }
            }
            catch (Exception e) { Main.LogDebug($"SpendPlanner quirks: {e.Message}"); }
            return b;
        }

        public struct PlannedBuy
        {
            public bool Known;
            public string Name;
            public long Cost;
            public int MinChapter;        // chapter the guide schedules it for
            public bool DifficultyGated;  // also needs a higher rebirth difficulty
            public bool CapGated;         // fruits: blocked by the Yggdrasil tier cap, not the chapter
        }

        // The first quirk buy the guide still has QUEUED but which is gated by chapter or difficulty
        // — i.e. what banked QP is FOR. User-reported: NextQuirk()=unknown used to surface as "plan
        // complete", which read as the advisor skipping quirks; on Normal the guide's only pre-Evil
        // buy is Baby's First Quirk: Adventure (ch.4), so the plan idles for whole chapters while QP
        // accumulates. This names the next scheduled buy so the advisor can say "bank for X".
        public static PlannedBuy NextQuirkPlanned()
        {
            var f = new PlannedBuy();
            try
            {
                var c = Main.Character;
                if (c == null) return f;
                var qc = c.beastQuestPerkController;
                var levels = c.beastQuest.quirkLevel;
                if (qc == null || levels == null) return f;
                var diff = c.settings.rebirthDifficulty;

                int chapter = Chapter();

                foreach (var step in QuirkPlan)
                {
                    if (step.WindowPassed(chapter)) continue;
                    int id = FindByName(qc.quirkName, step, "quirk");
                    if (id < 0 || id >= levels.Count || id >= qc.maxLevel.Count) continue;
                    long max = qc.maxLevel[id] > 0 ? qc.maxLevel[id] : long.MaxValue;
                    long target = step.Target > 0 ? Math.Min(step.Target, max) : max;
                    if (levels[id] >= target) continue;

                    f.Known = true;
                    f.Name = qc.quirkName[id]?.Trim();   // game data has a trailing space on id 6
                    f.Cost = qc.quirkCost(id);
                    f.MinChapter = step.MinChapter;
                    f.DifficultyGated = qc.quirkDifficultyReq[id] > diff;
                    return f;
                }
            }
            catch (Exception e) { Main.LogDebug($"SpendPlanner planned quirk: {e.Message}"); }
            return f;
        }

        // Same contract as BuyPerks: mirrors BeastQuestPerkController.doLevelUp(id) -- deduct quirkPoints,
        // increment quirkLevel[id], doEffect(id) -- and skips only its UI calls (showTooltip, updateText),
        // which carry no game state.
        public static int BuyQuirks(int maxBuys)
        {
            int bought = 0;
            try
            {
                var c = Main.Character;
                if (c == null) return 0;
                var qc = c.beastQuestPerkController;
                for (; bought < maxBuys; )
                {
                    var b = NextQuirk();
                    if (!b.Known || !b.Affordable) break;
                    c.beastQuest.quirkPoints -= qc.quirkCost(b.Id);
                    c.beastQuest.quirkLevel[b.Id]++;
                    qc.doEffect(b.Id);
                    bought++;
                }
            }
            catch (Exception e) { Main.LogDebug($"SpendPlanner buy quirks: {e.Message}"); }
            return bought;
        }

        // ---------- YGGDRASIL FRUIT TIERS ----------

        public static Buy NextFruit()
        {
            var b = new Buy();
            try
            {
                var c = Main.Character;
                if (c == null) return b;
                var ycon = c.yggdrasilController;
                var fruits = c.yggdrasil.fruits;
                if (ycon == null || fruits == null) return b;
                int chapter = Chapter();
                int cap = ycon.capTier();

                foreach (var step in FruitPlan)
                {
                    if (!step.InWindow(chapter)) continue;
                    if (cap < step.MinCap) continue;
                    int id = FindByName(ycon.fruitName, step, "fruit");
                    if (id < 0 || id >= fruits.Count || id >= ycon.baseSeedCost.Count) continue;
                    long target = Math.Min(step.Target, cap);
                    long tier = fruits[id].maxTier;
                    if (tier >= target) continue;
                    if (tier == 0 && !CanUnlockFruit(c, ycon.fruitName[id])) continue;

                    b.Known = true; b.Id = id; b.Name = ycon.fruitName[id];
                    b.CurLevel = tier; b.TargetLevel = target;
                    b.Cost = ycon.baseSeedCost[id] * (long)Math.Ceiling(Math.Pow(tier + 1, 2));
                    b.Affordable = c.yggdrasil.seeds >= b.Cost;
                    return b;
                }
            }
            catch (Exception e) { Main.LogDebug($"SpendPlanner fruits: {e.Message}"); }
            return b;
        }

        // The first fruit tier the guide still has QUEUED but which is gated by the chapter or by the
        // Yggdrasil tier cap -- what banked seeds are FOR. Seeds' only fallback used to be "no fruit
        // tier queued at this chapter", which names the WRONG cause whenever the real gate is the cap
        // (AllYggdrasil.capTier(): 10 until Troll Challenge 3x, then 24). Mirrors NextPerkPlanned:
        // gates are reported, not applied, so a step blocked by either one still gets named.
        public static PlannedBuy NextFruitPlanned()
        {
            var f = new PlannedBuy();
            try
            {
                var c = Main.Character;
                if (c == null) return f;
                var ycon = c.yggdrasilController;
                var fruits = c.yggdrasil.fruits;
                if (ycon == null || fruits == null) return f;
                int cap = ycon.capTier();

                int chapter = Chapter();

                foreach (var step in FruitPlan)
                {
                    if (step.WindowPassed(chapter)) continue;
                    int id = FindByName(ycon.fruitName, step, "fruit");
                    if (id < 0 || id >= fruits.Count || id >= ycon.baseSeedCost.Count) continue;
                    long tier = fruits[id].maxTier;
                    if (tier >= step.Target) continue;
                    if (tier == 0 && !CanUnlockFruit(c, ycon.fruitName[id])) continue;

                    f.Known = true;
                    f.Name = ycon.fruitName[id]?.Trim();
                    f.Cost = ycon.baseSeedCost[id] * (long)Math.Ceiling(Math.Pow(tier + 1, 2));
                    f.MinChapter = step.MinChapter;
                    f.CapGated = cap < step.MinCap;
                    return f;
                }
            }
            catch (Exception e) { Main.LogDebug($"SpendPlanner planned fruit: {e.Message}"); }
            return f;
        }

        // Unlock gates for the special fruits (from FruitController.upgrade()'s checks; MacGuffin β has none).
        private static bool CanUnlockFruit(Character c, string name)
        {
            try
            {
                // upgrade() refuses Numbers at tier 0 outright: only Troll Challenge #5 grants it.
                if (name.IndexOf("Numbers", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
                if (name.IndexOf("Rage", StringComparison.OrdinalIgnoreCase) >= 0)
                    return c.settings.itopodOn;
                if (name.IndexOf("MacGuffin α", StringComparison.OrdinalIgnoreCase) >= 0)
                    return c.achievements.achievementComplete[145];
                if (name.IndexOf("Quirks", StringComparison.OrdinalIgnoreCase) >= 0)
                    return c.settings.beastOn;
                if (name.IndexOf("mayo", StringComparison.OrdinalIgnoreCase) >= 0)
                    return c.cards.cardsOn;
                return true;
            }
            catch { return false; }
        }

        // One tier per call (tiers are chunky purchases). Mirrors FruitController.upgrade()'s state
        // mutation exactly: deduct seeds, increment fruits[id].maxTier. That game method has NO per-tier
        // doEffect; everything else it runs (unlockInfo(), updateFruitDisplay(), tooltips) is UI-only and
        // is deliberately skipped here.
        public static bool BuyFruitTier()
        {
            try
            {
                var c = Main.Character;
                var b = NextFruit();
                if (c == null || !b.Known || !b.Affordable) return false;
                c.yggdrasil.seeds -= b.Cost;
                c.yggdrasil.fruits[b.Id].maxTier++;
                return true;
            }
            catch (Exception e) { Main.LogDebug($"SpendPlanner buy fruit: {e.Message}"); return false; }
        }
    }
}
