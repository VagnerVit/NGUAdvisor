using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Systems > BLOOD (layout C: status hero + compact inputs, user pick). Consolidates the legacy
    // manual blood-spell inputs (re-homed from the Settings tab) with a LIVE read of what the blood
    // ADVISOR (BloodPlanner) is doing: Iron Pill worth + timing, and which sink it routes blood into
    // (Number Boost / Spaghetti loot / Counterfeit Gold). Crimson = the Blood system identity color.
    public class BloodPanel : Panel
    {
        private static readonly Color Blood = ColorTranslator.FromHtml("#9E2B36");

        private readonly int _w;
        // The two layers, verified: AUTOMATION = Settings.CastBloodSpells — a REAL execution gate, not a
        // manual-mode knob: AdvisorApply.ApplyBlood() opens `if (!CastBloodSpells) return;` (:150), and
        // BloodMagicManager:35, CustomAllocation:264 and BaseRebirth:223 all gate on it too. DECISIONS =
        // Settings.AdvisorBlood (AdvisorApply:59, CustomAllocation:285). The old panel showed both as
        // unrelated buttons — "MANAGED" up top, "Cast Blood Spells" down in the inputs — and never said
        // that the first does nothing without the second.
        private SystemControlBar _controlBar;
        private Button _swap;                    // AutoSpellSwap (manual %-cap path; NOT one of the layers)
        private Button _pillRb, _guffARb, _guffBRb;
        private Button _refresh;
        private Label _bloodTotal, _pillStatus;
        private Panel _card, _barOuter, _fill;
        private NumericUpDown _guffAThr, _guffBThr;
        // Whether a spell takes part in the equal split.
        private LineComboBox _spagMode, _goldMode;
        private Label _spagStat, _goldStat, _numStat;
        private Label _spagWhy, _goldWhy, _numWhy;   // WHY this sink is / isn't taking blood right now
        private Label _budgetLine;                   // the run's blood the plan splits
        private bool _syncing;

        public BloodPanel(int canvasW = 0)
        {
            _w = canvasW > 0 ? canvasW : UiLayout.PanelW;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;
            AutoScroll = true;
            Build();
            VisibleChanged += (s, e) => { if (Visible) RefreshStatus(); };
            SyncFromSettings();
        }

        private static Button MkBtn(string text)
        {
            var b = new Button { Text = text, Size = new Size(UiLayout.BtnWidth(text), UiTheme.SCtl(24)), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat };
            b.FlatAppearance.BorderColor = UiTheme.Border;
            return b;
        }

        private Button MkToggle(string text, Action onClick)
        {
            var b = MkBtn(text);
            b.Click += (s, e) => { if (Settings == null) return; onClick(); SyncFromSettings(); };
            Controls.Add(b);
            return b;
        }

        private Label MkHead(string text, int x, int y)
        {
            var l = new Label { Text = text, AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(x, y) };
            Controls.Add(l);
            return l;
        }

        // Inline "label [numeric]" that advances a running x cursor. Ints only (NumericUpDown).
        private NumericUpDown MkNum(string label, ref int cx, int y, int min, int max, Action<decimal> set, int width = 82)
        {
            Controls.Add(new Label { Text = label, AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(cx, y + UiTheme.S(4)) });
            cx += UiLayout.MeasureText(label, UiTheme.Ui) + UiTheme.S(6);
            int w = UiTheme.S(width);
            var n = new NumericUpDown { Location = new Point(cx, y), Width = w, Minimum = min, Maximum = max, Font = UiTheme.Ui };
            UiTheme.StyleNum(n);
            n.ValueChanged += (s, e) => { if (_syncing || Settings == null) return; try { set(n.Value); } catch (Exception ex) { LogDebug($"Blood num '{label}': {ex.Message}"); } };
            Controls.Add(n);
            cx += w + UiTheme.S(18);
            return n;
        }

        // Sink-row geometry: caption | on/off dropdown | live status, with a muted WHY line under it.
        private const int SinkCapX = 10, SinkModeX = 214;
        private static int SinkStatX => UiTheme.S(SinkModeX) + UiTheme.S(152);

        private void MkSinkCaption(string text, int y)
        {
            Controls.Add(new Label { Text = text, AutoSize = false, Size = new Size(UiTheme.S(198), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(SinkCapX), y + UiTheme.S(4)) });
        }

        // LineComboBox, not ComboBox: Mono snaps a plain dropdown back to a 96-DPI height (ui-infra.md).
        private LineComboBox MkMode(int y, Action<SinkMode> set)
        {
            var cb = new LineComboBox { Location = new Point(UiTheme.S(SinkModeX), y), Width = UiTheme.S(138), Font = UiTheme.Ui, DropDownStyle = ComboBoxStyle.DropDownList };
            cb.Items.AddRange(new object[] { "Off", "Equal share" });
            UiTheme.StyleCombo(cb);
            cb.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing || Settings == null || cb.SelectedIndex < 0) return;
                try { set((SinkMode)cb.SelectedIndex); } catch (Exception ex) { LogDebug($"Blood mode: {ex.Message}"); }
                SyncFromSettings();
            };
            Controls.Add(cb);
            return cb;
        }

        private Label MkSinkStatus(int y)
        {
            var l = new Label
            {
                Text = "",
                AutoSize = false,
                Size = new Size(Math.Max(UiTheme.S(140), _w - SinkStatX - UiTheme.S(30)), UiTheme.TextH),
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground,
                Location = new Point(SinkStatX, y + UiTheme.S(4))
            };
            Controls.Add(l);
            return l;
        }

        // The WHY line: the constraint that stops this sink's planned share, in plain words.
        private Label MkSinkWhy(int y)
        {
            var l = new Label
            {
                Text = "",
                AutoSize = false,
                Size = new Size(_w - UiTheme.S(54), UiTheme.HeadH),
                Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(SinkCapX + 4), y)
            };
            Controls.Add(l);
            return l;
        }

        private void Build()
        {
            // Top row (the established convention): the control bar owns the system state; the compact
            // panel-level readout and action ride beside it. Width leaves room for both plus a margin —
            // an AutoScroll host that lays out to its full width summons a horizontal scrollbar.
            _controlBar = new SystemControlBar(
                Math.Max(UiTheme.S(300), _w - UiTheme.S(312)),
                () => Settings.CastBloodSpells, v => Settings.CastBloodSpells = v,
                () => Settings.AdvisorBlood, v => Settings.AdvisorBlood = v,
                "Advisor casts the spells.",
                "Casts on your thresholds.",
                "Off — no blood spells cast.");
            _controlBar.Changed += SyncFromSettings;
            Controls.Add(_controlBar);

            _bloodTotal = new Label { Text = "BLOOD …", AutoSize = false, Size = new Size(UiTheme.S(220), UiTheme.TextH), Font = UiTheme.Bold, ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground };
            Controls.Add(_bloodTotal);
            _refresh = new Button { Text = "↻", Size = new Size(Math.Max(UiTheme.S(36), UiLayout.BtnWidth("↻")), UiTheme.SCtl(24)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(_refresh);
            _refresh.Click += (s, e) => RefreshStatus();
            Controls.Add(_refresh);
            UiLayout.Row(UiTheme.S(10), UiTheme.S(10), UiTheme.S(8), _controlBar, _bloodTotal, _refresh);
            // Centre the companions on the bar's 64px row rather than letting them ride its top edge.
            _bloodTotal.Top = UiTheme.S(10) + (SystemControlBar.BarHeight - _bloodTotal.Height) / 2;
            _refresh.Top = UiTheme.S(10) + (SystemControlBar.BarHeight - _refresh.Height) / 2;

            int top = UiTheme.S(10) + SystemControlBar.BarHeight + UiTheme.S(8);

            // Iron Pill card: status line + cooldown bar (crimson identity strip).
            _card = new Panel { Location = new Point(UiTheme.S(10), top), Size = new Size(_w - UiTheme.S(40), UiTheme.S(70)), BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle };
            var strip = new Panel { Location = new Point(0, 0), Size = new Size(UiTheme.S(4), UiTheme.S(68)), BackColor = Blood };
            _pillStatus = new Label { Text = "IRON PILL …", AutoSize = false, Size = new Size(_w - UiTheme.S(90), UiTheme.TextH), Font = UiTheme.Bold, ForeColor = Blood, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(12), UiTheme.S(8)) };
            _barOuter = new Panel { Location = new Point(UiTheme.S(12), UiTheme.S(34)), Size = new Size(_w - UiTheme.S(68), UiTheme.S(24)), BackColor = UiTheme.Zebra, BorderStyle = BorderStyle.FixedSingle };
            _fill = new Panel { Location = new Point(0, 0), Size = new Size(0, UiTheme.S(22)), BackColor = Blood };
            _barOuter.Controls.Add(_fill);
            _card.Controls.Add(strip);
            _card.Controls.Add(_pillStatus);
            _card.Controls.Add(_barOuter);
            _card.Height = Math.Max(_card.Height, _barOuter.Bottom + UiTheme.S(8));
            strip.Height = _card.Height - 2;
            Controls.Add(_card);
            int helpW = UiLayout.MeasureText("?", UiTheme.Bold) + UiTheme.S(8);
            var pillHelp = new Label { Text = "?", AutoSize = false, Font = UiTheme.Bold, ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Cursor = Cursors.Help, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(_card.Width - UiTheme.S(12) - helpW, _pillStatus.Top, helpW, Math.Max(_pillStatus.Height, UiTheme.SText(22))) };
            _card.Controls.Add(pillHelp);
            _pillStatus.Width = pillHelp.Left - UiTheme.S(4) - _pillStatus.Left;
            UiLayout.AttachHelp(pillHelp, "The bar is the pill's cooldown. It is skipped while its gain is under 10 % of your base adventure power, and while the cooldown outlasts the run.");

            int y = _card.Bottom + UiTheme.S(10);
            MkHead("INPUTS", UiTheme.S(10), y);
            y += UiTheme.HeadPitch;
            _pillRb = MkToggle("Pill on Rebirth", () => Settings.IronPillOnRebirth = !Settings.IronPillOnRebirth);
            _guffARb = MkToggle("Guff A on Rebirth", () => Settings.BloodMacGuffinAOnRebirth = !Settings.BloodMacGuffinAOnRebirth);
            _guffBRb = MkToggle("Guff B on Rebirth", () => Settings.BloodMacGuffinBOnRebirth = !Settings.BloodMacGuffinBOnRebirth);
            // Last in the row: it only exists while AUTOMATION is off (Main's manual path), so hiding it
            // leaves no gap.
            _swap = MkToggle("Auto Spell Swap", () => Settings.AutoSpellSwap = !Settings.AutoSpellSwap);
            UiLayout.Tip(_swap, "Manual mode: keeps each spell below the % you set in Settings.");
            y = UiLayout.Row(UiTheme.S(10), y, UiTheme.S(8), _pillRb, _guffARb, _guffBRb, _swap) + UiTheme.S(8);

            // No "Pill ≥" input: IronPillThreshold is dead — the advisor casts the pill on
            // BloodPlanner timing (CastIronNow), nothing reads a manual blood threshold anymore.
            int cx = UiTheme.S(10);
            _guffAThr = MkNum("Guff A ≥", ref cx, y, 0, 100000, v => Settings.BloodMacGuffinAThreshold = (int)v);
            _guffBThr = MkNum("Guff B ≥", ref cx, y, 0, 100000, v => Settings.BloodMacGuffinBThreshold = (int)v);
            y += UiTheme.LinePitch + UiTheme.S(12);

            // SPELLS. The run's blood is split equally between the enabled spells (BloodRouter.Plan); the
            // pool goes to whichever spell is still short of its share.
            var spellsHead = MkHead("SPELLS", UiTheme.S(10), y);
            UiLayout.HelpMark(this, spellsHead,
                "This run's blood (in the spells, on hand, and the income still to come by the rebirth) is split " +
                "equally between the enabled spells. Counterfeit and Spaghetti only take whole +1 % steps: blood " +
                "pools until the next step is paid for, and what their share cannot turn into a step goes to NUMBER.");
            y += UiTheme.HeadPitch;
            _budgetLine = new Label { Text = "", AutoSize = false, Size = new Size(_w - UiTheme.S(54), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(10), y) };
            Controls.Add(_budgetLine);

            // Row pitch is DERIVED from the two lines a row holds (a value line + a WHY line), never a
            // tuned constant — that is what clipped stacked lines at 200 % scaling before.
            int rowPitch = UiTheme.LinePitch + UiTheme.HeadPitch;
            y += UiTheme.LinePitch;
            MkSinkCaption("Spaghetti (DC)", y);
            _spagMode = MkMode(y, m => Settings.BloodWantSpaghetti = m == SinkMode.On);
            _spagStat = MkSinkStatus(y);
            _spagWhy = MkSinkWhy(y + UiTheme.LinePitch);

            y += rowPitch;
            MkSinkCaption("Counterfeit (GPS)", y);
            _goldMode = MkMode(y, m => Settings.BloodWantCounterfeit = m == SinkMode.On);
            _goldStat = MkSinkStatus(y);
            _goldWhy = MkSinkWhy(y + UiTheme.LinePitch);

            // NUMBER has no dropdown: it always takes its share whenever a rebirth will cash it.
            y += rowPitch;
            MkSinkCaption("NUMBER", y);
            _numStat = MkSinkStatus(y);
            _numWhy = MkSinkWhy(y + UiTheme.LinePitch);
        }

        private static string Fmt(double v) => NumberFormatter.Abbrev(v);

        private static string SinkStatus(SinkPlan s)
        {
            string now = $"+{s.NowPct}% · {Fmt(s.Invested)}";
            return s.Mode == SinkMode.Off ? now : now + $"  →  +{s.TargetPct}% · {Fmt(s.TargetBlood)}";
        }

        // Blood is cast only in whole +1 % steps, so the line says what the pool is collecting for.
        private static string SinkWhy(bool routing, SinkPlan s, double pool)
        {
            if (s.Mode == SinkMode.Off) return "off";
            if (routing) return $"pooling {Fmt(Math.Min(pool, s.NextStepCost))} / {Fmt(s.NextStepCost)}";
            if (s.Deficit > 0) return $"next +{s.NowPct + 1}%: {Fmt(s.NextStepCost)}";
            return "at share";
        }

        public void SyncFromSettings()
        {
            if (Settings == null) return;
            _syncing = true;
            try
            {
                // Reflects both layers, incl. a flip made from the ADVICE panel's blood chip (the other
                // reachable writer of AdvisorBlood) or a settings reload — SettingsForm.UpdateFromSettings
                // calls this method. Sync() never raises Changed, so this cannot recurse. It is NOT in
                // RefreshStatus: the bar stays out of the per-tick path entirely.
                _controlBar?.Sync();

                // Main only runs Auto Spell Swap while CastBloodSpells is OFF — with automation on it is a
                // dead switch, so it is not shown at all.
                _swap.Visible = !Settings.CastBloodSpells;
                UiTheme.ApplyState(_swap, Settings.AutoSpellSwap ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_pillRb, Settings.IronPillOnRebirth ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_guffARb, Settings.BloodMacGuffinAOnRebirth ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_guffBRb, Settings.BloodMacGuffinBOnRebirth ? UiTheme.Cap : UiTheme.Danger, Color.White);
                _guffAThr.Value = Clamp(_guffAThr, Settings.BloodMacGuffinAThreshold);
                _guffBThr.Value = Clamp(_guffBThr, Settings.BloodMacGuffinBThreshold);
                var spagMode = BloodPlanner.Mode(false);
                var goldMode = BloodPlanner.Mode(true);
                _spagMode.SelectedIndex = (int)spagMode;
                _goldMode.SelectedIndex = (int)goldMode;
            }
            catch (Exception e) { LogDebug($"Blood sync: {e.Message}"); }
            finally { _syncing = false; }
            RefreshStatus();
        }

        private static decimal Clamp(NumericUpDown n, decimal v) => v < n.Minimum ? n.Minimum : (v > n.Maximum ? n.Maximum : v);

        // Live status refresh: called on show, refresh button, sync, and the periodic UpdateStatus tick.
        public void RefreshStatus()
        {
            if (!Visible) return;
            try
            {
                var c = Main.Character;
                double blood = 0;
                try { blood = c.bloodMagic.bloodPoints; } catch { }
                UiLayout.FitInto(_bloodTotal, $"BLOOD {Fmt(blood)}");

                var plan = BloodPlanner.Analyze();
                BloodPlanner.FillRouting(ref plan);

                // Bar = COOLDOWN charge toward ready (full = castable). The advisor pools by TIME,
                // not a blood target — the old denominator was Settings.IronPillThreshold, the dead
                // manual-mode knob nothing casts on anymore (user-reported "1.96Qa/3K"). Readout =
                // what casting the current pool would grant.
                double frac = plan.Known && plan.CdTotalSec > 0
                    ? Math.Max(0, Math.Min(1, 1.0 - plan.CdLeftSec / plan.CdTotalSec)) : 0;
                _fill.Width = (int)((_barOuter.Width - 2) * frac);
                _fill.BackColor = plan.Known && (!plan.PillWorthwhile || plan.UnreachableThisRun) ? UiTheme.Faint : Blood;

                string status;
                if (!plan.Known) status = "IRON PILL";
                else if (!plan.PillWorthwhile) status = "IRON PILL — skip";
                else if (plan.UnreachableThisRun) status = "IRON PILL — not before rebirth";
                else if (plan.CastIronNow) status = "IRON PILL — CAST NOW";
                else if (plan.PoolForPill) status = "IRON PILL — pooling";
                else status = "IRON PILL";
                if (plan.Known && plan.PillPowerNow > 0) status += $" · +{plan.PillPowerNow:N0} adv";
                UiLayout.FitInto(_pillStatus, status);

                // Sink rows: every number comes from the one plan the routing follows, so the panel and
                // the toggles can never disagree.
                double rp = 1;
                try { rp = c.bloodMagic.rebirthPower; } catch { }
                if (plan.RouteKnown)
                {
                    BudgetPlan b = plan.Budget;
                    UiLayout.FitInto(_budgetLine, $"Run blood {Fmt(b.TotalBlood)} · {Fmt(b.Share)} per spell");
                    UiLayout.FitInto(_spagStat, SinkStatus(b.Loot));
                    UiLayout.FitInto(_goldStat, SinkStatus(b.Gold));
                    UiLayout.FitInto(_spagWhy, SinkWhy(plan.WantLoot, b.Loot, blood));
                    UiLayout.FitInto(_goldWhy, SinkWhy(plan.WantGold, b.Gold, blood));
                    UiLayout.FitInto(_numStat, $"{Fmt(b.NumberInvested)}  →  {Fmt(b.NumberTarget)}");
                }
                else
                {
                    UiLayout.FitInto(_budgetLine, "locked until boss 37");
                    UiLayout.FitInto(_spagStat, "");
                    UiLayout.FitInto(_goldStat, "");
                    UiLayout.FitInto(_spagWhy, "");
                    UiLayout.FitInto(_goldWhy, "");
                    UiLayout.FitInto(_numStat, $"now x{Fmt(rp)}");
                }
                UiLayout.FitInto(_numWhy, plan.PoolForPill ? "paused for the Iron Pill" : plan.WantRebirth ? "receiving" : "");

            }
            catch (Exception e) { LogDebug($"Blood panel: {e.Message}"); }
        }
    }
}
