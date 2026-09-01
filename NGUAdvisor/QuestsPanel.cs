using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Systems > QUESTS, Q1 "quest ticket" (user-approved): the running quest as a ticket stub
    // (MAJOR/MINOR badge, zone, drop bar, butter state, capstone-hold line) beside the QUEST BANK
    // meter (banked count, regen bar + next-in, the overfill predictor's verdict). ADVISOR runs the
    // strategy (AdvisorApply.ApplyQuests) + the capstone hold; MANUAL exposes the full rulebook.
    public class QuestsPanel : Panel
    {
        // The two layers, verified: AUTOMATION = Settings.AutoQuest (the execution gate the quest block
        // in Main.Update reads, Main.cs:1015); DECISIONS = Settings.AdvisorQuests (the strategy the
        // advisor reads — QuestManager.cs:72, AdvisorApply.cs:58). Independent fields, ANDed in practice,
        // which is why "AutoQuest off + advisor on" was the state this panel already had to apologise for
        // in prose. The bar owns that explanation now.
        private SystemControlBar _controlBar;
        private Button _refresh;

        private Panel _ticket;
        private Label _badge;
        private Label _questName;
        private Panel _dropOuter;
        private Panel _dropInner;
        private Label _dropText;
        private Label _capstone;

        private Panel _bank;
        private Label _bankCount;
        private Label _bankNext;
        private Panel _bankOuter;
        private Panel _bankInner;
        private Label _bankVerdict;

        private Label _plan;
        private Panel _rules;
        private Button _majors;
        private Button _fullBank;
        private Button _manualMinors;
        private Button _abandon;
        private NumericUpDown _abandonPct;
        private Button _fifty;
        private Button _butterMinor;
        private Button _butterMajor;
        private Button _questGear;
        private ComboBox _combatMode;
        private Button _beast;
        private Button _poolMajors;
        private Button _holdGear;

        private bool _syncing;

        // canvasW: explicit canvas width when hosted in an M1 section column (0 = UiLayout.PanelW).
        public QuestsPanel(int canvasW = 0)
        {
            int W = canvasW > 0 ? canvasW : UiLayout.PanelW;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            // Replaces the old "ADVISOR RUNS QUESTS / MANUAL RULES" button, which showed only the
            // DECISIONS layer and left AUTOMATION invisible on another screen.
            _controlBar = new SystemControlBar(
                W - UiTheme.S(98),   // leaves room for the refresh button on the same row
                () => Settings.AutoQuest, v => Settings.AutoQuest = v,
                () => Settings.AdvisorQuests, v => Settings.AdvisorQuests = v,
                "The advisor runs quests: picks them, butters, banks and abandons.",
                "Your quest rules below drive it; the tool executes them.",
                "Automation is off — the tool will not start or manage quests.");
            _controlBar.Changed += SyncFromSettings;
            _refresh = new Button { Text = "↻", Size = new Size(Math.Max(UiTheme.S(36), UiLayout.BtnWidth("↻")), UiTheme.SCtl(24)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(_refresh);
            _refresh.Click += (s, e) => RefreshView();
            Controls.Add(_controlBar);
            Controls.Add(_refresh);
            UiLayout.Row(UiTheme.S(10), UiTheme.S(10), UiTheme.S(8), _controlBar, _refresh);
            _refresh.Top = UiTheme.S(10) + (SystemControlBar.BarHeight - _refresh.Height) / 2;   // centred on the bar

            // Everything below the bar shifts by its height + an 8px gap.
            int top = UiTheme.S(10) + SystemControlBar.BarHeight + UiTheme.S(8);

            // Ticket stub (left) — gold left edge like a torn ticket. Ticket + bank split the canvas
            // 3:2 (the legacy 372/228 in the 664 canvas); in a narrow M1 column they STACK instead
            // (side-by-side at <560 starves the bank meter's labels).
            bool narrow = W < UiTheme.S(560);
            int contentW = W - UiTheme.S(44);
            int ticketW = narrow ? contentW : contentW * 3 / 5;   // 372 legacy
            int bankW = narrow ? contentW : contentW - ticketW - UiTheme.S(20);
            _ticket = new Panel { Location = new Point(UiTheme.S(10), top), Size = new Size(ticketW, UiTheme.S(100)), BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(_ticket);
            _ticket.Controls.Add(new Panel { Location = new Point(0, 0), Size = new Size(UiTheme.S(4), UiTheme.S(100) - 2), BackColor = UiTheme.Energy });
            _badge = new Label { Text = "", AutoSize = false, Size = new Size(UiTheme.S(64), UiTheme.SHead(18)), Font = UiTheme.Chip, ForeColor = Color.White, BackColor = UiTheme.Faint, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(UiTheme.S(12), UiTheme.S(7)) };
            _questName = new Label { Text = "…", AutoSize = false, Size = new Size(ticketW - UiTheme.S(92), UiTheme.TextH), Font = UiTheme.Bold, ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(84), UiTheme.S(5)) };
            _dropOuter = new Panel { Location = new Point(UiTheme.S(12), UiTheme.S(34)), Size = new Size(ticketW - UiTheme.S(24), UiTheme.S(10)), BackColor = UiTheme.Zebra, BorderStyle = BorderStyle.FixedSingle };
            _dropInner = new Panel { Location = new Point(0, 0), Size = new Size(0, UiTheme.S(10) - 2), BackColor = UiTheme.Energy };
            _dropOuter.Controls.Add(_dropInner);
            _dropText = new Label { Text = "", AutoSize = false, Size = new Size(ticketW - UiTheme.S(24), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(12), UiTheme.S(48)) };
            _capstone = new Label { Text = "", AutoSize = false, Size = new Size(ticketW - UiTheme.S(24), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Energy, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(12), UiTheme.S(72)) };
            _ticket.Controls.Add(_badge);
            _ticket.Controls.Add(_questName);
            _ticket.Controls.Add(_dropOuter);
            _ticket.Controls.Add(_dropText);
            _ticket.Controls.Add(_capstone);

            // Bank meter (right; below the ticket when narrow).
            _bank = new Panel { Location = narrow ? new Point(UiTheme.S(10), top + UiTheme.S(106)) : new Point(UiTheme.S(10) + ticketW + UiTheme.S(10), top), Size = new Size(bankW, UiTheme.S(100)), BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(_bank);
            _bank.Controls.Add(new Label { Text = "QUEST BANK", AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(10), UiTheme.S(6)) });
            _bankCount = new Label { Text = "…", AutoSize = false, Size = new Size(UiTheme.S(90), UiTheme.TextH), Font = UiTheme.Bold, ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(10), UiTheme.S(28)) };
            _bankNext = new Label { Text = "", AutoSize = false, Size = new Size(bankW - UiTheme.S(116), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(104), UiTheme.S(28)) };
            _bankOuter = new Panel { Location = new Point(UiTheme.S(10), UiTheme.S(54)), Size = new Size(bankW - UiTheme.S(22), UiTheme.S(10)), BackColor = UiTheme.Zebra, BorderStyle = BorderStyle.FixedSingle };
            _bankInner = new Panel { Location = new Point(0, 0), Size = new Size(0, UiTheme.S(10) - 2), BackColor = UiTheme.Cap };
            _bankOuter.Controls.Add(_bankInner);
            _bankVerdict = new Label { Text = "", AutoSize = false, Size = new Size(bankW - UiTheme.S(22), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(10), UiTheme.S(70)) };
            _bank.Controls.Add(_bankCount);
            _bank.Controls.Add(_bankNext);
            _bank.Controls.Add(_bankOuter);
            _bank.Controls.Add(_bankVerdict);

            int rulesY = narrow ? top + UiTheme.S(214) : top + UiTheme.S(108);
            _plan = new Label { Text = "", AutoSize = false, Size = new Size(W - UiTheme.S(54), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(10), rulesY + UiTheme.S(4)), Tag = "exclusive" };
            Controls.Add(_plan);

            // Manual rulebook: two measured rows (they WRAP in narrow columns).
            _rules = new Panel { Location = new Point(0, rulesY), Size = new Size(W - UiTheme.S(4), UiTheme.S(66)), BackColor = UiTheme.Ground, Tag = "exclusive" };
            Controls.Add(_rules);
            _majors = MkRule("Majors", () => Settings.AllowMajorQuests = !Settings.AllowMajorQuests);
            _fullBank = MkRule("Full-Bank Guard", () => Settings.QuestsFullBank = !Settings.QuestsFullBank);
            _manualMinors = MkRule("Manual Minors", () => Settings.ManualMinors = !Settings.ManualMinors);
            _abandon = MkRule("Abandon <", () => Settings.AbandonMinors = !Settings.AbandonMinors);
            _abandonPct = new NumericUpDown { Width = UiTheme.S(48), Minimum = 0, Maximum = 100, Font = UiTheme.Ui };
            UiTheme.StyleNum(_abandonPct);
            _abandonPct.ValueChanged += (s, e) => { if (!_syncing && Settings != null) Settings.MinorAbandonThreshold = (int)_abandonPct.Value; };
            var pctLbl = new Label { Text = "%", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            _rules.Controls.Add(_majors);
            _rules.Controls.Add(_fullBank);
            _rules.Controls.Add(_manualMinors);
            _rules.Controls.Add(_abandon);
            _rules.Controls.Add(_abandonPct);
            _rules.Controls.Add(pctLbl);
            int rulesRow1 = UiLayout.WrapRow(UiTheme.S(10), UiTheme.S(4), UiTheme.S(8), _rules.Width - UiTheme.S(10), UiTheme.S(30),
                new Control[] { _majors, _fullBank, _manualMinors, _abandon, _abandonPct, pctLbl });

            _fifty = MkRule("50-Item Minors", () => Settings.FiftyItemMinors = !Settings.FiftyItemMinors);
            _butterMinor = MkRule("Butter Minors", () => Settings.UseButterMinor = !Settings.UseButterMinor);
            _butterMajor = MkRule("Butter Majors", () => Settings.UseButterMajor = !Settings.UseButterMajor);
            _questGear = MkRule("Quest Gear", () => Settings.ManageQuestLoadouts = !Settings.ManageQuestLoadouts);
            _rules.Controls.Add(_fifty);
            _rules.Controls.Add(_butterMinor);
            _rules.Controls.Add(_butterMajor);
            _rules.Controls.Add(_questGear);
            int rulesRow2 = UiLayout.WrapRow(UiTheme.S(10), rulesRow1 + UiTheme.S(2), UiTheme.S(8), _rules.Width - UiTheme.S(10), UiTheme.S(30),
                new Control[] { _fifty, _butterMinor, _butterMajor, _questGear });
            _rules.Height = rulesRow2 + UiTheme.S(2);

            // Re-homed from the retired Old Quests page (Phase B): quest-zone combat style. Sits
            // below whichever of plan/rules is taller (they're exclusive views sharing the slot).
            var cmLbl = new Label { Text = "Quest combat", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            _combatMode = new LineComboBox { Width = UiTheme.S(110), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui };
            UiTheme.StyleCombo(_combatMode);
            _combatMode.Items.AddRange(new object[] { "Idle", "Snipe", "Defensive", "Offensive" });
            _combatMode.SelectedIndexChanged += (s, e) => { if (!_syncing && Settings != null && _combatMode.SelectedIndex >= 0) Settings.QuestCombatMode = _combatMode.SelectedIndex; };
            _beast = MkRule("Beast Mode", () => Settings.QuestBeastMode = !Settings.QuestBeastMode);
            // Strategy toggles live on the always-visible row (both advisor + manual modes):
            // Pool Majors = bank to cap then burst the whole bank; Hold for Gear = the opt-in
            // capstone hold (default OFF — a held major reads as a hang at the quest ticket).
            _poolMajors = MkRule("Pool Majors", () => Settings.PoolMajorQuests = !Settings.PoolMajorQuests);
            _holdGear = MkRule("Hold for Gear", () => Settings.QuestHoldForGear = !Settings.QuestHoldForGear);
            Controls.Add(cmLbl);
            Controls.Add(_combatMode);
            Controls.Add(_beast);
            Controls.Add(_poolMajors);
            Controls.Add(_holdGear);
            UiLayout.WrapRow(UiTheme.S(10), Math.Max(top + UiTheme.S(182), _rules.Bottom + UiTheme.S(8)), UiTheme.S(8), W - UiTheme.S(20), UiTheme.S(30),
                new Control[] { cmLbl, _combatMode, _beast, _poolMajors, _holdGear });

            VisibleChanged += (s, e) => { if (Visible) RefreshView(); };
            SyncFromSettings();
        }

        private static Button MkBtn(string text)
        {
            var b = new Button { Text = text, Size = new Size(UiLayout.BtnWidth(text), UiTheme.SCtl(24)), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat };
            b.FlatAppearance.BorderColor = UiTheme.Border;
            return b;
        }

        private Button MkRule(string text, Action toggle)
        {
            var b = MkBtn(text);
            b.Click += (s, e) =>
            {
                if (Settings == null) return;
                try { toggle(); } catch (Exception ex) { LogDebug($"Quest rule: {ex.Message}"); }
                SyncFromSettings();
            };
            return b;
        }

        public void SyncFromSettings()
        {
            if (Settings == null) return;
            _syncing = true;
            try
            {
                // Reflects both layers, incl. a flip made from the Settings grid or a settings reload.
                // Sync() never raises Changed, so this cannot recurse.
                _controlBar?.Sync();

                bool advisor = Settings.AdvisorQuests;
                // The plan sentence is hidden when the advisor is idle: the bar states WHY (once), and a
                // plan for work that cannot run would be a lie. The rulebook is the manual-mode view.
                _plan.Visible = advisor && Settings.AutoQuest;
                _rules.Visible = !advisor;

                UiTheme.ApplyState(_majors, Settings.AllowMajorQuests ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_fullBank, Settings.QuestsFullBank ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_manualMinors, Settings.ManualMinors ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_abandon, Settings.AbandonMinors ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_fifty, Settings.FiftyItemMinors ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_butterMinor, Settings.UseButterMinor ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_butterMajor, Settings.UseButterMajor ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_questGear, Settings.ManageQuestLoadouts ? UiTheme.Cap : UiTheme.Danger, Color.White);
                int pct = Math.Max(0, Math.Min(100, Settings.MinorAbandonThreshold));
                _abandonPct.Value = pct;
                _abandonPct.Enabled = Settings.AbandonMinors;
                int cm = Settings.QuestCombatMode;
                if (cm >= 0 && cm < _combatMode.Items.Count) _combatMode.SelectedIndex = cm;
                UiTheme.ApplyState(_beast, Settings.QuestBeastMode ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_poolMajors, Settings.PoolMajorQuests ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_holdGear, Settings.QuestHoldForGear ? UiTheme.Cap : UiTheme.Danger, Color.White);
            }
            finally { _syncing = false; }
            RefreshView();
        }

        private void RefreshView()
        {
            try
            {
                var c = Main.Character;
                if (c == null || Settings == null) return;
                var q = c.beastQuest;
                var qc = c.beastQuestController;

                // Ticket.
                if (q.inQuest)
                {
                    bool minor = q.reducedRewards;
                    _badge.Text = minor ? "MINOR" : "MAJOR";
                    _badge.BackColor = minor ? UiTheme.Cap : UiTheme.Energy;
                    string zone = "?";
                    try { ZoneHelpers.ZoneList.TryGetValue(qc.curQuestZone(), out zone); } catch { }
                    UiLayout.FitInto(_questName, zone ?? "?");
                    double frac = q.targetDrops > 0 ? Math.Min(1.0, q.curDrops / (double)q.targetDrops) : 0;
                    _dropInner.Width = (int)((_dropOuter.Width - 2) * frac);
                    string butter = q.usedButter ? "butter: USED"
                        : (minor ? Settings.UseButterMinor : Settings.UseButterMajor) ? "butter: armed" : "butter: off";
                    string mode = minor && q.idleMode ? " · idle" : " · fighting";
                    UiLayout.FitInto(_dropText, $"{q.curDrops} / {q.targetDrops} drops · {butter}{mode}");
                    UiLayout.FitInto(_capstone, QuestManager.CapstoneItem != null
                        ? $"HOLDING — maxing {QuestManager.CapstoneItem}"
                        : "");
                }
                else
                {
                    _badge.Text = "NONE";
                    _badge.BackColor = UiTheme.Faint;
                    UiLayout.FitInto(_questName, "No quest running");
                    _dropInner.Width = 0;
                    // Ticket status, not a second dependency warning — but it must not carry the retired
                    // "Auto Quest" name for the automation layer.
                    UiLayout.FitInto(_dropText, Settings.AutoQuest ? "next quest starts automatically" : "no quest — automation is off");
                    UiLayout.FitInto(_capstone, "");
                }

                // Bank.
                int banked = 0, maxBank = 0;
                float thr = 1, timer = 0;
                try
                {
                    banked = q.curBankedQuests;
                    maxBank = qc.maxBankedQuests();
                    thr = qc.timerThreshold();
                    timer = (float)q.dailyQuestTimer.totalseconds;
                }
                catch { }
                UiLayout.FitInto(_bankCount, $"{banked} / {maxBank}");
                double into = thr > 0 ? timer % thr : 0;
                double next = Math.Max(0, thr - into);
                UiLayout.FitInto(_bankNext, banked >= maxBank
                    ? "FULL"
                    : $"next in {(next >= 3600 ? $"{next / 3600:0.#}h" : $"{next / 60:0}m")}");
                _bankInner.Width = (int)((_bankOuter.Width - 2) * (thr > 0 ? into / thr : 0));
                bool overfill = QuestManager.BankOverfill;
                bool pooling = Settings.PoolMajorQuests;
                bool bursting = pooling && Settings.QuestBurstActive;
                UiLayout.FitInto(_bankVerdict, bursting ? "BURST: burning the bank"
                    : pooling ? $"pooling — burst at {maxBank}"
                    : overfill ? "overfill: FORCING QUESTS" : "overfill guard: safe");
                _bankVerdict.ForeColor = bursting ? UiTheme.Energy : overfill && !pooling ? UiTheme.Danger : UiTheme.Muted;

                // Plan sentence (advisor mode, and only when it can actually run — the old
                // "Auto Quest is OFF (Settings tab) — advisor is idle." line lived here and is now the
                // control bar's job. One idle explanation, not two competing ones.)
                if (Settings.AdvisorQuests && Settings.AutoQuest)
                {
                    string plan;
                    bool hunting = false;
                    try { hunting = GearHunter.Active; } catch { }
                    if (QuestManager.CapstoneItem != null) plan = $"Plan: max {QuestManager.CapstoneItem}, turn in, then {(banked > 0 ? "next banked major" : "idle minors")}.";
                    else if (bursting) plan = $"Plan: BURST — chain {(q.inQuest && !q.reducedRewards ? "this major and " : "")}{banked} banked major{(banked == 1 ? "" : "s")}{(hunting ? " (paused: gear hunt)" : "")}.";
                    else if (q.inQuest && !q.reducedRewards) plan = $"Plan: finish this major → {(banked > 0 ? $"{banked} more banked" : "idle minors while sniping resumes")}.";
                    else if (pooling) plan = $"Plan: pooling majors — {banked}/{maxBank} banked; burst when full{(hunting ? " and the gear hunt ends" : "")}.";
                    else if (banked > 0) plan = $"Plan: {banked} banked major{(banked > 1 ? "s" : "")} queued — starting when current quest clears.";
                    else plan = "Plan: idle minors while sniping; majors start as the bank fills.";
                    UiLayout.FitOrGrow(_plan, plan);
                }
            }
            catch (Exception ex) { LogDebug($"Quests panel: {ex.Message}"); }
        }
    }
}
