using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;

namespace NGUAdvisor
{
    // Editor for one resource timeline (Energy / Magic / R3), styled to the approved mockup.
    //
    // The priority dropdown offers only the types VALID FOR THIS RESOURCE (per the README), with correct
    // index ranges - there is no free-text option, so a profile can't gain an incorrect allocation type.
    // Any pre-existing token that isn't valid here (e.g. dead WISH tokens) is shown as a read-only,
    // removable "unsupported" row so nothing is silently lost.
    //
    // Mono WinForms: AutoScroll/AutoSize don't compute scroll, so we use an AutoScroll Panel holding ONE
    // fixed-size content child and lay out cards/rows at EXPLICIT positions. Colors/fonts from UiTheme.
    public class ResourceEditorPanel : UserControl
    {
        private static readonly int RowH = UiTheme.SNumRow(30);   // holds a NumericUpDown — derive, don't scale
        private static readonly int HeaderH = UiTheme.S(40);
        private static readonly int ColHeadH = UiTheme.S(20);
        private static readonly int AddH = UiTheme.SNumRow(30);
        private static readonly int BodyPad = UiTheme.S(10);
        private static readonly int StripW = UiTheme.S(6);
        private static readonly int CardGap = UiTheme.S(10);
        private static readonly int OuterPad = UiTheme.S(8);

        private static readonly int ColPrioX = UiTheme.S(6), ColPrioW = UiTheme.S(214);
        private static readonly int ColIdxX = UiTheme.S(226), ColIdxW = UiTheme.S(46);
        private static readonly int ColCapX = UiTheme.S(280), ColCapW = UiTheme.S(84);
        private static readonly int ColPctX = UiTheme.S(374);
        private static readonly int ColScopeX = UiTheme.S(396);
        private static readonly int ColNumX = UiTheme.S(476);
        private static readonly int ColUnitX = UiTheme.S(524);

        private readonly List<ProfileModel.PriorityBreakpoint> _data;
        private readonly Color _accent;
        private readonly ResourceKind _kind;
        private readonly Panel _scroll;
        private readonly Panel _content;
        private readonly List<BreakpointCard> _cards = new List<BreakpointCard>();
        public event EventHandler Changed;

        public ResourceEditorPanel(List<ProfileModel.PriorityBreakpoint> data, Color accent, ResourceKind kind)
        {
            _data = data ?? new List<ProfileModel.PriorityBreakpoint>();
            _accent = accent;
            _kind = kind;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Ground };
            _content = new Panel { Location = new Point(0, 0), BackColor = UiTheme.Ground };
            _scroll.Controls.Add(_content);

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = UiTheme.S(42), FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(UiTheme.S(8), UiTheme.S(8), 0, 0), BackColor = UiTheme.Ground };
            var addBtn = new Button { Text = "+ Add time breakpoint", Height = UiTheme.S(26), Width = UiTheme.S(170), Font = UiTheme.Ui };
            UiTheme.StyleFlat(addBtn);
            addBtn.Click += (s, e) => AddBreakpoint();
            toolbar.Controls.Add(addBtn);
            toolbar.Controls.Add(new Label { Text = "Applied top → bottom; the advisor uses the latest breakpoint whose time has passed.", AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui, Margin = new Padding(UiTheme.S(10), UiTheme.S(6), 0, 0) });

            Controls.Add(_scroll);
            Controls.Add(toolbar);

            _scroll.ClientSizeChanged += (s, e) => Relayout();
            RebuildCards();
        }

        private int CardWidth => Math.Max(UiTheme.S(560), _scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - OuterPad * 2);

        private void RebuildCards()
        {
            _content.SuspendLayout();
            foreach (var c in _cards) _content.Controls.Remove(c);
            _cards.Clear();
            foreach (var bp in _data)
            {
                var card = BuildCard(bp);
                _cards.Add(card);
                _content.Controls.Add(card);
            }
            _content.ResumeLayout();
            Relayout();
        }

        private BreakpointCard BuildCard(ProfileModel.PriorityBreakpoint bp)
        {
            var card = new BreakpointCard(bp, _accent, _kind);
            card.Changed += (s, e) => OnChanged();
            card.CardResized += (s, e) => Relayout();
            card.DeleteRequested += (s, e) => { _data.Remove(bp); RebuildCards(); OnChanged(); };
            return card;
        }

        private void AddBreakpoint()
        {
            var bp = new ProfileModel.PriorityBreakpoint { TimeSeconds = 0 };
            _data.Add(bp);
            var card = BuildCard(bp);
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

        private class BreakpointCard : Panel
        {
            private readonly ProfileModel.PriorityBreakpoint _bp;
            private readonly ResourceKind _kind;
            private readonly Panel _body, _rows, _colHead;
            private readonly NumericUpDown _h, _m, _s;
            private readonly Label _orderHdr;
            private Button _del;
            private ComboBox _chTag;
            private bool _loading;

            public event EventHandler Changed;
            public event EventHandler DeleteRequested;
            public event EventHandler CardResized;

            public BreakpointCard(ProfileModel.PriorityBreakpoint bp, Color accent, ResourceKind kind)
            {
                _bp = bp;
                _kind = kind;
                BorderStyle = BorderStyle.FixedSingle;
                BackColor = UiTheme.Surface;

                var strip = new Panel { Dock = DockStyle.Left, Width = StripW, BackColor = accent };
                _body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(UiTheme.S(12), BodyPad, UiTheme.S(12), BodyPad) };

                var header = new Panel { Dock = DockStyle.Top, Height = HeaderH, BackColor = UiTheme.Surface };
                var chip = new Panel { Location = new Point(0, UiTheme.S(4)), Size = new Size(UiTheme.S(238), UiTheme.S(30)), BackColor = UiTheme.AccentWeak, BorderStyle = BorderStyle.FixedSingle };
                chip.Controls.Add(new Label { Text = "TIME", Location = new Point(UiTheme.S(8), UiTheme.S(9)), AutoSize = true, ForeColor = UiTheme.Accent, Font = UiTheme.Chip });
                _h = Nud(UiTheme.S(42)); _m = Nud(UiTheme.S(106)); _s = Nud(UiTheme.S(170));
                chip.Controls.Add(Sep("h", UiTheme.S(90))); chip.Controls.Add(Sep("m", UiTheme.S(154))); chip.Controls.Add(Sep("s", UiTheme.S(218)));
                chip.Controls.Add(_h); chip.Controls.Add(_m); chip.Controls.Add(_s);
                header.Controls.Add(chip);
                header.Controls.Add(new Label { Text = "of the rebirth", Location = new Point(UiTheme.S(250), UiTheme.S(11)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui });
                _del = new Button { Text = "🗑  Delete breakpoint", Width = UiTheme.S(150), Height = UiTheme.S(26), Top = UiTheme.S(4), Font = UiTheme.Ui };
                UiTheme.StyleFlat(_del); _del.ForeColor = UiTheme.Danger;
                _del.Click += (s, e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
                header.Controls.Add(_del);
                // Challenge tag: this breakpoint applies only while the picked challenge is active.
                _chTag = ChallengeTagUi.Attach(header, () => _bp.Challenge, v => { _bp.Challenge = v; Changed?.Invoke(this, EventArgs.Empty); });

                _colHead = new Panel { Dock = DockStyle.Top, Height = ColHeadH, BackColor = UiTheme.Surface };
                _colHead.Controls.Add(ColLabel("PRIORITY", ColPrioX));
                _colHead.Controls.Add(ColLabel("INDEX", ColIdxX));
                _colHead.Controls.Add(ColLabel("CAP", ColCapX));
                _colHead.Controls.Add(ColLabel("LIMIT %", ColPctX));
                _orderHdr = ColLabel("ORDER", 0);
                _colHead.Controls.Add(_orderHdr);
                _colHead.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border });

                var addPrio = new Button { Text = "+ Add priority", Height = AddH, Dock = DockStyle.Bottom, Font = UiTheme.Ui };
                UiTheme.StyleGhost(addPrio);
                addPrio.Click += (s, e) => { AddRow(new PriorityRow(null, _kind)); Restripe(); Sync(); RecalcHeight(); };

                _rows = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };

                _body.Controls.Add(_rows);
                _body.Controls.Add(addPrio);
                _body.Controls.Add(_colHead);
                _body.Controls.Add(header);

                Controls.Add(_body);
                Controls.Add(strip);

                _loading = true;
                _h.Value = Math.Min(_h.Maximum, bp.Hours);
                _m.Value = bp.Minutes;
                _s.Value = bp.Seconds;
                foreach (var p in bp.Priorities) AddRow(new PriorityRow(p, _kind));
                _loading = false;
                Restripe();

                _h.ValueChanged += TimeChanged; _m.ValueChanged += TimeChanged; _s.ValueChanged += TimeChanged;
            }

            private static Label Sep(string t, int x) => new Label { Text = t, Location = new Point(x, UiTheme.S(9)), AutoSize = true, ForeColor = UiTheme.Faint, Font = UiTheme.Ui };
            private static Label ColLabel(string t, int x) => new Label { Text = t, Location = new Point(x, UiTheme.S(5)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.ColHeader };
            private NumericUpDown Nud(int x)
            {
                NumericUpDown n = new NumericUpDown { Minimum = 0, Maximum = 9999, Width = UiTheme.S(46), Location = new Point(x, UiTheme.S(4)), Font = UiTheme.Ui, TextAlign = HorizontalAlignment.Right };
                UiTheme.StyleNum(n);
                return n;
            }

            public void SetWidth(int w)
            {
                Width = w;
                int rowW = w - StripW - UiTheme.S(24) - UiTheme.S(4);
                foreach (Control c in _rows.Controls) if (c is PriorityRow r) r.SetWidth(rowW);
                int bodyW = w - StripW - 2;
                if (_del != null) _del.Left = bodyW - _del.Width - UiTheme.S(24);
                // Anchor the challenge picker left of Delete so they can never collide.
                if (_chTag != null && _del != null) _chTag.Left = _del.Left - _chTag.Width - UiTheme.S(10);
                _orderHdr.Left = rowW - UiTheme.S(84);
                RecalcHeight();
            }

            private void RecalcHeight()
            {
                int y = 0;
                foreach (Control c in _rows.Controls) { c.Location = new Point(0, y); y += RowH; }
                int rowsH = Math.Max(RowH, _rows.Controls.Count * RowH);
                int newH = BodyPad * 2 + HeaderH + ColHeadH + rowsH + AddH + 2;
                if (Height != newH) { Height = newH; CardResized?.Invoke(this, EventArgs.Empty); }
            }

            private void AddRow(PriorityRow row)
            {
                row.Height = RowH;
                row.Changed += (s, e) => Sync();
                row.RemoveRequested += (s, e) => { _rows.Controls.Remove(row); Restripe(); Sync(); RecalcHeight(); };
                row.MoveRequested += (s, e) =>
                {
                    var list = _rows.Controls.Cast<Control>().ToList();
                    int i = list.IndexOf(row);
                    int j = i + e.Direction;
                    if (j < 0 || j >= list.Count) return;
                    _rows.Controls.SetChildIndex(row, j);
                    Restripe(); Sync();
                };
                _rows.Controls.Add(row);
            }

            private void Restripe()
            {
                for (int i = 0; i < _rows.Controls.Count; i++)
                {
                    var c = _rows.Controls[i];
                    c.Location = new Point(0, i * RowH);
                    c.BackColor = (i % 2 == 0) ? UiTheme.Surface : UiTheme.Zebra;
                    if (c is PriorityRow r) r.ApplyRowColor();
                }
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
                _bp.Priorities.Clear();
                foreach (Control c in _rows.Controls)
                    if (c is PriorityRow r)
                    {
                        var tok = r.BuildToken();
                        if (!string.IsNullOrEmpty(tok)) _bp.Priorities.Add(tok);
                    }
                OnChanged();
            }

            private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
        }

        // ---------------------------------------------------------------- CAP toggle

        private class CapToggle : Panel
        {
            private readonly Label _yes, _no;
            private bool _yes_;
            public event EventHandler Changed;

            public CapToggle()
            {
                Size = new Size(ColCapW, UiTheme.S(24));
                BorderStyle = BorderStyle.FixedSingle;
                _yes = Half("Yes", 0);
                _no = Half("No", ColCapW / 2);
                _yes.Click += (s, e) => Set(true, true);
                _no.Click += (s, e) => Set(false, true);
                Controls.Add(_yes); Controls.Add(_no);
                Restyle();
            }

            private Label Half(string t, int x) => new Label
            {
                Text = t,
                Location = new Point(x, 0),
                Size = new Size(ColCapW / 2 - 1, UiTheme.S(22)),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UiTheme.Ui
            };

            public bool Yes { get => _yes_; set => Set(value, false); }

            private void Set(bool yes, bool raise)
            {
                if (_yes_ == yes && raise) return;
                _yes_ = yes;
                Restyle();
                if (raise) Changed?.Invoke(this, EventArgs.Empty);
            }

            private void Restyle()
            {
                _yes.BackColor = _yes_ ? UiTheme.CapBg : UiTheme.Surface;
                _yes.ForeColor = _yes_ ? UiTheme.Cap : UiTheme.Muted;
                _yes.Font = _yes_ ? UiTheme.Bold : UiTheme.Ui;
                _no.BackColor = !_yes_ ? UiTheme.BtnFace : UiTheme.Surface;
                _no.ForeColor = !_yes_ ? UiTheme.Ink : UiTheme.Muted;
                _no.Font = !_yes_ ? UiTheme.Bold : UiTheme.Ui;
            }
        }

        // ---------------------------------------------------------------- priority row

        public class MoveEventArgs : EventArgs { public int Direction; }

        private class PriorityRow : Panel
        {
            private readonly ResourceKind _kind;

            // structured controls
            private readonly ComboBox _base = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = ColPrioW, Font = UiTheme.Ui };
            private readonly NumericUpDown _index = new NumericUpDown { Minimum = 0, Maximum = 99, Width = ColIdxW, Font = UiTheme.Ui, TextAlign = HorizontalAlignment.Right };
            private readonly CapToggle _cap = new CapToggle();
            private readonly CheckBox _pctEnable = new CheckBox { Width = UiTheme.S(16), Height = UiTheme.S(20) };
            private readonly Label _scope = new Label { AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui };
            private readonly NumericUpDown _pct = new NumericUpDown { Minimum = 0, Maximum = 100, Width = UiTheme.S(44), Enabled = false, Font = UiTheme.Ui, TextAlign = HorizontalAlignment.Right };
            private readonly Label _unit = new Label { Text = "%", AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui };
            private readonly Label _idxDim = new Label { Text = "—", AutoSize = true, ForeColor = UiTheme.Faint, Font = UiTheme.Ui };

            // unsupported (read-only) mode
            private readonly bool _unsupported;
            private readonly string _rawToken;
            private readonly Label _warn = new Label { AutoSize = true, ForeColor = UiTheme.Danger, Font = UiTheme.Ui, Visible = false };

            private readonly Button _up, _down, _rem;
            private bool _loading;

            public event EventHandler Changed;
            public event EventHandler RemoveRequested;
            public event EventHandler<MoveEventArgs> MoveRequested;

            public PriorityRow(string existingToken, ResourceKind kind)
            {
                _kind = kind;
                Height = RowH;

                UiTheme.StyleCombo(_base);
                UiTheme.StyleNum(_index);
                UiTheme.StyleNum(_pct);

                foreach (var bt in PriorityCatalog.For(kind)) _base.Items.Add(bt.Display);

                _up = Icon("↑"); _down = Icon("↓"); _rem = Icon("✕", true);
                _up.Click += (s, e) => MoveRequested?.Invoke(this, new MoveEventArgs { Direction = -1 });
                _down.Click += (s, e) => MoveRequested?.Invoke(this, new MoveEventArgs { Direction = 1 });
                _rem.Click += (s, e) => RemoveRequested?.Invoke(this, EventArgs.Empty);

                Controls.Add(_base); Controls.Add(_index); Controls.Add(_idxDim);
                Controls.Add(_cap); Controls.Add(_pctEnable); Controls.Add(_scope); Controls.Add(_pct); Controls.Add(_unit);
                Controls.Add(_warn); Controls.Add(_up); Controls.Add(_down); Controls.Add(_rem);

                // Decide mode: a token that isn't a valid type for THIS resource becomes read-only (preserved).
                if (!string.IsNullOrEmpty(existingToken))
                {
                    var t = PriorityCatalog.Parse(existingToken);
                    if (!t.Recognized || !PriorityCatalog.IsValidFor(kind, t.Base))
                    {
                        _unsupported = true;
                        _rawToken = existingToken;
                        _warn.Text = $"⚠  {existingToken}  — not a valid {kind} priority; kept as-is";
                    }
                }

                Place();

                if (_unsupported)
                {
                    foreach (Control c in new Control[] { _base, _index, _idxDim, _cap, _pctEnable, _scope, _pct, _unit }) c.Visible = false;
                    _warn.Visible = true;
                }
                else
                {
                    LoadFrom(existingToken);
                    _base.SelectedIndexChanged += (s, e) => { UpdateEnabled(); Raise(); };
                    _cap.Changed += (s, e) => { UpdateEnabled(); Raise(); };
                    _index.ValueChanged += (s, e) => Raise();
                    _pctEnable.CheckedChanged += (s, e) => { UpdateEnabled(); Raise(); };
                    _pct.ValueChanged += (s, e) => Raise();
                    UpdateEnabled();
                }
            }

            public void SetWidth(int w) { Width = w; Place(); }

            public void ApplyRowColor()
            {
                _scope.BackColor = BackColor; _unit.BackColor = BackColor; _idxDim.BackColor = BackColor;
                _pctEnable.BackColor = BackColor; _warn.BackColor = BackColor;
            }

            private void Place()
            {
                _base.Location = new Point(ColPrioX, UiTheme.S(3));
                _index.Location = new Point(ColIdxX, UiTheme.S(4));
                _idxDim.Location = new Point(ColIdxX + UiTheme.S(14), UiTheme.S(6));
                _cap.Location = new Point(ColCapX, UiTheme.S(3));
                _pctEnable.Location = new Point(ColPctX, UiTheme.S(5));
                _scope.Location = new Point(ColScopeX, UiTheme.S(6));
                _pct.Location = new Point(ColNumX, UiTheme.S(4));
                _unit.Location = new Point(ColUnitX, UiTheme.S(6));
                _warn.Location = new Point(ColPrioX, UiTheme.S(7));
                int rx = Width - 3 * UiTheme.S(28) - UiTheme.S(8);
                _up.Location = new Point(rx, UiTheme.S(3)); _down.Location = new Point(rx + UiTheme.S(28), UiTheme.S(3)); _rem.Location = new Point(rx + UiTheme.S(56), UiTheme.S(3));
                ApplyRowColor();
            }

            private void LoadFrom(string token)
            {
                _loading = true;
                if (string.IsNullOrEmpty(token))
                {
                    _base.SelectedIndex = 0;
                    _cap.Yes = false;
                }
                else
                {
                    var t = PriorityCatalog.Parse(token);
                    SelectBase(t.Base);
                    _cap.Yes = t.Cap;
                    var bt = PriorityCatalog.Find(_kind, t.Base);
                    _index.Maximum = (bt != null && bt.HasIndex) ? bt.IndexMax : 99;
                    if (t.Index.HasValue) _index.Value = Math.Min(_index.Maximum, Math.Max(0, t.Index.Value));
                    if (t.Percent.HasValue) { _pctEnable.Checked = true; _pct.Value = Math.Min(100, Math.Max(0, t.Percent.Value)); }
                }
                _loading = false;
            }

            private void SelectBase(string code)
            {
                var bt = PriorityCatalog.Find(_kind, code);
                _base.SelectedItem = bt != null ? bt.Display : (_base.Items.Count > 0 ? _base.Items[0] : null);
            }

            private string SelectedCode()
            {
                var s = _base.SelectedItem as string;
                if (string.IsNullOrEmpty(s)) return null;
                int i = s.IndexOf(" — ", StringComparison.Ordinal);
                return i > 0 ? s.Substring(0, i) : s;
            }

            private void UpdateEnabled()
            {
                var bt = PriorityCatalog.Find(_kind, SelectedCode());
                bool hasIdx = bt != null && bt.HasIndex;
                _index.Visible = hasIdx;
                _idxDim.Visible = !hasIdx;
                if (hasIdx)
                {
                    _index.Maximum = bt.IndexMax;
                    if (_index.Value > _index.Maximum) _index.Value = _index.Maximum;
                }

                _scope.Text = _cap.Yes ? "of total" : "of remaining";
                _pct.Enabled = _pctEnable.Checked;
                _scope.ForeColor = _pctEnable.Checked ? UiTheme.Muted : UiTheme.Faint;
            }

            public string BuildToken()
            {
                if (_unsupported) return _rawToken;
                var code = SelectedCode();
                var bt = PriorityCatalog.Find(_kind, code);
                int? idx = (bt != null && bt.HasIndex) ? (int?)(int)_index.Value : null;
                int? pct = _pctEnable.Checked ? (int?)(int)_pct.Value : null;
                return PriorityCatalog.Build(_cap.Yes, code, idx, pct);
            }

            private void Raise() { if (!_loading) Changed?.Invoke(this, EventArgs.Empty); }

            private static Button Icon(string t, bool danger = false)
            {
                var b = new Button { Text = t, Width = UiTheme.S(26), Height = UiTheme.S(24), Font = UiTheme.Ui };
                UiTheme.StyleIcon(b, danger);
                return b;
            }
        }
    }
}
