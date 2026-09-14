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
        // Sink INTENT, three states (Off / Advisor decides / Push to): the old checkbox said only
        // "allowed", so a ceiling the user typed read like a goal and behaved like a filter.
        private LineComboBox _spagMode, _goldMode;
        private Label _spagStat, _goldStat, _numStat, _swapNote;
        private Label _spagWhy, _goldWhy, _numWhy;   // WHY this sink is / isn't taking blood right now
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

        // Sink-row geometry: caption | intent dropdown | ceiling | live status, with a muted WHY line
        // under it. The ceiling column is measured, never tuned: UiTheme.Num is font-derived, so a
        // hardcoded spinner width goes stale silently (ui-infra.md, "bigger digits are WIDER").
        private const int SinkCapX = 10, SinkModeX = 214;
        private static int SinkNumX => UiTheme.S(SinkModeX) + UiTheme.S(146);
        private static int SinkStatX => SinkNumX + UiTheme.NumWidthFor("100000") + UiTheme.S(14);

        private void MkSinkCaption(string text, int y)
        {
            Controls.Add(new Label { Text = text, AutoSize = false, Size = new Size(UiTheme.S(198), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(SinkCapX), y + UiTheme.S(4)) });
        }

        private void MkColLabel(string text, int y)
        {
            Controls.Add(new Label { Text = text, AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(SinkModeX), y + UiTheme.S(4)) });
        }

        // Off / Advisor decides / Push to — the three states a sink can be in. LineComboBox, not
        // ComboBox: Mono snaps a plain dropdown back to a 96-DPI height (ui-infra.md).
        private LineComboBox MkMode(int y, Action<SinkMode> set)
        {
            var cb = new LineComboBox { Location = new Point(UiTheme.S(SinkModeX), y), Width = UiTheme.S(138), Font = UiTheme.Ui, DropDownStyle = ComboBoxStyle.DropDownList };
            cb.Items.AddRange(new object[] { "Off", "Advisor decides", "Push to" });
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

        private NumericUpDown MkSinkNum(int y, int min, int max, Action<decimal> set)
        {
            var n = new NumericUpDown { Location = new Point(SinkNumX, y), Width = UiTheme.NumWidthFor("100000"), Minimum = min, Maximum = max, Font = UiTheme.Ui };
            UiTheme.StyleNum(n);
            n.ValueChanged += (s, e) => { if (_syncing || Settings == null) return; try { set(n.Value); RefreshStatus(); } catch (Exception ex) { LogDebug($"Blood sink num: {ex.Message}"); } };
            Controls.Add(n);
            return n;
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

        // The WHY line: the gate that is actually deciding this sink, in plain words. Without it the
        // panel named only the winner, so a user whose Counterfeit ceiling was nowhere near binding had
        // no way to see that the cost-curve knee was what kept handing the pool back to NUMBER.
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

            // Auto Spell Swap runs ONLY in Main's manual path (`AutoSpellSwap && !CastBloodSpells`), so with
            // automation on it is a dead switch. It used to sit here lit green and doing nothing.
            _swapNote = new Label { Text = "", AutoSize = false, Size = new Size(_w - UiTheme.S(54), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(10), top + UiTheme.S(164)) };
            Controls.Add(_swapNote);

            // No "Pill ≥" input: IronPillThreshold is dead — the advisor casts the pill on
            // BloodPlanner timing (CastIronNow), nothing reads a manual blood threshold anymore.
            int cx = UiTheme.S(10);
            _guffAThr = MkNum("Guff A ≥", ref cx, top + UiTheme.S(192), 0, 100000, v => Settings.BloodMacGuffinAThreshold = (int)v);
            _guffBThr = MkNum("Guff B ≥", ref cx, top + UiTheme.S(192), 0, 100000, v => Settings.BloodMacGuffinBThreshold = (int)v);

            // SINKS. The game caps neither log sink, so the ceiling has to come from the user: without one
            // Counterfeit/Spaghetti holds the pool for the rest of the run once it wins the routing.
            // Checkbox = permission, number = ceiling (0 = none, as BloodNumberThreshold's 0 = no floor);
            // inside what they allow BloodPlanner's own gates still choose. Before this the two % fields
            // were read ONLY by Main's manual AutoSpellSwap path, so in ADVISOR mode they were dead.
            MkHead("SINKS — ONE AT A TIME; THE GAME SPLITS BLOOD EVENLY BETWEEN WHICHEVER ARE ON", UiTheme.S(10), top + UiTheme.S(230));

            // Row pitch is DERIVED from the two lines a row holds (a value line + a WHY line), never a
            // tuned constant — that is what clipped stacked lines at 200 % scaling before.
            int rowPitch = UiTheme.LinePitch + UiTheme.HeadPitch;
            int y = top + UiTheme.S(256);
            MkSinkCaption("Spaghetti — drop chance", y);
            _spagMode = MkMode(y, SetSpagMode);
            _spag = MkSinkNum(y, 0, 100000, v => Settings.SpaghettiThreshold = (int)v);
            _spagStat = MkSinkStatus(y);
            _spagWhy = MkSinkWhy(y + UiTheme.LinePitch);

            y += rowPitch;
            MkSinkCaption("Counterfeit Gold — GPS", y);
            _goldMode = MkMode(y, SetGoldMode);
            // Counterfeit has NO game-side cap (goldBonus = 1 + floor((log2(blood/min)+1)^2)/100,
            // decomp AllBloodMagicController:105) — the old max of 100 falsely capped the target.
            _counter = MkSinkNum(y, 0, 100000, v => Settings.CounterfeitThreshold = (int)v);
            _goldStat = MkSinkStatus(y);
            _goldWhy = MkSinkWhy(y + UiTheme.LinePitch);

            // NUMBER has no intent dropdown: it is the FALLBACK sink (FillRouting's default branch), so
            // "off" is not a state it can be in — and its number is a FLOOR, not a ceiling.
            y += rowPitch;
            MkSinkCaption("NUMBER — rebirth multi", y);
            MkColLabel("floor", y);
            _numberThr = new TextBox { Location = new Point(SinkNumX, y), Width = UiTheme.NumWidthFor("100000"), Font = UiTheme.Ui, Height = UiTheme.LineH };
            _numberThr.TextChanged += (s2, e2) =>
            {
                if (_syncing || Settings == null) return;
                // Finite and non-negative only: an Infinity floor (typed, or round-tripped through this
                // very box's ToString) is never reached, so NUMBER would own the routing forever.
                if (double.TryParse(_numberThr.Text, out var d) && !double.IsNaN(d) && !double.IsInfinity(d) && d >= 0)
                {
                    try { Settings.BloodNumberThreshold = d; } catch { }
                }
            };
            Controls.Add(_numberThr);
            _numStat = MkSinkStatus(y);
            _numWhy = MkSinkWhy(y + UiTheme.LinePitch);

            _advice = new Label { Text = "", AutoSize = false, Size = new Size(_w - UiTheme.S(54), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(10), y + rowPitch + UiTheme.S(6)) };
            Controls.Add(_advice);
        }

        // The two flags behind one dropdown: permission (may this sink run at all) and push (does the
        // user want the bonus regardless of the advisor's own opinion of its value).
        private static void SetSpagMode(SinkMode m)
        {
            Settings.BloodWantSpaghetti = m != SinkMode.Off;
            Settings.BloodPushSpaghetti = m == SinkMode.Push;
        }

        private static void SetGoldMode(SinkMode m)
        {
            Settings.BloodWantCounterfeit = m != SinkMode.Off;
            Settings.BloodPushCounterfeit = m == SinkMode.Push;
        }

        private static string Fmt(double v) => NumberFormatter.Abbrev(v);

        private static string SinkStatus(int now, int target, SinkMode mode)
        {
            if (mode == SinkMode.Off) return $"now {now}% — off";
            if (target <= 0) return $"now {now}% — no ceiling";
            return now < target ? $"now {now}% → {target}%" : $"now {now}% — target reached";
        }   // consolidated (finding #31); handles negative deltas

        // The WHY line. "not routed: <gate>" names the gate that is actually deciding — and when that
        // gate is one Push may overrule, it says so, because the fix is a dropdown away.
        private static string SinkWhy(bool routing, SinkVerdict v, SinkMode mode, string detail)
        {
            string head;
            if (routing) head = "routing now";
            else if (v == SinkVerdict.Eligible) head = "eligible — another sink holds the pool";
            else head = "not routed: " + BloodRouter.Describe(v);
            if (mode == SinkMode.Auto && (v == SinkVerdict.NoDemand || v == SinkVerdict.PastKnee))
                head += " — switch to \"Push to\" to invest anyway";
            return string.IsNullOrEmpty(detail) ? head : head + " · " + detail;
        }

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

                // Dead switch while automation owns the spells — Main only runs it when CastBloodSpells
                // is OFF, so lighting it green there advertised a control that does nothing.
                bool swapLive = !Settings.CastBloodSpells;
                _swap.Enabled = swapLive;
                UiTheme.ApplyState(_swap, !swapLive ? UiTheme.Faint : (Settings.AutoSpellSwap ? UiTheme.Cap : UiTheme.Danger), Color.White);
                _swapNote.Text = swapLive ? "" : "Auto Spell Swap applies only while AUTOMATION is off — the advisor owns the spell toggles.";
                UiTheme.ApplyState(_pillRb, Settings.IronPillOnRebirth ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_guffARb, Settings.BloodMacGuffinAOnRebirth ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_guffBRb, Settings.BloodMacGuffinBOnRebirth ? UiTheme.Cap : UiTheme.Danger, Color.White);
                _guffAThr.Value = Clamp(_guffAThr, Settings.BloodMacGuffinAThreshold);
                _guffBThr.Value = Clamp(_guffBThr, Settings.BloodMacGuffinBThreshold);
                _spag.Value = Clamp(_spag, Settings.SpaghettiThreshold);
                _counter.Value = Clamp(_counter, Settings.CounterfeitThreshold);
                var spagMode = BloodPlanner.Mode(false);
                var goldMode = BloodPlanner.Mode(true);
                _spagMode.SelectedIndex = (int)spagMode;
                _goldMode.SelectedIndex = (int)goldMode;
                // The ceiling is meaningless while the sink is Off — grey it rather than leaving a live
                // spinner that changes nothing.
                _spag.Enabled = spagMode != SinkMode.Off;
                _counter.Enabled = goldMode != SinkMode.Off;
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

                // Sink rows: current bonus against the user's ceiling, read through BloodPlanner so the
                // panel and the routing can never disagree about what "reached" means.
                int spagNow = BloodPlanner.SpaghettiPercentNow(c);
                int goldNow = BloodPlanner.CounterfeitPercentNow(c);
                var spagMode = BloodPlanner.Mode(false);
                var goldMode = BloodPlanner.Mode(true);
                UiLayout.FitInto(_spagStat, SinkStatus(spagNow, Settings.SpaghettiThreshold, spagMode));
                UiLayout.FitInto(_goldStat, SinkStatus(goldNow, Settings.CounterfeitThreshold, goldMode));
                UiLayout.FitInto(_spagWhy, plan.RouteKnown
                    ? SinkWhy(plan.WantLoot, plan.LootVerdict, spagMode, plan.LootDetail)
                    : "auto-spells locked until boss 37");
                UiLayout.FitInto(_goldWhy, plan.RouteKnown
                    ? SinkWhy(plan.WantGold, plan.GoldVerdict, goldMode, plan.GoldDetail)
                    : "auto-spells locked until boss 37");
                double rp = 1;
                try { rp = c.bloodMagic.rebirthPower; } catch { }
                double floor = Settings.BloodNumberThreshold;
                UiLayout.FitInto(_numStat, floor <= 0
                    ? $"now x{Fmt(rp)} — no floor"
                    : (rp < floor ? $"now x{Fmt(rp)} → floor {Fmt(floor)}" : $"now x{Fmt(rp)} — floor met"));
                UiLayout.FitInto(_numWhy, plan.WantRebirth
                    ? "routing now" + (plan.PoolForPill ? " (paused — pooling for the pill)" : "")
                    : "fallback sink — takes the pool whenever nothing above it is eligible");

                string advice = !plan.Known ? "Blood advisor idle." : plan.Text;
                if (plan.Known && !string.IsNullOrEmpty(plan.RouteReason)) advice += $" — {plan.RouteReason}";
                UiLayout.FitOrGrow(_advice, advice);
            }
            catch (Exception e) { LogDebug($"Blood panel: {e.Message}"); }
        }
    }
}
