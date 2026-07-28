using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // GROWTH band (G1, user-approved): five rate tiles between the lights and the AUTO PROFILE
    // card — EXP · AP · PP · CUBE P/T · NGU LEVELS, each with the current value, a windowed
    // rate (15M / 1H / RUN chips switch all five), and one context line. The NGU tile shows
    // measured vs NGUAdvisors-predicted — the tick-rate calibration measuring itself.
    public class GrowthPanel : Panel
    {
        private class Tile
        {
            public Panel Box;
            public Label Name;
            public Label Value;
            public Label Rate;
            public Label Sub;
        }

        private static readonly string[] Names = { "EXP", "AP", "PP", "CUBE P / T", "NGU LEVELS" };
        private readonly Tile[] _tiles = new Tile[5];
        private readonly Button[] _chips = new Button[3];
        private static readonly string[] ChipNames = { "15M", "1H", "RUN" };
        private static readonly double[] ChipWindows = { 15, 60, -1 };
        private int _window = 1;   // default 1H
        private DateTime _lastTick = DateTime.MinValue;

        public GrowthPanel(int canvasW)
        {
            BackColor = UiTheme.Ground;
            Width = canvasW;

            var head = new Label { Text = "GROWTH", AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(0, UiTheme.S(4)) };
            Controls.Add(head);
            int cx = UiLayout.MeasureText("GROWTH", UiTheme.ColHeader) + UiTheme.S(12);
            for (int i = 0; i < ChipNames.Length; i++)
            {
                int idx = i;
                var b = new Button { Text = ChipNames[i], Bounds = new Rectangle(cx, 0, UiLayout.BtnWidth(ChipNames[i]), UiTheme.SCtl(20)), Font = UiTheme.Chip, FlatStyle = FlatStyle.Flat };
                b.FlatAppearance.BorderColor = UiTheme.Border;
                b.Click += (s, e) => { _window = idx; StyleChips(); RefreshTiles(); };
                Controls.Add(b);
                _chips[i] = b;
                cx += b.Width + UiTheme.S(4);
            }

            int tileW = (canvasW - 4 * UiTheme.S(8)) / 5;
            // Every line in the tile is floored at what the renderer actually needs, so the stack and the
            // tile height are DERIVED from those floors. Scaling the 78px tile independently of its four
            // lines is what cut the rate/sub lines ("next pit toss 30m", "predicted 1.9/hr") at high DPI.
            int capH = UiTheme.SHead(14);
            int valH = UiTheme.SText(22);
            int rateH = UiTheme.SHead(15);
            int subH = UiTheme.SHead(14);
            int capY = UiTheme.S(4);
            int valY = capY + capH;
            int rateY = valY + valH;
            int subY = rateY + rateH;
            int tileH = subY + subH + UiTheme.S(4);
            // The band's header row: the caption label and the window chips both hold text, so the tiles
            // start below whichever of them is taller (at the tuning baseline this is exactly 26px).
            int tileTop = Math.Max(UiTheme.SCtl(20), UiTheme.S(4) + UiTheme.HeadH);
            for (int i = 0; i < Names.Length; i++)
            {
                var t = new Tile();
                t.Box = new Panel { Bounds = new Rectangle(i * (tileW + UiTheme.S(8)), tileTop, tileW, tileH), BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle };
                t.Name = new Label { Text = Names[i], AutoSize = false, Size = new Size(tileW - UiTheme.S(14), capH), Font = UiTheme.Chip, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(7), capY) };
                t.Value = new Label { Text = "…", AutoSize = false, Size = new Size(tileW - UiTheme.S(14), valH), Font = UiTheme.Bold, ForeColor = UiTheme.Ink, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(7), valY) };
                t.Rate = new Label { Text = "", AutoSize = false, Size = new Size(tileW - UiTheme.S(14), rateH), Font = UiTheme.Chip, ForeColor = UiTheme.Cap, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(7), rateY) };
                t.Sub = new Label { Text = "", AutoSize = false, Size = new Size(tileW - UiTheme.S(14), subH), Font = UiTheme.Chip, ForeColor = UiTheme.Faint, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(7), subY) };
                t.Box.Controls.Add(t.Name);
                t.Box.Controls.Add(t.Value);
                t.Box.Controls.Add(t.Rate);
                t.Box.Controls.Add(t.Sub);
                Controls.Add(t.Box);
                _tiles[i] = t;
            }
            Height = tileTop + tileH;

            StyleChips();
            VisibleChanged += (s, e) => { if (Visible) RefreshTiles(); };
        }

        private void StyleChips()
        {
            for (int i = 0; i < _chips.Length; i++)
                UiTheme.ApplyState(_chips[i], i == _window ? UiTheme.Accent : UiTheme.BtnFace,
                    i == _window ? Color.White : UiTheme.Ink);
        }

        public void TickGrowth()
        {
            if (!Visible) return;
            if ((DateTime.UtcNow - _lastTick).TotalSeconds < 5) return;
            _lastTick = DateTime.UtcNow;
            RefreshTiles();
        }

        private void Set(int i, string value, double? perHour, string sub)
        {
            var t = _tiles[i];
            UiLayout.FitInto(t.Value, value);
            if (perHour == null)
            {
                UiLayout.FitInto(t.Rate, "Sampling…");
                t.Rate.ForeColor = UiTheme.Faint;
            }
            else
            {
                UiLayout.FitInto(t.Rate, $"+{Fmt(Math.Max(0, perHour.Value))}/hr");
                t.Rate.ForeColor = perHour.Value > 0 ? UiTheme.Cap : UiTheme.Faint;
            }
            UiLayout.FitInto(t.Sub, sub);
        }

        private void RefreshTiles()
        {
            try
            {
                var newest = GrowthTracker.Newest;
                if (newest == null)
                {
                    for (int i = 0; i < _tiles.Length; i++) Set(i, "—", null, "");
                    return;
                }
                double win = ChipWindows[_window];

                // EXP — value, windowed rate, and the ratio-walk status (balance % + next chunk),
                // so the guide-ratio progress is visible on the Status page (not just Top Actions).
                // Rates/run-deltas read the GAIN counters (G*): spending must never count them
                // down (user rule) — only a rebirth resets the RUN window.
                double r;
                bool expRate = GrowthTracker.Rate(s => s.GExp, win, false, out r);
                string expSub = $"+{Fmt(GrowthTracker.RunDelta(s => s.GExp))} this run";
                Color expSubColor = UiTheme.Faint;
                try
                {
                    var xb = ExpBalancer.Analyze();
                    if (xb.Known && xb.Balanced) { expSub = "on guide ratio ✓"; expSubColor = UiTheme.Cap; }
                    else if (xb.Known) { expSub = $"ratio {xb.BalancePct:0}% ▸ {xb.NextShort}"; expSubColor = UiTheme.Energy; }
                }
                catch { }
                Set(0, Fmt(newest.Exp), expRate ? r : (double?)null, expSub);
                _tiles[0].Sub.ForeColor = expSubColor;

                // AP — the pit is its main drip; say when the next toss lands.
                string apSub = "";
                try
                {
                    apSub = MoneyPitManager.MoneyPitReady() ? "pit toss READY"
                        : $"next pit toss {FmtEta(MoneyPitManager.TimeUntilReady())}";
                }
                catch { }
                Set(1, Fmt(newest.Ap),
                    GrowthTracker.Rate(s => s.GAp, win, false, out r) ? r : (double?)null,
                    apSub);

                // PP
                bool inItopod = false;
                try { inItopod = Settings != null && (Settings.AdventureTargetITOPOD || Settings.SnipeZone >= 1000); } catch { }
                bool ppHasRate = GrowthTracker.Rate(s => s.GPp, win, false, out r);
                Set(2, Fmt(newest.Pp),
                    ppHasRate ? r : (double?)null,
                    ppHasRate && r <= 0 && !inItopod ? "not in ITOPOD" : $"+{Fmt(GrowthTracker.RunDelta(s => s.GPp))} this run");

                // CUBE P/T — rate over both combined.
                bool cubeHasRate = GrowthTracker.Rate(s => s.GCubeP + s.GCubeT, win, false, out r);
                Set(3, $"{Fmt(newest.CubeP)} / {Fmt(newest.CubeT)}",
                    cubeHasRate ? r : (double?)null,
                    $"+{Fmt(GrowthTracker.RunDelta(s => s.GCubeP + s.GCubeT))} this run");

                // NGU LEVELS — per-run metric; measured vs NGUAdvisors prediction calibrates the
                // tick-rate constant (predicted = the plan's levels/hr at each target's share).
                bool nguHasRate = GrowthTracker.Rate(s => s.GNgu, win, true, out r);
                string nguSub = "resets each rebirth";
                try
                {
                    var plan = NGUAdvisors.Compute(
                        ChallengeOverlay.ChapterNguIds(ResourceType.Energy),
                        ChallengeOverlay.ChapterNguIds(ResourceType.Magic));
                    if (plan.Known)
                    {
                        double pred = plan.Energy.Where(x => plan.EnergyTargets.Contains(x.Id)).Sum(x => x.Lph)
                                    + plan.Magic.Where(x => plan.MagicTargets.Contains(x.Id)).Sum(x => x.Lph);
                        if (pred > 0 && nguHasRate && r > 0)
                            nguSub = $"predicted {Fmt(pred)}/hr — {r / pred:0%}";
                        else if (pred > 0)
                            nguSub = $"predicted {Fmt(pred)}/hr";
                    }
                }
                catch { }
                Set(4, Fmt(newest.Ngu), nguHasRate ? r : (double?)null, nguSub);
            }
            catch (Exception ex) { LogDebug($"Growth panel: {ex.Message}"); }
        }

        private static string FmtEta(double seconds)
        {
            if (seconds >= 3600) return $"{seconds / 3600:0.#}h";
            return $"{Math.Max(0, seconds / 60):0}m";
        }

        private static string Fmt(double v) => NumberFormatter.Abbrev(v);   // consolidated (finding #31); handles negative deltas
    }
}
