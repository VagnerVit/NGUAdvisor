using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;

namespace NGUAdvisor
{
    // Combined editor for the two single-value systems (Wandoos OS + NGU Difficulty) on one tab. Two
    // labeled sections, each an independent timeline written to its own JSON array (Wandoos "OS" / NGUDiff
    // "Diff"). The value is picked with a click-to-select segmented control rather than a dropdown.
    //
    // Mono-safe explicit layout + single-content-child scroll, UiTheme styling.
    public class WanDiffEditorPanel : UserControl
    {
        private static readonly int CardH = UiTheme.S(92);
        private static readonly int CardGap = UiTheme.S(8);
        private static readonly int SectionGap = UiTheme.S(18);
        private static readonly int HeaderH = UiTheme.S(26);
        private static readonly int AddH = UiTheme.SNumRow(28);
        private static readonly int OuterPad = UiTheme.S(8);
        private static readonly int StripW = UiTheme.S(6);

        private class Section
        {
            public string Title;
            public List<ProfileModel.ValueBreakpoint> Data;
            public IReadOnlyList<KeyValuePair<int, string>> Options;
            public Color Accent;
            public string Label;
            public readonly List<Card> Cards = new List<Card>();
            public Label Head;
            public Button Add;
        }

        private readonly List<Section> _sections = new List<Section>();
        private readonly Panel _scroll;
        private readonly Panel _content;
        public event EventHandler Changed;

        public WanDiffEditorPanel(
            List<ProfileModel.ValueBreakpoint> wandoos, Color wanAccent, IReadOnlyList<KeyValuePair<int, string>> wanOptions, string wanLabel,
            List<ProfileModel.ValueBreakpoint> ngudiff, Color diffAccent, IReadOnlyList<KeyValuePair<int, string>> diffOptions, string diffLabel)
        {
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            _sections.Add(new Section { Title = "WANDOOS OS", Data = wandoos ?? new List<ProfileModel.ValueBreakpoint>(), Options = wanOptions, Accent = wanAccent, Label = wanLabel });
            _sections.Add(new Section { Title = "NGU DIFFICULTY", Data = ngudiff ?? new List<ProfileModel.ValueBreakpoint>(), Options = diffOptions, Accent = diffAccent, Label = diffLabel });

            _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Ground };
            _content = new Panel { Location = new Point(0, 0), BackColor = UiTheme.Ground };
            _scroll.Controls.Add(_content);
            Controls.Add(_scroll);

            foreach (var sec in _sections)
            {
                sec.Head = new Label { Text = sec.Title, AutoSize = true, ForeColor = sec.Accent, Font = UiTheme.Bold };
                _content.Controls.Add(sec.Head);
                sec.Add = new Button { Text = "+ Add time breakpoint", Height = AddH, Width = UiTheme.S(170), Font = UiTheme.Ui };
                UiTheme.StyleFlat(sec.Add);
                var captured = sec;
                sec.Add.Click += (s, e) => AddBreakpoint(captured);
                _content.Controls.Add(sec.Add);
                foreach (var bp in sec.Data)
                    AddCard(sec, bp);
            }

            _scroll.ClientSizeChanged += (s, e) => Relayout();
            Relayout();
        }

        private int CardWidth => Math.Max(UiTheme.S(420), _scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - OuterPad * 2);

        private void AddCard(Section sec, ProfileModel.ValueBreakpoint bp)
        {
            var card = new Card(bp, sec.Accent, sec.Options, sec.Label);
            card.Changed += (s, e) => OnChanged();
            card.DeleteRequested += (s, e) => { sec.Data.Remove(bp); sec.Cards.Remove(card); _content.Controls.Remove(card); Relayout(); OnChanged(); };
            sec.Cards.Add(card);
            _content.Controls.Add(card);
        }

        private void AddBreakpoint(Section sec)
        {
            var bp = new ProfileModel.ValueBreakpoint { TimeSeconds = 0 };
            sec.Data.Add(bp);
            AddCard(sec, bp);
            Relayout();
            _scroll.ScrollControlIntoView(sec.Cards[sec.Cards.Count - 1]);
            OnChanged();
        }

        private void Relayout()
        {
            int w = CardWidth;
            int y = OuterPad;
            foreach (var sec in _sections)
            {
                sec.Head.Location = new Point(OuterPad + UiTheme.S(2), y);
                y += HeaderH;
                foreach (var card in sec.Cards)
                {
                    card.SetWidth(w);
                    card.Location = new Point(OuterPad, y);
                    y += card.Height + CardGap;
                }
                sec.Add.Location = new Point(OuterPad, y);
                y += AddH + SectionGap;
            }
            _content.Size = new Size(w + OuterPad * 2, y + OuterPad);
            _scroll.AutoScrollMinSize = _content.Size;
        }

        private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

        // ---------------------------------------------------------------- card

        private class Card : Panel
        {
            private readonly ProfileModel.ValueBreakpoint _bp;
            private readonly NumericUpDown _h, _m, _s;
            private readonly SegmentedSelector _sel;
            private Button _del;
            private bool _loading;

            public event EventHandler Changed;
            public event EventHandler DeleteRequested;

            public Card(ProfileModel.ValueBreakpoint bp, Color accent, IReadOnlyList<KeyValuePair<int, string>> options, string label)
            {
                _bp = bp;
                Height = CardH;
                BorderStyle = BorderStyle.FixedSingle;
                BackColor = UiTheme.Surface;

                var strip = new Panel { Dock = DockStyle.Left, Width = StripW, BackColor = accent };
                var body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(UiTheme.S(12), UiTheme.S(10), UiTheme.S(12), UiTheme.S(10)) };

                var header = new Panel { Dock = DockStyle.Top, Height = UiTheme.S(40), BackColor = UiTheme.Surface };
                var chip = new Panel { Location = new Point(0, UiTheme.S(4)), Size = new Size(UiTheme.S(238), UiTheme.S(30)), BackColor = UiTheme.AccentWeak, BorderStyle = BorderStyle.FixedSingle };
                chip.Controls.Add(new Label { Text = "TIME", Location = new Point(UiTheme.S(8), UiTheme.S(9)), AutoSize = true, ForeColor = UiTheme.Accent, Font = UiTheme.Chip });
                _h = Nud(UiTheme.S(42), 9999); _m = Nud(UiTheme.S(106), 59); _s = Nud(UiTheme.S(170), 59);
                chip.Controls.Add(Sep("h", UiTheme.S(90))); chip.Controls.Add(Sep("m", UiTheme.S(154))); chip.Controls.Add(Sep("s", UiTheme.S(218)));
                chip.Controls.Add(_h); chip.Controls.Add(_m); chip.Controls.Add(_s);
                header.Controls.Add(chip);
                header.Controls.Add(new Label { Text = "of the rebirth", Location = new Point(UiTheme.S(250), UiTheme.S(11)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui });
                _del = new Button { Text = "🗑  Delete breakpoint", Width = UiTheme.S(150), Height = UiTheme.S(26), Top = UiTheme.S(4), Font = UiTheme.Ui };
                UiTheme.StyleFlat(_del); _del.ForeColor = UiTheme.Danger;
                _del.Click += (s, e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
                header.Controls.Add(_del);

                var row = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
                row.Controls.Add(new Label { Text = "Set to:", Location = new Point(UiTheme.S(2), UiTheme.S(8)), AutoSize = true, ForeColor = UiTheme.Ink, Font = UiTheme.Bold });
                _sel = new SegmentedSelector(options, accent) { Location = new Point(UiTheme.S(58), UiTheme.S(4)) };
                _sel.Changed += (s, e) => { if (!_loading) { _bp.Value = _sel.Value; Changed?.Invoke(this, EventArgs.Empty); } };
                row.Controls.Add(_sel);

                body.Controls.Add(row);
                body.Controls.Add(header);
                Controls.Add(body);
                Controls.Add(strip);

                _loading = true;
                _h.Value = Math.Min(_h.Maximum, bp.Hours);
                _m.Value = bp.Minutes;
                _s.Value = bp.Seconds;
                _sel.Value = bp.Value;
                _loading = false;

                _h.ValueChanged += TimeChanged; _m.ValueChanged += TimeChanged; _s.ValueChanged += TimeChanged;
            }

            private static Label Sep(string t, int x) => new Label { Text = t, Location = new Point(x, UiTheme.S(9)), AutoSize = true, ForeColor = UiTheme.Faint, Font = UiTheme.Ui };
            private NumericUpDown Nud(int x, int max)
            {
                NumericUpDown n = new NumericUpDown { Minimum = 0, Maximum = max, Width = UiTheme.S(46), Location = new Point(x, UiTheme.S(4)), Font = UiTheme.Ui, TextAlign = HorizontalAlignment.Right };
                UiTheme.StyleNum(n);
                return n;
            }

            public void SetWidth(int w)
            {
                Width = w;
                int bodyW = w - StripW - 2;
                if (_del != null) _del.Left = bodyW - _del.Width - UiTheme.S(24);
            }

            private void TimeChanged(object sender, EventArgs e)
            {
                if (_loading) return;
                _bp.TimeSeconds = (int)(_h.Value * 3600 + _m.Value * 60 + _s.Value);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        // ---------------------------------------------------------------- segmented selector

        private class SegmentedSelector : Panel
        {
            private readonly List<int> _values = new List<int>();
            private readonly List<Label> _segs = new List<Label>();
            private readonly Color _accent;
            private int _selected = -1;
            public event EventHandler Changed;

            public SegmentedSelector(IReadOnlyList<KeyValuePair<int, string>> options, Color accent)
            {
                _accent = accent;
                Height = UiTheme.S(26);
                BorderStyle = BorderStyle.FixedSingle;

                int x = 0;
                foreach (var kv in options)
                {
                    int wseg = TextRenderer.MeasureText(kv.Value, UiTheme.Ui).Width + UiTheme.S(22);
                    var lab = new Label
                    {
                        Text = kv.Value,
                        Location = new Point(x, 0),
                        Size = new Size(wseg, UiTheme.S(24)),
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = UiTheme.Ui
                    };
                    int idx = _segs.Count;
                    lab.Click += (s, e) => Select(idx, true);
                    _values.Add(kv.Key);
                    _segs.Add(lab);
                    Controls.Add(lab);
                    x += wseg;
                }
                Width = x;
                Restyle();
            }

            public int Value
            {
                get => _selected >= 0 && _selected < _values.Count ? _values[_selected] : (_values.Count > 0 ? _values[0] : 0);
                set { int i = _values.IndexOf(value); Select(i >= 0 ? i : 0, false); }
            }

            private void Select(int idx, bool raise)
            {
                if (idx < 0 || idx >= _segs.Count) return;
                if (_selected == idx && raise) return;
                _selected = idx;
                Restyle();
                if (raise) Changed?.Invoke(this, EventArgs.Empty);
            }

            private void Restyle()
            {
                for (int i = 0; i < _segs.Count; i++)
                {
                    bool on = i == _selected;
                    _segs[i].BackColor = on ? _accent : UiTheme.Surface;
                    _segs[i].ForeColor = on ? Color.White : UiTheme.Muted;
                    _segs[i].Font = on ? UiTheme.Bold : UiTheme.Ui;
                }
            }
        }
    }
}
