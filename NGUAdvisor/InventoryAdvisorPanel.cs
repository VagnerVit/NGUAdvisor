using System;
using System.Drawing;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Systems > INVENTORY sub-tab (B2 layout): the KEEP/TRASH advisor columns, full height — the
    // legacy boost/cube/blacklist config lives on its own native "Boosts" sub-tab next door.
    //
    // Layout pre-flight (host client ~620 narrow): columns (hostW-30)/2 each at x10 / x20+colW;
    // Refresh x10 w120; caveat = two stacked single-line labels at x140. Width re-fit at Shown via
    // SetHostWidth (ctor-time ClientSize reads are stale under Mono).
    public class InventoryAdvisorPanel : Panel
    {
        private readonly ListBox _keep;
        private readonly ListBox _trash;
        private readonly Label _keepHead;
        private readonly Label _trashHead;
        private bool _computedOnce;

        private int _hostW;
        private Panel _content;

        // Called from SettingsForm.Shown with the FINAL client width.
        public void SetHostWidth(int hostW)
        {
            hostW = Math.Max(UiTheme.S(460), hostW);
            if (hostW == _hostW) return;
            _hostW = hostW;
            int colW = (_hostW - UiTheme.S(20) - UiTheme.S(30)) / 2;   // −20 scrollbar allowance (round-3 rule)
            _keep.Width = colW;
            _trash.Width = colW;
            _trash.Left = UiTheme.S(20) + colW;
            _trashHead.Left = UiTheme.S(20) + colW;
            _content.Width = _hostW - UiTheme.S(20);
            _caveat.Width = _hostW - UiTheme.S(40);
            AutoScrollMinSize = _content.Size;
        }

        private Label _caveat;

        public InventoryAdvisorPanel(int hostW = 640)
        {
            _hostW = Math.Max(UiTheme.S(460), hostW);
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;
            AutoScroll = true;

            var content = new Panel { Location = new Point(0, 0), BackColor = UiTheme.Ground };
            _content = content;
            Controls.Add(content);

            _keepHead = new Label
            {
                Text = "KEEP",
                Location = new Point(UiTheme.S(10), UiTheme.S(6)),
                AutoSize = true,
                Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Cap,
                BackColor = UiTheme.Ground
            };
            content.Controls.Add(_keepHead);
            int colW = (_hostW - UiTheme.S(20) - UiTheme.S(30)) / 2;   // −20 scrollbar allowance (round-3 rule)
            _trashHead = new Label
            {
                Text = "TRASH",
                Location = new Point(UiTheme.S(20) + colW, UiTheme.S(6)),
                AutoSize = true,
                Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Danger,
                BackColor = UiTheme.Ground
            };
            content.Controls.Add(_trashHead);

            const int ListRows = 10;   // the tuned S(240) at the 25px line height
            _keep = new ListBox { Location = new Point(UiTheme.S(10), UiTheme.S(24)), Size = new Size(colW, UiTheme.ListH(ListRows)), Font = UiTheme.Ui, BorderStyle = BorderStyle.FixedSingle, SelectionMode = SelectionMode.None };
            UiTheme.StyleList(_keep);
            content.Controls.Add(_keep);
            _trash = new ListBox { Location = new Point(UiTheme.S(20) + colW, UiTheme.S(24)), Size = new Size(colW, UiTheme.ListH(ListRows)), Font = UiTheme.Ui, BorderStyle = BorderStyle.FixedSingle, SelectionMode = SelectionMode.None };
            UiTheme.StyleList(_trash);
            content.Controls.Add(_trash);

            var refresh = new Button { Text = "Refresh Gear", Location = new Point(UiTheme.S(10), _keep.Bottom + UiTheme.S(8)), Size = new Size(UiTheme.S(120), UiTheme.SCtl(24)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(refresh);
            refresh.Click += (s, e) => Recompute();
            content.Controls.Add(refresh);

            // Caveat lives BELOW the lists at full width and word-wraps (round-3: the two labels
            // beside the refresh button clipped at every column width).
            _caveat = new Label
            {
                AutoSize = false,
                Size = new Size(_hostW - UiTheme.S(40), UiTheme.TextH),
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), refresh.Bottom + UiTheme.S(8))
            };
            UiLayout.FitOrGrow(_caveat,
                "KEEP = wins a slot in some optimizer objective, a saved loadout, or is worn. Duplicates of KEEP items are merge fodder — merge them, don't trash them.", 3);
            content.Controls.Add(_caveat);

            content.Size = new Size(_hostW - UiTheme.S(20), _caveat.Bottom + UiTheme.S(10));
            AutoScrollMinSize = content.Size;

            VisibleChanged += (s, e) =>
            {
                if (Visible && !_computedOnce) Recompute();
            };
        }

        private void Recompute()
        {
            try
            {
                if (Main.Character == null) return;
                _computedOnce = true;
                var v = InventoryAdvisor.Compute();
                _keepHead.Text = $"KEEP ({v.Keep.Count})";
                _trashHead.Text = $"TRASH ({v.Trash.Count})";
                _keep.BeginUpdate();
                _keep.Items.Clear();
                foreach (var kv in v.Keep) _keep.Items.Add($"{CollapseAscended(kv.Value)}  (#{kv.Key})");
                _keep.EndUpdate();
                _trash.BeginUpdate();
                _trash.Items.Clear();
                foreach (var kv in v.Trash) _trash.Items.Add($"{CollapseAscended(kv.Value)}  (#{kv.Key})");
                _trash.EndUpdate();
            }
            catch (Exception ex) { LogDebug($"Inventory advisor: {ex.Message}"); }
        }
    }
}
