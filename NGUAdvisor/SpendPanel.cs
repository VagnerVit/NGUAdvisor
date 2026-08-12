using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Economy > NEXT BUY — one row per spendable currency: what it is saving for, what that costs, and
    // WHICH MODULE decided. The AP/PP detail panels below it stay the place to read the reasoning; this
    // is the rail that says a currency has a plan at all.
    //
    // The owner column is the point of the panel, not decoration. Every number here is fetched from the
    // module that owns the ordering (SpendOverview does the fetching and nothing else), so the surface
    // that shows "what to buy next" can never become a second, competing opinion about it.
    //
    // READ-ONLY. Nothing here buys anything — AP is advise-only permanently, and PP/QP/seed auto-buy
    // already has its owner in AdvisorApply.
    public class SpendPanel : Panel
    {
        private const string Provenance = "Each row is the owning module's own answer — this page holds no ordering of its own.";

        private class RowUi
        {
            public Label Currency;
            public Label Main;
            public Label Note;
        }

        private readonly List<RowUi> _rows = new List<RowUi>();

        public int ContentHeight { get; private set; }

        public SpendPanel(int canvasW = 0)
        {
            int W = canvasW > 0 ? canvasW : UiLayout.PanelW;
            int inner = W - UiTheme.S(20);
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            var head = new Label
            {
                Text = "NEXT BUY", AutoSize = true, Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), UiTheme.S(10))
            };
            Controls.Add(head);

            // The currency column is sized from the widest caption it will ever hold, so the main
            // column starts at the same x on every row without a hand-tuned pixel.
            int curW = UiLayout.MeasureText("Seeds", UiTheme.Bold) + UiTheme.S(10);
            int y = UiTheme.S(10) + UiTheme.HeadPitch;

            for (int i = 0; i < 5; i++)
            {
                var row = new RowUi();
                row.Currency = new Label
                {
                    AutoSize = false, Size = new Size(curW, UiTheme.SText(20)), Font = UiTheme.Bold,
                    ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground,
                    Location = new Point(UiTheme.S(10), y)
                };
                row.Main = new Label
                {
                    AutoSize = false, Size = new Size(inner - curW, UiTheme.SText(20)), Font = UiTheme.Ui,
                    ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                    Location = new Point(UiTheme.S(10) + curW, y)
                };
                // The note carries the owner name and the gate, and is indented under the main column
                // so the currency letters stay the only thing in the left gutter.
                row.Note = new Label
                {
                    AutoSize = false, Size = new Size(inner - curW, UiTheme.SHead(16)), Font = UiTheme.Chip,
                    ForeColor = UiTheme.Faint, BackColor = UiTheme.Ground,
                    Location = new Point(UiTheme.S(10) + curW, row.Main.Bottom)
                };
                Controls.Add(row.Currency);
                Controls.Add(row.Main);
                Controls.Add(row.Note);
                _rows.Add(row);
                y = row.Note.Bottom + UiTheme.S(6);
            }

            var provenance = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.TextH), Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), y + UiTheme.S(4))
            };
            Controls.Add(provenance);
            UiLayout.FitOrGrow(provenance, Provenance);

            // Derived, never tuned: the panel is hosted in a scrolling section and does not scroll
            // itself, so a hand-set height would clip the last row at real DPI.
            ContentHeight = provenance.Bottom + UiTheme.S(10);

            VisibleChanged += (s, e) => { if (Visible) SyncFromSettings(); };
        }

        // Called from SettingsForm.UpdateFromSettings — the deferred, at-most-once-a-second pass on the
        // Unity main thread. Contained in one try/catch: a throw here would abort every panel after it.
        public void SyncFromSettings()
        {
            try
            {
                if (!Visible) return;
                if (Main.Character == null) return;

                IList<SpendOverview.Row> rows = SpendOverview.Rows();
                SpendOverview.LogChanges(rows);

                for (int i = 0; i < _rows.Count; i++)
                {
                    if (i >= rows.Count) { Clear(_rows[i]); continue; }
                    Render(_rows[i], rows[i]);
                }
            }
            catch (Exception ex) { LogDebug($"Spend panel refresh: {ex.Message}"); }
        }

        private static void Clear(RowUi ui)
        {
            UiLayout.FitInto(ui.Currency, "");
            UiLayout.FitInto(ui.Main, "");
            UiLayout.FitInto(ui.Note, "");
        }

        private static void Render(RowUi ui, SpendOverview.Row row)
        {
            UiLayout.FitInto(ui.Currency, row.Currency);

            string balance = NumberFormatter.Abbrev(row.Balance);
            if (!row.Known)
            {
                ui.Main.ForeColor = UiTheme.Muted;
                UiLayout.FitInto(ui.Main, $"{balance} banked · nothing queued");
            }
            else
            {
                // A missing price is absence of data, never a free purchase — so the affordability
                // verdict is printed only inside the CostKnown branch.
                string price = row.CostKnown
                    ? $"{NumberFormatter.Abbrev(row.Cost)} · {(row.Affordable ? "affordable" : "keep saving")}"
                    : "cost unknown";
                ui.Main.ForeColor = !row.CostKnown ? UiTheme.Muted
                    : row.Affordable ? UiTheme.Cap : UiTheme.Energy;
                UiLayout.FitInto(ui.Main, $"{balance} ▸ {row.Next} · {price}");
            }

            UiLayout.FitInto(ui.Note, $"{row.Note} — decided by {row.Owner}");
        }
    }
}
