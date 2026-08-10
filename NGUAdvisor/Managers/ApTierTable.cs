using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    /// <summary>
    /// The static "what should I spend AP (Arbitrary Points) on next?" plan.
    ///
    /// Two very different kinds of fact are mixed into this table, and they must not be confused:
    ///
    /// - The ORDERING (tier and rank) is one player's opinion: OJ of Steel's AP Tier List, build
    ///   1.200. Its own introduction admits uncertainty about late-game entries. It is a plan, not
    ///   game truth, and any UI surfacing this table must say so.
    /// - The IDS (<see cref="ApItem.Key"/>) and the ownership semantics ARE decomp-derived game
    ///   truth, read directly off <c>ArbitraryController.shouldDisableBuyButton(int id)</c> in the
    ///   decompiled Assembly-CSharp.dll. They are transcribed verbatim from that switch and must not
    ///   be re-derived, re-ordered, or "corrected" here.
    ///
    /// The Yellow Heart's Tier 0 placement is the one claim in this table that decomp evidence
    /// actually backs: <c>Character.addAP</c> branches on
    /// <c>inventory.itemList.itemMaxxed[129]</c> — a maxxed Yellow Heart multiplies AP by 1.2
    /// instead of applying the gear AP bonus. That is why its <see cref="ApItem.Note"/> cites item
    /// id 129.
    ///
    /// <see cref="ApItem.Note"/> is QUOTED guidance transcribed verbatim from OJ of Steel's AP Tier
    /// List, build 1.200 — never a paraphrase or a description written for this file. Where the
    /// source gives no per-item guidance, <c>Note</c> is the empty string; an empty note is honest,
    /// an invented one is not. Do not "fill in" a note that isn't there.
    ///
    /// The ONE exception is a suffix introduced by <c>· [advisor] </c>. Anything after that marker is
    /// ours, not OJ of Steel's, and it exists only where a row needs a fact the tier list does not
    /// carry — currently the PP and EXP rows, which are priced at their cheapest purchase tier and
    /// name their other tiers so the panel is not silently quoting the smallest bundle. Never put
    /// advisor text before the marker, and never edit the quoted part that precedes it.
    ///
    /// This file is Unity-free by design: no <c>Main</c>, no <c>Character</c>, no UnityEngine types.
    /// It is linked directly into the net9.0 test project, and a live-game reference here would break
    /// that build. The live binding (resolving a <see cref="ApSource.ShopId"/> or
    /// <see cref="ApSource.Heart"/> key against the running game) belongs to a separate,
    /// Unity-dependent advisor that consumes this table.
    /// </summary>
    public enum ApSource
    {
        ShopId,
        Heart,
        Repeatable
    }

    public class ApItem
    {
        public readonly string Name;
        public readonly int Tier;
        public readonly int Rank;
        public readonly ApSource Source;
        public readonly int Key;

        /// <summary>
        /// The <c>ArbitraryController</c> id whose <c>cost()</c> prices this row, when that is NOT the
        /// same number as <see cref="Key"/>. <c>0</c> means "use <see cref="Key"/>".
        ///
        /// Ownership and pricing are two different questions with two different keys, which is why
        /// this field exists. For a <see cref="ApSource.ShopId"/> row they coincide — the shop entry
        /// both answers <c>shouldDisableBuyButton</c> and quotes its own price — so those rows leave
        /// this 0. Hearts are keyed by ITEM id for ownership (<c>itemDropped[Key]</c>) but still have
        /// a shop pod that prices them, and <see cref="ApSource.Repeatable"/> rows have a pod per
        /// purchase tier while having no ownership state at all. Both therefore carry an explicit
        /// cost id, transcribed from the <c>ArbitraryController.cost()</c> switch in the decompiled
        /// Assembly-CSharp.dll.
        /// </summary>
        public readonly int CostId;

        public readonly string Note;

        public ApItem(string name, int tier, int rank, ApSource source, int key, string note, int costId = 0)
        {
            Name = name;
            Tier = tier;
            Rank = rank;
            Source = source;
            Key = key;
            Note = note;
            CostId = costId;
        }
    }

    public static class ApTierTable
    {
        // Declared in tier-then-rank order. This IS the plan order — Unowned/NextUnowned must not
        // re-sort it, since a defensive sort would silently hide a mis-ranked row.
        public static readonly IReadOnlyList<ApItem> Items = new List<ApItem>
        {
            new ApItem("ILF (improved loot filter)", 0, 1, ApSource.ShopId, 7, ""),
            new ApItem("Yellow Heart", 0, 2, ApSource.Heart, 129, "Maxxed multiplies AP itself by 1.2 (Character.addAP branches on itemMaxxed[129]) instead of the usual gear AP bonus. AP% is super important to every other purchase", 14),

            new ApItem("Red Heart", 1, 1, ApSource.Heart, 119, "If you have an open Daycare slot", 11),
            new ApItem("AP Beard Slots", 1, 2, ApSource.ShopId, 28, "2-4 Total; Beard Slot 2 should total to 5, useful for early Evil; slots 3-4: beards are nice, but not essential to progress"),
            new ApItem("Acc slot 1", 1, 3, ApSource.ShopId, 17, ""),
            new ApItem("Acc slot 2", 1, 4, ApSource.ShopId, 34, ""),
            new ApItem("Green Heart", 1, 5, ApSource.Heart, 171, "Get when you got an open Daycare Slot", 33),
            new ApItem("Grey Heart", 1, 6, ApSource.Heart, 297, "Worth saving for if you are close to T7", 63),
            new ApItem("Pink Heart", 1, 7, ApSource.Heart, 344, "Worth saving for if you are close to T8", 70),
            new ApItem("Rainbow Heart", 1, 8, ApSource.Heart, 390, "The set bonus is good, should be purchased when unlocked", 80),

            new ApItem("Digger slots", 2, 1, ApSource.ShopId, 40, "Digger 1: get this before Blue/Orange Heart if you have no open daycare slots. Diggers 8 and 9 are typically used for less important diggers, like full time blood or wandoos, useful if you are whaling, tough to justify for F2P players"),
            new ApItem("Filter Boosts into Infinity Cube", 2, 2, ApSource.ShopId, 29, "Get this when you aren't working on anything specifically"),
            new ApItem("Blue Heart", 2, 3, ApSource.Heart, 196, "Set Bonus affects Poop", 38),
            new ApItem("Orange Heart", 2, 4, ApSource.Heart, 293, "Get this over faster questing if you have an open Daycare slot", 50),
            new ApItem("Faster Questing", 2, 5, ApSource.ShopId, 48, "Essentially free goodies"),
            new ApItem("Faster Wishes", 2, 6, ApSource.ShopId, 68, "Ideally purchase when you can"),
            new ApItem("Acc slot 3", 2, 7, ApSource.ShopId, 54, "Accessory Slots are always useful"),
            new ApItem("Acc slot 4", 2, 8, ApSource.ShopId, 62, "Accessory Slots are always useful"),
            new ApItem("MacGuffin slots", 2, 9, ApSource.ShopId, 41, "1-2 really useful after T7; 3-11 up to you when you need more"),
            new ApItem("Daycare Speed Boost", 2, 10, ApSource.ShopId, 32, "When Daycare gets good, this is excellent, and retroactive"),
            new ApItem("Acc slot 5 (Evil)", 2, 11, ApSource.ShopId, 74, "aka Evil acc slots"),
            new ApItem("Acc slot 6 (Evil)", 2, 12, ApSource.ShopId, 81, "aka Evil acc slots"),
            new ApItem("Extra Tag Slot", 2, 13, ApSource.ShopId, 77, "Some people say to get it as soon as you unlock the feature, others say to wait till you have better things to use it for"),

            new ApItem("Insta Training Cap", 3, 1, ApSource.ShopId, 9, "Only worth once all of your basic training caps are reduced to 1"),
            new ApItem("1/2 Auto Merge and Boost Timers", 3, 2, ApSource.ShopId, 8, "15 minute Autoboost and Automerge alleviate inv. problems"),
            new ApItem("NGU Cap Modifier", 3, 3, ApSource.ShopId, 58, "This can be accomplished by different means"),
            new ApItem("Extended Quest Bank", 3, 4, ApSource.ShopId, 49, "Can be useful if you are running out of space"),
            new ApItem("Mayo Generator", 3, 5, ApSource.ShopId, 76, ""),
            new ApItem("Extra Deck Size", 3, 6, ApSource.ShopId, 75, ""),

            new ApItem("Loadout slots", 4, 1, ApSource.ShopId, 25, "1 is nice, anymore is typically for whales"),
            new ApItem("Lazy ITOPOD Floor Shifter", 4, 2, ApSource.ShopId, 39, "Saves some clicks and nets you slightly more pp, overall impact is small however"),
            new ApItem("Custom E/M % set 1", 4, 3, ApSource.ShopId, 12, "I haven't found a use for them, but others love them, useful if you find a use for them"),
            new ApItem("Custom E/M % set 2", 4, 4, ApSource.ShopId, 13, "I haven't found a use for them, but others love them, useful if you find a use for them"),
            new ApItem("Custom idle E/M % set 1", 4, 5, ApSource.ShopId, 55, "I haven't found a use for them, but others love them, useful if you find a use for them"),
            new ApItem("Custom R3 % set 1", 4, 6, ApSource.ShopId, 64, "I haven't found a use for them, but others love them, useful if you find a use for them"),
            new ApItem("Custom R3 % set 2", 4, 7, ApSource.ShopId, 65, "I haven't found a use for them, but others love them, useful if you find a use for them"),
            new ApItem("Custom idle R3 % set 1", 4, 8, ApSource.ShopId, 66, "I haven't found a use for them, but others love them, useful if you find a use for them"),
            new ApItem("Inventory Merge Slots", 4, 9, ApSource.ShopId, 69, "Don't typically need more than what can be obtained with other means, so mostly QoL"),
            new ApItem("Yggdrasil Harvest Light", 4, 10, ApSource.ShopId, 21, "Can be good to get early, but after Tier24 fruits, benefit goes down considerably"),
            new ApItem("Adventure Light", 4, 11, ApSource.ShopId, 71, "Has some use, but tough to justify the price"),

            new ApItem("Brown Heart", 5, 1, ApSource.Heart, 162, "Seed gain is really useful, but typically is only worth if you get it really early, otherwise there are better purchases. Completion Bonus is not very useful", 31),
            new ApItem("PP", 5, 2, ApSource.Repeatable, 0, "This is controversial, but some extra PP early on can speed progression considerably; only for hyper normal whales · [advisor] priced at the 25 PP tier (pod 51); also 100 PP (52) and 500 PP (53)", 51),
            new ApItem("Inv. Space", 5, 3, ApSource.ShopId, 15, "You really don't need more than what you get for free, but it's there if you want it"),
            new ApItem("EXP", 5, 4, ApSource.Repeatable, 0, "PP > EXP · [advisor] priced at the 200 EXP tier (pod 23); also 500 EXP (10) and 2K EXP (24)", 23),

            new ApItem("'Go To Quest Zone' Button", 6, 1, ApSource.ShopId, 73, "Actually a decent bit of QoL"),
            new ApItem("7-Day Time Bank for Daily Spin", 6, 2, ApSource.ShopId, 22, "Only real use is if you go on vacation"),
            new ApItem("Quest Reminder", 6, 3, ApSource.ShopId, 47, "It's really not that hard to check"),
            new ApItem("Auto Nuker", 6, 4, ApSource.ShopId, 56, "Don't own it, don't plan on it"),
            new ApItem("Adventure Advancer", 6, 5, ApSource.ShopId, 72, "4g said it best, it's for lazy people"),
            new ApItem("Resource 3 Name Randomizer", 6, 6, ApSource.ShopId, 67, "Lulz"),

            new ApItem("Purple Heart", 7, 1, ApSource.Heart, 212, "This is an awful purchase, through and through. One benefit is to max it so you can say you maxed every item. Post T8 purple heart has some use as guff levels increase", 42),
        };

        public static IEnumerable<ApItem> Unowned(Func<ApItem, bool> owned)
            => Items.Where(i => !owned(i));

        public static ApItem NextUnowned(Func<ApItem, bool> owned)
            => Unowned(owned).FirstOrDefault();
    }
}
