using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // CHALLENGES block (B1: owns the bottom of the Advisors home with room to expand). Fully
    // reflowed — completed chips wrap to as many rows as they need, queued rows render only when
    // they exist (the old fixed slots left a dead gap), and the gear-rotation prerequisite hint is
    // CONDITIONAL: it only appears while a challenge is running with a required toggle off.
    public class ChallengesPanel : Panel
    {
        private const int MaxQueued = 6;

        // Chips hold one 7.5pt line, so the chip height is floored at the measured head line box and the
        // wrap pitch is derived from it (at the tuning baseline this is exactly the old 18/24 pair).
        private static readonly int ChipH = UiTheme.SHead(18);
        private Button _srcToggle;
        private Button _refresh;
        private Panel _doneChips;
        private Label _current;
        private Label _lsc;
        private Label _queuedHead;
        private readonly Label[] _queued = new Label[MaxQueued];
        private Label _note;
        private readonly int _lineW;

        // Signature of the last completed-chip build (see Refresh2) — chips are rebuilt only when
        // the completed set changes, and the old ones are DISPOSED, not just cleared.
        private string _doneSig;

        // canvasW: explicit canvas width when hosted in an M1 section column (0 = UiLayout.PanelW).
        public ChallengesPanel(int canvasW = 0)
        {
            int W = canvasW > 0 ? canvasW : UiLayout.PanelW;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;
            _lineW = W - UiTheme.S(54);

            _srcToggle = new Button { Text = "ADVISOR OVERLAYS ACTIVE", Size = new Size(UiLayout.BtnWidth("ADVISOR OVERLAYS ACTIVE"), UiTheme.SCtl(24)), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat };
            _srcToggle.FlatAppearance.BorderColor = UiTheme.Border;
            _srcToggle.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.AdvisorChallenges = !Settings.AdvisorChallenges;
                SyncFromSettings();
            };
            _refresh = new Button { Text = "↻", Size = new Size(Math.Max(UiTheme.S(36), UiLayout.BtnWidth("↻")), UiTheme.SCtl(24)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(_refresh);
            _refresh.Click += (s, e) => Refresh2();
            Controls.Add(_srcToggle);
            Controls.Add(_refresh);
            UiLayout.Row(UiTheme.S(10), UiTheme.S(8), UiTheme.S(8), _srcToggle, _refresh);

            _doneChips = new Panel { Location = new Point(UiTheme.S(10), UiTheme.S(42)), Size = new Size(_lineW, ChipH + UiTheme.S(6)), BackColor = UiTheme.Ground, Tag = "exclusive" };
            Controls.Add(_doneChips);

            _current = new Label { Text = "…", AutoSize = false, Size = new Size(_lineW, UiTheme.TextH), Font = UiTheme.Bold, ForeColor = UiTheme.Energy, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(10), UiTheme.S(74)) };
            Controls.Add(_current);

            _lsc = new Label { Text = "", AutoSize = false, Size = new Size(_lineW, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(10), UiTheme.S(100)) };
            Controls.Add(_lsc);

            _queuedHead = new Label { Text = "QUEUED", AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(10), UiTheme.S(128)) };
            Controls.Add(_queuedHead);
            for (int i = 0; i < _queued.Length; i++)
            {
                _queued[i] = new Label { Text = "", AutoSize = false, Size = new Size(_lineW, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(10), UiTheme.S(152) + i * UiTheme.LinePitch) };
                Controls.Add(_queued[i]);
            }

            _note = new Label { Text = "", AutoSize = false, Size = new Size(_lineW, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Danger, BackColor = UiTheme.Ground, Visible = false };
            Controls.Add(_note);

            VisibleChanged += (s, e) => { if (Visible) Refresh2(); };
            SyncFromSettings();
        }

        public void SyncFromSettings()
        {
            if (Settings == null) return;
            bool on = Settings.AdvisorChallenges;
            _srcToggle.Text = on ? "ADVISOR OVERLAYS ACTIVE" : "OVERLAYS OFF";
            UiTheme.ApplyState(_srcToggle, on ? UiTheme.Cap : UiTheme.Danger, Color.White);
            Refresh2();
        }

        // Not named Refresh: Control.Refresh() is a repaint, this rebuilds content.
        private void Refresh2()
        {
            try
            {
                var block = ChallengeOverlay.Block();
                string active = null;
                try { active = ChallengeDetector.Current(); } catch { }

                // Completed chips — wrap to as many rows as needed, everything below reflows.
                // Rebuilt only when the completed set changes, with explicit disposal: this runs on
                // every settings save, and Controls.Clear() does NOT dispose — the orphaned chips
                // held native handles until the process GDI budget ran out (GUI death).
                var done = block.Where(b => b.Max > 0 && b.Cur >= b.Max)
                    .Select(e => $"✓ {e.Code} {e.Cur}/{e.Max}").ToList();
                string sig = string.Join("|", done.ToArray());
                if (sig != _doneSig)
                {
                    UiLayout.DisposeChildren(_doneChips);
                    var chips = new List<Control>();
                    foreach (var text in done)
                    {
                        var chip = MkChip(text, UiTheme.Cap);
                        chips.Add(chip);
                        _doneChips.Controls.Add(chip);
                    }
                    if (done.Count == 0)
                    {
                        var chip = MkChip("NO CHALLENGES COMPLETE YET", UiTheme.Faint);
                        chips.Add(chip);
                        _doneChips.Controls.Add(chip);
                    }
                    int chipBottom = UiLayout.WrapRow(0, UiTheme.S(2), UiTheme.S(6), _doneChips.Width - UiTheme.S(6), ChipH + UiTheme.S(2), chips);
                    _doneChips.Height = chipBottom + UiTheme.S(2);
                    // Sig LAST: a throw mid-rebuild (Mono's text measure can fail under GDI
                    // pressure — the very thing this guard exists for) is swallowed by the catch
                    // below, so committing it first would pin the empty strip until the completed
                    // set changed. Stale sig => the next Refresh2 simply rebuilds.
                    _doneSig = sig;
                }
                int y = _doneChips.Bottom + UiTheme.S(8);

                // Current line.
                _current.Top = y;
                if (active != null)
                {
                    var cur = block.FirstOrDefault(b => b.Code == active);
                    string prog = cur != null ? $" ({cur.Cur + 1}/{cur.Max})" : "";
                    string phase = string.IsNullOrEmpty(ChallengeOverlay.Phase) ? "" : $" · {ChallengeOverlay.Phase} phase";
                    string ea = ChallengeOverlay.AllocationStatus(ResourceType.Energy);
                    string ma = ChallengeOverlay.AllocationStatus(ResourceType.Magic);
                    string alloc = "";
                    if (ea != null) alloc += $" · E {ea}";
                    if (ma != null) alloc += $" · M {ma}";
                    UiLayout.FitOrGrow(_current, $"{active}{prog} — RUNNING{phase}{alloc}");
                    _current.ForeColor = UiTheme.Energy;
                }
                else
                {
                    UiLayout.FitOrGrow(_current, "No challenge active — overlays idle, profile rules apply.");
                    _current.ForeColor = UiTheme.Muted;
                }
                y = _current.Bottom + UiTheme.S(4);

                // LSC opportunity (user rule): number is NOT reset — when finishable inside the Augs
                // window, this is free challenge progress. Target read live from the game.
                var lsc = LscAdvisor.Compute();
                _lsc.Visible = lsc.Known && active == null;
                if (_lsc.Visible)
                {
                    _lsc.Top = y;
                    _lsc.ForeColor = lsc.Recommended ? UiTheme.Cap : UiTheme.Muted;
                    _lsc.Font = lsc.Recommended ? UiTheme.Bold : UiTheme.Ui;
                    UiLayout.FitOrGrow(_lsc, lsc.Text);
                    y = _lsc.Bottom + UiTheme.S(4);
                }

                // Queued: only the rows that exist (the fixed 3-slot reservation left a dead gap).
                var queued = block.Where(b => b.Code != active && b.Max > 0 && b.Cur < b.Max).Take(_queued.Length).ToList();
                _queuedHead.Visible = queued.Count > 0;
                if (queued.Count > 0)
                {
                    _queuedHead.Top = y + UiTheme.S(6);
                    y = _queuedHead.Top + UiTheme.HeadPitch;
                }
                for (int i = 0; i < _queued.Length; i++)
                {
                    if (i < queued.Count)
                    {
                        _queued[i].Top = y;
                        UiLayout.FitOrGrow(_queued[i], $"{queued[i].Code} {queued[i].Cur}/{queued[i].Max} — {queued[i].StripNote}");
                        _queued[i].Visible = true;
                        y = _queued[i].Bottom + UiTheme.S(4);
                    }
                    else _queued[i].Visible = false;
                }

                // CONDITIONAL prerequisite (was a permanent footnote): only while a challenge runs
                // with the gear-rotation toggles off does the warning appear.
                bool gearReady = Settings != null && Settings.ManageGear && Settings.AdvisorGearRefresh;
                _note.Visible = active != null && !gearReady;
                if (_note.Visible)
                {
                    _note.Top = y + UiTheme.S(6);
                    UiLayout.FitOrGrow(_note, "⚠ Challenge gear rotation is OFF — turn on Manage Gear + Advisor Gear Refresh (Settings).");
                    y = _note.Bottom;
                }

                // Self-size to actual content: a low floor lets the panel COLLAPSE when idle (no active
                // challenge / empty queue) so the Status page doesn't scroll over empty reserved space.
                // It still grows for a long queue (host scrolls only then, which is legitimate).
                Height = Math.Max(UiTheme.S(120), y + UiTheme.S(12));
            }
            catch (Exception ex) { LogDebug($"Challenges panel: {ex.Message}"); }
        }

        private static Label MkChip(string text, Color bg)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Size = new Size(UiLayout.MeasureText(text, UiTheme.Chip) + UiTheme.S(14), ChipH),
                Font = UiTheme.Chip,
                ForeColor = Color.White,
                BackColor = bg,
                TextAlign = ContentAlignment.MiddleCenter
            };
        }
    }
}
