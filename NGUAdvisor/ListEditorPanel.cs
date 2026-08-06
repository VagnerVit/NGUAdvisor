using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;

namespace NGUAdvisor
{
    // Editor for a slot-limited priority timeline (Diggers / Beards).
    //
    // Model per breakpoint: every item is present in one reorderable PRIORITY list; a "Slots" number
    // (max = item count: 12 diggers / 7 beards) sets how many are active. The top `Slots` items (in order)
    // are what gets saved to the profile's List and run; the rest sit below the slot-limit line as bench.
    //
    // Mono-safe explicit layout + single-content-child scroll, UiTheme styling.
    public class ListEditorPanel : UserControl
    {
        private static readonly int RowH = UiTheme.SNumRow(26);   // holds a NumericUpDown — derive, don't scale
        // Containers DERIVED from what they hold (ui-infra.md): the chip from the NumericUpDown line,
        // the column strip from the header font.
        private static readonly int ChipH = UiTheme.NumH + UiTheme.S(6);
        private static readonly int HeaderH = ChipH + UiTheme.S(8);
        private static readonly int SlotsH = UiTheme.SCtl(30);
        private static readonly int ColHeadH = UiTheme.SHead(20) + UiTheme.S(4);
        private static readonly int IconW = IconWidth();
        private static readonly int IconPitch = IconW + UiTheme.S(2);

        private static int IconWidth()
        {
            int w = UiTheme.S(26);
            foreach (string glyph in new[] { "↑", "↓" })
                w = Math.Max(w, UiLayout.MeasureText(glyph, UiTheme.Ui) + UiTheme.S(16));
            return w;
        }
        private static readonly int BodyPad = UiTheme.S(10);
        private static readonly int StripW = UiTheme.S(6);
        private static readonly int CardGap = UiTheme.S(10);
        private static readonly int OuterPad = UiTheme.S(8);

        private readonly List<ProfileModel.ListBreakpoint> _data;
        private readonly Color _accent;
        private readonly IReadOnlyList<KeyValuePair<int, string>> _options;
        private readonly Panel _scroll;
        private readonly Panel _content;
        private readonly List<Card> _cards = new List<Card>();
        public event EventHandler Changed;

        public ListEditorPanel(List<ProfileModel.ListBreakpoint> data, Color accent, IReadOnlyList<KeyValuePair<int, string>> options)
        {
            _data = data ?? new List<ProfileModel.ListBreakpoint>();
            _accent = accent;
            _options = options;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Ground };
            _content = new Panel { Location = new Point(0, 0), BackColor = UiTheme.Ground };
            _scroll.Controls.Add(_content);

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = UiTheme.S(42), FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(UiTheme.S(8), UiTheme.S(8), 0, 0), BackColor = UiTheme.Ground };
            var addBtn = new Button { Text = "+ Add time breakpoint", Height = UiTheme.SCtl(26), Width = UiLayout.BtnWidth("+ Add time breakpoint"), Font = UiTheme.Ui };
            UiTheme.StyleFlat(addBtn);
            addBtn.Click += (s, e) => AddBreakpoint();
            toolbar.Controls.Add(addBtn);
            toolbar.Controls.Add(new Label { Text = "Rank every item; set your available slots. The top items (above the line) are active.", AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui, Margin = new Padding(UiTheme.S(10), UiTheme.S(6), 0, 0) });

            Controls.Add(_scroll);
            Controls.Add(toolbar);

            _scroll.ClientSizeChanged += (s, e) => Relayout();
            RebuildCards();
        }

        private int CardWidth => Math.Max(UiTheme.S(420), _scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - OuterPad * 2);

        private void RebuildCards()
        {
            _content.SuspendLayout();
            foreach (var c in _cards) _content.Controls.Remove(c);
            _cards.Clear();
            foreach (var bp in _data)
            {
                var card = Build(bp);
                _cards.Add(card);
                _content.Controls.Add(card);
            }
            _content.ResumeLayout();
            Relayout();
        }

        private Card Build(ProfileModel.ListBreakpoint bp)
        {
            var card = new Card(bp, _accent, _options);
            card.Changed += (s, e) => OnChanged();
            card.CardResized += (s, e) => Relayout();
            card.DeleteRequested += (s, e) => { _data.Remove(bp); RebuildCards(); OnChanged(); };
            return card;
        }

        private void AddBreakpoint()
        {
            var bp = new ProfileModel.ListBreakpoint { TimeSeconds = 0 };
            _data.Add(bp);
            var card = Build(bp);
            _cards.Add(card);
            _content.Controls.Add(card);
            Relayout();
            _scroll.ScrollControlIntoView(card);
            OnChanged();
        }

        private void Relayout()
        {
            int w = CardWidth;
            int y = OuterPad;
            foreach (var card in _cards)
            {
                card.SetWidth(w);
                card.Location = new Point(OuterPad, y);
                y += card.Height + CardGap;
            }
            _content.Size = new Size(w + OuterPad * 2, y + OuterPad);
            _scroll.AutoScrollMinSize = _content.Size;
        }

        private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

        // ---------------------------------------------------------------- card

        private class Card : Panel
        {
            private readonly ProfileModel.ListBreakpoint _bp;
            private readonly IReadOnlyList<KeyValuePair<int, string>> _options;
            private readonly Panel _body, _rows;
            private readonly NumericUpDown _h, _m, _s;
            private readonly NumericUpDown _slots;
            private readonly Label _slotsInfo;
            private readonly Label _orderHdr;
            private Button _del;
            private ComboBox _chTag;
            private bool _loading;

            public event EventHandler Changed;
            public event EventHandler DeleteRequested;
            public event EventHandler CardResized;

            public Card(ProfileModel.ListBreakpoint bp, Color accent, IReadOnlyList<KeyValuePair<int, string>> options)
            {
                _bp = bp;
                _options = options;
                BorderStyle = BorderStyle.FixedSingle;
                BackColor = UiTheme.Surface;

                var strip = new Panel { Dock = DockStyle.Left, Width = StripW, BackColor = accent };
                _body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(UiTheme.S(12), BodyPad, UiTheme.S(12), BodyPad) };

                var header = new Panel { Dock = DockStyle.Top, Height = HeaderH, BackColor = UiTheme.Surface };
                var chip = new Panel { Location = new Point(0, UiTheme.S(4)), Size = new Size(UiTheme.S(238), ChipH), BackColor = UiTheme.AccentWeak, BorderStyle = BorderStyle.FixedSingle };
                int chipLblY = (ChipH - UiTheme.HeadH) / 2;
                int chipTextY = (ChipH - UiTheme.LineH) / 2;
                chip.Controls.Add(new Label { Text = "TIME", Location = new Point(UiTheme.S(8), chipLblY), AutoSize = true, ForeColor = UiTheme.Accent, Font = UiTheme.Chip });
                _h = Nud(UiTheme.S(42), 9999); _m = Nud(UiTheme.S(106), 59); _s = Nud(UiTheme.S(170), 59);
                chip.Controls.Add(Sep("h", UiTheme.S(90), chipTextY)); chip.Controls.Add(Sep("m", UiTheme.S(154), chipTextY)); chip.Controls.Add(Sep("s", UiTheme.S(218), chipTextY));
                chip.Controls.Add(_h); chip.Controls.Add(_m); chip.Controls.Add(_s);
                header.Controls.Add(chip);
                header.Controls.Add(new Label { Text = "of the rebirth", Location = new Point(UiTheme.S(250), UiTheme.S(4) + chipTextY), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui });
                _del = new Button { Text = "🗑  Delete breakpoint", Width = UiLayout.BtnWidth("🗑  Delete breakpoint"), Height = UiTheme.SCtl(26), Font = UiTheme.Ui };
                _del.Top = (HeaderH - _del.Height) / 2;
                UiTheme.StyleFlat(_del); _del.ForeColor = UiTheme.Danger;
                _del.Click += (s, e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
                header.Controls.Add(_del);
                // Challenge tag: this breakpoint applies only while the picked challenge is active.
                _chTag = ChallengeTagUi.Attach(header, () => _bp.Challenge, v => { _bp.Challenge = v; Changed?.Invoke(this, EventArgs.Empty); });

                var slotsRow = new Panel { Dock = DockStyle.Top, Height = SlotsH, BackColor = UiTheme.Surface };
                slotsRow.Controls.Add(new Label { Text = "Slots available:", Location = new Point(UiTheme.S(2), UiTheme.S(6)), AutoSize = true, ForeColor = UiTheme.Ink, Font = UiTheme.Bold });
                _slots = new NumericUpDown { Minimum = 0, Maximum = options.Count, Width = UiTheme.S(50), Location = new Point(UiTheme.S(102), UiTheme.S(3)), Font = UiTheme.Ui, TextAlign = HorizontalAlignment.Right };
                UiTheme.StyleNum(_slots);
                slotsRow.Controls.Add(_slots);
                slotsRow.Controls.Add(new Label { Text = $"of {options.Count}", Location = new Point(UiTheme.S(158), UiTheme.S(6)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui });
                _slotsInfo = new Label { Location = new Point(UiTheme.S(210), UiTheme.S(6)), AutoSize = true, ForeColor = UiTheme.Cap, Font = UiTheme.Ui };
                slotsRow.Controls.Add(_slotsInfo);

                var colHead = new Panel { Dock = DockStyle.Top, Height = ColHeadH, BackColor = UiTheme.Surface };
                colHead.Controls.Add(new Label { Text = "PRIORITY  (top = highest)", Location = new Point(UiTheme.S(6), UiTheme.S(3)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.ColHeader });
                _orderHdr = new Label { Text = "MOVE", Location = new Point(0, UiTheme.S(3)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.ColHeader };
                colHead.Controls.Add(_orderHdr);
                colHead.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border });

                _rows = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };

                _body.Controls.Add(_rows);
                _body.Controls.Add(colHead);
                _body.Controls.Add(slotsRow);
                _body.Controls.Add(header);

                Controls.Add(_body);
                Controls.Add(strip);

                _loading = true;
                _h.Value = Math.Min(_h.Maximum, bp.Hours);
                _m.Value = bp.Minutes;
                _s.Value = bp.Seconds;
                BuildRows();
                _slots.Value = Math.Min(_slots.Maximum, Math.Max(0, bp.Items.Count(id => options.Any(o => o.Key == id))));
                _loading = false;
                RefreshActive();

                _h.ValueChanged += TimeChanged; _m.ValueChanged += TimeChanged; _s.ValueChanged += TimeChanged;
                _slots.ValueChanged += (s, e) => { RefreshActive(); Sync(); };
            }

            // Build one row per item: saved (active) items first in their saved order, then the rest.
            private void BuildRows()
            {
                var seen = new HashSet<int>();
                var order = new List<int>();
                foreach (var id in _bp.Items)
                    if (_options.Any(o => o.Key == id) && seen.Add(id)) order.Add(id);
                foreach (var o in _options)
                    if (seen.Add(o.Key)) order.Add(o.Key);

                foreach (var id in order)
                    AddRow(new Row(id, SystemCatalog.NameOf(_options, id)));
            }

            private static Label Sep(string t, int x, int y) => new Label { Text = t, Location = new Point(x, y), AutoSize = true, ForeColor = UiTheme.Faint, Font = UiTheme.Ui };
            private NumericUpDown Nud(int x, int max)
            {
                NumericUpDown n = new NumericUpDown { Minimum = 0, Maximum = max, Width = UiTheme.S(46), Font = UiTheme.Ui, TextAlign = HorizontalAlignment.Right };
                UiTheme.StyleNum(n);   // states the height, so centre AFTER it
                n.Location = new Point(x, (ChipH - n.Height) / 2);
                return n;
            }

            public void SetWidth(int w)
            {
                Width = w;
                int rowW = w - StripW - UiTheme.S(24) - UiTheme.S(4);
                foreach (Control c in _rows.Controls) if (c is Row r) r.SetWidth(rowW);
                int bodyW = w - StripW - 2;
                if (_del != null) _del.Left = bodyW - _del.Width - UiTheme.S(24);
                // Anchor the challenge picker left of Delete so they can never collide.
                if (_chTag != null && _del != null) _chTag.Left = _del.Left - _chTag.Width - UiTheme.S(10);
                _orderHdr.Left = rowW - 2 * IconPitch;   // stays over the icon block when the icons widen
                RecalcHeight();
            }

            private void RecalcHeight()
            {
                int y = 0;
                foreach (Control c in _rows.Controls) { c.Location = new Point(0, y); y += RowH; }
                int rowsH = Math.Max(RowH, _rows.Controls.Count * RowH);
                int newH = BodyPad * 2 + HeaderH + SlotsH + ColHeadH + rowsH + 2;
                if (Height != newH) { Height = newH; CardResized?.Invoke(this, EventArgs.Empty); }
            }

            private void AddRow(Row row)
            {
                row.Height = RowH;
                row.MoveRequested += (s, e) =>
                {
                    var list = _rows.Controls.Cast<Control>().ToList();
                    int i = list.IndexOf(row);
                    int j = i + e.Direction;
                    if (j < 0 || j >= list.Count) return;
                    _rows.Controls.SetChildIndex(row, j);
                    RefreshActive(); Sync();
                };
                _rows.Controls.Add(row);
            }

            // Mark the top `Slots` rows active, the rest bench; reposition and stripe.
            private void RefreshActive()
            {
                int slots = (int)_slots.Value;
                for (int i = 0; i < _rows.Controls.Count; i++)
                {
                    var c = _rows.Controls[i];
                    c.Location = new Point(0, i * RowH);
                    bool active = i < slots;
                    c.BackColor = active ? (i % 2 == 0 ? UiTheme.Surface : UiTheme.Zebra) : UiTheme.Ground;
                    if (c is Row r) r.SetActive(active, i == slots && slots > 0);
                }
                _slotsInfo.Text = slots == 0 ? "none active" : $"top {slots} active";
            }

            private void TimeChanged(object sender, EventArgs e)
            {
                if (_loading) return;
                _bp.TimeSeconds = (int)(_h.Value * 3600 + _m.Value * 60 + _s.Value);
                OnChanged();
            }

            private void Sync()
            {
                if (_loading) return;
                int slots = (int)_slots.Value;
                _bp.Items.Clear();
                int i = 0;
                foreach (Control c in _rows.Controls)
                {
                    if (i++ >= slots) break;
                    if (c is Row r) _bp.Items.Add(r.Id);
                }
                OnChanged();
            }

            private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
        }

        // ---------------------------------------------------------------- row (fixed item)

        public class MoveEventArgs : EventArgs { public int Direction; }

        private class Row : Panel
        {
            public readonly int Id;
            private readonly Label _name;
            private readonly Label _tag;
            private readonly Button _up, _down;

            public event EventHandler<MoveEventArgs> MoveRequested;

            public Row(int id, string name)
            {
                Id = id;
                Height = RowH;
                _name = new Label { Text = name, Location = new Point(UiTheme.S(8), UiTheme.S(5)), AutoSize = true, Font = UiTheme.Ui };
                _tag = new Label { Location = new Point(0, UiTheme.S(5)), AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Faint };
                _up = Icon("↑"); _down = Icon("↓");
                _up.Click += (s, e) => MoveRequested?.Invoke(this, new MoveEventArgs { Direction = -1 });
                _down.Click += (s, e) => MoveRequested?.Invoke(this, new MoveEventArgs { Direction = 1 });
                Controls.Add(_name); Controls.Add(_tag); Controls.Add(_up); Controls.Add(_down);
                Place();
            }

            public void SetActive(bool active, bool firstBench)
            {
                _name.ForeColor = active ? UiTheme.Ink : UiTheme.Faint;
                _tag.Text = active ? "" : "bench";
                _tag.BackColor = BackColor;
                _name.BackColor = BackColor;
                // A faint top rule on the first bench row marks the slot limit.
                BorderStyle = firstBench ? BorderStyle.FixedSingle : BorderStyle.None;
            }

            public void SetWidth(int w) { Width = w; Place(); }

            private void Place()
            {
                _tag.Location = new Point(Width - 2 * IconPitch - UiTheme.S(70), Mid(UiTheme.LineH));
                int rx = Width - 2 * IconPitch - UiTheme.S(8);
                int iconY = Mid(_up.Height);
                _up.Location = new Point(rx, iconY); _down.Location = new Point(rx + IconPitch, iconY);
            }

            private int Mid(int childHeight) => Math.Max(0, (Height - childHeight) / 2);

            private static Button Icon(string t)
            {
                var b = new Button { Text = t, Width = IconW, Height = UiTheme.SCtl(22), Font = UiTheme.Ui };
                UiTheme.StyleIcon(b);
                return b;
            }
        }
    }
}
