using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Combat > ADVENTURE sub-tab, V2 — REBUILT on UiLayout.Row (no hand-computed x positions: rows
    // place measured controls left-to-right, so sibling overlap is impossible by construction) and
    // audited at Shown by UiLayout.Audit. Segmented [ZONES] [ITOPOD] [BLACKLIST].
    public class AdventurePanel : Panel
    {
        private readonly List<Button> _segButtons = new List<Button>();
        private readonly List<Panel> _pages = new List<Panel>();

        // AUTOMATION = Settings.CombatEnabled — the ADVENTURE ROUTING gate (Main.cs:1218, AdvisorApply:513).
        // It does NOT gate all combat: titan (Main.cs:1183), quest (:1196) and gold-CBlock (:1202) routing
        // all DoZone() and return BEFORE it. DECISIONS = Settings.AdvisorZones — who picks the FARM ZONE
        // (SnipeZone), and only that: Gear Hunt (:1223) and Target ITOPOD (:1225) both outrank it.
        private SystemControlBar _controlBar;
        private int _pageTop;

        // ZONES view
        private Button _farmGear;
        private Button _farmBoost;
        private ComboBox _zoneCombo;
        private Label _zoneLbl;
        private Button _gearHunt;
        private Label _huntLbl;
        private ComboBox _huntZone;
        private Label _huntLine;
        private Label _boostLine1;
        private Label _boostLine2;
        private Label _gearLine;
        private Button _beast;
        private Button _bossesOnly;
        private Button _fallthrough;
        private ComboBox _combatMode;

        // ITOPOD view
        private Button _targetItopod;
        private Button _autoPush;
        private Button _itopodBeast;
        private ComboBox _itopodOptimize;
        private ComboBox _itopodCombat;
        private Label _floorInfo;

        // BLACKLIST view
        private ListBox _blackList;
        private ComboBox _blZone;
        private ComboBox _blEnemy;
        private Dictionary<int, string> _spriteNames;

        private bool _syncing;
        private readonly int _w;

        // How tall this panel's content actually is. It deliberately has NO AutoScroll (see the ctor: a
        // scrollbar here eats client width and re-wraps the very content it was added to reveal), and its
        // pages derive their own heights from their last row — so the HOST has to be told the real number,
        // or the tallest page is simply cut off (the reported "below COMBAT STYLE is not visible").
        // Hidden pages count: any of them can be the one on screen.
        public int ContentHeight
        {
            get
            {
                int bottom = 0;
                foreach (Control c in Controls) bottom = Math.Max(bottom, c.Bottom);
                return bottom + UiTheme.S(10);
            }
        }

        // canvasW: explicit canvas width when hosted in an M1 section column (0 = UiLayout.PanelW).
        public AdventurePanel(int canvasW = 0)
        {
            _w = canvasW > 0 ? canvasW : UiLayout.PanelW;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;
            // NO AutoScroll here. The bar costs 72px in a column that was already ~30px from full, and an
            // AutoScroll host is a trap: the vertical bar eats ~17px of client width, so content laid out
            // to the full width then summons a HORIZONTAL bar too (seen in game). The column is given the
            // height it needs in SettingsForm instead — the Combat section already scrolls.

            // PANEL-LEVEL, not inside ZONES. CombatEnabled gates the whole adventure routing tail —
            // which feeds the ITOPOD page's Target ITOPOD as much as the ZONES page's farm zone — so it
            // is not a zones-only switch. DECISIONS is the narrow one (who picks the farm zone), and the
            // text says so. It also says what AUTOMATION does NOT stop: titan, quest and gold-snipe
            // routing all DoZone() and return BEFORE the CombatEnabled check (Main.cs:1183/1196/1202 vs
            // :1218), so "combat off" has never meant "no combat".
            // Width matches the pages' right edge (_w - 34 at x=0), so nothing sticks out past the widest
            // sibling — the thing that summoned the horizontal scrollbar.
            _controlBar = new SystemControlBar(
                _w - UiTheme.S(44),
                () => Settings.CombatEnabled, v => Settings.CombatEnabled = v,
                () => Settings.AdvisorZones, v => Settings.AdvisorZones = v,
                "Advisor picks the farm zone. Gear Hunt and ITOPOD outrank it.",
                "Your zone is the farm zone. Gear Hunt and ITOPOD outrank it.",
                "Adventure routing off — titan and quest zones still run.",
                null,
                "Advisor idle — adventure routing off (titans/quests run).");
            _controlBar.Changed += SyncFromSettings;
            _controlBar.Location = new Point(UiTheme.S(10), UiTheme.S(10));
            Controls.Add(_controlBar);

            _pageTop = UiTheme.S(10) + SystemControlBar.BarHeight + UiTheme.S(8);

            int bx = UiTheme.S(10);
            foreach (var name in new[] { "ZONES", "ITOPOD", "BLACKLIST" })
            {
                var b = MkBtn(name, Math.Max(UiTheme.S(88), UiLayout.BtnWidth(name)));
                b.Location = new Point(bx, _pageTop);
                int idx = _segButtons.Count;
                b.Click += (s, e) => SelectPage(idx);
                Controls.Add(b);
                _segButtons.Add(b);
                bx += b.Width + UiTheme.S(6);
            }

            _pages.Add(BuildZonesPage());
            _pages.Add(BuildItopodPage());
            _pages.Add(BuildBlacklistPage());
            foreach (var p in _pages)
            {
                p.Tag = "exclusive";   // alternate views share the area below the segment bar
                Controls.Add(p);
            }

            SyncFromSettings();
            SelectPage(0);
        }

        private static Button MkBtn(string text, int? width = null)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(width ?? UiLayout.BtnWidth(text), UiTheme.SCtl(24)),
                Font = UiTheme.Ui,
                FlatStyle = FlatStyle.Flat
            };
            b.FlatAppearance.BorderColor = UiTheme.Border;
            return b;
        }

        private static Label MkLbl(string text, bool muted = true)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = UiTheme.Ui,
                ForeColor = muted ? UiTheme.Muted : UiTheme.Ink,
                BackColor = UiTheme.Ground
            };
        }

        private static Label MkHead(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground
            };
        }

        private void SelectPage(int idx)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].Visible = i == idx;
                UiTheme.ApplyState(_segButtons[i], i == idx ? UiTheme.Accent : UiTheme.BtnFace, i == idx ? Color.White : UiTheme.Ink);
            }
            if (idx == 0) RefreshBoostAdvice();
            if (idx == 1) RefreshFloorInfo();
            UiLayout.AuditOnce(_pages[idx], $"Adventure/{_segButtons[idx].Text}");
        }

        private Panel NewPage() => new Panel { Location = new Point(0, _pageTop + UiTheme.S(32)), Size = new Size(_w - UiTheme.S(34), UiTheme.S(440)), BackColor = UiTheme.Ground, Visible = false };

        private Button MkToggle(string text, Action onClick)
        {
            var b = MkBtn(text);
            b.Click += (s, e) =>
            {
                if (Settings == null) return;
                try { onClick(); } catch (Exception ex) { LogDebug($"Adventure toggle: {ex.Message}"); }
                SyncFromSettings();
            };
            return b;
        }

        private Panel BuildZonesPage()
        {
            var page = NewPage();
            int y = UiTheme.S(8);

            var head = MkHead("ZONE SOURCE");
            page.Controls.Add(head);
            head.Location = new Point(UiTheme.S(10), y);
            y += UiTheme.HeadPitch;

            // The old "ADVISOR ROUTES ZONES / MANUAL ZONE" toggle wrote AdvisorZones and lived here; it is
            // the DECISIONS layer and now sits in the bar. The zone picker stays — it IS the manual choice.
            _zoneLbl = MkLbl("Zone");
            _zoneCombo = new ComboBox { Width = UiTheme.S(200), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui };
            UiTheme.StyleCombo(_zoneCombo);
            foreach (var kv in ZoneHelpers.ZoneList)
            {
                if (kv.Key < 0 || ZoneHelpers.ZoneIsTitan(kv.Key)) continue;
                _zoneCombo.Items.Add(new KeyValuePair<int, string>(kv.Key, kv.Value));
            }
            _zoneCombo.Items.Add(new KeyValuePair<int, string>(1000, "ITOPOD"));
            _zoneCombo.DisplayMember = "Value";
            _zoneCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing || Settings == null || _zoneCombo.SelectedItem == null) return;
                Settings.SnipeZone = ((KeyValuePair<int, string>)_zoneCombo.SelectedItem).Key;
            };
            page.Controls.Add(_zoneLbl);
            page.Controls.Add(_zoneCombo);
            y = UiLayout.Row(UiTheme.S(10), y, UiTheme.S(10), _zoneLbl, _zoneCombo) + UiTheme.S(6);

            // Advisor strategies (visible in advisor mode): gear-capping farm outranks the boost
            // farm; the boost farm only leaves the ITOPOD while something consumes boosts.
            _farmGear = MkToggle("Farm Gear Zones", () => Settings.AdvisorFarmGear = !Settings.AdvisorFarmGear);
            _farmBoost = MkToggle("Farm Best Boost", () => Settings.AdvisorFarmBoost = !Settings.AdvisorFarmBoost);
            page.Controls.Add(_farmGear);
            page.Controls.Add(_farmBoost);
            y = UiLayout.Row(UiTheme.S(10), y, UiTheme.S(8), _farmGear, _farmBoost) + UiTheme.S(10);

            // GEAR HUNT (user feature): camp a chosen stage for its drops in the Loot Hunter hybrid
            // set (pool accessories + best P/T). Works in BOTH zone-source modes and outranks the
            // automatic farms; the pool itself is curated in Loadouts › Loot Hunter.
            var ghead = MkHead("GEAR HUNT");
            page.Controls.Add(ghead);
            ghead.Location = new Point(UiTheme.S(10), y);
            y += UiTheme.HeadPitch;

            _gearHunt = MkToggle("Gear Hunt", () =>
            {
                Settings.GearHuntEnabled = !Settings.GearHuntEnabled;
                AdvisorApply.GearRestored();   // re-arm the gear pass: swap on the next tick, not after the 120s throttle
            });
            _huntLbl = MkLbl("Stage");
            _huntZone = new ComboBox { Width = UiTheme.S(200), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui };
            UiTheme.StyleCombo(_huntZone);
            foreach (var kv in ZoneHelpers.ZoneList)
            {
                if (kv.Key < 0 || ZoneHelpers.ZoneIsTitan(kv.Key)) continue;
                _huntZone.Items.Add(new KeyValuePair<int, string>(kv.Key, kv.Value));
            }
            _huntZone.DisplayMember = "Value";
            _huntZone.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing || Settings == null || _huntZone.SelectedItem == null) return;
                Settings.GearHuntZone = ((KeyValuePair<int, string>)_huntZone.SelectedItem).Key;
                SyncFromSettings();
            };
            page.Controls.Add(_gearHunt);
            page.Controls.Add(_huntLbl);
            page.Controls.Add(_huntZone);
            y = UiLayout.Row(UiTheme.S(10), y, UiTheme.S(10), _gearHunt, _huntLbl, _huntZone) + UiTheme.S(4);

            _huntLine = MkLbl("");
            _huntLine.AutoSize = false;
            _huntLine.Size = new Size(page.Width - UiTheme.S(20), UiTheme.TextH);
            page.Controls.Add(_huntLine);
            _huntLine.Location = new Point(UiTheme.S(10), y);
            y += UiTheme.LinePitch * 2;

            var bhead = MkHead("BOOST FARM ADVICE");
            page.Controls.Add(bhead);
            bhead.Location = new Point(UiTheme.S(10), y);
            y += UiTheme.HeadPitch;
            _boostLine1 = new Label { Text = "…", AutoSize = true, Font = UiTheme.Bold, ForeColor = UiTheme.AccentDark, BackColor = UiTheme.Ground };
            page.Controls.Add(_boostLine1);
            _boostLine1.Location = new Point(UiTheme.S(10), y);
            y += UiTheme.LinePitch;
            // Fixed width + 2-line reservation: these advisor verdicts run long and were AutoSize labels
            // that clipped past the narrow combat-column edge. FitOrGrow (in RefreshBoostAdvice) wraps them.
            _boostLine2 = MkLbl("");
            _boostLine2.AutoSize = false;
            _boostLine2.Size = new Size(page.Width - UiTheme.S(20), UiTheme.TextH);
            page.Controls.Add(_boostLine2);
            _boostLine2.Location = new Point(UiTheme.S(10), y);
            y += UiTheme.LinePitch * 2;
            _gearLine = MkLbl("");
            _gearLine.AutoSize = false;
            _gearLine.Size = new Size(page.Width - UiTheme.S(20), UiTheme.TextH);
            page.Controls.Add(_gearLine);
            _gearLine.Location = new Point(UiTheme.S(10), y);
            y += UiTheme.LinePitch * 2 + UiTheme.S(4);

            var chead = MkHead("COMBAT STYLE");
            page.Controls.Add(chead);
            chead.Location = new Point(UiTheme.S(10), y);
            y += UiTheme.HeadPitch;

            // "Combat" (CombatEnabled) is GONE from this row — it was never a combat STYLE, it was the
            // AUTOMATION gate sitting among posture options, which is how "combat off" came to look like
            // it stopped all fighting when titan and quest zones carry on regardless.
            _beast = MkToggle("Beast Mode", () => Settings.BeastMode = !Settings.BeastMode);
            _bossesOnly = MkToggle("Bosses Only", () => Settings.SnipeBossOnly = !Settings.SnipeBossOnly);
            _fallthrough = MkToggle("Fallthrough", () => Settings.AllowZoneFallback = !Settings.AllowZoneFallback);
            var modeLbl = MkLbl("Mode");
            _combatMode = new ComboBox { Width = UiTheme.S(110), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui };
            UiTheme.StyleCombo(_combatMode);
            _combatMode.Items.AddRange(new object[] { "Idle", "Snipe", "Defensive", "Offensive" });
            _combatMode.SelectedIndexChanged += (s, e) => { if (!_syncing && Settings != null) Settings.CombatMode = _combatMode.SelectedIndex; };
            foreach (Control c in new Control[] { _beast, _bossesOnly, _fallthrough, modeLbl, _combatMode })
                page.Controls.Add(c);
            // Wraps in narrow M1 columns — but the three posture toggles wrap as a GROUP and "Mode" keeps
            // its dropdown. WrapRow has no notion of a label belonging to the control beside it, so with
            // all five in one call it broke exactly there: "Mode" stayed on the toggle row and its combo
            // dropped to the next line, leaving a stranded caption above an unlabelled dropdown.
            int styleRowPitch = Math.Max(UiTheme.S(30), _combatMode.Height + UiTheme.S(6));
            y = UiLayout.WrapRow(UiTheme.S(10), y, UiTheme.S(8), page.Width - UiTheme.S(10), styleRowPitch,
                new Control[] { _beast, _bossesOnly, _fallthrough });
            y = UiLayout.Row(UiTheme.S(10), y, UiTheme.S(8), modeLbl, _combatMode) + UiTheme.S(8);

            // Two short stacked lines: the single long line measured past the page edge and clipped.
            var note1 = MkLbl("Advisor routing: gold and quest logic keep their overrides;");
            var note2 = MkLbl("otherwise the best boost farm wins.");
            page.Controls.Add(note1);
            page.Controls.Add(note2);
            note1.Location = new Point(UiTheme.S(10), y);
            note2.Location = new Point(UiTheme.S(10), y + UiTheme.LinePitch);
            // The page height is a fixed constant while every stacked line inside it is floored at the
            // measured pitch — the closing notes fell off the bottom. Derive from the last line.
            page.Height = Math.Max(page.Height, note2.Bottom + UiTheme.S(8));
            return page;
        }

        private Panel BuildItopodPage()
        {
            var page = NewPage();
            int y = UiTheme.S(8);

            var head = MkHead("ITOPOD");
            page.Controls.Add(head);
            head.Location = new Point(UiTheme.S(10), y);
            y += UiTheme.HeadPitch;

            _targetItopod = MkToggle("Target ITOPOD", () => Settings.AdventureTargetITOPOD = !Settings.AdventureTargetITOPOD);
            _autoPush = MkToggle("Auto-Push", () => Settings.ITOPODAutoPush = !Settings.ITOPODAutoPush);
            _itopodBeast = MkToggle("Beast Mode", () => Settings.ITOPODBeastMode = !Settings.ITOPODBeastMode);
            foreach (Control c in new Control[] { _targetItopod, _autoPush, _itopodBeast })
                page.Controls.Add(c);
            y = UiLayout.Row(UiTheme.S(10), y, UiTheme.S(8), _targetItopod, _autoPush, _itopodBeast) + UiTheme.S(14);

            var optLbl = MkLbl("Optimize");
            _itopodOptimize = new ComboBox { Width = UiTheme.S(110), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui };
            UiTheme.StyleCombo(_itopodOptimize);
            _itopodOptimize.Items.AddRange(new object[] { "Disabled", "Default", "PP", "EXP/AP" });
            _itopodOptimize.SelectedIndexChanged += (s, e) => { if (!_syncing && Settings != null) Settings.ITOPODOptimizeMode = _itopodOptimize.SelectedIndex; };
            var cmbLbl = MkLbl("Combat");
            _itopodCombat = new ComboBox { Width = UiTheme.S(110), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui };
            UiTheme.StyleCombo(_itopodCombat);
            _itopodCombat.Items.AddRange(new object[] { "Idle", "Snipe", "Defensive", "Offensive" });
            _itopodCombat.SelectedIndexChanged += (s, e) => { if (!_syncing && Settings != null) Settings.ITOPODCombatMode = _itopodCombat.SelectedIndex; };
            foreach (Control c in new Control[] { optLbl, _itopodOptimize, cmbLbl, _itopodCombat })
                page.Controls.Add(c);
            y = UiLayout.Row(UiTheme.S(10), y, UiTheme.S(8), optLbl, _itopodOptimize, cmbLbl, _itopodCombat) + UiTheme.S(14);

            _floorInfo = MkLbl("");
            page.Controls.Add(_floorInfo);
            _floorInfo.Location = new Point(UiTheme.S(10), y);
            return page;
        }

        private Panel BuildBlacklistPage()
        {
            var page = NewPage();
            int y = UiTheme.S(8);

            var head = MkHead("ENEMY BLACKLIST (never sniped)");
            page.Controls.Add(head);
            head.Location = new Point(UiTheme.S(10), y);
            y += UiTheme.HeadPitch;

            _blackList = new ListBox { Location = new Point(UiTheme.S(10), y), Size = new Size(page.Width - UiTheme.S(20), UiTheme.ListH(8)), Font = UiTheme.Ui, BorderStyle = BorderStyle.FixedSingle };
            UiTheme.StyleList(_blackList);
            page.Controls.Add(_blackList);
            y = _blackList.Bottom + UiTheme.S(8);

            _spriteNames = new Dictionary<int, string>();
            _blZone = new ComboBox { Width = UiTheme.S(185), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui };
            UiTheme.StyleCombo(_blZone);
            _blEnemy = new ComboBox { Width = UiTheme.S(200), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui };
            UiTheme.StyleCombo(_blEnemy);
            try
            {
                var el = Main.Character.adventureController.enemyList;
                for (int z = 0; z < el.Count; z++)
                {
                    if (el[z] == null || el[z].Count == 0) continue;
                    foreach (var en in el[z])
                        if (!_spriteNames.ContainsKey(en.spriteID))
                            _spriteNames[en.spriteID] = en.name;
                    string zn = ZoneHelpers.ZoneList.TryGetValue(z, out var n) ? n : $"Zone {z}";
                    _blZone.Items.Add(new KeyValuePair<int, string>(z, zn));
                }
            }
            catch (Exception ex) { LogDebug($"Blacklist zones: {ex.Message}"); }
            _blZone.DisplayMember = "Value";
            _blZone.SelectedIndexChanged += (s, e) =>
            {
                try
                {
                    if (_blZone.SelectedItem == null) return;
                    int z = ((KeyValuePair<int, string>)_blZone.SelectedItem).Key;
                    _blEnemy.Items.Clear();
                    foreach (var en in Main.Character.adventureController.enemyList[z].Select(x => new KeyValuePair<int, string>(x.spriteID, x.name)).Distinct())
                        _blEnemy.Items.Add(en);
                    _blEnemy.DisplayMember = "Value";
                    if (_blEnemy.Items.Count > 0) _blEnemy.SelectedIndex = 0;
                }
                catch (Exception ex) { LogDebug($"Blacklist enemies: {ex.Message}"); }
            };

            var add = MkBtn("Add");
            UiTheme.StyleFlat(add);
            add.Click += (s, e) =>
            {
                if (Settings == null || _blEnemy.SelectedItem == null) return;
                int id = ((KeyValuePair<int, string>)_blEnemy.SelectedItem).Key;
                var cur = (Settings.BlacklistedBosses ?? new int[0]).ToList();
                if (!cur.Contains(id)) { cur.Add(id); Settings.BlacklistedBosses = cur.ToArray(); }
                SyncFromSettings();
            };
            var rem = MkBtn("Remove");
            UiTheme.StyleFlat(rem);
            rem.Click += (s, e) =>
            {
                if (Settings == null) return;
                int sel = _blackList.SelectedIndex;
                var cur = (Settings.BlacklistedBosses ?? new int[0]).ToList();
                if (sel < 0 || sel >= cur.Count) return;
                cur.RemoveAt(sel);
                Settings.BlacklistedBosses = cur.ToArray();
                SyncFromSettings();
            };
            foreach (Control c in new Control[] { _blZone, _blEnemy, add, rem })
                page.Controls.Add(c);
            UiLayout.WrapRow(UiTheme.S(10), y, UiTheme.S(8), page.Width - UiTheme.S(10), UiTheme.S(30), new Control[] { _blZone, _blEnemy, add, rem });
            // The list is now sized in ROWS, so the edit row below it no longer lands at a known pixel —
            // derive the page height from its lowest child instead of keeping NewPage's tuned constant.
            int blBottom = 0;
            foreach (Control c in page.Controls) blBottom = Math.Max(blBottom, c.Bottom);
            page.Height = Math.Max(page.Height, blBottom + UiTheme.S(8));
            return page;
        }

        private static void StyleOnOff(Button b, bool on)
        {
            UiTheme.ApplyState(b, on ? UiTheme.Cap : UiTheme.Danger, Color.White);
        }

        public void SyncFromSettings()
        {
            if (Settings == null) return;
            _syncing = true;
            try
            {
                // Reflects both layers, incl. a flip from the Settings grid ("Adventure Combat" is the
                // other reachable writer of CombatEnabled) or a settings reload. Sync() never raises Changed.
                _controlBar?.Sync();

                bool advisor = Settings.AdvisorZones;
                _zoneCombo.Visible = _zoneLbl.Visible = !advisor;
                _farmGear.Visible = _farmBoost.Visible = advisor;
                StyleOnOff(_farmGear, Settings.AdvisorFarmGear);
                StyleOnOff(_farmBoost, Settings.AdvisorFarmBoost);
                for (int i = 0; i < _zoneCombo.Items.Count; i++)
                    if (((KeyValuePair<int, string>)_zoneCombo.Items[i]).Key == Settings.SnipeZone)
                    { _zoneCombo.SelectedIndex = i; break; }

                StyleOnOff(_gearHunt, Settings.GearHuntEnabled);
                for (int i = 0; i < _huntZone.Items.Count; i++)
                    if (((KeyValuePair<int, string>)_huntZone.Items[i]).Key == Settings.GearHuntZone)
                    { _huntZone.SelectedIndex = i; break; }
                string hunt;
                if (!Settings.GearHuntEnabled)
                    hunt = "Off — pick a stage; curate the accessory pool in Loadouts › Loot Hunter";
                else if (Settings.GearHuntZone < 0)
                    hunt = "On — pick a stage to hunt";
                else if (!GearHunter.ZoneReachable())
                    hunt = "Stage not reachable yet — zone routing unchanged until it unlocks";
                else
                {
                    int pool = (Settings.LootHunterAccessories ?? new int[0]).Count(x => x > 0);
                    int wr = Settings.LootHunterRespawnCount, wd = Settings.LootHunterDropCount;
                    string picks = wr == 0 && wd == 0
                        ? $"optimizer auto over the {pool}-item pool"
                        : $"{wr} respawn + {wd} DC from the {pool}-item pool";
                    hunt = $"Hunting this stage in the Loot Hunter set ({picks} + best P/T gear)";
                }
                UiLayout.FitOrGrow(_huntLine, hunt, 2);

                StyleOnOff(_beast, Settings.BeastMode);
                StyleOnOff(_bossesOnly, Settings.SnipeBossOnly);
                StyleOnOff(_fallthrough, Settings.AllowZoneFallback);
                int cm = Settings.CombatMode;
                if (cm >= 0 && cm < _combatMode.Items.Count) _combatMode.SelectedIndex = cm;

                StyleOnOff(_targetItopod, Settings.AdventureTargetITOPOD);
                StyleOnOff(_autoPush, Settings.ITOPODAutoPush);
                StyleOnOff(_itopodBeast, Settings.ITOPODBeastMode);
                int om = Settings.ITOPODOptimizeMode;
                if (om >= 0 && om < _itopodOptimize.Items.Count) _itopodOptimize.SelectedIndex = om;
                int icm = Settings.ITOPODCombatMode;
                if (icm >= 0 && icm < _itopodCombat.Items.Count) _itopodCombat.SelectedIndex = icm;

                _blackList.BeginUpdate();
                _blackList.Items.Clear();
                foreach (var id in Settings.BlacklistedBosses ?? new int[0])
                    _blackList.Items.Add(_spriteNames != null && _spriteNames.TryGetValue(id, out var n) ? $"{n}  (#{id})" : $"#{id}");
                _blackList.EndUpdate();
            }
            finally { _syncing = false; }
        }

        private void RefreshBoostAdvice()
        {
            try
            {
                var v = BoostFarmAdvisor.Analyze();
                if (!v.Known) { _boostLine1.Text = "…"; return; }
                _boostLine1.Text = $"Best boost farm: {v.BestName} in {BoostFarmAdvisor.ModeName(v.BestMode)}";
                string line2 = v.BestZone == -1000
                    ? $"~{v.ItopodRate:0.###} boost/s at the optimal floor — beats every farmable zone"
                    : $"~{v.BestRate:0.###} boost/s (ITOPOD {v.ItopodRate:0.###}) — priced against your gear/cube headroom";
                if (v.RateAtCurrentMode > 0 && v.BestRate > v.RateAtCurrentMode * 1.02)
                    line2 += $" · your current {BoostFarmAdvisor.ModeName(Settings?.CombatMode ?? 0)} gets {v.RateAtCurrentMode:0.###} ({v.BestRate / v.RateAtCurrentMode:0.##}x slower)";
                if (Settings != null && Settings.AdvisorFarmBoost && !BoostFarmAdvisor.BoostDemandExists(out var why))
                    line2 += $" · no demand ({why}) — ITOPOD wins";
                UiLayout.FitOrGrow(_boostLine2, line2, 2);

                var g = GearFarmAdvisor.Analyze();
                UiLayout.FitOrGrow(_gearLine, g.Known ? g.Text : "", 2);
            }
            catch (Exception ex) { LogDebug($"Boost advice: {ex.Message}"); }
        }

        private void RefreshFloorInfo()
        {
            try
            {
                var c = Main.Character;
                if (c == null) return;
                int optimal = ItopodConstants.BestFloor(c.totalAdvAttack(), c.idleAttackPower(), false);
                string text = $"Optimal idle floor right now: {optimal}  (highest reached: {c.adventure.highestItopodLevel})";
                // The floor above is the idle one-shot floor; the rates are what the CONFIGURED mode
                // actually earns, averaged over its attack rotation. Boosts are left out — they need
                // a BoostSinks snapshot, and the boost advisor already shows them.
                var rates = ItopodFarmAdvisor.ForMode(Settings?.CombatMode ?? 0);
                if (rates.Known)
                    text += $"\nAt {BoostFarmAdvisor.ModeName(rates.CombatMode)}, floors {rates.DefaultFloor}-{rates.PeakFloor}:"
                          + $" {rates.PpPerSecond:0.####} PP/s · {rates.ExpPerSecond:0.##} EXP/s";
                // Direct assignment, NOT FitOrGrow: this label is AutoSize (MkLbl) and was never given
                // a Width, so FitOrGrow measured against the width of the empty string it was created
                // with and wrapped every line into "Optimal / idle f...". FitOrGrow is for labels that
                // own a fixed width; an AutoSize label grows on its own, including across the \n.
                _floorInfo.Text = text;
            }
            catch (Exception ex) { LogDebug($"Floor info: {ex.Message}"); }
        }
    }
}
