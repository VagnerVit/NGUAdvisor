using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Economy > PIT (draft-approved): three status chips — NEXT TOSS (cooldown + toss count this
    // run), PREDICTION (RNG-read outcome + its prep loadout), THROW PLAN (the shared advisor policy
    // verdict) — with the ADVISOR THROWS GOLD toggle and the manual strip (min tier, Predict, Pit
    // Run, Daily Spin, Throw Now). The plan chip renders MoneyPitManager.AdvisorPlan, the same
    // policy ApplyPit acts on, so display and behavior cannot disagree.
    public class PitPanel : Panel
    {
        private class Chip
        {
            public Panel Box;
            public Label Title;
            public Label Value;
            public Label Sub;
        }

        private Button _srcToggle;
        private Button _throwNow;
        private Button _refresh;
        private readonly Chip[] _chips = new Chip[3];

        private Panel _manualStrip;
        private Button _autoThrow;
        private ComboBox _minTier;
        private Button _predict;
        private Button _pitRun;
        private Button _dailySpin;
        private Button _swapDiggers;
        private Button _daycare;
        private NumericUpDown _daycareTh;
        private Label _advisorNote;
        private Label _shockNote;

        private bool _syncing;
        private const string ShockAdvice = "Shockwave set not configured — a Pit Run is worth considering once you have Worn gear to farm.";

        // canvasW: explicit canvas width when hosted in an M1 section column (0 = UiLayout.PanelW).
        public PitPanel(int canvasW = 0)
        {
            int W = canvasW > 0 ? canvasW : UiLayout.PanelW;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            _srcToggle = MkBtn("ADVISOR THROWS GOLD");
            _srcToggle.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.AdvisorPit = !Settings.AdvisorPit;
                SyncFromSettings();
            };
            _throwNow = MkBtn("Throw Now");
            UiTheme.StyleFlat(_throwNow);
            _throwNow.Click += (s, e) =>
            {
                if (Settings == null) return;
                if (!MoneyPitManager.MoneyPitReady()) { Log("Money pit is on cooldown."); return; }
                Log("Money pit: manual throw");

                // The throw engages the pit (irreversible) and R3 owns the lock cleanup — but the exception
                // still escapes into the WinForms/Unity pump with no advisor diagnostic. Bound it here: this
                // handler is the only place the primary manual fault can be reported.
                try
                {
                    MoneyPitManager.AdvisorThrow();
                }
                catch (Exception ex)
                {
                    try { Activity.Failed("Money Pit throw failed. See Logs.", null, true); } catch { }
                    try { LogDebug($"Manual Money Pit throw failed:\n{ex}"); } catch { }
                    return;
                }

                // Success reporting and the cosmetic refresh are bounded separately: a report or refresh
                // fault must not reclassify a throw that already happened as a failure.
                try { Activity.Completed("Money Pit throw completed."); }
                catch (Exception reportEx) { try { LogDebug($"Manual Money Pit throw completion report failed:\n{reportEx}"); } catch { } }

                try { RefreshChips(); }
                catch (Exception refreshEx)
                {
                    try { Activity.Warning("Money Pit throw completed, but the panel could not refresh."); } catch { }
                    try { LogDebug($"Manual Money Pit throw UI refresh failed:\n{refreshEx}"); } catch { }
                }
            };
            _refresh = new Button { Text = "↻", Size = new Size(Math.Max(UiTheme.S(36), UiLayout.BtnWidth("↻")), UiTheme.SCtl(24)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(_refresh);
            _refresh.Click += (s, e) => RefreshChips();
            Controls.Add(_srcToggle);
            Controls.Add(_throwNow);
            Controls.Add(_refresh);
            UiLayout.Row(UiTheme.S(10), UiTheme.S(10), UiTheme.S(8), _srcToggle, _throwNow, _refresh);

            string[] titles = { "NEXT TOSS", "PREDICTION", "THROW PLAN" };
            int chipW = (W - UiTheme.S(36)) / 3;   // three chips, 10px margins + 8px gaps
            int x = UiTheme.S(10);
            for (int i = 0; i < 3; i++)
            {
                var ch = new Chip();
                // Two-line sub-caption budget (round-3: "TOSS #5 THIS RUN …" never again).
                ch.Box = new Panel { Location = new Point(x, UiTheme.S(48)), Size = new Size(chipW, UiTheme.S(88)), BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle };
                ch.Title = new Label { Text = titles[i], AutoSize = true, Font = UiTheme.Chip, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(6), UiTheme.S(4)) };
                ch.Value = new Label { Text = "…", AutoSize = false, Size = new Size(chipW - UiTheme.S(12), UiTheme.SText(22)), Font = UiTheme.Bold, ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(6), UiTheme.S(22)) };
                ch.Sub = new Label { Text = "", AutoSize = false, Size = new Size(chipW - UiTheme.S(12), UiTheme.SHead(36)), Font = UiTheme.Chip, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(6), UiTheme.S(48)) };
                ch.Box.Controls.Add(ch.Title);
                ch.Box.Controls.Add(ch.Value);
                ch.Box.Controls.Add(ch.Sub);
                Controls.Add(ch.Box);
                _chips[i] = ch;
                x += chipW + UiTheme.S(8);
            }

            _manualStrip = new Panel { Location = new Point(0, UiTheme.S(148)), Size = new Size(W - UiTheme.S(4), UiTheme.S(34)), BackColor = UiTheme.Ground, Tag = "exclusive" };
            Controls.Add(_manualStrip);
            var tierLbl = new Label { Text = "min tier", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            _minTier = new LineComboBox { Width = UiTheme.S(80), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui };
            UiTheme.StyleCombo(_minTier);
            foreach (var t in MoneyPitManager.moneyPitThresholds)
                _minTier.Items.Add(MoneyPitManager.TierName(t));
            _minTier.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing || Settings == null || _minTier.SelectedIndex < 0) return;
                Settings.MoneyPitThreshold = MoneyPitManager.moneyPitThresholds[_minTier.SelectedIndex];
            };
            // AUTO THROW is Settings.AutoMoneyPit, and this panel is now its ONLY home (slice 7.6C2B — the
            // Application Settings twin is gone). It leads the strip because it is the ON/OFF for the loop the
            // rest of the strip CONFIGURES: min tier, Predict + Prep and Pit Run Mode are all inputs to
            // MoneyPitManager.CheckMoneyPit(), and until now the switch that starts it lived in another tab.
            //
            // The name is deliberately narrow. It is NOT "pit automation" — AdvisorPit is a separate,
            // independent throw path, and calling this a master gate would be the exact lie the Pit system row
            // refuses to tell. It gates the STANDARD threshold-driven throw, and that is all it says.
            //
            // Living inside the advisor-exclusive strip is honest as of 7.6C2A-1 and would NOT have been
            // before it: the standard path really is suppressed while AdvisorPit owns timing (Main.cs:821), so
            // hiding its controls in advisor mode hides things that genuinely are not running. The saved value
            // is untouched by the mode switch — flip back to MANUAL PIT and your configuration is still there.
            _autoThrow = MkTrig("AUTO THROW", () => Settings.AutoMoneyPit = !Settings.AutoMoneyPit);
            _predict = MkTrig("Predict + Prep", () => Settings.PredictMoneyPit = !Settings.PredictMoneyPit);
            _pitRun = MkTrig("Pit Run Mode", () => Settings.MoneyPitRunMode = !Settings.MoneyPitRunMode);
            _manualStrip.Controls.Add(_autoThrow);
            _manualStrip.Controls.Add(tierLbl);
            _manualStrip.Controls.Add(_minTier);
            _manualStrip.Controls.Add(_predict);
            _manualStrip.Controls.Add(_pitRun);
            // The strip's controls are floored at the full line box now, so its own 34px height can no
            // longer be assumed to contain them — take it from the row (as the Gold strip already does).
            int stripBottom = UiLayout.Row(UiTheme.S(10), UiTheme.S(4), UiTheme.S(8), _autoThrow, tierLbl, _minTier, _predict, _pitRun);
            _manualStrip.Height = Math.Max(_manualStrip.Height, stripBottom + UiTheme.S(2));

            _advisorNote = new Label
            {
                AutoSize = false,
                Size = new Size(W - UiTheme.S(20), UiTheme.TextH),
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), UiTheme.S(154)),
                Tag = "exclusive"
            };
            UiLayout.FitOrGrow(_advisorNote,
                "Throws only when TM is funded and augments stay affordable; holds when the next reward tier is close.");
            Controls.Add(_advisorNote);

            _shockNote = new Label
            {
                AutoSize = false,
                Size = new Size(W - UiTheme.S(20), UiTheme.TextH),
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), Math.Max(UiTheme.S(148) + _manualStrip.Height + UiTheme.S(8), _advisorNote.Bottom + UiTheme.S(8)))
            };
            // Reserve the wrapped height up front so the rows below never collide with it.
            UiLayout.FitOrGrow(_shockNote, ShockAdvice);
            int belowShock = _shockNote.Bottom + UiTheme.S(10);
            _shockNote.Text = "";
            Controls.Add(_shockNote);

            // The ALWAYS-VISIBLE row: switches that keep running no matter which throw path owns the pit.
            // Re-homed from the retired Old Pit page (Phase B): digger swap + daycare feed — and now Daily
            // Spin, which was living in the advisor-exclusive strip above and had no business there.
            //
            // AutoSpin is gated by GlobalEnabled and NOTHING else (Main.cs:824 -> DoDailySpin). It is a
            // different game system — dailyController, its own cooldown, no advisor policy anywhere — so the
            // strip was hiding a switch that carried on spinning the whole time it was out of sight. Same
            // trap the strip's other tenants avoid by being genuinely suppressed. Pure UI correction: same
            // field, same binding, same behavior, one control, just somewhere it isn't lying.
            _swapDiggers = MkTrig("Pit Diggers", () => Settings.SwapPitDiggers = !Settings.SwapPitDiggers);
            _daycare = MkTrig("Daycare Feed", () => Settings.MoneyPitDaycare = !Settings.MoneyPitDaycare);
            var dcLbl = new Label { Text = "daycare ≥", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            _daycareTh = new NumericUpDown { Width = UiTheme.S(60), Minimum = 0, Maximum = 100, Font = UiTheme.Ui };
            UiTheme.StyleNum(_daycareTh);
            _daycareTh.ValueChanged += (s, e) => { if (!_syncing && Settings != null) Settings.DaycareThreshold = (int)_daycareTh.Value; };
            _dailySpin = MkTrig("Daily Spin", () => Settings.AutoSpin = !Settings.AutoSpin);
            Controls.Add(_dailySpin);
            Controls.Add(_swapDiggers);
            Controls.Add(_daycare);
            Controls.Add(dcLbl);
            Controls.Add(_daycareTh);
            // Daily Spin leads: it is the one here that has nothing to do with the throw, and "Daycare Feed"
            // keeps its threshold spinner adjacent, which is the pairing that actually needs to stay together.
            UiLayout.Row(UiTheme.S(10), Math.Max(UiTheme.S(204), belowShock), UiTheme.S(8), _dailySpin, _swapDiggers, _daycare, dcLbl, _daycareTh);

            VisibleChanged += (s, e) => { if (Visible) RefreshChips(); };
            SyncFromSettings();
        }

        private static Button MkBtn(string text)
        {
            var b = new Button { Text = text, Size = new Size(UiLayout.BtnWidth(text), UiTheme.SCtl(24)), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat };
            b.FlatAppearance.BorderColor = UiTheme.Border;
            return b;
        }

        private Button MkTrig(string text, Action toggle)
        {
            var b = MkBtn(text);
            b.Click += (s, e) =>
            {
                if (Settings == null) return;
                try { toggle(); } catch (Exception ex) { LogDebug($"Pit toggle: {ex.Message}"); }
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
                bool advisor = Settings.AdvisorPit;
                _srcToggle.Text = advisor ? "ADVISOR THROWS GOLD" : "MANUAL PIT";
                UiTheme.ApplyState(_srcToggle, advisor ? UiTheme.Cap : UiTheme.Danger, Color.White);
                _manualStrip.Visible = !advisor;
                _advisorNote.Visible = advisor;

                int idx = MoneyPitManager.moneyPitThresholds.FindIndex(t => Math.Abs(t - Settings.MoneyPitThreshold) < t * 0.01);
                if (idx >= 0) _minTier.SelectedIndex = idx;
                // Reads Settings, holds no state of its own — so a settings reload, a profile load or any
                // other writer of AutoMoneyPit lands here like every other control on this panel.
                UiTheme.ApplyState(_autoThrow, Settings.AutoMoneyPit ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_predict, Settings.PredictMoneyPit ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_pitRun, Settings.MoneyPitRunMode ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_dailySpin, Settings.AutoSpin ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_swapDiggers, Settings.SwapPitDiggers ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_daycare, Settings.MoneyPitDaycare ? UiTheme.Cap : UiTheme.Danger, Color.White);
                _daycareTh.Value = Math.Max(0, Math.Min(100, Settings.DaycareThreshold));
            }
            finally { _syncing = false; }
            RefreshChips();
        }

        private void SetChip(int i, bool lit, string value, string sub)
        {
            var ch = _chips[i];
            var bg = lit ? Color.FromArgb(253, 246, 233) : UiTheme.Surface;
            ch.Box.BackColor = bg;
            ch.Title.BackColor = ch.Value.BackColor = ch.Sub.BackColor = bg;
            ch.Title.ForeColor = lit ? UiTheme.Energy : UiTheme.Muted;
            UiLayout.FitInto(ch.Value, value);
            UiLayout.WrapInto(ch.Sub, sub);
        }

        private void RefreshChips()
        {
            try
            {
                var c = Main.Character;
                if (c == null || Settings == null) return;

                // NEXT TOSS.
                bool ready = MoneyPitManager.MoneyPitReady();
                int toss = 0;
                try { toss = c.pit.tossCount; } catch { }
                string cd;
                if (ready) cd = "READY";
                else
                {
                    float t = MoneyPitManager.TimeUntilReady();
                    cd = t > 3600 ? $"in {t / 3600:0.#}h" : $"in {t / 60:0}m";
                }
                SetChip(0, ready, cd, $"TOSS #{toss + 1} THIS RUN ({toss + 2}H CD AFTER)");

                // PREDICTION.
                double gold = c.realGold;
                if (gold >= 1e13)
                {
                    var outcome = MoneyPitManager.PredictNext();
                    string prep;
                    switch (outcome)
                    {
                        case MoneyPitManager.Outcomes.IronPill: prep = "PREP: MAGIC + RITUALS"; break;
                        case MoneyPitManager.Outcomes.Worn: prep = "PREP: SHOCKWAVE SET"; break;
                        case MoneyPitManager.Outcomes.Exp: prep = "PREP: EXP GEAR"; break;
                        case MoneyPitManager.Outcomes.Pomegranate: prep = "PREP: YGG LOADOUT"; break;
                        case MoneyPitManager.Outcomes.Daycare: prep = "PREP: FILL DAYCARE"; break;
                        default: prep = "NO SPECIAL OUTCOME"; break;
                    }
                    SetChip(1, outcome != MoneyPitManager.Outcomes.None, outcome == MoneyPitManager.Outcomes.None ? "STANDARD" : outcome.ToString().ToUpperInvariant(), prep);
                }
                else
                {
                    SetChip(1, false, "STANDARD", "OUTCOMES START AT 1E13");
                }

                // THROW PLAN (advisor policy) / manual summary.
                if (Settings.AdvisorPit)
                {
                    var plan = MoneyPitManager.AdvisorPlan();
                    SetChip(2, plan.Throw, plan.Verdict, plan.Detail);
                }
                else
                {
                    // This chip used to be the ONLY place AutoMoneyPit's state was visible anywhere on the
                    // panel — it narrated a switch the user could not reach. Now AUTO THROW is right below it,
                    // so the sub-caption's job shrinks to stating the CONSEQUENCE, and it adopts the control's
                    // word: "AUTO MONEY PIT IS OFF" named a field, "AUTO THROW IS OFF" names the button the
                    // user is looking at. Same truth, one vocabulary.
                    SetChip(2, false, $"MANUAL — min {MoneyPitManager.TierName(Settings.MoneyPitThreshold)}", Settings.AutoMoneyPit ? "AUTO THROW AT THRESHOLD" : "AUTO THROW IS OFF");
                }

                // Shockwave advice.
                bool shockEmpty = Settings.Shockwave == null || Settings.Shockwave.Length == 0;
                if (shockEmpty) UiLayout.FitOrGrow(_shockNote, ShockAdvice);
                else _shockNote.Text = "";
            }
            catch (Exception ex) { LogDebug($"Pit chips: {ex.Message}"); }
        }
    }
}
