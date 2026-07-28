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
    // AUTO PROFILE card (B1) — the run plan (segment chips + E/M/R3 token lines) on the left, and a
    // READ-ONLY profile summary on the right. UI4 moved every MUTABLE profile control (the mode toggle,
    // file selector, SWITCH/EDIT/FILES/APPLY) out to the dedicated PROFILE section; this card now only
    // SHOWS allocation source / selected(standby) file / recommendation, with OPEN PROFILE as the route
    // to change any of it. Fully reflowed per refresh: wrapped text grows rows, never "…" on plan tokens.
    public class AutopilotPanel : Panel
    {
        private Button _openBtn;
        private Button _refresh;
        private Panel _card;
        private Label _title;
        private Label _eLine;
        private Label _mLine;
        private Label _rLine;
        private Label _note1;
        private Label _note2;
        private static readonly string[] SegOrder = { "TM HOUR", "AT HOUR", "RECOVERY", "MARATHON" };
        private readonly Label[] _segChips = new Label[4];
        private readonly SettingsForm _form;
        private Label _srcLine;      // read-only: allocation source
        private Label _fileLine;     // read-only: active/standby file
        private Label _recProfile;   // read-only: recommendation
        private readonly ToolTip _tips = new ToolTip();
        private readonly int _planW;    // left zone width
        private readonly int _stripX;   // right zone x inside the card
        private readonly int _chipH = UiTheme.SHead(18);   // floored 7.5pt line box; also the chip row pitch

        public AutopilotPanel(SettingsForm form, int canvasW = 0)
        {
            int W = canvasW > 0 ? canvasW : UiLayout.PanelW;
            _form = form;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            _openBtn = new Button { Text = "OPEN PROFILE →", Size = new Size(UiLayout.BtnWidth("OPEN PROFILE →"), UiTheme.SCtl(24)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(_openBtn);
            _openBtn.Click += (s, e) => { try { _form?.NavigateTo(Destinations.Profile); } catch (Exception ex) { LogDebug($"Open profile: {ex.Message}"); } };
            _refresh = new Button { Text = "↻", Size = new Size(Math.Max(UiTheme.S(36), UiLayout.BtnWidth("↻")), UiTheme.SCtl(24)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(_refresh);
            _refresh.Click += (s, e) => RefreshView();
            Controls.Add(_openBtn);
            Controls.Add(_refresh);
            UiLayout.Row(UiTheme.S(10), UiTheme.S(8), UiTheme.S(8), _openBtn, _refresh);

            _card = new Panel { Location = new Point(UiTheme.S(10), UiTheme.S(40)), Size = new Size(W - UiTheme.S(40), UiTheme.S(170)), BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(_card);

            // Zones: plan left, read-only profile summary right (B1). A vertical divider keeps them readable.
            _stripX = _card.Width * 3 / 5 + UiTheme.S(20);
            _planW = _stripX - UiTheme.S(30);
            int stripW = _card.Width - _stripX - UiTheme.S(10);
            _card.Controls.Add(new Panel { Location = new Point(_stripX - UiTheme.S(10), UiTheme.S(6)), Size = new Size(1, UiTheme.S(150)), BackColor = UiTheme.Border, Tag = "exclusive" });

            _title = new Label { Text = "…", AutoSize = false, Size = new Size(_planW, UiTheme.TextH), Font = UiTheme.Bold, ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(10), UiTheme.S(6)) };
            _card.Controls.Add(_title);
            for (int i = 0; i < SegOrder.Length; i++)
            {
                _segChips[i] = new Label
                {
                    Text = SegOrder[i], AutoSize = false,
                    Size = new Size(UiLayout.MeasureText($"{SegOrder[i]} ✓", UiTheme.Chip) + UiTheme.S(14), _chipH),
                    Font = UiTheme.Chip, ForeColor = Color.White, BackColor = UiTheme.Faint,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                _card.Controls.Add(_segChips[i]);
            }
            _eLine = MkLine();
            _mLine = MkLine();
            _rLine = MkLine();

            // Read-only profile summary (right strip): source -> file -> recommendation. The mutable controls
            // live on the PROFILE page now; OPEN PROFILE (top row) is the route to them.
            _srcLine = MkStripLabel(stripW, UiTheme.S(6), UiTheme.Ink);
            _fileLine = MkStripLabel(stripW, UiTheme.S(32), UiTheme.Ink);
            _recProfile = MkStripLabel(stripW, UiTheme.S(58), UiTheme.Accent);

            _note1 = new Label { Text = "", AutoSize = false, Size = new Size(stripW, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(_stripX, UiTheme.S(100)) };
            _note2 = new Label { Text = "", AutoSize = false, Size = new Size(stripW, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Faint, BackColor = UiTheme.Surface, Location = new Point(_stripX, UiTheme.S(126)) };
            _card.Controls.Add(_note1);
            _card.Controls.Add(_note2);

            VisibleChanged += (s, e) => { if (Visible) RefreshView(); };
            RefreshView();
        }

        private Label MkStripLabel(int stripW, int y, Color fore)
        {
            var l = new Label { Text = "", AutoSize = false, Size = new Size(stripW, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = fore, BackColor = UiTheme.Surface, AutoEllipsis = false, Location = new Point(_stripX, y) };
            _card.Controls.Add(l);
            return l;
        }

        // Fixed-width measured fit + full text in the shared tooltip (Mono blanks an overflowing fixed label).
        private void SetSummary(Label l, string text)
        {
            _tips.SetToolTip(l, text ?? "");
            string fit = UiLayout.FitText(text ?? "", l.Font, l.Width);
            if (l.Text != fit) l.Text = fit;
        }

        private Label MkLine()
        {
            var l = new Label { Text = "", AutoSize = false, Size = new Size(_planW, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Ink, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(10), UiTheme.S(60)) };
            _card.Controls.Add(l);
            return l;
        }

        // Read-only summary of the profile state — mirrors the PROFILE page's three concepts, no controls.
        private void UpdateSummary()
        {
            try
            {
                if (Settings == null) return;
                bool advisor = Settings.AutoProfile;
                string file = Settings.AllocationFile ?? "-";
                // Bounded FIXED-HEIGHT fit (never FitOrGrow): a long file/recommendation name must not grow
                // the summary label downward into the run-plan/notes — it truncates with the full text in a
                // tooltip. RefreshView is show/refresh-driven (not per-frame), so the tooltip set is cheap.
                SetSummary(_srcLine, advisor ? "Allocation source: advisor-generated" : "Allocation source: profile file");
                SetSummary(_fileLine, advisor ? $"Standby file: {file}" : $"Active file: {file}");
                string rec = "";
                try { var prog = ProgressionAnalyzer.Detect(); rec = prog.Known ? prog.RecommendedProfile : ""; } catch { }
                SetSummary(_recProfile, string.IsNullOrEmpty(rec) ? "Recommended: —" : $"Recommended: {rec}");
            }
            catch (Exception e) { LogDebug($"Autopilot summary: {e.Message}"); }
        }

        private static readonly string[] ENguNames = { "Augs", "Wandoos", "Respawn", "Gold", "Adv-α", "Power-α", "DC", "Magic", "PP" };
        private static readonly string[] MNguNames = { "Ygg", "EXP", "Power-β", "Number", "TM", "Energy", "Adv-β" };

        // Parse the token grammar — [CAP]BASE[-index][:percent] — instead of tabulating literals. The
        // old switch enumerated every percent variant by hand (CAPTM:5, CAPTM:30, CAPWAN:40, CAPWAN:60),
        // so retuning any of them fell through and the panel printed the raw token at the user. That
        // already showed for CAPBESTAUG and CAPALLBT, which were never in the table at all.
        private static string Friendly(ResourceType type, string tok)
        {
            if (string.IsNullOrEmpty(tok)) return tok;

            string body = tok, pct = null;
            int colon = tok.IndexOf(':');
            if (colon >= 0) { body = tok.Substring(0, colon); pct = tok.Substring(colon + 1); }

            bool cap = body.StartsWith("CAP");
            string rest = cap ? body.Substring(3) : body;

            string bare = rest;
            int index = -1;
            int dash = rest.IndexOf('-');
            if (dash >= 0 && int.TryParse(rest.Substring(dash + 1), out var parsed))
            {
                bare = rest.Substring(0, dash);
                index = parsed;
            }

            string name;
            switch (bare)
            {
                case "TM": name = "TM"; break;
                case "WAN": name = "WAN"; break;
                case "BESTAUG": name = "best aug"; break;
                case "ALLAT": name = "AT caps"; break;
                case "ALLNGU": name = "all NGU"; break;
                case "ALLBT": name = "all BT"; break;
                case "ALLHACK": name = "all hacks"; break;
                case "BR": name = "rituals"; break;
                case "NGU":
                    var names = type == ResourceType.Magic ? MNguNames : ENguNames;
                    if (index < 0 || index >= names.Length) return tok;
                    name = $"NGU:{names[index]}";
                    break;
                default: return tok;
            }

            if (pct != null) name += $" {pct}%";
            // Keep the hot/warm distinction visible: a CAP NGU lane is a surplus absorber, not a hot
            // lane, and the two used to be told apart only by the raw token leaking through.
            if (cap && bare == "NGU") name += " (surplus)";
            return name;
        }

        private static string PlanLine(string prefix, ResourceType type)
        {
            var toks = ChallengeOverlay.AutoTokens(type).Select(t => Friendly(type, t)).ToArray();
            return toks.Length > 0 ? $"{prefix}: {string.Join(" → ", toks)}" : $"{prefix}: —";
        }

        public void SyncFromSettings()
        {
            if (Settings == null) return;
            RefreshView();
        }

        private void RefreshView()
        {
            try
            {
                if (Settings == null) return;
                UpdateSummary();
                bool on = Settings.AutoProfile;
                string challenge = null;
                try { challenge = ChallengeDetector.Current(); } catch { }

                if (!on)
                {
                    UiLayout.FitOrGrow(_title, "AUTO PROFILE — off");
                    _title.ForeColor = UiTheme.Muted;
                    UiLayout.FitOrGrow(_eLine, $"Allocation comes from the profile timeline: {Settings.AllocationFile ?? "-"}");
                    UiLayout.FitOrGrow(_mLine, "Flip the toggle and the advisor generates priorities from run phase + TM state.");
                    UiLayout.FitOrGrow(_rLine, "");
                }
                else if (challenge != null && Settings.AdvisorChallenges)
                {
                    UiLayout.FitOrGrow(_title, $"AUTO PROFILE — standing by ({challenge} overlay owns allocation)");
                    _title.ForeColor = UiTheme.Energy;
                    UiLayout.FitOrGrow(_eLine, "Challenge strips/templates outrank the generator while the challenge runs.");
                    UiLayout.FitOrGrow(_mLine, "Generation resumes the moment the challenge ends.");
                    UiLayout.FitOrGrow(_rLine, "");
                }
                else
                {
                    UiLayout.FitOrGrow(_title, $"AUTO PROFILE — {ChallengeOverlay.AutoStatus()}");
                    _title.ForeColor = UiTheme.Accent;
                    var mTokens = ChallengeOverlay.AutoTokens(ResourceType.Magic);
                    string ritual = Array.IndexOf(mTokens, "BR-30") < 0 ? "   · rituals off (no live consumer)" : "";
                    // The run plan is the one thing that must never truncate.
                    UiLayout.FitOrGrow(_eLine, PlanLine("E", ResourceType.Energy));
                    UiLayout.FitOrGrow(_mLine, PlanLine("M", ResourceType.Magic) + ritual);
                    UiLayout.FitOrGrow(_rLine, PlanLine("R3", ResourceType.R3));
                }

                // Timeline chips: window passed = green ✓, current = gold ←, future = grey.
                double runSec = 0;
                try { runSec = Main.Character.rebirthTime.totalseconds; } catch { }
                string cur = ChallengeOverlay.Segment;
                double[] windowEnd = { 3600, 7200, 14400, double.MaxValue };
                for (int i = 0; i < SegOrder.Length; i++)
                {
                    bool current = SegOrder[i] == cur || (SegOrder[i] == "MARATHON" && cur == "NGU MARATHON");
                    bool walked = !current && runSec >= windowEnd[i];
                    _segChips[i].Text = current ? $"{SegOrder[i]} ←" : walked ? $"{SegOrder[i]} ✓" : SegOrder[i];
                    _segChips[i].BackColor = current ? UiTheme.Energy : walked ? UiTheme.Cap : UiTheme.Faint;
                    _segChips[i].ForeColor = Color.White;
                }

                string caps = string.IsNullOrEmpty(LevelPlanner.Status) ? "" : $"caps: {LevelPlanner.Status}";
                UiLayout.FitOrGrow(_note1, caps);
                string runLen = OptimizationAdvisor.RecommendedRunLength();
                UiLayout.FitOrGrow(_note2, string.IsNullOrEmpty(runLen) ? "" : $"Guide run length: {runLen}");

                Reflow();
            }
            catch (Exception ex) { LogDebug($"Autopilot panel: {ex.Message}"); }
        }

        // Vertical reflow of both zones; the card takes whichever column runs deeper.
        private void Reflow()
        {
            try
            {
                // Plan zone (left): title → chips row → E/M/R3.
                int cy = _title.Bottom + UiTheme.S(6);
                int cx = UiTheme.S(10);
                for (int i = 0; i < _segChips.Length; i++)
                {
                    if (cx + _segChips[i].Width > _planW && cx > UiTheme.S(10)) { cx = UiTheme.S(10); cy += _chipH; }
                    _segChips[i].Location = new Point(cx, cy);
                    cx += _segChips[i].Width + UiTheme.S(5);
                }
                int afterChips = cy + _chipH + UiTheme.S(4);
                _eLine.Top = afterChips;
                _mLine.Top = _eLine.Bottom + UiTheme.S(2);
                _rLine.Top = _mLine.Bottom + UiTheme.S(2);

                // Profile summary (right): source → file → recommendation → notes (all read-only).
                _srcLine.Top = UiTheme.S(6);
                _fileLine.Top = _srcLine.Bottom + UiTheme.S(4);
                _recProfile.Top = _fileLine.Bottom + UiTheme.S(8);
                _note1.Top = _recProfile.Bottom + UiTheme.S(12);
                _note2.Top = _note1.Bottom + UiTheme.S(4);

                _card.Height = Math.Max(_rLine.Bottom, _note2.Bottom) + UiTheme.S(10);
                Height = _card.Bottom + UiTheme.S(8);
            }
            catch (Exception ex) { LogDebug($"Autopilot reflow: {ex.Message}"); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tips?.Dispose();   // the one owned ToolTip has no container to dispose it
            base.Dispose(disposing);
        }
    }
}
