using System;
using System.Drawing;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // COMBAT › RESPAWN ITEMS: how many respawn accessories the auto profile's segment gear keeps
    // (Settings.SegmentRespawnItems; the rule is AdvisorApply.SegmentChainWithRespawn). A view only —
    // the price line reads AdvisorApply.SegmentRespawnStatus, which the gear pass writes.
    public class RespawnItemsBlock : Panel
    {
        private const int MaxItems = 3;
        private const double LineRefreshSeconds = 5;

        private readonly ScaledCheckBox _auto;
        private readonly NumericUpDown _count;
        private readonly Label _line;
        private bool _syncing;
        private DateTime _lineAt = DateTime.MinValue;

        public RespawnItemsBlock(int width)
        {
            BackColor = UiTheme.Ground;
            Width = width;

            var head = new Label { Text = "RESPAWN ITEMS", AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(0, 0) };
            Controls.Add(head);
            UiLayout.HelpMark(this, head, "How many respawn accessories the auto profile's segment gear "
                + "(Time Machine, augments, NGUs, ...) keeps while you fight. Auto takes them only while the set "
                + "loses less than 2 %. None go in with combat off or once respawn is within one attack. "
                + "Titan, gold, quest, Yggdrasil and hunt gear keep their own respawn switches.");
            int y = UiTheme.HeadPitch;

            _auto = new ScaledCheckBox { Text = "Auto", AutoSize = true, ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground };
            _count = new NumericUpDown { Width = UiTheme.S(60), Minimum = 0, Maximum = MaxItems, Font = UiTheme.Ui };
            UiTheme.StyleNum(_count);
            var unit = new Label { Text = "items", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            _auto.CheckedChanged += (s, e) => { if (!_syncing) Write(); };
            _count.ValueChanged += (s, e) => { if (!_syncing) Write(); };
            Controls.Add(_auto);
            Controls.Add(_count);
            Controls.Add(unit);
            y = UiLayout.Row(0, y, UiTheme.S(10), _auto, _count, unit) + UiTheme.S(6);

            _line = new Label { AutoSize = false, Size = new Size(width, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(0, y) };
            Controls.Add(_line);
            Height = y + UiTheme.LinePitch * 2 + UiTheme.S(4);

            SyncFromSettings();
        }

        // Auto keeps the counter's number for when it is switched off again; the counter is only the
        // choice while Auto is off, so it greys out instead of disappearing.
        private void Write()
        {
            if (Settings == null) return;
            Settings.SegmentRespawnItems = _auto.Checked
                ? SavedSettings.SegmentRespawnAuto
                : ((int)_count.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            AdvisorApply.GearRestored();   // re-equip on the next tick, not after the 120 s throttle
            SyncFromSettings();
        }

        public void SyncFromSettings()
        {
            if (Settings == null) return;
            _syncing = true;
            try
            {
                string value = Settings.SegmentRespawnItems;
                bool auto = value == SavedSettings.SegmentRespawnAuto;
                _auto.Checked = auto;
                if (!auto && int.TryParse(value, out int n))
                    _count.Value = Math.Max(0, Math.Min(MaxItems, n));
                _count.Enabled = !auto;
            }
            finally { _syncing = false; }
            RefreshLine();
        }

        public void Tick()
        {
            if (!Visible) return;
            DateTime now = DateTime.UtcNow;
            if ((now - _lineAt).TotalSeconds < LineRefreshSeconds) return;
            _lineAt = now;
            RefreshLine();
        }

        private void RefreshLine()
        {
            string text = Settings == null || !Settings.AutoProfile
                ? "Applies to the auto profile's segment gear — the auto profile is off."
                : string.IsNullOrEmpty(AdvisorApply.SegmentRespawnStatus)
                    ? "Decided on the next gear check."
                    : AdvisorApply.SegmentRespawnStatus;
            UiLayout.FitOrGrow(_line, text, 2);
        }
    }
}
