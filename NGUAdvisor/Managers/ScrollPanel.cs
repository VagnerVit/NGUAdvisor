using System;
using System.Drawing;
using System.Windows.Forms;

namespace NGUAdvisor.Managers
{
    // The app's scrolling surface, with a scrollbar we can actually size.
    //
    // WHY NOT AutoScroll. `Panel.AutoScroll` draws the OS scrollbar, and its width is a system metric with
    // no property behind it — on a 200%-scaling display it stays a thin sliver beside 38px text, which is
    // exactly as hard to grab as it looks. There is no setting for it. The only way to get a scrollbar
    // sized like the rest of the UI is to stop using AutoScroll and own the scrolling: an explicit
    // VScrollBar (whose Width *is* settable) beside a content panel this class offsets itself.
    //
    // That also fixes what AutoScroll got wrong besides the width:
    //
    //  1. TEARING. AutoScroll scrolls by blitting the client area and invalidating only the newly-exposed
    //     strip, so children painting outside the blit — the owner-drawn lists especially — left their old
    //     pixels behind as streaks across the window. Offsetting a double-buffered content panel repaints
    //     the whole visible surface, which is what the eye expects.
    //
    //  2. TOUCHPAD SCROLLING. WinForms turns a wheel NOTCH into scrolling. A precision touchpad does not
    //     send notches, it sends a stream of far smaller deltas, each of which rounded to zero or to a
    //     whole notch — so the page either refused to move or jumped. Delta is accumulated here and spent
    //     in measured line units.
    //
    //  3. THE WHEEL GOING TO THE WRONG CONTROL. A ListBox eats the wheel even at its own end, stopping the
    //     page dead. See ForwardWheel.
    //
    // USAGE — the one thing to remember: children go into `Content`, not into the ScrollPanel. Adding to
    // the panel itself would put them beside the scrollbar and outside the surface that moves. Both
    // Dock-based children (Settings stacks two Dock.Top panels) and absolutely-positioned ones work.
    public class ScrollPanel : Panel
    {
        // Wide enough to hit. The OS metric is ~17px unscaled and that is the entire problem, so this is
        // derived from the measured text like every other dimension in the app.
        private static int BarW => Math.Max(UiTheme.S(17), UiTheme.LineH * 2 / 3);

        private readonly VScrollBar _bar;
        private readonly Panel _content;
        private int _accum;
        private bool _syncing;

        // Some sections do not scroll THEMSELVES: Advisors and Systems host Dock.Fill sub-pages, and it is
        // the sub-page that scrolls. Such a host must give its content the viewport exactly — a content
        // panel sized to its children would be circular when the child is Dock.Fill (child bottom defines
        // content height defines child bottom), and would collapse to nothing.
        public bool Scrollable { get; set; } = true;

        // Where children belong. Named rather than hidden behind an overridden Controls collection: a
        // redirected Add is the kind of cleverness that makes a later reader's Place() call inexplicable.
        public Panel Content { get { return _content; } }

        public ScrollPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            DoubleBuffered = true;

            _bar = new VScrollBar { Dock = DockStyle.Right, Width = BarW, Visible = false, SmallChange = UiTheme.LinePitch };
            _bar.ValueChanged += (s, e) => { if (!_syncing) _content.Top = -_bar.Value; };

            _content = new Panel { Location = new Point(0, 0), BackColor = BackColor };
            _content.ControlAdded += ContentChildAdded;
            _content.ControlRemoved += (s, e) => BeginInvokeSync();
            _content.Resize += (s, e) => Sync();

            Controls.Add(_bar);
            Controls.Add(_content);

            // The wheel may be delivered to the content panel rather than to us — a plain Panel neither
            // handles it nor passes it on, which would leave the page dead to the wheel over most of its
            // own surface. Route it explicitly.
            ForwardWheel(_content);
        }

        // A child that REFLOWS changes the scroll range and nothing else would notice: the autopilot card
        // grows when a plan wraps, BasicSettingsPanel shrinks to its search results, a list re-fills. The
        // container is the only thing that can keep the range honest, so it watches its children.
        private void ContentChildAdded(object sender, ControlEventArgs e)
        {
            if (e.Control != null)
            {
                e.Control.SizeChanged -= ChildSizeChanged;
                e.Control.SizeChanged += ChildSizeChanged;
                e.Control.VisibleChanged -= ChildSizeChanged;
                e.Control.VisibleChanged += ChildSizeChanged;
            }
            Sync();
        }

        private void ChildSizeChanged(object sender, EventArgs e) => Sync();

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            if (_content != null) _content.BackColor = BackColor;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Sync();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // Becoming visible is when a section's real width is finally known, so the range computed
            // while it was hidden is the wrong one.
            if (Visible) Sync();
        }

        private void BeginInvokeSync()
        {
            // ControlRemoved fires BEFORE the child leaves the collection, so measuring now would still
            // count it. Defer to after the removal completes — and only if there is a handle to post to.
            try { if (IsHandleCreated) BeginInvoke((Action)Sync); }
            catch { }
        }

        // The one place that reconciles content height, viewport height, bar visibility and bar range.
        // Everything else just calls this.
        public void Sync()
        {
            if (_syncing || _content == null || _bar == null) return;
            _syncing = true;
            try
            {
                if (!Scrollable)
                {
                    _bar.Visible = false;
                    _content.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
                    return;
                }

                int need = ContentExtent();
                int viewport = ClientSize.Height;
                bool show = need > viewport && viewport > 0;

                _bar.Visible = show;
                // The content is as wide as whatever is left beside the bar. Docked children lay out
                // against this, so it has to be set before the range is computed from their bottoms.
                int w = Math.Max(0, ClientSize.Width - (show ? _bar.Width : 0));
                if (_content.Width != w) _content.Width = w;
                if (_content.Height != need) _content.Height = need;

                if (!show)
                {
                    _bar.Value = 0;
                    _content.Top = 0;
                    return;
                }

                int max = Math.Max(0, need - viewport);
                // WinForms quirk worth stating: the highest reachable Value is Maximum - LargeChange + 1,
                // so Maximum is NOT the last scroll offset. Setting LargeChange first keeps the clamp
                // below from reading a stale range.
                _bar.LargeChange = Math.Max(1, viewport);
                _bar.Maximum = Math.Max(0, need - 1);
                _bar.SmallChange = UiTheme.LinePitch;
                if (_bar.Value > max) _bar.Value = max;
                _content.Top = -_bar.Value;
            }
            catch (Exception e) { Main.LogDebug($"ScrollPanel.Sync: {e.Message}"); }
            finally { _syncing = false; }
        }

        // How tall the content really is. Docked children (Settings' two stacked panels) report through
        // their own bottoms just like absolutely-placed ones, so one rule covers both.
        private int ContentExtent()
        {
            int bottom = 0;
            foreach (Control c in _content.Controls)
                if (c.Visible && c.Bottom > bottom) bottom = c.Bottom;
            return bottom;
        }

        // Replaces `AutoScrollPosition = Point.Empty`, which no longer means anything here. Every search
        // query lands at the top of the results, so this is called on that path too.
        public void ScrollToTop()
        {
            try
            {
                Sync();
                if (_bar.Visible) _bar.Value = 0;
                _content.Top = 0;
            }
            catch { }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(e.Delta > 0 ? -1 : 1, e.Delta);
            if (e is HandledMouseEventArgs h) h.Handled = true;
            // Deliberately NOT calling base: it would add its own notch-based scroll on top of ours.
        }

        // dir is the direction (-1 up, +1 down); raw is the wheel delta being accumulated. A touchpad
        // sends many deltas smaller than a notch (120), and dividing each one separately is what threw
        // the movement away.
        private void ScrollBy(int dir, int raw)
        {
            _accum += Math.Abs(raw);
            int notches = _accum / 120;
            if (notches <= 0) return;
            _accum -= notches * 120;
            ScrollPixels(dir * notches * UiTheme.LinePitch * 2);
        }

        private void ScrollPixels(int dy)
        {
            if (dy == 0) return;
            Sync();
            if (!_bar.Visible) return;
            int max = Math.Max(0, _content.Height - ClientSize.Height);
            int v = _bar.Value + dy;
            if (v < 0) v = 0;
            if (v > max) v = max;
            _bar.Value = v;   // ValueChanged moves the content
        }

        private bool CanScroll(int dir)
        {
            if (!_bar.Visible) return false;
            int max = Math.Max(0, _content.Height - ClientSize.Height);
            return dir < 0 ? _bar.Value > 0 : _bar.Value < max;
        }

        // Give a child's wheel to the nearest ScrollPanel once that child is at its own limit. A NAMED
        // handler, not a lambda, so the -=/+= pair genuinely removes the old subscription: the style
        // helpers are idempotent by contract (StyleList may run twice on one list) and a lambda would
        // stack handlers and scroll by a multiple of what was asked.
        public static void ForwardWheel(Control child)
        {
            if (child == null) return;
            child.MouseWheel -= ChildWheel;
            child.MouseWheel += ChildWheel;
        }

        private static void ChildWheel(object sender, MouseEventArgs e)
        {
            var c = sender as Control;
            if (c == null) return;
            if (c is ListBox lb && !AtEnd(lb, e.Delta)) return;   // the list can still scroll — leave it
            var host = FindHost(c);
            if (host == null) return;
            int dir = e.Delta > 0 ? -1 : 1;
            if (!host.CanScroll(dir)) return;
            host.ScrollBy(dir, e.Delta);
            if (e is HandledMouseEventArgs h) h.Handled = true;
        }

        private static bool AtEnd(ListBox lb, int delta)
        {
            try
            {
                if (delta > 0) return lb.TopIndex <= 0;
                // ItemHeight is the owner-drawn pitch (UiTheme.StyleList), so this counts REAL rows.
                int visible = Math.Max(1, lb.ClientSize.Height / Math.Max(1, lb.ItemHeight));
                return lb.TopIndex + visible >= lb.Items.Count;
            }
            catch { return true; }
        }

        private static ScrollPanel FindHost(Control c)
        {
            for (var p = c.Parent; p != null; p = p.Parent)
            {
                var sp = p as ScrollPanel;
                if (sp != null) return sp;
            }
            return null;
        }
    }
}
