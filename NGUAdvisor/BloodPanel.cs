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
        private Label _bloodTotal, _pillStatus, _advice;
        private Panel _card, _barOuter, _fill, _routeChips;
        private Label _cNum, _cLoot, _cGold;     // route chips: created ONCE, recolored in place (never per-tick churn)
        private NumericUpDown _guffAThr, _guffBThr, _spag, _counter;
        private TextBox _numberThr;
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

        private void MkHead(string text, int x, int y)
        {
            Controls.Add(new Label { Text = text, AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(x, y) });
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

        private void Build()
        {
            // Top row (the established convention): the control bar owns the system state; the compact
            // panel-level readout and action ride beside it. Width leaves room for both plus a margin —
            // an AutoScroll host that lays out to its full width summons a horizontal scrollbar.
            _controlBar = new SystemControlBar(
                Math.Max(UiTheme.S(300), _w - UiTheme.S(312)),
                () => Settings.CastBloodSpells, v => Settings.CastBloodSpells = v,
                () => Settings.AdvisorBlood, v => Settings.AdvisorBlood = v,
                "The advisor routes blood: pill timing, and which spell gets the pool.",
                "Your thresholds below drive it; the tool casts on your rules.",
                "Automation is off — the tool will not cast blood spells.");
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

            // Everything below the bar shifts by its height + an 8px gap.
            int top = UiTheme.S(10) + SystemControlBar.BarHeight + UiTheme.S(8);

            // Hero card: IRON PILL status + a pooling bar + routing chips (crimson identity strip).
            _card = new Panel { Location = new Point(UiTheme.S(10), top), Size = new Size(_w - UiTheme.S(40), UiTheme.S(96)), BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle };
            var strip = new Panel { Location = new Point(0, 0), Size = new Size(UiTheme.S(4), UiTheme.S(94)), BackColor = Blood };
            _pillStatus = new Label { Text = "IRON PILL …", AutoSize = false, Size = new Size(_w - UiTheme.S(60), UiTheme.TextH), Font = UiTheme.Bold, ForeColor = Blood, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(12), UiTheme.S(8)) };
            _barOuter = new Panel { Location = new Point(UiTheme.S(12), UiTheme.S(34)), Size = new Size(_w - UiTheme.S(68), UiTheme.S(28)), BackColor = UiTheme.Zebra, BorderStyle = BorderStyle.FixedSingle };
            _fill = new Panel { Location = new Point(0, 0), Size = new Size(0, UiTheme.S(26)), BackColor = Blood };
            _barOuter.Controls.Add(_fill);
            _routeChips = new Panel { Location = new Point(UiTheme.S(12), UiTheme.S(66)), Size = new Size(_w - UiTheme.S(68), UiTheme.S(22)), BackColor = UiTheme.Surface };
            _cNum = MakeChip("▶ NUMBER BOOST");
            _cLoot = MakeChip("SPAGHETTI (loot)");
            _cGold = MakeChip("COUNTERFEIT GOLD");
            int chx = 0;
            foreach (var ch in new[] { _cNum, _cLoot, _cGold }) { ch.Location = new Point(chx, 0); ch.Visible = false; _routeChips.Controls.Add(ch); chx += ch.Width + UiTheme.S(6); }
            // The chips are floored at the measured header line — the 22px strip would clip them.
            _routeChips.Height = Math.Max(_routeChips.Height, _cNum.Height + UiTheme.S(2));
            _card.Controls.Add(strip);
            _card.Controls.Add(_pillStatus);
            _card.Controls.Add(_barOuter);
            _card.Controls.Add(_routeChips);
            Controls.Add(_card);

            // INPUTS: manual auto-cast toggles + thresholds (moved from the Settings tab). "Cast Blood
            // Spells" is GONE from this row — it was never an input, it was the AUTOMATION layer wearing
            // an input's clothes, and it now lives in the bar where its dependency can be stated.
            MkHead("INPUTS", UiTheme.S(10), top + UiTheme.S(106));
            _swap = MkToggle("Auto Spell Swap", () => Settings.AutoSpellSwap = !Settings.AutoSpellSwap);
            _pillRb = MkToggle("Pill on Rebirth", () => Settings.IronPillOnRebirth = !Settings.IronPillOnRebirth);
            _guffARb = MkToggle("Guff A on Rebirth", () => Settings.BloodMacGuffinAOnRebirth = !Settings.BloodMacGuffinAOnRebirth);
            _guffBRb = MkToggle("Guff B on Rebirth", () => Settings.BloodMacGuffinBOnRebirth = !Settings.BloodMacGuffinBOnRebirth);
            UiLayout.Row(UiTheme.S(10), top + UiTheme.S(130), UiTheme.S(8), _swap, _pillRb, _guffARb, _guffBRb);

            // No "Pill ≥" input: IronPillThreshold is dead — the advisor casts the pill on
            // BloodPlanner timing (CastIronNow), nothing reads a manual blood threshold anymore.
            int cx = UiTheme.S(10);
            _guffAThr = MkNum("Guff A ≥", ref cx, top + UiTheme.S(166), 0, 100000, v => Settings.BloodMacGuffinAThreshold = (int)v);
            _guffBThr = MkNum("Guff B ≥", ref cx, top + UiTheme.S(166), 0, 100000, v => Settings.BloodMacGuffinBThreshold = (int)v);

            cx = UiTheme.S(10);
            _spag = MkNum("Spaghetti %", ref cx, top + UiTheme.S(200), 0, 100, v => Settings.SpaghettiThreshold = (int)v, 60);
            // Counterfeit has NO game-side cap (goldBonus = 1 + floor((log2(blood/min)+1)^2)/100,
            // decomp AllBloodMagicController:105) — the old max of 100 falsely capped the target.
            _counter = MkNum("Counterfeit %", ref cx, top + UiTheme.S(200), 0, 100000, v => Settings.CounterfeitThreshold = (int)v, 60);
            Controls.Add(new Label { Text = "Number ≥", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(cx, top + UiTheme.S(204)) });
            cx += UiLayout.MeasureText("Number ≥", UiTheme.Ui) + UiTheme.S(6);
            _numberThr = new TextBox { Location = new Point(cx, top + UiTheme.S(200)), Width = UiTheme.S(110), Font = UiTheme.Ui };
            _numberThr.TextChanged += (s, e) =>
            {
                if (_syncing || Settings == null) return;
                if (double.TryParse(_numberThr.Text, out var d)) { try { Settings.BloodNumberThreshold = d; } catch { } }
            };
            Controls.Add(_numberThr);

            _advice = new Label { Text = "", AutoSize = false, Size = new Size(_w - UiTheme.S(54), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(10), top + UiTheme.S(242)) };
            Controls.Add(_advice);
        }

        private static string Fmt(double v) => NumberFormatter.Abbrev(v);   // consolidated (finding #31); handles negative deltas

        private static Label MakeChip(string text) => new Label
        {
            Text = text,
            AutoSize = false,
            Size = new Size(UiLayout.MeasureText(text, UiTheme.Chip) + UiTheme.S(14), UiTheme.SHead(20)),
            Font = UiTheme.Chip,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            ForeColor = UiTheme.Muted,
            BackColor = UiTheme.Surface
        };

        private void SetChip(Label ch, bool active)
        {
            ch.ForeColor = active ? Color.White : UiTheme.Muted;
            ch.BackColor = active ? Blood : UiTheme.Surface;
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

                UiTheme.ApplyState(_swap, Settings.AutoSpellSwap ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_pillRb, Settings.IronPillOnRebirth ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_guffARb, Settings.BloodMacGuffinAOnRebirth ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_guffBRb, Settings.BloodMacGuffinBOnRebirth ? UiTheme.Cap : UiTheme.Danger, Color.White);
                _guffAThr.Value = Clamp(_guffAThr, Settings.BloodMacGuffinAThreshold);
                _guffBThr.Value = Clamp(_guffBThr, Settings.BloodMacGuffinBThreshold);
                _spag.Value = Clamp(_spag, Settings.SpaghettiThreshold);
                _counter.Value = Clamp(_counter, Settings.CounterfeitThreshold);
                _numberThr.Text = Settings.BloodNumberThreshold.ToString("0");
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
                if (!plan.Known) status = "IRON PILL — gathering data…";
                else if (!plan.PillWorthwhile) status = "IRON PILL — not worthwhile (can't raise your adventure stats)";
                else if (plan.UnreachableThisRun) status = "IRON PILL — on cooldown past this rebirth (not pooling)";
                else if (plan.CastIronNow) status = "IRON PILL — CAST NOW";
                else if (plan.PoolForPill) status = "IRON PILL — pooling (autos paused while charging)";
                else status = "IRON PILL — worthwhile";
                if (plan.Known)
                    status += plan.PillPowerNow > 0
                        ? $" · {Fmt(blood)} → +{plan.PillPowerNow:N0} adv"
                        : $" · {Fmt(blood)} — below cast minimum";
                UiLayout.FitInto(_pillStatus, status);

                bool showRoute = plan.Known && plan.RouteKnown;
                _cNum.Visible = _cLoot.Visible = _cGold.Visible = showRoute;
                if (showRoute)
                {
                    SetChip(_cNum, plan.WantRebirth);
                    SetChip(_cLoot, plan.WantLoot);
                    SetChip(_cGold, plan.WantGold);
                }

                string advice = !plan.Known ? "Blood advisor idle." : plan.Text;
                if (plan.Known && !string.IsNullOrEmpty(plan.RouteReason)) advice += $" — {plan.RouteReason}";
                UiLayout.FitOrGrow(_advice, advice);
            }
            catch (Exception e) { LogDebug($"Blood panel: {e.Message}"); }
        }
    }
}
