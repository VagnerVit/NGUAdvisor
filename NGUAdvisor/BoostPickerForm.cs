using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;

namespace NGUAdvisor
{
    // "Add from inventory" picker for the boost priority list (spec 2026-07-28 §5). Replaces typing raw
    // item IDs. Modal, owned by SettingsForm; every dimension goes through the UiTheme DPI helpers.
    public sealed class BoostPickerForm : Form
    {
        private sealed class Row
        {
            public int Id;
            public string Name;
            public int Level;
            public string Where;      // equipped / inventory / daycare
            public float NeedTotal;
            public string NeedText;   // "P 12 · T 8", or "—"
            public bool AlreadyListed;
            public int Usage;         // objective-optimal loadouts containing it (0 when unknown)
            public bool Equipped;
        }

        private readonly List<Row> _all = new List<Row>();
        private readonly ListBox _list;
        private readonly TextBox _search;
        private readonly ScaledCheckBox _needsOnly;
        private readonly Label _count;
        private List<Row> _shown = new List<Row>();
        private int[] _result = new int[0];

        public static int[] Pick(IWin32Window owner, int[] alreadyInList)
        {
            try
            {
                using (BoostPickerForm f = new BoostPickerForm(alreadyInList))
                    return f.ShowDialog(owner) == DialogResult.OK ? f._result : new int[0];
            }
            catch (Exception e)
            {
                Main.LogDebug($"Boost picker failed: {e}");
                Activity.Failed("Couldn't open the item picker", e.Message, true);
                return new int[0];
            }
        }

        private BoostPickerForm(int[] alreadyInList)
        {
            Text = "Add items to priority boosts";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = UiTheme.Ground;
            // Height is a placeholder until every control is laid out below (see the final ClientSize
            // assignment) — only the width matters here, for the list and button positions.
            ClientSize = new Size(UiTheme.S(520), UiTheme.SCtl(24));

            BuildRows(alreadyInList ?? new int[0]);

            _search = new TextBox
            {
                Location = new Point(UiTheme.S(10), UiTheme.S(10)),
                Width = UiTheme.S(240),
                Height = UiTheme.SCtl(24),
                Font = UiTheme.Ui
            };
            _search.TextChanged += (s, e) => { try { Refill(); } catch (Exception ex) { Main.LogDebug($"Picker search: {ex.Message}"); } };
            Controls.Add(_search);

            _needsOnly = new ScaledCheckBox
            {
                Text = "Needs boosts only",
                Checked = true,
                Location = new Point(_search.Right + UiTheme.S(12), UiTheme.S(10)),
                AutoSize = true,
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Ink,
                BackColor = UiTheme.Ground
            };
            _needsOnly.CheckedChanged += (s, e) => { try { Refill(); } catch (Exception ex) { Main.LogDebug($"Picker filter: {ex.Message}"); } };
            Controls.Add(_needsOnly);

            int listTop = Math.Max(_search.Bottom, _needsOnly.Bottom) + UiTheme.S(8);
            _list = new ListBox
            {
                Location = new Point(UiTheme.S(10), listTop),
                Size = new Size(ClientSize.Width - UiTheme.S(20), UiTheme.ListH(12)),
                Font = UiTheme.Ui,
                BorderStyle = BorderStyle.FixedSingle,
                SelectionMode = SelectionMode.MultiExtended
            };
            UiTheme.StyleList(_list);
            // Spec: rows already in the priority list render greyed, not just text-suffixed. Attached
            // AFTER StyleList so it runs after UiTheme's own DrawItem handler (multicast delegates fire in
            // subscription order) and repaints only the already-listed rows on top, reusing UiTheme's
            // palette rather than duplicating its row layout.
            _list.DrawItem += (s, e) =>
            {
                if (e.Index < 0 || e.Index >= _shown.Count || !_shown[e.Index].AlreadyListed) return;
                using (SolidBrush bg = new SolidBrush(UiTheme.Zebra))
                    e.Graphics.FillRectangle(bg, e.Bounds);
                TextRenderer.DrawText(e.Graphics, _list.GetItemText(_list.Items[e.Index]), _list.Font, e.Bounds,
                    UiTheme.Faint, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            _list.SelectedIndexChanged += (s, e) => { try { DropListedFromSelection(); UpdateCount(); } catch (Exception ex) { Main.LogDebug($"Picker select: {ex.Message}"); } };
            Controls.Add(_list);

            _count = new Label
            {
                Location = new Point(UiTheme.S(10), _list.Bottom + UiTheme.S(6)),
                AutoSize = true,
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground,
                Text = "0 selected"
            };
            Controls.Add(_count);

            Button cancel = MkButton("Cancel", () => { DialogResult = DialogResult.Cancel; Close(); });
            Button add = MkButton("Add selected", () =>
            {
                _result = SelectedRows().Select(r => r.Id).ToArray();
                DialogResult = DialogResult.OK;
                Close();
            });
            cancel.Location = new Point(ClientSize.Width - UiTheme.S(10) - cancel.Width, _list.Bottom + UiTheme.S(6));
            add.Location = new Point(cancel.Left - UiTheme.S(8) - add.Width, cancel.Top);
            Controls.Add(cancel);
            Controls.Add(add);

            AcceptButton = add;
            CancelButton = cancel;
            ClientSize = new Size(ClientSize.Width, cancel.Bottom + UiTheme.S(10));

            Refill();
        }

        private Button MkButton(string text, Action onClick)
        {
            Button b = new Button
            {
                Text = text,
                Font = UiTheme.Ui,
                Size = new Size(UiLayout.MeasureText(text, UiTheme.Ui) + UiTheme.S(24), UiTheme.SCtl(24))
            };
            UiTheme.StyleFlat(b);
            b.Click += (s, e) => { try { onClick(); } catch (Exception ex) { Main.LogDebug($"Picker button: {ex.Message}"); } };
            return b;
        }

        // Every owned equipment id, once, with the data the columns show.
        private void BuildRows(int[] alreadyInList)
        {
            HashSet<int> listed = new HashSet<int>(alreadyInList);
            Dictionary<int, int> usage = InventoryAdvisor.Last?.Usage;   // cached only — never start the optimizer sweep here
            HashSet<int> seen = new HashSet<int>();
            Character c = Main.Character;
            if (c == null) return;
            Inventory inv = c.inventory;
            if (inv == null) return;

            void Consider(Equipment e, string where, bool equipped)
            {
                if (e == null || e.id == 0 || !e.isEquipment()) return;
                if (!seen.Add(e.id)) return;
                BoostsNeeded need = e.GetNeededBoosts();
                _all.Add(new Row
                {
                    Id = e.id,
                    Name = Main.ItemNameNice(e.id),
                    Level = e.level,
                    Where = where,
                    NeedTotal = need.Total(),
                    NeedText = FormatNeed(need),
                    AlreadyListed = listed.Contains(e.id),
                    Usage = usage != null && usage.TryGetValue(e.id, out int n) ? n : 0,
                    Equipped = equipped
                });
            }

            Consider(inv.weapon, "equipped", true);
            try { if (Main.InventoryController.weapon2Unlocked()) Consider(inv.weapon2, "equipped", true); }
            catch (Exception ex) { Main.LogDebug($"Picker weapon2: {ex.Message}"); }
            Consider(inv.head, "equipped", true);
            Consider(inv.chest, "equipped", true);
            Consider(inv.legs, "equipped", true);
            Consider(inv.boots, "equipped", true);
            if (inv.accs != null) foreach (Equipment a in inv.accs) Consider(a, "equipped", true);
            if (inv.inventory != null) foreach (Equipment e in inv.inventory) Consider(e, "inventory", false);
            if (inv.daycare != null) foreach (Equipment e in inv.daycare) Consider(e, "daycare", false);

            _all.Sort((a, b) =>
            {
                if (a.Equipped != b.Equipped) return a.Equipped ? -1 : 1;
                if (a.Usage != b.Usage) return b.Usage.CompareTo(a.Usage);
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static string FormatNeed(BoostsNeeded n)
        {
            List<string> parts = new List<string>();
            if (n.power > 0) parts.Add($"P {n.power:0}");
            if (n.toughness > 0) parts.Add($"T {n.toughness:0}");
            if (n.special > 0) parts.Add($"S {n.special:0}");
            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }

        private void Refill()
        {
            string q = _search.Text.Trim();
            bool needsOnly = _needsOnly.Checked;
            _shown = _all.Where(r =>
            {
                if (needsOnly && r.NeedTotal <= 0) return false;
                if (q.Length == 0) return true;
                if (r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return r.Id.ToString().IndexOf(q.TrimStart('#'), StringComparison.Ordinal) >= 0;
            }).ToList();

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (Row r in _shown)
                _list.Items.Add(r.AlreadyListed
                    ? $"{r.Name}  (#{r.Id})   —   already in list"
                    : $"{r.Name}  (#{r.Id})   ·   lvl {r.Level}/100   ·   {r.NeedText}   ·   {r.Where}");
            _list.EndUpdate();
            UpdateCount();
        }

        // Rows already in the list cannot be added twice: deselect them the moment they get selected.
        // SetSelected raises SelectedIndexChanged synchronously, re-entering this method while
        // SelectedIndices is shrinking underneath the outer loop — guard against re-entry and snapshot
        // the indices up front so both loops see a stable collection.
        private bool _dropping;

        private void DropListedFromSelection()
        {
            if (_dropping) return;
            _dropping = true;
            try
            {
                List<int> selected = new List<int>(_list.SelectedIndices.Cast<int>());
                for (int i = selected.Count - 1; i >= 0; i--)
                {
                    int idx = selected[i];
                    if (idx >= 0 && idx < _shown.Count && _shown[idx].AlreadyListed)
                        _list.SetSelected(idx, false);
                }
            }
            finally { _dropping = false; }
        }

        private IEnumerable<Row> SelectedRows()
        {
            foreach (int idx in _list.SelectedIndices)
                if (idx >= 0 && idx < _shown.Count && !_shown[idx].AlreadyListed)
                    yield return _shown[idx];
        }

        private void UpdateCount() => _count.Text = $"{SelectedRows().Count()} selected";
    }
}
