using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;

namespace NGUAdvisor
{
    // Combined editor for three systems on one tab:
    //   CONSUMABLES - per-time-breakpoint cards; each has a toggle+amount for every consumable.
    //   REBIRTH     - one toggle per rebirth type (Time/Number/Muffin/Bosses) with its field(s).
    //   CHALLENGES  - one flat ordered list of challenge (type + index) rows.
    // Each writes to its own JSON structure. Mono-safe explicit layout + single-content-child scroll.
    public class MiscEditorPanel : UserControl
    {
        private static readonly int OuterPad = UiTheme.S(8);
        private static readonly int SectionGap = UiTheme.S(18);
        private static readonly int HeaderH = UiTheme.S(24);

        private readonly ProfileModel _model;
        private readonly Panel _scroll, _content;
        public event EventHandler Changed;

        private readonly List<ConsumCard> _consCards = new List<ConsumCard>();
        private Label _consHead; private Button _consAdd;
        private RebirthBlock _rebirth;
        private Label _rbHead;
        private ChallengesBlock _challenges;
        private Label _chHead;

        public MiscEditorPanel(ProfileModel model)
        {
            _model = model;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Ground };
            _content = new Panel { Location = new Point(0, 0), BackColor = UiTheme.Ground };
            _scroll.Controls.Add(_content);
            Controls.Add(_scroll);

            _consHead = Head("CONSUMABLES", UiTheme.Energy);
            _consAdd = Add(); _consAdd.Click += (s, e) => AddConsumable();
            _content.Controls.Add(_consHead); _content.Controls.Add(_consAdd);
            foreach (var bp in _model.Consumables) AddConsumCard(bp);

            _rbHead = Head("REBIRTH", UiTheme.NGUDiff);
            _rebirth = new RebirthBlock(_model);
            _rebirth.Changed += (s, e) => OnChanged();
            _content.Controls.Add(_rbHead); _content.Controls.Add(_rebirth);

            _chHead = Head("CHALLENGES", UiTheme.Magic);
            _challenges = new ChallengesBlock(_model.Challenges);
            _challenges.Changed += (s, e) => OnChanged();
            _content.Controls.Add(_chHead); _content.Controls.Add(_challenges);

            _scroll.ClientSizeChanged += (s, e) => Relayout();
            Relayout();
        }

        private int Width2 => Math.Max(UiTheme.S(440), _scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - OuterPad * 2);

        private Label Head(string t, Color c) => new Label { Text = t, AutoSize = true, ForeColor = c, Font = UiTheme.Bold };
        private Button Add() { var b = new Button { Text = "+ Add time breakpoint", Height = UiTheme.S(26), Width = UiTheme.S(170), Font = UiTheme.Ui }; UiTheme.StyleFlat(b); return b; }

        private void AddConsumCard(ProfileModel.StringListBreakpoint bp)
        {
            var card = new ConsumCard(bp);
            card.Changed += (s, e) => OnChanged();
            card.DeleteRequested += (s, e) => { _model.Consumables.Remove(bp); _consCards.Remove(card); _content.Controls.Remove(card); Relayout(); OnChanged(); };
            _consCards.Add(card);
            _content.Controls.Add(card);
        }

        private void AddConsumable()
        {
            var bp = new ProfileModel.StringListBreakpoint { TimeSeconds = 0 };
            _model.Consumables.Add(bp);
            AddConsumCard(bp);
            Relayout();
            OnChanged();
        }

        private bool _laying;
        private void Relayout()
        {
            if (_laying) return;   // prevent re-entry from child Resized events (was an infinite loop)
            _laying = true;
            try { RelayoutCore(); }
            finally { _laying = false; }
        }

        private void RelayoutCore()
        {
            int w = Width2;
            int y = OuterPad;

            _consHead.Location = new Point(OuterPad + UiTheme.S(2), y); y += HeaderH;
            foreach (var c in _consCards) { c.SetWidth(w); c.Location = new Point(OuterPad, y); y += c.Height + UiTheme.S(8); }
            _consAdd.Location = new Point(OuterPad, y); y += _consAdd.Height + SectionGap;

            _rbHead.Location = new Point(OuterPad + UiTheme.S(2), y); y += HeaderH;
            _rebirth.SetWidth(w); _rebirth.Location = new Point(OuterPad, y); y += _rebirth.Height + SectionGap;

            _chHead.Location = new Point(OuterPad + UiTheme.S(2), y); y += HeaderH;
            _challenges.SetWidth(w); _challenges.Location = new Point(OuterPad, y); y += _challenges.Height + SectionGap;

            _content.Size = new Size(w + OuterPad * 2, y + OuterPad);
            _scroll.AutoScrollMinSize = _content.Size;
        }

        private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

        // shared time chip helpers
        internal static Label Sep(string t, int x) => new Label { Text = t, Location = new Point(x, UiTheme.S(9)), AutoSize = true, ForeColor = UiTheme.Faint, Font = UiTheme.Ui };
        internal static NumericUpDown Nud(int x, int max)
        {
            NumericUpDown n = new NumericUpDown { Minimum = 0, Maximum = max, Width = UiTheme.S(46), Location = new Point(x, UiTheme.S(4)), Font = UiTheme.Ui, TextAlign = HorizontalAlignment.Right };
            UiTheme.StyleNum(n);
            return n;
        }

        internal static Panel TimeChip(NumericUpDown h, NumericUpDown m, NumericUpDown s)
        {
            var chip = new Panel { Location = new Point(0, UiTheme.S(4)), Size = new Size(UiTheme.S(238), UiTheme.S(30)), BackColor = UiTheme.AccentWeak, BorderStyle = BorderStyle.FixedSingle };
            chip.Controls.Add(new Label { Text = "TIME", Location = new Point(UiTheme.S(8), UiTheme.S(9)), AutoSize = true, ForeColor = UiTheme.Accent, Font = UiTheme.Chip });
            chip.Controls.Add(Sep("h", UiTheme.S(90))); chip.Controls.Add(Sep("m", UiTheme.S(154))); chip.Controls.Add(Sep("s", UiTheme.S(218)));
            chip.Controls.Add(h); chip.Controls.Add(m); chip.Controls.Add(s);
            return chip;
        }

        // ============================================================ CONSUMABLES card

        private class ConsumCard : Panel
        {
            private readonly ProfileModel.StringListBreakpoint _bp;
            private readonly NumericUpDown _h, _m, _s;
            private readonly List<CheckBox> _checks = new List<CheckBox>();
            private readonly List<NumericUpDown> _amts = new List<NumericUpDown>();
            private readonly List<string> _codes = new List<string>();
            private Button _del;
            private bool _loading;

            public event EventHandler Changed;
            public event EventHandler DeleteRequested;

            public ConsumCard(ProfileModel.StringListBreakpoint bp)
            {
                _bp = bp;
                BorderStyle = BorderStyle.FixedSingle;
                BackColor = UiTheme.Surface;

                var strip = new Panel { Dock = DockStyle.Left, Width = UiTheme.S(6), BackColor = UiTheme.Energy };
                var body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(UiTheme.S(12), UiTheme.S(10), UiTheme.S(12), UiTheme.S(10)) };

                var header = new Panel { Dock = DockStyle.Top, Height = UiTheme.S(40), BackColor = UiTheme.Surface };
                _h = Nud(UiTheme.S(42), 9999); _m = Nud(UiTheme.S(106), 59); _s = Nud(UiTheme.S(170), 59);
                header.Controls.Add(TimeChip(_h, _m, _s));
                header.Controls.Add(new Label { Text = "of the rebirth", Location = new Point(UiTheme.S(250), UiTheme.S(11)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui });
                _del = new Button { Text = "🗑  Delete breakpoint", Width = UiTheme.S(150), Height = UiTheme.S(26), Top = UiTheme.S(4), Font = UiTheme.Ui };
                UiTheme.StyleFlat(_del); _del.ForeColor = UiTheme.Danger;
                _del.Click += (s, e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
                header.Controls.Add(_del);

                var grid = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
                // parse existing items into code->amount
                var have = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var it in _bp.Items)
                {
                    var parts = it.Split(':');
                    var code = parts[0].Trim();
                    int amt = (parts.Length > 1 && int.TryParse(parts[1], out var a)) ? a : 1;
                    have[code] = amt;
                }

                int col = 0, rowY = 0; int colW = UiTheme.S(210), rowH = UiTheme.SNumRow(26);
                foreach (var kv in SystemCatalog.Consumables)
                {
                    int x = UiTheme.S(6) + col * colW;
                    var chk = new CheckBox { Text = $"{kv.Value}", AutoSize = true, Location = new Point(x, rowY + UiTheme.S(4)), Font = UiTheme.Ui, BackColor = UiTheme.Surface };
                    var amt = new NumericUpDown { Minimum = 1, Maximum = 9999, Width = UiTheme.S(50), Location = new Point(x + UiTheme.S(150), rowY + UiTheme.S(2)), Font = UiTheme.Ui, Enabled = false, TextAlign = HorizontalAlignment.Right };
                    UiTheme.StyleNum(amt);
                    if (have.TryGetValue(kv.Key, out var a2)) { chk.Checked = true; amt.Value = Math.Min(9999, Math.Max(1, a2)); amt.Enabled = true; }
                    int idx = _codes.Count;
                    chk.CheckedChanged += (s, e) => { amt.Enabled = chk.Checked; if (!_loading) Sync(); };
                    amt.ValueChanged += (s, e) => { if (!_loading) Sync(); };
                    _checks.Add(chk); _amts.Add(amt); _codes.Add(kv.Key);
                    grid.Controls.Add(chk); grid.Controls.Add(amt);
                    col++;
                    if (col >= 3) { col = 0; rowY += rowH; }
                }
                _gridRows = (SystemCatalog.Consumables.Count + 2) / 3;

                body.Controls.Add(grid);
                body.Controls.Add(header);
                Controls.Add(body); Controls.Add(strip);

                _loading = true;
                _h.Value = Math.Min(_h.Maximum, bp.Hours); _m.Value = bp.Minutes; _s.Value = bp.Seconds;
                _loading = false;

                Height = UiTheme.S(20) + UiTheme.S(40) + _gridRows * rowH + UiTheme.S(6);
                _h.ValueChanged += TimeChanged; _m.ValueChanged += TimeChanged; _s.ValueChanged += TimeChanged;
            }

            private int _gridRows;

            public void SetWidth(int w) { Width = w; if (_del != null) _del.Left = w - UiTheme.S(6) - _del.Width - UiTheme.S(26); }

            private void TimeChanged(object sender, EventArgs e)
            {
                if (_loading) return;
                _bp.TimeSeconds = (int)(_h.Value * 3600 + _m.Value * 60 + _s.Value);
                Changed?.Invoke(this, EventArgs.Empty);
            }

            private void Sync()
            {
                _bp.Items.Clear();
                for (int i = 0; i < _codes.Count; i++)
                    if (_checks[i].Checked)
                    {
                        int amt = (int)_amts[i].Value;
                        _bp.Items.Add(amt > 1 ? $"{_codes[i]}:{amt}" : _codes[i]);
                    }
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        // ============================================================ REBIRTH block (toggle per type)

        private class RebirthBlock : Panel
        {
            private readonly ProfileModel _model;
            private readonly List<TypeRow> _rows = new List<TypeRow>();
            private readonly List<ProfileModel.RebirthEntry> _preserved = new List<ProfileModel.RebirthEntry>();
            private readonly Label _note;
            public event EventHandler Changed;

            public RebirthBlock(ProfileModel model)
            {
                _model = model;
                BackColor = UiTheme.Surface;
                BorderStyle = BorderStyle.FixedSingle;

                // map first entry of each known type to a toggle; preserve the rest
                var byType = new Dictionary<string, ProfileModel.RebirthEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in model.Rebirth)
                {
                    if (!byType.ContainsKey(e.Type) && SystemCatalog.RebirthTypes.Any(t => string.Equals(t.Key, e.Type, StringComparison.OrdinalIgnoreCase)))
                        byType[e.Type] = e;
                    else
                        _preserved.Add(e);
                }

                int y = UiTheme.S(8);
                foreach (var t in SystemCatalog.RebirthTypes)
                {
                    byType.TryGetValue(t.Key, out var existing);
                    var row = new TypeRow(t.Key, t.Value, existing);
                    row.Changed += (s, e) => Push();
                    row.Location = new Point(UiTheme.S(8), y);
                    _rows.Add(row);
                    Controls.Add(row);
                    y += TypeRow.RowHeight;
                }
                _note = new Label { Location = new Point(UiTheme.S(8), y + UiTheme.S(2)), AutoSize = true, ForeColor = UiTheme.Faint, Font = UiTheme.Ui };
                Controls.Add(_note);
                UpdateNote();
                Height = y + UiTheme.S(24);
            }

            public void SetWidth(int w) { Width = w; foreach (var r in _rows) r.SetWidth(w - UiTheme.S(18)); }

            private void UpdateNote() => _note.Text = _preserved.Count > 0 ? $"+ {_preserved.Count} extra rebirth entr(ies) preserved (not shown)" : "";

            private void Push()
            {
                _model.Rebirth.Clear();
                foreach (var r in _rows) { var e = r.ToEntry(); if (e != null) _model.Rebirth.Add(e); }
                _model.Rebirth.AddRange(_preserved);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private class TypeRow : Panel
        {
            public static readonly int RowHeight = UiTheme.SNumRow(30);   // holds NumericUpDowns
            private readonly string _type;
            private readonly CheckBox _on;
            private readonly NumericUpDown _h, _m, _s;
            private readonly Label _targetLbl;
            private readonly TextBox _target;
            private readonly bool _takesTarget;
            public event EventHandler Changed;

            public TypeRow(string type, string label, ProfileModel.RebirthEntry existing)
            {
                _type = type;
                _takesTarget = SystemCatalog.TypeTakesTarget(type);
                Height = RowHeight; BackColor = UiTheme.Surface;

                _on = new CheckBox { Text = label, AutoSize = true, Location = new Point(0, UiTheme.S(6)), Font = UiTheme.Ui, BackColor = UiTheme.Surface };
                _h = MakeNud(UiTheme.S(240), 999); _m = MakeNud(UiTheme.S(300), 59); _s = MakeNud(UiTheme.S(360), 59);
                Controls.Add(_on);
                Controls.Add(new Label { Text = "at", Location = new Point(UiTheme.S(222), UiTheme.S(8)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui });
                Controls.Add(_h); Controls.Add(Lbl("h", UiTheme.S(286))); Controls.Add(_m); Controls.Add(Lbl("m", UiTheme.S(346))); Controls.Add(_s); Controls.Add(Lbl("s", UiTheme.S(406)));
                _targetLbl = new Label { Text = "target", Location = new Point(UiTheme.S(430), UiTheme.S(8)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui, Visible = _takesTarget };
                _target = new TextBox { Location = new Point(UiTheme.S(475), UiTheme.S(5)), Width = UiTheme.S(120), Font = UiTheme.Ui, Visible = _takesTarget };
                Controls.Add(_targetLbl); Controls.Add(_target);

                if (existing != null)
                {
                    _on.Checked = true;
                    _h.Value = Math.Min(999, existing.Hours); _m.Value = existing.Minutes; _s.Value = existing.Seconds;
                    if (existing.Target.HasValue) _target.Text = existing.Target.Value.ToString("R", CultureInfo.InvariantCulture);
                }
                UpdateEnabled();

                _on.CheckedChanged += (s, e) => { UpdateEnabled(); Changed?.Invoke(this, EventArgs.Empty); };
                _h.ValueChanged += (s, e) => Changed?.Invoke(this, EventArgs.Empty);
                _m.ValueChanged += (s, e) => Changed?.Invoke(this, EventArgs.Empty);
                _s.ValueChanged += (s, e) => Changed?.Invoke(this, EventArgs.Empty);
                _target.TextChanged += (s, e) => Changed?.Invoke(this, EventArgs.Empty);
            }

            private NumericUpDown MakeNud(int x, int max)
            {
                NumericUpDown n = new NumericUpDown { Minimum = 0, Maximum = max, Width = UiTheme.S(44), Location = new Point(x, UiTheme.S(4)), Font = UiTheme.Ui, TextAlign = HorizontalAlignment.Right };
                UiTheme.StyleNum(n);
                return n;
            }
            private Label Lbl(string t, int x) => new Label { Text = t, Location = new Point(x, UiTheme.S(8)), AutoSize = true, ForeColor = UiTheme.Faint, Font = UiTheme.Ui };

            public void SetWidth(int w) { Width = w; }

            private void UpdateEnabled()
            {
                bool on = _on.Checked;
                _h.Enabled = _m.Enabled = _s.Enabled = on;
                _target.Enabled = on && _takesTarget;
            }

            public ProfileModel.RebirthEntry ToEntry()
            {
                if (!_on.Checked) return null;
                var e = new ProfileModel.RebirthEntry { Type = _type, TimeSeconds = (int)(_h.Value * 3600 + _m.Value * 60 + _s.Value) };
                if (_takesTarget && double.TryParse(_target.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) e.Target = t;
                return e;
            }
        }

        // ============================================================ CHALLENGES block (one range row per type)

        private class ChallengesBlock : Panel
        {
            private readonly List<string> _data;
            private readonly List<ChRow> _rows = new List<ChRow>();
            public event EventHandler Changed;

            public ChallengesBlock(List<string> data)
            {
                _data = data;
                BackColor = UiTheme.Surface;
                BorderStyle = BorderStyle.FixedSingle;

                // parse existing entries into per-code min/max, tracking first-appearance order
                var min = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var max = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var order = new List<string>();
                foreach (var c in _data)
                {
                    var parts = c.Split('-');
                    var code = parts[0].ToUpperInvariant();
                    int idx = (parts.Length > 1 && int.TryParse(parts[1], out var v)) ? v : 0;
                    if (!min.ContainsKey(code)) { min[code] = idx; max[code] = idx; order.Add(code); }
                    else { if (idx < min[code]) min[code] = idx; if (idx > max[code]) max[code] = idx; }
                }

                // present codes first (in original order), then the rest (catalog order)
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var ordered = new List<SystemCatalog.ChallengeInfo>();
                foreach (var code in order)
                {
                    var info = SystemCatalog.Challenges.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
                    if (info != null && seen.Add(info.Code)) ordered.Add(info);
                }
                foreach (var info in SystemCatalog.Challenges)
                    if (seen.Add(info.Code)) ordered.Add(info);

                int y = UiTheme.S(6);
                foreach (var info in ordered)
                {
                    int from = min.TryGetValue(info.Code, out var mn) ? mn : 0;
                    int to = max.TryGetValue(info.Code, out var mx) ? mx : 0;
                    var row = new ChRow(info, from, to) { Location = new Point(UiTheme.S(8), y) };
                    row.Changed += (s, e) => Sync();
                    _rows.Add(row);
                    Controls.Add(row);
                    y += UiTheme.S(26);
                }
                Height = y + UiTheme.S(8);
            }

            public void SetWidth(int w) { Width = w; foreach (var r in _rows) r.Width = w - UiTheme.S(18); }

            private void Sync()
            {
                _data.Clear();
                foreach (var r in _rows) r.Emit(_data);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        // One challenge type: label + a [from]-to-[to] range (0..cap). Emits CODE-n for n in max(1,from)..to.
        private class ChRow : Panel
        {
            private readonly string _code;
            private readonly NumericUpDown _from, _to;
            public event EventHandler Changed;

            public ChRow(SystemCatalog.ChallengeInfo info, int from, int to)
            {
                _code = info.Code;
                Height = UiTheme.S(26); BackColor = UiTheme.Surface;

                Controls.Add(new Label { Text = $"{info.Code} — {info.Label}", Location = new Point(0, UiTheme.S(5)), Size = new Size(UiTheme.S(200), UiTheme.S(20)), Font = UiTheme.Ui, ForeColor = UiTheme.Ink });
                Controls.Add(new Label { Text = "count", Location = new Point(UiTheme.S(208), UiTheme.S(5)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui });
                _from = MakeNud(UiTheme.S(252), info.Cap, from);
                Controls.Add(_from);
                Controls.Add(new Label { Text = "to", Location = new Point(UiTheme.S(302), UiTheme.S(5)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui });
                _to = MakeNud(UiTheme.S(324), info.Cap, to);
                Controls.Add(_to);
                Controls.Add(new Label { Text = $"(max {info.Cap})", Location = new Point(UiTheme.S(378), UiTheme.S(5)), AutoSize = true, ForeColor = UiTheme.Faint, Font = UiTheme.Ui });

                _from.ValueChanged += (s, e) => Changed?.Invoke(this, EventArgs.Empty);
                _to.ValueChanged += (s, e) => Changed?.Invoke(this, EventArgs.Empty);
            }

            private NumericUpDown MakeNud(int x, int cap, int val)
            {
                var n = new NumericUpDown { Minimum = 0, Maximum = cap, Width = UiTheme.S(46), Location = new Point(x, UiTheme.S(2)), Font = UiTheme.Ui, TextAlign = HorizontalAlignment.Right };
                UiTheme.StyleNum(n);
                n.Value = Math.Min(cap, Math.Max(0, val));
                return n;
            }

            // Active when "to" >= 1; emit CODE-n for n from max(1,from) to to.
            public void Emit(List<string> data)
            {
                int to = (int)_to.Value;
                if (to < 1) return;
                int start = Math.Max(1, (int)_from.Value);
                for (int n = start; n <= to; n++)
                    data.Add($"{_code}-{n}");
            }
        }
    }
}
