using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // The TOP ACTIONS column — split out of the old StatusBoardPanel for the M1 Control Room shell.
    // Master toggles (AUTO PROFILE / CHALLENGE OVERLAYS) ride the top row; severity-ranked action
    // cards below with per-system ADVISOR/MANUAL chips; last slot doubles as the collapsed "n optimal"
    // card (click to expand/collapse).
    public class ActionsPanel : Panel
    {
        private class ActSlot
        {
            public Panel Card;
            public Panel Stripe;
            public Label Name;
            public Label Text;
            public Label Chip;
            public string AutoKey;
        }

        // Room for EVERY system when the optimal list is expanded (user rule: SHOW grows the panel
        // downward; the section canvas scrolls if it outgrows the viewport).
        private const int MaxActs = 16;
        private readonly ActSlot[] _acts = new ActSlot[MaxActs];
        private Button _autoToggle;
        private Button _overlayToggle;
        private DateTime _lastRefresh = DateTime.MinValue;
        private bool _syncing;
        // Full-page mode (B1: TOP ACTIONS is its own rail sub-page): every system renders inline,
        // optimal rows included — no SHOW/HIDE collapse card.
        private readonly bool _fullPage;
        // First card sits under the "ADVISOR PRIORITIES" caption, so it is derived from that caption's
        // measured line box instead of a scaled constant (both are 60px at the tuning baseline).
        private readonly int _cardsTop = UiTheme.S(38) + UiTheme.HeadH;

        public ActionsPanel(int canvasW, bool fullPage = false)
        {
            _fullPage = fullPage;
            BackColor = UiTheme.Ground;
            Width = canvasW;

            _autoToggle = MkBtn("AUTO PROFILE");
            _autoToggle.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.AutoProfile = !Settings.AutoProfile;
                SyncFromSettings();
            };
            _overlayToggle = MkBtn("CHALLENGE OVERLAYS");
            _overlayToggle.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.AdvisorChallenges = !Settings.AdvisorChallenges;
                SyncFromSettings();
            };
            Controls.Add(_autoToggle);
            Controls.Add(_overlayToggle);
            UiLayout.Row(0, UiTheme.S(4), UiTheme.S(8), _autoToggle, _overlayToggle);

            var head = new Label
            {
                Text = "ADVISOR PRIORITIES", AutoSize = true, Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(0, UiTheme.S(38))
            };
            Controls.Add(head);

            for (int i = 0; i < MaxActs; i++)
            {
                var a = new ActSlot();
                int cardW = canvasW;
                int chipX = cardW - UiTheme.S(84);
                a.Card = new Panel
                {
                    Location = new Point(0, _cardsTop + i * UiTheme.S(30)),
                    Size = new Size(cardW, UiTheme.S(26)),
                    BackColor = UiTheme.Surface,
                    BorderStyle = BorderStyle.FixedSingle,
                    Visible = false
                };
                a.Stripe = new Panel { Location = new Point(0, 0), Size = new Size(UiTheme.S(4), UiTheme.S(24)), BackColor = UiTheme.Energy };
                // Name column sized to the longest real system name ("ADV TRAINING" clipped at 86).
                a.Name = new Label { Text = "", AutoSize = false, Size = new Size(UiTheme.S(100), UiTheme.SHead(20)), Font = UiTheme.Chip, ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(10), UiTheme.S(4)) };
                a.Text = new Label { Text = "", AutoSize = false, Size = new Size(chipX - UiTheme.S(122), UiTheme.SText(20)), Font = UiTheme.Ui, ForeColor = UiTheme.Ink, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(114), UiTheme.S(2)) };
                a.Chip = new Label
                {
                    Text = "", AutoSize = false, Size = new Size(UiTheme.S(74), UiTheme.SHead(18)), Font = UiTheme.Chip,
                    ForeColor = Color.White, BackColor = UiTheme.Cap,
                    TextAlign = ContentAlignment.MiddleCenter, Location = new Point(chipX, UiTheme.S(3)),
                    Cursor = Cursors.Hand
                };
                var cap = a;
                a.Chip.Click += (s, e) =>
                {
                    if (_syncing) return;
                    try
                    {
                        if (cap.AutoKey == null) ToggleOptimal();
                        else { SetAuto(cap.AutoKey, !GetAuto(cap.AutoKey)); RefreshActions(); }
                    }
                    catch (Exception ex) { LogDebug($"Board toggle: {ex.Message}"); }
                };
                a.Card.Click += (s, e) => { if (!_syncing && cap.AutoKey == null && cap.Card.Cursor == Cursors.Hand) ToggleOptimal(); };
                a.Card.Controls.Add(a.Stripe);
                a.Card.Controls.Add(a.Name);
                a.Card.Controls.Add(a.Text);
                a.Card.Controls.Add(a.Chip);
                Controls.Add(a.Card);
                _acts[i] = a;
            }
            Height = _cardsTop + MaxActs * UiTheme.S(30) + UiTheme.S(4);

            VisibleChanged += (s, e) => { if (Visible) RefreshActions(); };
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
            UiTheme.ApplyState(_autoToggle, Settings.AutoProfile ? UiTheme.Cap : UiTheme.Danger, Color.White);
            UiTheme.ApplyState(_overlayToggle, Settings.AdvisorChallenges ? UiTheme.Cap : UiTheme.Danger, Color.White);
            RefreshActions();
        }

        public void TickActions()
        {
            if (!Visible) return;
            if ((DateTime.UtcNow - _lastRefresh).TotalSeconds < 3) return;
            RefreshActions();
        }

        private static bool GetAuto(string key)
        {
            if (Settings == null) return false;
            switch (key)
            {
                case "gear": return Settings.AdvisorGearRefresh;
                case "wandoos": return Settings.AdvisorWandoosOS;
                case "diggers": return Settings.AdvisorDiggers;
                case "beards": return Settings.AdvisorBeards;
                case "perks": return Settings.AdvisorPerks;
                case "quirks": return Settings.AdvisorQuirks;
                case "yggbuys": return Settings.AdvisorYggBuys;
                case "exp": return Settings.AdvisorExpBuys;
                case "titangold": return Settings.AutoTitanGold;
                case "blood": return Settings.AdvisorBlood;
                default: return false;
            }
        }

        private static void SetAuto(string key, bool value)
        {
            if (Settings == null) return;
            switch (key)
            {
                case "gear": Settings.AdvisorGearRefresh = value; break;
                case "wandoos": Settings.AdvisorWandoosOS = value; break;
                case "diggers": Settings.AdvisorDiggers = value; break;
                case "beards": Settings.AdvisorBeards = value; break;
                case "perks": Settings.AdvisorPerks = value; break;
                case "quirks": Settings.AdvisorQuirks = value; break;
                case "yggbuys": Settings.AdvisorYggBuys = value; break;
                case "exp": Settings.AdvisorExpBuys = value; break;
                case "titangold": Settings.AutoTitanGold = value; break;
                case "blood": Settings.AdvisorBlood = value; break;
            }
        }

        private void ToggleOptimal()
        {
            if (Settings == null) return;
            Settings.AdvisorShowOptimal = !Settings.AdvisorShowOptimal;
            RefreshActions();
        }

        // Capitalization scheme (user rule): reports are sentence case.
        private static string Report(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.IsLower(s[0]) ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;
        }

        private void RefreshActions()
        {
            _lastRefresh = DateTime.UtcNow;
            _syncing = true;
            try
            {
                var recs = OptimizationAdvisor.Analyze();
                bool expand = _fullPage || (Settings != null && Settings.AdvisorShowOptimal);
                var visible = new List<OptimizationAdvisor.Rec>();
                var optimalNames = new List<string>();
                foreach (var rec in recs)
                {
                    if (string.IsNullOrEmpty(rec.System)) continue;
                    if (rec.Optimal && !expand) optimalNames.Add(rec.System);
                    else visible.Add(rec);
                }
                visible.Sort((x, y) => y.Severity.CompareTo(x.Severity));

                int slot = 0;
                for (int i = 0; i < visible.Count && slot < MaxActs - 1; i++, slot++)
                {
                    var rec = visible[i];
                    var a = _acts[slot];
                    a.Card.Visible = true;
                    a.Card.Cursor = Cursors.Default;
                    a.Stripe.BackColor = rec.Optimal ? UiTheme.Cap : rec.Severity >= 2 ? UiTheme.Danger : UiTheme.Energy;
                    UiLayout.FitInto(a.Name, rec.System.ToUpperInvariant());
                    UiLayout.FitOrGrow(a.Text, Report(rec.Text));   // card grows a second line, no "…"
                    a.AutoKey = rec.AutoKey;
                    bool hasKey = rec.AutoKey != null;
                    a.Chip.Visible = hasKey;
                    if (hasKey)
                    {
                        // These chips read/write the Advisor* fields — they ARE the DECISIONS layer, so
                        // they use its vocabulary. ("AUTO" was the old word and collides with the
                        // AUTOMATION permission, which is a different question entirely.)
                        bool on = GetAuto(rec.AutoKey);
                        a.Chip.Text = on ? SystemControlBar.Advisor : SystemControlBar.Manual;
                        a.Chip.BackColor = on ? UiTheme.Cap : UiTheme.Faint;
                    }
                }

                // Collapsed/expandable optimal card.
                if (!expand && optimalNames.Count > 0 && slot < MaxActs)
                {
                    var a = _acts[slot++];
                    a.Card.Visible = true;
                    a.Card.Cursor = Cursors.Hand;
                    a.Stripe.BackColor = UiTheme.Cap;
                    UiLayout.FitInto(a.Name, $"{optimalNames.Count} OPTIMAL");
                    UiLayout.FitOrGrow(a.Text, Report(string.Join(" · ", optimalNames.ToArray()).ToLowerInvariant()));
                    a.AutoKey = null;
                    a.Chip.Visible = true;
                    a.Chip.Text = "SHOW";
                    a.Chip.BackColor = UiTheme.Faint;
                }
                else if (expand && !_fullPage && slot < MaxActs)
                {
                    var a = _acts[slot++];
                    a.Card.Visible = true;
                    a.Card.Cursor = Cursors.Hand;
                    a.Stripe.BackColor = UiTheme.Cap;
                    UiLayout.FitOrGrow(a.Text, "Hide the optimal rows again");
                    UiLayout.FitInto(a.Name, "COLLAPSE");
                    a.AutoKey = null;
                    a.Chip.Visible = true;
                    a.Chip.Text = "HIDE";
                    a.Chip.BackColor = UiTheme.Faint;
                }

                for (; slot < MaxActs; slot++) _acts[slot].Card.Visible = false;

                // Reflow: two-line cards are taller, rows below shift down (the no-ellipsis rule).
                int flowY = _cardsTop;
                foreach (var a in _acts)
                {
                    if (!a.Card.Visible) continue;
                    a.Card.Top = flowY;
                    a.Card.Height = Math.Max(UiTheme.S(26), a.Text.Bottom + UiTheme.S(4));
                    a.Stripe.Height = a.Card.Height - 2;
                    flowY += a.Card.Height + UiTheme.S(4);
                }
                Height = flowY + UiTheme.S(4);   // exact: SHOW grows the panel down, HIDE shrinks it back
            }
            catch (Exception e) { LogDebug($"Board actions: {e.Message}"); }
            finally { _syncing = false; }
        }
    }
}
