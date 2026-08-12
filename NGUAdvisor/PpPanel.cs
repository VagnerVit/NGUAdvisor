using System;
using System.Drawing;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Economy > PERK POINTS — the view over SpendPlanner's perk plan: what the banked PP is being saved
    // for, and when it will be enough. Every number on it is owned by somebody else (SpendPlanner the
    // order and the cost, GrowthTracker the measured rate, ItopodFarmAdvisor the modelled one, PpEta the
    // estimate); this file only renders them.
    //
    // NOTHING HERE BUYS A PERK. Auto-buy already exists in AdvisorApply and is configured there — a
    // second spend path on a read-only advice surface would be two owners for one irreversible action.
    //
    // MEASURED AND MODELLED ARE NEVER BLENDED. They answer different questions: "at the pace I am
    // actually going" versus "if I went and farmed the pod". The headline ETA says which of the two it
    // used, every time, because a modelled ETA wearing a measured label is simply a wrong answer.
    //
    // "NO NEXT BUY" IS NOT "PLAN COMPLETE". SpendPlanner.NextPerk() goes unknown whenever the next
    // guide step is gated by chapter or difficulty, which on Normal is most of the run. Only when
    // NextPerkPlanned() is ALSO unknown is the plan really finished — collapsing the two was a
    // user-reported bug (see SpendPlanner.cs:236 and docs/modules/SpendPlanner.md).
    public class PpPanel : Panel
    {
        // The same window GrowthPanel's chips default to (GrowthPanel.cs:30-31, "1H"). Reused rather
        // than re-picked so the ETA here cannot disagree with the PP/hr chip on the Status page.
        private const double RateWindowMinutes = 60;

        // AdventurePanel.cs:359 builds the Optimize list { Disabled, Default, PP, EXP/AP } and stores
        // the raw SelectedIndex; there is no named constant to reference, so the index is named here.
        private const int OptimizeModePp = 2;

        private const string ToggleCaption = "Farm ITOPOD for PP";

        private const string Provenance = "Order: community guide perk plan (docs/NGU-KNOWLEDGE.md).";

        private readonly Label _banked;
        private readonly Panel _card;
        private readonly Label _cardValue;
        private readonly Label _cardCost;
        private readonly Label _cardNote;
        private readonly Label _queued;
        private readonly Label _modelled;
        private readonly Button _toggle;
        private readonly Label _toggleNote;

        public int ContentHeight { get; private set; }

        // canvasW: explicit canvas width when hosted in a section column (0 = UiLayout.PanelW).
        public PpPanel(int canvasW = 0)
        {
            int W = canvasW > 0 ? canvasW : UiLayout.PanelW;
            int inner = W - UiTheme.S(20);
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            var head = new Label
            {
                Text = "PERK POINTS", AutoSize = true, Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), UiTheme.S(10))
            };
            Controls.Add(head);

            _banked = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.SText(22)), Font = UiTheme.Bold,
                ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), UiTheme.S(10) + UiTheme.HeadPitch)
            };
            Controls.Add(_banked);

            // The card's height is DERIVED from the children it holds, never tuned: its note is a
            // reserved two-line box whose floor grows with the measured line box, and a fixed card
            // would clip it silently — a card has no scrollbar to reach the overflow with.
            _card = new Panel
            {
                Location = new Point(UiTheme.S(10), _banked.Bottom + UiTheme.S(8)),
                Size = new Size(inner, UiTheme.S(10)),
                BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle
            };
            int cardInner = inner - UiTheme.S(16);
            var cardTitle = new Label
            {
                Text = "NEXT PERK", AutoSize = true, Font = UiTheme.Chip,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface,
                Location = new Point(UiTheme.S(8), UiTheme.S(6))
            };
            _cardValue = new Label
            {
                AutoSize = false, Size = new Size(cardInner, UiTheme.SText(22)), Font = UiTheme.Bold,
                ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface,
                Location = new Point(UiTheme.S(8), UiTheme.S(6) + UiTheme.HeadPitch)
            };
            _cardCost = new Label
            {
                AutoSize = false, Size = new Size(cardInner, UiTheme.SText(20)), Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface,
                Location = new Point(UiTheme.S(8), _cardValue.Bottom + UiTheme.S(2))
            };
            _cardNote = new Label
            {
                AutoSize = false, Size = new Size(cardInner, UiTheme.SHead(36)), Font = UiTheme.Chip,
                ForeColor = UiTheme.Faint, BackColor = UiTheme.Surface,
                Location = new Point(UiTheme.S(8), _cardCost.Bottom + UiTheme.S(4))
            };
            _card.Controls.Add(cardTitle);
            _card.Controls.Add(_cardValue);
            _card.Controls.Add(_cardCost);
            _card.Controls.Add(_cardNote);
            _card.Height = _cardNote.Bottom + UiTheme.S(8);
            Controls.Add(_card);

            _queued = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.SText(20)), Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), _card.Bottom + UiTheme.S(8))
            };
            Controls.Add(_queued);

            _modelled = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.SText(20)), Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), _queued.Bottom + UiTheme.S(2))
            };
            Controls.Add(_modelled);

            // The ONE control on this surface, and a routing preference rather than a purchase: it can
            // be turned straight back off and it never spends a perk point.
            _toggle = new Button
            {
                Text = ToggleCaption,
                Size = new Size(UiLayout.BtnWidth(ToggleCaption), UiTheme.SCtl(24)),
                Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat,
                Location = new Point(UiTheme.S(10), _modelled.Bottom + UiTheme.S(10))
            };
            _toggle.FlatAppearance.BorderColor = UiTheme.Border;
            _toggle.Click += ToggleClicked;
            Controls.Add(_toggle);

            // Four lines reserved: the toggle can be gated, outranked, climb-overridden AND bypassing
            // the advisor all at once, and the whole point of the note is that none of the four ever
            // goes unsaid.
            _toggleNote = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.SHead(72)), Font = UiTheme.Chip,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), _toggle.Bottom + UiTheme.S(6))
            };
            Controls.Add(_toggleNote);

            var provenance = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.TextH), Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), _toggleNote.Bottom + UiTheme.S(8))
            };
            Controls.Add(provenance);
            // Set once, in the ctor: the text is constant, so the height it grows to is part of the
            // panel's derived height rather than something a later refresh could change underneath it.
            UiLayout.FitOrGrow(provenance, Provenance);

            // The panel does not scroll — it is hosted in a scrolling section — so its height must come
            // from its content. Anything hand-tuned here would clip the last lines at real DPI.
            ContentHeight = provenance.Bottom + UiTheme.S(10);

            VisibleChanged += (s, e) => { if (Visible) SyncFromSettings(); };
        }

        // WinForms handler: it writes Settings and NOTHING else. Touching allocation or routing code
        // from here would run Unity calls off the main thread and hard-crash the game; the next
        // Main.Update() pass reads the flag and does the routing.
        private void ToggleClicked(object sender, EventArgs e)
        {
            if (Settings == null) return;
            try
            {
                bool on = !Settings.AdventureTargetITOPOD;
                Settings.AdventureTargetITOPOD = on;
                // Only on the way ON, and ITOPODCombatMode is deliberately untouched: AdventurePanel
                // owns that choice, and turning this off is meant to restore routing, not to rewrite
                // the pod's optimisation target behind the user's back.
                if (on) Settings.ITOPODOptimizeMode = OptimizeModePp;
            }
            catch (Exception ex) { LogDebug($"PP panel toggle: {ex.Message}"); }
            SyncFromSettings();
        }

        // Named for the deferred pass in SettingsForm.UpdateFromSettings, which runs at most once a
        // second on the Unity main thread — the only place live game reads may happen. Every failure is
        // contained here: one throwing read must not abort the rest of that pass.
        public void SyncFromSettings()
        {
            try
            {
                // Hidden panel, no work: UpdateFromSettings calls this every refresh regardless of the
                // page on screen, and ItopodFarmAdvisor.ForMode prices four currencies per rotation
                // slice. Visible is false while any parent is hidden, so an unselected sub-page counts.
                if (!Visible) return;

                if (Main.Character == null) return;

                long banked = Main.Character.adventure.itopod.perkPoints;
                _banked.Text = $"BANKED — {NumberFormatter.Abbrev(banked)} PP";

                double modelledPerHour = 0;
                ItopodFarmAdvisor.Rates rates = ItopodFarmAdvisor.ForMode(Settings != null ? Settings.CombatMode : 0);
                if (rates.Known) modelledPerHour = rates.PpPerSecond * 3600.0;

                // The measured rate is the headline because it is what is actually happening. It is
                // read off GPp — cumulative GAINS, so buying a perk cannot depress it (GrowthTracker.cs:8).
                double measuredPerHour;
                bool hasMeasured = GrowthTracker.Rate(s => s.GPp, RateWindowMinutes, false, out measuredPerHour);

                double perHour;
                string rateLabel;
                if (hasMeasured && measuredPerHour > 0)
                {
                    perHour = measuredPerHour;
                    rateLabel = "measured";
                }
                else
                {
                    // Falling back to the modelled figure is allowed; doing it silently is not. The two
                    // no-measurement cases are told apart because they mean different things to the
                    // user: one is "wait a minute", the other is "you are not farming PP at all".
                    perHour = modelledPerHour;
                    rateLabel = hasMeasured
                        ? "modelled — you are not gaining PP right now"
                        : "modelled — no measured rate yet";
                }

                // The planner is asked ONCE per refresh and both the card and the queued line render
                // from that one answer, so they cannot disagree about whether the plan is finished.
                SpendPlanner.PlannedBuy planned = SpendPlanner.NextPerkPlanned();
                RenderCard(SpendPlanner.NextPerk(), planned, banked, perHour, rateLabel);
                RenderQueued(planned, banked);

                UiLayout.FitInto(_modelled, rates.Known
                    ? $"ITOPOD would pay {NumberFormatter.Abbrev(modelledPerHour)} PP/hr ({BoostFarmAdvisor.ModeName(rates.CombatMode)}, floors {rates.DefaultFloor}-{rates.PeakFloor})"
                    : "ITOPOD rate unavailable — the floor solve could not be read.");

                RenderToggle();
            }
            catch (Exception ex) { LogDebug($"PP panel refresh: {ex.Message}"); }
        }

        private void RenderCard(SpendPlanner.Buy next, SpendPlanner.PlannedBuy planned, long banked, double perHour, string rateLabel)
        {
            if (!next.Known)
            {
                // Unknown next buy is USUALLY the plan waiting on a chapter or a difficulty, not the
                // plan being finished. Only the absence of a queued step too means finished.
                _cardValue.ForeColor = UiTheme.Muted;
                UiLayout.FitInto(_cardValue, planned.Known ? "NOTHING BUYABLE RIGHT NOW" : "PERK PLAN COMPLETE");
                _cardCost.ForeColor = UiTheme.Muted;
                UiLayout.FitInto(_cardCost, planned.Known
                    ? "The next guide step is gated — see below for what the bank is for."
                    : "Every perk the guide schedules reads as bought.");
                UiLayout.WrapInto(_cardNote, "");
                return;
            }

            _cardValue.ForeColor = UiTheme.Accent;
            UiLayout.FitInto(_cardValue, next.Name ?? "");

            _cardCost.ForeColor = next.Affordable ? UiTheme.Cap : UiTheme.Energy;
            UiLayout.FitInto(_cardCost,
                $"Lv {next.CurLevel} -> {next.TargetLevel} · {NumberFormatter.Abbrev(next.Cost)} PP");

            if (next.Affordable)
            {
                _cardNote.ForeColor = UiTheme.Cap;
                UiLayout.WrapInto(_cardNote, "You can afford this now.");
                return;
            }

            _cardNote.ForeColor = UiTheme.Faint;
            double? hours = PpEta.HoursTo(next.Cost, banked, perHour);
            string shortfall = $"short {NumberFormatter.Abbrev(next.Cost - banked)}";
            // A null ETA prints NO duration at all. A 0h or an infinity in this slot reads as a real
            // prediction, and this line's only value is that its numbers can be trusted.
            UiLayout.WrapInto(_cardNote, hours.HasValue
                ? $"{shortfall} · ~{Duration(hours.Value)} at {NumberFormatter.Abbrev(perHour)} PP/hr ({rateLabel})"
                : $"{shortfall} · no rate yet");
        }

        private void RenderQueued(SpendPlanner.PlannedBuy planned, long banked)
        {
            if (!planned.Known) { _queued.Text = ""; UiLayout.Tip(_queued, null); return; }

            string gate = $"needs chapter {planned.MinChapter}"
                + (planned.DifficultyGated ? " and the next difficulty" : "");
            UiLayout.FitInto(_queued,
                $"Queued: {planned.Name} · {gate} · {NumberFormatter.Abbrev(planned.Cost)} PP (have {NumberFormatter.Abbrev(banked)})");
        }

        // The toggle reflects the LIVE setting rather than a cached copy, so flipping Target ITOPOD on
        // the Adventure page moves this button too — they are one property with one owner.
        private void RenderToggle()
        {
            if (Settings == null) return;

            bool on = Settings.AdventureTargetITOPOD;
            UiTheme.ApplyState(_toggle, on ? UiTheme.Cap : UiTheme.BtnFace, on ? Color.White : UiTheme.Ink);

            // Four preconditions, all read from the routing code itself (Main.cs:1386-1404). A control
            // that silently fails to do what its caption says is worse than no control.
            var notes = new System.Collections.Generic.List<string>();

            if (!Settings.CombatEnabled)
                notes.Add("Combat is OFF — routing never runs, so this changes nothing until you enable combat.");

            bool hunting = false;
            try { hunting = GearHunter.Active && GearHunter.ZoneReachable(); } catch { }
            if (hunting)
                notes.Add("A gear hunt is running and outranks ITOPOD targeting — this takes effect when the hunt ends.");

            // Main.cs:1404 — an EVIL CLIMB segment farms the furthest clearable zone and ignores this
            // toggle outright, because honoring it during a climb parked the run in the pod after one
            // kill and collapsed gross gold, i.e. the digger budget (user-caught). ChallengeOverlay
            // blanks the segment whenever AutoProfile is off (ChallengeOverlay.cs:142-143), so this
            // never fires on a manual profile — and the wording says so, rather than warning every
            // Evil player that their toggle is dead.
            bool evilClimb = false;
            try { evilClimb = ChallengeOverlay.Segment == "EVIL CLIMB"; } catch { }
            if (evilClimb)
                notes.Add("Auto profile is running an EVIL CLIMB segment, which farms the furthest clearable zone for its bosses and gold and ignores this toggle until the segment ends (manual profiles are unaffected).");

            notes.Add(on
                ? "ON — adventuring is parked in the ITOPOD and the advisor's gear/boost zone routing is bypassed."
                : "Off — turning it on parks adventuring in the ITOPOD and bypasses the advisor's gear/boost zone routing.");

            _toggleNote.ForeColor = !Settings.CombatEnabled || hunting || evilClimb ? UiTheme.Danger : UiTheme.Muted;
            UiLayout.WrapInto(_toggleNote, string.Join(" ", notes.ToArray()), 4);
        }

        // Moved to NumberFormatter when SpendOverview became the second caller — one renderer, so the
        // two surfaces cannot quote the same wait differently.
        private static string Duration(double hours) => NumberFormatter.Duration(hours);
    }
}
