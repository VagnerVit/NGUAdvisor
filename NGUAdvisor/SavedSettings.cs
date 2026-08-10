using NGUAdvisor.Managers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace NGUAdvisor
{
    [Serializable]
    public class SavedSettings
    {
        [SerializeField] private int _snipeZone = -1;
        [SerializeField] private bool _manageTitans;
        [SerializeField] private bool _swapTitanLoadouts;
        [SerializeField] private bool _swapTitanDiggers;
        [SerializeField] private bool _swapTitanBeards;
        [SerializeField] private bool _swapYggdrasilLoadouts;
        [SerializeField] private bool _swapYggdrasilDiggers;
        [SerializeField] private bool _swapYggdrasilBeards;
        [SerializeField] private int[] _priorityBoosts;
        [SerializeField] private bool _manageEnergy;
        [SerializeField] private bool _manageMagic;
        [SerializeField] private bool _manageGear;
        [SerializeField] private bool _manageDiggers;
        [SerializeField] private bool _upgradeDiggers;
        [SerializeField] private double _diggerCap;
        [SerializeField] private bool _manageBeards;
        [SerializeField] private bool _manageYggdrasil;
        [SerializeField] private int[] _titanLoadout;
        [SerializeField] private int[] _pinnedGearIds;
        [SerializeField] private int[] _yggdrasilLoadout;
        [SerializeField] private bool _manageInventory;
        [SerializeField] private bool _autoFight;
        [SerializeField] private bool _autoQuest;
        [SerializeField] private bool _allowMajorQuests;
        [SerializeField] private bool _questsFullBank;
        [SerializeField] private bool _autoConvertBoosts;
        [SerializeField] private int[] _goldDropLoadout;
        [SerializeField] private bool _autoMoneyPit;
        [SerializeField] private bool _swapPitDiggers;
        [SerializeField] private bool _predictMoneyPit;
        [SerializeField] private bool _moneyPitDaycare;
        [SerializeField] private bool _autoSpin;
        [SerializeField] private int[] _shockwave;
        [SerializeField] private bool _autoRebirth;
        [SerializeField] private bool _manageWandoos;
        [SerializeField] private double _moneyPitThreshold;
        [SerializeField] private int _daycareThreshold;
        [SerializeField] private int[] _boostBlacklist;
        [SerializeField] private bool _snipeBossOnly;
        [SerializeField] private int _combatMode;
        [SerializeField] private bool _allowZoneFallback;
        [SerializeField] private bool _abandonMinors;
        [SerializeField] private int _minorAbandonThreshold;
        [SerializeField] private int _questCombatMode;
        [SerializeField] private bool _questBeastMode;
        [SerializeField] private bool _autoSpellSwap;
        [SerializeField] private int _spaghettiThreshold;
        [SerializeField] private int _counterfeitThreshold;
        [SerializeField] private bool _castBloodSpells;
        [SerializeField] private double _ironPillThreshold;
        [SerializeField] private int _bloodMacGuffinAThreshold;
        [SerializeField] private int _bloodMacGuffinBThreshold;
        [SerializeField] private bool _ironPillOnRebirth;
        [SerializeField] private bool _bloodMacGuffinAOnRebirth;
        [SerializeField] private bool _bloodMacGuffinBOnRebirth;
        [SerializeField] private bool _autoBuyEm;
        [SerializeField] private bool _autoBuyAdventure;
        [SerializeField] private double _bloodNumberThreshold;
        // AT HOUR extension decision, persisted so an advisor reload cannot cancel an extension already
        // in flight (AtHourPlanner's statics don't survive one). Internal — not user-facing, not in
        // SettingsIndex. 0 = no decision this run; cleared when a rebirth re-arms the planner.
        [SerializeField] private double _atHourPlannedEnd;
        [SerializeField] private double _atHourDecidedRunSec;
        [SerializeField] private int[] _quickLoadout;
        [SerializeField] private int[] _quickDiggers;
        [SerializeField] private int[] _quickBeards;
        [SerializeField] private bool _globalEnabled;
        [SerializeField] private bool _combatEnabled;
        [SerializeField] private bool _useButterMajor;
        [SerializeField] private bool _useButterMinor;
        [SerializeField] private bool _manualMinors;
        [SerializeField] private bool _fiftyItemMinors;
        [SerializeField] private bool _manageR3;
        [SerializeField] private bool _activateFruits;
        [SerializeField] private int[] _wishPriorities;
        [SerializeField] private int[] _wishBlacklist;
        [SerializeField] private bool _weakPriorities;
        [SerializeField] private bool _manageWishes;
        [SerializeField] private int _wishLimit;
        [SerializeField] private int _wishMode;
        [SerializeField] private double _wishEnergy;
        [SerializeField] private double _wishMagic;
        [SerializeField] private double _wishR3;
        [SerializeField] private bool _beastMode;
        [SerializeField] private int _cubePriority;
        [SerializeField] private int _favoredMacguffin;
        [SerializeField] private bool _manageNguDiff;
        [SerializeField] private string _allocationFile;
        [SerializeField] private bool _manageGoldLoadouts;
        [SerializeField] private int _resnipeTime;
        [SerializeField] private bool[] _titanMoneyDone;
        [SerializeField] private bool[] _titanGoldTargets;
        [SerializeField] private bool[] _titanSwapTargets;
        [SerializeField] private bool _goldSnipeComplete;
        [SerializeField] private bool _goldCBlockMode;
        [SerializeField] private int _itopodCombatMode;
        [SerializeField] private int _itopodOptimizeMode;
        [SerializeField] private bool _itopodBeastMode;
        [SerializeField] private bool _itopodAutoPush;
        [SerializeField] private bool _adventureTargetItopod;
        [SerializeField] private int _titanCombatMode;
        [SerializeField] private bool _titanBeastMode;
        [SerializeField] private bool _disableOverlay;
        [SerializeField] private bool _moneyPitRunMode;
        [SerializeField] private int _yggSwapThreshold;
        [SerializeField] private int[] _blacklistedBosses;
        [SerializeField] private bool _manageMayo;
        [SerializeField] private bool _trashCards;
        [SerializeField] private bool _autoCastCards;
        [SerializeField] private bool _castProtectedCards;
        [SerializeField] private bool _trashProtectedCards;
        [SerializeField] private string[] _cardSortOrder;
        [SerializeField] private bool _cardSortEnabled;
        [SerializeField] private bool _manageCooking;
        [SerializeField] private bool _manageQuestLoadouts;
        [SerializeField] private bool _manageCookingLoadouts;
        [SerializeField] private int[] _questLoadout;
        [SerializeField] private int[] _cookingLoadout;
        [SerializeField] private bool _poolMajorQuests;
        [SerializeField] private bool _questHoldForGear;
        [SerializeField] private bool _questBurstActive;
        [SerializeField] private bool _gearHuntEnabled;
        [SerializeField] private int _gearHuntZone = -1;
        [SerializeField] private int[] _lootHunterAccessories;
        [SerializeField] private int _lootHunterRespawnCount;
        [SerializeField] private int _lootHunterDropCount;
        // Mode-loadout optimizer (route C3 3.2): when a mode's objective is set, gear is optimized live
        // for it before that mode's kill/action instead of using the static loadout above. Respawn flag
        // pins the single best Respawn item into the optimized loadout. "" objective = use static loadout.
        [SerializeField] private string _titanObjective;
        [SerializeField] private bool _titanObjectiveRespawn;
        [SerializeField] private string _goldObjective;
        [SerializeField] private bool _goldObjectiveRespawn;
        [SerializeField] private string _questObjective;
        [SerializeField] private bool _questObjectiveRespawn;
        [SerializeField] private string _yggdrasilObjective;
        [SerializeField] private bool _yggdrasilObjectiveRespawn;
        [SerializeField] private string _cookingObjective;
        [SerializeField] private bool _cookingObjectiveRespawn;
        [SerializeField] private bool _manageConsumables;
        [SerializeField] private bool _autoBuyConsumables;
        [SerializeField] private bool _consumeIfAlreadyRunning;
        [SerializeField] private bool _autosave;
        [SerializeField] private string[] _boostPriority;
        [SerializeField] private int[] _cardRarities;
        [SerializeField] private int[] _cardCosts;
        // Advisor auto-apply (route C3 Phase B): per-system opt-ins that let OptimizationAdvisor's
        // goal-aware recommendations drive the actual sets instead of the profile timeline.
        [SerializeField] private bool _advisorDiggers;
        [SerializeField] private bool _advisorBeards;
        [SerializeField] private bool _advisorWandoosOS;
        [SerializeField] private bool _advisorGearRefresh;
        [SerializeField] private bool _advisorPerks;
        [SerializeField] private bool _advisorQuirks;
        [SerializeField] private bool _advisorYggBuys;
        [SerializeField] private bool _advisorExpBuys;
        // Data-driven titan gold: auto-target the highest autokill-able titan for gold banking and
        // re-bank when its AK version rises (TitanGoldVersionBanked tracks the banked version).
        [SerializeField] private bool _autoTitanGold;
        [SerializeField] private int[] _titanGoldVersionBanked;
        [SerializeField] private bool _advisorBlood;
        [SerializeField] private bool _advisorShowOptimal;
        [SerializeField] private bool _wideLayout;
        // Boosts sub-tab v3: advisor-driven boost priority + per-transform-chain flags (index = chain).
        [SerializeField] private bool _autoBoostPriority;
        // Boosts v4 (spec 2026-07-28): the priority list is the ONLY boost source. _boostSeeded makes the
        // one-time migration from equipped + locked idempotent. _boostCubeOnly parks the gear list and
        // sends every boost to the Infinity Cube instead (merges and filters keep running).
        [SerializeField] private bool _boostSeeded;
        [SerializeField] private bool _boostCubeOnly;
        [SerializeField] private bool _advisorZones;
        // Advisor zone-routing strategies (Combat > ZONES): gear-capping farm + demand-gated boost farm.
        [SerializeField] private bool _advisorFarmGear;
        [SerializeField] private bool _advisorFarmBoost;
        [SerializeField] private bool _advisorTitans;
        // Gold pipeline (E1): advisor manages gold end-to-end; manual snipe triggers (S3 event model).
        [SerializeField] private bool _advisorGold;
        [SerializeField] private bool _snipeOnNewZone;
        [SerializeField] private bool _snipeOnRebirth;
        [SerializeField] private bool _snipeOnGoldStarved;
        [SerializeField] private bool _snipeOnTimer;
        [SerializeField] private bool _advisorPit;
        [SerializeField] private bool _advisorChallenges;
        [SerializeField] private bool _advisorQuests;
        [SerializeField] private bool _autoProfile;
        [SerializeField] private bool _lscTargetsSaved;
        [SerializeField] private long _lscSavedAugTarget;
        [SerializeField] private long _lscSavedUpgTarget;
        [SerializeField] private int[] _transformAutoClimb;
        [SerializeField] private int[] _transformKeepMax;
        [SerializeField] private int[] _transformFilter;

        private readonly string _savePath;
        private bool _disableSave;

        // COALESCED WRITE. Every one of the ~100 property setters below calls SaveSettings, and the advisor
        // rewrites settings continuously (titan targets, snipe state, banked versions...). Doing the full
        // JsonUtility.ToJson + file write + flush inline meant a synchronous disk write per setter — several
        // per second in steady state, and a burst per advisor tick. SaveSettings now only marks dirty;
        // Main.Update calls FlushSettings at most once a second, and Unload flushes unconditionally so
        // nothing is lost on exit. Callers that need the file on disk right now (Main.Start's
        // save/load round-trip) call FlushSettings themselves.
        private volatile bool _dirty;

        public SavedSettings(string dir)
        {
            if (dir != null)
                _savePath = Path.Combine(dir, "settings.json");
        }

        public void SaveSettings()
        {
            if (_savePath == null)
                return;
            if (_disableSave)
                return;
            _dirty = true;
            Main.UpdateForm(this);
        }

        // Write the pending settings to disk. No-op unless a setter marked dirty, so calling this every
        // second (or on every Unload path) is free. IgnoreNextChange is armed HERE, next to the write that
        // actually trips ConfigWatcher — arming it per-setter meant a burst of N saves armed a single-shot
        // flag N times against N separate watcher events, and the surplus reload was a real race.
        public void FlushSettings()
        {
            if (!_dirty)
                return;
            if (_savePath == null || _disableSave)
            {
                _dirty = false;
                return;
            }
            _dirty = false;
            try
            {
                Main.Log("Saving Settings");
                Main.IgnoreNextChange = true;
                string serialized = JsonUtility.ToJson(this, true);
                using (var writer = new StreamWriter(_savePath))
                {
                    writer.Write(serialized);
                    writer.Flush();
                }
            }
            catch (Exception e)
            {
                Main.LogDebug($"Settings write failed: {e.Message}");
            }
        }

        public void SetSaveDisabled(bool disabled) => _disableSave = disabled;

        public bool LoadSettings()
        {
            if (File.Exists(_savePath))
            {
                try
                {
                    var newSettings = JsonUtility.FromJson<SavedSettings>(File.ReadAllText(_savePath));
                    MassUpdate(newSettings);
                    // Deliberately NOT dumping the serialized settings here: this runs on every external
                    // settings.json change, and a second full ToJson plus a multi-hundred-line log write
                    // per reload was the single largest writer in inject.log. The file itself is the record.
                    Main.Log("Loaded Settings");
                    return true;
                }
                catch (Exception e)
                {
                    Main.Log(e.Message);
                    Main.Log(e.StackTrace);
                    return false;
                }
            }

            return false;
        }

        private void AssignValue<T>(ref T setting, T? newSetting, Func<T, bool> predicate, T defaultValue = default) where T : struct
        {
            if (newSetting.HasValue && predicate(newSetting.Value))
                setting = newSetting.Value;
            else
                setting = defaultValue;
        }

        private void AssignValues<T>(ref T[] setting, T[] newSetting, int length, T defaultValue = default)
        {
            var result = new T[length];
            for (var i = 0; i < length; i++)
                result[i] = defaultValue;
            if (newSetting != null)
            {
                var count = Math.Min(newSetting.Length, length);
                for (var i = 0; i < count; i++)
                    result[i] = newSetting[i];
            }
            setting = result;
        }

        private void AssignValues<T>(ref T[] setting, T[] newSetting, Func<T, bool> predicate)
        {
            if (newSetting == null)
            {
                setting = new T[0];
            }
            else
            {
                var temp = new List<T>();
                foreach (var item in newSetting)
                {
                    if (predicate(item)) 
                        temp.Add(item);
                }
                setting = temp.ToArray();
            }
        }

        private void AssignValues<T>(ref T[] setting, T[] newSetting, int length, Func<T, bool> predicate, T defaultValue = default)
        {
            var result = new T[length];
            for (var i = 0; i < length; i++)
                result[i] = defaultValue;
            if (newSetting != null)
            {
                var count = Math.Min(newSetting.Length, length);
                for (var i = 0; i < count; i++)
                    result[i] = predicate(newSetting[i]) ? newSetting[i] : defaultValue;
            }
            setting = result;
        }

        private void AssignBoostPriority(string[] newSetting)
        {
            var boostPriority = new string[] { "Power", "Toughness", "Special" };
            if (newSetting?.Length == 3 && newSetting.Intersect(boostPriority).Count() == 3)
                _boostPriority = newSetting;
            else
                _boostPriority = boostPriority;
        }

        private bool IsEquipment(int id)
        {
            if (id < 0 || id > Consts.MAX_GEAR_ID)
                return false;
            return (int)Main.Character.itemInfo.type[id] <= 5;
        }

        private bool IsAdvEnemy(int id)
        {
            var enemyList = Main.Character.adventureController.enemyList;
            for (var i = 0; i < enemyList.Count; i++)
            {
                if (ZoneHelpers.ZoneIsTitan(i))
                    continue;
                if (enemyList[i].Any(x => x.spriteID == id))
                    return true;
            }
            return false;
        }

        public void MassUpdate(SavedSettings other)
        {
            _globalEnabled = other?.GlobalEnabled ?? false;
            _disableOverlay = other?.DisableOverlay ?? false;
            _moneyPitRunMode = other?.MoneyPitRunMode ?? false;
            _autoFight = other?.AutoFight ?? false;
            _autoBuyEm = other?.AutoBuyEM ?? false;
            _autoBuyAdventure = other?.AutoBuyAdventure ?? false;
            _autoBuyConsumables = other?.AutoBuyConsumables ?? false;
            _consumeIfAlreadyRunning = other?.ConsumeIfAlreadyRunning ?? false;
            _autosave = other?.Autosave ?? false;

            _allocationFile = other?.AllocationFile ?? "default";
            _manageEnergy = other?.ManageEnergy ?? false;
            _manageMagic = other?.ManageMagic ?? false;
            _manageR3 = other?.ManageR3 ?? false;
            _manageWandoos = other?.ManageWandoos ?? false;
            _manageNguDiff = other?.ManageNGUDiff ?? false;
            _manageBeards = other?.ManageBeards ?? false;
            _manageDiggers = other?.ManageDiggers ?? false;
            _upgradeDiggers = other?.UpgradeDiggers ?? false;
            AssignValue(ref _diggerCap, other?.DiggerCap, (value) => value >= 0 && value <= 100, 100);
            AssignValues(ref _quickDiggers, other?.QuickDiggers, (id) => id >= 0 && id <= Consts.MAX_DIGGER_ID);
            _manageGear = other?.ManageGear ?? false;
            AssignValues(ref _quickLoadout, other?.QuickLoadout, (id) => IsEquipment(id));
            _manageConsumables = other?.ManageConsumables ?? false;
            _autoRebirth = other?.AutoRebirth ?? false;

            _autoSpellSwap = other?.AutoSpellSwap ?? false;
            AssignValue(ref _bloodNumberThreshold, other?.BloodNumberThreshold, (value) => value >= 0.0, 0.0);
            AssignValue(ref _atHourPlannedEnd, other?.AtHourPlannedEnd, (value) => value >= 0.0, 0.0);
            AssignValue(ref _atHourDecidedRunSec, other?.AtHourDecidedRunSec, (value) => value >= 0.0, 0.0);
            AssignValue(ref _counterfeitThreshold, other?.CounterfeitThreshold, (value) => value >= 0);
            AssignValue(ref _spaghettiThreshold, other?.SpaghettiThreshold, (value) => value >= 0);
            _castBloodSpells = other?.CastBloodSpells ?? false;
            AssignValue(ref _ironPillThreshold, other?.IronPillThreshold, (value) => value >= 0.0);
            AssignValue(ref _bloodMacGuffinAThreshold, other?.BloodMacGuffinAThreshold, (value) => value >= 0);
            AssignValue(ref _bloodMacGuffinBThreshold, other?.BloodMacGuffinBThreshold, (value) => value >= 0);
            _ironPillOnRebirth = other?.IronPillOnRebirth ?? false;
            _bloodMacGuffinAOnRebirth = other?.BloodMacGuffinAOnRebirth ?? false;
            _bloodMacGuffinBOnRebirth = other?.BloodMacGuffinBOnRebirth ?? false;

            _manageYggdrasil = other?.ManageYggdrasil ?? false;
            _swapYggdrasilLoadouts = other?.SwapYggdrasilLoadouts ?? false;
            _swapYggdrasilDiggers = other?._swapYggdrasilDiggers ?? false;
            _swapYggdrasilBeards = other?._swapYggdrasilBeards ?? false;
            AssignValues(ref _yggdrasilLoadout, other?.YggdrasilLoadout, (id) => IsEquipment(id));
            _activateFruits = other?.ActivateFruits ?? false;
            AssignValue(ref _yggSwapThreshold, other?.YggSwapThreshold, (value) => value >= 1 && value <= 24, 1);

            _manageInventory = other?.ManageInventory ?? false;
            _autoConvertBoosts = other?.AutoConvertBoosts ?? false;
            AssignValue(ref _cubePriority, other?.CubePriority, (mode) => mode >= 0 && mode <= 4);
            AssignValue(ref _favoredMacguffin, other?.FavoredMacguffin, (id) => InventoryManager.macguffinList.ContainsKey(id), -1);
            AssignValues(ref _priorityBoosts, other?.PriorityBoosts, (id) => IsEquipment(id));
            AssignBoostPriority(other?.BoostPriority);
            AssignValues(ref _boostBlacklist, other?.BoostBlacklist, (id) => id >= 0 && id <= Consts.MAX_GEAR_ID);

            _manageTitans = other?.ManageTitans ?? false;
            _swapTitanLoadouts = other?.SwapTitanLoadouts ?? false;
            _swapTitanDiggers = other?.SwapTitanDiggers ?? false;
            _swapTitanBeards = other?.SwapTitanBeards ?? false;
            AssignValues(ref _titanLoadout, other?.TitanLoadout, (id) => IsEquipment(id));
            AssignValues(ref _pinnedGearIds, other?.PinnedGearIds, (id) => IsEquipment(id));
            AssignValue(ref _titanCombatMode, other?.TitanCombatMode, (mode) => mode >= 0 && mode <= 4);
            AssignValues(ref _titanSwapTargets, other?.TitanSwapTargets, Consts.MAX_TITAN);

            _combatEnabled = other?.CombatEnabled ?? false;
            AssignValue(ref _combatMode, other?.CombatMode, (mode) => mode >= 0 && mode <= 4);
            _beastMode = other?.BeastMode ?? false;
            AssignValue(ref _snipeZone, other?.SnipeZone, (id) => ZoneHelpers.ZoneList.ContainsKey(id) && !ZoneHelpers.ZoneIsTitan(id));
            _snipeBossOnly = other?.SnipeBossOnly ?? false;
            _allowZoneFallback = other?.AllowZoneFallback ?? false;

            _adventureTargetItopod = other?.AdventureTargetITOPOD ?? false;
            AssignValue(ref _itopodCombatMode, other?.ITOPODCombatMode, (mode) => mode >= 0 && mode <= 4);
            _titanBeastMode = other?.TitanBeastMode ?? false;
            _itopodBeastMode = other?.ITOPODBeastMode ?? false;
            AssignValue(ref _itopodOptimizeMode, other?.ITOPODOptimizeMode, (mode) => mode >= 0 && mode <= 3);
            _itopodAutoPush = other?.ITOPODAutoPush ?? false;

            AssignValues(ref _blacklistedBosses, other?.BlacklistedBosses, (id) => IsAdvEnemy(id));
            CombatManager.UpdateBlacklists();

            _manageGoldLoadouts = other?.ManageGoldLoadouts ?? false;
            AssignValue(ref _resnipeTime, other?.ResnipeTime, (value) => value >= 0, 3600);
            _goldSnipeComplete = other?.GoldSnipeComplete ?? false;
            _goldCBlockMode = other?.GoldCBlockMode ?? false;
            AssignValues(ref _goldDropLoadout, other?.GoldDropLoadout, (id) => IsEquipment(id));
            AssignValues(ref _titanGoldTargets, other?.TitanGoldTargets, Consts.MAX_TITAN);
            AssignValues(ref _titanMoneyDone, other?.TitanMoneyDone, Consts.MAX_TITAN);

            _autoQuest = other?.AutoQuest ?? false;
            _allowMajorQuests = other?.AllowMajorQuests ?? false;
            _useButterMajor = other?.UseButterMajor ?? false;
            _questsFullBank = other?.QuestsFullBank ?? false;
            _manualMinors = other?.ManualMinors ?? false;
            _useButterMinor = other?.UseButterMinor ?? false;
            _fiftyItemMinors = other?.FiftyItemMinors ?? false;
            _abandonMinors = other?.AbandonMinors ?? false;
            AssignValue(ref _minorAbandonThreshold, other?.MinorAbandonThreshold, (value) => value >= 0 && value <= 100, 30);
            _manageQuestLoadouts = other?.ManageQuestLoadouts ?? false;
            AssignValues(ref _questLoadout, other?.QuestLoadout, (id) => IsEquipment(id));
            AssignValue(ref _questCombatMode, other?.QuestCombatMode, (mode) => mode >= 0 && mode <= 4);
            _questBeastMode = other?.QuestBeastMode ?? false;

            _manageWishes = other?.ManageWishes ?? false;
            AssignValue(ref _wishLimit, other?.WishLimit, (value) => value > 0 && value <= 4, 4);
            AssignValue(ref _wishEnergy, other?.WishEnergy, (value) => value >= 0.0 && value <= 100.0);
            AssignValue(ref _wishMagic, other?.WishMagic, (value) => value >= 0.0 && value <= 100.0);
            AssignValue(ref _wishR3, other?.WishR3, (value) => value >= 0.0 && value <= 100.0);
            AssignValue(ref _wishMode, other?.WishMode, (mode) => mode >= 0 && mode <= 3);
            _weakPriorities = other?.WeakPriorities ?? false;
            AssignValues(ref _wishPriorities, other?.WishPriorities, (id) => id >= 0 && id <= Consts.MAX_WISH_ID);
            AssignValues(ref _wishBlacklist, other?.WishBlacklist, (id) => id >= 0 && id <= Consts.MAX_WISH_ID);

            _autoSpin = other?.AutoSpin ?? false;
            _autoMoneyPit = other?.AutoMoneyPit ?? false;
            _predictMoneyPit = other?.PredictMoneyPit ?? false;
            _moneyPitDaycare = other?.MoneyPitDaycare ?? false;
            AssignValue(ref _moneyPitThreshold, other?.MoneyPitThreshold, (value) => MoneyPitManager.moneyPitThresholds.Contains(value), 1e5);
            AssignValue(ref _daycareThreshold, other?.DaycareThreshold, (value) => value >= 0 && value <= 100, 80);
            _swapPitDiggers = other?.SwapPitDiggers ?? false;
            AssignValues(ref _shockwave, other?.Shockwave, (id) => id >= 0 && id <= Consts.MAX_GEAR_ID);

            _manageMayo = other?.ManageMayo ?? false;
            _autoCastCards = other?.AutoCastCards ?? false;
            _castProtectedCards = other?.CastProtectedCards ?? false;
            _cardSortEnabled = other?.CardSortEnabled ?? false;
            _trashCards = other?.TrashCards ?? false;
            _trashProtectedCards = other?.TrashProtectedCards ?? false;
            AssignValues(ref _cardSortOrder, other?.CardSortOrder, (item) => Array.IndexOf(CardManager.sortList, item) >= 0);
            AssignValues(ref _cardRarities, other?.CardRarities, Consts.MAX_CARD_FILTERS, (id) => CardManager.rarityList.ContainsKey(id), -1);
            AssignValues(ref _cardCosts, other?.CardCosts, Consts.MAX_CARD_FILTERS, (cost) => Array.IndexOf(CardManager.costList, cost) >= 0);

            _manageCooking = other?.ManageCooking ?? false;
            _manageCookingLoadouts = other?.ManageCookingLoadouts ?? false;
            AssignValues(ref _cookingLoadout, other?.CookingLoadout, (id) => IsEquipment(id));

            _titanObjective = other?.TitanObjective ?? "";
            _titanObjectiveRespawn = other?.TitanObjectiveRespawn ?? false;
            _goldObjective = other?.GoldObjective ?? "";
            _goldObjectiveRespawn = other?.GoldObjectiveRespawn ?? false;
            _questObjective = other?.QuestObjective ?? "";
            _questObjectiveRespawn = other?.QuestObjectiveRespawn ?? false;
            _yggdrasilObjective = other?.YggdrasilObjective ?? "";
            _yggdrasilObjectiveRespawn = other?.YggdrasilObjectiveRespawn ?? false;
            _cookingObjective = other?.CookingObjective ?? "";
            _cookingObjectiveRespawn = other?.CookingObjectiveRespawn ?? false;

            _poolMajorQuests = other?.PoolMajorQuests ?? false;
            _questHoldForGear = other?.QuestHoldForGear ?? false;
            _questBurstActive = other?.QuestBurstActive ?? false;
            _gearHuntEnabled = other?.GearHuntEnabled ?? false;
            AssignValue(ref _gearHuntZone, other?.GearHuntZone, (z) => z >= -1, -1);
            AssignValues(ref _lootHunterAccessories, other?.LootHunterAccessories, (id) => IsEquipment(id));
            AssignValue(ref _lootHunterRespawnCount, other?.LootHunterRespawnCount, (n) => n >= 0 && n <= 20, 0);
            AssignValue(ref _lootHunterDropCount, other?.LootHunterDropCount, (n) => n >= 0 && n <= 20, 0);

            _advisorDiggers = other?.AdvisorDiggers ?? false;
            _advisorBeards = other?.AdvisorBeards ?? false;
            _advisorWandoosOS = other?.AdvisorWandoosOS ?? false;
            _advisorGearRefresh = other?.AdvisorGearRefresh ?? false;
            _advisorPerks = other?.AdvisorPerks ?? false;
            _advisorQuirks = other?.AdvisorQuirks ?? false;
            _advisorYggBuys = other?.AdvisorYggBuys ?? false;
            _advisorExpBuys = other?.AdvisorExpBuys ?? false;
            _autoTitanGold = other?.AutoTitanGold ?? false;
            AssignValues(ref _titanGoldVersionBanked, other?.TitanGoldVersionBanked, Consts.MAX_TITAN);
            _advisorBlood = other?.AdvisorBlood ?? false;
            _advisorShowOptimal = other?.AdvisorShowOptimal ?? false;
            _wideLayout = other?.WideLayout ?? false;
            _autoBoostPriority = other?.AutoBoostPriority ?? false;
            _boostSeeded = other?.BoostSeeded ?? false;
            _boostCubeOnly = other?.BoostCubeOnly ?? false;
            _advisorZones = other?.AdvisorZones ?? false;
            _advisorFarmGear = other?.AdvisorFarmGear ?? false;
            _advisorFarmBoost = other?.AdvisorFarmBoost ?? false;
            _advisorTitans = other?.AdvisorTitans ?? false;
            _advisorGold = other?.AdvisorGold ?? false;
            _snipeOnNewZone = other?.SnipeOnNewZone ?? true;
            _snipeOnRebirth = other?.SnipeOnRebirth ?? true;
            _snipeOnGoldStarved = other?.SnipeOnGoldStarved ?? false;
            // Migration: users with a configured resnipe time keep timer behavior by default.
            _snipeOnTimer = other?.SnipeOnTimer ?? ((other?.ResnipeTime ?? 0) > 0);
            _advisorPit = other?.AdvisorPit ?? false;
            _advisorChallenges = other?.AdvisorChallenges ?? false;
            _advisorQuests = other?.AdvisorQuests ?? false;
            _autoProfile = other?.AutoProfile ?? false;
            _lscTargetsSaved = other?.LscTargetsSaved ?? false;
            _lscSavedAugTarget = other?.LscSavedAugTarget ?? 0;
            _lscSavedUpgTarget = other?.LscSavedUpgTarget ?? 0;
            AssignValues(ref _transformAutoClimb, other?.TransformAutoClimb, 5);
            // Auto-climb defaults ON: transforming at max level is the game's normal progression —
            // a fresh/legacy settings file must not silently freeze maxed chain items.
            if (other?.TransformAutoClimb == null || other.TransformAutoClimb.Length == 0)
                _transformAutoClimb = new[] { 1, 1, 1, 1, 1 };
            AssignValues(ref _transformKeepMax, other?.TransformKeepMax, 5);
            AssignValues(ref _transformFilter, other?.TransformFilter, 5);
        }

        public int SnipeZone
        {
            get => _snipeZone;
            set
            {
                if (value == _snipeZone) return;
                _snipeZone = value;
                SaveSettings();
            }
        }

        public bool ManageTitans
        {
            get => _manageTitans;
            set
            {
                if (value == _manageTitans) return;
                _manageTitans = value;
                SaveSettings();
            }
        }

        public bool SwapTitanLoadouts
        {
            get => _swapTitanLoadouts;
            set
            {
                if (value == _swapTitanLoadouts) return;
                _swapTitanLoadouts = value;
                SaveSettings();
            }
        }

        public bool SwapTitanDiggers
        {
            get => _swapTitanDiggers;
            set
            {
                if (value == _swapTitanDiggers) return;
                _swapTitanDiggers = value;
                SaveSettings();
            }
        }

        public bool SwapTitanBeards
        {
            get => _swapTitanBeards;
            set
            {
                if (value == _swapTitanBeards) return;
                _swapTitanBeards = value;
                SaveSettings();
            }
        }

        public bool SwapYggdrasilLoadouts
        {
            get => _swapYggdrasilLoadouts;
            set
            {
                if (value == _swapYggdrasilLoadouts) return;
                _swapYggdrasilLoadouts = value;
                SaveSettings();
            }
        }

        public bool SwapYggdrasilDiggers
        {
            get => _swapYggdrasilDiggers;
            set
            {
                if (value == _swapYggdrasilDiggers) return;
                _swapYggdrasilDiggers = value;
                SaveSettings();
            }
        }
        public bool SwapYggdrasilBeards
        {
            get => _swapYggdrasilBeards;
            set
            {
                if (value == _swapYggdrasilBeards) return;
                _swapYggdrasilBeards = value;
                SaveSettings();
            }
        }

        public int[] PriorityBoosts
        {
            get => _priorityBoosts;
            set
            {
                _priorityBoosts = value;
                SaveSettings();
            }
        }

        public bool ManageEnergy
        {
            get => _manageEnergy;
            set
            {
                if (value == _manageEnergy) return;
                _manageEnergy = value;
                SaveSettings();
            }
        }

        public bool ManageMagic
        {
            get => _manageMagic;
            set
            {
                if (value == _manageMagic) return;
                _manageMagic = value;
                SaveSettings();
            }
        }

        public bool ManageGear
        {
            get => _manageGear;
            set
            {
                if (value == _manageGear) return;
                _manageGear = value;
                SaveSettings();
            }
        }

        public int[] TitanLoadout
        {
            get => _titanLoadout;
            set
            {
                _titanLoadout = value;
                SaveSettings();
            }
        }

        // Items to always keep equipped, regardless of which breakpoint/objective is optimizing gear.
        // One global list ("Ring of Greed should be in every inventory") rather than per-breakpoint.
        public int[] PinnedGearIds
        {
            get => _pinnedGearIds;
            set
            {
                _pinnedGearIds = value;
                SaveSettings();
            }
        }

        public int[] YggdrasilLoadout
        {
            get => _yggdrasilLoadout;
            set
            {
                _yggdrasilLoadout = value;
                SaveSettings();
            }
        }

        public bool ManageYggdrasil
        {
            get => _manageYggdrasil;
            set
            {
                if (value == _manageYggdrasil) return;
                _manageYggdrasil = value;
                SaveSettings();
            }
        }

        public bool ManageDiggers
        {
            get => _manageDiggers;
            set
            {
                if (value == _manageDiggers) return;
                _manageDiggers = value;
                SaveSettings();
            }
        }

        public bool UpgradeDiggers
        {
            get => _upgradeDiggers;
            set
            {
                if (value == _upgradeDiggers) return;
                _upgradeDiggers = value;
                SaveSettings();
            }
        }

        public double DiggerCap
        {
            get => _diggerCap;
            set
            {
                if (value == _diggerCap) return;
                _diggerCap = value;
                SaveSettings();
            }
        }

        public bool ManageBeards
        {
            get => _manageBeards;
            set
            {
                if (value == _manageBeards) return;
                _manageBeards = value;
                SaveSettings();
            }
        }

        public bool ManageInventory
        {
            get => _manageInventory;
            set
            {
                if (value == _manageInventory) return;
                _manageInventory = value;
                SaveSettings();
            }
        }

        public bool AutoFight
        {
            get => _autoFight;
            set
            {
                if (value == _autoFight) return;
                _autoFight = value;
                SaveSettings();
            }
        }

        public bool AutoQuest
        {
            get => _autoQuest;
            set
            {
                if (value == _autoQuest) return;
                _autoQuest = value;
                SaveSettings();
            }
        }

        public bool AllowMajorQuests
        {
            get => _allowMajorQuests;
            set
            {
                if (value == _allowMajorQuests) return;
                _allowMajorQuests = value;
                SaveSettings();
            }
        }

        public bool QuestsFullBank
        {
            get => _questsFullBank;
            set
            {
                if (value == _questsFullBank) return;
                _questsFullBank = value;
                SaveSettings();
            }
        }

        public bool AutoConvertBoosts
        {
            get => _autoConvertBoosts;
            set
            {
                if (value == _autoConvertBoosts) return;
                _autoConvertBoosts = value;
                SaveSettings();
            }
        }

        public int[] GoldDropLoadout
        {
            get => _goldDropLoadout;
            set
            {
                _goldDropLoadout = value;
                SaveSettings();
            }
        }

        public int[] Shockwave
        {
            get => _shockwave;
            set
            {
                _shockwave = value;
                SaveSettings();
            }
        }

        public bool AutoMoneyPit
        {
            get => _autoMoneyPit;
            set
            {
                if (value == _autoMoneyPit) return;
                _autoMoneyPit = value;
                SaveSettings();
            }
        }

        public bool SwapPitDiggers
        {
            get => _swapPitDiggers;
            set
            {
                if (value == _swapPitDiggers) return;
                _swapPitDiggers = value;
                SaveSettings();
            }
        }

        public bool PredictMoneyPit
        {
            get => _predictMoneyPit;
            set
            {
                if (value == _predictMoneyPit) return;
                _predictMoneyPit = value;
                SaveSettings();
            }
        }

        public bool MoneyPitDaycare
        {
            get => _moneyPitDaycare;
            set
            {
                if (value == _moneyPitDaycare) return;
                _moneyPitDaycare = value;
                SaveSettings();
            }
        }

        public bool AutoSpin
        {
            get => _autoSpin;
            set
            {
                if (value == _autoSpin) return;
                _autoSpin = value;
                SaveSettings();
            }
        }

        public bool AutoRebirth
        {
            get => _autoRebirth;
            set
            {
                if (value == _autoRebirth) return;
                _autoRebirth = value;
                SaveSettings();
            }
        }

        public bool ManageWandoos
        {
            get => _manageWandoos;
            set
            {
                if (value == _manageWandoos) return;
                _manageWandoos = value;
                SaveSettings();
            }
        }

        public double MoneyPitThreshold
        {
            get => _moneyPitThreshold;
            set
            {
                _moneyPitThreshold = value;
                SaveSettings();
            }
        }

        public int DaycareThreshold
        {
            get => _daycareThreshold;
            set
            {
                _daycareThreshold = value;
                SaveSettings();
            }
        }

        public bool SnipeBossOnly
        {
            get => _snipeBossOnly;
            set
            {
                if (value == _snipeBossOnly) return;
                _snipeBossOnly = value;
                SaveSettings();
            }
        }

        // RETIRED as of the 2026-07-28 boosts change: nothing reads this any more (the priority list is the
        // only boost source, and merges answer to the transform-chain toggles). The field is KEPT so
        // settings.json round-trips unchanged and a rollback to an older DLL still finds the user's data.
        // See docs/superpowers/specs/2026-07-28-boosts-panel-ux-design.md §3.
        public int[] BoostBlacklist
        {
            get => _boostBlacklist;
            set
            {
                _boostBlacklist = value;
                SaveSettings();
            }
        }

        public int CombatMode
        {
            get => _combatMode;
            set
            {
                if (value == _combatMode) return;
                _combatMode = value;
                SaveSettings();
            }
        }

        public bool AllowZoneFallback
        {
            get => _allowZoneFallback;
            set
            {
                if (value == _allowZoneFallback) return;
                _allowZoneFallback = value;
                SaveSettings();
            }
        }

        public bool AbandonMinors
        {
            get => _abandonMinors;
            set
            {
                if (value == _abandonMinors) return;
                _abandonMinors = value;
                SaveSettings();
            }
        }

        public int MinorAbandonThreshold
        {
            get => _minorAbandonThreshold;
            set
            {
                if (value == _minorAbandonThreshold) return;
                _minorAbandonThreshold = value;
                SaveSettings();
            }
        }

        public int QuestCombatMode
        {
            get => _questCombatMode;
            set
            {
                if (value == _questCombatMode) return;
                _questCombatMode = value;
                SaveSettings();
            }
        }

        public bool QuestBeastMode
        {
            get => _questBeastMode;
            set
            {
                if (value == _questBeastMode) return;
                _questBeastMode = value;
                SaveSettings();
            }
        }

        public bool AutoSpellSwap
        {
            get => _autoSpellSwap;
            set
            {
                if (value == _autoSpellSwap) return;
                _autoSpellSwap = value;
                SaveSettings();
            }
        }

        public int SpaghettiThreshold
        {
            get => _spaghettiThreshold;
            set
            {
                if (value == _spaghettiThreshold) return;
                _spaghettiThreshold = value;
                SaveSettings();
            }
        }

        public bool CastBloodSpells
        {
            get => _castBloodSpells;
            set
            {
                if (value == _castBloodSpells) return;
                _castBloodSpells = value;
                SaveSettings();
            }
        }

        public double IronPillThreshold
        {
            get => _ironPillThreshold;
            set
            {
                var round = Math.Floor(value);
                if (round == _ironPillThreshold) return;
                _ironPillThreshold = round;
                SaveSettings();
            }
        }

        public int BloodMacGuffinAThreshold
        {
            get => _bloodMacGuffinAThreshold;
            set
            {
                if (value == _bloodMacGuffinAThreshold) return;
                _bloodMacGuffinAThreshold = value;
                SaveSettings();
            }
        }

        public int BloodMacGuffinBThreshold
        {
            get => _bloodMacGuffinBThreshold;
            set
            {
                if (value == _bloodMacGuffinBThreshold) return;
                _bloodMacGuffinBThreshold = value;
                SaveSettings();
            }
        }

        public bool IronPillOnRebirth
        {
            get => _ironPillOnRebirth;
            set
            {
                if (value == _ironPillOnRebirth) return;
                _ironPillOnRebirth = value;
                SaveSettings();
            }
        }

        public bool BloodMacGuffinAOnRebirth
        {
            get => _bloodMacGuffinAOnRebirth;
            set
            {
                if (value == _bloodMacGuffinAOnRebirth) return;
                _bloodMacGuffinAOnRebirth = value;
                SaveSettings();
            }
        }

        public bool BloodMacGuffinBOnRebirth
        {
            get => _bloodMacGuffinBOnRebirth;
            set
            {
                if (value == _bloodMacGuffinBOnRebirth) return;
                _bloodMacGuffinBOnRebirth = value;
                SaveSettings();
            }
        }

        public int CounterfeitThreshold
        {
            get => _counterfeitThreshold;
            set
            {
                if (value == _counterfeitThreshold) return;
                _counterfeitThreshold = value;
                SaveSettings();
            }
        }

        public bool AutoBuyEM
        {
            get => _autoBuyEm;
            set
            {
                if (value == _autoBuyEm) return;
                _autoBuyEm = value;
                SaveSettings();
            }
        }

        public bool AutoBuyAdventure
        {
            get => _autoBuyAdventure;
            set
            {
                if (value == _autoBuyAdventure) return;
                _autoBuyAdventure = value;
                SaveSettings();
            }
        }

        public double BloodNumberThreshold
        {
            get => _bloodNumberThreshold;
            set
            {
                if (value == _bloodNumberThreshold) return;
                _bloodNumberThreshold = value;
                SaveSettings();
            }
        }

        public double AtHourPlannedEnd
        {
            get => _atHourPlannedEnd;
            set
            {
                if (value == _atHourPlannedEnd) return;
                _atHourPlannedEnd = value;
                SaveSettings();
            }
        }

        public double AtHourDecidedRunSec
        {
            get => _atHourDecidedRunSec;
            set
            {
                if (value == _atHourDecidedRunSec) return;
                _atHourDecidedRunSec = value;
                SaveSettings();
            }
        }

        public int[] QuickLoadout
        {
            get => _quickLoadout;
            set => _quickLoadout = value;
        }

        public int[] QuickDiggers
        {
            get => _quickDiggers;
            set => _quickDiggers = value;
        }

        public int[] QuickBeards
        {
            get => _quickBeards;
            set => _quickBeards = value;
        }

        public bool GlobalEnabled
        {
            get => _globalEnabled;
            set
            {
                if (value == _globalEnabled) return;
                _globalEnabled = value;
                SaveSettings();
            }
        }

        public bool CombatEnabled
        {
            get => _combatEnabled;
            set
            {
                if (value == _combatEnabled) return;
                _combatEnabled = value;
                SaveSettings();
            }
        }

        public bool ManualMinors
        {
            get => _manualMinors;
            set
            {
                if (value == _manualMinors) return;
                _manualMinors = value;
                SaveSettings();
            }
        }

        public bool FiftyItemMinors
        {
            get => _fiftyItemMinors;
            set
            {
                if (value == _fiftyItemMinors) return;
                _fiftyItemMinors = value;
                SaveSettings();
            }
        }

        public bool UseButterMajor
        {
            get => _useButterMajor;
            set
            {
                if (value == _useButterMajor) return;
                _useButterMajor = value;
                SaveSettings();
            }
        }

        public bool ManageR3
        {
            get => _manageR3;
            set
            {
                if (value == _manageR3) return;
                _manageR3 = value;
                SaveSettings();
            }
        }

        public bool UseButterMinor
        {
            get => _useButterMinor;
            set
            {
                if (value == _useButterMinor) return;
                _useButterMinor = value;
                SaveSettings();
            }
        }

        public bool ActivateFruits
        {
            get => _activateFruits;
            set
            {
                if (value == _activateFruits) return;
                _activateFruits = value;
                SaveSettings();
            }
        }
        public int[] WishPriorities
        {
            get => _wishPriorities;
            set
            {
                if (value == _wishPriorities) return;
                _wishPriorities = value;
                SaveSettings();
            }
        }
        public int[] WishBlacklist
        {
            get => _wishBlacklist;
            set
            {
                if (value == _wishBlacklist) return;
                _wishBlacklist = value;
                SaveSettings();
            }
        }

        public bool WeakPriorities
        {
            get => _weakPriorities;
            set
            {
                if (value == _weakPriorities) return;
                _weakPriorities = value;
                SaveSettings();
            }
        }

        public bool ManageWishes
        {
            get => _manageWishes;
            set
            {
                if (value == _manageWishes) return;
                _manageWishes = value;
                SaveSettings();
            }
        }

        public int WishLimit
        {
            get => _wishLimit;
            set
            {
                if (value == _wishLimit) return;
                _wishLimit = value;
                SaveSettings();
            }
        }

        public int WishMode
        {
            get => _wishMode;
            set
            {
                if (value == _wishMode) return;
                _wishMode = value;
                SaveSettings();
            }
        }

        public double WishEnergy
        {
            get => _wishEnergy;
            set
            {
                if (value == _wishEnergy) return;
                _wishEnergy = value;
                SaveSettings();
            }
        }

        public double WishMagic
        {
            get => _wishMagic;
            set
            {
                if (value == _wishMagic) return;
                _wishMagic = value;
                SaveSettings();
            }
        }

        public double WishR3
        {
            get => _wishR3;
            set
            {
                if (value == _wishR3) return;
                _wishR3 = value;
                SaveSettings();
            }
        }

        public bool BeastMode
        {
            get => _beastMode;
            set
            {
                if (value == _beastMode) return;
                _beastMode = value;
                SaveSettings();
            }
        }

        public int CubePriority
        {
            get => _cubePriority;
            set
            {
                if (value == _cubePriority) return;
                _cubePriority = value;
                SaveSettings();
            }
        }

        public int FavoredMacguffin
        {
            get => _favoredMacguffin;
            set
            {
                if (value == _favoredMacguffin) return;
                _favoredMacguffin = value;
                SaveSettings();
            }
        }

        public bool ManageNGUDiff
        {
            get => _manageNguDiff;
            set
            {
                if (value == _manageNguDiff) return;
                _manageNguDiff = value;
                SaveSettings();
            }
        }

        public string AllocationFile
        {
            get => _allocationFile;
            set
            {
                if (value == _allocationFile) return;
                _allocationFile = value;
                SaveSettings();
            }
        }

        public bool ManageGoldLoadouts
        {
            get => _manageGoldLoadouts || MoneyPitRunMode;
            set
            {
                if (value == _manageGoldLoadouts) return;
                _manageGoldLoadouts = value;
                SaveSettings();
            }
        }

        public int ResnipeTime
        {
            get => _resnipeTime;
            set
            {
                if (value == _resnipeTime) return;
                _resnipeTime = value;
                SaveSettings();
            }
        }

        public bool[] TitanMoneyDone
        {
            get => _titanMoneyDone;
            set
            {
                if (_titanMoneyDone?.SequenceEqual(value) ?? false) return;
                _titanMoneyDone = value;
                SaveSettings();
            }
        }

        public bool[] TitanGoldTargets
        {
            get => _titanGoldTargets;
            set
            {
                if (_titanGoldTargets?.SequenceEqual(value) ?? false) return;
                _titanGoldTargets = value;
                SaveSettings();
            }
        }

        public bool[] TitanSwapTargets
        {
            get => _titanSwapTargets;
            set
            {
                if (_titanSwapTargets?.SequenceEqual(value) ?? false) return;
                _titanSwapTargets = value;
                SaveSettings();
            }
        }

        public bool GoldSnipeComplete
        {
            get => _goldSnipeComplete;
            set
            {
                if (value == _goldSnipeComplete) return;
                _goldSnipeComplete = value;
                // Re-arming invalidates the "skipped, no gain vs the TM bank" note the Gold panel shows
                // in place of COMPLETE (GoldDropAdvisor).
                if (!value) Managers.GoldDropAdvisor.ClearSnipeSkip();
                SaveSettings();
            }
        }

        public bool GoldCBlockMode
        {
            get => _goldCBlockMode || MoneyPitRunMode;
            set
            {
                if (value == _goldCBlockMode) return;
                _goldCBlockMode = value;
                SaveSettings();
            }
        }

        public bool AdventureTargetITOPOD
        {
            get => _adventureTargetItopod;
            set
            {
                if (value == _adventureTargetItopod) return;
                _adventureTargetItopod = value;
                SaveSettings();
            }
        }

        public int TitanCombatMode
        {
            get => _titanCombatMode;
            set
            {
                if (value == _titanCombatMode) return;
                _titanCombatMode = value;
                SaveSettings();
            }
        }

        public bool TitanBeastMode
        {
            get => _titanBeastMode;
            set
            {
                if (value == _titanBeastMode) return;
                _titanBeastMode = value;
                SaveSettings();
            }
        }

        public int ITOPODCombatMode
        {
            get => _itopodCombatMode;
            set
            {
                if (value == _itopodCombatMode) return;
                _itopodCombatMode = value;
                SaveSettings();
            }
        }

        public int ITOPODOptimizeMode
        {
            get => _itopodOptimizeMode;
            set
            {
                if (value == _itopodOptimizeMode) return;
                _itopodOptimizeMode = value;
                SaveSettings();
            }
        }

        public bool ITOPODBeastMode
        {
            get => _itopodBeastMode;
            set
            {
                if (value == _itopodBeastMode) return;
                _itopodBeastMode = value;
                SaveSettings();
            }
        }

        public bool ITOPODAutoPush
        {
            get => _itopodAutoPush;
            set
            {
                if (value == _itopodAutoPush) return;
                _itopodAutoPush = value;
                SaveSettings();
            }
        }

        public bool DisableOverlay
        {
            get => _disableOverlay;
            set
            {
                if (value == _disableOverlay) return;
                _disableOverlay = value;
                SaveSettings();
            }
        }

        public bool MoneyPitRunMode
        {
            get => _moneyPitRunMode;
            set
            {
                if (value == _moneyPitRunMode) return;
                _moneyPitRunMode = value;
                SaveSettings();
            }
        }

        public int YggSwapThreshold
        {
            get => _yggSwapThreshold;
            set
            {
                if (value == _yggSwapThreshold) return;
                _yggSwapThreshold = value;
                SaveSettings();
            }
        }

        public int[] BlacklistedBosses
        {
            get => _blacklistedBosses;
            set
            {
                if (_blacklistedBosses?.SequenceEqual(value) ?? false) return;
                _blacklistedBosses = value;
                SaveSettings();
                CombatManager.UpdateBlacklists();
            }
        }

        public bool ManageMayo
        {
            get => _manageMayo;
            set
            {
                if (value == _manageMayo) return;
                _manageMayo = value;
                SaveSettings();
            }
        }

        public bool TrashCards
        {
            get => _trashCards;
            set
            {
                if (value == _trashCards) return;
                _trashCards = value;
                SaveSettings();
            }
        }

        public bool AutoCastCards
        {
            get => _autoCastCards;
            set
            {
                if (value == _autoCastCards) return;
                _autoCastCards = value;
                SaveSettings();
            }
        }

        public bool CastProtectedCards
        {
            get => _castProtectedCards;
            set
            {
                if (value == _castProtectedCards) return;
                _castProtectedCards = value;
                SaveSettings();
            }
        }

        public bool TrashProtectedCards
        {
            get => _trashProtectedCards;
            set
            {
                if (value == _trashProtectedCards) return;
                _trashProtectedCards = value;
                SaveSettings();
            }
        }

        public int[] CardRarities
        {
            get => _cardRarities;
            set
            {
                if (value == _cardRarities) return;
                _cardRarities = value;
                SaveSettings();
            }
        }

        public void SetCardRarity(int index, int cardRarity)
        {
            if (cardRarity == _cardRarities[index]) return;
            _cardRarities[index] = cardRarity;
            SaveSettings();
        }

        public int[] CardCosts
        {
            get => _cardCosts;
            set
            {
                if (value == _cardCosts) return;
                _cardCosts = value;
                SaveSettings();
            }
        }

        public void SetCardCost(int index, int cardTier)
        {
            if (cardTier == _cardCosts[index]) return;
            _cardCosts[index] = cardTier;
            SaveSettings();
        }

        public string[] CardSortOrder
        {
            get => _cardSortOrder;
            set
            {
                if (value == _cardSortOrder) return;
                _cardSortOrder = value;
                SaveSettings();
            }
        }

        public string[] BoostPriority
        {
            get => _boostPriority;
            set
            {
                if (value == _boostPriority) return;
                _boostPriority = value;
                SaveSettings();
            }
        }

        public bool CardSortEnabled
        {
            get => _cardSortEnabled;
            set
            {
                if (value == _cardSortEnabled) return;
                _cardSortEnabled = value;
                SaveSettings();
            }
        }

        public bool NeedsGoldSwap()
        {
            for (var i = 0; i < TitanSwapTargets.Length; i++)
            {
                if (!TitanSwapTargets[i])
                    continue;

                if (TitanSwapTargets[i] && !TitanMoneyDone[i])
                    return true;
            }

            return false;
        }

        public bool ManageCooking
        {
            get => _manageCooking;
            set
            {
                if (value == _manageCooking) return;
                _manageCooking = value;
                SaveSettings();
            }
        }

        public bool ManageQuestLoadouts
        {
            get => _manageQuestLoadouts;
            set
            {
                if (value == _manageQuestLoadouts) return;
                _manageQuestLoadouts = value;
                SaveSettings();
            }
        }

        public bool ManageCookingLoadouts
        {
            get => _manageCookingLoadouts;
            set
            {
                if (value == _manageCookingLoadouts) return;
                _manageCookingLoadouts = value;
                SaveSettings();
            }
        }

        public int[] QuestLoadout
        {
            get => _questLoadout;
            set
            {
                _questLoadout = value;
                SaveSettings();
            }
        }

        public int[] CookingLoadout
        {
            get => _cookingLoadout;
            set
            {
                _cookingLoadout = value;
                SaveSettings();
            }
        }

        // --- Quest pooling (user feature): bank majors to cap, then burst the whole bank ---

        public bool PoolMajorQuests
        {
            get => _poolMajorQuests;
            set
            {
                if (value == _poolMajorQuests) return;
                _poolMajorQuests = value;
                SaveSettings();
            }
        }

        // Opt-in capstone hold (default OFF — user: a ready major parked at 100% for hours read
        // as a hang; Gear Hunt is the deliberate gear-farming tool now).
        public bool QuestHoldForGear
        {
            get => _questHoldForGear;
            set
            {
                if (value == _questHoldForGear) return;
                _questHoldForGear = value;
                SaveSettings();
            }
        }

        // Internal burst state (persisted so a reload mid-burst keeps burning the bank).
        public bool QuestBurstActive
        {
            get => _questBurstActive;
            set
            {
                if (value == _questBurstActive) return;
                _questBurstActive = value;
                SaveSettings();
            }
        }

        // --- Gear Hunt (user feature): camp a chosen stage for its drops in a hybrid loadout ---
        // (best N accessories from the Loot Hunter pool + optimizer-best Power/Toughness gear).

        public bool GearHuntEnabled
        {
            get => _gearHuntEnabled;
            set
            {
                if (value == _gearHuntEnabled) return;
                _gearHuntEnabled = value;
                SaveSettings();
            }
        }

        public int GearHuntZone
        {
            get => _gearHuntZone;
            set
            {
                if (value == _gearHuntZone) return;
                _gearHuntZone = value;
                SaveSettings();
            }
        }

        // The Loot Hunter ACCESSORY POOL: candidate Drop Chance / Respawn accessory item IDs the
        // user curates; the advisor equips the best of them, as many as accessory slots allow.
        public int[] LootHunterAccessories
        {
            get => _lootHunterAccessories;
            set
            {
                _lootHunterAccessories = value;
                SaveSettings();
            }
        }

        // Per-type quotas (user 2026-07-11): how many Respawn and how many Drop Chance accessories
        // the advisor should allocate from the pool. Both 0 = auto (blended DCxRespawn ranking).
        public int LootHunterRespawnCount
        {
            get => _lootHunterRespawnCount;
            set
            {
                if (value == _lootHunterRespawnCount) return;
                _lootHunterRespawnCount = value;
                SaveSettings();
            }
        }

        public int LootHunterDropCount
        {
            get => _lootHunterDropCount;
            set
            {
                if (value == _lootHunterDropCount) return;
                _lootHunterDropCount = value;
                SaveSettings();
            }
        }

        // --- Mode-loadout optimizer objectives (3.2). "" => use the static loadout for that mode. ---

        public string TitanObjective
        {
            get => _titanObjective ?? "";
            set { var v = value ?? ""; if (v == (_titanObjective ?? "")) return; _titanObjective = v; SaveSettings(); }
        }

        public bool TitanObjectiveRespawn
        {
            get => _titanObjectiveRespawn;
            set { if (value == _titanObjectiveRespawn) return; _titanObjectiveRespawn = value; SaveSettings(); }
        }

        public string GoldObjective
        {
            get => _goldObjective ?? "";
            set { var v = value ?? ""; if (v == (_goldObjective ?? "")) return; _goldObjective = v; SaveSettings(); }
        }

        public bool GoldObjectiveRespawn
        {
            get => _goldObjectiveRespawn;
            set { if (value == _goldObjectiveRespawn) return; _goldObjectiveRespawn = value; SaveSettings(); }
        }

        public string QuestObjective
        {
            get => _questObjective ?? "";
            set { var v = value ?? ""; if (v == (_questObjective ?? "")) return; _questObjective = v; SaveSettings(); }
        }

        public bool QuestObjectiveRespawn
        {
            get => _questObjectiveRespawn;
            set { if (value == _questObjectiveRespawn) return; _questObjectiveRespawn = value; SaveSettings(); }
        }

        public string YggdrasilObjective
        {
            get => _yggdrasilObjective ?? "";
            set { var v = value ?? ""; if (v == (_yggdrasilObjective ?? "")) return; _yggdrasilObjective = v; SaveSettings(); }
        }

        public bool YggdrasilObjectiveRespawn
        {
            get => _yggdrasilObjectiveRespawn;
            set { if (value == _yggdrasilObjectiveRespawn) return; _yggdrasilObjectiveRespawn = value; SaveSettings(); }
        }

        public string CookingObjective
        {
            get => _cookingObjective ?? "";
            set { var v = value ?? ""; if (v == (_cookingObjective ?? "")) return; _cookingObjective = v; SaveSettings(); }
        }

        // --- Advisor auto-apply opt-ins (Phase B). When on, the advisor's goal-aware set/OS choice
        // is applied automatically (profile digger/beard timelines are overridden while enabled). ---

        public bool AdvisorDiggers
        {
            get => _advisorDiggers;
            set { if (value == _advisorDiggers) return; _advisorDiggers = value; SaveSettings(); }
        }

        public bool AdvisorBeards
        {
            get => _advisorBeards;
            set { if (value == _advisorBeards) return; _advisorBeards = value; SaveSettings(); }
        }

        public bool AdvisorWandoosOS
        {
            get => _advisorWandoosOS;
            set { if (value == _advisorWandoosOS) return; _advisorWandoosOS = value; SaveSettings(); }
        }

        public bool AdvisorGearRefresh
        {
            get => _advisorGearRefresh;
            set { if (value == _advisorGearRefresh) return; _advisorGearRefresh = value; SaveSettings(); }
        }

        public bool AdvisorPerks
        {
            get => _advisorPerks;
            set { if (value == _advisorPerks) return; _advisorPerks = value; SaveSettings(); }
        }

        public bool AdvisorQuirks
        {
            get => _advisorQuirks;
            set { if (value == _advisorQuirks) return; _advisorQuirks = value; SaveSettings(); }
        }

        public bool AdvisorYggBuys
        {
            get => _advisorYggBuys;
            set { if (value == _advisorYggBuys) return; _advisorYggBuys = value; SaveSettings(); }
        }

        public bool AdvisorExpBuys
        {
            get => _advisorExpBuys;
            set { if (value == _advisorExpBuys) return; _advisorExpBuys = value; SaveSettings(); }
        }

        public bool AutoTitanGold
        {
            get => _autoTitanGold;
            set { if (value == _autoTitanGold) return; _autoTitanGold = value; SaveSettings(); }
        }

        public bool AdvisorBlood
        {
            get => _advisorBlood;
            set { if (value == _advisorBlood) return; _advisorBlood = value; SaveSettings(); }
        }

        public bool AdvisorShowOptimal
        {
            get => _advisorShowOptimal;
            set { if (value == _advisorShowOptimal) return; _advisorShowOptimal = value; SaveSettings(); }
        }

        // A/B layout test: widens the settings window (default ~608 client from DPI autoscale halving
        // the designed 1216) to 940. Applied at form construction — flip + Reload Advisor to compare.
        public bool WideLayout
        {
            get => _wideLayout;
            set { if (value == _wideLayout) return; _wideLayout = value; SaveSettings(); }
        }

        public bool AutoBoostPriority
        {
            get => _autoBoostPriority;
            set { if (value == _autoBoostPriority) return; _autoBoostPriority = value; SaveSettings(); }
        }

        // Boost the Infinity Cube INSTEAD of the priority list. The Cube dropdown next to it only says
        // HOW the cube is fed once it gets boosts; this is what makes it get them exclusively.
        public bool BoostCubeOnly
        {
            get => _boostCubeOnly;
            set { if (value == _boostCubeOnly) return; _boostCubeOnly = value; SaveSettings(); }
        }

        // Set once by the boost-list migration (Main.Start). Never set it from the UI.
        public bool BoostSeeded
        {
            get => _boostSeeded;
            set { if (value == _boostSeeded) return; _boostSeeded = value; SaveSettings(); }
        }

        // Adventure > ZONES source toggle: advisor points SnipeZone at the best boost farm (gold and
        // quest logic keep their own overrides); off = the manual zone dropdown owns SnipeZone.
        public bool AdvisorZones
        {
            get => _advisorZones;
            set { if (value == _advisorZones) return; _advisorZones = value; SaveSettings(); }
        }

        // Farm Gear Zones: advisor routes to a zone whose droppable equipment isn't level-100 yet
        // (capping items is a permanent bonus), when one caps inside the GearFarmAdvisor budget.
        // Outranks the boost farm; gold and quest logic keep their overrides.
        public bool AdvisorFarmGear
        {
            get => _advisorFarmGear;
            set { if (value == _advisorFarmGear) return; _advisorFarmGear = value; SaveSettings(); }
        }

        // Farm Best Boost: gate the boost-farm routing on boost DEMAND — gear still needing
        // boosts, or an Infinity Cube under its softcap. No demand = ITOPOD beats boost zones.
        public bool AdvisorFarmBoost
        {
            get => _advisorFarmBoost;
            set { if (value == _advisorFarmBoost) return; _advisorFarmBoost = value; SaveSettings(); }
        }

        // Titans hero toggle: advisor writes TitanSwapTargets (spawnable titans below auto-kill,
        // riddle titans only once their quest flags unlock); off = the manual kill-toggle grid.
        public bool AdvisorTitans
        {
            get => _advisorTitans;
            set { if (value == _advisorTitans) return; _advisorTitans = value; SaveSettings(); }
        }

        // Gold pipeline: advisor = fully event-driven snipes (new zone/rebirth/starvation), auto
        // CBlock in challenges, titan banking; manual = the S3 trigger toggles below.
        public bool AdvisorGold
        {
            get => _advisorGold;
            set { if (value == _advisorGold) return; _advisorGold = value; SaveSettings(); }
        }

        public bool SnipeOnNewZone
        {
            get => _snipeOnNewZone;
            set { if (value == _snipeOnNewZone) return; _snipeOnNewZone = value; SaveSettings(); }
        }

        public bool SnipeOnRebirth
        {
            get => _snipeOnRebirth;
            set { if (value == _snipeOnRebirth) return; _snipeOnRebirth = value; SaveSettings(); }
        }

        public bool SnipeOnGoldStarved
        {
            get => _snipeOnGoldStarved;
            set { if (value == _snipeOnGoldStarved) return; _snipeOnGoldStarved = value; SaveSettings(); }
        }

        public bool SnipeOnTimer
        {
            get => _snipeOnTimer;
            set { if (value == _snipeOnTimer) return; _snipeOnTimer = value; SaveSettings(); }
        }

        // Money Pit advisor: tier-ETA throw policy (hold for the next log10 tier when close), TM/augment
        // safety gates, prediction + outcome prep always on.
        public bool AdvisorPit
        {
            get => _advisorPit;
            set { if (value == _advisorPit) return; _advisorPit = value; SaveSettings(); }
        }

        // Challenge overlays (Option B): live transforms on top of the profile while a challenge runs.
        public bool AdvisorChallenges
        {
            get => _advisorChallenges;
            set { if (value == _advisorChallenges) return; _advisorChallenges = value; SaveSettings(); }
        }

        // Quest advisor: strategy (majors when banked, overfill guard, abandon <30%, butter majors)
        // + the capstone hold (max the quest zone's gear before turning in a major).
        public bool AdvisorQuests
        {
            get => _advisorQuests;
            set { if (value == _advisorQuests) return; _advisorQuests = value; SaveSettings(); }
        }

        // Option A slice 1: energy/magic/R3 priorities GENERATED per cycle (phase/TM/unlock
        // parameterized) instead of read from the profile timeline. Profile file stays on standby.
        public bool AutoProfile
        {
            get => _autoProfile;
            set { if (value == _autoProfile) return; _autoProfile = value; SaveSettings(); }
        }

        // LSC override bookkeeping. While the Laser Sword Challenge runs, the overlay rewrites the
        // game's aug-6 targets so the sword and its upgrade both stop at the challenge level. These
        // persist the player's own targets across a save/reload — held in memory they were lost if
        // the game closed mid-challenge, and the next LSC then snapshotted the OVERRIDE as if it
        // were the player's value, poisoning aug-6 permanently. Not surfaced in the UI.
        public bool LscTargetsSaved
        {
            get => _lscTargetsSaved;
            set { if (value == _lscTargetsSaved) return; _lscTargetsSaved = value; SaveSettings(); }
        }

        public long LscSavedAugTarget
        {
            get => _lscSavedAugTarget;
            set { if (value == _lscSavedAugTarget) return; _lscSavedAugTarget = value; SaveSettings(); }
        }

        public long LscSavedUpgTarget
        {
            get => _lscSavedUpgTarget;
            set { if (value == _lscSavedUpgTarget) return; _lscSavedUpgTarget = value; SaveSettings(); }
        }

        public int[] TransformAutoClimb
        {
            get => _transformAutoClimb;
            set { _transformAutoClimb = value; SaveSettings(); }
        }

        public int[] TransformKeepMax
        {
            get => _transformKeepMax;
            set { _transformKeepMax = value; SaveSettings(); }
        }

        public int[] TransformFilter
        {
            get => _transformFilter;
            set { _transformFilter = value; SaveSettings(); }
        }

        public int[] TitanGoldVersionBanked
        {
            get => _titanGoldVersionBanked;
            set
            {
                if (_titanGoldVersionBanked?.SequenceEqual(value) ?? false) return;
                _titanGoldVersionBanked = value;
                SaveSettings();
            }
        }

        public bool CookingObjectiveRespawn
        {
            get => _cookingObjectiveRespawn;
            set { if (value == _cookingObjectiveRespawn) return; _cookingObjectiveRespawn = value; SaveSettings(); }
        }

        public bool AutoBuyConsumables
        {
            get => _autoBuyConsumables;
            set
            {
                if (value == _autoBuyConsumables) return;
                _autoBuyConsumables = value;
                SaveSettings();
            }
        }

        public bool ConsumeIfAlreadyRunning
        {
            get => _consumeIfAlreadyRunning;
            set
            {
                if (value == _consumeIfAlreadyRunning) return;
                _consumeIfAlreadyRunning = value;
                SaveSettings();
            }
        }

        public bool Autosave
        {
            get => _autosave;
            set
            {
                if (value == _autosave) return;
                _autosave = value;
                SaveSettings();
            }
        }

        public bool ManageConsumables
        {
            get => _manageConsumables;
            set
            {
                if (value == _manageConsumables) return;
                _manageConsumables = value;
                SaveSettings();
            }
        }
    }
}
