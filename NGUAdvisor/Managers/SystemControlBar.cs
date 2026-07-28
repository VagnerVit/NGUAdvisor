using System;
using System.Drawing;
using System.Windows.Forms;

namespace NGUAdvisor.Managers
{
    // THE CANONICAL TWO-LAYER CONTROL (Concept B′, slice 1). Every advisor-driven system answers two
    // independent questions, and the code has always kept them in separate fields — the UI just never
    // said so, which is how "I set it to ADVISOR and nothing happened" became the app's worst bug:
    //
    //   AUTOMATION  ON / OFF          may the tool operate this system at all?
    //                                 (the Manage*/Auto* permission, read by the MANAGERS in Main.Update)
    //   DECISIONS   ADVISOR / MANUAL  who decides what it does?
    //                                 (the Advisor* strategy, read by AdvisorApply.Tick)
    //
    // They are ANDed in code (e.g. AdvisorApply.ApplyGearRefresh opens with `if (!ManageGear) return;`),
    // so AUTOMATION OFF + DECISIONS ADVISOR is a legal state that silently does NOTHING. That state stays
    // legal — a user mid-tuning may want it — but it is never silent again: the stripe goes red and the
    // line says so. We never auto-correct either layer; the user's switch is the user's switch.
    //
    // This is presentation only. It knows nothing about titans, gold or gear: it reads and writes through
    // the delegates it is given, exactly as BasicSettingsPanel.Mk() already does. It must never grow
    // system logic, a refresh timer, or knowledge of which settings exist.
    public class SystemControlBar : Panel
    {
        // The vocabulary contract. These strings are the ONLY words for these two concepts — no
        // "MANAGED", no "ADVISOR MODE", no "Enabled". Structure/controls are ALL CAPS (house rule);
        // the explanation line reports, so it is sentence case.
        public const string AutomationCap = "AUTOMATION";
        public const string DecisionsCap = "DECISIONS";
        public const string On = "ON";
        public const string Off = "OFF";
        public const string Advisor = "ADVISOR";
        public const string Manual = "MANUAL";
        public const string ManualOnly = "MANUAL ONLY";

        // The dependency message. One wording, everywhere.
        public const string AdvisorIdle = "Advisor idle — automation is off";

        // 6 + 24 (button row) then the explanation at 38 + 22 = 60, + 4 bottom margin. The gap above the
        // explanation is 3px of real clearance: an AutoSize 7.5pt caption renders ~25px tall under this
        // Mono (NOT the 15px Font.Height implies), so the row can reach y=35. Both host panels derive
        // their content offsets from this constant, so changing it reflows them automatically.
        public static readonly int BarHeight = UiTheme.S(64);

        private readonly Func<bool> _getAutomation;
        private readonly Action<bool> _setAutomation;
        private readonly Func<bool> _getAdvisor;      // null = this system has no decisions layer
        private readonly Action<bool> _setAdvisor;

        private readonly string _whenAdvisor;         // automation on, advisor decides
        private readonly string _whenManual;          // automation on, user's rules decide
        private readonly string _whenOff;             // automation off (and decisions = manual)
        private readonly string _whenNoDecisions;     // systems with no advisor layer at all
        // State D override. The canonical line (AdvisorIdle) is scope-BLIND: it says "automation", which
        // is honest wherever the permission governs exactly this system. Boosts is the exception — its
        // permission (ManageInventory) governs merges, filters and convertibles too, so the generic line
        // would understate what is off. A system whose permission is BROADER than its panel may pass a
        // truthful replacement; everyone else gets the canonical wording, and the contract holds.
        private readonly string _whenIdle;

        private readonly Panel _stripe;
        private readonly Button _autoBtn;
        private readonly Button _decBtn;              // null when there is no decisions layer
        private readonly Label _decChip;              // shown instead of _decBtn in that case
        private readonly Label _why;

        // Raised after either layer is flipped, so the host panel re-syncs itself. The bar does not
        // know what else depends on the state.
        public event Action Changed;

        public SystemControlBar(
            int width,
            Func<bool> getAutomation, Action<bool> setAutomation,
            Func<bool> getAdvisor, Action<bool> setAdvisor,
            string whenAdvisor, string whenManual, string whenOff, string whenNoDecisions = null,
            string whenIdle = null)
        {
            _whenIdle = string.IsNullOrEmpty(whenIdle) ? AdvisorIdle : whenIdle;
            _getAutomation = getAutomation;
            _setAutomation = setAutomation;
            _getAdvisor = getAdvisor;
            _setAdvisor = setAdvisor;
            _whenAdvisor = whenAdvisor;
            _whenManual = whenManual;
            _whenOff = whenOff;
            _whenNoDecisions = whenNoDecisions;

            Size = new Size(width, BarHeight);
            BackColor = UiTheme.Surface;
            BorderStyle = BorderStyle.FixedSingle;

            // Severity stripe (the ActionsPanel card pattern) — there is no warning-background token in
            // UiTheme and inventing one is out of scope, so state colour rides the stripe + the line.
            // See Sync() for the ladder: Cap = running, Faint = off on purpose, Energy = idle warning.
            _stripe = new Panel { Location = new Point(0, 0), Size = new Size(UiTheme.S(3), BarHeight), BackColor = UiTheme.Cap };
            Controls.Add(_stripe);

            var autoCap = MkCap(AutomationCap);
            _autoBtn = MkStateBtn(Math.Max(UiLayout.BtnWidth(On), UiLayout.BtnWidth(Off)));
            _autoBtn.Click += (s, e) =>
            {
                if (_getAutomation == null || _setAutomation == null) return;
                try { _setAutomation(!_getAutomation()); }
                catch (Exception ex) { Main.LogDebug($"SystemControlBar automation: {ex.Message}"); }
                Sync();
                Changed?.Invoke();
            };

            var decCap = MkCap(DecisionsCap);
            Controls.Add(autoCap);
            Controls.Add(_autoBtn);
            Controls.Add(decCap);

            if (_getAdvisor != null && _setAdvisor != null)
            {
                // Width is fixed from the WIDEST state so the row never reflows when the state flips
                // (a moving explanation line would be its own layout bug).
                _decBtn = MkStateBtn(Math.Max(UiLayout.BtnWidth(Advisor), UiLayout.BtnWidth(Manual)));
                _decBtn.Click += (s, e) =>
                {
                    try { _setAdvisor(!_getAdvisor()); }
                    catch (Exception ex) { Main.LogDebug($"SystemControlBar decisions: {ex.Message}"); }
                    Sync();
                    Changed?.Invoke();
                };
                Controls.Add(_decBtn);
                UiLayout.Row(UiTheme.S(10), UiTheme.S(6), UiTheme.S(8), autoCap, _autoBtn, decCap, _decBtn);
            }
            else
            {
                // No advisor layer exists for this system. A dead button would be a lie, so this is a
                // chip: it states the fact and cannot be pressed.
                _decChip = new Label
                {
                    Text = ManualOnly,
                    AutoSize = false,
                    Size = new Size(UiLayout.MeasureText(ManualOnly, UiTheme.Chip) + UiTheme.S(14), UiTheme.SHead(24)),
                    Font = UiTheme.Chip,
                    ForeColor = Color.White,
                    BackColor = UiTheme.Faint,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                Controls.Add(_decChip);
                UiLayout.Row(UiTheme.S(10), UiTheme.S(6), UiTheme.S(8), autoCap, _autoBtn, decCap, _decChip);
            }

            // Second line: the state in words. Fixed size => must go through UiLayout.FitInto, or an
            // overflowing Mono label paints NOTHING (the blank-label law) — and FitInto also hangs the
            // full sentence on the label as a tooltip whenever it had to shorten it.
            _why = new Label
            {
                Text = "",
                AutoSize = false,
                Location = new Point(UiTheme.S(10), UiTheme.S(38)),
                Size = new Size(width - UiTheme.S(20), UiTheme.TextH),
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Surface
            };
            Controls.Add(_why);

            Sync();
        }

        // AUTOSIZE, not a measured fixed width. A fixed label sized to MeasureText + 2 CLIPPED under the
        // game's Mono ("AUTOMATION" painted as "AUTOMATIO"): the renderer draws wider than
        // TextRenderer.MeasureText reports, so a 2px cushion is not one. The UI auditor cannot catch this
        // class either — its TEXT CLIPPED check compares the control against the SAME measurement, so
        // measured+2 always passes. These captions are static, so AutoSize is free and immune: it grows
        // to whatever the renderer actually needs. (UiLayout.Row measures AutoSize labels for placement,
        // so the row still lays out correctly.)
        private static Label MkCap(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Font = UiTheme.ColHeader,
            ForeColor = UiTheme.Muted,
            BackColor = UiTheme.Surface
        };

        private static Button MkStateBtn(int w)
        {
            var b = new Button { Size = new Size(w, UiTheme.SCtl(24)), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat };
            b.FlatAppearance.BorderColor = UiTheme.Border;
            return b;
        }

        // Reflects external changes too (settings reload, the legacy twin toggle, a future Settings
        // index row): call it whenever the host re-syncs. Mutates properties only — never creates or
        // destroys controls, because a per-refresh rebuild is what killed the GUI in BloodPanel.
        public void Sync()
        {
            try
            {
                bool auto = _getAutomation != null && _getAutomation();
                bool advisor = _getAdvisor != null && _getAdvisor();
                bool hasDecisions = _decBtn != null;

                UiTheme.ApplyState(_autoBtn, auto ? UiTheme.Cap : UiTheme.Danger, Color.White);
                _autoBtn.Text = auto ? On : Off;

                if (hasDecisions)
                {
                    UiTheme.ApplyState(_decBtn, advisor ? UiTheme.Accent : UiTheme.Energy, Color.White);
                    _decBtn.Text = advisor ? Advisor : Manual;
                }

                // The four states, on a SEVERITY ladder — not an on/off one. The stripe answers "does
                // this need me?", which is a different question from the buttons' "is it on?":
                //
                //   Cap    (green)  running as configured                    — A, B
                //   Faint  (grey)   deliberately inactive, nothing is wrong  — C
                //   Energy (amber)  configured but idle: a DEPENDENCY warning — D
                //
                // D is NOT Danger. Danger is this app's error/off tone (the paused master, UNMANAGED,
                // a queued titan) and red here would read as data loss, a failed action or a broken
                // config — none of which is true: the user asked for advisor decisions on a system the
                // tool may not touch, which is a legal, recoverable, self-inflicted state. Energy is
                // already the app's warning tier (LightsPanel/rail rank it Danger > Energy > Cap), so
                // it is the strongest honest treatment available without inventing a token. The text
                // goes Ink (not Muted) so it reads as something to act on rather than a caption.
                string text;
                Color ink, stripe;
                if (!hasDecisions)
                {
                    text = auto ? _whenNoDecisions : _whenOff;
                    ink = UiTheme.Muted;
                    stripe = auto ? UiTheme.Cap : UiTheme.Faint;
                }
                else if (auto && advisor)          // State A
                {
                    text = _whenAdvisor; ink = UiTheme.Muted; stripe = UiTheme.Cap;
                }
                else if (auto)                     // State B — valid, NOT degraded
                {
                    text = _whenManual; ink = UiTheme.Muted; stripe = UiTheme.Cap;
                }
                else if (!advisor)                 // State C — off on purpose, not an error
                {
                    text = _whenOff; ink = UiTheme.Muted; stripe = UiTheme.Faint;
                }
                else                               // State D — legal, but never silent again
                {
                    text = _whenIdle; ink = UiTheme.Ink; stripe = UiTheme.Energy;
                }

                _stripe.BackColor = stripe;
                _why.ForeColor = ink;
                UiLayout.FitInto(_why, text);
            }
            catch (Exception e) { Main.LogDebug($"SystemControlBar sync: {e.Message}"); }
        }
    }
}
