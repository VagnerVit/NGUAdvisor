using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Loadouts tab v2 (user feedback: v1's five stacked cards with 2-line lists were unusable).
    // One mode visible at a time behind a sub-tab bar (the proven Combat/Systems switcher pattern),
    // giving the full tab to that mode: source picker + a tall WILL EQUIP list that shows the whole
    // loadout with no scrolling. "Override Advisor" checkbox replaced by an ADVISOR/MANUAL segmented
    // pair (measured buttons — clearer, and sidesteps the faint checkbox rendering seen in v1).
    //
    // Layout pre-flight (~660x380 tab client): mode bar buttons measured +26px pad at y6.
    // Source row y48: "SOURCE" x10 (~50px) · [ADVISOR] x64 w76 (right 140) · [MANUAL] x144 w72
    // (right 216) · objective combo x230 w170 (right 400) · "Keep Respawn" x412 (~111px, right 523) ·
    // refresh x600 w28 (right 628). Manual row y78: "IDs" x10 · textbox x40 w380 (right 420) · Save
    // x426 w52 (right 478) · "Use Current Gear" x486 w126 (right 612). Preview header y112; list
    // y130 h228 w618 (~13 rows visible; loadouts are <=12 items). No overlaps, no horizontal scroll.
    public class LoadoutsPanel : Panel
    {
        private class Mode
        {
            public string Name;
            public Func<string> GetObj;
            public Action<string> SetObj;
            public Func<bool> GetResp;
            public Action<bool> SetResp;
            public Func<int[]> GetStatic;
            public Action<int[]> SetStatic;
            public bool GoldDefault;
            public bool LootHunter;   // Gear Hunt pool: IDs = accessory CANDIDATES, preview = resolved hybrid
            public bool StaticOnly;   // a plain item list — no objective, so no ADVISOR/MANUAL choice exists

            public Button Tab;
            public Panel Page;
            public Button Src;
            public Button Refresh;
            public Button Save;
            public Button UseCur;
            public ComboBox Combo;
            public Button Resp;
            public Panel ManualRow;
            public TextBox Ids;
            public Label PvHead;
            public ListBox Preview;
            public string LastObjective;

            // UI5 — read-only state surfaces (no equip, no lock, no mutation):
            public Label ReqHelp;       // supporting text under WILL EQUIP
            public ListBox Snapshot;    // CURRENTLY EQUIPPED — snapshot of live gear
            public Label SnapHead;
            public Button RefreshState;
            public Label SnapHelp;
            public Label SwapStatus;    // per-mode enable gate (read-only)
            public Label LockLine;      // generic current equipment lock (read-only)
        }

        private readonly List<Mode> _modes = new List<Mode>();
        private NumericUpDown _lhResp;
        private NumericUpDown _lhDrop;
        private bool _syncing;
        private int _hostW;
        private int _wSave;
        private int _wUse;

        // Measured button width (design system: never hardcode text-fitted widths) — renderer-true.
        private static int MeasureBtn(string text) => Math.Max(UiTheme.S(46), UiLayout.MeasureText(text, UiTheme.Ui) + UiTheme.S(22));

        // Mono settles the form's true (DPI-scaled) size after construction, so ctor-time widths can
        // be stale — SettingsForm calls this from Shown with the final client width to re-fit.
        public void SetHostWidth(int hostW)
        {
            hostW = Math.Max(UiTheme.S(560), hostW);
            if (hostW == _hostW) return;
            _hostW = hostW;
            foreach (var m in _modes)
            {
                if (m.Page == null) continue;
                m.Page.Width = _hostW;
                m.Refresh.Left = _hostW - UiTheme.S(46);
                m.Preview.Width = _hostW - UiTheme.S(22);
                if (m.Snapshot != null) m.Snapshot.Width = _hostW - UiTheme.S(22);
                if (m.RefreshState != null) m.RefreshState.Left = (_hostW - UiTheme.S(22)) + UiTheme.S(10) - m.RefreshState.Width;
                m.ManualRow.Width = _hostW;
                int idsW = _hostW - (UiTheme.S(40) + UiTheme.S(6) + _wSave + UiTheme.S(8) + _wUse + UiTheme.S(10));
                m.Ids.Width = idsW;
                m.Save.Left = UiTheme.S(40) + idsW + UiTheme.S(6);
                m.UseCur.Left = UiTheme.S(40) + idsW + UiTheme.S(6) + _wSave + UiTheme.S(8);
            }
        }

        public LoadoutsPanel(int hostW = 640)
        {
            _hostW = Math.Max(UiTheme.S(560), hostW);
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            _modes.Add(new Mode
            {
                Name = "TITAN",
                GetObj = () => Settings.TitanObjective, SetObj = v => Settings.TitanObjective = v,
                GetResp = () => Settings.TitanObjectiveRespawn, SetResp = v => Settings.TitanObjectiveRespawn = v,
                GetStatic = () => Settings.TitanLoadout, SetStatic = v => Settings.TitanLoadout = v,
            });
            _modes.Add(new Mode
            {
                Name = "GOLD",
                GetObj = () => Settings.GoldObjective, SetObj = v => Settings.GoldObjective = v,
                GetResp = () => Settings.GoldObjectiveRespawn, SetResp = v => Settings.GoldObjectiveRespawn = v,
                GetStatic = () => Settings.GoldDropLoadout, SetStatic = v => Settings.GoldDropLoadout = v,
                GoldDefault = true,
            });
            _modes.Add(new Mode
            {
                Name = "QUEST",
                GetObj = () => Settings.QuestObjective, SetObj = v => Settings.QuestObjective = v,
                GetResp = () => Settings.QuestObjectiveRespawn, SetResp = v => Settings.QuestObjectiveRespawn = v,
                GetStatic = () => Settings.QuestLoadout, SetStatic = v => Settings.QuestLoadout = v,
            });
            _modes.Add(new Mode
            {
                Name = "YGGDRASIL",
                GetObj = () => Settings.YggdrasilObjective, SetObj = v => Settings.YggdrasilObjective = v,
                GetResp = () => Settings.YggdrasilObjectiveRespawn, SetResp = v => Settings.YggdrasilObjectiveRespawn = v,
                GetStatic = () => Settings.YggdrasilLoadout, SetStatic = v => Settings.YggdrasilLoadout = v,
            });
            _modes.Add(new Mode
            {
                Name = "COOKING",
                GetObj = () => Settings.CookingObjective, SetObj = v => Settings.CookingObjective = v,
                GetResp = () => Settings.CookingObjectiveRespawn, SetResp = v => Settings.CookingObjectiveRespawn = v,
                GetStatic = () => Settings.CookingLoadout, SetStatic = v => Settings.CookingLoadout = v,
            });
            // Gear Hunt (user feature): the ID list is the ACCESSORY POOL (Drop Chance / Respawn
            // candidates), not a literal loadout — the advisor equips the best of the pool plus the
            // optimizer's best Power/Toughness gear. Objective/respawn lambdas are inert.
            _modes.Add(new Mode
            {
                Name = "LOOT HUNTER",
                GetObj = () => "", SetObj = v => { },
                GetResp = () => false, SetResp = v => { },
                GetStatic = () => Settings.LootHunterAccessories, SetStatic = v => Settings.LootHunterAccessories = v,
                LootHunter = true,
            });
            // Re-homed from the retired Old Pit page (Phase B): the 7-day shockwave set is a plain
            // static list — no objective concept, so the objective lambdas are inert.
            _modes.Add(new Mode
            {
                Name = "SHOCKWAVE",
                GetObj = () => "", SetObj = v => { },
                GetResp = () => false, SetResp = v => { },
                GetStatic = () => Settings.Shockwave, SetStatic = v => Settings.Shockwave = v,
                StaticOnly = true,   // no objective concept: its ADVISOR/MANUAL button could never move
            });

            // NO SystemControlBar here, and that is the finding — not an omission.
            //
            // The slice assumed Settings.ManageGear was this panel's automation gate. It is not. ManageGear
            // has exactly THREE readers in the whole codebase: the advisor's segment/objective gear refresh
            // (AdvisorApply:611, which also equips the Loot Hunter set), the PROFILE's gear timeline
            // (CustomAllocation:158, only when !AutoProfile), and a display flag (ChallengesPanel:188).
            // It gates NONE of the mode swaps. Each mode is enabled by its OWN setting:
            //   TITAN -> SwapTitanLoadouts   GOLD -> ManageGoldLoadouts   QUEST -> ManageQuestLoadouts
            //   YGGDRASIL -> SwapYggdrasilLoadouts   COOKING -> ManageCookingLoadouts
            //   LOOT HUNTER -> GearHuntEnabled (+ ManageGear, since ApplyGearRefresh equips it)
            //   SHOCKWAVE -> the pit swap consumes the list (MoneyPitManager:144)
            // So an "AUTOMATION ON/OFF" header wired to ManageGear would tell the user it governs the seven
            // modes when it governs none of them — precisely the lie this whole migration exists to remove.
            // What the panel owes the user instead is the truth, in two lines.
            var note1 = new Label
            {
                Text = "No single gear switch: each mode swaps when its OWN setting is on (Titans · Gold · Quests · Yggdrasil · Cooking · Gear Hunt).",
                AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), UiTheme.S(6))
            };
            var note2 = new Label
            {
                Text = "Per mode — ADVISOR: the optimizer picks the set for an objective.  MANUAL: your item list below.",
                AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Faint, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), UiTheme.S(6) + UiTheme.LinePitch)
            };
            Controls.Add(note1);
            Controls.Add(note2);

            // Mode sub-tab bar (measured widths — never estimate).
            int bx = UiTheme.S(10);
            {
                foreach (var m in _modes)
                {
                    int textW = UiLayout.MeasureText(m.Name, UiTheme.Ui);
                    var mode = m;
                    m.Tab = new Button
                    {
                        Text = m.Name,
                        Location = new Point(bx, UiTheme.S(6)),
                        Size = new Size(Math.Max(UiTheme.S(70), textW + UiTheme.S(26)), UiTheme.SCtl(25)),
                        Font = UiTheme.Ui,
                        FlatStyle = FlatStyle.Flat
                    };
                    m.Tab.FlatAppearance.BorderColor = UiTheme.Border;
                    m.Tab.Click += (s, e) => SelectMode(mode);
                    Controls.Add(m.Tab);
                    bx += m.Tab.Width + UiTheme.S(6);
                }
            }

            foreach (var m in _modes)
                BuildPage(m);

            SyncFromSettings();
            SelectMode(_modes[0]);
        }

        // A1 rail sub-nav: the mode bar folds into the left rail — the rail calls these.
        public void HideModeBar()
        {
            foreach (var m in _modes)
                if (m.Tab != null) m.Tab.Visible = false;
        }

        public void SelectModeByName(string name)
        {
            foreach (var m in _modes)
                if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) { SelectMode(m); return; }
        }

        private void SelectMode(Mode sel)
        {
            foreach (var m in _modes)
            {
                bool on = m == sel;
                m.Page.Visible = on;
                UiTheme.ApplyState(m.Tab, on ? UiTheme.Accent : UiTheme.BtnFace, on ? Color.White : UiTheme.Ink);
            }
            RefreshCard(sel);
            RefreshSnapshot(sel);   // capture live-gear snapshot + gate/lock when the page is shown
            UiLayout.AuditOnce(sel.Page, $"Loadouts/{sel.Name}");
        }

        private void BuildPage(Mode m)
        {
            var page = new Panel
            {
                // Below the two-line panel note (the mode bar itself is hidden — the rail owns mode nav).
                Location = new Point(0, UiTheme.S(64)),
                Size = new Size(_hostW, UiTheme.S(340)),
                BackColor = UiTheme.Ground,
                Visible = false,
                Tag = "exclusive"   // mode pages share the area below the sub-tab bar
            };
            m.Page = page;
            Controls.Add(page);

            var srcHead = new Label
            {
                Text = "SOURCE",
                Location = new Point(UiTheme.S(10), UiTheme.S(15)),
                AutoSize = true,
                Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground
            };
            page.Controls.Add(srcHead);

            // The per-mode DECISIONS control. Clicking flips the source and reveals/hides the manual ID row.
            // x measured off the SOURCE label (hand-set 64 overlapped it by 3px — audit catch).
            int srcX = UiTheme.S(10) + UiLayout.MeasureText("SOURCE", UiTheme.ColHeader) + UiTheme.S(10);
            m.Src = new Button { Text = SystemControlBar.Advisor, Location = new Point(srcX, UiTheme.S(10)), Size = new Size(UiTheme.S(150), UiTheme.SCtl(24)), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat };
            m.Src.FlatAppearance.BorderColor = UiTheme.Border;
            m.Src.Click += (s, e) =>
            {
                bool manualNow = false;
                try { manualNow = string.IsNullOrEmpty(m.GetObj()); } catch { }
                SetSource(m, manual: !manualNow);
            };
            page.Controls.Add(m.Src);

            m.Combo = new ComboBox
            {
                Location = new Point(Math.Max(UiTheme.S(230), srcX + UiTheme.S(158)), UiTheme.S(11)),
                Width = UiTheme.S(170),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = UiTheme.Ui
            };
            UiTheme.StyleCombo(m.Combo);
            foreach (var o in GearObjectives.Objectives)
                m.Combo.Items.Add(o.Name);
            m.Combo.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing || Settings == null) return;
                if (string.IsNullOrEmpty(m.GetObj())) return;   // manual mode: combo is display-only
                try { m.SetObj((string)m.Combo.SelectedItem ?? ""); } catch (Exception ex) { LogDebug($"Loadouts obj: {ex.Message}"); }
                RefreshCard(m);
            };
            page.Controls.Add(m.Combo);

            // Toggle button (design system: green on / red off; checkboxes render unreliably).
            m.Resp = new Button
            {
                Text = "Keep Respawn",
                Location = new Point(UiTheme.S(412), UiTheme.S(10)),
                Size = new Size(MeasureBtn("Keep Respawn"), UiTheme.SCtl(24)),
                Font = UiTheme.Ui,
                FlatStyle = FlatStyle.Flat
            };
            m.Resp.FlatAppearance.BorderColor = UiTheme.Border;
            m.Resp.Click += (s, e) =>
            {
                if (Settings == null) return;
                bool now = false;
                try { now = m.GetResp(); } catch { }
                try { m.SetResp(!now); } catch (Exception ex) { LogDebug($"Loadouts resp: {ex.Message}"); }
                SyncCard(m);
            };
            page.Controls.Add(m.Resp);

            // Loot Hunter: no source/objective concept — the ID row IS the accessory pool, and the
            // freed top row holds the per-type QUOTAS (user rule: choose how many Respawn and how
            // many Drop Chance accessories the advisor allocates; 0/0 = auto blended ranking).
            // Shockwave is a plain item list: SetObj is inert, so its source button could be clicked
            // forever and SyncCard would read back "" and snap it to MANUAL. A control that cannot change
            // the state it displays is a lie with a hover effect — hide it, and name the row for what it is.
            if (m.StaticOnly)
            {
                srcHead.Text = "ITEM LIST";
                m.Src.Visible = false;
                m.Combo.Visible = false;
                m.Resp.Visible = false;
            }

            if (m.LootHunter)
            {
                srcHead.Text = "ACCESSORY POOL";
                m.Src.Visible = false;
                m.Combo.Visible = false;
                m.Resp.Visible = false;

                Label QLbl(string t) => new Label { Text = t, AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
                var pickLbl = QLbl("Advisor picks:");
                _lhResp = new NumericUpDown { Width = UiTheme.S(44), Minimum = 0, Maximum = 20, Font = UiTheme.Ui };
                UiTheme.StyleNum(_lhResp);
                var respLbl = QLbl("× Respawn  +");
                _lhDrop = new NumericUpDown { Width = UiTheme.S(44), Minimum = 0, Maximum = 20, Font = UiTheme.Ui };
                UiTheme.StyleNum(_lhDrop);
                var dropLbl = QLbl("× Drop Chance  (0/0 = optimizer auto · shortfall picks from whole inventory)");
                _lhResp.ValueChanged += (s, e) =>
                {
                    if (_syncing || Settings == null) return;
                    Settings.LootHunterRespawnCount = (int)_lhResp.Value;
                    RefreshCard(m);
                };
                _lhDrop.ValueChanged += (s, e) =>
                {
                    if (_syncing || Settings == null) return;
                    Settings.LootHunterDropCount = (int)_lhDrop.Value;
                    RefreshCard(m);
                };
                foreach (Control c in new Control[] { pickLbl, _lhResp, respLbl, _lhDrop, dropLbl })
                    page.Controls.Add(c);
                UiLayout.Row(UiTheme.S(10) + UiLayout.MeasureText("ACCESSORY POOL", UiTheme.ColHeader) + UiTheme.S(14), UiTheme.S(11), UiTheme.S(6),
                    pickLbl, _lhResp, respLbl, _lhDrop, dropLbl);
            }

            m.Refresh = new Button { Text = "↻", Location = new Point(_hostW - UiTheme.S(46), UiTheme.S(10)), Size = new Size(Math.Max(UiTheme.S(36), UiLayout.BtnWidth("↻")), UiTheme.SCtl(24)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(m.Refresh);
            m.Refresh.Click += (s, e) => RefreshCard(m);
            page.Controls.Add(m.Refresh);

            m.ManualRow = new Panel { Location = new Point(0, UiTheme.S(42)), Size = new Size(_hostW, UiTheme.S(28)), BackColor = UiTheme.Ground, Visible = false };
            page.Controls.Add(m.ManualRow);

            m.ManualRow.Controls.Add(new Label
            {
                Text = "IDs",
                Location = new Point(UiTheme.S(10), UiTheme.S(6)),
                AutoSize = true,
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground
            });
            // Width-aware manual row: IDs box grows with the host; buttons (MEASURED widths — the old
            // hardcoded 126 clipped "Use Current Gear") trail it, row ends 10px inside the host edge.
            if (_wSave == 0) { _wSave = MeasureBtn("Save"); _wUse = MeasureBtn("Use Current Gear"); }
            int idsW = _hostW - (UiTheme.S(40) + UiTheme.S(6) + _wSave + UiTheme.S(8) + _wUse + UiTheme.S(10));
            m.Ids = new TextBox { Location = new Point(UiTheme.S(40), UiTheme.S(3)), Width = idsW, Font = UiTheme.Ui };
            m.ManualRow.Controls.Add(m.Ids);

            m.Save = new Button { Text = "Save", Location = new Point(UiTheme.S(40) + idsW + UiTheme.S(6), UiTheme.S(2)), Size = new Size(_wSave, UiTheme.SCtl(23)), Font = UiTheme.Ui };
            var save = m.Save;
            UiTheme.StyleFlat(save);
            save.Click += (s, e) =>
            {
                if (Settings == null) return;
                var ids = ParseIds(m.Ids.Text);
                try { m.SetStatic(ids); } catch (Exception ex) { LogDebug($"Loadouts save: {ex.Message}"); }
                Log($"Loadouts: {m.Name} manual loadout set ({ids.Length} items)");
                RefreshCard(m);
            };
            m.ManualRow.Controls.Add(save);

            m.UseCur = new Button { Text = "Use Current Gear", Location = new Point(UiTheme.S(40) + idsW + UiTheme.S(6) + _wSave + UiTheme.S(8), UiTheme.S(2)), Size = new Size(_wUse, UiTheme.SCtl(23)), Font = UiTheme.Ui };
            var useCur = m.UseCur;
            UiTheme.StyleFlat(useCur);
            useCur.Click += (s, e) =>
            {
                var ids = LoadoutManager.CurrentGearIds();
                if (ids.Length == 0) return;
                m.Ids.Text = string.Join(", ", ids.Select(x => x.ToString()).ToArray());
                if (Settings == null) return;
                try { m.SetStatic(ids); } catch (Exception ex) { LogDebug($"Loadouts usecur: {ex.Message}"); }
                Log($"Loadouts: {m.Name} manual loadout set from equipped gear ({ids.Length} items)");
                RefreshCard(m);
                RefreshSnapshot(m);
            };
            m.ManualRow.Controls.Add(useCur);

            int fullW = _hostW - UiTheme.S(22);
            int listH = UiTheme.ListH(7);   // the tuned S(180) at the 25px line height
            // DPI-true vertical rhythm (the game's Mono renders 9pt AutoSize text ~26px = LinePitch). A 9pt
            // description label placed only 24px above a bordered ListBox overlapped its top border by ~2px
            // (its opaque Ground box painted over the FixedSingle line) — the bug. textToList clears the
            // ~26px line PLUS the border; listToNext/lineToLine follow the same pitches.
            int headToText = UiTheme.S(24);   // 7.5pt header -> first 9pt line
            int textToList = UiTheme.S(30);   // 9pt line -> bordered ListBox (26px line + border clearance)
            int listToNext = UiTheme.S(12);   // ListBox bottom -> next header
            int lineToLine = UiTheme.LinePitch;   // stacked 9pt lines (26 at the tuning baseline)

            // WILL EQUIP — the REQUESTED set (existing resolution). Header labelled in RefreshCard.
            m.PvHead = new Label { Text = "WILL EQUIP", Location = new Point(UiTheme.S(10), UiTheme.S(78)), AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            page.Controls.Add(m.PvHead);
            int reqY = UiTheme.S(78) + headToText;
            m.ReqHelp = new Label { Text = ReqHelpText(m), Location = new Point(UiTheme.S(10), reqY), AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Faint, BackColor = UiTheme.Ground };
            page.Controls.Add(m.ReqHelp);
            int prevY = reqY + textToList;
            m.Preview = new ListBox { Location = new Point(UiTheme.S(10), prevY), Size = new Size(fullW, listH), Font = UiTheme.Ui, BorderStyle = BorderStyle.FixedSingle };
            UiTheme.StyleList(m.Preview);
            page.Controls.Add(m.Preview);

            // CURRENTLY EQUIPPED — a read-only SNAPSHOT of live gear (LoadoutManager.CurrentGearIds), captured
            // on show / Refresh State. NOT a live monitor, NOT the requested set. Refresh only reads.
            int snapY = m.Preview.Bottom + listToNext;
            m.SnapHead = new Label { Text = "CURRENTLY EQUIPPED — SNAPSHOT", Location = new Point(UiTheme.S(10), snapY), AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            page.Controls.Add(m.SnapHead);
            int wRefresh = MeasureBtn("REFRESH STATE");
            m.RefreshState = new Button { Text = "REFRESH STATE", Size = new Size(wRefresh, UiTheme.SCtl(22)), Location = new Point(fullW + UiTheme.S(10) - wRefresh, snapY - UiTheme.S(2)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(m.RefreshState);
            var mSnap = m;
            m.RefreshState.Click += (s, e) => RefreshSnapshot(mSnap);
            page.Controls.Add(m.RefreshState);
            int snapHelpY = snapY + headToText;
            m.SnapHelp = new Label { Text = "Captured when this page was opened or manually refreshed — it does not update by itself.", Location = new Point(UiTheme.S(10), snapHelpY), AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Faint, BackColor = UiTheme.Ground };
            page.Controls.Add(m.SnapHelp);
            int snapListY = snapHelpY + textToList;
            m.Snapshot = new ListBox { Location = new Point(UiTheme.S(10), snapListY), Size = new Size(fullW, listH), Font = UiTheme.Ui, BorderStyle = BorderStyle.FixedSingle };
            UiTheme.StyleList(m.Snapshot);
            page.Controls.Add(m.Snapshot);

            // Read-only status: per-mode swap gate + the generic current equipment lock.
            int statY = m.Snapshot.Bottom + listToNext;
            m.SwapStatus = new Label { Text = "", Location = new Point(UiTheme.S(10), statY), AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            page.Controls.Add(m.SwapStatus);
            m.LockLine = new Label { Text = "", Location = new Point(UiTheme.S(10), statY + lineToLine), AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Faint, BackColor = UiTheme.Ground };
            page.Controls.Add(m.LockLine);

            page.Height = statY + lineToLine + UiTheme.LinePitch + UiTheme.S(10);
        }

        private static string ReqHelpText(Mode m)
        {
            if (m.LootHunter)
                return "Pool accessories plus the optimizer's best Power/Toughness gear, resolved live at the next Gear Hunt.";
            if (m.StaticOnly)
                return "This daycare set is placed by the existing Money Pit / daycare path — it is not an equipment swap.";
            return "The set the manager will request when this mode next acquires its equipment lock.";
        }

        private void SetSource(Mode m, bool manual)
        {
            if (Settings == null) return;
            try
            {
                if (manual)
                {
                    var cur = m.GetObj();
                    if (!string.IsNullOrEmpty(cur)) m.LastObjective = cur;
                    m.SetObj("");
                }
                else
                {
                    string back = !string.IsNullOrEmpty(m.LastObjective) ? m.LastObjective : (string)m.Combo.SelectedItem;
                    if (string.IsNullOrEmpty(back) && m.Combo.Items.Count > 0) back = (string)m.Combo.Items[0];
                    m.SetObj(back ?? "");
                }
            }
            catch (Exception ex) { LogDebug($"Loadouts source: {ex.Message}"); }
            SyncCard(m);
        }

        private static int[] ParseIds(string text)
        {
            var list = new List<int>();
            foreach (Match match in Regex.Matches(text ?? "", @"\d+"))
                if (int.TryParse(match.Value, out var id) && id > 0 && !list.Contains(id))
                    list.Add(id);
            return list.ToArray();
        }

        public void SyncFromSettings()
        {
            if (Settings == null) return;
            foreach (var m in _modes) SyncCard(m);
        }

        private void SyncCard(Mode m)
        {
            _syncing = true;
            try
            {
                string obj = "";
                try { obj = m.GetObj() ?? ""; } catch { }
                bool manual = string.IsNullOrEmpty(obj);
                if (!manual) m.LastObjective = obj;

                // Canonical DECISIONS vocabulary — per mode, because that is where the strategy actually
                // lives. ADVISOR = objective-driven (the optimizer resolves the set); MANUAL = the item
                // list below. Amber for MANUAL, matching the bars: it is a valid choice, not a fault.
                m.Src.Text = manual ? SystemControlBar.Manual : SystemControlBar.Advisor;
                UiTheme.ApplyState(m.Src, manual ? UiTheme.Energy : UiTheme.Accent, Color.White);
                m.ManualRow.Visible = manual;
                m.Combo.Enabled = !manual;

                string show = !string.IsNullOrEmpty(obj) ? obj : m.LastObjective;
                if (string.IsNullOrEmpty(show) && m.GoldDefault) show = "Gold Drops";
                int idx = show != null ? m.Combo.Items.IndexOf(show) : -1;
                if (idx >= 0) m.Combo.SelectedIndex = idx;

                bool resp = false;
                try { resp = m.GetResp(); } catch { }
                UiTheme.ApplyState(m.Resp, resp ? UiTheme.Cap : UiTheme.Danger, Color.White);

                var ids = new int[0];
                try { ids = m.GetStatic() ?? new int[0]; } catch { }
                m.Ids.Text = string.Join(", ", ids.Where(x => x > 0).Select(x => x.ToString()).ToArray());

                if (m.LootHunter && _lhResp != null)
                {
                    _lhResp.Value = Math.Max(0, Math.Min(20, Settings.LootHunterRespawnCount));
                    _lhDrop.Value = Math.Max(0, Math.Min(20, Settings.LootHunterDropCount));
                }
            }
            finally { _syncing = false; }
            RefreshCard(m);
        }

        // WILL EQUIP preview via the same resolution the swap code uses.
        private void RefreshCard(Mode m)
        {
            try
            {
                if (Settings == null || Main.Character == null) return;

                string obj = "";
                try { obj = m.GetObj() ?? ""; } catch { }
                int[] ids;
                string note;

                if (m.LootHunter)
                {
                    // Same resolution the gear-hunt swap uses: pool accessories + best P/T gear.
                    ids = GearHunter.ResolveLoadout(out var what);
                    note = ids.Length > 0 ? what : "add accessory IDs to the pool";
                }
                else if (string.IsNullOrEmpty(obj))
                {
                    int[] fallback = new int[0];
                    try { fallback = (m.GetStatic() ?? new int[0]).Where(x => x > 0).ToArray(); } catch { }
                    if (m.GoldDefault && fallback.Length == 0)
                    {
                        ids = GearOptimizer.ResolveGoldGear();
                        note = "advisor default: Gold Drops";
                    }
                    else
                    {
                        ids = fallback;
                        note = ids.Length > 0 ? "your manual items" : "nothing configured";
                    }
                }
                else
                {
                    var objective = GearOptimizer.FindObjective(obj);
                    bool resp = false;
                    try { resp = m.GetResp(); } catch { }
                    ids = objective != null ? GearOptimizer.OptimizeIds(objective, resp) : new int[0];
                    note = "optimized live at swap time";
                }

                ids = (ids ?? new int[0]).Where(x => x > 0).Distinct().ToArray();
                // Requested, not equipped. Shockwave is a daycare set, not an equipment swap.
                m.PvHead.Text = (m.StaticOnly
                    ? $"DAYCARE SET ({ids.Length}) · {note}"
                    : $"WILL EQUIP ({ids.Length}) — REQUESTED AT NEXT SWAP · {note}").ToUpperInvariant();
                m.Preview.BeginUpdate();
                m.Preview.Items.Clear();
                foreach (var id in ids)
                    m.Preview.Items.Add($"{ItemNameNice(id)}  (#{id})");
                m.Preview.EndUpdate();
            }
            catch (Exception ex) { LogDebug($"Loadouts preview {m.Name}: {ex.Message}"); }
        }

        // READ-ONLY snapshot of live gear + gate + lock. Reads only (CurrentGearIds / GetLockTypeName /
        // the mode's existing gate setting) — no equip, no lock acquire, no settings mutation. Called on
        // show / mode select / Refresh State / Use Current Gear; never on a timer or per-frame path.
        private void RefreshSnapshot(Mode m)
        {
            try
            {
                if (m.Snapshot == null) return;
                int[] ids = new int[0];
                try { ids = LoadoutManager.CurrentGearIds(); } catch { }
                ids = (ids ?? new int[0]).Where(x => x > 0).ToArray();

                m.SnapHead.Text = $"CURRENTLY EQUIPPED — SNAPSHOT ({ids.Length})";
                m.Snapshot.BeginUpdate();
                m.Snapshot.Items.Clear();
                if (ids.Length == 0)
                    m.Snapshot.Items.Add("(no items in current equipment snapshot)");
                else
                    foreach (var id in ids)
                        m.Snapshot.Items.Add($"{ItemNameNice(id)}  (#{id})");
                m.Snapshot.EndUpdate();

                m.SwapStatus.Text = GateText(m);
                string lockName = "None";
                try { lockName = LockManager.GetLockTypeName(); } catch { }
                m.LockLine.Text = $"CURRENT EQUIPMENT LOCK: {lockName.ToUpperInvariant()}";
            }
            catch (Exception ex) { LogDebug($"Loadouts snapshot {m.Name}: {ex.Message}"); }
        }

        // Each mode's existing equipment-swap gate, READ-ONLY. Shockwave has no single authoritative gate
        // (its set is consumed by the Money Pit / daycare path), so it says so rather than inventing a bool.
        private static string GateText(Mode m)
        {
            bool? g;
            switch (m.Name)
            {
                case "TITAN": g = Settings.SwapTitanLoadouts; break;
                case "GOLD": g = Settings.ManageGoldLoadouts; break;
                case "QUEST": g = Settings.ManageQuestLoadouts; break;
                case "YGGDRASIL": g = Settings.SwapYggdrasilLoadouts; break;
                case "COOKING": g = Settings.ManageCookingLoadouts; break;
                case "LOOT HUNTER": g = Settings.GearHuntEnabled; break;
                default: g = null; break;   // SHOCKWAVE
            }
            if (g == null) return "SWAP STATUS: CONTROLLED BY ITS MANAGER SETTINGS";
            return g.Value
                ? "SWAP STATUS: ENABLED"
                : "SWAP STATUS: DISABLED — this loadout is configured, but its automatic swap won't run.";
        }

        public void RefreshPreviews()
        {
            foreach (var m in _modes)
                if (m.Page != null && m.Page.Visible)
                {
                    RefreshCard(m);
                    RefreshSnapshot(m);
                }
        }
    }
}
