using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Economy > GOLD, pipeline v2 (user-revised): THREE stage chips with the ALL-CAPS status grammar
    // (ACTIVE:/WAITING:), Time Machine shows its gold TOTAL (single-decimal suffix), the bank chip is
    // honest about challenge-limited banking, and the old SPENDING chip is replaced by a full-width
    // GOLD DRAIN ledger (gross -> net gps, per-consumer rows: diggers + blood rituals, augment status).
    public class GoldPanel : Panel
    {
        private class Stage
        {
            public Panel Box;
            public Label Title;
            public Label Value;
            public Label Sub;
        }

        // The two layers, verified: AUTOMATION = Settings.ManageGoldLoadouts (the gate on the snipe and
        // loadout swap, Main.cs:1165); DECISIONS = Settings.AdvisorGold (who arms the triggers —
        // AdvisorApply:252/265, Main.cs:1417). The trigger chips below are MODIFIERS of the manual
        // strategy, not a third layer: in advisor mode the advisor arms starvation regardless of its chip.
        private SystemControlBar _controlBar;
        private Button _snipeNow;
        private Button _resetBanks;
        private Button _refresh;
        private readonly Stage[] _stages = new Stage[3];

        private Panel _manualStrip;
        private Button _trigNewZone;
        private Button _trigRebirth;
        private Button _trigStarved;
        private Button _trigTimer;
        private NumericUpDown _timerMin;
        private Label _minLbl;
        private Button _cblock;
        private Label _advisorNote;

        private Label _grossNet;
        private Label _digVal;
        private Panel _digBarOuter;
        private Panel _digBarInner;
        private Label _bloodVal;
        private Panel _bloodBarOuter;
        private Panel _bloodBarInner;
        private Label _augVal;

        private bool _syncing;

        // canvasW: explicit canvas width when hosted in an M1 section column (0 = UiLayout.PanelW).
        public GoldPanel(int canvasW = 0)
        {
            int W = canvasW > 0 ? canvasW : UiLayout.PanelW;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            _snipeNow = MkBtn("Snipe Now");
            UiTheme.StyleFlat(_snipeNow);
            _snipeNow.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.GoldSnipeComplete = false;
                LastSnipeTrigger = "manual";
                Log("Re-snipe: manual");

                // The arming ALWAYS takes effect: GoldSnipeComplete is persisted, and the only thing that
                // ever clears it is LockManager:219 — which itself sits inside `if (ManageGoldLoadouts)`.
                // So with automation off the trigger stays armed indefinitely and fires the moment
                // automation is switched on. That makes this a WARNING, never a FAILURE: the action DID
                // happen, it just cannot execute yet. Promising a "next pass" swap while the gate is shut
                // was the lie; saying nothing at all would be the next one.
                if (Settings.ManageGoldLoadouts)
                    Activity.Queued("Re-snipe armed — the gold loadout swaps on the next pass.");
                else
                    Activity.Warning("Re-snipe armed, but automation is off — nothing swaps until you turn it on.");

                RefreshPipeline();
            };
            _resetBanks = MkBtn("Reset Banks");
            UiTheme.StyleFlat(_resetBanks);
            _resetBanks.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.TitanMoneyDone = new bool[ZoneHelpers.TitanZones.Length];
                Log("Titan gold banks reset — all AK'd titans will re-bank");
                Activity.Completed("Titan gold banks reset — all AK'd titans will re-bank.");
                RefreshPipeline();
            };
            _refresh = new Button { Text = "↻", Size = new Size(Math.Max(UiTheme.S(36), UiLayout.BtnWidth("↻")), UiTheme.SCtl(24)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(_refresh);
            _refresh.Click += (s, e) => RefreshPipeline();
            // The bar gets its own row here: this panel is only 520px wide and carries a genuine action
            // GROUP (Snipe Now · Reset Banks · ↻), which is exactly the Yggdrasil case the convention
            // carves out. Squeezing all four beside the bar would starve it below its ~310px minimum.
            _controlBar = new SystemControlBar(
                W - UiTheme.S(54),
                () => Settings.ManageGoldLoadouts, v => Settings.ManageGoldLoadouts = v,
                () => Settings.AdvisorGold, v => Settings.AdvisorGold = v,
                "The advisor arms the snipe triggers and funds the TM.",
                "Your trigger chips below decide when to re-snipe.",
                "Automation is off — no gold loadout swap and no snipe.")
            {
                Location = new Point(UiTheme.S(10), UiTheme.S(10))
            };
            _controlBar.Changed += SyncFromSettings;
            Controls.Add(_controlBar);

            Controls.Add(_snipeNow);
            Controls.Add(_resetBanks);
            Controls.Add(_refresh);
            int actionsY = UiTheme.S(10) + SystemControlBar.BarHeight + UiTheme.S(8);
            UiLayout.Row(UiTheme.S(10), actionsY, UiTheme.S(8), _snipeNow, _resetBanks, _refresh);

            // Everything below the action row.
            int content = actionsY + UiTheme.SCtl(24) + UiTheme.S(8);

            // Three stage chips joined by two arrows, stretched across the PanelW canvas.
            //
            // ONE arrow width, used by all three places that care. The glyph needs more than the tuned
            // 16px slot once the renderer scales (audit: needs 31px, has 24px) — but widening the label
            // alone put the arrows out of place, because the space BETWEEN the chips and the advance of
            // the x cursor were still the old 16px. A measured width has to be reserved everywhere it is
            // spent: in the chip width, and in the step past each arrow.
            string[] titles = { "ZONE SNIPE", "TIME MACHINE", "TITAN BANK" };
            int boxH = UiTheme.S(88);
            int arrowH = UiTheme.SText(22);
            int arrowW = Math.Max(UiTheme.S(16), UiLayout.MeasureText("→", UiTheme.Bold) + UiTheme.S(4));
            int stageW = (W - UiTheme.S(20) - 2 * arrowW) / 3;
            int x = UiTheme.S(10);
            for (int i = 0; i < 3; i++)
            {
                var st = new Stage();
                // Two-line sub-caption budget (round-3: "WAITING: 3 TRIGGE…" never again).
                st.Box = new Panel { Location = new Point(x, content), Size = new Size(stageW, boxH), BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle };
                st.Title = new Label { Text = titles[i], AutoSize = true, Font = UiTheme.Chip, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(6), UiTheme.S(4)) };
                st.Value = new Label { Text = "…", AutoSize = false, Size = new Size(stageW - UiTheme.S(12), UiTheme.SText(22)), Font = UiTheme.Bold, ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(6), UiTheme.S(22)) };
                st.Sub = new Label { Text = "", AutoSize = false, Size = new Size(stageW - UiTheme.S(12), UiTheme.SHead(36)), Font = UiTheme.Chip, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(6), UiTheme.S(48)) };
                st.Box.Controls.Add(st.Title);
                st.Box.Controls.Add(st.Value);
                st.Box.Controls.Add(st.Sub);
                Controls.Add(st.Box);
                _stages[i] = st;
                x += stageW;
                if (i < 2)
                {
                    // Centred ON THE CHIP it joins, rather than at a tuned offset that only happened to
                    // look centred at the old line height.
                    var arrow = new Label
                    {
                        Text = "→", AutoSize = false, Size = new Size(arrowW, arrowH),
                        Font = UiTheme.Bold, ForeColor = UiTheme.Faint, BackColor = UiTheme.Ground,
                        Location = new Point(x, content + (boxH - arrowH) / 2),
                        TextAlign = ContentAlignment.MiddleCenter
                    };
                    Controls.Add(arrow);
                    x += arrowW;
                }
            }

            // Trigger strip (manual) / note (advisor) — below the taller chips.
            _manualStrip = new Panel { Location = new Point(0, content + UiTheme.S(100)), Size = new Size(W - UiTheme.S(4), UiTheme.S(34)), BackColor = UiTheme.Ground, Tag = "exclusive" };
            Controls.Add(_manualStrip);
            var trigLbl = new Label { Text = "re-snipe on:", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            _trigNewZone = MkTrig("New Zone", () => Settings.SnipeOnNewZone = !Settings.SnipeOnNewZone);
            _trigRebirth = MkTrig("Rebirth", () => Settings.SnipeOnRebirth = !Settings.SnipeOnRebirth);
            _trigStarved = MkTrig("Gold Starved", () => Settings.SnipeOnGoldStarved = !Settings.SnipeOnGoldStarved);
            _trigTimer = MkTrig("Timer", () => Settings.SnipeOnTimer = !Settings.SnipeOnTimer);
            _timerMin = new NumericUpDown { Width = UiTheme.S(56), Minimum = 1, Maximum = 240, Font = UiTheme.Ui };
            UiTheme.StyleNum(_timerMin);
            _timerMin.ValueChanged += (s, e) => { if (!_syncing && Settings != null) Settings.ResnipeTime = (int)_timerMin.Value * 60; };
            _minLbl = new Label { Text = "min into run", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            _cblock = MkTrig("CBlock Snipe", () => Settings.GoldCBlockMode = !Settings.GoldCBlockMode);
            _manualStrip.Controls.Add(trigLbl);
            _manualStrip.Controls.Add(_trigNewZone);
            _manualStrip.Controls.Add(_trigRebirth);
            _manualStrip.Controls.Add(_trigStarved);
            _manualStrip.Controls.Add(_trigTimer);
            _manualStrip.Controls.Add(_timerMin);
            _manualStrip.Controls.Add(_minLbl);
            _manualStrip.Controls.Add(_cblock);
            // Wraps in narrow M1 columns; the ledger reflows below whichever strip is taller.
            int stripBottom = UiLayout.WrapRow(UiTheme.S(10), UiTheme.S(4), UiTheme.S(6), _manualStrip.Width - UiTheme.S(10), UiTheme.S(30), new Control[] { trigLbl, _trigNewZone, _trigRebirth, _trigStarved, _trigTimer, _timerMin, _minLbl, _cblock });
            _manualStrip.Height = stripBottom + UiTheme.S(2);

            _advisorNote = new Label
            {
                AutoSize = false,
                Size = new Size(W - UiTheme.S(20), UiTheme.TextH),
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), content + UiTheme.S(106)),
                Tag = "exclusive"
            };
            // Unchanged disclosure of the trigger truth table: in advisor mode the advisor arms these
            // itself (starvation regardless of its chip), which is why the manual strip gives way to this
            // note rather than pretending every chip is still authoritative.
            UiLayout.FitOrGrow(_advisorNote,
                // Length is load-bearing: the two-line budget ellipsized at ~126 chars (PrintWindow check
                // — the audit calls it clean, Mono draws wider than the measurement). Keep it at the
                // length of the sentence this replaced.
                "Re-snipes on: new zone · rebirth · gold starvation · gold drop improved — challenge mode auto-detected.");
            Controls.Add(_advisorNote);

            BuildDrainLedger(W, Math.Max(content + UiTheme.S(100) + _manualStrip.Height, _advisorNote.Bottom) + UiTheme.S(10));

            VisibleChanged += (s, e) => { if (Visible) RefreshPipeline(); };
            SyncFromSettings();
        }

        private void BuildDrainLedger(int W, int y)
        {
            int boxW = W - UiTheme.S(54);   // 610 legacy
            // GROSS/NET gets a permanent two-line budget — octillion-scale numbers wrap, not clip.
            var box = new Panel { Location = new Point(UiTheme.S(10), y), Size = new Size(boxW, UiTheme.S(150)), BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(box);

            box.Controls.Add(new Label { Text = "GOLD DRAIN", AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(8), UiTheme.S(4)) });

            _grossNet = new Label { Text = "…", AutoSize = false, Size = new Size(boxW - UiTheme.S(18), UiTheme.SText(42)), Font = UiTheme.Bold, ForeColor = UiTheme.Ink, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(8), UiTheme.S(26)) };
            box.Controls.Add(_grossNet);

            // Measured label column (the fixed 90px column blanked "Blood rituals" under the game's
            // wider font rendering) + TextH heights and 26px row pitch.
            int labelW = Math.Max(UiTheme.S(100), Math.Max(
                UiLayout.MeasureText("Blood rituals", UiTheme.Ui),
                Math.Max(UiLayout.MeasureText("Diggers", UiTheme.Ui), UiLayout.MeasureText("Augments", UiTheme.Ui))) + UiTheme.S(14));
            int barX = UiTheme.S(8) + labelW + UiTheme.S(6);
            // MEASURE THE VALUE COLUMN, then give the bar what is left. The tuned split (a 200px bar and
            // whatever remained) left 186px for strings the renderer needs 205px for, so the live audit
            // reported "1,000E+012/s · 5%" clipped. The widest string this column ever shows is a full
            // mantissa-exponent rate at 100%, so size from that and let the bar absorb the difference —
            // a shorter bar reads fine, a cut number does not.
            int valW = UiLayout.MeasureText("0,000E+000/s · 100%", UiTheme.Ui) + UiTheme.S(8);
            int valX = boxW - UiTheme.S(16) - valW;
            int barW = Math.Max(UiTheme.S(80), valX - barX - UiTheme.S(8));

            var digLbl = new Label { Text = "Diggers", AutoSize = false, Size = new Size(labelW, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(8), UiTheme.S(72)) };
            box.Controls.Add(digLbl);
            _digBarOuter = new Panel { Location = new Point(barX, UiTheme.S(78)), Size = new Size(barW, UiTheme.S(9)), BackColor = UiTheme.Zebra, BorderStyle = BorderStyle.FixedSingle };
            _digBarInner = new Panel { Location = new Point(0, 0), Size = new Size(0, UiTheme.S(7)), BackColor = UiTheme.Energy };
            _digBarOuter.Controls.Add(_digBarInner);
            box.Controls.Add(_digBarOuter);
            _digVal = new Label { Text = "", AutoSize = false, Size = new Size(valW, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(valX, UiTheme.S(72)) };
            box.Controls.Add(_digVal);

            var bloodLbl = new Label { Text = "Blood rituals", AutoSize = false, Size = new Size(labelW, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(8), UiTheme.S(98)) };
            box.Controls.Add(bloodLbl);
            _bloodBarOuter = new Panel { Location = new Point(barX, UiTheme.S(104)), Size = new Size(barW, UiTheme.S(9)), BackColor = UiTheme.Zebra, BorderStyle = BorderStyle.FixedSingle };
            _bloodBarInner = new Panel { Location = new Point(0, 0), Size = new Size(0, UiTheme.S(7)), BackColor = UiTheme.Energy };
            _bloodBarOuter.Controls.Add(_bloodBarInner);
            box.Controls.Add(_bloodBarOuter);
            _bloodVal = new Label { Text = "", AutoSize = false, Size = new Size(valW, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(valX, UiTheme.S(98)) };
            box.Controls.Add(_bloodVal);

            var augLbl = new Label { Text = "Augments", AutoSize = false, Size = new Size(labelW, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(8), UiTheme.S(124)) };
            box.Controls.Add(augLbl);
            _augVal = new Label { Text = "", AutoSize = false, Size = new Size(boxW - UiTheme.S(16) - barX, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(barX, UiTheme.S(124)) };
            box.Controls.Add(_augVal);
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
                try { toggle(); } catch (Exception ex) { LogDebug($"Gold trigger: {ex.Message}"); }
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
                // Reflects both layers, incl. a flip made from the Settings grid (which owns the only other
                // reachable writer of ManageGoldLoadouts) or a settings reload. Sync() never raises
                // Changed, so this cannot recurse.
                _controlBar?.Sync();

                bool advisor = Settings.AdvisorGold;
                _manualStrip.Visible = !advisor;
                _advisorNote.Visible = advisor;

                UiTheme.ApplyState(_trigNewZone, Settings.SnipeOnNewZone ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_trigRebirth, Settings.SnipeOnRebirth ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_trigStarved, Settings.SnipeOnGoldStarved ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_trigTimer, Settings.SnipeOnTimer ? UiTheme.Cap : UiTheme.Danger, Color.White);
                UiTheme.ApplyState(_cblock, Settings.GoldCBlockMode ? UiTheme.Cap : UiTheme.Danger, Color.White);
                int min = Math.Max(1, Math.Min(240, Settings.ResnipeTime / 60));
                _timerMin.Value = min;
                _timerMin.Enabled = _minLbl.Enabled = Settings.SnipeOnTimer;
            }
            finally { _syncing = false; }
            RefreshPipeline();
        }

        private void SetStage(int i, bool lit, string value, string sub)
        {
            var st = _stages[i];
            var bg = lit ? Color.FromArgb(253, 246, 233) : UiTheme.Surface;
            st.Box.BackColor = bg;
            st.Title.BackColor = st.Value.BackColor = st.Sub.BackColor = bg;
            st.Title.ForeColor = lit ? UiTheme.Energy : UiTheme.Muted;
            UiLayout.FitInto(st.Value, value);
            UiLayout.WrapInto(st.Sub, sub);
        }

        private static string TriggerCaps(string t)
        {
            switch (t)
            {
                case "new zone fightable": return "NEW ZONE";
                case "rebirth (TM empty)": return "REBIRTH";
                case "gold starvation": return "GOLD STARVED";
                case "gold drop improved": return "DROP IMPROVED";
                case "timer": return "TIMER";
                case "manual": return "MANUAL";
                default: return "SNIPING";
            }
        }

        // Armed-trigger summary sized to the chip: full list when it fits, count when it wouldn't.
        private string ArmedTriggers()
        {
            var parts = new List<string>();
            if (Settings.AdvisorGold) { parts.Add("ZONE"); parts.Add("REBIRTH"); parts.Add("STARVED"); }
            else
            {
                if (Settings.SnipeOnNewZone) parts.Add("ZONE");
                if (Settings.SnipeOnRebirth) parts.Add("REBIRTH");
                if (Settings.SnipeOnGoldStarved) parts.Add("STARVED");
                if (Settings.SnipeOnTimer) parts.Add("TIMER");
            }
            if (parts.Count == 0) return "MANUAL ONLY";
            string joined = string.Join(" · ", parts.ToArray());
            return UiLayout.MeasureText($"WAITING: {joined}", UiTheme.Chip) <= UiTheme.S(174)
                ? joined
                : $"{parts.Count} TRIGGERS ARMED";
        }

        private void RefreshPipeline()
        {
            try
            {
                var c = Main.Character;
                if (c == null || Settings == null) return;

                bool sniping = !Settings.GoldSnipeComplete;

                // ZONE SNIPE.
                if (sniping)
                {
                    string zone = "pending";
                    int fz = Main.FurthestZone;
                    if (fz >= 0 && ZoneHelpers.ZoneList.TryGetValue(fz, out var zn)) zone = zn;
                    SetStage(0, true, zone, $"ACTIVE: {TriggerCaps(LastSnipeTrigger)}");
                }
                else if (GoldDropAdvisor.SnipeSkipped)
                {
                    // Latched without a kill: the TM only ever converts the run's highest drop, so a zone
                    // that can't beat the banked one is not "complete", it is not worth the gear swap.
                    // No armed-trigger list here: the sub-caption is a two-line box that already needs both
                    // lines for "WAITING: 3 TRIGGERS ARMED" alone, and the two numbers are the point.
                    SetStage(0, false, "NO GAIN",
                        $"~{Fmt1(GoldDropAdvisor.SnipeSkipPredicted)} vs {Fmt1(GoldDropAdvisor.SnipeSkipBanked)} BANKED");
                }
                else
                {
                    SetStage(0, false, "COMPLETE", $"WAITING: {ArmedTriggers()}");
                }

                // TIME MACHINE: gold TOTAL, single-decimal suffix.
                double tmGold = 0;
                try { tmGold = c.machine.realBaseGold; } catch { }
                string cf = "NO COUNTERFEIT";
                try
                {
                    double gb = c.bloodMagic.goldSpellBlood, gm = c.bloodSpells.minGoldBlood();
                    if (gb >= gm && gm > 0)
                        cf = $"COUNTERFEIT +{Math.Floor(Math.Pow(Math.Log(gb / gm, 2.0) + 1.0, 2.0)):0}%";
                }
                catch { }

                // TITAN BANK.
                int best = AdvisorApply.HighestAkTitan();
                bool bankQueued = false;
                string bankValue = "NONE YET";
                string bankSub = "NO AK TITAN";
                string challenge = null;
                try { challenge = ChallengeDetector.Current(); } catch { }
                if (best >= 0)
                {
                    var done = Settings.TitanMoneyDone;
                    bankQueued = done == null || best >= done.Length || !done[best];
                    // Version tag comes from the game's own enemy entry (user-reported: "Walderp v1"
                    // mislabel — WALDERP has no versions; Beast V1/V2 are separate enemy #s).
                    bankValue = TitansPanel.AbbrevWithVersion(best);
                    bankSub = challenge != null ? "CHALLENGE-LIMITED" : (bankQueued ? "AT NEXT AK KILL" : "UP TO DATE");
                }

                SetStage(1, !sniping && !bankQueued, Fmt1(tmGold), tmGold > 0 ? cf : "WAITING ON SNIPE");
                SetStage(2, !sniping && bankQueued, bankValue, bankSub);

                // GOLD DRAIN ledger.
                double gross = 0, drainDig = 0, drainBlood = 0;
                try { gross = c.grossGoldPerSecond(); } catch { }
                try { drainDig = c.totalGPSDrain(); } catch { }
                try
                {
                    var rituals = c.bloodMagicController.bloodMagics;
                    if (rituals != null)
                        foreach (var r in rituals)
                            if (r != null)
                                drainBlood += r.goldConsumedPerSecond();
                }
                catch { }
                double net = gross - drainDig - drainBlood;
                double consumedPct = gross > 0 ? (drainDig + drainBlood) / gross * 100.0 : 0;
                UiLayout.WrapInto(_grossNet, $"GROSS {Fmt1(gross)}/s   →   NET {Fmt1(Math.Max(0, net))}/s   ({consumedPct:0}% consumed)");

                double digPct = gross > 0 ? drainDig / gross : 0;
                _digBarInner.Width = (int)(UiTheme.S(198) * Math.Min(1, digPct));
                UiLayout.FitInto(_digVal, $"{Fmt1(drainDig)}/s · {digPct * 100:0}%");

                double bloodPct = gross > 0 ? drainBlood / gross : 0;
                _bloodBarInner.Width = (int)(UiTheme.S(198) * Math.Min(1, bloodPct));
                UiLayout.FitInto(_bloodVal, $"{Fmt1(drainBlood)}/s · {bloodPct * 100:0}%");

                bool starved = false;
                try { starved = OptimizationAdvisor.GoldStarvedForAugs(c, 1.0); } catch { }
                UiLayout.FitInto(_augVal, starved ? "STARVED — snipe trigger armed" : "FUNDED");
                _augVal.ForeColor = starved ? UiTheme.Danger : UiTheme.Cap;
            }
            catch (Exception ex) { LogDebug($"Gold pipeline: {ex.Message}"); }
        }

        // The GAME's own formatter first (matches what the player sees in-game and respects their
        // number-display setting) — the hand ladder capped at Q and printed "1194605.7Q" for 1.19e21.
        private static string Fmt1(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
            try
            {
                var s = Main.Character?.display(v);
                if (!string.IsNullOrEmpty(s)) return s;
            }
            catch { }
            // Game formatter unavailable -> fall back to the shared canonical ladder (finding #31).
            return NumberFormatter.Abbrev(v);
        }
    }
}
