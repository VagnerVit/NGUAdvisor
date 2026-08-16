using System;
using System.Collections.Generic;
using System.Linq;
using static NGUAdvisor.Main;

namespace NGUAdvisor.Managers
{
    public class FixedSizedQueue
    {
        private Queue<float> queue = new Queue<float>();

        public int Size { get; private set; }

        public FixedSizedQueue(int size)
        {
            Size = size;
        }

        public void Enqueue(float obj)
        {
            queue.Enqueue(obj);

            while (queue.Count > Size)
                queue.Dequeue();
        }

        public void Reset() => queue.Clear();

        public float Avg()
        {
            try
            {
                return queue.Average();
            }
            catch (Exception e)
            {
                Log(e.Message);
                return 0;
            }
        }
    }

    public class Cube
    {
        public float Power { get; set; }

        public float Toughness { get; set; }

        public bool Changed(float power, float toughness) => Power != power || Toughness != toughness;
    }

    public static class InventoryManager
    {
        private static readonly Character _character = Main.Character;
        private static readonly InventoryController _ic = Main.InventoryController;
        // Pendants, Lootys, Wanderer's Cane, Lonely Flubber, A Giant Seed
        private static readonly int[] _convertibles = { 53, 67, 76, 92, 94, 120, 128, 142, 154, 169, 170, 229, 230, 295, 296, 388, 389, 430, 431, 504, 505 };
        private static readonly int[] _filterExcludes = { 119, 129, 162, 171, 195, 196, 212, 293, 297, 344, 390 }; // Lemmi and Hearts
        private static BoostsNeeded _previousBoostsNeeded;
        private static readonly Cube _cube = new Cube { Power = Inventory.cubePower, Toughness = Inventory.cubeToughness };
        private static readonly FixedSizedQueue _invBoostAvg = new FixedSizedQueue(60);
        private static readonly FixedSizedQueue _cubeBoostAvg = new FixedSizedQueue(60);
        private static int[] _savedMacguffins = null;
        private static int _daycareSlot = -1;

        private static Inventory Inventory => _character.inventory;

        public static readonly Dictionary<int, string> macguffinList = new Dictionary<int, string>
        {
            { -1, "None" },
            { 198, "Energy Power" },
            { 199, "Energy Cap" },
            { 200, "Magic Power" },
            { 201, "Magic Cap" },
            { 202, "Energy NGU" },
            { 203, "Magic NGU" },
            { 204, "Energy Bar" },
            { 205, "Magic Bar" },
            { 206, "SEXY" },
            { 207, "SMART" },
            { 208, "Drop Chance" },
            { 209, "Golden" },
            { 210, "Augment" },
            { 211, "Energy Wandoos" },
            { 228, "Stat" },
            { 250, "Magic Wandoos" },
            { 289, "NUMBER" },
            { 290, "Blood" },
            { 291, "Adventure" },
            { 298, "Resource 3 Power" },
            { 299, "Resource 3 Cap" },
            { 300, "Resource 3 Bars" }
        };

        public static void Reset()
        {
            _invBoostAvg.Reset();
            _cubeBoostAvg.Reset();
        }

        // THE priority list is the ONLY source of boosting (spec 2026-07-28). It used to be one of three:
        // the list, every equipped item, and every locked inventory item — so removing an item from the
        // list did not stop it being boosted, and the blacklist was the only "never boost this" lever.
        // Main.Start seeds the two implicit groups into the list ONCE (BoostSeed) so this is not a silent
        // behavior change; from then on the list is exactly what gets boosted, in its own order.
        //
        // `ci` is unused now and kept only because callers pass their existing snapshot.
        public static ih[] GetBoostSlots(ih[] ci)
        {
            List<ih> result = new List<ih>();
            int[] priority = Settings.PriorityBoosts;
            if (priority == null) return new ih[0];

            foreach (int id in priority)
            {
                ih f = LoadoutManager.FindItemSlot(id);
                if (f?.equipment.isEquipment() != true) continue;
                // Transform protection is NOT part of the retired blacklist: a maxed chain copy the user
                // holds back must never be boosted, because applying a boost runs the game's
                // checkItemTransform and would trigger the transformation.
                if (TransformManager.Frozen(f)) continue;
                result.Add(f);
            }

            return result.FindAll(x => x.equipment.GetNeededBoosts().Total() > 0).ToArray();
        }

        // Drops priority-list entries the player no longer owns (user request: trashing an item in game
        // should take it off the boost list too). Returns the number removed, 0 when nothing changed.
        //
        // OWNERSHIP IS CHECKED WIDER THAN THE BOOST PATH ON PURPOSE. GetBoostSlots resolves through
        // LoadoutManager.FindItemSlot, which searches equipped + inventory only — daycare is not in it.
        // Pruning on that test would delete the shockwave/levelling set out of the user's list the moment
        // it went into daycare, which is data loss, not tidying. So daycare and the MacGuffin slots count
        // as owned here.
        //
        // SAFETY: if the inventory is not populated yet, EVERY id reads as unowned and the whole list
        // would be wiped. That is the same trap the one-time seed hit, so the same guard applies — no
        // inventory, no pruning.
        public static int PruneUnownedPriorityBoosts()
        {
            try
            {
                int[] priority = Settings?.PriorityBoosts;
                if (priority == null || priority.Length == 0) return 0;

                var inv = Main.Character?.inventory;
                if (inv?.inventory == null || inv.inventory.Count == 0) return 0;

                var owned = new HashSet<int>();
                void Own(Equipment e) { if (e != null && e.id != 0) owned.Add(e.id); }

                Own(inv.weapon);
                try { if (_ic.weapon2Unlocked()) Own(inv.weapon2); } catch (Exception ex) { LogDebug($"Prune weapon2: {ex.Message}"); }
                Own(inv.head); Own(inv.chest); Own(inv.legs); Own(inv.boots);
                if (inv.accs != null) foreach (var a in inv.accs) Own(a);
                foreach (var e in inv.inventory) Own(e);
                if (inv.daycare != null) foreach (var e in inv.daycare) Own(e);
                try { if (inv.macguffins != null) foreach (var e in inv.macguffins) Own(e); }
                catch (Exception ex) { LogDebug($"Prune macguffins: {ex.Message}"); }

                var kept = new List<int>();
                var dropped = new List<int>();
                foreach (int id in priority)
                {
                    if (owned.Contains(id)) kept.Add(id);
                    else dropped.Add(id);
                }
                if (dropped.Count == 0) return 0;

                Settings.PriorityBoosts = kept.ToArray();
                Log($"Boost priority: dropped {dropped.Count} item(s) you no longer own — "
                    + string.Join(", ", dropped.Select(id => $"{Main.ItemNameNice(id)} (#{id})").ToArray()));
                return dropped.Count;
            }
            catch (Exception e)
            {
                LogDebug($"PruneUnownedPriorityBoosts: {e.Message}");
                return 0;
            }
        }

        public static void BoostInventory(ih[] boostSlots)
        {
            foreach (var item in boostSlots)
            {
                if (!Inventory.HasBoosts())
                    break;
                _ic.applyAllBoosts(item.slot);
            }
        }

        private static int ChangePage(int slot)
        {
            var page = slot / 60;
            _ic.changePage(page);
            return slot - (page * 60);
        }

        public static void BoostInfinityCube()
        {
            if (!Inventory.HasBoosts())
                return;
            _ic.infinityCubeAll();
        }

        public static void MergeEquipped(ih[] ci)
        {
            if (!MergeBlockedId(Inventory.head.id) && Array.Exists(ci, x => x.id == Inventory.head.id))
                _ic.mergeAll(-1);

            if (!MergeBlockedId(Inventory.chest.id) && Array.Exists(ci, x => x.id == Inventory.chest.id))
                _ic.mergeAll(-2);

            if (!MergeBlockedId(Inventory.legs.id) && Array.Exists(ci, x => x.id == Inventory.legs.id))
                _ic.mergeAll(-3);

            if (!MergeBlockedId(Inventory.boots.id) && Array.Exists(ci, x => x.id == Inventory.boots.id))
                _ic.mergeAll(-4);

            if (!MergeBlockedId(Inventory.weapon.id) && Array.Exists(ci, x => x.id == Inventory.weapon.id))
                _ic.mergeAll(-5);

            if (_ic.weapon2Unlocked())
            {
                if (!MergeBlockedId(Inventory.weapon2.id) && Array.Exists(ci, x => x.id == Inventory.weapon2.id))
                    _ic.mergeAll(-6);
            }

            // Merge Accessories
            for (var i = 10000; _ic.accessoryID(i) < _ic.accessorySpaces(); i++)
            {
                int id = _ic.accessoryID(i);
                if (!MergeBlockedId(Inventory.accs[id].id) && Array.Exists(ci, x => x.id == Inventory.accs[id].id))
                    _ic.mergeAll(i);
            }
        }

        public static void MergeBoosts(ih[] ci)
        {
            var grouped = Array.FindAll(ci, x => IsBoost(x) && IsLocked(x) && !IsMaxxed(x));
            foreach (var target in grouped)
            {
                if (ci.Count(x => x.id == target.id) <= 1)
                    continue;
                Log($"Merging {target.name} in slot {target.slot}");
                _ic.mergeAll(target.slot);
            }
        }

        private static string SanitizeName(string name)
        {
            if (name.Contains("\n"))
                name = name.Split('\n').Last();

            return name;
        }

        public static void ManageQuestItems(ih[] ci)
        {
            var questItems = Array.FindAll(ci, x => IsQuest(x) && !IsBlacklisted(x) && IsLocked(x) && !IsMaxxed(x));

            // Merge non-maxxed quest items first
            foreach (var item in questItems)
            {
                Log($"Merging {SanitizeName(item.name)} in slot {item.slot}");
                _ic.mergeAll(item.slot);
            }

            // Consume quest items that dont need to be merged
            var quest = Main.Character.beastQuest;
            if (quest.inQuest)
            {
                int num = quest.curDrops;
                _ic.dumpAllIntoQuest(quest.questID);
                if (quest.curDrops > num)
                    Log($"Turning in {quest.curDrops - num} quest items");

                // Surplus purge (user-reported: a FULL INVENTORY of Diploma 287 after a capstone
                // hold). The game rolls quest drops on every manual-mode kill with NO at-target
                // check, but checkItemConsumed refuses items once curDrops >= targetDrops — so
                // every copy dropped while a finished quest is held is dead weight the game will
                // never count, and it starves gear/boost drops. Delete them (removable slots only).
                if (quest.curDrops >= quest.targetDrops && quest.targetDrops > 0)
                {
                    var inv = Main.Character.inventory;
                    int purged = 0;
                    for (int i = inv.inventory.Count - 1; i >= 0; i--)
                    {
                        var it = inv.inventory[i];
                        if (it == null || it.id != quest.questID || !it.removable) continue;
                        inv.deleteItem(i);
                        purged++;
                    }
                    if (purged > 0)
                    {
                        _ic.updateInventory();
                        Log($"Purged {purged} surplus quest item(s) — quest already at target, extras never count");
                        ChallengeOverlay.Record("QUEST", $"purged {purged} surplus quest items", "drops past the quest target never count");
                    }
                }
            }
        }

        public static void MergeInventory(ih[] ci)
        {
            var filtered = Array.FindAll(ci, x => !IsBoost(x) && x.level < 100 && !IsCooking(x) && !MergeBlocked(x) && !IsGuff(x) && !IsQuest(x));
            var grouped = filtered.GroupBy(x => x.id).Where(x => x.Count() > 1);

            foreach (var item in grouped)
            {
                // All-locked groups can't merge (the game only consumes removable sources) — except
                // transform-chain items (user-reported 3x locked Sir Looty pileup): those we unlock,
                // merge, and re-lock whatever survives so nothing is left trash-exposed.
                bool allLocked = item.All(x => x.locked);
                if (allLocked && !TransformManager.ChainItem(item.Key))
                    continue;

                ih target = item.MaxItem();

                if (allLocked)
                {
                    foreach (var src in item)
                        if (src.slot != target.slot && src.slot >= 0 && src.slot < Inventory.inventory.Count)
                            Inventory.inventory[src.slot].removable = true;
                }

                Log($"Merging {SanitizeName(target.name)} in slot {target.slot}");
                _ic.mergeAll(target.slot);

                if (allLocked)
                {
                    // Any copy the merge didn't consume gets its lock back.
                    for (int slot = 0; slot < Inventory.inventory.Count; slot++)
                    {
                        var e = Inventory.inventory[slot];
                        if (e != null && e.id == item.Key && e.removable)
                            e.removable = false;
                    }
                }
            }
        }

        public static void MergeGuffs(ih[] ci)
        {
            for (var id = 0; id < Inventory.macguffins.Count; ++id)
            {
                int guffId = Inventory.macguffins[id].id;
                if (!IsBlacklisted(guffId) && Array.Exists(ci, x => x.id == guffId))
                    _ic.mergeAll(_ic.globalMacguffinID(id));
            }

            var invGuffs = Array.FindAll(ci, x => IsGuff(x) && !IsBlacklisted(x)).GroupBy(x => x.id).Where(x => x.Count() > 1);
            foreach (var guff in invGuffs)
            {
                ih target = guff.MaxItem();
                _ic.mergeAll(target.slot);
            }
        }

        public static void ManageConvertibles(ih[] ci)
        {
            int curPage = _ic.inventory[0].id / 60;
            var grouped = ci.Where(x => Array.BinarySearch(_convertibles, x.id) >= 0);
            foreach (var item in grouped)
            {
                if (item.level != 100)
                    continue;
                // Never consume a transform-chain tier or a KEEP-MAX/HOLD-protected at-100 copy:
                // TransformManager owns chain items; consuming one destroys climb/keep progress (data loss).
                if (TransformManager.MergeAllowed(item.id).HasValue || TransformManager.Frozen(item))
                    continue;
                var temp = Inventory.inventory[item.slot];
                if (!temp.removable)
                    continue;
                var newSlot = ChangePage(item.slot);
                _ic.inventory[newSlot].CallMethod("consumeItem");
            }
            _ic.changePage(curPage);
        }

        public static void ShowBoostProgress(ih[] boostSlots)
        {
            var needed = new BoostsNeeded();

            foreach (var item in boostSlots)
                needed += item.equipment.GetNeededBoosts();

            float current = needed.Total();

            if (current > 0)
            {
                if (_previousBoostsNeeded == null)
                {
                    Log($"Boosts Needed to Green: {needed.power} Power, {needed.toughness} Toughness, {needed.special} Special");
                    _previousBoostsNeeded = needed;
                }
                else
                {
                    float old = _previousBoostsNeeded.Total();

                    var diff = current - old;
                    if (diff == 0)
                        return;

                    // If diff is > 0, then we either added another item to boost or we levelled something. Don't add the diff to average
                    if (diff <= 0)
                        _invBoostAvg.Enqueue(-diff);

                    Log($"Boosts Needed to Green: {needed.power} Power, {needed.toughness} Toughness, {needed.special} Special");
                    float average = _invBoostAvg.Avg();
                    if (average > 0)
                    {
                        var eta = current / average;
                        Log($"Last Minute: {diff}. Average Per Minute: {average:0}. ETA: {eta:0} minutes.");
                    }
                    else
                    {
                        Log($"Last Minute: {diff}.");
                    }

                    _previousBoostsNeeded = needed;
                }
            }

            var power = Inventory.cubePower;
            var toughness = Inventory.cubeToughness;

            if (_cube.Changed(power, toughness))
            {
                var output = "Cube Progress:";
                float toughnessDiff = toughness - _cube.Toughness;
                float powerDiff = power - _cube.Power;

                output = toughnessDiff > 0 ? $"{output} {toughnessDiff} Toughness." : output;
                output = powerDiff > 0 ? $"{output} {powerDiff} Power." : output;

                _cubeBoostAvg.Enqueue(toughnessDiff + powerDiff);
                output = $"{output} Average Per Minute: {_cubeBoostAvg.Avg():0}";
                Log(output);
                Log($"Cube Power: {power} ({_ic.cubePowerSoftcap()} softcap). Cube Toughness: {toughness} ({_ic.cubeToughnessSoftcap()} softcap)");

                _cube.Power = power;
                _cube.Toughness = toughness;
            }
        }

        // WHAT THIS NO LONGER DOES: pick the boost auto-transform type.
        //
        // It used to own that setting outright — locked boost wins, else BoostPriority against the gear's
        // actual need, else CubePriority — through the game's own `_ic.selectAuto*Transform()` setters.
        // Nothing named it, which is how a second owner went unnoticed until TransformManager started
        // writing `settings.autoTransform` too and the two fought every ~30 s (user-reported: "T jumps for
        // a moment, then P overrides it"). ONE owner now: TransformManager.ApplyBoostTransform, which
        // carries the locked-boost rule over and answers to the user's explicit P/T/S/X choice.
        //
        // What remains here is the part that is genuinely about conversion, not about the type.
        public static void ManageBoostConversion(ih[] boostSlots)
        {
            if (_character.challenges.levelChallenge10k.curCompletions < _character.allChallenges.level100Challenge.maxCompletions)
                return;

            if (!Settings.AutoConvertBoosts)
                return;

            // MATERIALIZED, not a lazy Where: re-running GetConvertedInventory allocated an ih plus an
            // item-name lookup per occupied slot every time.
            List<ih> lockedBoosts = Inventory.GetConvertedInventory().Where(x => x.id < 40 && x.locked).ToList();

            // Unlock level 100 boosts — a maxed padlocked boost has nothing left to gain from being held.
            foreach (var maxLockedBoost in lockedBoosts)
                if (maxLockedBoost.level == 100)
                    Inventory.inventory[maxLockedBoost.slot].removable = true;
        }

        public static int MoveFromDaycareToInventory(Inventory inv, int slot)
        {
            int emptySlot = inv.inventory.FindIndex(x => x.id == 0);
            if (emptySlot < 0 || emptySlot > inv.inventory.Count)
                return -1;

            inv.item1 = slot;
            inv.item2 = emptySlot;

            _ic.swapDaycare();

            return emptySlot;
        }

        public static int MoveFromMacguffinsToInventory(Inventory inv, int slot)
        {
            int emptySlot = inv.inventory.FindIndex(x => x.id == 0);
            if (emptySlot < 0 || emptySlot > inv.inventory.Count)
                return -1;

            inv.item1 = slot;
            inv.item2 = emptySlot;

            _ic.swapMacguffin();

            return emptySlot;
        }

        public static void ManageFavoredMacguffin(bool spell = false, bool fruit = false)
        {
            if (Settings.FavoredMacguffin < 0)
                return;

            var inventory = Main.Character.inventory;
            int slot;
            _savedMacguffins = inventory.macguffins.Select(x => x.id).ToArray();
            if (Array.Exists(_savedMacguffins, x => x == Settings.FavoredMacguffin))
            {
                _daycareSlot = -1;
                slot = Array.IndexOf(_savedMacguffins, Settings.FavoredMacguffin) + 1000000;
            }
            else if (inventory.inventory.Exists(x => x.id == Settings.FavoredMacguffin))
            {
                _daycareSlot = -1;
                // Equip highest level MacGuffin
                var item = inventory.inventory.Where(x => x.id == Settings.FavoredMacguffin).AllMaxBy(x => x.level).First();
                slot = inventory.inventory.IndexOf(item);
            }
            else if (inventory.daycare.Exists(x => x.id == Settings.FavoredMacguffin))
            {
                var item = inventory.daycare.First(x => x.id == Settings.FavoredMacguffin);
                _daycareSlot = inventory.daycare.IndexOf(item) + 100000;
                slot = MoveFromDaycareToInventory(inventory, _daycareSlot);
                if (slot < 0)
                {
                    try
                    {
                        Log("Failed to move an item from daycare: missing empty slots in the inventory.");
                    }
                    catch (Exception)
                    {
                        // pass
                    }
                    return;
                }
            }
            else
            {
                _daycareSlot = -1;
                return;
            }

            if (slot != 1000000)
            {
                inventory.item2 = slot;
                inventory.item1 = 1000000;
                _ic.swapMacguffin();
            }

            if ((!spell || Main.Character.wishes.wishes[24].level <= 0) && (!fruit || Main.Character.wishes.wishes[25].level <= 0))
            {
                for (var i = 1; i < inventory.macguffins.Count; i++)
                {
                    if (inventory.macguffins[i].id != Settings.FavoredMacguffin)
                    {
                        if (MoveFromMacguffinsToInventory(inventory, i + 1000000) < 0)
                        {
                            try
                            {
                                Log("Failed to unequip a macguffin: missing empty slots in the inventory.");
                            }
                            catch (Exception)
                            {
                                // pass
                            }
                            break;
                        }
                    }
                }
            }

            _ic.updateBonuses();
            _ic.updateInventory();
        }

        public static void RestoreMacguffins()
        {
            if (_savedMacguffins?.Length > 0 == false)
                return;

            var macguffins = Inventory.macguffins.Select(x => x.id);
            var allMacguffins = Inventory.macguffins.Select((x, i) => (equip: x, i: i + 1000000)).Where(x => x.equip.id != 0);
            allMacguffins = allMacguffins.Union(Inventory.inventory.Select((x, i) => (equip: x, i)).Where(x => x.equip.id != 0));
            for (var i = 0; i < _savedMacguffins.Length; i++)
            {
                if (_savedMacguffins[i] != macguffins.ElementAt(i))
                {
                    if (allMacguffins.Any(x => x.equip.id == _savedMacguffins[i]))
                    {
                        Inventory.item1 = i + 1000000;
                        // Equip highest level MacGuffins
                        Inventory.item2 = allMacguffins.Where(x => x.equip.id == _savedMacguffins[i]).AllMaxBy(x => x.equip.level).First().i;

                        _ic.swapMacguffin();
                    }
                    else
                    {
                        Log($"Failed to find a macguffin with id {_savedMacguffins[i]}.");
                    }
                }
            }

            if (_daycareSlot >= 0)
            {
                var favMacguffins = allMacguffins.Where(x => x.i < 1000000 && x.equip.id == Settings.FavoredMacguffin);
                if (favMacguffins.Any())
                {
                    Inventory.item1 = _daycareSlot;
                    // Put lowest level MacGuffin into daycare
                    Inventory.item2 = favMacguffins.AllMinBy(x => x.equip.level).First().i;

                    _ic.swapDaycare();
                }
                else
                {
                    Log($"Failed to find a macguffin with id {Settings.FavoredMacguffin}.");
                }
            }

            _savedMacguffins = null;
            _daycareSlot = -1;

            _ic.updateBonuses();
            _ic.updateInventory();
        }

        #region Filtering
        public static void EnsureFiltered(ih[] ci)
        {
            if (!_character.arbitrary.lootFilter)
                return;

            var targets = Array.FindAll(ci, x => x.level == 100);
            foreach (var target in targets)
                FilterItem(target.id);

            FilterEquip(Inventory.head);
            FilterEquip(Inventory.boots);
            FilterEquip(Inventory.chest);
            FilterEquip(Inventory.legs);
            FilterEquip(Inventory.weapon);
            if (_ic.weapon2Unlocked())
                FilterEquip(Inventory.weapon2);

            foreach (var acc in Inventory.accs)
                FilterEquip(acc);
        }

        private static void FilterItem(int id)
        {
            // Don't filter out wandoos 98 if it is not level 100
            if (id == 66 && _character.wandoos98.OSlevel < 100L)
                return;

            // Don't filter out wandoos XL if it is not level 100
            if (id == 163 && _character.wandoos98.XLLevels < 100L)
                return;

            // Don't filter out convertibles
            if (Array.BinarySearch(_convertibles, id) >= 0)
                return;

            // Don't filter out MacGuffins
            if (macguffinList.ContainsKey(id))
                return;

            // Don't filter out cooking
            if (id >= 367 && id <= 372)
                return;

            // Don't filter out Lemmi and hearts
            if (Array.BinarySearch(_filterExcludes, id) >= 0)
                return;

            // Dont filter out quest items
            if (id >= 278 && id <= 287)
                return;

            // Don't filter out boosts
            if (id < 40)
                return;

            Inventory.itemList.itemFiltered[id] = true;
        }

        private static void FilterEquip(Equipment e)
        {
            if (e.level == 100)
                FilterItem(e.id);
        }
        #endregion

        #region Lambda
        // Quest-item and MacGuffin-merge exclusion only (ManageQuestItems, MergeGuffs) — the boost path
        // calls TransformManager.Frozen directly now. Settings.BoostBlacklist survives as a persisted
        // field so settings.json round-trips and an older-DLL rollback still finds the data.
        private static bool IsBlacklisted(ih x) => Settings.BoostBlacklist.Contains(x.id) || TransformManager.Frozen(x);

        private static bool IsBlacklisted(int id) => Settings.BoostBlacklist.Contains(id);

        // Frozen = transform-chain protection (TransformManager): a maxed chain item whose transform the
        // user is holding back (Keep max lvl, or Auto-climb off) must not be boosted or merged — both
        // paths run the game's checkItemTransform and would trigger the transformation.
        //
        // The boost blacklist that used to live here is RETIRED (spec 2026-07-28): boosting is driven by
        // the priority list alone, and merging is governed by the chain toggles that actually govern it.
        // Its second job — blocking merges — had already needed an exception carved out of it (blacklisted
        // Sir Lootys at lv 0/5/77 never merged), which is what a rule serving two purposes looks like.
        private static bool MergeBlocked(ih x)
        {
            var chain = TransformManager.MergeAllowed(x.id);
            if (chain.HasValue) return !chain.Value || TransformManager.Frozen(x);
            return false;
        }

        private static bool MergeBlockedId(int id)
        {
            var chain = TransformManager.MergeAllowed(id);
            if (chain.HasValue) return !chain.Value;
            return false;
        }

        private static bool IsLocked(ih x) => !Inventory.inventory[x.slot].removable;

        private static bool IsMaxxed(ih x) => Inventory.itemList.itemMaxxed[x.id];

        private static bool IsBoost(ih x) => x.id >= 1 && x.id <= 39;

        private static bool IsQuest(ih x) => x.id >= 278 && x.id <= 287;

        private static bool IsGuff(ih x) => macguffinList.ContainsKey(x.id);

        private static bool IsCooking(ih x) => x.id >= 367 && x.id <= 372;
        #endregion
    }

    public class ih
    {
        public int slot;
        public string name;
        public int level;
        public bool locked;
        public int id;
        public Equipment equipment;
    }

    public class BoostsNeeded
    {
        public float power;
        public float toughness;
        public float special;

        public BoostsNeeded(float power = 0f, float toughness = 0f, float special = 0f)
        {
            this.power = power;
            this.toughness = toughness;
            this.special = special;
        }

        public static BoostsNeeded operator +(BoostsNeeded boostsNeeded, BoostsNeeded other)
        {
            return new BoostsNeeded(
                boostsNeeded.power + other.power,
                boostsNeeded.toughness + other.toughness,
                boostsNeeded.special + other.special
            );
        }

        public float Total() => power + toughness + special;
    }
}
