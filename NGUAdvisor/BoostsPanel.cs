using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Systems > BOOSTS sub-tab, V3 (user-approved): segmented [BOOSTING] [TRANSFORMS] views, each
    // getting the full page. First clean rebuild of a legacy section — the resx Boosts page retires
    // to Advanced as the escape hatch.
    //
    // BOOSTING: ADVISOR ACTIVE (green, advisor writes Settings.PriorityBoosts: equipped first via the
    // existing manager pass, then KEEP items by objective usage, then chain climbers) / MANUAL MODE
    // (red: editable priority + blacklist lists). Cube priority + favored MacGuffin ride the top row.
    // TRANSFORMS: one row per chain — live tier/level state + Auto-climb / Keep max lvl / Filter lower.
    //
    // Layout pre-flight (page 620 wide, ~350 tall): top row toggle 10..160, "Cube" 180..215, combo
    // 218..318, "Guffin" 330..377, combo 382..502, refresh 580..608 — no overlaps. Advisor readout
    // list 600x190 at y44. Manual view: priority list h100 + edit row (tb 10..130, Add 136..196,
    // Remove 202..272, Up 278..318, Down 324..364), blacklist h80 + edit row — bottom 328 < 350.
    // Transforms rows at 56px pitch: name 10..120, status 125..375, checkboxes 385/479/563 (measured
    // 86/76/56 wide) right edge 619. Stacked-label pitch rule: all single lines, 18px+ spacing.
    public class BoostsPanel : Panel
    {
        private Button _segBoost;
        private Button _segXform;
        private Panel _boostPage;
        private Panel _xformPage;

        // AUTOMATION = Settings.ManageInventory — INVENTORY-WIDE (Main.cs:846 gates filtering,
        // convertibles, four merge passes, equipped boosting, the cube and boost conversion; AdvisorApply:76
        // gates the advisor's priority write). DECISIONS = Settings.AutoBoostPriority — despite the name,
        // a strategy field: it chooses who writes the boost priority list, not whether boosting runs.
        // The two have DIFFERENT SCOPES, and the bar's text says so instead of implying symmetry.
        private SystemControlBar _controlBar;
        private ComboBox _cube;
        private ComboBox _guffin;
        private Panel _advisorView;
        private Panel _manualView;
        private ListBox _readout;
        private ListBox _prio;
        private TextBox _prioAdd;
        private ListBox _black;
        private TextBox _blackAdd;
        private ComboBox _order;

        // Layout C (user-approved): two-line cards. Line 1 = full item name + right-aligned toggle
        // BUTTONS (measured text — never checkboxes: Mono randomly drops checkbox glyphs). Line 2 =
        // progress bar (nested Panels, proven controls) + "level/100 · next: <name>" detail.
        private class ChainRow
        {
            public Label Name;
            public Button Climb;
            public Button KeepMax;
            public Button Filter;
            public Panel BarOuter;
            public Panel BarInner;
            public Label Detail;
            public void SetVisible(bool v)
            {
                Name.Visible = Climb.Visible = KeepMax.Visible = Filter.Visible = v;
                BarOuter.Visible = Detail.Visible = v;
            }
            public void SetY(int y)
            {
                Name.Top = y + UiTheme.S(2);
                Climb.Top = y + UiTheme.S(2);
                KeepMax.Top = y + UiTheme.S(2);
                Filter.Top = y + UiTheme.S(2);
                BarOuter.Top = y + UiTheme.S(36);
                Detail.Top = y + UiTheme.S(30);
            }
        }
        // Measured button width (design system: never hardcode text-fitted widths) — renderer-true.
        private static int MeasureBtn(string text) => Math.Max(UiTheme.S(42), UiLayout.MeasureText(text, UiTheme.Ui) + UiTheme.S(22));

        private readonly List<ChainRow> _chains = new List<ChainRow>();
        private Panel _xformContent;
        private Label _xformNote1;
        private Label _xformNote2;
        private Label _xformEmpty;

        private bool _syncing;
        private readonly int _w;
        private readonly int _pw;           // per-page width
        private readonly bool _sideBySide;  // C1: full canvas shows BOOSTING and TRANSFORMS together

        // canvasW: explicit canvas width when hosted in an M1 section column (0 = UiLayout.PanelW).
        // How tall this panel's content actually turned out — the pages are sized from their own rows now
        // (the priority list is asked for a ROW COUNT, not a pixel height), so the host cannot keep
        // placing it at a tuned pixel height and still show the last row.
        public int ContentHeight
        {
            get
            {
                int bottom = 0;
                foreach (Control c in Controls) bottom = Math.Max(bottom, c.Bottom);
                return bottom + UiTheme.S(10);
            }
        }

        public BoostsPanel(int canvasW = 0)
        {
            _w = canvasW > 0 ? canvasW : UiLayout.PanelW;
            _sideBySide = _w >= UiTheme.S(900);
            _pw = _sideBySide ? (_w - UiTheme.S(40)) / 2 : _w - UiTheme.S(34);
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            // PANEL-LEVEL bar, not a BOOSTING-column one. AUTOMATION here is Settings.ManageInventory,
            // and that gate is INVENTORY-WIDE (Main.cs:846 runs filtering, convertibles, four merge
            // passes, equipped boosting, the infinity cube and boost conversion). Parking an
            // inventory-wide switch inside a column headed BOOSTING would tell the user it only turns
            // boosting off, and they would find out otherwise by losing their merges. DECISIONS is the
            // narrow one — boost priority only — and the text says so rather than letting the pairing
            // imply equal scope. TRANSFORMS keeps its own rules and is untouched by the decisions layer.
            _controlBar = new SystemControlBar(
                _w - UiTheme.S(34),
                () => Settings.ManageInventory, v => Settings.ManageInventory = v,
                () => Settings.AutoBoostPriority, v => Settings.AutoBoostPriority = v,
                "Inventory automation is on (boosts, merges, filters). The advisor sets boost priority; transforms keep their own rules.",
                "Inventory automation is on (boosts, merges, filters). Your priority list below drives boosting.",
                "Inventory automation is off — no boosting, merging, filtering or convertibles.",
                null,
                "Advisor idle — inventory automation is off. Boosts, merges, filters and convertibles are all disabled.");
            _controlBar.Changed += SyncFromSettings;
            _controlBar.Location = new Point(UiTheme.S(10), UiTheme.S(10));
            Controls.Add(_controlBar);

            // Everything below the bar.
            int top = UiTheme.S(10) + SystemControlBar.BarHeight + UiTheme.S(8);

            {
                int bx = UiTheme.S(10);
                foreach (var name in new[] { "BOOSTING", "TRANSFORMS" })
                {
                    var b = new Button
                    {
                        Text = name,
                        Location = new Point(bx, top),
                        Size = new Size(Math.Max(UiTheme.S(88), UiLayout.MeasureText(name, UiTheme.Ui) + UiTheme.S(26)), UiTheme.SCtl(25)),
                        Font = UiTheme.Ui,
                        FlatStyle = FlatStyle.Flat,
                        Visible = !_sideBySide   // segmented buttons retire on the full canvas
                    };
                    b.FlatAppearance.BorderColor = UiTheme.Border;
                    Controls.Add(b);
                    bx += b.Width + UiTheme.S(6);
                    if (name == "BOOSTING") _segBoost = b; else _segXform = b;
                }
            }
            _segBoost.Click += (s, e) => SelectPage(boost: true);
            _segXform.Click += (s, e) => SelectPage(boost: false);

            BuildBoostPage(top);
            BuildXformPage(top);
            if (_sideBySide)
            {
                // Both pages visible at once: BOOSTING left, TRANSFORMS right, headers instead of
                // the segmented pair.
                Controls.Add(new Label { Text = "BOOSTING", AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(10), top + UiTheme.S(2)) });
                Controls.Add(new Label { Text = "TRANSFORMS", AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(_pw + UiTheme.S(30), top + UiTheme.S(2)) });
                _boostPage.Location = new Point(UiTheme.S(10), top + UiTheme.S(24));
                _xformPage.Location = new Point(_pw + UiTheme.S(30), top + UiTheme.S(24));
                _boostPage.Visible = true;
                _xformPage.Visible = true;
                SyncFromSettings();
                RefreshReadout();
                RefreshChains();
            }
            else
            {
                _boostPage.Tag = "exclusive";
                _xformPage.Tag = "exclusive";
                SyncFromSettings();
                SelectPage(boost: true);
            }
            _advisorView.Tag = "exclusive";
            _manualView.Tag = "exclusive";
        }

        private void SelectPage(bool boost)
        {
            if (_sideBySide) return;   // both always visible on the full canvas
            _boostPage.Visible = boost;
            _xformPage.Visible = !boost;
            UiTheme.ApplyState(_segBoost, boost ? UiTheme.Accent : UiTheme.BtnFace, boost ? Color.White : UiTheme.Ink);
            UiTheme.ApplyState(_segXform, boost ? UiTheme.BtnFace : UiTheme.Accent, boost ? UiTheme.Ink : Color.White);
            if (!boost) RefreshChains();
            else RefreshReadout();
            UiLayout.AuditOnce(boost ? _boostPage : _xformPage, boost ? "Boosts/BOOSTING" : "Boosts/TRANSFORMS");
        }

        private void BuildBoostPage(int top)
        {
            _boostPage = new Panel { Location = new Point(0, top + UiTheme.S(32)), Size = new Size(_pw, UiTheme.S(312)), BackColor = UiTheme.Ground, Visible = false };
            Controls.Add(_boostPage);

            // The old "ADVISOR ACTIVE / MANUAL MODE" button lived here and wrote AutoBoostPriority — the
            // DECISIONS layer under a name that reads like a permission. It is now in the bar.
            var cubeLbl = new Label { Text = "Cube", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            _boostPage.Controls.Add(cubeLbl);
            _cube = new ComboBox { Width = UiTheme.S(100), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui };
            UiTheme.StyleCombo(_cube);
            _cube.Items.AddRange(new object[] { "None", "Balanced", "Softcap", "Power", "Toughness" });
            _cube.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing || Settings == null) return;
                Settings.CubePriority = _cube.SelectedIndex;
            };
            _boostPage.Controls.Add(_cube);

            var gufLbl = new Label { Text = "Guffin", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            _boostPage.Controls.Add(gufLbl);
            _guffin = new ComboBox { Width = UiTheme.S(120), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui };
            UiTheme.StyleCombo(_guffin);
            foreach (var kv in InventoryManager.macguffinList)
                _guffin.Items.Add(new KeyValuePair<int, string>(kv.Key, kv.Value));
            _guffin.DisplayMember = "Value";
            _guffin.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing || Settings == null || _guffin.SelectedItem == null) return;
                Settings.FavoredMacguffin = ((KeyValuePair<int, string>)_guffin.SelectedItem).Key;
            };
            _boostPage.Controls.Add(_guffin);

            var refresh = new Button { Text = "↻", Size = new Size(Math.Max(UiTheme.S(36), UiLayout.BtnWidth("↻")), UiTheme.SCtl(24)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(refresh);
            refresh.Click += (s, e) => { if (Settings != null && Settings.AutoBoostPriority) RefreshReadout(recompute: true); else RefreshReadout(); };
            _boostPage.Controls.Add(refresh);

            // Measured row layout — the old hand-placed "Cube" label overlapped its combo by 3px.
            UiLayout.Row(UiTheme.S(10), UiTheme.S(10), UiTheme.S(8), cubeLbl, _cube, gufLbl, _guffin, refresh);

            // ADVISOR view: computed order readout.
            _advisorView = new Panel { Location = new Point(0, UiTheme.S(44)), Size = new Size(_pw - 0, UiTheme.S(268)), BackColor = UiTheme.Ground, Visible = false };
            _boostPage.Controls.Add(_advisorView);
            _advisorView.Controls.Add(new Label
            {
                Text = "BOOST ORDER (advisor-written; blacklist advisor-managed)",
                Location = new Point(UiTheme.S(10), 0),
                AutoSize = true,
                Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground
            });
            _readout = new ListBox { Location = new Point(UiTheme.S(10), UiTheme.HeadPitch), Size = new Size(_pw - UiTheme.S(30), UiTheme.ListH(8)), Font = UiTheme.Ui, BorderStyle = BorderStyle.FixedSingle, SelectionMode = SelectionMode.None };
            UiTheme.StyleList(_readout);
            _advisorView.Controls.Add(_readout);
            _advisorView.Controls.Add(new Label
            {
                Text = "Order refreshes every 10 minutes (or press the refresh button above).",
                Location = new Point(UiTheme.S(10), _readout.Bottom + UiTheme.S(8)),
                AutoSize = true,
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground
            });

            // MANUAL view: editable priority + blacklist.
            _manualView = new Panel { Location = new Point(0, UiTheme.S(44)), Size = new Size(_pw - 0, UiTheme.S(268)), BackColor = UiTheme.Ground, Visible = false };
            _boostPage.Controls.Add(_manualView);

            // ROWS, NOT OFFSETS. This view is where the boost list is actually edited, and its two lists
            // were pitched in raw pixels (90px and 56px) against rows that pitch from the measured line —
            // so at 200% they showed THREE and ONE entry with a scrollbar, inside a section that had
            // hundreds of unused pixels below. Asking for a row count instead (UiTheme.ListH) makes the
            // usable space the thing that is specified, and a running cursor keeps everything below the
            // lists honest when they change.
            const int PrioRows = 8, BlackRows = 4;
            int listW = _pw - UiTheme.S(30);
            int y = 0;

            _manualView.Controls.Add(new Label { Text = "PRIORITY BOOSTS (item IDs, boosted top-down)", Location = new Point(UiTheme.S(10), y), AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground });
            y += UiTheme.HeadPitch;
            _prio = new ListBox { Location = new Point(UiTheme.S(10), y), Size = new Size(listW, UiTheme.ListH(PrioRows)), Font = UiTheme.Ui, BorderStyle = BorderStyle.FixedSingle };
            UiTheme.StyleList(_prio);
            _manualView.Controls.Add(_prio);
            y = _prio.Bottom + UiTheme.S(8);

            int wAdd = MeasureBtn("Add"), wRem = MeasureBtn("Remove"), wUp = MeasureBtn("Up"), wDown = MeasureBtn("Down");
            _prioAdd = new TextBox { Location = new Point(UiTheme.S(10), y), Width = UiTheme.S(120), Font = UiTheme.Ui };
            _manualView.Controls.Add(_prioAdd);
            int bx = UiTheme.S(10) + _prioAdd.Width + UiTheme.S(6);
            _manualView.Controls.Add(MkBtn("Add", bx, y, wAdd, () => EditList(true, add: true))); bx += wAdd + UiTheme.S(6);
            _manualView.Controls.Add(MkBtn("Remove", bx, y, wRem, () => EditList(true, add: false))); bx += wRem + UiTheme.S(6);
            _manualView.Controls.Add(MkBtn("Up", bx, y, wUp, () => MovePrio(-1))); bx += wUp + UiTheme.S(6);
            _manualView.Controls.Add(MkBtn("Down", bx, y, wDown, () => MovePrio(1)));
            // The row's height is whatever the tallest control in it turned out to be — the TextBox sizes
            // itself from the font and the buttons are floored at SCtl, so neither is a known pixel here.
            y += Math.Max(_prioAdd.Height, UiTheme.SCtl(24)) + UiTheme.S(14);

            _manualView.Controls.Add(new Label { Text = "BOOST BLACKLIST (never boost/merge these IDs)", Location = new Point(UiTheme.S(10), y), AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground });
            y += UiTheme.HeadPitch;
            _black = new ListBox { Location = new Point(UiTheme.S(10), y), Size = new Size(listW, UiTheme.ListH(BlackRows)), Font = UiTheme.Ui, BorderStyle = BorderStyle.FixedSingle };
            UiTheme.StyleList(_black);
            _manualView.Controls.Add(_black);
            y = _black.Bottom + UiTheme.S(8);

            _blackAdd = new TextBox { Location = new Point(UiTheme.S(10), y), Width = UiTheme.S(120), Font = UiTheme.Ui };
            _manualView.Controls.Add(_blackAdd);
            bx = UiTheme.S(10) + _blackAdd.Width + UiTheme.S(6);
            _manualView.Controls.Add(MkBtn("Add", bx, y, wAdd, () => EditList(false, add: true))); bx += wAdd + UiTheme.S(6);
            _manualView.Controls.Add(MkBtn("Remove", bx, y, wRem, () => EditList(false, add: false)));
            int blackRowH = Math.Max(_blackAdd.Height, UiTheme.SCtl(24));

            // Re-homed from the retired Old Boosts page (Phase C): the boost APPLICATION order —
            // Power/Toughness/Special as a six-permutation combo (Mono-safe: no reorder listbox).
            // Narrow M1 column: the combo won't fit after the Remove button — own row below.
            int ordX = bx + wRem + UiTheme.S(20), ordY = y;
            if (_pw < UiTheme.S(560))
            {
                ordX = UiTheme.S(10);
                ordY = y + blackRowH + UiTheme.S(10);
            }
            var ordLbl = new Label { Text = "Apply order", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(ordX, ordY + UiTheme.S(4)) };
            _manualView.Controls.Add(ordLbl);
            _order = new ComboBox { Width = UiTheme.S(170), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui, Location = new Point(ordX + UiLayout.MeasureText("Apply order", UiTheme.Ui) + UiTheme.S(8), ordY) };
            UiTheme.StyleCombo(_order);
            foreach (var p in OrderPerms)
                _order.Items.Add(string.Join(" → ", p));
            _order.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing || Settings == null || _order.SelectedIndex < 0) return;
                Settings.BoostPriority = (string[])OrderPerms[_order.SelectedIndex].Clone();
            };
            _manualView.Controls.Add(_order);

            // THE CARD MUST FIT ITS LAST ROW. The edit rows end in controls whose height is NOT a
            // hand-placed pixel — a Button floored at SCtl, and a ComboBox/TextBox that sizes itself
            // from the font — so the tuned S(268)/S(312) no longer describes the content: the "Apply
            // order" row lands past the bottom edge and a borderless Panel clips it with no scrollbar
            // to reach it. Derive both heights from where the children actually end.
            int manualBottom = 0;
            foreach (Control c in _manualView.Controls) manualBottom = Math.Max(manualBottom, c.Bottom);
            manualBottom += UiTheme.S(8);
            if (_manualView.Height < manualBottom) _manualView.Height = manualBottom;

            // The ADVISOR view is the same card seen the other way round, and its list grew too — so it
            // gets the same treatment rather than keeping a tuned height that no longer describes it.
            int advisorBottom = 0;
            foreach (Control c in _advisorView.Controls) advisorBottom = Math.Max(advisorBottom, c.Bottom);
            advisorBottom += UiTheme.S(8);
            if (_advisorView.Height < advisorBottom) _advisorView.Height = advisorBottom;

            // The page hosts whichever of the two views is showing, so it must fit the TALLER one.
            int pageBottom = Math.Max(_manualView.Bottom, _advisorView.Bottom);
            if (_boostPage.Height < pageBottom) _boostPage.Height = pageBottom;
        }

        private static readonly string[][] OrderPerms =
        {
            new[] { "Power", "Toughness", "Special" },
            new[] { "Power", "Special", "Toughness" },
            new[] { "Toughness", "Power", "Special" },
            new[] { "Toughness", "Special", "Power" },
            new[] { "Special", "Power", "Toughness" },
            new[] { "Special", "Toughness", "Power" },
        };

        private Button MkBtn(string text, int x, int y, int w, Action onClick)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, UiTheme.SCtl(24)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(b);
            b.Click += (s, e) => { try { onClick(); } catch (Exception ex) { LogDebug($"Boosts edit: {ex.Message}"); } };
            return b;
        }

        // Two-line cards, 54px pitch. Toggles right-aligned at edge 610 with MEASURED widths (the
        // "Keep Ma" truncation came from hardcoded widths); the name gets everything left of them.
        private void BuildXformPage(int top)
        {
            // NO AutoScroll of its own. This page sits inside a sub-page that already scrolls, and a
            // nested scroller is the trap the Settings migration documents: the wheel goes to whichever
            // region the pointer happens to be over, so scrolling the Boost page stalled as soon as the
            // cursor crossed into TRANSFORMS. ONE scroll owner — the sub-page — and this page simply grows
            // to its content (see RefreshChains) so there is nothing here left to scroll.
            _xformPage = new Panel { Location = new Point(0, top + UiTheme.S(32)), Size = new Size(_pw, UiTheme.S(312)), BackColor = UiTheme.Ground, Visible = false };
            Controls.Add(_xformPage);
            _xformContent = new Panel { Location = new Point(0, 0), Size = new Size(_pw - UiTheme.S(16), UiTheme.S(312)), BackColor = UiTheme.Ground };
            _xformPage.Controls.Add(_xformContent);

            int Measure(string t) => UiLayout.MeasureText(t, UiTheme.Ui) + UiTheme.S(20);
            int wClimb = Measure("Climb");
            int wKeep = Measure("Keep Max");
            // Filter swaps text; size to the longer so it never moves or clips.
            int wFilter = Math.Max(Measure("Not Filtered"), Measure("Filtered"));
            int xFilter = _xformContent.Width - UiTheme.S(4) - wFilter;
            int xKeep = xFilter - UiTheme.S(6) - wKeep;
            int xClimb = xKeep - UiTheme.S(6) - wClimb;
            int nameW = xClimb - UiTheme.S(18);

            for (int i = 0; i < TransformManager.Chains.Length; i++)
            {
                int idx = i;
                var row = new ChainRow();
                _chains.Add(row);

                row.Name = new Label
                {
                    Text = "",
                    Location = new Point(UiTheme.S(10), UiTheme.S(2)),
                    Size = new Size(nameW, UiTheme.SText(22)),
                    Font = UiTheme.Bold,
                    ForeColor = UiTheme.Accent,
                    BackColor = UiTheme.Ground
                };
                _xformContent.Controls.Add(row.Name);

                row.Climb = MkChainToggle("Climb", xClimb, wClimb, idx, () => Settings.TransformAutoClimb, v => Settings.TransformAutoClimb = v);
                row.KeepMax = MkChainToggle("Keep Max", xKeep, wKeep, idx, () => Settings.TransformKeepMax, v => Settings.TransformKeepMax = v);
                row.Filter = MkChainToggle("Not Filtered", xFilter, wFilter, idx, () => Settings.TransformFilter, v => Settings.TransformFilter = v);

                row.BarOuter = new Panel
                {
                    Location = new Point(UiTheme.S(10), UiTheme.S(36)),
                    Size = new Size(UiTheme.S(180), UiTheme.S(10)),
                    BackColor = UiTheme.Surface,
                    BorderStyle = BorderStyle.FixedSingle
                };
                row.BarInner = new Panel { Location = new Point(0, 0), Size = new Size(0, UiTheme.S(10) - 2), BackColor = UiTheme.Accent };
                row.BarOuter.Controls.Add(row.BarInner);
                _xformContent.Controls.Add(row.BarOuter);

                row.Detail = new Label
                {
                    Text = "",
                    Location = new Point(UiTheme.S(200), UiTheme.S(30)),
                    Size = new Size(_xformContent.Width - UiTheme.S(204), UiTheme.SText(22)),
                    Font = UiTheme.Ui,
                    ForeColor = UiTheme.Muted,
                    BackColor = UiTheme.Ground
                };
                _xformContent.Controls.Add(row.Detail);

                row.SetVisible(false);
            }

            _xformEmpty = new Label
            {
                Text = "No transformable items owned yet — chains appear here when one drops.",
                Location = new Point(UiTheme.S(10), UiTheme.S(14)),
                AutoSize = true,
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground,
                Visible = false
            };
            _xformContent.Controls.Add(_xformEmpty);

            // WRAP, DON'T CLIP. These two lines were AutoSize, so they simply ran off the right edge of
            // the content panel and lost their ends ("...spare copies keep mergin"). An AutoSize label
            // past its parent's edge clips SILENTLY — the documented Adventure-footer failure. Bounded
            // width + FitOrGrow applies the project's no-ellipsis rule instead: if it doesn't fit on one
            // line the label grows and word-wraps, so the sentence is always readable in full.
            int noteW = _xformContent.Width - UiTheme.S(20);
            _xformNote1 = new Label
            {
                Location = new Point(UiTheme.S(10), UiTheme.S(240)),
                AutoSize = false,
                Width = noteW,
                Height = UiTheme.TextH,
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground
            };
            _xformContent.Controls.Add(_xformNote1);
            UiLayout.FitOrGrow(_xformNote1, "Held chains freeze only at-100 copies — spare copies keep merging.");
            _xformNote2 = new Label
            {
                Location = new Point(UiTheme.S(10), _xformNote1.Bottom + UiTheme.S(2)),
                AutoSize = false,
                Width = noteW,
                Height = UiTheme.TextH,
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground
            };
            _xformContent.Controls.Add(_xformNote2);
            UiLayout.FitOrGrow(_xformNote2, "Keep Max + Climb keeps one maxed copy; extras climb. Filter drops lower-tier loot.");
        }

        private Button MkChainToggle(string text, int x, int w, int idx, Func<int[]> get, Action<int[]> set)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, UiTheme.S(2)),
                Size = new Size(w, UiTheme.SCtl(24)),
                Font = UiTheme.Ui,
                FlatStyle = FlatStyle.Flat
            };
            b.FlatAppearance.BorderColor = UiTheme.Border;
            b.Click += (s, e) =>
            {
                if (Settings == null) return;
                try
                {
                    var arr = (get() ?? new int[TransformManager.Chains.Length]).ToArray();
                    if (arr.Length < TransformManager.Chains.Length)
                        Array.Resize(ref arr, TransformManager.Chains.Length);
                    arr[idx] = arr[idx] != 0 ? 0 : 1;
                    set(arr);
                }
                catch (Exception ex) { LogDebug($"Chain flag: {ex.Message}"); }
                RefreshChains();
            };
            _xformContent.Controls.Add(b);
            return b;
        }

        private void EditList(bool prio, bool add)
        {
            if (Settings == null) return;
            var box = prio ? _prio : _black;
            var tb = prio ? _prioAdd : _blackAdd;
            var cur = (prio ? Settings.PriorityBoosts : Settings.BoostBlacklist)?.ToList() ?? new List<int>();

            if (add)
            {
                if (!int.TryParse(tb.Text.Trim(), out var id) || id <= 0) return;
                if (!cur.Contains(id)) cur.Add(id);
                tb.Text = "";
            }
            else
            {
                int sel = box.SelectedIndex;
                if (sel < 0 || sel >= cur.Count) return;
                cur.RemoveAt(sel);
            }
            if (prio) Settings.PriorityBoosts = cur.ToArray();
            else Settings.BoostBlacklist = cur.ToArray();
            SyncFromSettings();
        }

        private void MovePrio(int dir)
        {
            if (Settings == null) return;
            var cur = Settings.PriorityBoosts?.ToList() ?? new List<int>();
            int sel = _prio.SelectedIndex;
            int to = sel + dir;
            if (sel < 0 || sel >= cur.Count || to < 0 || to >= cur.Count) return;
            var tmp = cur[sel]; cur[sel] = cur[to]; cur[to] = tmp;
            Settings.PriorityBoosts = cur.ToArray();
            SyncFromSettings();
            _prio.SelectedIndex = to;
        }

        public void SyncFromSettings()
        {
            if (Settings == null) return;
            _syncing = true;
            try
            {
                // Reflects both layers, incl. a flip made from the Settings grid (the other reachable
                // writer of ManageInventory) or a settings reload. Sync() never raises Changed.
                _controlBar?.Sync();

                bool auto = Settings.AutoBoostPriority;
                // MANUAL is not degraded: the user's priority list simply stays authoritative. Nothing
                // here writes PriorityBoosts — only the advisor's own pass does (AdvisorApply:82), and
                // only when this flag is on. Opening or syncing the panel never overwrites a manual list.
                _advisorView.Visible = auto;
                _manualView.Visible = !auto;

                int cube = Settings.CubePriority;
                if (cube >= 0 && cube < _cube.Items.Count) _cube.SelectedIndex = cube;
                for (int i = 0; i < _guffin.Items.Count; i++)
                    if (((KeyValuePair<int, string>)_guffin.Items[i]).Key == Settings.FavoredMacguffin)
                    { _guffin.SelectedIndex = i; break; }
                var cur = Settings.BoostPriority != null && Settings.BoostPriority.Length == 3
                    ? Settings.BoostPriority : OrderPerms[0];
                for (int i = 0; i < OrderPerms.Length; i++)
                    if (OrderPerms[i][0] == cur[0] && OrderPerms[i][1] == cur[1])
                    { _order.SelectedIndex = i; break; }

                _prio.BeginUpdate();
                _prio.Items.Clear();
                foreach (var id in Settings.PriorityBoosts ?? new int[0])
                    _prio.Items.Add($"{ItemNameNice(id)}  (#{id})");
                _prio.EndUpdate();

                _black.BeginUpdate();
                _black.Items.Clear();
                foreach (var id in Settings.BoostBlacklist ?? new int[0])
                    _black.Items.Add($"{ItemNameNice(id)}  (#{id})");
                _black.EndUpdate();

            }
            finally { _syncing = false; }
            RefreshChains();
            if (Settings.AutoBoostPriority) RefreshReadout();
        }

        private static bool Flag(int[] arr, int i) => arr != null && i < arr.Length && arr[i] != 0;

        // Advisor readout from the last computed verdict (cheap); the full compute (30+ optimizer
        // runs) happens ONLY on the explicit refresh button — never during form construction.
        private void RefreshReadout(bool recompute = false)
        {
            try
            {
                if (Main.Character == null) return;
                var v = InventoryAdvisor.Last;
                if (v == null && !recompute)
                {
                    _readout.BeginUpdate();
                    _readout.Items.Clear();
                    _readout.Items.Add("1. equipped gear  (always boosted first)");
                    _readout.Items.Add("… press the refresh button above to compute the ranked order");
                    _readout.EndUpdate();
                    return;
                }
                if (v == null || recompute)
                    v = InventoryAdvisor.Compute();

                var ids = InventoryAdvisor.AutoBoostPriority(v);
                _readout.BeginUpdate();
                _readout.Items.Clear();
                _readout.Items.Add("1. equipped gear  (always boosted first)");
                int n = 2;
                foreach (var id in ids)
                {
                    string note = v.Usage.TryGetValue(id, out var u) ? $"used by {u} objectives" : "chain climber";
                    _readout.Items.Add($"{n}. {ItemNameNice(id)}  (#{id})  ·  {note}");
                    n++;
                }
                _readout.EndUpdate();
            }
            catch (Exception ex) { LogDebug($"Boost readout: {ex.Message}"); }
        }

        // Live chain states: only OWNED chains show, packed top-down as two-line cards. C1 naming
        // ("Ascended x n") everywhere; next tier by NAME; top-tier singles show provenance.
        private void RefreshChains()
        {
            try
            {
                if (Main.Character == null || Settings == null) return;
                int y = UiTheme.S(6);
                {
                    for (int i = 0; i < _chains.Count; i++)
                    {
                        var row = _chains[i];
                        var s = TransformManager.Read(i);
                        if (s.OwnedTier < 0)
                        {
                            row.SetVisible(false);
                            continue;
                        }

                        row.SetVisible(true);
                        row.SetY(y);
                        UiLayout.FitInto(row.Name, ItemNameNice(s.OwnedId));

                        long lvl = Math.Max(0, Math.Min(100, s.Level));
                        row.BarInner.Width = (int)((UiTheme.S(180) - 2) * lvl / 100);
                        string detail;
                        if (s.NextId > 0)
                            detail = $"{s.Level}/100 · next: {ItemNameNice(s.NextId)}";
                        else if (s.OwnedTier > 0)
                            detail = $"{s.Level}/100 · top tier — from {ItemNameNice(TransformManager.Chains[i].Tiers[s.OwnedTier - 1])}";
                        else
                            detail = $"{s.Level}/100 · top tier";
                        UiLayout.FitInto(row.Detail, detail);

                        StyleOnOff(row.Climb, Flag(Settings.TransformAutoClimb, i));
                        StyleOnOff(row.KeepMax, Flag(Settings.TransformKeepMax, i));
                        bool filtered = Flag(Settings.TransformFilter, i);
                        row.Filter.Text = filtered ? "Filtered" : "Not Filtered";
                        UiTheme.ApplyState(row.Filter, filtered ? UiTheme.Faint : UiTheme.Cap, Color.White);

                        y += UiTheme.S(54);
                    }
                }
                _xformEmpty.Visible = y == UiTheme.S(6);
                _xformNote1.Top = Math.Max(y + UiTheme.S(8), UiTheme.S(60));
                // Both notes may have grown to two lines, so chain off their real bottoms.
                _xformNote2.Top = _xformNote1.Bottom + UiTheme.S(2);
                _xformContent.Height = _xformNote2.Bottom + UiTheme.S(10);
                // GROW, don't scroll: this page has no scrollbar of its own any more, so it takes its
                // content's height and lets the one scroll owner above it do the scrolling.
                _xformPage.Height = _xformContent.Height;
            }
            catch (Exception ex) { LogDebug($"Chain status: {ex.Message}"); }
        }

        private static void StyleOnOff(Button b, bool on)
        {
            UiTheme.ApplyState(b, on ? UiTheme.Cap : UiTheme.Danger, Color.White);
        }

    }
}
