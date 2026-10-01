using System;
using System.Collections.Generic;
using System.Linq;
using static NGUAdvisor.Main;

namespace NGUAdvisor.Managers
{
    public static class LoadoutManager
    {
        private static readonly Character _character = Main.Character;
        private static readonly InventoryController _ic = Main.InventoryController;

        private static int[] _savedLoadout;
        private static int[] _tempLoadout;
        private static int[] _savedDaycare;

        private static Inventory Inventory => _character.inventory;

        private static List<Equipment> Daycare => Inventory.daycare;

        public static void RestoreGear()
        {
            Log($"Restoring original loadout");
            ChangeGear(_savedLoadout);
        }

        public static void ChangeGear(int[] gearIds, bool shockwave = false)
        {
            if (gearIds?.Length > 0 == false)
                return;

            if (GetCurrentGear().Where(x => x > 0).Distinct().OrderBy(x => x).SequenceEqual(gearIds.Where(x => x > 0).Distinct().OrderBy(x => x))
                && (shockwave || !HasStrongerCopyToSwapIn(gearIds)))
                return;

            Log($"Received New Gear for {LockManager.GetLockTypeName()}: {string.Join(", ", gearIds)}");
            var headSwapped = false;
            var chestSwapped = false;
            var legsSwapped = false;
            var bootsSwapped = false;
            var weaponSlot = -5;
            var accSlot = 10000;

            _character.removeMostEnergy();
            _character.removeMostMagic();
            _character.removeAllRes3();

            try
            {
                if (!shockwave)
                    SwapInStrongerCopies(gearIds);

                foreach (var itemId in gearIds)
                {
                    var equip = FindItemSlot(itemId, shockwave);

                    // Still worn as the weaker copy (no free slot to swap through): handing the game the
                    // inventory copy would MERGE it into the worn one, so the worn copy stands.
                    if (!shockwave && equip != null && !IsWornSlot(equip.slot))
                        equip = WornCopy(itemId) ?? equip;

                    if (equip == null)
                    {
                        try
                        {
                            Log($"Missing item {_ic.itemInfo.itemName[itemId]} with ID {itemId}");
                        }
                        catch (Exception)
                        {
                            // pass
                        }

                        continue;
                    }

                    if (equip.slot >= 100000)
                    {
                        if (!equip.equipment.isEquipment())
                            continue;

                        var newSlot = InventoryManager.MoveFromDaycareToInventory(Inventory, equip.slot);
                        if (newSlot < 0)
                        {
                            try
                            {
                                Log("Failed to move an item from daycare: missing empty slots in the inventory.");
                            }
                            catch (Exception)
                            {
                                // pass
                            }

                            continue;
                        }
                        equip.slot = newSlot;
                    }

                    var type = equip.equipment.type;

                    Inventory.item2 = equip.slot;
                    switch (type)
                    {
                        case part.Head when !headSwapped:
                            Inventory.item1 = -1;
                            _ic.swapHead();
                            headSwapped = true;
                            break;
                        case part.Chest when !chestSwapped:
                            Inventory.item1 = -2;
                            _ic.swapChest();
                            chestSwapped = true;
                            break;
                        case part.Legs when !legsSwapped:
                            Inventory.item1 = -3;
                            _ic.swapLegs();
                            legsSwapped = true;
                            break;
                        case part.Boots when !bootsSwapped:
                            Inventory.item1 = -4;
                            _ic.swapBoots();
                            bootsSwapped = true;
                            break;
                        case part.Weapon when weaponSlot == -5:
                            Inventory.item1 = -5;
                            _ic.swapWeapon();
                            weaponSlot--;
                            break;
                        case part.Weapon when weaponSlot == -6 && _ic.weapon2Unlocked():
                            Inventory.item1 = -6;
                            _ic.swapWeapon2();
                            break;
                        case part.Accessory:
                            if (_ic.accessoryID(accSlot) < _ic.accessorySpaces() && accSlot != equip.slot)
                            {
                                Inventory.item1 = accSlot;
                                _ic.swapAcc();
                            }
                            accSlot++;
                            break;
                        default:
                            continue;
                    }
                }
            }
            catch (Exception e)
            {
                Log(e.Message);
                Log(e.StackTrace);
            }

            _ic.updateBonuses();
            _ic.updateInventory();

            UpdateResources();

            Log("Finished equipping gear");
        }

        // Equipped slot codes are -1..-6 (main slots) and 10000+ (accessories); the inventory is 0..,
        // daycare 100000+.
        private static bool IsWornSlot(int slot) => slot < 0 || (slot >= 10000 && slot < 100000);

        private static ih WornCopy(int id) => Inventory.GetConvertedEquips().FirstOrDefault(x => x.id == id);

        // A wanted id is worn as a weaker copy than the one FindItemSlot picks (the copy the optimizer's
        // pools score) and a free inventory slot exists to swap it through -- i.e. ChangeGear would act.
        public static bool HasStrongerCopyToSwapIn(IEnumerable<int> gearIds)
        {
            if (gearIds == null || !Inventory.inventory.Exists(x => x.id == 0)) return false;
            foreach (var id in gearIds.Where(x => x > 0).Distinct())
            {
                if (WornCopy(id) == null) continue;
                var best = FindItemSlot(id);
                if (best != null && !IsWornSlot(best.slot)) return true;
            }
            return false;
        }

        // The game MERGES an item dropped onto a slot holding the same id (InventoryController.mergeable)
        // and refuses a second worn copy of a weapon or accessory (alreadyEquipped). So a stronger copy
        // replaces the worn one in two swaps: the worn copy out to a free inventory slot, the stronger in.
        private static void SwapInStrongerCopies(int[] gearIds)
        {
            foreach (var id in gearIds.Where(x => x > 0).Distinct())
            {
                var worn = WornCopy(id);
                if (worn == null) continue;
                var best = FindItemSlot(id);
                if (best == null || IsWornSlot(best.slot)) continue;
                var free = Inventory.inventory.FindIndex(x => x.id == 0);
                if (free < 0)
                {
                    Log($"No free inventory slot to swap in the stronger copy of {Main.ItemName(id)} ({id})");
                    continue;
                }

                Inventory.item1 = worn.slot;
                Inventory.item2 = free;
                SwapWornSlot(worn.slot);
                if (Inventory.inventory[free].id != id)
                    continue;   // the game refused the swap, nothing moved

                Inventory.item1 = worn.slot;
                Inventory.item2 = best.slot;
                SwapWornSlot(worn.slot);
                Log($"Swapped in the stronger copy of {Main.ItemName(id)} ({id}): lvl {best.level} for lvl {worn.level}");
            }
        }

        private static void SwapWornSlot(int slot)
        {
            switch (slot)
            {
                case -1: _ic.swapHead(); break;
                case -2: _ic.swapChest(); break;
                case -3: _ic.swapLegs(); break;
                case -4: _ic.swapBoots(); break;
                case -5: _ic.swapWeapon(); break;
                case -6: _ic.swapWeapon2(); break;
                default: _ic.swapAcc(); break;
            }
        }

        public static void FillDaycare()
        {
            if (Settings.Shockwave.Length > 0)
            {
                var missingDaycare = Settings.Shockwave.Except(Daycare.Select(x => x.id));
                if (!missingDaycare.Any())
                    return;

                Log($"Putting gear into daycare: {string.Join(", ", missingDaycare)}");

                var availableSlots = new Queue<int>();
                for (int i = 0; i < Daycare.Count; i++)
                {
                    var slotInfo = Daycare[i];
                    if (slotInfo.id == 0)
                        availableSlots.Enqueue(i + 100000);
                }

                if (Settings.MoneyPitDaycare)
                {
                    for (int i = 0; i < Daycare.Count; i++)
                    {
                        var slotInfo = Daycare[i];
                        if (slotInfo.id == 0)
                            continue;
                        if (Array.IndexOf(Settings.Shockwave, slotInfo.id) >= 0)
                            continue;
                        if (_ic.daycares[i].daycareSlider.value < Settings.DaycareThreshold / 100f)
                            availableSlots.Enqueue(i + 100000);
                    }
                }

                foreach (var itemId in missingDaycare)
                {
                    if (availableSlots.Count <= 0)
                        break;

                    var equip = FindItemSlot(itemId, true);
                    if (equip == null)
                    {
                        try
                        {
                            Log($"Missing item {_ic.itemInfo.itemName[itemId]} with ID {itemId}");
                        }
                        catch (Exception)
                        {
                            // pass
                        }

                        continue;
                    }

                    if (equip.slot < 0 || _ic.accessoryID(equip.slot) >= 0)
                    {
                        var emptySlot = Inventory.inventory.FindIndex(x => x.id == 0);
                        if (emptySlot < 0)
                            continue;

                        _character.removeMostEnergy();
                        _character.removeMostMagic();
                        _character.removeAllRes3();

                        Inventory.item1 = equip.slot;
                        Inventory.item2 = emptySlot;
                        switch (equip.equipment.type)
                        {
                            case part.Head:
                                _ic.swapHead();
                                break;
                            case part.Chest:
                                _ic.swapChest();
                                break;
                            case part.Legs:
                                _ic.swapLegs();
                                break;
                            case part.Boots:
                                _ic.swapBoots();
                                break;
                            case part.Weapon:
                                _ic.swapWeapon();
                                break;
                            case part.Accessory:
                                _ic.swapAcc();
                                break;
                            default:
                                continue;
                        }
                    }
                    else
                    {
                        Inventory.item2 = equip.slot;
                    }
                    Inventory.item1 = availableSlots.Dequeue();
                    _ic.swapDaycare();
                }

                _ic.updateBonuses();
                _ic.updateInventory();

                UpdateResources();

                Log("Finished putting gear into daycare");
            }
        }

        public static ih FindItemSlot(int id, bool shockwave = false)
        {
            if (id <= 0)
                return null;

            var items = Inventory.GetConvertedEquips().Concat(Inventory.GetConvertedInventory()).Where(x => x.id == id);
            var isMacGuffin = InventoryManager.macguffinList.Keys.Contains(id);

            if (shockwave)
            {
                // MacGuffins don't hardcap at level 100
                if (!isMacGuffin)
                    items = items.Where(x => x.level < 100);

                // We want to upgrade highest level items
                if (items.Any())
                    return items.AllMaxBy(x => x.level).First();
            }
            else if (items.Any())
            {
                // We want to put lowest level MacGuffin into daycare
                if (isMacGuffin)
                    return items.AllMinBy(x => x.level).First();

                return items.MaxItem();
            }

            if (shockwave && Settings.MoneyPitDaycare)
            {
                var index = Daycare.FindIndex(x => x.id == id);
                if (index >= 0)
                {
                    var completion = Main.InventoryController.daycares[index].daycareSlider.value;
                    if (isMacGuffin || completion <= Settings.DaycareThreshold / 100f)
                    {
                        var helper = Daycare.First(x => x.id == id).GetInventoryHelper(index + 100000);
                        return helper;
                    }
                }
            }

            return null;
        }

        public static void SaveDaycare() => _savedDaycare = Daycare.Select(x => x.id).ToArray();

        public static void RestoreDaycare()
        {
            for (int i = 0; i < _savedDaycare?.Length; i++)
            {
                var item = _savedDaycare[i];

                if (Daycare[i].id == item)
                    continue;

                if (item == 0)
                {
                    InventoryManager.MoveFromDaycareToInventory(Inventory, i + 100000);
                }
                else
                {
                    if (Daycare.Find(x => x.id == item) != null)
                        continue;

                    var equip = FindItemSlot(item, true);
                    if (equip == null)
                    {
                        InventoryManager.MoveFromDaycareToInventory(Inventory, i + 100000);
                        continue;
                    }

                    if (equip.level == 100 && equip.equipment.type != part.MacGuffin)
                    {
                        InventoryManager.MoveFromDaycareToInventory(Inventory, i + 100000);
                        continue;
                    }

                    if (equip.slot < 0 || _ic.accessoryID(equip.slot) >= 0)
                    {
                        var emptySlot = Inventory.inventory.FindIndex(x => x.id == 0);
                        if (emptySlot < 0)
                            continue;

                        _character.removeMostEnergy();
                        _character.removeMostMagic();
                        _character.removeAllRes3();

                        Inventory.item1 = equip.slot;
                        Inventory.item2 = emptySlot;
                        switch (equip.equipment.type)
                        {
                            case part.Head:
                                _ic.swapHead();
                                break;
                            case part.Chest:
                                _ic.swapChest();
                                break;
                            case part.Legs:
                                _ic.swapLegs();
                                break;
                            case part.Boots:
                                _ic.swapBoots();
                                break;
                            case part.Weapon:
                                _ic.swapWeapon();
                                break;
                            case part.Accessory:
                                _ic.swapAcc();
                                break;
                            default:
                                continue;
                        }
                    }
                    else
                    {
                        Inventory.item2 = equip.slot;
                    }
                    Inventory.item1 = i + 100000;
                    _ic.swapDaycare();
                }
            }

            _ic.updateBonuses();
            _ic.updateInventory();

            UpdateResources();
        }

        private static void UpdateResources()
        {
            UpdateEnergy();
            UpdateMagic();
            UpdateRes3();
        }

        private static void UpdateEnergy()
        {
            if (_character.curEnergy >= _character.totalCapEnergy())
            {
                long num = _character.totalCapEnergy() - _character.curEnergy;
                _character.curEnergy += num;
                _character.idleEnergy += num;
            }
        }

        private static void UpdateMagic()
        {
            if (_character.magic.curMagic >= _character.totalCapMagic())
            {
                long num = _character.totalCapMagic() - _character.magic.curMagic;
                _character.magic.curMagic += num;
                _character.magic.idleMagic += num;
            }
        }

        private static void UpdateRes3()
        {
            if (_character.res3.curRes3 >= _character.totalCapRes3())
            {
                long num = _character.totalCapRes3() - _character.res3.curRes3;
                _character.res3.curRes3 += num;
                _character.res3.idleRes3 += num;
            }
        }

        // Public wrapper for the Loadouts tab's "Use Current Gear" (fills manual IDs from equipped).
        public static int[] CurrentGearIds()
        {
            try { return GetCurrentGear().Where(x => x > 0).ToArray(); }
            catch { return new int[0]; }
        }

        private static List<int> GetCurrentGear()
        {
            var loadout = new List<int>
            {
                Inventory.head.id,
                Inventory.boots.id,
                Inventory.chest.id,
                Inventory.legs.id,
                Inventory.weapon.id
            };


            if (_ic.weapon2Unlocked())
                loadout.Add(Inventory.weapon2.id);

            for (var id = 10000; _ic.accessoryID(id) < Inventory.accs.Count; ++id)
            {
                var index = Main.InventoryController.accessoryID(id);
                loadout.Add(Inventory.accs[index].id);
            }

            return loadout;
        }

        public static void SaveCurrentLoadout()
        {
            var loadout = GetCurrentGear();
            _savedLoadout = loadout.ToArray();
            if (_savedLoadout?.Length > 0)
                Log($"Saved Current Loadout {string.Join(", ", _savedLoadout)}");
        }

        public static void SaveTempLoadout()
        {
            var loadout = GetCurrentGear();
            _tempLoadout = loadout.ToArray();
            if (_tempLoadout?.Length > 0)
                Log($"Saved Temp Loadout {string.Join(", ", _tempLoadout)}");
        }

        public static void RestoreTempLoadout() => ChangeGear(_tempLoadout);
    }
}
