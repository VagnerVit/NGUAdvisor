using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Route C3 Phase 2 (slimmed in UI3): the live status strip docked to the bottom of the advisor Settings
    // window. Built from standard Label controls (NOT custom OnPaint) because labels repaint reliably in this
    // normal window under the injected Mono - the earlier custom-paint/floating-window attempts did not. Shows
    // what the automation is doing and what the player is working toward. Updated from Main.Update (main thread).
    //
    // UI3: ONE row of eight cells (was two rows of four). The M1 window is fixed at ~1240px wide, so the extra
    // width lets the strip go from 94px to ~52px — reclaimed on every view, since the strip is Dock.Bottom on
    // the whole form. Cells are WEIGHTED, not equal: goal/resource cells that carry long strings get more room
    // than AUTO/REBIRTH. Every dynamic value is measured-fit (UiLayout.FitText — a fixed Mono label with
    // overflowing text paints NOTHING) and its full text is exposed through a tooltip.
    public class StatusPanel : Panel
    {
        private static readonly int Pad = UiTheme.S(12);         // outer left/right padding
        private static readonly int AccentH = UiTheme.S(3);      // top accent stripe
        private static readonly int CapY = UiTheme.S(6);         // caption baseline (below the stripe)
        private static readonly int ValY = UiTheme.HeadPitch;    // value baseline (caption -> value pitch)
        private static readonly int ValPad = UiTheme.S(10);      // value width = cell width - this
        private static readonly int MinCell = UiTheme.S(40);     // defensive per-cell floor
        private static readonly int BottomMargin = UiTheme.S(6);
        // Value height >= TextH so 9pt descenders (g/p/y) are not clipped (the old 20px box was under it).
        private static readonly int ValH = UiTheme.TextH;
        // Derived from the geometry above, not a magic number: value row bottom + a bottom margin.
        public static readonly int PanelHeight = ValY + ValH + BottomMargin;   // 24 + 22 + 6 = 52 at the tuning baseline

        // Cells in display order. AUTO is cell 0 (the control); the seven below are the dynamic chips.
        private readonly List<string> _order = new List<string>
            { "STAGE", "PROFILE", "CURRENT GOAL", "GEAR", "REBIRTH", "RESOURCES", "NEXT GOAL" };
        // Relative column weights, AUTO first then _order. Goal/RESOURCES cells carry long strings and get
        // the width; the low-pressure AUTO/STAGE/REBIRTH cells lend it. RESOURCES is weighted heaviest
        // because the in-game Mono renderer paints "E .. M .. R3 .." wider than a headless measure reports,
        // so it needs real margin. The denominator is the array's OWN sum (computed in DoLayout), so the
        // weights are the single source of truth and no stored total can drift from them.
        private static readonly double[] _weights = { 0.75, 0.85, 1.30, 1.50, 1.20, 0.85, 1.70, 1.50 };

        private class Chip { public Label Cap; public Label Val; public string Full = "-"; }
        private readonly Dictionary<string, Chip> _chips = new Dictionary<string, Chip>();

        // AUTO cell: a bounded panel so the WHOLE cell is a click target (not only the light/caption).
        private readonly Panel _autoCell;
        private readonly Label _autoCap;
        private readonly Panel _autoLight;
        private readonly Label _autoState;   // explicit ON/OFF text (state readable without relying on color)

        private readonly ToolTip _tips;      // ONE instance; content set only when a full value changes

        // Wrap-safe throttle. (Environment.TickCount goes NEGATIVE after ~24.9 days uptime, which made the
        // old `TickCount - last < 250` check true forever => the strip never updated. Root cause of the saga.)
        private DateTime _lastContent = DateTime.MinValue;
        // Last width at which UiLayout.Audit ACTUALLY ran. Recorded only after a successful audit, so a
        // pre-handle DoLayout at the (fixed) form width can never suppress the real, handle-backed audit.
        private int _auditedW = -1;

        public StatusPanel()
        {
            Dock = DockStyle.Bottom;
            Height = PanelHeight;
            BackColor = UiTheme.Surface;

            _tips = new ToolTip();

            Controls.Add(new Panel { Dock = DockStyle.Top, Height = AccentH, BackColor = UiTheme.Accent });

            // ---- AUTO cell (cell 0): caption + light + ON/OFF, all inside one clickable panel ----
            _autoCell = new Panel { BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
            _autoCap = MakeCaption("AUTO");
            _autoCap.Cursor = Cursors.Hand;
            _autoLight = new Panel { Size = new Size(UiTheme.S(14), UiTheme.S(14)), BackColor = UiTheme.Danger, Cursor = Cursors.Hand };
            _autoState = new Label
            {
                Text = "OFF", AutoSize = true, Font = UiTheme.Bold, ForeColor = UiTheme.Danger,
                BackColor = UiTheme.Surface, Cursor = Cursors.Hand
            };
            _autoCell.Controls.Add(_autoCap);
            _autoCell.Controls.Add(_autoLight);
            _autoCell.Controls.Add(_autoState);
            Controls.Add(_autoCell);

            // Click ANY part of the cell => exactly one toggle. WinForms Click does not bubble, so a click
            // lands on exactly one of these controls and fires its handler once — no parent/child double-fire.
            _autoCell.Click += ToggleAuto;
            _autoCap.Click += ToggleAuto;
            _autoLight.Click += ToggleAuto;
            _autoState.Click += ToggleAuto;

            const string autoTip = "Turns automation execution on or off. Advisor decision ownership is configured separately.";
            _tips.SetToolTip(_autoCell, autoTip);
            _tips.SetToolTip(_autoCap, autoTip);
            _tips.SetToolTip(_autoLight, autoTip);
            _tips.SetToolTip(_autoState, autoTip);

            // ---- the seven dynamic chips ----
            foreach (var name in _order)
            {
                var cap = MakeCaption(name);
                // AutoEllipsis=false: Mono paints NOTHING when AutoEllipsis text overflows (cause of the
                // missing dashboard AT row). We fit the text ourselves via UiLayout.FitText instead.
                var val = new Label { AutoSize = false, Height = ValH, ForeColor = UiTheme.Ink, Font = UiTheme.Bold, BackColor = UiTheme.Surface, AutoEllipsis = false, Text = "-" };
                Controls.Add(cap);
                Controls.Add(val);
                _chips[name] = new Chip { Cap = cap, Val = val };
            }

            Resize += (s, e) => DoLayout();
            DoLayout();
        }

        private void ToggleAuto(object sender, EventArgs e)
        {
            try
            {
                if (Settings == null) return;
                Settings.GlobalEnabled = !Settings.GlobalEnabled;
                PaintAuto(Settings.GlobalEnabled);
            }
            catch (Exception ex) { LogDebug($"AUTO toggle failed: {ex.Message}"); }
        }

        // Light + ON/OFF text agree, and both carry the state (never color alone).
        private void PaintAuto(bool on)
        {
            var col = on ? UiTheme.Cap : UiTheme.Danger;
            if (_autoLight.BackColor != col) _autoLight.BackColor = col;
            string t = on ? "ON" : "OFF";
            if (_autoState.Text != t) _autoState.Text = t;
            if (_autoState.ForeColor != col) _autoState.ForeColor = col;
        }

        private static Label MakeCaption(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Font = UiTheme.ColHeader,
            BackColor = UiTheme.Surface
        };

        // One row of eight WEIGHTED cells. Column edges are cumulative rounded positions, so the widths sum
        // EXACTLY to the usable width and the rounding remainder lands deterministically on the last edges.
        private void DoLayout()
        {
            int usable = Math.Max(8 * MinCell, Width - Pad * 2);

            // x[0..8] = cell edges. x[i] = Pad + round(usable * cumulativeWeight / totalWeight). Cumulative
            // rounding => the widths sum EXACTLY to usable and the remainder lands deterministically on the
            // last edges. totalWeight is the array's own sum, so it can never disagree with the weights.
            double total = 0;
            for (int i = 0; i < _weights.Length; i++) total += _weights[i];

            var x = new int[_weights.Length + 1];
            x[0] = Pad;
            double cum = 0;
            for (int i = 0; i < _weights.Length; i++)
            {
                cum += _weights[i];
                x[i + 1] = Pad + (int)Math.Round(usable * cum / total);
            }

            // Cell 0 = AUTO. Sits below the accent stripe; children are placed relative to the cell.
            _autoCell.Bounds = new Rectangle(x[0], AccentH, Math.Max(MinCell, x[1] - x[0]), PanelHeight - AccentH);
            _autoCap.Location = new Point(UiTheme.S(2), CapY - AccentH);
            _autoLight.Location = new Point(UiTheme.S(2), ValY - AccentH + UiTheme.S(2));
            _autoState.Location = new Point(UiTheme.S(2) + _autoLight.Width + UiTheme.S(4), ValY - AccentH);

            // Cells 1..7 = the dynamic chips.
            for (int i = 0; i < _order.Count; i++)
            {
                var ch = _chips[_order[i]];
                int cellX = x[i + 1];
                int cellW = Math.Max(MinCell, x[i + 2] - x[i + 1]);
                ch.Cap.Location = new Point(cellX, CapY);
                ch.Val.Location = new Point(cellX, ValY);
                ch.Val.Width = Math.Max(10, cellW - ValPad);
                ch.Val.Height = ValH;
                ApplyFit(ch);
            }

            // Register with the canonical auditor after a REAL layout, never in the 250ms update loop.
            TryAuditLayout();
        }

        // Audit ONLY when the handle exists and ONLY once per width — and record the width ONLY after the
        // audit actually runs. So if DoLayout ran before the handle at the fixed form width, that width is
        // NOT recorded, and this still audits once the handle is created at that same width. The 250ms
        // UpdateStatus path never calls this, so normal updates never re-audit.
        private void TryAuditLayout()
        {
            if (!IsHandleCreated || Width <= 0 || Width == _auditedW) return;
            UiLayout.Audit(this, "Status");
            _auditedW = Width;
        }

        // The handle can be created after the first DoLayout, at the same fixed width — re-run layout so the
        // now-handle-backed audit runs (DoLayout -> TryAuditLayout). Without this, a fixed-width form whose
        // only DoLayout preceded HandleCreated would never get its initial Status audit.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DoLayout();
        }

        // Displayed value = measured-fit; full value lives in the tooltip. Never blanks a nonempty value.
        private void ApplyFit(Chip ch)
        {
            string fit = UiLayout.FitText(ch.Full, UiTheme.Bold, Math.Max(10, ch.Val.Width));
            if (ch.Val.Text != fit) ch.Val.Text = fit;
        }

        // Called each frame from Main.Update (main thread). Throttled; reads live state and updates labels.
        public void UpdateStatus()
        {
            try
            {
                if ((DateTime.UtcNow - _lastContent).TotalMilliseconds < 250) return;
                _lastContent = DateTime.UtcNow;

                var c = Main.Character;
                PaintAuto(Settings != null && Settings.GlobalEnabled);

                var prog = ProgressionAnalyzer.Detect();
                var challenge = ChallengeDetector.Current();

                Set("STAGE", prog.Known ? prog.Label : "-", UiTheme.Ink);
                Set("PROFILE", Settings.AutoProfile ? "AUTO (advisor)" : Settings.AllocationFile ?? "-",
                    Settings.AutoProfile ? UiTheme.Accent : UiTheme.Ink);
                Set("CURRENT GOAL", prog.Known ? prog.Activity : "-", challenge != null ? UiTheme.Accent : UiTheme.Ink);

                string gear = "Auto";
                if (challenge != null)
                {
                    var def = ChallengeDetector.DefaultGear(challenge);
                    if (def != null) gear = def.Objective;
                }
                Set("GEAR", gear, UiTheme.Ink);

                Set("REBIRTH", RebirthElapsed(c), UiTheme.Ink);
                Set("RESOURCES", Resources(c), UiTheme.Ink);
                Set("NEXT GOAL", prog.Known ? Capitalize(prog.NextGoal) : "-", UiTheme.Ink);

                Refresh(); // force a synchronous repaint (label Text changes don't always repaint on their own)
            }
            catch (Exception e) { LogDebug($"StatusPanel update failed: {e.Message}"); }
        }

        // Store the full value, show a measured-fit form, and expose the full value via tooltip (only when
        // the source string actually changes — not every 250ms tick).
        private void Set(string name, string value, Color color)
        {
            if (!_chips.TryGetValue(name, out var ch)) return;
            if (ch.Full != value)
            {
                ch.Full = value;
                _tips.SetToolTip(ch.Val, value);
                ApplyFit(ch);
            }
            if (ch.Val.ForeColor != color) ch.Val.ForeColor = color;
        }

        private static string RebirthElapsed(Character c)
        {
            try
            {
                int s = (int)Math.Floor(c.rebirthTime.totalseconds);
                int h = s / 3600, m = (s % 3600) / 60, sec = s % 60;
                return h > 0 ? $"{h}h {m}m" : (m > 0 ? $"{m}m {sec}s" : $"{sec}s");
            }
            catch { return "-"; }
        }

        private static string Resources(Character c)
        {
            try
            {
                string e = Pct(c.curEnergy, c.totalCapEnergy());
                string m = Pct(c.magic.curMagic, c.totalCapMagic());
                string r = c.res3 != null && c.res3.res3On ? Pct(c.res3.curRes3, c.totalCapRes3()) : "-";
                return $"E {e}  M {m}  R3 {r}";
            }
            catch { return "-"; }
        }

        private static string Pct(double cur, double cap)
            => cap > 0 ? $"{(int)Math.Round(Math.Min(cur, cap) / cap * 100)}%" : "-";

        private static string Capitalize(string s)
            => string.IsNullOrEmpty(s) ? "-" : char.ToUpper(s[0]) + s.Substring(1);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tips?.Dispose();   // the one owned ToolTip has no container to dispose it
            base.Dispose(disposing);
        }
    }
}
