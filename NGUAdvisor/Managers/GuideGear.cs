using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // The community guide's per-chapter "Items to Keep" lists (sayolove ngu-guide, chapters 2-8,
    // fetched 2026-08-15 into reference/ngu-guide/chapters/). Chapter 1's list is empty by design —
    // the guide says all chapter-1 gear is replaceable.
    //
    // Every id was resolved against the decomp's ItemNameDesc.cs and spec-verified: the entry's
    // reason matches the item's actual specType columns (e.g. "Tentacle (R3 bars)" is id 330, the
    // accessory with Res3Bar — NOT id 337, a spec-less part.Misc quest item with the same name).
    // The guide's shorthand names differ from the game's ("Lemmiwinks" is item 195 "A Small Gerbil",
    // named Lemmiwinks only in its description) — which is why this table is ids, not name matching.
    //
    // KeepUntil is the LAST chapter the guide still wants the item held, on the canonical titan-kill
    // chapter (ProgressionAnalyzer.Chapter — see its class note; NOT StageDetector's boss-threshold
    // chapter). 9 = keep forever. Sub-chapter horizons ("until late Chapter 5", "through early Sad")
    // round UP to the whole chapter: over-keeping is recoverable, over-trashing is not. Chapter 0
    // (unknown / game not ready) keeps everything active for the same reason.
    //
    // This class is PURE — no game reads — so the test project can link it directly. The caller
    // supplies the chapter.
    //
    // Ported from upstream. The id table was spot-checked against our own docs/ITEM-IDS.md before
    // trusting it, including both cases their header flags as traps: 195 resolves to "A Small Gerbil"
    // (the guide calls it Lemmiwinks, a name that appears only in the description) and 330 to
    // "Tentacle of the Exile", not the same-named spec-less quest item. 91/116/118/438 matched too.
    // Regenerate ITEM-IDS.md (build/gen-item-ids.sh) and re-check if the game ever renumbers items.
    public static class GuideGear
    {
        public struct Entry
        {
            public int Id;
            public int FoundCh;    // chapter whose "Items to Keep" list names it (2..8)
            public int KeepUntil;  // last chapter the hold applies (FoundCh..8), 9 = forever
            public string Reason;  // the guide's parenthetical, sentence case, game terms keep caps
        }

        private static Entry E(int id, int found, int until, string reason)
            => new Entry { Id = id, FoundCh = found, KeepUntil = until, Reason = reason };

        public static readonly Entry[] Entries =
        {
            // --- Chapter 2 (T1-Mega): "Definitely Keep to next chapter (unique specials)"
            E(91,  2, 3, "cooldown"),                       // The Sands of Time
            E(101, 2, 3, "AT / Wandoos"),                   // King Circle's Amulet of Helping Random Stuff
            E(118, 2, 3, "respawn"),                        // Stapler
            E(116, 2, 3, "NGU / gold"),                     // A Regular Tie
            E(438, 2, 3, "drop chance"),                    // Ghost Typewriter
            // "Can be handy in the future"
            E(109, 2, 3, "strong EM power"),                // Amulet of Sunshine, Sparkles, and Gore
            E(110, 2, 3, "gold / magic cap"),               // Dragon Wings
            // "Can be useful now for specials, replace next chapter"
            E(114, 2, 2, "specials now, replace next chapter"),  // Office Shoes
            E(123, 2, 2, "specials now, replace next chapter"),  // Gaudy Shirt
            E(124, 2, 2, "specials now, replace next chapter"),  // Gaudy Pants
            // "Optional Magic Cap items for AutoPom"
            E(436, 2, 3, "magic cap for AutoPom"),          // Giant Windup Gear
            E(435, 2, 3, "magic cap for AutoPom"),          // Magicite Crystal
            E(437, 2, 3, "magic cap for AutoPom"),          // A Sinusoidal Wave
            E(127, 2, 3, "magic cap for AutoPom"),          // A Beanie
            E(115, 2, 3, "magic cap for AutoPom"),          // The Pen-Is

            // --- Chapter 3 (T4-BAE): "Keep Forever"
            E(136, 3, 9, "respawn / drop chance"),          // Ring of Greed
            E(137, 3, 9, "move cooldown / respawn"),        // Ring of Might
            E(158, 3, 9, "seed gain"),                      // stooB s'rerednaW
            E(149, 3, 9, "seed gain"),                      // UUG's 'Special' Ring
            E(171, 3, 9, "respawn / drop chance / beard speed"), // My Green Heart <3
            // "Keep Temporarily"
            E(138, 3, 4, "NGU / AT, through end of Normal"),     // Ring of Utility
            E(161, 3, 5, "NGU / Wandoos, through early Evil"),   // Dorky Glasses
            E(168, 3, 5, "augment speed, until late ch.5"),      // Badly Drawn Gun
            E(164, 3, 5, "AT, until late ch.5"),                 // Badly Drawn Smiley Face
            E(178, 3, 5, "NGU, until late ch.5"),                // The Stealthiest Armour (also on ch.4's list)

            // --- Chapter 4 (T6): "Keep Forever"
            E(193, 4, 9, "Ygg yield / seed gain"),          // A Giant Apple
            E(121, 4, 9, "respawn"),                        // The Triple Flubber
            E(444, 4, 9, "seed gain"),                      // Candy Corn Necklace
            // "Keep Temporarily"
            E(189, 4, 5, "Wandoos, until late ch.5"),       // A Bald Egg
            E(190, 4, 6, "beard speed, until late ch.6"),   // A Shrunken Voodoo Doll
            E(195, 4, 7, "keep until Sadistic"),            // A Small Gerbil ("Lemmiwinks")

            // --- Chapter 5 (Evil-IDP): "Keep Forever"
            E(236, 5, 9, "daycare / quest drops"),          // A Pretty Pink Bow
            E(446, 5, 9, "respawn / daycare"),              // Creepy Doll
            E(246, 5, 9, "respawn"),                        // Anime Bodypillow
            E(248, 5, 9, "daycare"),                        // A Bag of Trash
            E(256, 5, 9, "move cooldown"),                  // Infinity Charm
            E(264, 5, 9, "Ygg yield"),                      // Party Whistle
            // "Keep Temporarily"
            E(445, 5, 8, "NGU speed, through early Sadistic"),   // Edgy Magicite Crystal
            E(242, 5, 8, "NGU speed, through early Sadistic"),   // An Ordinary Calculator
            E(247, 5, 6, "augment speed"),                       // Red Meeple Thingy
            E(249, 5, 8, "NGU speed, through early Sadistic"),   // Heart Shaped Panties
            E(257, 5, 6, "Wandoos"),                             // 69 Charm
            E(297, 5, 6, "R3, then only while it fits a Hack loadout"), // My Grey Heart <3

            // --- Chapter 6 (T8-JRPG): "Keep Forever"
            E(270, 6, 9, "daycare"),                        // A Garrote
            E(275, 6, 9, "Ygg yield"),                      // The Godmother's Wand
            E(306, 6, 9, "Ygg yield"),                      // The Ass-cessory
            E(321, 6, 9, "Ygg yield"),                      // Anime Hero Wig
            E(344, 6, 9, "daycare / wish speed"),           // My Pink Heart <3
            // "Keep Temporarily"
            E(273, 6, 7, "Wandoos / seed gain"),                       // Molotov Cocktail
            E(312, 6, 8, "EM/R3, until early Sadistic"),               // THE MALF SLAMMER
            E(274, 6, 8, "drop chance, a rarer stat from here"),       // The Godmother's Ring
            E(451, 6, 8, "drop chance, a rarer stat from here"),       // A Hand Cursor

            // --- Chapter 7 (T9): "Keep Forever"
            E(327, 7, 9, "cooking / wish speed / R3"),      // The Joker
            E(390, 7, 9, "cooking / R3 bars / seeds"),      // My Rainbow Heart
            // "Keep Temporarily"
            E(342, 7, 8, "R3 / wish / hack, until mid Sadistic"),      // Blue Eyes Ultimate Chestplate
            E(329, 7, 8, "EM bars, a rarer stat from here"),           // The Credit Card
            E(330, 7, 8, "R3 bars, a rarer stat from here"),           // Tentacle of the Exile
            E(331, 7, 8, "NGU speed, through early Sadistic"),         // The Skip Card
            E(351, 7, 8, "augment speed, best aug item"),              // The Glove of Power
            E(452, 7, 8, "through early Sadistic"),                    // Rad Mixtape

            // --- Chapter 8 (Sadistic): "Keep Forever"
            E(379, 8, 9, "respawn"),                        // A Red Shirt
            E(383, 8, 9, "Ygg yield"),                      // An Inanimate Carbon Rod
            E(501, 8, 9, "EM cap / respawn"),               // Some Duck-t Tape
            E(513, 8, 9, "respawn"),                        // A Compass!
            // "Keep Temporarily" (chapter 8 is terminal, so these are effectively forever too)
            E(392, 8, 9, "EM cap until hardcap"),           // Bread Bowl Helmet
            E(407, 8, 9, "EM cap until hardcap"),           // A Vinyl Record Shard
        };

        private static Dictionary<int, Entry> _byId;
        private static Dictionary<int, Entry> ById()
        {
            if (_byId == null)
            {
                var d = new Dictionary<int, Entry>(Entries.Length);
                foreach (var e in Entries) d[e.Id] = e;
                _byId = d;
            }
            return _byId;
        }

        public static bool TryGet(int id, out Entry e) => ById().TryGetValue(id, out e);

        // Is the guide's hold still in force at this chapter? Chapter 0 = unknown -> hold everything
        // (conservative: an un-detected chapter must never be the reason an item lands in TRASH).
        public static bool KeepActive(Entry e, int chapter) => chapter <= 0 || chapter <= e.KeepUntil;
    }
}
