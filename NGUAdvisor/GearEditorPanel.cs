using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using NGUAdvisor.Managers;

namespace NGUAdvisor
{
    // Editor for the Gear timeline. Each card is a time breakpoint with an ORDERED list of item IDs (the
    // profile's "ID" array, equipped slot-by-slot in order). Item names are shown from the game.
    //
    // Bridge to the external gear-optimizer: "Paste IDs" replaces the list from a copied ID list (any
    // separators), and "Copy IDs" exports the current list. Named backup loadouts in the breakpoint
    // (e.g. "NoTM (...)") are preserved by the model and surfaced as a count.
    //
    // Mono-safe explicit layout + single-content-child scroll, UiTheme styling.
    public class GearEditorPanel : UserControl
    {
        private static readonly int RowH = UiTheme.SNumRow(26);   // holds a NumericUpDown — derive, don't scale
        // Containers DERIVED from what they hold (ui-infra.md): the chip from the NumericUpDown line, the
        // objective band from the two stacked lines inside it, the column strip from the header font.
        private static readonly int ChipH = UiTheme.NumH + UiTheme.S(6);
        private static readonly int HeaderH = ChipH + UiTheme.S(8);
        private static readonly int SourceH = UiTheme.S(40);
        private static readonly int ObjInfoH = UiTheme.SLines(2, 10);
        private static readonly int BarH = UiTheme.SCtl(24) + UiTheme.S(8);
        private static readonly int ColHeadH = UiTheme.SHead(20) + UiTheme.S(4);
        private static readonly int IconW = IconWidth();
        private static readonly int IconPitch = IconW + UiTheme.S(2);

        private static int IconWidth()
        {
            int w = UiTheme.S(26);
            foreach (string glyph in new[] { "↑", "↓", "✕" })
                w = Math.Max(w, UiLayout.MeasureText(glyph, UiTheme.Ui) + UiTheme.S(16));
            return w;
        }
        private static readonly int AddH = UiTheme.SNumRow(30);
        private static readonly int BodyPad = UiTheme.S(10);
        private static readonly int StripW = UiTheme.S(6);
        private static readonly int CardGap = UiTheme.S(10);
        private static readonly int OuterPad = UiTheme.S(8);

        private readonly List<ProfileModel.ListBreakpoint> _data;
        private readonly Color _accent;
        private readonly Panel _scroll;
        private readonly Panel _content;
        private readonly List<Card> _cards = new List<Card>();
        public event EventHandler Changed;

        public GearEditorPanel(List<ProfileModel.ListBreakpoint> data, Color accent)
        {
            _data = data ?? new List<ProfileModel.ListBreakpoint>();
            _accent = accent;
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
            toolbar.Controls.Add(new Label { Text = "Items equip in list order. Paste an ID list from the gear-optimizer into a breakpoint.", AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui, Margin = new Padding(UiTheme.S(10), UiTheme.S(6), 0, 0) });

            Controls.Add(_scroll);
            Controls.Add(toolbar);

            _scroll.ClientSizeChanged += (s, e) => Relayout();
            RebuildCards();
        }

        private int CardWidth => Math.Max(UiTheme.S(460), _scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - OuterPad * 2);

        private void RebuildCards()
        {
            _content.SuspendLayout();
            foreach (var c in _cards) { _content.Controls.Remove(c); c.Dispose(); }
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
            var card = new Card(bp, _accent);
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

        // Parse any pasted text into a list of positive item ids (handles commas, brackets, spaces, newlines).
        // UNCHANGED and deliberately so: it is the authority on what a paste yields. Order, duplicates and
        // acceptance rules must stay exactly as they were — slice 4 changes WHEN it is called, not what it does.
        internal static List<int> ParseIds(string text)
        {
            var ids = new List<int>();
            if (string.IsNullOrEmpty(text)) return ids;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "\\d+"))
                if (int.TryParse(m.Value, out var v) && v > 0) ids.Add(v);
            return ids;
        }

        // What the paste WOULD do, computed before anything is touched. `ids` is whatever ParseIds says —
        // never a second opinion. `rejected` is additive reporting only: the clipboard is split into entries
        // and each is fed back through ParseIds itself, so the two can never drift apart. An entry is
        // "unrecognized" exactly when ParseIds finds nothing usable in it: letters, a bare 0 (the v > 0 rule),
        // or a digit run too long for an int.
        internal static void AnalyzePaste(string text, out List<int> ids, out List<string> rejected)
        {
            ids = ParseIds(text);
            rejected = new List<string>();
            if (string.IsNullOrEmpty(text)) return;
            foreach (var token in System.Text.RegularExpressions.Regex.Split(text, "[\\s,;\\[\\]{}()\"']+"))
            {
                if (token.Length == 0) continue;
                if (ParseIds(token).Count == 0) rejected.Add(token);
            }
        }

        // ---------------------------------------------------------------- card

        private class Card : Panel
        {
            private readonly ProfileModel.ListBreakpoint _bp;
            private readonly Panel _body, _rows, _bar, _colHead, _objPanel;
            private readonly NumericUpDown _h, _m, _s;
            private readonly Label _countLbl, _backupLbl, _orderHdr, _objInfo;
            private readonly ComboBox _source;
            private readonly CheckBox _respawn;
            private readonly Button _addItem;
            private readonly Button _paste, _copy, _undoBtn;
            private Button _del;
            private ComboBox _chTag;
            private bool _loading;
            private int _rowW = UiTheme.S(460);

            // SINGLE-LEVEL UNDO for a confirmed paste. The snapshot is PLAIN DATA — the row ids in order,
            // including any 0s — never Control instances (those get disposed, and a disposed control is not
            // a snapshot).
            //
            // ★ NO TIMER. A System.Windows.Forms.Timer was tried here and PROVABLY NEVER TICKS in the
            // injected host: this window has no WinForms message pump (the game's Unity loop pumps it, so
            // clicks arrive but WM_TIMER never does). The button sat at "(20s)" forever. Time-based UI in
            // these windows must therefore be LAZY — the DEADLINE is the only authority, evaluated when the
            // user actually reaches for the control (MouseEnter) and again on click. Nothing schedules.
            private const int UndoSeconds = 20;
            private const int RejectPreview = 8;     // bounded dialog: show this many, then say how many more
            private const int MaxRowId = 9999;       // Row's NumericUpDown maximum
            private List<int> _undoRows;
            private DateTime _undoDeadline;

            private bool ObjectiveMode => !string.IsNullOrEmpty(_bp.Objective);

            public event EventHandler Changed;
            public event EventHandler DeleteRequested;
            public event EventHandler CardResized;

            public Card(ProfileModel.ListBreakpoint bp, Color accent)
            {
                _bp = bp;
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

                // Gear source band: pick Manual item IDs, or optimize live for an objective. The tinted
                // band + bold label + bordered dropdown make the mode selector read as a distinct control.
                var sourcePanel = new Panel { Dock = DockStyle.Top, Height = SourceH, BackColor = UiTheme.AccentWeak };
                sourcePanel.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border });
                sourcePanel.Controls.Add(new Label { Text = "GEAR SOURCE", Location = new Point(UiTheme.S(2), UiTheme.S(13)), AutoSize = true, ForeColor = UiTheme.Accent, Font = UiTheme.Bold });
                _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = UiTheme.S(260), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat, BackColor = UiTheme.Surface, ForeColor = UiTheme.Ink };
                UiTheme.StyleCombo(_source);   // states the height, so centre AFTER it
                _source.Location = new Point(UiTheme.S(104), (SourceH - _source.Height) / 2);
                _source.Items.Add("Manual (item IDs)");
                foreach (var o in GearObjectives.Objectives) _source.Items.Add("Optimize: " + o.Name);
                sourcePanel.Controls.Add(_source);

                // Objective-mode details: the "keep top respawn" toggle + a live-optimize note.
                _objPanel = new Panel { Dock = DockStyle.Top, Height = ObjInfoH, BackColor = UiTheme.Surface };
                _respawn = new ScaledCheckBox { Text = "Always keep the single best Respawn item", Location = new Point(UiTheme.S(2), UiTheme.S(3)), AutoSize = true, ForeColor = UiTheme.Ink };
                _respawn.CheckedChanged += RespawnChanged;
                // Second of two stacked lines — one LinePitch below the first, and ObjInfoH is derived from
                // both, never scaled next to them.
                _objInfo = new Label { Location = new Point(UiTheme.S(2), UiTheme.S(3) + UiTheme.LinePitch), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui, Text = "Gear is auto-optimized live for this objective while the breakpoint is active." };
                _objPanel.Controls.Add(_respawn);
                _objPanel.Controls.Add(_objInfo);

                // paste/copy/undo bar
                _bar = new Panel { Dock = DockStyle.Top, Height = BarH, BackColor = UiTheme.Surface };
                _paste = new Button { Text = "Paste IDs", Width = UiLayout.BtnWidth("Paste IDs"), Height = UiTheme.SCtl(24), Font = UiTheme.Ui };
                _copy = new Button { Text = "Copy IDs", Width = UiLayout.BtnWidth("Copy IDs"), Height = UiTheme.SCtl(24), Font = UiTheme.Ui };
                UiTheme.StyleFlat(_paste); UiTheme.StyleFlat(_copy);
                _paste.Click += (s, e) => PasteIds();
                _copy.Click += (s, e) => CopyIds();
                // Static caption: without a working timer a live countdown would either lie or need the game
                // loop, and a stale "(14s)" is worse than no number. The WINDOW is stated in the message
                // line instead; the button just says what it does.
                _undoBtn = new Button
                {
                    Text = "Undo paste",
                    Width = UiLayout.BtnWidth("Undo paste") + UiTheme.S(6),
                    Height = UiTheme.SCtl(24),
                    Font = UiTheme.Ui,
                    Visible = false
                };
                UiTheme.StyleGhost(_undoBtn);
                // Reaching for the button is the moment to find out the window has closed — so the dead
                // affordance clears itself before it can be clicked, and the click re-checks anyway.
                _undoBtn.MouseEnter += (s, e) => ExpireUndoIfStale();
                _undoBtn.Click += (s, e) => UndoPaste();
                _bar.Controls.Add(_paste); _bar.Controls.Add(_copy); _bar.Controls.Add(_undoBtn);
                _countLbl = new Label { AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Ui };
                _bar.Controls.Add(_countLbl);
                _backupLbl = new Label { Location = new Point(UiTheme.S(300), UiTheme.S(6)), AutoSize = true, ForeColor = UiTheme.Faint, Font = UiTheme.Ui };
                _bar.Controls.Add(_backupLbl);
                LayoutBar();

                _colHead = new Panel { Dock = DockStyle.Top, Height = ColHeadH, BackColor = UiTheme.Surface };
                // y = S(3), not S(5): a 7.5pt AutoSize label renders HeadH tall, so at S(5) it ran out
                // through the bottom of the column strip.
                _colHead.Controls.Add(new Label { Text = "ITEM ID", Location = new Point(UiTheme.S(6), UiTheme.S(3)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.ColHeader });
                _colHead.Controls.Add(new Label { Text = "NAME", Location = new Point(UiTheme.S(74), UiTheme.S(3)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.ColHeader });
                _orderHdr = new Label { Text = "ORDER", Location = new Point(0, UiTheme.S(3)), AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.ColHeader };
                _colHead.Controls.Add(_orderHdr);
                _colHead.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border });

                _addItem = new Button { Text = "+ Add item", Height = AddH, Dock = DockStyle.Bottom, Font = UiTheme.Ui };
                UiTheme.StyleGhost(_addItem);
                _addItem.Click += (s, e) => { ClearUndo(); AddRow(new Row(0)); Restripe(); Sync(); UpdateInfo(); RecalcHeight(); };

                _rows = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };

                _body.Controls.Add(_rows);
                _body.Controls.Add(_addItem);
                _body.Controls.Add(_colHead);
                _body.Controls.Add(_bar);
                _body.Controls.Add(_objPanel);
                _body.Controls.Add(sourcePanel);
                _body.Controls.Add(header);

                Controls.Add(_body);
                Controls.Add(strip);

                _loading = true;
                _h.Value = Math.Min(_h.Maximum, bp.Hours);
                _m.Value = bp.Minutes;
                _s.Value = bp.Seconds;
                foreach (var id in bp.Items) AddRow(new Row(id));

                // Initialize the gear-source dropdown from the model's objective (0 = Manual). Any
                // objective string that doesn't match a preset is added verbatim so it round-trips.
                int objIdx = 0;
                if (ObjectiveMode)
                {
                    for (int i = 0; i < GearObjectives.Objectives.Count; i++)
                        if (string.Equals(GearObjectives.Objectives[i].Name, _bp.Objective, StringComparison.OrdinalIgnoreCase))
                        { objIdx = i + 1; break; }
                    if (objIdx == 0) { _source.Items.Add("Optimize: " + _bp.Objective); objIdx = _source.Items.Count - 1; }
                }
                _source.SelectedIndex = objIdx;
                _source.SelectedIndexChanged += SourceChanged;
                _respawn.Checked = _bp.ForceRespawn;

                _loading = false;
                Restripe();
                ApplyMode();

                _h.ValueChanged += TimeChanged; _m.ValueChanged += TimeChanged; _s.ValueChanged += TimeChanged;
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
                _rowW = rowW;
                foreach (Control c in _rows.Controls) if (c is Row r) r.SetWidth(rowW);
                int bodyW = w - StripW - 2;
                if (_del != null) _del.Left = bodyW - _del.Width - UiTheme.S(24);
                // Anchor the challenge picker left of Delete so they can never collide.
                if (_chTag != null && _del != null) _chTag.Left = _del.Left - _chTag.Width - UiTheme.S(10);
                _orderHdr.Left = rowW - 3 * IconPitch;   // stays over the icon block when the icons widen
                PlaceBackup();
                RecalcHeight();
            }

            private void RecalcHeight()
            {
                int y = 0;
                foreach (Control c in _rows.Controls) { c.Location = new Point(0, y); y += RowH; }
                int rowsH = Math.Max(RowH, _rows.Controls.Count * RowH);
                int contentH = ObjectiveMode ? ObjInfoH : (BarH + ColHeadH + rowsH + AddH);
                int newH = BodyPad * 2 + HeaderH + SourceH + contentH + 2;
                if (Height != newH) { Height = newH; CardResized?.Invoke(this, EventArgs.Empty); }
            }

            // Toggle the card between Manual (ID rows) and Optimize (live objective) modes.
            private void ApplyMode()
            {
                bool obj = ObjectiveMode;
                _objPanel.Visible = obj;
                _bar.Visible = !obj;
                _colHead.Visible = !obj;
                _rows.Visible = !obj;
                _addItem.Visible = !obj;
                if (obj)
                    _objInfo.Text = "Gear is auto-optimized live for \"" + _bp.Objective + "\" while this breakpoint is active.";
                UpdateInfo();
                RecalcHeight();
            }

            private void SourceChanged(object sender, EventArgs e)
            {
                if (_loading) return;
                // Switching to Optimize takes the item list out of play entirely — the snapshot describes a
                // list that is no longer what this breakpoint means.
                ClearUndo();
                if (_source.SelectedIndex <= 0) _bp.Objective = "";
                else
                {
                    var txt = _source.SelectedItem.ToString();
                    const string pre = "Optimize: ";
                    _bp.Objective = txt.StartsWith(pre) ? txt.Substring(pre.Length) : txt;
                }
                ApplyMode();
                OnChanged();
            }

            private void RespawnChanged(object sender, EventArgs e)
            {
                if (_loading) return;
                _bp.ForceRespawn = _respawn.Checked;
                OnChanged();
            }

            // Every one of these handlers is a MANUAL edit to the list the undo snapshot describes, so each
            // invalidates the token — a stale undo must never restore an old loadout over newer work. (They
            // fire only on user interaction: the ctor sets Row's value before subscribing, and a paste or an
            // undo rebuilds rows with _loading set, so neither trips these.)
            private void AddRow(Row row)
            {
                row.Height = RowH;
                row.Changed += (s, e) => { ClearUndo(); Sync(); UpdateInfo(); };
                row.RemoveRequested += (s, e) => { ClearUndo(); _rows.Controls.Remove(row); row.Dispose(); Restripe(); Sync(); UpdateInfo(); RecalcHeight(); };
                row.MoveRequested += (s, e) =>
                {
                    var list = _rows.Controls.Cast<Control>().ToList();
                    int i = list.IndexOf(row);
                    int j = i + e.Direction;
                    if (j < 0 || j >= list.Count) return;
                    ClearUndo();
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
                    if (c is Row r) r.ApplyRowColor();
                }
            }

            // PARSE -> VALIDATE -> PREVIEW -> CONFIRM -> SNAPSHOT -> REPLACE -> UNDO.
            //
            // The old order cleared the rows and parsed afterwards, so a mis-copied clipboard destroyed a
            // finished loadout for nothing. Nothing is touched now until the replacement is known to be
            // usable AND the user has said yes to the actual consequence.
            private void PasteIds()
            {
                string raw;
                try { raw = Clipboard.GetText(); }
                catch (Exception ex)
                {
                    Main.LogDebug($"Gear paste (clipboard read): {ex.Message}");
                    UpdateInfo("Could not read the clipboard. Nothing changed.");
                    return;
                }

                List<int> ids;
                List<string> rejected;
                AnalyzePaste(raw, out ids, out rejected);

                // Nothing usable: the list stays exactly as it is, and a live undo token stays live —
                // a failed paste is not an edit, so it must not consume the previous paste's undo.
                if (ids.Count == 0)
                {
                    UpdateInfo(rejected.Count > 0
                        ? $"No item IDs found ({rejected.Count} unrecognized). Nothing changed."
                        : "Clipboard had no item IDs. Nothing changed.");
                    return;
                }

                var before = CaptureRowIds();
                if (!ConfirmReplace(before.Count, ids, rejected)) return;   // Cancel: a true no-op, no rebuild

                try
                {
                    ReplaceRows(ids);
                }
                catch (Exception ex)
                {
                    // Partial rebuild is the one outcome we must never present as success.
                    Main.LogDebug($"Gear paste failed mid-rebuild: {ex.Message}");
                    try { ReplaceRows(before); } catch (Exception re) { Main.LogDebug($"Gear rollback failed: {re.Message}"); }
                    ClearUndo();   // a failed replacement never earns an undo token
                    UpdateInfo("Paste failed — the list was left as it was.");
                    return;
                }

                SetUndo(before);
                UpdateInfo($"Pasted {ids.Count} ID(s) — undo for {UndoSeconds}s.");
            }

            // The row ids in order, INCLUDING zeros: _bp.Items drops them (Sync only stores id > 0), so it is
            // not a faithful snapshot of what the user is looking at.
            private List<int> CaptureRowIds()
                => _rows.Controls.Cast<Control>().OfType<Row>().Select(r => r.Id).ToList();

            // The single mutation path — used by paste, rollback and undo alike, so they cannot diverge.
            private void ReplaceRows(List<int> ids)
            {
                UiLayout.DisposeChildren(_rows);   // remove-then-dispose: Controls.Clear() leaks handles here
                _loading = true;
                try
                {
                    foreach (var id in ids) AddRow(new Row(id));
                }
                finally { _loading = false; }
                Restripe();
                Sync();
                RecalcHeight();
            }

            private bool ConfirmReplace(int current, List<int> ids, List<string> rejected)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Replace {current} current item(s) with {ids.Count} parsed item(s)?");
                sb.AppendLine();
                sb.AppendLine($"Current items: {current}");
                sb.AppendLine($"New valid items: {ids.Count}");
                sb.AppendLine($"Unrecognized entries: {rejected.Count}");

                if (rejected.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Unrecognized (will not be pasted):");
                    int show = Math.Min(RejectPreview, rejected.Count);
                    for (int i = 0; i < show; i++) sb.AppendLine("    " + Clip(rejected[i]));
                    if (rejected.Count > show)
                        sb.AppendLine($"    … {rejected.Count - show} more not shown");
                }

                // Pre-existing behaviour, disclosed rather than changed: a Row's NumericUpDown caps at 9999,
                // so a larger id silently becomes 9999 once pasted. Slice 4 does not alter that (it would
                // change post-confirm semantics) — it just stops it being a surprise.
                var clamped = ids.Where(i => i > MaxRowId).ToList();
                if (clamped.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"{clamped.Count} ID(s) above {MaxRowId} will be stored as {MaxRowId}:");
                    sb.AppendLine("    " + string.Join(", ", clamped.Take(RejectPreview).Select(i => i.ToString()).ToArray())
                        + (clamped.Count > RejectPreview ? " …" : ""));
                }

                return MessageBox.Show(sb.ToString(), "Paste IDs", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                    == DialogResult.Yes;
            }

            // A garbage clipboard must not be able to inflate the dialog.
            private static string Clip(string s)
                => s.Length <= 24 ? s : s.Substring(0, 24) + "…";

            // ---- undo ----

            private void SetUndo(List<int> before)
            {
                _undoRows = before;
                _undoDeadline = DateTime.UtcNow.AddSeconds(UndoSeconds);
                _undoBtn.Visible = true;
                LayoutBar();
            }

            // Invalidation. Called from every mutation that makes the snapshot stale (see AddRow, _addItem,
            // SourceChanged) and after the token is consumed or expires.
            private void ClearUndo()
            {
                _undoRows = null;
                if (_undoBtn.Visible) { _undoBtn.Visible = false; LayoutBar(); }
            }

            // Lazy expiry: nothing schedules, so the deadline is evaluated whenever the user comes near the
            // control. Returns true if the token was expired and cleared.
            private bool ExpireUndoIfStale()
            {
                if (_undoRows == null || DateTime.UtcNow <= _undoDeadline) return false;
                ClearUndo();
                UpdateInfo("Undo window closed — the paste stands.");
                return true;
            }

            private void UndoPaste()
            {
                if (_undoRows == null) return;
                // The deadline decides. Belt and braces with MouseEnter: even a click that somehow lands on
                // a stale button cannot resurrect an expired undo.
                if (ExpireUndoIfStale()) return;
                var snapshot = _undoRows;
                ClearUndo();               // consume first: single level, never reusable
                try
                {
                    ReplaceRows(snapshot);
                    UpdateInfo($"Undone — {snapshot.Count} item(s) restored.");
                }
                catch (Exception ex)
                {
                    Main.LogDebug($"Gear undo failed: {ex.Message}");
                    UpdateInfo("Undo failed — see debug.log.");
                }
            }

            // Measured left-to-right placement (overlap impossible by construction), rerun whenever the undo
            // button appears or disappears so the row closes up instead of leaving a hole.
            private void LayoutBar()
            {
                var items = new List<Control> { _paste, _copy };
                if (_undoBtn.Visible) items.Add(_undoBtn);
                items.Add(_countLbl);
                UiLayout.Row(0, UiTheme.S(2), UiTheme.S(8), items.ToArray());
                PlaceBackup();
            }

            // Right-aligned, but never allowed to collide with the count label at a narrow card width.
            private void PlaceBackup()
            {
                int w = string.IsNullOrEmpty(_backupLbl.Text) ? 0 : UiLayout.MeasureText(_backupLbl.Text, UiTheme.Ui);
                _backupLbl.Location = new Point(Math.Max(_countLbl.Right + UiTheme.S(12), _rowW - w - UiTheme.S(4)), UiTheme.S(6));
            }


            private void CopyIds()
            {
                try
                {
                    var ids = _rows.Controls.Cast<Control>().OfType<Row>().Select(r => r.Id).Where(x => x > 0);
                    Clipboard.SetText(string.Join(", ", ids));
                    UpdateInfo("Copied IDs to clipboard.");
                }
                catch (Exception ex) { Main.LogDebug($"Gear copy failed: {ex.Message}"); }
            }

            private void UpdateInfo(string msg = null)
            {
                int count = _rows.Controls.Count;
                _countLbl.Text = msg ?? $"{count} item(s)";
                _backupLbl.Text = _bp.Extras.Count > 0 ? $"+ {_bp.Extras.Count} saved backup loadout(s) preserved" : "";
                LayoutBar();   // the count label's width changes with the message; the row re-measures
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
                _bp.Items.Clear();
                foreach (Control c in _rows.Controls)
                    if (c is Row r && r.Id > 0) _bp.Items.Add(r.Id);
                OnChanged();
            }

            private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
        }

        // ---------------------------------------------------------------- row

        public class MoveEventArgs : EventArgs { public int Direction; }

        private class Row : Panel
        {
            private readonly NumericUpDown _id = new NumericUpDown { Minimum = 0, Maximum = 9999, Width = UiTheme.S(60), Font = UiTheme.Ui, TextAlign = HorizontalAlignment.Right };
            private readonly Label _name = new Label { AutoSize = true, ForeColor = UiTheme.Ink, Font = UiTheme.Ui };
            private readonly Button _up, _down, _rem;

            public event EventHandler Changed;
            public event EventHandler RemoveRequested;
            public event EventHandler<MoveEventArgs> MoveRequested;

            public Row(int id)
            {
                Height = RowH;
                UiTheme.StyleNum(_id);
                _id.Value = Math.Min(_id.Maximum, Math.Max(0, id));
                _up = Icon("↑"); _down = Icon("↓"); _rem = Icon("✕", true);
                _up.Click += (s, e) => MoveRequested?.Invoke(this, new MoveEventArgs { Direction = -1 });
                _down.Click += (s, e) => MoveRequested?.Invoke(this, new MoveEventArgs { Direction = 1 });
                _rem.Click += (s, e) => RemoveRequested?.Invoke(this, EventArgs.Empty);
                Controls.Add(_id); Controls.Add(_name); Controls.Add(_up); Controls.Add(_down); Controls.Add(_rem);
                UpdateName();
                _id.ValueChanged += (s, e) => { UpdateName(); Changed?.Invoke(this, EventArgs.Empty); };
                Place();
            }

            public int Id => (int)_id.Value;

            private void UpdateName() => _name.Text = Main.ItemName((int)_id.Value);

            public void ApplyRowColor() { _name.BackColor = BackColor; }

            public void SetWidth(int w) { Width = w; Place(); }

            // Centred on the row, never a tuned offset — StyleNum states a NumH-tall control and a
            // hardcoded top pushed it out through a row that has no scrollbar to reach it.
            private void Place()
            {
                _id.Location = new Point(UiTheme.S(6), Mid(_id.Height));
                _name.Location = new Point(UiTheme.S(74), Mid(UiTheme.LineH));
                int rx = Width - 3 * IconPitch - UiTheme.S(8);
                int iconY = Mid(_up.Height);
                _up.Location = new Point(rx, iconY);
                _down.Location = new Point(rx + IconPitch, iconY);
                _rem.Location = new Point(rx + 2 * IconPitch, iconY);
                ApplyRowColor();
            }

            private int Mid(int childHeight) => Math.Max(0, (Height - childHeight) / 2);

            private static Button Icon(string t, bool danger = false)
            {
                var b = new Button { Text = t, Width = IconW, Height = UiTheme.SCtl(22), Font = UiTheme.Ui };
                UiTheme.StyleIcon(b, danger);
                return b;
            }
        }
    }
}
