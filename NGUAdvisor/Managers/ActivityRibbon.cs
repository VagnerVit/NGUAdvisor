using System;
using System.Drawing;
using System.Windows.Forms;

namespace NGUAdvisor
{
    using NGUAdvisor.Managers;

    // "What happened because of the thing I just clicked?" — answered ON the screen where it was clicked,
    // instead of making the user go and read LOGS. It is NOT a log viewer: one line, one outcome, no
    // history, no filters. LOGS keeps the record; this only says how the last user action turned out.
    //
    // Autonomous advisor activity NEVER lands here — a segment change or a re-snipe would put this on
    // screen permanently and it would become wallpaper. Only user-triggered outcomes report.
    //
    // ★ NOTHING SCHEDULES. There is no Timer (it provably never ticks in these windows). Expiry is
    // evaluated in Sync(now), which the existing Main.Update -> SettingsForm.UpdateStatus tick already
    // calls — so a message ages out with no user interaction, and the correctness lives in
    // Activity.Expired(ReportedAt, now), not in paint timing.
    public class ActivityRibbon : Panel
    {
        // DERIVED, not scaled: the two buttons sit at y=4 and their height is now floored at the full
        // line box (SCtl), so a scaled 28 would cut their chrome — the ribbon is that row plus the
        // padding it always had, 4 above and 4 below.
        public static readonly int RibbonHeight = UiTheme.S(4) + UiTheme.SCtl(20) + UiTheme.S(4);

        private readonly Panel _stripe;
        private readonly Label _msg;
        private readonly Button _logBtn;
        private readonly Button _dismiss;
        private readonly SettingsForm _form;

        // Change-gate: what is currently PAINTED. Sync compares against this and returns without touching
        // a single control when nothing moved — which is the common case, every frame.
        private int _shownSeq;

        public ActivityRibbon(SettingsForm form)
        {
            _form = form;
            Dock = DockStyle.Top;
            Height = RibbonHeight;
            BackColor = UiTheme.Surface;
            Visible = false;   // zero footprint until there is something to say

            // Severity rides the stripe, exactly as SystemControlBar does — one visual language for
            // "how bad is this", and no invented colour tokens.
            _stripe = new Panel { Dock = DockStyle.Left, Width = UiTheme.S(3), BackColor = UiTheme.Cap };
            Controls.Add(_stripe);

            _dismiss = new Button { Text = "✕", Size = new Size(Math.Max(UiTheme.S(26), UiLayout.MeasureText("✕", UiTheme.Chip) + UiTheme.S(12)), UiTheme.SCtl(20)), Font = UiTheme.Chip };
            UiTheme.StyleIcon(_dismiss);
            _dismiss.Click += (s, e) => { Activity.Dismiss(); Sync(DateTime.UtcNow); };
            Controls.Add(_dismiss);

            _logBtn = new Button { Text = "Open log", Size = new Size(UiLayout.BtnWidth("Open log") + UiTheme.S(6), UiTheme.SCtl(20)), Font = UiTheme.Ui, Visible = false };
            UiTheme.StyleGhost(_logBtn);
            // The failure detail lives in inject.log, which the LOGS screen shows as SESSION — the ADVISOR
            // source is the decision feed and does NOT contain it. Landing the user on a page without the
            // thing they clicked to see would be a dead link wearing a working one's clothes.
            _logBtn.Click += (s, e) => { try { _form?.NavigateTo(Destinations.LogsSession); } catch { } };
            Controls.Add(_logBtn);

            // Dynamic text in a fixed-size label => must go through UiLayout.FitInto (an overflowing Mono
            // label paints NOTHING, and a shortened message needs the full one as a tooltip). Width is
            // set in Place().
            _msg = new Label
            {
                AutoSize = false,
                Height = UiTheme.TextH,
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Ink,
                BackColor = UiTheme.Surface,
                Location = new Point(UiTheme.S(12), UiTheme.S(4))
            };
            Controls.Add(_msg);

            Resize += (s, e) =>
            {
                Place();
                var a = Activity.Current;
                if (a != null && !Activity.Expired(a, DateTime.UtcNow)) Repaint(a);
            };
            Place();
        }

        private void Place()
        {
            _dismiss.Location = new Point(Math.Max(UiTheme.S(40), Width - _dismiss.Width - UiTheme.S(8)), UiTheme.S(4));
            _logBtn.Location = new Point(Math.Max(UiTheme.S(40), _dismiss.Left - _logBtn.Width - UiTheme.S(6)), UiTheme.S(4));
            int right = _logBtn.Visible ? _logBtn.Left : _dismiss.Left;
            _msg.Width = Math.Max(UiTheme.S(60), right - UiTheme.S(12) - UiTheme.S(8));
        }

        // Called every frame from SettingsForm.UpdateStatus (main thread). MUST stay cheap: when nothing
        // has changed this is a couple of comparisons and a return — no text assignment, no layout, no
        // control creation, no handler churn.
        public void Sync(DateTime nowUtc)
        {
            var a = Activity.Current;
            bool show = a != null && !Activity.Expired(a, nowUtc);

            if (!show)
            {
                if (_shownSeq != 0)
                {
                    _shownSeq = 0;
                    Visible = false;
                }
                return;
            }

            if (a.Seq == _shownSeq) return;   // already painted — the common case
            Repaint(a);
        }

        private void Repaint(ActivityItem a)
        {
            if (a == null) return;

            _stripe.BackColor = StripeFor(a.Kind);

            bool wantLog = a.LogLink;
            if (_logBtn.Visible != wantLog) { _logBtn.Visible = wantLog; Place(); }

            string text = string.IsNullOrEmpty(a.Detail) ? a.Message : $"{a.Message} — {a.Detail}";
            UiLayout.FitInto(_msg, text);

            _shownSeq = a.Seq;
            if (!Visible) Visible = true;
        }

        private static Color StripeFor(ActivityKind k)
        {
            switch (k)
            {
                case ActivityKind.Failure: return UiTheme.Danger;
                case ActivityKind.Warning: return UiTheme.Energy;
                case ActivityKind.Queued: return UiTheme.Accent;
                default: return UiTheme.Cap;
            }
        }
    }
}
