using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NGUAdvisor.Managers
{
    // The app's tooltip. Under the game's Mono neither the WinForms ToolTip nor MouseEnter fires while
    // the advisor window is not the active one — and it rarely is, it sits beside the game — and a
    // WinForms Timer never ticks here (GearEditorPanel), so hover is detected by polling the cursor from
    // SettingsForm.UpdateStatus, the per-frame pump Main.Update drives. One borderless card for the
    // whole app (GDI handles are scarce here), created on first use on the UI thread.
    internal static class HelpPopup
    {
        private sealed class Tip { public string Text; public bool Instant; }

        private sealed class Card : Form
        {
            protected override bool ShowWithoutActivation => true;
        }

        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder s, int n);
        private static readonly uint OwnPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        private static readonly ConditionalWeakTable<Control, Tip> _tips = new ConditionalWeakTable<Control, Tip>();
        private static readonly List<WeakReference> _registered = new List<WeakReference>();
        private static Card _card;
        private static Panel _inner, _stripe;
        private static Label _text;
        private static Control _hovered, _shown;
        private static DateTime _hoverSince;

        // Hover delay for tips on ordinary controls; a "?" mark exists only to be read, so it opens at once.
        private const int HoverDelayMs = 400;
        private static int MaxTextW => UiTheme.S(360);

        public static void Set(Control c, string text, bool instant = false)
        {
            if (c == null) return;
            if (!_tips.TryGetValue(c, out Tip tip))
            {
                tip = new Tip();
                _tips.Add(c, tip);
                _registered.Add(new WeakReference(c));
                c.Disposed += (s, e) => { if (_shown == c) Hide(); };
            }
            tip.Text = text;
            tip.Instant |= instant;
            if (string.IsNullOrEmpty(text) && _shown == c) Hide();
        }

        // Clicking opens it too; the poll closes it once the cursor leaves.
        public static void Toggle(Control c) => Show(c);

        public static void Poll()
        {
            try
            {
                Point pos = Cursor.Position;
                Control over = ControlUnder(pos);
                if (over != _hovered)
                {
                    _hovered = over;
                    _hoverSince = DateTime.UtcNow;
                }
                // Closes whenever the cursor is not on the control it belongs to — not only on a change,
                // or a card opened by a click would stay up once the cursor left for empty space.
                if (_shown != null && _shown != over) Hide();
                if (over == null || over == _shown) return;
                if (!_tips.TryGetValue(over, out Tip tip) || string.IsNullOrEmpty(tip.Text)) return;
                if (tip.Instant || (DateTime.UtcNow - _hoverSince).TotalMilliseconds >= HoverDelayMs) Show(over);
            }
            catch (Exception e) { Main.LogDebug($"HelpPopup poll: {e.Message}"); }
        }

        // The registered control under the cursor, only when an advisor window is what is there (not the
        // game or another app covering it). Mono's control handles do not match the Win32 window, so the
        // test is "a Mono.WinForms window of this process", not a handle comparison. The last registered
        // match wins, which is the innermost one for controls built parent-first.
        private static Control ControlUnder(Point pos)
        {
            if (!AdvisorWindowAt(pos)) return null;
            Control found = null;
            for (int i = _registered.Count - 1; i >= 0; i--)
            {
                Control c = _registered[i].Target as Control;
                if (c == null || c.IsDisposed) { _registered.RemoveAt(i); continue; }
                if (found != null || !c.Visible || !c.IsHandleCreated) continue;
                Form f = c.FindForm();
                if (f == null || !f.Visible || f == _card) continue;
                if (c.RectangleToScreen(c.ClientRectangle).Contains(pos)) found = c;
            }
            return found;
        }

        private static bool AdvisorWindowAt(Point pos)
        {
            IntPtr hwnd = WindowFromPoint(pos);
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != OwnPid) return false;
            var cls = new System.Text.StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            return cls.ToString().StartsWith("Mono.WinForms", StringComparison.Ordinal);
        }

        private static void Show(Control c)
        {
            try
            {
                if (c.IsDisposed || !_tips.TryGetValue(c, out Tip tip) || string.IsNullOrEmpty(tip.Text)) return;
                Ensure();

                string wrapped = UiLayout.WrapText(tip.Text, UiTheme.Ui, MaxTextW, 12);
                string[] lines = wrapped.Split('\n');
                int textW = 0;
                foreach (string l in lines) textW = Math.Max(textW, UiLayout.MeasureText(l, UiTheme.Ui));
                textW += UiTheme.S(8);   // Mono paints wider than TextRenderer measures
                int textH = lines.Length * UiTheme.LinePitch;

                int padX = UiTheme.S(12), padY = UiTheme.S(8), stripeW = UiTheme.S(3);
                _text.Text = wrapped;
                _text.Bounds = new Rectangle(stripeW + padX, padY, textW, textH);
                _inner.Bounds = new Rectangle(1, 1, _text.Right + padX, _text.Bottom + padY);
                _stripe.Height = _inner.Height;
                _card.ClientSize = new Size(_inner.Width + 2, _inner.Height + 2);

                Point at = c.PointToScreen(new Point(0, c.Height + UiTheme.S(4)));
                Rectangle area = Screen.FromControl(c).WorkingArea;
                if (at.X + _card.Width > area.Right) at.X = Math.Max(area.Left, area.Right - _card.Width);
                if (at.Y + _card.Height > area.Bottom) at.Y = Math.Max(area.Top, c.PointToScreen(Point.Empty).Y - _card.Height - UiTheme.S(4));
                _card.Location = at;
                if (!_card.Visible) _card.Show();
                _card.BringToFront();
                _shown = c;
            }
            catch (Exception e) { Main.LogDebug($"HelpPopup: {e.Message}"); }
        }

        private static void Hide()
        {
            _shown = null;
            try { if (_card != null && !_card.IsDisposed && _card.Visible) _card.Hide(); }
            catch { }
        }

        private static void Ensure()
        {
            if (_card != null && !_card.IsDisposed) return;
            _card = new Card
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                TopMost = true,
                BackColor = UiTheme.Border
            };
            _inner = new Panel { BackColor = UiTheme.Surface };
            _stripe = new Panel { Location = new Point(0, 0), Width = UiTheme.S(3), BackColor = UiTheme.Accent };
            _text = new Label { AutoSize = false, Font = UiTheme.Ui, ForeColor = UiTheme.Ink, BackColor = UiTheme.Surface };
            _inner.Controls.Add(_stripe);
            _inner.Controls.Add(_text);
            _card.Controls.Add(_inner);
        }
    }
}
