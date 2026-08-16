using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    public partial class SettingsForm : Form
    {
        private enum Direction
        {
            Up = 1,
            Down = -1
        }

        private class ItemControlGroup
        {
            public ListBox ItemList { get; }

            public NumericUpDown ItemBox { get; }

            public ErrorProvider ErrorProvider { get; }

            public Label ItemLabel { get; }

            public Func<int[]> GetSettings { get; }

            public Action<int[]> SaveSettings { get; }

            public int MinVal { get; }

            public int MaxVal { get; }

            public bool CheckIsEquipment { get; }

            public Func<int, string> GetDisplayName { get; }

            public ItemControlGroup(ListBox itemList, NumericUpDown itemBox, ErrorProvider errorProvider, Label itemLabel,
                Func<int[]> getSettings, Action<int[]> saveSettings, bool checkIsEquipment = true)
            {
                ItemList = itemList;
                ItemBox = itemBox;
                ErrorProvider = errorProvider;
                ItemLabel = itemLabel;

                GetSettings = getSettings;
                SaveSettings = saveSettings;

                MinVal = 1;
                MaxVal = Consts.MAX_GEAR_ID;

                ItemBox.Minimum = MinVal;
                ItemBox.Maximum = MaxVal;

                CheckIsEquipment = checkIsEquipment;
                GetDisplayName = (id) => _character.itemInfo.itemName[id];
            }

            public ItemControlGroup(ListBox itemList, NumericUpDown itemBox, Label itemLabel,
                Func<int[]> getSettings, Action<int[]> saveSettings,
                bool checkIsEquipment, int minVal, int maxVal, Func<int, string> getDisplayName)
                : this(itemList, itemBox, null, itemLabel, getSettings, saveSettings, checkIsEquipment)
            {
                MinVal = minVal;
                MaxVal = maxVal;

                ItemBox.Minimum = MinVal;
                ItemBox.Maximum = MaxVal;

                GetDisplayName = getDisplayName;
            }

            public void ClearError() => SetError("");

            public void SetError(string message) => ErrorProvider?.SetError(ItemLabel, message);

            public void UpdateList(int[] newList) => UpdateItemList(ItemList, newList, GetDisplayName);
        }

        private static readonly Character _character = Main.Character;

        private bool _initializing = true;

        private readonly Dictionary<int, string> zoneList;
        private readonly Dictionary<int, string> titanZoneList;
        private readonly Dictionary<int, string> spriteEnemyList;

        private readonly ItemControlGroup _yggControls;
        private readonly ItemControlGroup _priorityControls;
        private readonly ItemControlGroup _blacklistControls;
        private readonly ItemControlGroup _titanControls;
        private readonly ItemControlGroup _goldControls;
        private readonly ItemControlGroup _questControls;
        private readonly ItemControlGroup _wishControls;
        private readonly ItemControlGroup _wishBlacklistControls;
        private readonly ItemControlGroup _shockwaveControls;
        private readonly ItemControlGroup _cookingControls;

        // Mode-loadout optimizer sections (route C3 3.2), one per mode; installed over the manual loadout UI.
        private LoadoutsPanel _loadoutsPanel;
        private InventoryAdvisorPanel _invPanel;
        private BoostsPanel _boostsPanel;
        private AdventurePanel _adventurePanel;
        private TitansPanel _titansPanel;
        private GoldPanel _goldPanel;
        private PitPanel _pitPanel;
        private SpendPanel _spendPanel;
        private ApPanel _apPanel;
        private PpPanel _ppPanel;
        private AtPanel _atPanel;
        private ChallengesPanel _challengesPanel;
        private LightsPanel _lights;
        private ActionsPanel _actions;
        private LogsPanel _logsPanel;
        // Section navigation registry ("Tab" or "Tab/Section") for the STATUS board's click-through.
        private readonly System.Collections.Generic.Dictionary<string, Action> _sectionNav
            = new System.Collections.Generic.Dictionary<string, Action>();
        private YggPanel _yggPanel;
        private QuestsPanel _questsPanel;
        private BloodPanel _bloodPanel;
        private AutopilotPanel _autopilotPanel;
        private ProfilePanel _profilePanel;

        // M1 Control Room shell: left rail nav + one canvas per section (tabControl1 stays alive but
        // hidden — the legacy pages' controls keep their bindings).
        private static readonly int RailW = UiTheme.S(170);
        private static readonly int CanvasW = UiTheme.S(1030);   // content width inside a section (1240 - rail - margins, at the tuning baseline)
        private Panel _rail;
        private Panel _canvasHost;
        private Button _railMaster;
        private readonly System.Collections.Generic.Dictionary<string, ScrollPanel> _sections
            = new System.Collections.Generic.Dictionary<string, ScrollPanel>();
        private class RailChild { public string Name; public Panel Row; public Panel Dot; public Label Lbl; }
        private class RailItem
        {
            public Panel Row; public Panel Dot; public Label Lbl; public Label Caret; public string Name;
            public bool Expanded;
            public readonly System.Collections.Generic.List<RailChild> Children
                = new System.Collections.Generic.List<RailChild>();
        }
        private readonly System.Collections.Generic.List<RailItem> _railItems
            = new System.Collections.Generic.List<RailItem>();
        // A1 accordion sub-nav: children per section, growing downward under the parent.
        private static readonly System.Collections.Generic.Dictionary<string, string[]> RailChildren
            = new System.Collections.Generic.Dictionary<string, string[]>
            {
                { "Advisors", new[] { "Overview", "Priorities" } },
                { "Economy", new[] { "Overview", "Planners" } },
                { "Systems", new[] { "Yggdrasil", "Quests", "Boosts", "Inventory", "Cooking", "Blood" } },
                { "Loadouts", new[] { "Titan", "Gold", "Quest", "Yggdrasil", "Cooking", "Loot Hunter", "Shockwave" } },
                { "Logs", new[] { "Advisor", "Loot", "Session" } },
                { "Cards", new[] { "Cards", "Wishes" } },
            };
        private readonly System.Collections.Generic.Dictionary<string, string> _activeChild
            = new System.Collections.Generic.Dictionary<string, string>();
        private readonly System.Collections.Generic.Dictionary<string, Action> _childNav
            = new System.Collections.Generic.Dictionary<string, Action>();
        private readonly System.Collections.Generic.Dictionary<string, Panel> _subPages
            = new System.Collections.Generic.Dictionary<string, Panel>();
        private readonly System.Collections.Generic.List<Panel> _cardsLegacy
            = new System.Collections.Generic.List<Panel>();
        private LogSliver _combatSliver;
        private LogSliver _pitSliver;
        private GrowthPanel _growthPanel;
        private ActivityRibbon _ribbon;
        private SystemIndexPanel _systemIndex;

        // Live status strip docked to the bottom of this window (route C3 Phase 2, in-GUI).
        private StatusPanel _statusPanel;

        // Dashboard landing view, inserted as the first tab (route C3 Phase 3).

        // Strangler step 1: clean master-toggle tab (BasicSettingsPanel), second tab.
        private BasicSettingsPanel _basicSettings;

        private readonly CheckBox[] _killTitan = new CheckBox[14];
        private readonly ComboBox[] _titanVersion = new ComboBox[7];

        private readonly ComboBox[] _cardRarity = new ComboBox[14];
        private readonly ComboBox[] _cardCost = new ComboBox[14];

        public SettingsForm()
        {
            try
            {
                _initializing = true;
                InitializeComponent();

                // M1 Control Room: ONE designed client size, no WideLayout flag (the old toggle left
                // panels on a 664 canvas inside an 869 window when off — user-caught). FixedSingle
                // still allows programmatic sizing; Shown re-asserts after Mono's DPI pass settles.
                try
                {
                    LogDebug(UiTheme.CalibrationInfo);
                    ClientSize = new System.Drawing.Size(RailW + UiTheme.S(1070), UiTheme.S(760));
                    UiLayout.PanelW = CanvasW;
                }
                catch (Exception wideEx) { LogDebug($"M1 sizing: {wideEx.Message}"); }

                for (int i = 0; i <= 13; i++)
                {
                    _killTitan[i] = GetElement<CheckBox>($"KillTitan{i + 1}");
                    _cardRarity[i] = GetElement<ComboBox>($"CardRarity{i + 1}");
                    _cardCost[i] = GetElement<ComboBox>($"CardCost{i + 1}");
                }

                for (int i = 0; i <= 6; i++)
                    _titanVersion[i] = GetElement<ComboBox>($"Titan{i + 6}Version");

                AdjustDimensions();

                // Populate our data sources
                var allZoneList = new Dictionary<int, string>(ZoneHelpers.ZoneList);

                spriteEnemyList = new Dictionary<int, string>();
                foreach (var x in _character.adventureController.enemyList)
                {
                    foreach (var enemy in x)
                    {
                        try
                        {
                            spriteEnemyList.Add(enemy.spriteID, enemy.name);
                        }
                        catch
                        {
                            // pass
                        }
                    }
                }

                UpdateTitanVersions();

                string[] cardBonusTypes = typeof(cardBonus).GetEnumNames().Where(x => x != "none").ToArray();
                var cardSortOptions = new List<string> { "RARITY", "TIER", "COST", "PROTECTED", "CHANGE", "VALUE", "NORMALVALUE" };
                foreach (string sortOption in cardSortOptions)
                {
                    CardSortOptions.Items.Add(sortOption);
                    CardSortOptions.Items.Add($"{sortOption}-ASC");
                }
                foreach (string bonus in cardBonusTypes)
                {
                    CardSortOptions.Items.Add($"TYPE:{bonus}");
                    CardSortOptions.Items.Add($"TYPE-ASC:{bonus}");
                }

                for (int i = 0; i <= 13; i++)
                {
                    _cardRarity[i].DataSource = new BindingSource(CardManager.rarityList, null);
                    _cardRarity[i].ValueMember = "Key";
                    _cardRarity[i].DisplayMember = "Value";

                    _cardCost[i].DataSource = new BindingSource(CardManager.costList, null);
                }

                FavoredMacguffin.DataSource = new BindingSource(InventoryManager.macguffinList, null);
                FavoredMacguffin.ValueMember = "Key";
                FavoredMacguffin.DisplayMember = "Value";

                // Remove ITOPOD for non combat zones
                allZoneList.Remove(1000);
                allZoneList.Remove(-1);

                zoneList = allZoneList.Where(x => !ZoneHelpers.TitanZones.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value);
                titanZoneList = allZoneList.Except(zoneList).ToDictionary(x => x.Key, x => x.Value);

                CombatTargetZone.DataSource = new BindingSource(zoneList, null);
                CombatTargetZone.ValueMember = "Key";
                CombatTargetZone.DisplayMember = "Value";

                EnemyBlacklistZone.DataSource = new BindingSource(zoneList, null);
                EnemyBlacklistZone.ValueMember = "Key";
                EnemyBlacklistZone.DisplayMember = "Value";
                EnemyBlacklistZone.SelectedIndex = 0;

                MoneyPitThreshold.DataSource = new BindingSource(MoneyPitManager.moneyPitThresholds, null);
                MoneyPitThreshold.ValueMember = "Key";
                MoneyPitThreshold.DisplayMember = "Value";

                numberErrProvider.SetIconAlignment(BloodNumberThreshold, ErrorIconAlignment.MiddleRight);

                YggLoadoutItem.TextChanged += YggLoadoutItem_TextChanged;
                PriorityBoostItemAdd.TextChanged += PriorityBoostItemAdd_TextChanged;
                BlacklistAddItem.TextChanged += BlacklistAddItem_TextChanged;
                TitanAddItem.TextChanged += TitanAddItem_TextChanged;
                GoldItemBox.TextChanged += GoldItemBox_TextChanged;
                QuestLoadoutItem.TextChanged += QuestLoadoutBox_TextChanged;
                WishAddInput.TextChanged += WishAddInput_TextChanged;
                WishBlacklistAddInput.TextChanged += WishBlacklistAddInput_TextChanged;
                ShockwaveInput.TextChanged += ShockwaveInput_TextChanged;
                CookingLoadoutItem.TextChanged += CookingLoadoutBox_TextChanged;

                _yggControls = new ItemControlGroup(
                    YggdrasilLoadoutBox, YggLoadoutItem, yggErrorProvider, YggItemLabel,
                    () => Settings.YggdrasilLoadout, (settings) => Settings.YggdrasilLoadout = settings);

                _priorityControls = new ItemControlGroup(
                    PriorityBoostBox, PriorityBoostItemAdd, invPrioErrorProvider, PriorityBoostLabel,
                    () => Settings.PriorityBoosts, (settings) => Settings.PriorityBoosts = settings);

                _blacklistControls = new ItemControlGroup(
                    BlacklistBox, BlacklistAddItem, null, BlacklistLabel,
                    () => Settings.BoostBlacklist, (settings) => Settings.BoostBlacklist = settings, false);

                _titanControls = new ItemControlGroup(
                    TitanLoadout, TitanAddItem, titanErrProvider, TitanLabel,
                    () => Settings.TitanLoadout, (settings) => Settings.TitanLoadout = settings);

                _goldControls = new ItemControlGroup(
                    GoldLoadout, GoldItemBox, goldErrorProvider, GoldItemLabel,
                    () => Settings.GoldDropLoadout, (settings) => Settings.GoldDropLoadout = settings);

                _questControls = new ItemControlGroup(
                    QuestLoadoutBox, QuestLoadoutItem, questErrorProvider, QuestItemLabel,
                    () => Settings.QuestLoadout, (settings) => Settings.QuestLoadout = settings);

                _wishControls = new ItemControlGroup(
                    WishPriority, WishAddInput, AddWishLabel,
                    () => Settings.WishPriorities, (settings) => Settings.WishPriorities = settings,
                    false, 0, Consts.MAX_WISH_ID, (id) => _character.wishesController.properties[id].wishName);

                _wishBlacklistControls = new ItemControlGroup(
                    WishBlacklist, WishBlacklistAddInput, AddWishBlacklistLabel,
                    () => Settings.WishBlacklist, (settings) => Settings.WishBlacklist = settings,
                    false, 0, Consts.MAX_WISH_ID, (id) => _character.wishesController.properties[id].wishName);

                _shockwaveControls = new ItemControlGroup(
                    ShockwaveBox, ShockwaveInput, shockwaveErrorProvider, ShockwaveLabel,
                    () => Settings.Shockwave, (settings) => Settings.Shockwave = settings, false);

                _cookingControls = new ItemControlGroup(
                    CookingLoadoutBox, CookingLoadoutItem, cookingErrorProvider, CookingItemLabel,
                    () => Settings.CookingLoadout, (settings) => Settings.CookingLoadout = settings);

                TryItemBoxTextChanged(_yggControls, out _);
                TryItemBoxTextChanged(_priorityControls, out _);
                TryItemBoxTextChanged(_blacklistControls, out _);
                TryItemBoxTextChanged(_titanControls, out _);
                TryItemBoxTextChanged(_goldControls, out _);
                TryItemBoxTextChanged(_questControls, out _);
                TryItemBoxTextChanged(_wishControls, out _);
                TryItemBoxTextChanged(_wishBlacklistControls, out _);
                TryItemBoxTextChanged(_shockwaveControls, out _);
                TryItemBoxTextChanged(_cookingControls, out _);

                // Turn each mode's manual loadout section into the gear optimizer + live preview (3.2).
                InstallModeLoadouts();

                VersionLabel.Text = $"NGU Advisor v{Main.Version} · build {Main.BuildTag}";

                // Color-only theme pass (no font/layout changes). The existing "Edit" button in the
                // allocation tab (next to the profile dropdown) now opens the visual Profile Editor, so
                // emphasize it as the primary action. Guarded so any theming issue can't break the form.
                try
                {
                    UiTheme.ApplyTo(this);
                }
                catch (Exception themeEx)
                {
                    LogDebug($"Theme apply failed: {themeEx.Message}");
                }

                // Grow the window and dock a live status strip along the bottom. tabControl1 is Dock.Fill,
                // so it keeps its size and the strip occupies the new space beneath it.
                try
                {
                    Height += StatusPanel.PanelHeight;
                    _statusPanel = new StatusPanel();
                    Controls.Add(_statusPanel);
                    _statusPanel.BringToFront();
                }
                catch (Exception spEx)
                {
                    LogDebug($"Status panel init failed: {spEx.Message}");
                }

                // M1 Control Room shell (user-approved mockup): left rail nav + section canvases;
                // the legacy tab control goes invisible but stays alive for its bindings. Guarded:
                // on failure the tab strip stays usable.
                try
                {
                    BuildControlRoom();
                }
                catch (Exception consEx)
                {
                    LogDebug($"Control room build failed: {consEx.Message}");
                }

                // Final-pixel sizing pass: Mono settles DPI scaling after construction, so ctor-time
                // ClientSize reads can be stale (widemode v2: panels sized for ~695 in a 940 window).
                // At Shown the size is authoritative — log it, re-assert wide, re-fit the custom panels.
                Shown += (s, e) =>
                {
                    try
                    {
                        Log($"Settings window client: {ClientSize.Width}x{ClientSize.Height} (M1 control room)");
                        // Mono's DPI pass can restamp the designed size after construction — re-assert.
                        if (ClientSize.Width < RailW + UiTheme.S(1060))
                        {
                            ClientSize = new System.Drawing.Size(RailW + UiTheme.S(1070), UiTheme.S(760) + (_statusPanel?.Height ?? 0));
                            Log($"M1 client re-applied at Shown: {ClientSize.Width}x{ClientSize.Height}");
                        }

                        // Machine-enforced pre-flight: every custom panel is scanned for overlapping
                        // siblings and clipped text; violations land in debug.log as "UI AUDIT" lines.
                        if (_loadoutsPanel != null) UiLayout.Audit(_loadoutsPanel, "Loadouts");
                        if (_invPanel != null) UiLayout.Audit(_invPanel, "Inventory");
                        if (_boostsPanel != null) UiLayout.Audit(_boostsPanel, "Boosts");
                        if (_adventurePanel != null) UiLayout.Audit(_adventurePanel, "Adventure");
                        if (_titansPanel != null) UiLayout.Audit(_titansPanel, "Titans");
                        if (_goldPanel != null) UiLayout.Audit(_goldPanel, "Gold");
                        if (_pitPanel != null) UiLayout.Audit(_pitPanel, "Pit");
                        if (_spendPanel != null) UiLayout.Audit(_spendPanel, "Spend");
                        if (_apPanel != null) UiLayout.Audit(_apPanel, "AP");
                        if (_ppPanel != null) UiLayout.Audit(_ppPanel, "PP");
                        if (_atPanel != null) UiLayout.Audit(_atPanel, "AT");
                        if (_challengesPanel != null) UiLayout.Audit(_challengesPanel, "Challenges");
                        if (_yggPanel != null) UiLayout.Audit(_yggPanel, "Yggdrasil");
                        if (_questsPanel != null) UiLayout.Audit(_questsPanel, "Quests");
                        if (_bloodPanel != null) UiLayout.Audit(_bloodPanel, "Blood");
                        if (_autopilotPanel != null) UiLayout.Audit(_autopilotPanel, "Autopilot");
                        if (_lights != null) UiLayout.Audit(_lights, "Lights");
                        if (_growthPanel != null) UiLayout.Audit(_growthPanel, "Growth");
                        if (_actions != null) UiLayout.Audit(_actions, "Actions");
                        if (_logsPanel != null) UiLayout.Audit(_logsPanel, "Logs");
                        if (_basicSettings != null) UiLayout.Audit(_basicSettings, "Settings");
                        if (_systemIndex != null) UiLayout.Audit(_systemIndex, "SystemIndex");
                        if (_rail != null) UiLayout.Audit(_rail, "Rail");

                        // Horizontal overflow: the app scrolls vertically only (README), so a section
                        // wider than its viewport is a bug whose only visible symptom is a scrollbar —
                        // the control causing it is off-screen. Name it in the log instead.
                        // Audit the CONTENT surface, not the ScrollPanel: the panel's own children are the
                        // scrollbar and the content host, so measuring it would compare the wrong things.
                        foreach (var sec in _sections)
                            UiLayout.AuditWidth(Host(sec.Value), $"{sec.Key}/width");
                        foreach (var sub in _subPages)
                            UiLayout.AuditWidth(Host(sub.Value), $"{sub.Key}/width");
                    }
                    catch (Exception shownEx) { LogDebug($"Shown sizing: {shownEx.Message}"); }
                };

                _initializing = false;
            }
            catch (Exception ex)
            {
                LogDebug($"{ex.Message}:\n{ex.StackTrace}");
            }
        }

        // M1 Control Room (user-approved mockup): left rail nav with per-section health dots + one
        // canvas per section — whole sections visible at once, no sub-tab hopping. tabControl1 is
        // HIDDEN but alive: the legacy pages' controls keep every binding, and the Cards/Wishes
        // legacy pages (unlock-gated by design) are re-hosted into the Cards section.
        private void BuildControlRoom()
        {
            var pages = new System.Collections.Generic.Dictionary<string, TabPage>();
            foreach (TabPage p in tabControl1.TabPages)
                if (!pages.ContainsKey(p.Text))
                    pages[p.Text] = p;

            // Shell chrome first: rail docks left, the canvas host fills the rest (front of z-order
            // so Fill computes after Left — the proven bar+body pattern).
            _rail = new Panel { Dock = DockStyle.Left, Width = RailW, BackColor = UiTheme.Ground };
            _canvasHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Ground };
            Controls.Add(_rail);
            Controls.Add(_canvasHost);
            _canvasHost.BringToFront();
            BuildRail();

            // Boosts V3 (first clean legacy rebuild): the new BoostsPanel (segmented BOOSTING/TRANSFORMS
            // with the advisor/manual toggle) replaces the legacy Boosts page in Systems; the legacy
            // resx page retires to Advanced (renamed "Old Boosts") as the escape hatch.
            try
            {
                if (pages.TryGetValue("Inventory", out var invPage))
                {
                    invPage.Text = "Old Boosts";
                    pages.Remove("Inventory");
                    pages["Old Boosts"] = invPage;
                }
                _invPanel = new InventoryAdvisorPanel(CanvasW);   // full-canvas page (C1)
                _boostsPanel = new BoostsPanel(CanvasW);   // side-by-side BOOSTING | TRANSFORMS
            }
            catch (Exception invEx) { LogDebug($"Inventory section init failed: {invEx.Message}"); }

            var systemsExtras = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Panel>>();
            if (_invPanel != null)
                systemsExtras.Add(new System.Collections.Generic.KeyValuePair<string, Panel>("Inventory", _invPanel));
            if (_boostsPanel != null)
                systemsExtras.Add(new System.Collections.Generic.KeyValuePair<string, Panel>("Boosts", _boostsPanel));

            // Adventure V2 + Titans T2 (clean rebuilds): custom panels replace the legacy pages,
            // which retire to Advanced.
            var combatExtras = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Panel>>();
            try
            {
                if (pages.TryGetValue("Adventure", out var advPage))
                {
                    advPage.Text = "Old Adventure";
                    pages.Remove("Adventure");
                    pages["Old Adventure"] = advPage;
                }
                _adventurePanel = new AdventurePanel(UiTheme.S(515));   // right Combat column
                combatExtras.Add(new System.Collections.Generic.KeyValuePair<string, Panel>("Adventure", _adventurePanel));
            }
            catch (Exception advEx) { LogDebug($"Adventure section init failed: {advEx.Message}"); }
            try
            {
                if (pages.TryGetValue("Titans", out var titanPage))
                {
                    titanPage.Text = "Old Titans";
                    pages.Remove("Titans");
                    pages["Old Titans"] = titanPage;
                }
                _titansPanel = new TitansPanel(UiTheme.S(515));   // left Combat column
                combatExtras.Add(new System.Collections.Generic.KeyValuePair<string, Panel>("Titans", _titansPanel));
            }
            catch (Exception titEx) { LogDebug($"Titans section init failed: {titEx.Message}"); }
            try
            {
                _atPanel = new AtPanel(CanvasW);   // full-width row, third on Economy > PLANNERS
            }
            catch (Exception atEx) { LogDebug($"AT section init failed: {atEx.Message}"); }

            // Gold E1 pipeline + Pit (clean rebuilds): legacy Gold page retires to Advanced.
            var economyExtras = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Panel>>();
            try
            {
                if (pages.TryGetValue("Gold", out var goldPage))
                {
                    goldPage.Text = "Old Gold";
                    pages.Remove("Gold");
                    pages["Old Gold"] = goldPage;
                }
                _goldPanel = new GoldPanel(UiTheme.S(520));   // left Economy column
                economyExtras.Add(new System.Collections.Generic.KeyValuePair<string, Panel>("Gold", _goldPanel));
            }
            catch (Exception goldEx) { LogDebug($"Gold section init failed: {goldEx.Message}"); }
            try
            {
                _pitPanel = new PitPanel(UiTheme.S(490));   // right Economy column
                economyExtras.Add(new System.Collections.Generic.KeyValuePair<string, Panel>("Pit", _pitPanel));
            }
            catch (Exception pitEx) { LogDebug($"Pit section init failed: {pitEx.Message}"); }
            try
            {
                _spendPanel = new SpendPanel(CanvasW);   // full-width row, first on Economy > PLANNERS (the cross-currency rail above the per-currency detail)
            }
            catch (Exception spendEx) { LogDebug($"Spend section init failed: {spendEx.Message}"); }
            try
            {
                _apPanel = new ApPanel(CanvasW);   // full-width row, second on Economy > PLANNERS
            }
            catch (Exception apEx) { LogDebug($"AP section init failed: {apEx.Message}"); }
            try
            {
                _ppPanel = new PpPanel(CanvasW);   // full-width row, second on Economy > PLANNERS (below the AP queue)
            }
            catch (Exception ppEx) { LogDebug($"PP section init failed: {ppEx.Message}"); }

            // Yggdrasil Y1 orchard grid (Systems rebuild #1): legacy page retires to Advanced, and the
            // legacy money-pit page retires too (fully superseded by Economy > PIT).
            try
            {
                if (pages.TryGetValue("Yggdrasil", out var yggPage))
                {
                    yggPage.Text = "Old Yggdrasil";
                    pages.Remove("Yggdrasil");
                    pages["Old Yggdrasil"] = yggPage;
                }
                if (pages.TryGetValue("The Pit", out var pitPage))
                {
                    pitPage.Text = "Old Pit";
                    pages.Remove("The Pit");
                    pages["Old Pit"] = pitPage;
                }
                _yggPanel = new YggPanel(CanvasW);   // full-canvas page (C1)
                systemsExtras.Insert(0, new System.Collections.Generic.KeyValuePair<string, Panel>("Yggdrasil", _yggPanel));
            }
            catch (Exception yggEx) { LogDebug($"Ygg section init failed: {yggEx.Message}"); }
            try
            {
                if (pages.TryGetValue("Quests", out var questPage))
                {
                    questPage.Text = "Old Quests";
                    pages.Remove("Quests");
                    pages["Old Quests"] = questPage;
                }
                _questsPanel = new QuestsPanel(CanvasW);   // full-canvas page (C1)
                systemsExtras.Insert(1, new System.Collections.Generic.KeyValuePair<string, Panel>("Quests", _questsPanel));
            }
            catch (Exception questEx) { LogDebug($"Quests section init failed: {questEx.Message}"); }

            // Advisors panels (B1): OVERVIEW home = lights → AUTO PROFILE card → CHALLENGES with the
            // whole bottom; PRIORITIES is its own rail sub-page (full ranked list, optimal inline).
            try
            {
                _lights = new LightsPanel(this, CanvasW, 6);
                _actions = new ActionsPanel(CanvasW - UiTheme.S(40), fullPage: true);
                _autopilotPanel = new AutopilotPanel(this, CanvasW);
                _challengesPanel = new ChallengesPanel(CanvasW);
                _logsPanel = new LogsPanel(CanvasW);
                _lights.BoardRefreshed += () => { try { UpdateRailDots(); } catch { } };
            }
            catch (Exception chEx) { LogDebug($"Advisors section init failed: {chEx.Message}"); }

            // PROFILE panel (UI4) — the mutable profile home, moved out of the Overview AUTO PROFILE card.
            // Inset width (CanvasW - 40) like the other placed panels, so a x=20 placement leaves a right
            // margin and never pushes past the section edge / summons a horizontal scrollbar.
            try { _profilePanel = new ProfilePanel(this, CanvasW - UiTheme.S(40)); }
            catch (Exception pEx) { LogDebug($"Profile panel init failed: {pEx.Message}"); }

            // ---- section canvases (A1: sections with children host sub-pages) ----
            var advisors = NewSection("Advisors");
            advisors.Scrollable = false;   // the sub-pages scroll, not the host
            var advStatus = NewSubPage(advisors, Destinations.Overview);
            var advActions = NewSubPage(advisors, Destinations.Priorities);
            _childNav[Destinations.Overview] = ShowSubPage("Advisors", Destinations.Overview);
            _childNav[Destinations.Priorities] = ShowSubPage("Advisors", Destinations.Priorities);
            // Page-title heading (UI2). PRIORITIES' heading lives in ActionsPanel ("ADVISOR PRIORITIES");
            // the OVERVIEW page had none, so it gets one here and everything below shifts down one head
            // pitch (uniform +26 → relative spacing unchanged, no new overlap). AutoSize = immune to clip.
            var advOverviewHead = new Label
            {
                Text = "ADVISOR OVERVIEW", AutoSize = true, Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new System.Drawing.Point(UiTheme.S(20), UiTheme.S(12))
            };
            advStatus.Controls.Add(advOverviewHead);
            // CHAINED Y (was four fixed offsets 38/140/256/508): the lights board and the growth band now
            // DERIVE their heights from the measured line box, so they are taller than the tuned constants
            // at real DPI and a fixed next-row offset would let them overlap. The gaps below are the ones
            // the tuned offsets implied (8 / 12 / 252).
            int advY = UiTheme.S(38);
            if (_lights != null)
            {
                Place(advStatus, _lights, UiTheme.S(20), advY, CanvasW, _lights.Height);
                advY = _lights.Bottom + UiTheme.S(8);
            }
            // GROWTH band (G1): rate tiles between the lights and the plan card.
            _growthPanel = new GrowthPanel(CanvasW);
            Place(advStatus, _growthPanel, UiTheme.S(20), advY, CanvasW, _growthPanel.Height);
            int autopilotTop = _growthPanel.Bottom + UiTheme.S(12);
            int challengesTop = autopilotTop + UiTheme.S(252);
            if (_autopilotPanel != null) Place(advStatus, _autopilotPanel, UiTheme.S(20), autopilotTop, CanvasW, UiTheme.S(240));
            if (_challengesPanel != null)
            {
                Place(advStatus, _challengesPanel, UiTheme.S(20), challengesTop, CanvasW, UiTheme.S(150));   // placeholder; panel self-sizes to content on refresh
                // The autopilot card reflows (wrapped titles/plans) — challenges rides below it.
                if (_autopilotPanel != null)
                    _autopilotPanel.SizeChanged += (s, e) =>
                    {
                        try { _challengesPanel.Top = Math.Max(challengesTop, _autopilotPanel.Bottom + UiTheme.S(12)); } catch { }
                    };
            }
            if (_actions != null) Place(advActions, _actions, UiTheme.S(20), UiTheme.S(12), CanvasW - UiTheme.S(40), UiTheme.S(700));

            // PROFILE (UI4): a distinct top-level section (not an Advisors child) — the canonical mutable
            // home for allocation source + selected file + recommendation. Reuses ApplyProfile/ShowEditor.
            var profileSec = NewSection("Profile");
            if (_profilePanel != null) Place(profileSec, _profilePanel, UiTheme.S(20), UiTheme.S(12), CanvasW - UiTheme.S(40), _profilePanel.Height);

            var combat = NewSection("Combat");
            if (_titansPanel != null) Place(combat, _titansPanel, UiTheme.S(20), UiTheme.S(12), UiTheme.S(505), UiTheme.S(500));
            // Adventure asks for its own height: it has no scrollbar of its own by design, so a tuned 580
            // clips whichever page is tallest. The sliver below then chains off the taller column.
            // Adventure is 80px taller than Titans: the two-layer control bar cost it 72px and its content
            // was already near the old 500px ceiling. Giving it the height is better than making the column
            // scroll inside itself — an AutoScroll host whose content fills the width summons a horizontal
            // scrollbar as well as a vertical one. This section canvas already scrolls, so nothing is lost.
            if (_adventurePanel != null) Place(combat, _adventurePanel, UiTheme.S(545), UiTheme.S(12), UiTheme.S(505), Math.Max(UiTheme.S(580), _adventurePanel.ContentHeight));
            // The 72px the control bar cost Adventure is paid for by the log tail, NOT by a scrollbar:
            // the section's total content height stays at its pre-migration 734px, so Combat still fits
            // the viewport with nothing below the fold. The sliver is a convenience tail — the full log
            // is one click away in LOGS — which makes it the cheapest 80px on this screen.
            _combatSliver = new LogSliver("COMBAT LOG — live tail of combat.log", "combat.log", CanvasW, UiTheme.S(130));
            int combatSliverY = Math.Max(UiTheme.S(604),
                Math.Max(_titansPanel?.Bottom ?? 0, _adventurePanel?.Bottom ?? 0) + UiTheme.S(12));
            Place(combat, _combatSliver, UiTheme.S(20), combatSliverY, CanvasW, UiTheme.S(130));

            // Economy is split into two rail sub-pages (A1 pattern, same as Advisors): OVERVIEW keeps the
            // gold/pit columns and the pit log tail exactly as they were; PLANNERS is the new home of the
            // three read-only advisory readouts (AP, PP, AT). They used to sit at the bottom of the Economy
            // and Combat canvases, below the fold, where nobody found them — a sub-page is a rail entry, so
            // they are now nameable and reachable in one click. AT lives here despite being combat-flavoured:
            // it is the same kind of advisory readout, not a control surface.
            var economy = NewSection("Economy");
            economy.Scrollable = false;   // the sub-pages scroll, not the host
            // OVERVIEW is the first entry in RailChildren["Economy"], which is what makes it the default
            // visible child: SelectSectionM1 falls back to RailChildren[name][0] whenever the section has no
            // remembered _activeChild, so opening Economy still lands on gold/pit exactly as it did before.
            // Gold/Pit keep routing to the bare "Economy" section (they are not sub-page keys), so the
            // overview key is spelled literally here.
            var ecoOverview = NewSubPage(economy, "Economy/Overview");
            var ecoPlanners = NewSubPage(economy, Destinations.ApPurchases);
            _childNav["Economy/Overview"] = ShowSubPage("Economy", "Economy/Overview");
            _childNav[Destinations.ApPurchases] = ShowSubPage("Economy", Destinations.ApPurchases);
            if (_goldPanel != null) Place(ecoOverview, _goldPanel, UiTheme.S(20), UiTheme.S(12), UiTheme.S(520), UiTheme.S(520));
            if (_pitPanel != null) Place(ecoOverview, _pitPanel, UiTheme.S(560), UiTheme.S(12), UiTheme.S(490), UiTheme.S(520));
            _pitSliver = new LogSliver("PIT & SPIN LOG — live tail of pitspin.log", "pitspin.log", CanvasW, UiTheme.S(190));
            Place(ecoOverview, _pitSliver, UiTheme.S(20), UiTheme.S(544), CanvasW, UiTheme.S(190));
            // PLANNERS: the three readouts stacked, each asking for its own ContentHeight — none of them
            // scrolls (the sub-page is the one scroll owner), so a tuned height would clip the AP queue rows,
            // the PP plan lines or AT's last Time Machine lines at real DPI. Each row chains off the previous
            // row's Bottom for the same reason: a tuned offset would overlap them.
            int ecoY = UiTheme.S(12);
            if (_spendPanel != null)
            {
                Place(ecoPlanners, _spendPanel, UiTheme.S(20), ecoY, CanvasW, _spendPanel.ContentHeight);
                ecoY = _spendPanel.Bottom + UiTheme.S(12);
            }
            if (_apPanel != null)
            {
                Place(ecoPlanners, _apPanel, UiTheme.S(20), ecoY, CanvasW, _apPanel.ContentHeight);
                ecoY = _apPanel.Bottom + UiTheme.S(12);
            }
            if (_ppPanel != null)
            {
                Place(ecoPlanners, _ppPanel, UiTheme.S(20), ecoY, CanvasW, _ppPanel.ContentHeight);
                ecoY = _ppPanel.Bottom + UiTheme.S(12);
            }
            if (_atPanel != null)
                Place(ecoPlanners, _atPanel, UiTheme.S(20), ecoY, CanvasW, _atPanel.ContentHeight);

            // Systems C1 (user pick): one system, one full-canvas page, navigated by rail children.
            var systems = NewSection("Systems");
            systems.Scrollable = false;   // the sub-pages scroll, not the host
            try { _bloodPanel = new BloodPanel(CanvasW); } catch (Exception be) { LogDebug($"Blood panel init failed: {be.Message}"); }
            var pgYgg = NewSubPage(systems, "Systems/Yggdrasil");
            var pgQuests = NewSubPage(systems, "Systems/Quests");
            var pgBoosts = NewSubPage(systems, "Systems/Boosts");
            var pgInv = NewSubPage(systems, "Systems/Inventory");
            var pgCook = NewSubPage(systems, "Systems/Cooking");
            var pgBlood = NewSubPage(systems, "Systems/Blood");
            foreach (var key in new[] { "Systems/Yggdrasil", "Systems/Quests", "Systems/Boosts", "Systems/Inventory", "Systems/Cooking", "Systems/Blood" })
                _childNav[key] = ShowSubPage("Systems", key);
            if (_yggPanel != null) Place(pgYgg, _yggPanel, UiTheme.S(20), UiTheme.S(12), CanvasW, UiTheme.S(560));
            if (_questsPanel != null) Place(pgQuests, _questsPanel, UiTheme.S(20), UiTheme.S(12), CanvasW, UiTheme.S(360));
            // Boosts asks for its own height: its lists are specified in ROWS now, so a tuned 460 would
            // cut the last row off (the sub-page scrolls, so a taller panel costs nothing).
            if (_boostsPanel != null) Place(pgBoosts, _boostsPanel, UiTheme.S(20), UiTheme.S(12), CanvasW, Math.Max(UiTheme.S(460), _boostsPanel.ContentHeight));
            if (_invPanel != null) Place(pgInv, _invPanel, UiTheme.S(20), UiTheme.S(12), CanvasW, UiTheme.S(640));
            if (_bloodPanel != null) Place(pgBlood, _bloodPanel, UiTheme.S(20), UiTheme.S(12), CanvasW, UiTheme.S(480));
            if (pages.TryGetValue("Cooking", out var cookPage))
            {
                var cookBox = new Panel { Bounds = new System.Drawing.Rectangle(UiTheme.S(20), UiTheme.S(12), CanvasW, UiTheme.S(660)), BackColor = UiTheme.Ground };
                var cookKids = new Control[cookPage.Controls.Count];
                cookPage.Controls.CopyTo(cookKids, 0);
                foreach (var k in cookKids) { cookPage.Controls.Remove(k); cookBox.Controls.Add(k); }
                pgCook.Controls.Add(cookBox);
                tabControl1.TabPages.Remove(cookPage);
            }

            var logs = NewSection("Logs");
            if (_logsPanel != null)
            {
                Place(logs, _logsPanel, UiTheme.S(20), UiTheme.S(12), CanvasW, UiTheme.S(700));
                _childNav["Logs/Advisor"] = () => _logsPanel.SelectSource(0);
                _childNav["Logs/Loot"] = () => _logsPanel.SelectSource(1);
                _childNav["Logs/Session"] = () => _logsPanel.SelectSource(2);
            }

            var loadouts = NewSection("Loadouts");
            try
            {
                _loadoutsPanel = new LoadoutsPanel(CanvasW);
                _loadoutsPanel.HideModeBar();   // the modes live in the rail now
                Place(loadouts, _loadoutsPanel, UiTheme.S(20), UiTheme.S(12), CanvasW, UiTheme.S(720));
                foreach (var mode in RailChildren["Loadouts"])
                {
                    string m = mode;
                    _childNav[$"Loadouts/{m}"] = () => _loadoutsPanel.SelectModeByName(m);
                }
            }
            catch (Exception loEx) { LogDebug($"Loadouts section init failed: {loEx.Message}"); }

            var settingsSec = NewSection("Settings");
            try
            {
                _basicSettings = new BasicSettingsPanel();
                settingsSec.Content.Controls.Add(_basicSettings);   // Dock.Top + explicit height; no scroll of its own
                _basicSettings.Sync();

                // SYSTEM INDEX above APPLICATION SETTINGS — ONE document, ONE scrollbar (slice 7.5A).
                //
                // Docking order, same rule as the rail and the ribbon: WinForms lays docked children out
                // from the LAST index to the FIRST, so the control at the BACK of the z-order claims its
                // edge first. Both children are Dock.Top now, so the back one takes the TOP strip: the
                // index is added after BasicSettingsPanel (landing at the back) and SendToBack() says so
                // explicitly; BasicSettingsPanel comes to the front and stacks underneath it.
                //
                // SCROLL OWNERSHIP: the SECTION (already AutoScroll) is the single scroll owner. Neither
                // child scrolls. The full document — index + settings — scrolls as one surface, so the
                // index no longer steals a fixed slice of the settings' viewport, and there is no nested
                // scroll to trap the wheel.
                _systemIndex = new SystemIndexPanel();
                settingsSec.Content.Controls.Add(_systemIndex);
                _systemIndex.SendToBack();
                _basicSettings.BringToFront();

                // ONE QUERY, TWO RENDERERS, NO OWNER. The index owns the search box and its own rows; the
                // settings panel owns its own canonical controls. Neither knows the other exists — the form
                // is only a wire between them, which is all a coordinator should be. Both then ask the same
                // pure matcher the same question, so the product has exactly one set of matching rules and
                // no search "service" to keep in step with itself.
                //
                // EVERY QUERY LANDS AT THE TOP. One keystroke can take this document from 824px to 76px, and
                // an offset measured against the old document is meaningless against the new one — it is how
                // you end up staring at blank space with your results scrolled off above. There is nothing to
                // preserve: the search box and the first results are at the top, which is exactly where you
                // want to be. Same assignment the section already uses when Settings is opened (:704), same
                // scroll owner, no new container, no ScrollControlIntoView.
                //
                // No silent catch: Filter() already logs its own failures, and a second empty `catch {}` here
                // would swallow anything it did not. A search that quietly stops working is worse than one
                // that visibly breaks.
                _systemIndex.SearchChanged += q =>
                {
                    _basicSettings.Filter(q);
                    settingsSec.ScrollToTop();
                };

                // Re-homed from BasicSettingsPanel, which used to own the scroll: Mono can restore a
                // section mid-scroll on return, so land at the top whenever Settings is shown.
                settingsSec.VisibleChanged += (s, e) =>
                {
                    if (settingsSec.Visible) settingsSec.ScrollToTop();
                };
            }
            catch (Exception basicEx) { LogDebug($"Settings section init failed: {basicEx.Message}"); }

            // Cards: the two unlock-gated legacy pages as rail children (untouched otherwise).
            var cardsSec = NewSection("Cards");
            BuildCardsSection(cardsSec, pages);

            // ACTIVITY RIBBON — the shell-level feedback surface, so no panel has to grow its own.
            //
            // Docking order is the whole trick here. WinForms lays docked children out from the LAST index
            // in Controls to the FIRST, so the control at the BACK of the z-order gets first claim on the
            // client area and a Dock.Fill sibling must be in FRONT of it to compute afterwards (the same
            // rule the rail/canvas pair already relies on). Every section was added to _canvasHost before
            // this, so adding the ribbon now puts it at the back = docked first = it takes the top strip,
            // and the Fill sections lay out in what remains. SendToBack() is belt-and-braces.
            //
            // Hidden => excluded from the dock pass entirely => zero footprint, no reserved gap.
            _ribbon = new ActivityRibbon(this);
            _canvasHost.Controls.Add(_ribbon);
            _ribbon.SendToBack();

            // Retired pages tidy-up (tab strip is hidden, but keep the collection clean). Controls
            // stay alive for the legacy bindings.
            foreach (var retired in new[] { "Old Titans", "Old Gold", "Old Pit", "Old Yggdrasil", "Old Quests", "Old Boosts", "Old Adventure", "General", "Allocation" })
                if (pages.TryGetValue(retired, out var deadPage))
                    tabControl1.TabPages.Remove(deadPage);

            tabControl1.Visible = false;
            SelectSectionM1("Advisors");
        }

        private ScrollPanel NewSection(string name)
        {
            // ScrollPanel, not Panel: it owns its scrollbar (the OS one is a thin system metric that no
            // property can widen), is double-buffered, and takes the wheel itself so a precision touchpad
            // scrolls smoothly instead of jumping.
            var sec = new ScrollPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Ground, Visible = false };
            _canvasHost.Controls.Add(sec);
            _sections[name] = sec;
            _sectionNav[name] = () => SelectSectionM1(name);
            return sec;
        }

        // A ScrollPanel's children belong to its Content — the panel itself holds the scrollbar, so
        // anything added directly to it would sit beside the bar and outside the surface that moves.
        // One indirection here keeps every Place() call site unchanged.
        private static Panel Host(Panel p) => p is ScrollPanel sp ? sp.Content : p;

        // Panels set Dock.Fill in their ctors (the old sub-tab hosting); pin them to explicit
        // bounds inside the section canvas instead.
        private static void Place(Panel host, Control c, int x, int y, int w, int h)
        {
            c.Dock = DockStyle.None;
            c.Bounds = new System.Drawing.Rectangle(x, y, w, h);
            Host(host).Controls.Add(c);
        }

        private void SelectSectionM1(string name)
        {
            if (!_sections.ContainsKey(name)) return;
            foreach (var kv in _sections)
                kv.Value.Visible = kv.Key == name;
            foreach (var it in _railItems)
            {
                bool on = it.Name == name;
                it.Row.BackColor = on ? UiTheme.Accent : UiTheme.Ground;
                it.Lbl.ForeColor = on ? System.Drawing.Color.White : UiTheme.Ink;
                it.Lbl.BackColor = it.Row.BackColor;
                if (it.Caret != null)
                {
                    it.Caret.BackColor = it.Row.BackColor;
                    it.Caret.ForeColor = on ? System.Drawing.Color.White : UiTheme.Faint;
                }
                // Accordion: only the active section shows its children.
                it.Expanded = on && it.Children.Count > 0;
                if (it.Caret != null) it.Caret.Text = it.Children.Count == 0 ? "" : it.Expanded ? "▾" : "▸";
            }
            ReflowRail();
            if (RailChildren.ContainsKey(name))
            {
                string child;
                if (!_activeChild.TryGetValue(name, out child)) child = RailChildren[name][0];
                SelectChild(name, child);
            }
            if (name == "Loadouts") _loadoutsPanel?.RefreshPreviews();
        }

        private void SelectChild(string section, string child)
        {
            _activeChild[section] = child;
            Action go;
            if (_childNav.TryGetValue($"{section}/{child}", out go))
            {
                try { go(); } catch (Exception ex) { LogDebug($"Child nav {section}/{child}: {ex.Message}"); }
            }
            foreach (var it in _railItems)
            {
                if (it.Name != section) continue;
                foreach (var c in it.Children)
                {
                    bool on = c.Name == child;
                    c.Row.BackColor = on ? UiTheme.AccentWeak : UiTheme.Ground;
                    c.Lbl.BackColor = c.Row.BackColor;
                    c.Lbl.ForeColor = on ? UiTheme.AccentDark : UiTheme.Muted;
                    c.Dot.BackColor = on ? UiTheme.AccentDark : UiTheme.Faint;
                }
            }
        }

        // Accordion stacking: parents 32px apart, expanded children 25px, everything repositions.
        private void ReflowRail()
        {
            int y = UiTheme.S(56);
            foreach (var it in _railItems)
            {
                it.Row.Top = y;
                y += UiTheme.S(32);
                foreach (var c in it.Children)
                {
                    c.Row.Visible = it.Expanded;
                    if (it.Expanded)
                    {
                        c.Row.Top = y;
                        y += UiTheme.S(25);
                    }
                }
                if (it.Expanded) y += UiTheme.S(4);
            }
        }

        private void BuildRail()
        {
            _rail.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 1, BackColor = UiTheme.Border });

            _railMaster = new Button
            {
                Bounds = new System.Drawing.Rectangle(UiTheme.S(10), UiTheme.S(10), RailW - UiTheme.S(21), UiTheme.S(30)),
                Font = UiTheme.Chip,
                FlatStyle = FlatStyle.Flat
            };
            _railMaster.FlatAppearance.BorderColor = UiTheme.Border;
            _railMaster.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.GlobalEnabled = !Settings.GlobalEnabled;
                TickRail();
            };
            _rail.Controls.Add(_railMaster);

            foreach (var name in new[] { "Advisors", "Profile", "Combat", "Economy", "Systems", "Loadouts", "Logs", "Settings", "Cards" })
            {
                var it = new RailItem { Name = name };
                it.Row = new Panel { Bounds = new System.Drawing.Rectangle(UiTheme.S(4), UiTheme.S(56), RailW - UiTheme.S(10), UiTheme.S(30)), BackColor = UiTheme.Ground, Cursor = Cursors.Hand };
                it.Dot = new Panel { Bounds = new System.Drawing.Rectangle(UiTheme.S(12), UiTheme.S(11), UiTheme.S(8), UiTheme.S(8)), BackColor = UiTheme.Faint };
                it.Lbl = new Label { Text = name.ToUpperInvariant(), AutoSize = true, Font = UiTheme.Bold, ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground, Location = new System.Drawing.Point(UiTheme.S(28), UiTheme.S(4)) };
                it.Caret = new Label { Text = RailChildren.ContainsKey(name) ? "▸" : "", AutoSize = false, Size = new System.Drawing.Size(UiTheme.S(20), UiTheme.S(20)), Font = UiTheme.Chip, ForeColor = UiTheme.Faint, BackColor = UiTheme.Ground, Location = new System.Drawing.Point(RailW - UiTheme.S(30), UiTheme.S(6)) };
                string captured = name;
                EventHandler go = (s, e) => SelectSectionM1(captured);
                it.Row.Click += go; it.Dot.Click += go; it.Lbl.Click += go; it.Caret.Click += go;
                it.Row.Controls.Add(it.Dot);
                it.Row.Controls.Add(it.Lbl);
                it.Row.Controls.Add(it.Caret);
                _rail.Controls.Add(it.Row);

                string[] kids;
                if (RailChildren.TryGetValue(name, out kids))
                {
                    foreach (var kid in kids)
                    {
                        var c = new RailChild { Name = kid };
                        c.Row = new Panel { Bounds = new System.Drawing.Rectangle(UiTheme.S(4), UiTheme.S(56), RailW - UiTheme.S(10), UiTheme.S(23)), BackColor = UiTheme.Ground, Cursor = Cursors.Hand, Visible = false };
                        c.Dot = new Panel { Bounds = new System.Drawing.Rectangle(UiTheme.S(24), UiTheme.S(9), UiTheme.S(6), UiTheme.S(6)), BackColor = UiTheme.Faint };
                        c.Lbl = new Label { Text = kid.ToUpperInvariant(), AutoSize = true, Font = UiTheme.Chip, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new System.Drawing.Point(UiTheme.S(38), UiTheme.S(4)) };
                        string cs = name, cc = kid;
                        EventHandler cgo = (s, e) => SelectChild(cs, cc);
                        c.Row.Click += cgo; c.Dot.Click += cgo; c.Lbl.Click += cgo;
                        c.Row.Controls.Add(c.Dot);
                        c.Row.Controls.Add(c.Lbl);
                        _rail.Controls.Add(c.Row);
                        it.Children.Add(c);
                        // Deep links ("Systems/Quests") work from anywhere.
                        _sectionNav[$"{name}/{kid}"] = () => { SelectSectionM1(cs); SelectChild(cs, cc); };
                    }
                }
                _railItems.Add(it);
            }
            ReflowRail();

            var foot = new Label
            {
                Dock = DockStyle.Bottom,
                Height = UiTheme.S(60),
                Font = UiTheme.Chip,
                ForeColor = UiTheme.Faint,
                BackColor = UiTheme.Ground,
                Text = $"F2 PAUSE · F9 EDITOR\nv{Main.Version} · {Main.BuildTag}",
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter
            };
            _rail.Controls.Add(foot);
            TickRail();
        }

        // Sub-pages inside a section canvas (Advisors/Systems): Dock.Fill panels visible-toggled
        // by the rail children — the same proven pattern as the section canvases themselves.
        private Panel NewSubPage(Panel section, string key)
        {
            var p = new ScrollPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Ground, Visible = false };
            Host(section).Controls.Add(p);
            _subPages[key] = p;
            return p;
        }

        private Action ShowSubPage(string section, string key) => () =>
        {
            foreach (var kv in _subPages)
                if (kv.Key.StartsWith(section + "/", StringComparison.Ordinal))
                    kv.Value.Visible = kv.Key == key;
        };

        // Master button reflects the global kill-switch (F1 flips it too — same setting).
        private void TickRail()
        {
            try
            {
                if (_railMaster == null) return;
                bool on = Settings?.GlobalEnabled ?? false;
                _railMaster.Text = on ? "ADVISOR ACTIVE" : "ADVISOR PAUSED";
                UiTheme.ApplyState(_railMaster, on ? UiTheme.Cap : UiTheme.Danger, System.Drawing.Color.White);
            }
            catch { }
        }

        // Per-section health dot = worst of that section's lights (red > amber > green > grey).
        private static readonly System.Collections.Generic.Dictionary<string, int[]> RailLightMap
            = new System.Collections.Generic.Dictionary<string, int[]>
            {
                { "Advisors", new[] { 2, 4 } },
                { "Combat", new[] { 6, 11 } },
                { "Economy", new[] { 0, 1, 10 } },
                { "Systems", new[] { 3, 7, 8, 9 } },
                { "Loadouts", new[] { 5 } },
            };

        private void UpdateRailDots()
        {
            if (_lights == null) return;
            foreach (var it in _railItems)
            {
                if (!RailLightMap.TryGetValue(it.Name, out var idx)) { it.Dot.BackColor = UiTheme.Faint; continue; }
                int rank = 0;
                foreach (var i in idx)
                {
                    var c = _lights.DotColors[i];
                    int r = c == UiTheme.Danger ? 3 : c == UiTheme.Energy ? 2 : c == UiTheme.Cap ? 1 : 0;
                    if (r > rank) rank = r;
                }
                it.Dot.BackColor = rank == 3 ? UiTheme.Danger : rank == 2 ? UiTheme.Energy : rank == 1 ? UiTheme.Cap : UiTheme.Faint;
            }
        }

        private void BuildCardsSection(Panel sec, System.Collections.Generic.Dictionary<string, TabPage> pages)
        {
            var containers = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Panel>>();
            foreach (var src in new[] { "Cards", "Wishes" })
            {
                if (!pages.TryGetValue(src, out var old)) continue;
                var box = new Panel { Bounds = new System.Drawing.Rectangle(UiTheme.S(20), UiTheme.S(12), CanvasW, UiTheme.S(726)), BackColor = UiTheme.Ground, Visible = false };
                var kids = new Control[old.Controls.Count];
                old.Controls.CopyTo(kids, 0);
                foreach (var k in kids) { old.Controls.Remove(k); box.Controls.Add(k); }
                Host(sec).Controls.Add(box);
                containers.Add(new System.Collections.Generic.KeyValuePair<string, Panel>(src, box));
                tabControl1.TabPages.Remove(old);
                _cardsLegacy.Add(box);
            }
            if (containers.Count == 0) return;

            // A1: the CARDS/WISHES switch lives in the rail children — register the toggles.
            for (int i = 0; i < containers.Count; i++)
            {
                int idx = i;
                _childNav[$"Cards/{containers[i].Key}"] = () =>
                {
                    for (int j = 0; j < containers.Count; j++)
                        containers[j].Value.Visible = j == idx;
                };
            }
            containers[0].Value.Visible = true;
        }

        // Sub-tab host (v2 after user feedback — stacked panels created double scrollbars): a segmented
        // switcher bar on top, ONE section visible at a time, each legacy panel kept Dock.Fill so it
        // renders at its native designed size exactly as it did on its own tab. Flat Buttons + color
        // swap = all Mono-proven patterns.
        private TabPage BuildHostTab(string title, System.Collections.Generic.Dictionary<string, TabPage> pages, params string[] sources)
            => BuildHostTab(title, pages, sources, null);

        private TabPage BuildHostTab(string title, System.Collections.Generic.Dictionary<string, TabPage> pages,
            string[] sources, System.Collections.Generic.KeyValuePair<string, Panel>[] extraSections, bool extrasFirst = false)
        {
            var sections = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Panel>>();

            if (extrasFirst && extraSections != null)
                foreach (var extra in extraSections)
                    if (extra.Value != null)
                    {
                        extra.Value.Visible = false;
                        sections.Add(extra);
                    }

            foreach (var src in sources)
            {
                if (!pages.TryGetValue(src, out var old)) continue;

                var container = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Ground, Visible = false };
                var kids = new Control[old.Controls.Count];
                old.Controls.CopyTo(kids, 0);
                foreach (var k in kids)
                {
                    old.Controls.Remove(k);
                    container.Controls.Add(k);   // keeps its original Dock.Fill — native layout intact
                }
                sections.Add(new System.Collections.Generic.KeyValuePair<string, Panel>(src, container));
                tabControl1.TabPages.Remove(old);
            }

            if (!extrasFirst && extraSections != null)
                foreach (var extra in extraSections)
                    if (extra.Value != null)
                    {
                        extra.Value.Visible = false;
                        sections.Add(extra);
                    }

            if (sections.Count == 0) return null;

            var page = new TabPage(title) { BackColor = UiTheme.Ground };
            var body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Ground };
            foreach (var s in sections)
                body.Controls.Add(s.Value);

            _sectionNav[title] = () => tabControl1.SelectedTab = page;

            if (sections.Count == 1)
            {
                sections[0].Value.Visible = true;
                page.Controls.Add(body);
                return page;
            }

            var bar = new Panel { Dock = DockStyle.Top, Height = UiTheme.S(36), BackColor = UiTheme.Ground };
            var buttons = new System.Collections.Generic.List<Button>();
            int bx = UiTheme.S(10);
            int rowY = UiTheme.S(6);
            // MEASURE the label, never estimate (the "ADVENTURE" truncation: uppercase Segoe UI runs
            // ~9px/char, an estimate of 8 clipped the final letter). Same proven Graphics path as FitText.
            using (var g = CreateGraphics())
            {
                for (int i = 0; i < sections.Count; i++)
                {
                    int idx = i;
                    string label = sections[i].Key.ToUpperInvariant();
                    int textW = (int)Math.Ceiling(g.MeasureString(label, UiTheme.Ui).Width);
                    int w = Math.Max(UiTheme.S(88), textW + UiTheme.S(26));
                    // Wrap to a second row rather than run off the window (Advanced hosts 8 sections).
                    if (bx + w > UiTheme.S(640) && bx > UiTheme.S(10))
                    {
                        bx = UiTheme.S(10);
                        rowY += UiTheme.S(31);
                    }
                    var b = new Button
                    {
                        Text = label,
                        Location = new System.Drawing.Point(bx, rowY),
                        Size = new System.Drawing.Size(w, UiTheme.S(25)),
                        Font = UiTheme.Ui,
                        FlatStyle = FlatStyle.Flat
                    };
                    b.FlatAppearance.BorderColor = UiTheme.Border;
                    b.Click += (s, e) => SelectSection(sections, buttons, idx);
                    _sectionNav[$"{title}/{sections[i].Key}"] = () =>
                    {
                        tabControl1.SelectedTab = page;
                        SelectSection(sections, buttons, idx);
                    };
                    bar.Controls.Add(b);
                    buttons.Add(b);
                    bx += b.Width + UiTheme.S(6);
                }
            }
            bar.Height = rowY + UiTheme.S(30);
            bar.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border });

            page.Controls.Add(bar);
            page.Controls.Add(body);
            body.BringToFront();   // bar docks Top, body fills the remainder
            SelectSection(sections, buttons, 0);
            return page;
        }

        // STATUS-board click-through: "Tab/Section" exact, then "Tab" registered host, then any
        // top-level tab whose title matches the first path segment (Dashboard, Loadouts...).
        public void NavigateTo(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (_sectionNav.TryGetValue(path, out var go)) { go(); return; }
                string title = path.Split('/')[0];
                if (_sectionNav.TryGetValue(title, out var goTab)) { goTab(); return; }
                foreach (TabPage p in tabControl1.TabPages)
                    if (string.Equals(p.Text, title, StringComparison.OrdinalIgnoreCase))
                    {
                        tabControl1.SelectedTab = p;
                        return;
                    }
            }
            catch (Exception e) { LogDebug($"NavigateTo({path}): {e.Message}"); }
        }

        private static void SelectSection(
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Panel>> sections,
            System.Collections.Generic.List<Button> buttons, int idx)
        {
            for (int i = 0; i < sections.Count; i++)
            {
                sections[i].Value.Visible = i == idx;
                UiTheme.ApplyState(buttons[i],
                    i == idx ? UiTheme.Accent : UiTheme.BtnFace,
                    i == idx ? System.Drawing.Color.White : UiTheme.Ink);
            }
        }

        private T GetElement<T>(string name) => this.GetFieldValue<SettingsForm, T>(name);

        private void AlignWidth(Control target, Control source)
        {
            target.Width = source.Width + source.Margin.Left + source.Margin.Right;
            target.Width -= target.Margin.Left + target.Margin.Right;
        }

        private void AlignHeight(Control target, Control source)
        {
            target.Height = source.Height + source.Margin.Top + source.Margin.Bottom;
            target.Height -= target.Margin.Top + target.Margin.Bottom;
        }

        private void AdjustDimensions()
        {
            // Adjust separator height in case it has changed due to rescaling
            Separator1.Height = 1;
            Separator2.Height = 1;
            Separator3.Height = 1;
            Separator4.Height = 1;

            Graphics g = CreateGraphics();
            var scale = g.DpiY / 96f;
            g.Dispose();

            tabControl1.ItemSize = new Size(tabControl1.ItemSize.Width, (int)(tabControl1.ItemSize.Height * scale));

            // General Tab
            UnloadButton.Height = OpenSettingsFolder.Height;

            // Allocation Tab
            AlignWidth(SpaghettiCap, AutoSpellSwap);
            AlignWidth(CounterfeitCap, AutoSpellSwap);
            AlignWidth(BloodNumberThreshold, AutoSpellSwap);
            BloodNumberThreshold.Width -= BloodNumberThreshold.Height;
            AlignWidth(GuffAThreshold, IronPillThreshold);
            AlignWidth(GuffBThreshold, IronPillThreshold);

            // Yggdrasil Tab
            YggAddButton.Size = YggRemoveButton.Size;

            // Inventory Tab
            AlignWidth(CubePriority, ManageBoostConvert);
            AlignWidth(FavoredMacguffin, ManageBoostConvert);
            PriorityBoostAdd.Size = PriorityBoostRemove.Size;
            BlacklistAdd.Size = BlacklistRemove.Size;

            // Titans Tab
            TitanAdd.Size = TitanRemove.Size;

            var height = Titan6Version.Height;
            label64.Height = height;
            label65.Height = height;
            Titan1Placeholder.Height = height;
            Titan2Placeholder.Height = height;
            Titan3Placeholder.Height = height;
            Titan4Placeholder.Height = height;
            Titan5Placeholder.Height = height;
            Titan13Placeholder.Height = height;
            Titan14Placeholder.Height = height;

            if (tableLayoutPanel15.Height < (height + 1) * 14)
                tableLayoutPanel22.ColumnStyles[2].Width = SystemInformation.VerticalScrollBarWidth - 1;
            else
                tableLayoutPanel22.ColumnCount = 2;

            // Adventure Tab
            BlacklistAddEnemyButton.Size = BlacklistRemoveEnemyButton.Size;

            // Gold Tab
            AlignHeight(label10, ManageGold);
            GoldLoadoutAdd.Size = GoldLoadoutRemove.Size;

            // Wishes Tab
            AddWishButton.Size = RemoveWishButton.Size;

            // Pit Tab
            AlignWidth(MoneyPitThreshold, AutoMoneyPit);
            ShockwaveAdd.Size = ShockwaveRemove.Size;

            // Cards Tab
            CardSortAdd.Size = CardSortRemove.Size;
            label1.Height = CardRarity1.Height;

            if (tableLayoutPanel13.Height < (CardRarity1.Height + 1) * 14)
                tableLayoutPanel14.ColumnStyles[3].Width = SystemInformation.VerticalScrollBarWidth - 1;
            else
                tableLayoutPanel14.ColumnCount = 3;

            // Cooking Tab
            CookingAddButton.Size = CookingRemoveButton.Size;
        }

        // Plain list item: ToString is the display text — NO DataSource binding. Mono's binding path
        // (set_DataSource -> OnDataSourceChanged -> set_SelectedIndex -> GetItemRectangle) kept
        // throwing on rebinds even after state resets; it is banned for these lists.
        public sealed class ListEntry
        {
            public readonly int Key;
            private readonly string _text;
            public ListEntry(int key, string text) { Key = key; _text = text ?? key.ToString(); }
            public override string ToString() => _text;
        }

        public static void UpdateItemList(ListBox itemList, int[] newList, Func<int, string> getDisplayName)
        {
            try
            {
                itemList.BeginUpdate();
                try
                {
                    if (itemList.DataSource != null) itemList.DataSource = null;
                    itemList.SelectedIndex = -1;
                    itemList.Items.Clear();
                    if (newList != null)
                        foreach (var id in newList)
                        {
                            string name;
                            try { name = getDisplayName(id); } catch { name = $"#{id}"; }
                            itemList.Items.Add(new ListEntry(id, name));
                        }
                }
                finally { itemList.EndUpdate(); }
            }
            catch (Exception e) { Main.LogDebug($"UpdateItemList: {e.Message}"); }
        }

        public void SetTitanGoldBox(SavedSettings newSettings)
        {
            TitanGoldTargets.Items.Clear();
            for (var i = 0; i < ZoneHelpers.TitanZones.Length; i++)
            {
                var zone = ZoneHelpers.TitanZones[i];
                var text = $"{titanZoneList[zone]}";
                if (newSettings.TitanGoldTargets[i])
                    text = $"{text} ({(newSettings.TitanMoneyDone[i] ? "Done" : "Waiting")})";
                var item = new ListViewItem
                {
                    Tag = i,
                    Checked = newSettings.TitanGoldTargets[i],
                    Text = text,
                    BackColor = newSettings.TitanGoldTargets[i]
                        ? newSettings.TitanMoneyDone[i] ? Color.LightGreen : Color.Yellow
                        : Color.White
                };
                TitanGoldTargets.Items.Add(item);
            }

            TitanGoldTargets.Columns[0].Width = -1;
        }

        private void SetSnipeZone(ComboBox control, int setting)
        {
            if (zoneList.ContainsKey(setting))
                control.SelectedItem = new KeyValuePair<int, string>(setting, zoneList[setting]);
        }

        private void SetMoneyPitThreshold(ComboBox control, SavedSettings newSettings)
        {
            if (newSettings.MoneyPitThreshold == MoneyPitManager.moneyPitThresholds[MoneyPitThreshold.SelectedIndex])
                return;
            var i = MoneyPitManager.moneyPitThresholds.BinarySearch(newSettings.MoneyPitThreshold);
            if (i < 0)
                i = -i - 2;
            if (i < 0)
                i = 0;
            control.SelectedIndex = i;
        }

        private string FormatDoubleNumber(double number)
        {
            if (number == 0.0)
                return "";

            if (number >= 1e6)
                return number.ToString("#.###E+0");

            return number.ToString("");
        }

        public void UpdateFromSettings(SavedSettings newSettings)
        {
            _initializing = true;

            try
            {
                // Keep the strangler Settings tab in step with whatever changed the settings.
                try { _basicSettings?.Sync(); } catch { }

                // General Tab
                MasterEnable.Checked = newSettings.GlobalEnabled;
                DisableOverlay.Checked = newSettings.DisableOverlay;
                MoneyPitRunMode.Checked = newSettings.MoneyPitRunMode;
                AutoFightBosses.Enabled = !newSettings.MoneyPitRunMode;
                AutoFightBosses.Checked = newSettings.AutoFight;
                AutoBuyAdv.Checked = newSettings.AutoBuyAdventure;
                AutoBuyConsumables.Checked = newSettings.AutoBuyConsumables;
                ConsumeIfRunning.Checked = newSettings.ConsumeIfAlreadyRunning;
                Autosave.Checked = newSettings.Autosave;

                // Allocation Tab
                ManageEnergy.Checked = newSettings.ManageEnergy;
                ManageMagic.Checked = newSettings.ManageMagic;
                ManageR3.Checked = newSettings.ManageR3;
                ManageWandoos.Checked = newSettings.ManageWandoos;
                ManageNGUDiff.Checked = newSettings.ManageNGUDiff;
                ManageBeards.Checked = newSettings.ManageBeards;
                ManageDiggers.Checked = newSettings.ManageDiggers;
                UpgradeDiggers.Checked = newSettings.UpgradeDiggers;
                DiggerCap.Text = $"{newSettings.DiggerCap:F2}";
                ManageGear.Checked = newSettings.ManageGear;
                ManageConsumables.Checked = newSettings.ManageConsumables;
                AutoRebirth.Checked = newSettings.AutoRebirth;

                AutoSpellSwap.Checked = newSettings.AutoSpellSwap;
                SpaghettiCap.Value = newSettings.SpaghettiThreshold;
                CounterfeitCap.Value = newSettings.CounterfeitThreshold;
                BloodNumberThreshold.Text = FormatDoubleNumber(newSettings.BloodNumberThreshold);
                CastBloodSpells.Checked = newSettings.CastBloodSpells;
                IronPillThreshold.Value = Convert.ToDecimal(newSettings.IronPillThreshold);
                GuffAThreshold.Value = newSettings.BloodMacGuffinAThreshold;
                GuffBThreshold.Value = newSettings.BloodMacGuffinBThreshold;
                IronPillOnRebirth.Checked = newSettings.IronPillOnRebirth;
                GuffAOnRebirth.Checked = newSettings.BloodMacGuffinAOnRebirth;
                GuffBOnRebirth.Checked = newSettings.BloodMacGuffinBOnRebirth;

                // Yggdrasil Tab
                ManageYggdrasil.Checked = newSettings.ManageYggdrasil;
                ActivateFruits.Checked = newSettings.ActivateFruits;
                YggSwapThreshold.Value = newSettings.YggSwapThreshold;
                YggdrasilSwap.Checked = newSettings.SwapYggdrasilLoadouts;
                SwapYggdrasilDiggers.Checked = newSettings.SwapYggdrasilDiggers;
                SwapYggdrasilBeards.Checked = newSettings.SwapYggdrasilBeards;
                _yggControls.UpdateList(newSettings.YggdrasilLoadout);

                // Inventory Tab
                ManageInventory.Checked = newSettings.ManageInventory;
                ManageBoostConvert.Checked = newSettings.AutoConvertBoosts;
                CubePriority.SelectedIndex = newSettings.CubePriority;
                FavoredMacguffin.SelectedIndex = InventoryManager.macguffinList.Keys.ToList().IndexOf(newSettings.FavoredMacguffin);

                BoostPriorityList.Items.Clear();
                foreach (string priority in newSettings.BoostPriority)
                    BoostPriorityList.Items.Add(priority);

                if (BoostPriorityList.Items.Count != 3)
                    BoostPriorityList.Items.AddRange(new string[] { "Power", "Toughness", "Special" });

                _priorityControls.UpdateList(newSettings.PriorityBoosts);
                _blacklistControls.UpdateList(newSettings.BoostBlacklist);

                // Titans Tab
                ManageTitans.Checked = newSettings.ManageTitans;
                SwapTitanLoadout.Checked = newSettings.SwapTitanLoadouts;
                SwapTitanDiggers.Checked = newSettings.SwapTitanDiggers;
                SwapTitanBeards.Checked = newSettings.SwapTitanBeards;
                _titanControls.UpdateList(newSettings.TitanLoadout);

                for (int i = 0; i <= 13; i++)
                    _killTitan[i].Checked = newSettings.TitanSwapTargets[i];

                TitanCombatMode.SelectedIndex = newSettings.TitanCombatMode;
                TitanBeastMode.Checked = newSettings.TitanBeastMode;

                // Adventure Tab
                CombatActive.Checked = newSettings.CombatEnabled;
                CombatMode.SelectedIndex = newSettings.CombatMode;
                SetSnipeZone(CombatTargetZone, newSettings.SnipeZone);
                BeastMode.Checked = newSettings.BeastMode;
                BossesOnly.Checked = newSettings.SnipeBossOnly;
                AllowFallthrough.Checked = newSettings.AllowZoneFallback;

                TargetITOPOD.Checked = newSettings.AdventureTargetITOPOD;
                // This RETIRED grid's combo carries only 2 modes (Idle, Snipe) while AdventurePanel's
                // offers 4 — so an unguarded assignment threw ArgumentOutOfRange for Defensive/Offensive
                // and aborted the WHOLE deferred UpdateFromSettings, silently stopping every panel after
                // this line from syncing (seen in debug.log as "Deferred form update failed:
                // ... Parameter name: SelectedIndex"). Clamp instead of widening the validator's range
                // back down: the live panel is the real editor, and this page is hidden.
                ITOPODCombatMode.SelectedIndex = Math.Min(newSettings.ITOPODCombatMode, ITOPODCombatMode.Items.Count - 1);
                ITOPODOptimizeMode.SelectedIndex = newSettings.ITOPODOptimizeMode;
                ITOPODBeastMode.Checked = newSettings.ITOPODBeastMode;
                ITOPODAutoPush.Checked = newSettings.ITOPODAutoPush;

                UpdateItemList(BlacklistedBosses, newSettings.BlacklistedBosses, x => spriteEnemyList[x]);

                // Gold Tab
                ManageGold.Enabled = !newSettings.MoneyPitRunMode;
                ManageGold.Checked = newSettings.ManageGoldLoadouts;
                ResnipeInput.Value = newSettings.ResnipeTime;
                CBlockMode.Enabled = !newSettings.MoneyPitRunMode;
                CBlockMode.Checked = newSettings.GoldCBlockMode;
                _goldControls.UpdateList(newSettings.GoldDropLoadout);
                SetTitanGoldBox(newSettings);

                // Quests Tab
                ManageQuests.Checked = newSettings.AutoQuest;
                AllowMajor.Checked = newSettings.AllowMajorQuests;
                ButterMajors.Checked = newSettings.UseButterMajor;
                QuestsFullBank.Checked = newSettings.QuestsFullBank;
                ManualMinor.Checked = newSettings.ManualMinors;
                ButterMinors.Checked = newSettings.UseButterMinor;
                FiftyItemMinors.Checked = newSettings.FiftyItemMinors;
                AbandonMinors.Checked = newSettings.AbandonMinors;
                AbandonMinorThreshold.Value = newSettings.MinorAbandonThreshold;
                ManageQuestLoadout.Checked = newSettings.ManageQuestLoadouts;
                _questControls.UpdateList(newSettings.QuestLoadout);
                QuestCombatMode.SelectedIndex = newSettings.QuestCombatMode;
                QuestBeastMode.Checked = newSettings.QuestBeastMode;

                // Wishes Tab
                ManageWishes.Checked = newSettings.ManageWishes;
                WishLimit.Value = newSettings.WishLimit;
                WishEnergy.Value = Convert.ToDecimal(newSettings.WishEnergy);
                WishMagic.Value = Convert.ToDecimal(newSettings.WishMagic);
                WishR3.Value = Convert.ToDecimal(newSettings.WishR3);
                WishMode.SelectedIndex = newSettings.WishMode;
                WeakPriorities.Checked = newSettings.WeakPriorities;
                _wishControls.UpdateList(newSettings.WishPriorities);
                _wishBlacklistControls.UpdateList(newSettings.WishBlacklist);

                // Pit Tab
                AutoDailySpin.Checked = newSettings.AutoSpin;
                AutoMoneyPit.Enabled = !newSettings.MoneyPitRunMode;
                AutoMoneyPit.Checked = newSettings.AutoMoneyPit;
                SwapPitDiggers.Checked = newSettings.SwapPitDiggers;
                PredictMoneyPit.Enabled = !newSettings.MoneyPitRunMode;
                PredictMoneyPit.Checked = newSettings.PredictMoneyPit;
                MoneyPitDaycare.Checked = newSettings.MoneyPitDaycare;
                SetMoneyPitThreshold(MoneyPitThreshold, newSettings);
                DaycareThreshold.Value = newSettings.DaycareThreshold;
                _shockwaveControls.UpdateList(newSettings.Shockwave);

                // Cards Tab
                BalanceMayo.Checked = newSettings.ManageMayo;
                AutoCastCards.Checked = newSettings.AutoCastCards;
                CastProtectedCards.Checked = newSettings.CastProtectedCards;
                TrashCards.Checked = newSettings.TrashCards;
                TrashProtectedCards.Checked = newSettings.TrashProtectedCards;
                SortCards.Checked = newSettings.CardSortEnabled;

                if (newSettings.CardSortOrder.Length > 0)
                {
                    CardSortList.DataSource = null;
                    CardSortList.DataSource = new BindingSource(newSettings.CardSortOrder, null);
                }
                else
                {
                    CardSortList.Items.Clear();
                }

                for (int i = 0; i <= 13; i++)
                {
                    _cardRarity[i].SelectedIndex = CardManager.rarityList.Keys.ToList().IndexOf(newSettings.CardRarities[i]);
                    _cardCost[i].SelectedIndex = Array.IndexOf(CardManager.costList, newSettings.CardCosts[i]);
                }

                // Cooking Tab
                ManageCooking.Checked = newSettings.ManageCooking;
                ManageCookingLoadout.Checked = newSettings.ManageCookingLoadouts;
                _cookingControls.UpdateList(newSettings.CookingLoadout);

                _loadoutsPanel?.SyncFromSettings();
                _boostsPanel?.SyncFromSettings();
                _adventurePanel?.SyncFromSettings();
                _titansPanel?.SyncFromSettings();
                _goldPanel?.SyncFromSettings();
                _pitPanel?.SyncFromSettings();
                _spendPanel?.SyncFromSettings();
                _apPanel?.SyncFromSettings();
                _ppPanel?.SyncFromSettings();
                _atPanel?.SyncFromSettings();
                _challengesPanel?.SyncFromSettings();
                _actions?.SyncFromSettings();
                _yggPanel?.SyncFromSettings();
                _questsPanel?.SyncFromSettings();
                _bloodPanel?.SyncFromSettings();
                _autopilotPanel?.SyncFromSettings();
                _systemIndex?.Sync();   // the index reads the same authoritative settings as its owner panel

                Refresh();
            }
            finally
            {
                _initializing = false;
            }
        }

        private bool TryGetValueFromNumericUpDown(NumericUpDown upDown, out int val)
        {
            try
            {
                val = (int)upDown.Value;
                return true;
            }
            catch
            {
                val = 0;
                return false;
            }
        }

        private bool TryGetTextFromNumericUpDown(NumericUpDown upDown, out int val) => int.TryParse(upDown.Text, out val);

        private bool TryItemBoxTextChanged(ItemControlGroup controls, out int val)
        {
            controls.ClearError();

            if (!TryGetTextFromNumericUpDown(controls.ItemBox, out val) || val < controls.MinVal || val > controls.MaxVal)
            {
                controls.ItemLabel.Text = "";
                return false;
            }

            var itemName = controls.GetDisplayName(val).Replace("<b><color=blue>[QUEST ITEM]</color></b>", "[QUEST ITEM]");
            bool isValid = true;

            if (controls.CheckIsEquipment)
            {
                isValid = (int)_character.itemInfo.type[val] <= 5;
                if (!isValid)
                    itemName += " (UNEQUIPPABLE)";
            }
            controls.ItemLabel.Text = itemName;

            return isValid;
        }

        private void ItemBoxKeyDown(KeyEventArgs e, ItemControlGroup controls)
        {
            if (e.KeyCode == Keys.Enter)
                ItemListAdd(controls);
        }

        private void ItemListAdd(ItemControlGroup controls)
        {
            controls.ClearError();

            if (!TryItemBoxTextChanged(controls, out int val))
            {
                controls.SetError("Invalid item id");
                return;
            }

            var settings = controls.GetSettings();
            if (settings.Contains(val))
                return;

            var index = settings.Length;

            Array.Resize(ref settings, index + 1);
            settings[index] = val;
            controls.SaveSettings(settings);

            // Form-wide refresh is deferred — rebuild THIS list immediately so the add feels instant.
            controls.UpdateList(settings);
            if (index < controls.ItemList.Items.Count)
                controls.ItemList.SelectedIndex = index;
        }

        private void ItemListRemove(ItemControlGroup controls)
        {
            controls.ClearError();

            var item = controls.ItemList.SelectedItem;
            if (item == null)
                return;

            var index = controls.ItemList.SelectedIndex;

            int id = item is ListEntry le ? le.Key : ((KeyValuePair<int, string>)item).Key;

            var settings = controls.GetSettings();
            settings = settings.Where(x => x != id).ToArray();
            controls.SaveSettings(settings);

            // Form-wide refresh is deferred — rebuild THIS list immediately; clamp the selection
            // against the ACTUAL item count (a bad index is what Mono throws on).
            controls.UpdateList(settings);
            int count = controls.ItemList.Items.Count;
            int want = settings.Length > index ? index : settings.Length - 1;
            if (want >= 0 && want < count)
                controls.ItemList.SelectedIndex = want;
        }

        private void ItemListUp(ItemControlGroup controls)
        {
            controls.ClearError();

            ItemListMove(controls.ItemList, controls.GetSettings(), Direction.Up);
        }

        private void ItemListDown(ItemControlGroup controls)
        {
            controls.ClearError();

            ItemListMove(controls.ItemList, controls.GetSettings(), Direction.Down);
        }

        private void ItemListMove<T>(ListBox itemList, T[] settings, Direction direction)
        {
            var index = itemList.SelectedIndex;
            if (index == -1)
                return;

            var newIndex = index - (int)direction;
            if (newIndex < 0 || newIndex >= settings.Length)
                return;

            (settings[newIndex], settings[index]) = (settings[index], settings[newIndex]);
            Settings.SaveSettings();

            itemList.SelectedIndex = newIndex;
        }

        public void UpdateProfileList(string[] profileList, string selectedProfile)
        {
            // The PROFILE page (UI4) is the live profile-list UI and re-scans the folder itself on this
            // AllocationWatcher-driven refresh (main thread — from Update's flag drain). Signature kept for
            // its caller (Main.LoadAllocationProfiles); the args are advisory now (ProfilePanel reads disk).
            _profilePanel?.OnFilesChanged();
        }

        public void UpdateProgressBar(int progress)
        {
            if (progress < 0)
                return;
            progressBar1.Value = progress;
        }

        // Refresh the docked live status strip + dashboard (called each frame from Main.Update; throttled).
        //
        // EVERY tick below only repaints UI, so the whole pump is gated on the window actually being on
        // screen. Closing the window HIDES it (SettingsForm_FormClosing cancels and calls Hide), which is
        // the normal state while playing — so before this gate the pump ran a full sub-tick chain, and
        // StatusPanel forced a synchronous Refresh() of an invisible control tree, four times a second,
        // for the entire session. Nothing here is a state machine: each tick recomputes from live game
        // state, so skipping frames while hidden loses nothing and the first frame after Show() repaints.
        // GrowthTracker.Tick is the one sampler that must run regardless — it moved to Main's 10s
        // AutomationRoutine (it self-throttles to 60s, so the cadence is unchanged).
        public void UpdateStatus()
        {
            if (!Visible || WindowState == FormWindowState.Minimized)
                return;

            // Ages out temporary outcomes with no user interaction (nothing here schedules — this tick
            // already exists). Cheap by construction: Sync returns after two comparisons when the painted
            // state is current, which is every frame except the one where an outcome arrives or expires.
            _ribbon?.Sync(DateTime.UtcNow);
            _statusPanel?.UpdateStatus();
            _titansPanel?.TickCountdown();
            _lights?.TickBoard();                    // 3s cadence while the Advisors canvas shows
            if (_lights != null && !_lights.Visible)
                _lights.TickBackground(10);          // slower background cadence feeds the rail dots
            _actions?.TickActions();
            TickRail();
            _logsPanel?.TickLogs();
            _combatSliver?.TickSliver();
            _pitSliver?.TickSliver();
            _growthPanel?.TickGrowth();
            _bloodPanel?.RefreshStatus();    // no-ops unless the Blood page is visible
        }

        // Switch the active allocation profile (used by the dashboard). Validates, persists, reloads.
        public void ApplyProfile(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            WarnIfProfileInvalid(name);
            Settings.AllocationFile = name;
            Main.RequestAllocationReload();   // main-thread rule: Update() performs the load
        }

        public void UpdateTitanVersions()
        {
            for (int i = 6; i <= 12; i++)
                _titanVersion[i - 6].SelectedIndex = ZoneHelpers.TitanVersion(i - 1) - 1;
        }

        private void MasterEnable_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.GlobalEnabled = MasterEnable.Checked;
        }

        private void AutoDailySpin_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AutoSpin = AutoDailySpin.Checked;
        }

        private void AutoMoneyPit_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AutoMoneyPit = AutoMoneyPit.Checked;
        }

        private void SwapPitDiggers_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.SwapPitDiggers = SwapPitDiggers.Checked;
        }

        private void PredictMoneyPit_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.PredictMoneyPit = PredictMoneyPit.Checked;
        }

        private void MoneyPitDaycare_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.MoneyPitDaycare = MoneyPitDaycare.Checked;
        }

        private void AutoFightBosses_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AutoFight = AutoFightBosses.Checked;
        }

        private void MoneyPitThreshold_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.MoneyPitThreshold = (double)MoneyPitThreshold.SelectedItem;
        }

        private void MoneyPitThreshold_Format(object sender, ListControlConvertEventArgs e)
        {
            e.Value = FormatDoubleNumber((double)e.ListItem);
        }

        private void DaycareThreshold_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            if (TryGetValueFromNumericUpDown(DaycareThreshold, out int val))
                Settings.DaycareThreshold = val;
        }

        private void ManageEnergy_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageEnergy = ManageEnergy.Checked;
        }

        private void ManageMagic_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageMagic = ManageMagic.Checked;
        }

        private void ManageGear_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageGear = ManageGear.Checked;
        }

        private void ManageBeards_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageBeards = ManageBeards.Checked;
        }

        private void ManageDiggers_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageDiggers = ManageDiggers.Checked;
        }

        private void ManageWandoos_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageWandoos = ManageWandoos.Checked;
        }

        private void AutoRebirth_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AutoRebirth = AutoRebirth.Checked;
        }

        private void ManageYggdrasil_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageYggdrasil = ManageYggdrasil.Checked;
        }

        private void YggdrasilSwap_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.SwapYggdrasilLoadouts = YggdrasilSwap.Checked;
        }

        private void YggdrasilSwapDiggers_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.SwapYggdrasilDiggers = SwapYggdrasilDiggers.Checked;
        }

        private void YggdrasilSwapBeards_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.SwapYggdrasilBeards = SwapYggdrasilBeards.Checked;
        }

        private void YggLoadoutItem_TextChanged(object sender, EventArgs e) => TryItemBoxTextChanged(_yggControls, out _);

        private void YggLoadoutItem_KeyDown(object sender, KeyEventArgs e) => ItemBoxKeyDown(e, _yggControls);

        private void YggAddButton_Click(object sender, EventArgs e) => ItemListAdd(_yggControls);

        private void YggRemoveButton_Click(object sender, EventArgs e) => ItemListRemove(_yggControls);

        private void ManageInventory_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageInventory = ManageInventory.Checked;
        }

        private void ManageBoostConvert_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AutoConvertBoosts = ManageBoostConvert.Checked;
        }

        private void BoostPrioUpButton_Click(object sender, EventArgs e) => ItemListMove(BoostPriorityList, Settings.BoostPriority, Direction.Up);

        private void BoostPrioDownButton_Click(object sender, EventArgs e) => ItemListMove(BoostPriorityList, Settings.BoostPriority, Direction.Down);

        private void PriorityBoostItemAdd_TextChanged(object sender, EventArgs e) => TryItemBoxTextChanged(_priorityControls, out _);

        private void PriorityBoostItemAdd_KeyDown(object sender, KeyEventArgs e) => ItemBoxKeyDown(e, _priorityControls);

        private void PriorityBoostAdd_Click(object sender, EventArgs e) => ItemListAdd(_priorityControls);

        private void PriorityBoostRemove_Click(object sender, EventArgs e) => ItemListRemove(_priorityControls);

        private void PrioUpButton_Click(object sender, EventArgs e) => ItemListUp(_priorityControls);

        private void PrioDownButton_Click(object sender, EventArgs e) => ItemListDown(_priorityControls);

        private void BlacklistAddItem_TextChanged(object sender, EventArgs e) => TryItemBoxTextChanged(_blacklistControls, out _);

        private void BlacklistAddItem_KeyDown(object sender, KeyEventArgs e) => ItemBoxKeyDown(e, _blacklistControls);

        private void BlacklistAdd_Click(object sender, EventArgs e) => ItemListAdd(_blacklistControls);

        private void BlacklistRemove_Click(object sender, EventArgs e) => ItemListRemove(_blacklistControls);

        private void ManageTitans_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageTitans = ManageTitans.Checked;
        }

        private void SwapTitanLoadout_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.SwapTitanLoadouts = SwapTitanLoadout.Checked;
        }

        private void SwapTitanDiggers_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.SwapTitanDiggers = SwapTitanDiggers.Checked;
        }

        private void SwapTitanBeards_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.SwapTitanBeards = SwapTitanBeards.Checked;
        }

        private void ManageQuestLoadout_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageQuestLoadouts = ManageQuestLoadout.Checked;
        }

        private void TitanAddItem_TextChanged(object sender, EventArgs e) => TryItemBoxTextChanged(_titanControls, out _);

        private void TitanAddItem_KeyDown(object sender, KeyEventArgs e) => ItemBoxKeyDown(e, _titanControls);

        private void TitanAdd_Click(object sender, EventArgs e) => ItemListAdd(_titanControls);

        private void TitanRemove_Click(object sender, EventArgs e) => ItemListRemove(_titanControls);

        private void CombatActive_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.CombatEnabled = CombatActive.Checked;
        }

        private void BossesOnly_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.SnipeBossOnly = BossesOnly.Checked;
        }

        private void CombatMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.CombatMode = CombatMode.SelectedIndex;
        }

        private void CombatTargetZone_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.SnipeZone = ((KeyValuePair<int, string>)CombatTargetZone.SelectedItem).Key;
        }

        private void AllowFallthrough_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AllowZoneFallback = AllowFallthrough.Checked;
        }

        private void GoldItemBox_TextChanged(object sender, EventArgs e) => TryItemBoxTextChanged(_goldControls, out _);

        private void GoldItemBox_KeyDown(object sender, KeyEventArgs e) => ItemBoxKeyDown(e, _goldControls);

        private void GoldLoadoutAdd_Click(object sender, EventArgs e) => ItemListAdd(_goldControls);

        private void GoldLoadoutRemove_Click(object sender, EventArgs e) => ItemListRemove(_goldControls);

        private void SettingsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Hide();
            e.Cancel = true;
        }

        private void ManageQuests_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AutoQuest = ManageQuests.Checked;
        }

        private void AllowMajor_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AllowMajorQuests = AllowMajor.Checked;
        }

        private void QuestsFullBank_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.QuestsFullBank = QuestsFullBank.Checked;
        }

        private void AbandonMinors_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AbandonMinors = AbandonMinors.Checked;
        }

        private void AbandonMinorThreshold_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            if (TryGetValueFromNumericUpDown(AbandonMinorThreshold, out int val))
                Settings.MinorAbandonThreshold = val;
        }

        private void QuestBeastMode_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.QuestBeastMode = QuestBeastMode.Checked;
        }

        private void QuestLoadoutBox_TextChanged(object sender, EventArgs e) => TryItemBoxTextChanged(_questControls, out _);

        private void QuestLoadoutItem_KeyDown(object sender, KeyEventArgs e) => ItemBoxKeyDown(e, _questControls);

        private void QuestAddButton_Click(object sender, EventArgs e) => ItemListAdd(_questControls);

        private void QuestRemoveButton_Click(object sender, EventArgs e) => ItemListRemove(_questControls);

        private void AutoSpellSwap_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AutoSpellSwap = AutoSpellSwap.Checked;
        }

        private void SpaghettiCap_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            if (TryGetValueFromNumericUpDown(SpaghettiCap, out int val))
                Settings.SpaghettiThreshold = val;
        }

        private void CounterfeitCap_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            if (TryGetValueFromNumericUpDown(CounterfeitCap, out int val))
                Settings.CounterfeitThreshold = val;
        }

        private void BloodNumberThreshold_TextChanged(object sender, EventArgs e)
        {
            numberErrProvider.SetError(BloodNumberThreshold, "");
        }

        private void UpdateBloodNumberThreshold()
        {
            double saved;
            if (BloodNumberThreshold.Text == "")
            {
                saved = 0.0;
            }
            else if (!double.TryParse(BloodNumberThreshold.Text, out saved))
            {
                numberErrProvider.SetError(BloodNumberThreshold, "Invalid format");
                return;
            }
            if (saved < 0.0)
                saved = 0.0;
            var divisor = saved >= 1E6 ? Math.Pow(10.0, (int)Math.Log10(saved) - 3) : 1.0;
            saved -= saved % divisor;
            if (Settings.BloodNumberThreshold == saved)
                BloodNumberThreshold.Text = FormatDoubleNumber(saved);
            Settings.BloodNumberThreshold = saved;
        }

        private void BloodNumberThreshold_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                UpdateBloodNumberThreshold();
        }

        private void BloodNumberThreshold_Leave(object sender, EventArgs e) => UpdateBloodNumberThreshold();

        private void IronPillThreshold_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            if (TryGetValueFromNumericUpDown(IronPillThreshold, out int val))
                Settings.IronPillThreshold = val;
        }

        private void GuffAThreshold_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            if (TryGetValueFromNumericUpDown(GuffAThreshold, out int val))
                Settings.BloodMacGuffinAThreshold = val;
        }

        private void GuffBThreshold_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            if (TryGetValueFromNumericUpDown(GuffBThreshold, out int val))
                Settings.BloodMacGuffinBThreshold = val;
        }

        private void IdleMinor_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManualMinors = ManualMinor.Checked;
        }

        private void FiftyItemMinors_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.FiftyItemMinors = FiftyItemMinors.Checked;
        }

        private void UseButter_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.UseButterMajor = ButterMajors.Checked;
        }

        private void ManageR3_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageR3 = ManageR3.Checked;
        }

        private void ButterMinors_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.UseButterMinor = ButterMinors.Checked;
        }

        private void ActivateFruits_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ActivateFruits = ActivateFruits.Checked;
        }

        private void WishAddInput_TextChanged(object sender, EventArgs e) => TryItemBoxTextChanged(_wishControls, out _);

        private void WishAddInput_KeyDown(object sender, KeyEventArgs e) => ItemBoxKeyDown(e, _wishControls);

        private void AddWishButton_Click(object sender, EventArgs e) => ItemListAdd(_wishControls);

        private void RemoveWishButton_Click(object sender, EventArgs e) => ItemListRemove(_wishControls);

        private void WishUpButton_Click(object sender, EventArgs e) => ItemListUp(_wishControls);

        private void WishDownButton_Click(object sender, EventArgs e) => ItemListDown(_wishControls);

        private void WishBlacklistAddInput_TextChanged(object sender, EventArgs e) => TryItemBoxTextChanged(_wishBlacklistControls, out _);

        private void WishBlacklistAddInput_KeyDown(object sender, KeyEventArgs e) => ItemBoxKeyDown(e, _wishBlacklistControls);

        private void AddWishBlacklistButton_Click(object sender, EventArgs e) => ItemListAdd(_wishBlacklistControls);

        private void RemoveWishBlacklistButton_Click(object sender, EventArgs e) => ItemListRemove(_wishBlacklistControls);

        private void BeastMode_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.BeastMode = BeastMode.Checked;
        }

        private void CubePriority_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.CubePriority = CubePriority.SelectedIndex;
        }

        private void FavoredMacguffin_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.FavoredMacguffin = ((KeyValuePair<int, string>)FavoredMacguffin.SelectedItem).Key;
        }

        private void ManageNGUDiff_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageNGUDiff = ManageNGUDiff.Checked;
        }

        // The advisor parses profiles with SimpleJSON, which silently misparses malformed JSON
        // (it treats } and ] as interchangeable and ignores stray commas) instead of erroring - so a
        // broken profile just makes the bot misbehave with no feedback. Validate strictly here and
        // surface the exact line/column so the user knows to fix it.
        private void WarnIfProfileInvalid(string profileName)
        {
            try
            {
                var path = Path.Combine(GetProfilesDir(), profileName + ".json");
                if (!File.Exists(path))
                    return;

                var result = ProfileValidator.Validate(File.ReadAllText(path));
                if (result.Ok)
                    return;

                Log($"Profile \"{profileName}\" has invalid JSON at line {result.Line}, col {result.Col}: {result.Message}");
                MessageBox.Show(
                    $"Profile \"{profileName}\" has a JSON error and may not be applied correctly:\n\n" +
                    $"Line {result.Line}, column {result.Col}:\n{result.Message}\n\n" +
                    "The profile is still selected, but you should fix this in the file.",
                    "Profile JSON Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                LogDebug($"Profile validation failed for \"{profileName}\": {ex.Message}");
            }
        }

        private void TitanGoldTargets_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_initializing) return;
            Settings.TitanGoldTargets[(int)e.Item.Tag] = e.Item.Checked;
            Settings.SaveSettings();
        }

        private void ResetTitanStatus_Click(object sender, EventArgs e)
        {
            if (_initializing) return;
            var temp = new bool[ZoneHelpers.TitanZones.Length];
            Settings.TitanMoneyDone = temp;
        }

        private void ManageGoldLoadouts_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageGoldLoadouts = ManageGold.Checked;
        }

        private void ResnipeInput_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            if (TryGetValueFromNumericUpDown(ResnipeInput, out int val))
                Settings.ResnipeTime = val;
        }

        private void GoldSnipeNow_Click(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.GoldSnipeComplete = false;
        }

        private void CBlockMode_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.GoldCBlockMode = CBlockMode.Checked;
        }

        private void HarvestSafety_CheckedChanged(object sender, EventArgs e) => HarvestAllButton.Enabled = HarvestSafety.Checked;

        private void HarvestAllButton_Click(object sender, EventArgs e)
        {
            if (!YggdrasilManager.AnyHarvestable())
                return;

            // Same boundary as the YggPanel button: R6/R8 clean up state and rethrow the primary, which
            // otherwise escapes into the WinForms/Unity pump with no diagnostic and no user feedback. This
            // handler has no cosmetic refresh, so only the core op and completion need bounding.
            bool beganHarvest;
            try
            {
                beganHarvest = LockManager.TryYggdrasilSwap(true);
                if (beganHarvest)
                    YggdrasilManager.HarvestAll(true);
            }
            catch (Exception ex)
            {
                try { Activity.Failed("Harvest failed. See Logs.", null, true); } catch { }
                try { LogDebug($"Manual Yggdrasil harvest failed:\n{ex}"); } catch { }
                return;
            }

            if (!beganHarvest)
            {
                Log("Unable to harvest now");
                return;
            }

            try { Activity.Completed("Harvest completed."); }
            catch (Exception reportEx) { try { LogDebug($"Manual Yggdrasil harvest completion report failed:\n{reportEx}"); } catch { } }
        }

        private void TargetITOPOD_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AdventureTargetITOPOD = TargetITOPOD.Checked;
        }

        private void KillTitan_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            var checkBox = (CheckBox)sender;
            if (int.TryParse(checkBox.Name.Substring(9), out var index))
            {
                Settings.TitanSwapTargets[index - 1] = checkBox.Checked;
                Settings.SaveSettings();
            }
        }

        private void TitanVersion_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            var comboBox = (ComboBox)sender;
            if (int.TryParse(comboBox.Name.Substring(5, comboBox.Name.Length - 12), out var index))
                ZoneHelpers.SetTitanVersion(index - 1, comboBox.SelectedIndex + 1);
        }

        private void TitanSwapTargets_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_initializing) return;
            Settings.TitanSwapTargets[(int)e.Item.Tag] = e.Item.Checked;
            Settings.SaveSettings();
        }

        private void UnloadSafety_CheckedChanged(object sender, EventArgs e) => UnloadButton.Enabled = UnloadSafety.Checked;

        private void UnloadButton_Click(object sender, EventArgs e) => Loader.Unload();

        private void ITOPODCombatMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ITOPODCombatMode = ITOPODCombatMode.SelectedIndex;
        }

        private void ITOPODOptimizeMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ITOPODOptimizeMode = ITOPODOptimizeMode.SelectedIndex;
        }

        private void ITOPODBeastMode_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ITOPODBeastMode = ITOPODBeastMode.Checked;
        }

        private void ITOPODAutoPush_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ITOPODAutoPush = ITOPODAutoPush.Checked;
            // The Adventure panel expresses the same decision as a floor mode. Keep them one truth:
            // a fixed target already pushes on its own, so only Optimal <-> Max flip here.
            if (Settings.ITOPODFloorMode != 1)
                Settings.ITOPODFloorMode = ITOPODAutoPush.Checked ? 2 : 0;
        }

        private void DisableOverlay_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.DisableOverlay = DisableOverlay.Checked;
        }

        private void MoneyPitRunMode_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.MoneyPitRunMode = MoneyPitRunMode.Checked;
        }

        private void ShockwaveInput_TextChanged(object sender, EventArgs e) => TryItemBoxTextChanged(_shockwaveControls, out _);

        private void ShockwaveInput_KeyDown(object sender, KeyEventArgs e) => ItemBoxKeyDown(e, _shockwaveControls);

        private void ShockwaveAdd_Click(object sender, EventArgs e) => ItemListAdd(_shockwaveControls);

        private void ShockwaveRemove_Click(object sender, EventArgs e) => ItemListRemove(_shockwaveControls);

        private void ShockwavePrioUpButton_Click(object sender, EventArgs e) => ItemListUp(_shockwaveControls);

        private void ShockwavePrioDownButton_Click(object sender, EventArgs e) => ItemListDown(_shockwaveControls);

        private void UpgradeDiggers_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.UpgradeDiggers = UpgradeDiggers.Checked;
        }

        private void CastBloodSpells_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.CastBloodSpells = CastBloodSpells.Checked;
        }

        private void IronPillOnRebirth_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.IronPillOnRebirth = IronPillOnRebirth.Checked;
        }

        private void GuffAOnRebirth_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.BloodMacGuffinAOnRebirth = GuffAOnRebirth.Checked;
        }

        private void GuffBOnRebirth_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.BloodMacGuffinBOnRebirth = GuffBOnRebirth.Checked;
        }

        private void QuestCombatMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selected = QuestCombatMode.SelectedIndex;

            if (_initializing) return;
            Settings.QuestCombatMode = selected;
        }

        private void YggSwapThreshold_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            if (TryGetValueFromNumericUpDown(YggSwapThreshold, out int val))
                Settings.YggSwapThreshold = val;
        }

        private void EnemyBlacklistZone_SelectedIndexChanged(object sender, EventArgs e)
        {
            var item = (KeyValuePair<int, string>)EnemyBlacklistZone.SelectedItem;
            var values = _character.adventureController.enemyList[item.Key]
                .Select(x => new KeyValuePair<int, string>(x.spriteID, x.name)).Distinct().ToList();
            EnemyBlacklistNames.DataSource = null;
            EnemyBlacklistNames.ValueMember = "Key";
            EnemyBlacklistNames.DisplayMember = "Value";
            EnemyBlacklistNames.DataSource = values;
        }

        private void BlacklistRemoveEnemyButton_Click(object sender, EventArgs e)
        {
            var item = BlacklistedBosses.SelectedItem;
            if (item == null)
                return;

            var index = BlacklistedBosses.SelectedIndex;

            var id = (KeyValuePair<int, string>)item;

            var temp = Settings.BlacklistedBosses.ToList();
            temp.RemoveAll(x => x == id.Key);
            Settings.BlacklistedBosses = temp.ToArray();

            if (Settings.BlacklistedBosses.Length > index)
                BlacklistedBosses.SelectedIndex = index;
            else if (Settings.BlacklistedBosses.Length > 0)
                BlacklistedBosses.SelectedIndex = Settings.BlacklistedBosses.Length - 1;
        }

        private void BlacklistAddEnemyButton_Click(object sender, EventArgs e)
        {
            var item = EnemyBlacklistNames.SelectedItem;
            if (item == null)
                return;

            var id = (KeyValuePair<int, string>)item;

            // This enemy is excluded already
            if (Array.IndexOf(Settings.BlacklistedBosses, id.Key) >= 0)
                return;

            var temp = Settings.BlacklistedBosses.ToList();
            temp.Add(id.Key);
            Settings.BlacklistedBosses = temp.ToArray();

            BlacklistedBosses.SelectedIndex = Settings.BlacklistedBosses.Length - 1;
        }

        private void BoostAvgReset_Click(object sender, EventArgs e) => ResetBoostProgress();

        private void WeakPriorities_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.WeakPriorities = WeakPriorities.Checked;
        }

        private void ManageMayo_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageMayo = BalanceMayo.Checked;
        }

        private void TrashCards_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.TrashCards = TrashCards.Checked;
        }

        private void AutoCastCards_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AutoCastCards = AutoCastCards.Checked;
        }

        private void CastChonkers_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.CastProtectedCards = CastProtectedCards.Checked;
        }

        private void CardRarity_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            var comboBox = (ComboBox)sender;
            if (int.TryParse(comboBox.Name.Substring(10), out var index))
                Settings.SetCardRarity(index - 1, ((KeyValuePair<int, string>)comboBox.SelectedItem).Key);
        }

        private void CardCost_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            var comboBox = (ComboBox)sender;
            if (int.TryParse(comboBox.Name.Substring(8), out var index))
                Settings.SetCardCost(index - 1, (int)comboBox.SelectedItem);
        }

        private void OpenSettingsFolder_Click(object sender, EventArgs e) => Process.Start(GetSettingsDir());

        // Loadouts-tab restructure (user decision 2): the five in-panel mode-loadout sections retire —
        // list + edit controls hidden, label becomes a pointer. All configuration and previews now live
        // on the LOADOUTS tab (LoadoutsPanel), bound to the same settings.
        private void InstallModeLoadouts()
        {
            void Retire(ListBox list, Control label, Control[] editing)
            {
                try
                {
                    if (label != null)
                    {
                        label.Text = "Loadout: see the LOADOUTS tab";
                        label.ForeColor = UiTheme.Muted;
                    }
                    if (list != null) list.Visible = false;
                    foreach (var c in editing)
                        if (c != null) c.Visible = false;
                }
                catch (Exception ex) { LogDebug($"Loadout retire: {ex.Message}"); }
            }

            Retire(TitanLoadout, TitanLabel, new Control[] { TitanRemove, TitanAddItem, TitanAdd });
            Retire(GoldLoadout, GoldItemLabel, new Control[] { GoldLoadoutRemove, GoldItemBox, GoldLoadoutAdd });
            Retire(QuestLoadoutBox, QuestItemLabel, new Control[] { QuestRemoveButton, QuestLoadoutItem, QuestAddButton });
            Retire(YggdrasilLoadoutBox, YggItemLabel, new Control[] { YggRemoveButton, YggLoadoutItem, YggAddButton });
            Retire(CookingLoadoutBox, CookingItemLabel, new Control[] { CookingRemoveButton, CookingLoadoutItem, CookingAddButton });
        }

        private void TrashProtectedCards_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.TrashProtectedCards = TrashProtectedCards.Checked;
        }

        private void TitanCombatMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.TitanCombatMode = TitanCombatMode.SelectedIndex;
        }

        private void TitanBeastMode_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.TitanBeastMode = TitanBeastMode.Checked;
        }

        private void ManageConsumables_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageConsumables = ManageConsumables.Checked;
        }

        private void AutoBuyAdv_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AutoBuyAdventure = AutoBuyAdv.Checked;
        }

        private void AutoBuyConsumables_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.AutoBuyConsumables = AutoBuyConsumables.Checked;
        }

        private void ConsumeIfRunning_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ConsumeIfAlreadyRunning = ConsumeIfRunning.Checked;
        }

        private void Autosave_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.Autosave = Autosave.Checked;
        }

        private void SortCards_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.CardSortEnabled = SortCards.Checked;
        }

        private void CardSortAdd_Click(object sender, EventArgs e)
        {
            if (CardSortOptions.SelectedItem != null && !CardSortList.Items.Contains(CardSortOptions.SelectedItem))
            {
                CardSortList.Items.Add(CardSortOptions.SelectedItem);
                Settings.CardSortOrder = CardSortList.Items.Cast<string>().ToArray();
            }
        }

        private void CardSortRemove_Click(object sender, EventArgs e)
        {
            if (CardSortList.SelectedItem != null)
            {
                CardSortList.Items.RemoveAt(CardSortList.SelectedIndex);
                Settings.CardSortOrder = CardSortList.Items.Cast<string>().ToArray();
            }
        }

        private void CardSortUp_Click(object sender, EventArgs e) => ItemListMove(CardSortList, Settings.CardSortOrder, Direction.Up);

        private void CardSortDown_Click(object sender, EventArgs e) => ItemListMove(CardSortList, Settings.CardSortOrder, Direction.Down);

        private void LocateWalderp_Click(object sender, EventArgs e)
        {
            if (_character.waldoUnlocker.currentMenu >= 0)
                _character.menuSwapper.swapMenu(_character.waldoUnlocker.currentMenu);
        }

        private void ManageCooking_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageCooking = ManageCooking.Checked;
        }

        private void ManageCookingLoadout_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageCookingLoadouts = ManageCookingLoadout.Checked;
        }

        private void CookingLoadoutBox_TextChanged(object sender, EventArgs e) => TryItemBoxTextChanged(_cookingControls, out _);

        private void CookingLoadoutItem_KeyDown(object sender, KeyEventArgs e) => ItemBoxKeyDown(e, _cookingControls);

        private void CookingAddButton_Click(object sender, EventArgs e) => ItemListAdd(_cookingControls);

        private void CookingRemoveButton_Click(object sender, EventArgs e) => ItemListRemove(_cookingControls);

        private void DiggerCap_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.DiggerCap = (double)(DiggerCap.Value);
        }

        private void ManageWishes_CheckedChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.ManageWishes = ManageWishes.Checked;
        }

        private void WishLimit_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            if (TryGetValueFromNumericUpDown(WishLimit, out int val))
                Settings.WishLimit = val;
        }

        private void WishMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.WishMode = WishMode.SelectedIndex;
        }

        private void WishEnergy_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.WishEnergy = (double)WishEnergy.Value;
        }

        private void WishMagic_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.WishMagic = (double)WishMagic.Value;
        }

        private void WishR3_ValueChanged(object sender, EventArgs e)
        {
            if (_initializing) return;
            Settings.WishR3 = (double)WishR3.Value;
        }
    }
}
