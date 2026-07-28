using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Systems > YGGDRASIL, Y1 "orchard grid" (user-approved): every unlocked fruit is a live tile —
    // name, tier bar filling toward harvest (fruits grow 1s/s, ~1h per tier), ETA to max — maxed
    // tiles glow gold, inactive fade. Header = MANAGED toggle + next-harvest status + Harvest Now
    // (gated by the Harvest Safety toggle, mirroring the legacy guard). Manual strip = Activate
    // Fruits / Swap Loadouts + tier threshold. Advisor line below reads the poop placement.
    public class YggPanel : Panel
    {
        // B1 game-mimic bar: fills over ONE TIER's hour and resets, fill color alternating per tier
        // (green/blue; gold when maxed) with the status text INSIDE. Text-in-fill is the two-layer
        // clipped-label trick: a dark base label spans the bar; the fill panel on top carries an
        // identical white label at the same coordinates, clipped to the fill width as it grows.
        private class Tile
        {
            public Panel Box;
            public Label Name;
            public Panel Dot;      // top-right poop marker: brown = advisor's best target, grey = current-but-suboptimal
            public Panel BarOuter;
            public Label TxtBase;
            public Panel Fill;
            public Label TxtFill;
        }

        private static readonly Color PoopBrown = Color.FromArgb(139, 94, 59);

        private class FruitInfo
        {
            public int Idx;
            public string Name;
            public bool Locked;
            public long UnlockCost;
            public bool Active;
            public bool Max;
            public int HTier;
            public double Frac;
            public double Eta;
            public bool Poop;
        }

        private static bool SafeFlag(Func<bool> get)
        {
            try { return get(); } catch { return false; }
        }

        // Both text layers get the same fitted string; the fill layer is revealed as the bar grows.
        // Elastic tiles (round-3): every width reads from the tile's own bar, no fixed 105px.
        private static void SetBar(Tile t, double frac, Color fill, string text, Color baseFg)
        {
            // MEASURE WITH THE FONT THAT PAINTS. These two labels render in UiTheme.Ui (9pt) but were
            // measured against UiTheme.Chip (7.5pt), so the fit believed more fitted than does — and a
            // fixed Mono label with overflowing text paints it CUT, with no ellipsis to hint at it
            // ("UNLOCK: 100K SEEDS" arrived as "UNLOCK: 100K SEE"). Reading the label's own Font keeps
            // the two from drifting apart again.
            // Both layers show the same fitted string, and both carry the full one as a tooltip — the fill
            // layer is what the pointer actually lands on once the bar has grown over the base.
            UiLayout.FitInto(t.TxtBase, text);
            string fitted = t.TxtBase.Text;
            t.TxtBase.ForeColor = baseFg;
            t.TxtFill.Text = fitted;
            UiLayout.Tip(t.TxtFill, fitted == text ? null : text);
            t.Fill.BackColor = fill;
            t.TxtFill.BackColor = fill;
            t.Fill.Width = (int)((t.BarOuter.Width - 2) * Math.Max(0, Math.Min(1, frac)));
        }

        private static string FmtSeeds(long n)
        {
            if (n >= 1000000) return $"{n / 1000000.0:0.#}M";
            if (n >= 1000) return $"{n / 1000.0:0.#}K";
            return n.ToString();
        }

        private const int MaxFruits = 21;
        private readonly int _w;
        private readonly int _tileW;
        // Orchard columns: bigger tiles (~165px pitch) so fruit names + the tier/ETA bar text read
        // clearly — ~6 across the full canvas, min 4 in a narrow cell.
        private int Cols => Math.Max(4, (_w - UiTheme.S(14)) / UiTheme.S(165));
        // Yggdrasil is the ONE true duplicate the ownership audit found: this panel's old
        // MANAGED/UNMANAGED button and Settings' "MANAGE › Yggdrasil" checkbox wrote the SAME field
        // (Settings.ManageYggdrasil). It has no advisor strategy layer at all — nothing reads an
        // "AdvisorYggdrasil" because none exists (AdvisorYggBuys is a different thing: EXP purchases).
        // So the bar shows AUTOMATION only, and says so rather than implying a decision to make.
        private SystemControlBar _controlBar;
        // Grid origin + action-row Y, both derived from the bar's height so nothing overlaps it.
        private readonly int _rowY;
        private readonly int _gridTop;
        // Tile geometry is DERIVED from the title line it holds, not scaled beside it: the title's
        // height is floored at the measured text box (UiTheme.SText), so a scaled bar offset and a
        // scaled tile height would both be overrun from the moment 9pt renders taller than 25px.
        // _tileH is the box; _tilePitch is the row step that keeps the orchard rows apart.
        private readonly int _tileH;
        private readonly int _tilePitch;
        private Label _info;
        private Button _harvestNow;
        private Button _safety;
        private Button _refresh;
        private readonly List<Tile> _tiles = new List<Tile>();
        private Button _activate;
        private Button _swap;
        private Button _swapDig;
        private Button _swapBeard;
        private Label _tierLbl;
        private NumericUpDown _swapTier;
        private Label _advice;
        private bool _syncing;
        private bool _safetyOn;

        // canvasW: explicit canvas width when hosted in an M1 section column (0 = UiLayout.PanelW).
        public YggPanel(int canvasW = 0)
        {
            _w = canvasW > 0 ? canvasW : UiLayout.PanelW;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;
            // Quad-cell hosting: late-game fruit counts outgrow the cell — scroll inside the panel
            // rather than clip (the section canvas keeps its own scroll for the whole quad).
            AutoScroll = true;

            _rowY = UiTheme.S(10) + SystemControlBar.BarHeight + UiTheme.S(8);
            // The action row's buttons are floored at the full line box, so the grid has to clear
            // SCtl(24) — a scaled 24 leaves the first tile row sitting on top of them.
            _gridTop = _rowY + UiTheme.SCtl(24) + UiTheme.S(8);

            _controlBar = new SystemControlBar(
                _w - UiTheme.S(54),
                () => Settings.ManageYggdrasil, v => Settings.ManageYggdrasil = v,
                null, null,   // no decisions layer exists for Yggdrasil harvesting
                null, null,
                "Automation is off — the tool will not harvest Yggdrasil.",
                "The tool harvests on your rules below. Yggdrasil has no advisor strategy to choose.")
            {
                Location = new Point(UiTheme.S(10), UiTheme.S(10))
            };
            _controlBar.Changed += SyncFromSettings;
            Controls.Add(_controlBar);

            _info = new Label { Text = "…", AutoSize = false, Size = new Size(UiTheme.S(240), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            _harvestNow = MkBtn("Harvest Now");
            UiTheme.StyleFlat(_harvestNow);
            _harvestNow.Click += (s, e) =>
            {
                // Each outcome is REPORTED, not inferred: the handler returning is not success. No log
                // link — these lines say all there is to say, and inject.log carries nothing more.
                if (!_safetyOn)
                {
                    Log("Harvest Safety is off — flip it green first.");
                    Activity.Failed("Harvest Safety is off — flip it green first.");
                    return;
                }
                if (!YggdrasilManager.AnyHarvestable())
                {
                    Log("Nothing harvestable yet.");
                    Activity.Failed("Nothing harvestable yet.");
                    return;
                }
                // R6/R8 already restore the lock, loadout and MacGuffins and rethrow the primary — but that
                // primary escapes into the WinForms/Unity pump with no advisor diagnostic and (worse) would
                // otherwise fall through to Activity.Completed. Bound the acquire+harvest here.
                bool beganHarvest;
                try
                {
                    beganHarvest = LockManager.TryYggdrasilSwap(true);
                    if (beganHarvest)
                        YggdrasilManager.HarvestAll(true);
                }
                catch (Exception ex)
                {
                    try { Activity.Failed("Harvest failed. See Logs.", null, true); } catch { }
                    try { LogDebug($"Manual Yggdrasil harvest failed:\n{ex}"); } catch { }
                    return;
                }

                if (!beganHarvest)
                {
                    Log("Unable to harvest now");
                    Activity.Failed("Could not harvest — the swap was blocked.");
                    try { RefreshTiles(); } catch (Exception rex) { try { LogDebug($"Yggdrasil panel refresh failed:\n{rex}"); } catch { } }
                    return;
                }

                // The harvest completed (irreversible). Completion report and the cosmetic refresh are bounded
                // separately so neither can reclassify it as a failure.
                try { Activity.Completed("Yggdrasil harvested."); }
                catch (Exception reportEx) { try { LogDebug($"Manual Yggdrasil harvest completion report failed:\n{reportEx}"); } catch { } }

                try { RefreshTiles(); }
                catch (Exception refreshEx)
                {
                    try { Activity.Warning("Harvest completed, but the panel could not refresh."); } catch { }
                    try { LogDebug($"Manual Yggdrasil harvest UI refresh failed:\n{refreshEx}"); } catch { }
                }
            };
            _safety = MkBtn("Harvest Safety");
            _safety.Click += (s, e) => { _safetyOn = !_safetyOn; SyncFromSettings(); };
            _refresh = new Button { Text = "↻", Size = new Size(Math.Max(UiTheme.S(36), UiLayout.BtnWidth("↻")), UiTheme.SCtl(24)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(_refresh);
            _refresh.Click += (s, e) => RefreshTiles();
            Controls.Add(_info);
            Controls.Add(_harvestNow);
            Controls.Add(_safety);
            Controls.Add(_refresh);
            UiLayout.Row(UiTheme.S(10), _rowY, UiTheme.S(8), _info, _harvestNow, _safety, _refresh);

            // Elastic tiles: width computed from the cell (scrollbar allowance included) so the
            // orchard fills whatever column hosts it — no fixed 117px pitch, no horizontal scroll.
            _tileW = ((_w - UiTheme.S(20) - UiTheme.S(17)) - (Cols - 1) * UiTheme.S(6)) / Cols;

            // Stack the tile downwards from its own parts instead of placing each at a scaled offset.
            int nameY = UiTheme.S(4);
            int nameH = UiTheme.SText(18);
            int barY = nameY + nameH + UiTheme.S(2);
            int barH = UiTheme.S(30);
            _tileH = barY + barH + UiTheme.S(6);      // the tuned 6px below the bar
            _tilePitch = _tileH + UiTheme.S(6);       // the tuned 6px between rows
            for (int i = 0; i < MaxFruits; i++)
            {
                var t = new Tile();
                t.Box = new Panel { Size = new Size(_tileW, _tileH), BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle, Visible = false };
                t.Name = new Label { Text = "", AutoSize = false, Size = new Size(_tileW - UiTheme.S(24), nameH), Font = UiTheme.Bold, ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(6), nameY) };
                t.Dot = new Panel { Location = new Point(_tileW - UiTheme.S(14), UiTheme.S(6)), Size = new Size(UiTheme.S(8), UiTheme.S(8)), BackColor = UiTheme.Surface, Visible = false };
                t.BarOuter = new Panel { Location = new Point(UiTheme.S(6), barY), Size = new Size(_tileW - UiTheme.S(12), barH), BackColor = UiTheme.Zebra, BorderStyle = BorderStyle.FixedSingle };
                t.TxtBase = new Label { Text = "", AutoSize = false, Size = new Size(_tileW - UiTheme.S(12) - 2, barH - 2), Font = UiTheme.Ui, ForeColor = UiTheme.Ink, BackColor = UiTheme.Zebra, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(0, 0), Tag = "exclusive" };
                t.Fill = new Panel { Location = new Point(0, 0), Size = new Size(0, barH - 2), BackColor = UiTheme.Cap, Tag = "exclusive" };
                t.TxtFill = new Label { Text = "", AutoSize = false, Size = new Size(_tileW - UiTheme.S(12) - 2, barH - 2), Font = UiTheme.Ui, ForeColor = Color.White, BackColor = UiTheme.Cap, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(0, 0) };
                t.Fill.Controls.Add(t.TxtFill);
                t.BarOuter.Controls.Add(t.TxtBase);
                t.BarOuter.Controls.Add(t.Fill);
                t.Fill.BringToFront();
                t.Box.Controls.Add(t.Name);
                t.Box.Controls.Add(t.Dot);
                t.Box.Controls.Add(t.BarOuter);
                Controls.Add(t.Box);
                _tiles.Add(t);
            }

            _activate = MkBtn("Activate Fruits");
            _activate.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.ActivateFruits = !Settings.ActivateFruits;
                SyncFromSettings();
            };
            _swap = MkBtn("Swap Loadouts");
            _swap.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.SwapYggdrasilLoadouts = !Settings.SwapYggdrasilLoadouts;
                SyncFromSettings();
            };
            _tierLbl = new Label { Text = "at tier", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            _swapTier = new NumericUpDown { Width = UiTheme.S(48), Minimum = 1, Maximum = 20, Font = UiTheme.Ui };
            UiTheme.StyleNum(_swapTier);
            _swapTier.ValueChanged += (s, e) => { if (!_syncing && Settings != null) Settings.YggSwapThreshold = (int)_swapTier.Value; };
            Controls.Add(_activate);
            Controls.Add(_swap);
            Controls.Add(_tierLbl);
            Controls.Add(_swapTier);
            UiLayout.Row(UiTheme.S(10), UiTheme.S(226), UiTheme.S(8), _activate, _swap, _tierLbl, _swapTier);

            // Re-homed from the retired Old Yggdrasil page (Phase B): harvest-swap companions.
            _swapDig = MkBtn("Swap Diggers");
            _swapDig.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.SwapYggdrasilDiggers = !Settings.SwapYggdrasilDiggers;
                SyncFromSettings();
            };
            _swapBeard = MkBtn("Swap Beards");
            _swapBeard.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.SwapYggdrasilBeards = !Settings.SwapYggdrasilBeards;
                SyncFromSettings();
            };
            Controls.Add(_swapDig);
            Controls.Add(_swapBeard);
            UiLayout.Row(UiTheme.S(10), UiTheme.S(258), UiTheme.S(8), _swapDig, _swapBeard);

            _advice = new Label { Text = "", AutoSize = false, Size = new Size(_w - UiTheme.S(54), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(10), UiTheme.S(290)) };
            Controls.Add(_advice);

            VisibleChanged += (s, e) => { if (Visible) RefreshTiles(); };
            SyncFromSettings();
        }

        private static Button MkBtn(string text)
        {
            var b = new Button { Text = text, Size = new Size(UiLayout.BtnWidth(text), UiTheme.SCtl(24)), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat };
            b.FlatAppearance.BorderColor = UiTheme.Border;
            return b;
        }

        public void SyncFromSettings()
        {
            if (Settings == null) return;
            _syncing = true;
            try
            {
                // Reflects a flip made here, from the Settings checkbox, or from a settings reload.
                _controlBar?.Sync();
                UiTheme.ApplyState(_safety, _safetyOn ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_activate, Settings.ActivateFruits ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_swap, Settings.SwapYggdrasilLoadouts ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_swapDig, Settings.SwapYggdrasilDiggers ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_swapBeard, Settings.SwapYggdrasilBeards ? UiTheme.Cap : UiTheme.Danger, Color.White);
                int v = Math.Max(1, Math.Min(20, Settings.YggSwapThreshold));
                _swapTier.Value = v;
            }
            finally { _syncing = false; }
            RefreshTiles();
        }

        private static string ShortName(string full)
        {
            if (string.IsNullOrEmpty(full)) return "?";
            return full.StartsWith("Fruit of ", StringComparison.OrdinalIgnoreCase) ? full.Substring(9) : full;
        }

        private static string FmtEta(double s)
        {
            if (s <= 0) return "now";
            if (s >= 3600) return $"{s / 3600:0.#}h";
            return $"{s / 60:0}m";
        }

        // Mono blanks a fixed-size label whose text overflows — everything variable gets fitted.
        private static string Fit(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (UiLayout.MeasureText(text, font) <= width) return text;
            while (text.Length > 1 && UiLayout.MeasureText(text + "…", font) > width)
                text = text.Substring(0, text.Length - 1);
            return text + "…";
        }

        // Poop priority (guide verbatim: "poop Pom ALWAYS, others at max"): Pomegranate first,
        // then macguffins > knowledge (EXP) > quirk > luck > gold > adventure.
        private static int PoopRank(string shortName)
        {
            string n = (shortName ?? "").ToLowerInvariant();
            if (n.Contains("pomegranate")) return 0;
            if (n.Contains("macguffin") && n.Contains("beta")) return 1;
            if (n.Contains("macguffin")) return 2;
            if (n.Contains("knowledge")) return 3;
            if (n.Contains("quirk")) return 4;
            if (n.Contains("luck")) return 5;
            if (n.Contains("gold")) return 6;
            if (n.Contains("adventure")) return 7;
            return 9;
        }

        private void RefreshTiles()
        {
            try
            {
                var c = Main.Character;
                if (c == null || Settings == null) return;
                var yc = c.yggdrasilController;
                var fruits = c.yggdrasil.fruits;
                if (yc == null || fruits == null) return;
                var fc = yc.fruits[0];
                float thr = fc.tierThreshold();

                // Pass 1: collect visible fruits. maxTier==0 means NOT YET BOUGHT, not irrelevant —
                // the unlock IS the first seed purchase (game increments maxTier from 0). Those show
                // as LOCKED tiles with their cost. Content-gated fruits (troll-only id 8, ITOPOD 9,
                // titan achievement 10, beast 14, cards 15-20) stay hidden until their gate opens.
                var infos = new List<FruitInfo>();
                for (int i = 0; i < fruits.Count && infos.Count < _tiles.Count; i++)
                {
                    var f = fruits[i];
                    bool locked = f.maxTier == 0;
                    if (locked)
                    {
                        // Near-term reveal (user request): show every locked fruit as a placeholder tile
                        // with its real in-game name so the orchard reads complete — but keep the deep
                        // card-gated "Mayo" fruits (index 15+) hidden until the Cards feature is on, so
                        // they don't crowd the early-game orchard.
                        bool gateOpen = i < 15 || SafeFlag(() => c.cards.cardsOn);
                        if (!gateOpen) continue;
                    }
                    var fi = new FruitInfo { Idx = i, Locked = locked, Poop = f.usePoop, Active = f.activated };
                    try { fi.Name = ShortName(yc.fruitName[i]); } catch { fi.Name = "?"; }
                    if (locked)
                    {
                        try { fi.UnlockCost = yc.baseSeedCost[i]; } catch { }
                    }
                    else
                    {
                        try { fi.HTier = fc.harvestTier(i); fi.Max = fc.fruitMaxxed(i); } catch { }
                        // B1 bar: progress through the CURRENT tier's hour, ETA to the NEXT tier.
                        double into = thr > 0 ? f.seconds % thr : 0;
                        fi.Frac = thr > 0 ? into / thr : 0;
                        fi.Eta = Math.Max(0, thr - into);
                    }
                    infos.Add(fi);
                }

                int poopCount = infos.Count(x => x.Poop);
                var best = infos.Where(x => x.Active && !x.Locked)
                    .OrderBy(x => PoopRank(x.Name))
                    .ThenByDescending(x => fruits[x.Idx].maxTier)
                    .Take(Math.Max(3, poopCount))
                    .Select(x => x.Idx)
                    .ToList();

                long seeds = 0;
                try { seeds = c.yggdrasil.seeds; } catch { }

                int shown = 0, maxed = 0;
                foreach (var fi in infos)
                {
                    var f = fruits[fi.Idx];
                    var t = _tiles[shown];
                    int col = shown % Cols, row = shown / Cols;
                    t.Box.Location = new Point(UiTheme.S(10) + col * (_tileW + UiTheme.S(6)), _gridTop + row * _tilePitch);
                    t.Box.Visible = true;
                    shown++;

                    UiLayout.FitInto(t.Name, fi.Name);

                    // Brown dot = advisor's best poop target; grey = poop is here but a better fruit exists.
                    bool recommended = best.Contains(fi.Idx);
                    t.Dot.Visible = recommended || fi.Poop;
                    t.Dot.BackColor = recommended ? PoopBrown : UiTheme.Faint;

                    var bg = UiTheme.Surface;
                    if (fi.Locked)
                    {
                        bool affordable = fi.UnlockCost > 0 && seeds >= fi.UnlockCost;
                        t.Name.ForeColor = UiTheme.Faint;
                        SetBar(t, 0, UiTheme.Cap,
                            fi.UnlockCost > 0 ? $"UNLOCK: {FmtSeeds(fi.UnlockCost)} SEEDS" : "LOCKED",
                            affordable ? UiTheme.Cap : UiTheme.Faint);
                    }
                    else if (!fi.Active)
                    {
                        t.Name.ForeColor = UiTheme.Faint;
                        SetBar(t, 0, UiTheme.Cap, "INACTIVE", UiTheme.Faint);
                    }
                    else if (fi.Max)
                    {
                        maxed++;
                        bg = Color.FromArgb(253, 246, 233);
                        t.Name.ForeColor = UiTheme.Energy;
                        SetBar(t, 1, UiTheme.Energy, $"T{fi.HTier}/{f.maxTier} · MAXED", UiTheme.Ink);
                    }
                    else
                    {
                        t.Name.ForeColor = UiTheme.Accent;
                        // Alternating fill per tier (the game's "reset = progress" signal).
                        var fill = fi.HTier % 2 == 0 ? UiTheme.Cap : UiTheme.Accent;
                        SetBar(t, fi.Frac, fill, $"T{fi.HTier}/{f.maxTier} · {FmtEta(fi.Eta)}", UiTheme.Ink);
                    }
                    t.Box.BackColor = bg;
                    t.Name.BackColor = bg;
                }
                for (int i = shown; i < _tiles.Count; i++) _tiles[i].Box.Visible = false;

                // Reflow the strip + swap row + advice under however many rows the orchard used
                // (user-caught overlap: the Phase B swap row wasn't part of this reflow, so the
                // advice line rendered underneath the new buttons).
                int rows = (shown + Cols - 1) / Cols;
                int stripY = _gridTop + rows * _tilePitch + UiTheme.S(8);
                _activate.Top = _swap.Top = _swapTier.Top = stripY;
                _tierLbl.Top = stripY + UiTheme.S(5);
                _swapDig.Top = _swapBeard.Top = stripY + UiTheme.S(32);
                _advice.Top = stripY + UiTheme.S(64);

                UiLayout.FitInto(_info,
                    $"{(maxed > 0 ? $"NEXT HARVEST: {maxed} maxed" : "NEXT HARVEST: none maxed yet")} · SEEDS {seeds}");

                // Advisor line: current placement vs the brown-dot recommendation (+ affordable unlocks).
                var curNames = infos.Where(x => x.Poop).Select(x => x.Name).ToList();
                var bestNames = infos.Where(x => best.Contains(x.Idx)).Select(x => x.Name).ToList();
                string advice;
                if (curNames.Count == 0)
                    advice = $"Advisor: poop unassigned — best targets (brown dots): {string.Join(", ", bestNames.ToArray())}.";
                else if (curNames.All(n => bestNames.Contains(n)))
                    advice = $"Advisor: poop on {string.Join(", ", curNames.ToArray())} — matches the best targets.";
                else
                {
                    var better = bestNames.Where(n => !curNames.Contains(n)).ToList();
                    advice = $"Advisor: poop on {string.Join(", ", curNames.ToArray())} — better: {string.Join(", ", better.ToArray())} (brown dots).";
                }
                var buy = infos.Where(x => x.Locked && x.UnlockCost > 0 && seeds >= x.UnlockCost)
                    .OrderBy(x => x.UnlockCost).FirstOrDefault();
                if (buy != null)
                    advice += $" · Can unlock {buy.Name} ({buy.UnlockCost} seeds).";
                UiLayout.FitOrGrow(_advice, advice);   // last element on the panel — free to wrap
            }
            catch (Exception ex) { LogDebug($"Ygg panel: {ex.Message}"); }
        }
    }
}
