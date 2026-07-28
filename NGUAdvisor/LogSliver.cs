using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Round-3 space fill (user-approved): a full-width sliver that tails one of the advisor's
    // log files (combat.log, pitspin.log) so section canvases show their own recent story.
    // Shared-read stream — the writers keep the files open.
    public class LogSliver : Panel
    {
        private readonly string _file;
        private readonly ListBox _list;
        private readonly int _lines;
        private DateTime _lastTick = DateTime.MinValue;
        private string _lastTop;

        public LogSliver(string title, string fileName, int canvasW, int height)
        {
            _file = fileName;
            BackColor = UiTheme.Surface;
            BorderStyle = BorderStyle.FixedSingle;
            Size = new Size(canvasW, height);
            // How many lines to READ must match how many the list can SHOW. The rows are pitched at the
            // measured LinePitch now (UiTheme.StyleList), so dividing by a scaled 20 asked the tail for
            // more lines than fit and the extra were fetched only to be scrolled out of reach.
            _lines = Math.Max(3, (height - UiTheme.S(34)) / UiTheme.LinePitch);

            Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Surface,
                Location = new Point(UiTheme.S(8), UiTheme.S(5))
            });

            _list = new ListBox
            {
                Bounds = new Rectangle(UiTheme.S(8), UiTheme.S(26), canvasW - UiTheme.S(36), height - UiTheme.S(34)),
                Font = UiTheme.Ui,
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Ink,
                SelectionMode = SelectionMode.None
            };
            UiTheme.StyleList(_list);
            Controls.Add(_list);

            VisibleChanged += (s, e) => { if (Visible) Refresh2(); };
        }

        public void TickSliver()
        {
            if (!Visible) return;
            if ((DateTime.UtcNow - _lastTick).TotalSeconds < 5) return;
            _lastTick = DateTime.UtcNow;
            Refresh2();
        }

        private void Refresh2()
        {
            try
            {
                var tail = LogTail.Read(Path.Combine(GetLogDir(), _file), _lines);
                string top = tail.Count > 0 ? tail[0] : null;
                if (top == _lastTop && tail.Count == _list.Items.Count) return;
                _lastTop = top;
                _list.BeginUpdate();
                try
                {
                    _list.Items.Clear();
                    if (tail.Count == 0) _list.Items.Add("(nothing logged yet this session)");
                    else foreach (var l in tail) _list.Items.Add(l);
                }
                finally { _list.EndUpdate(); }
            }
            catch (Exception ex) { LogDebug($"Log sliver {_file}: {ex.Message}"); }
        }

    }
}
