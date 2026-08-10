using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Economy > AP PURCHASES — a READ-ONLY view of ApPurchaseAdvisor: the AP balance, the next
    // recommended buy, the queue behind it, and where the ordering comes from.
    //
    // NOTHING HERE BUYS ANYTHING, and nothing here may ever be made to. AP is not refundable and the
    // ordering is one player's ranking, so the panel advises and the player spends. There is no button,
    // no double-click handler and no context menu on this surface by design — see ApPurchaseAdvisor's
    // "advise-only" note, which this panel is the visible half of.
    //
    // THE PROVENANCE LINE IS PART OF THE DATA, not decoration. Two very different kinds of fact meet on
    // this screen: the ORDER is OJ of Steel's opinion, while the ownership and cost reads are decomp-derived
    // game truth. This is the one place a user could mistake the first for the second, so the line stays.
    //
    // A ROW WITH NO PRICE SAYS SO. ApRec.CostKnown is false when the shop pod could not be read (typically
    // an entry the account has not unlocked yet) and ApRec.Cost is then 0 — absence of data, not a free
    // purchase. Printing that zero where a price belongs would be worse than printing nothing.
    public class ApPanel : Panel
    {
        // The advisor is asked ONCE per refresh and both the card and the list are rendered from that one
        // answer, so they cannot disagree about the next buy or about the balance it was priced against.
        // Row 0 is the next purchase (the card); the rest are the queue behind it.
        private const int QueueDepth = 8;

        private const string Provenance =
            "Order: OJ of Steel's AP Tier List (build 1.200) — one player's ranking, not game truth.";

        private readonly Label _balance;
        private readonly Panel _card;
        private readonly Label _cardValue;
        private readonly Label _cardCost;
        private readonly Label _cardNote;
        private readonly Label[] _rows = new Label[QueueDepth - 1];

        public int ContentHeight { get; private set; }

        // canvasW: explicit canvas width when hosted in a section column (0 = UiLayout.PanelW).
        public ApPanel(int canvasW = 0)
        {
            int W = canvasW > 0 ? canvasW : UiLayout.PanelW;
            int inner = W - UiTheme.S(20);
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            var head = new Label
            {
                Text = "AP PURCHASES", AutoSize = true, Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), UiTheme.S(10))
            };
            Controls.Add(head);

            _balance = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.SText(22)), Font = UiTheme.Bold,
                ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), UiTheme.S(10) + UiTheme.HeadPitch)
            };
            Controls.Add(_balance);

            // The card's height is DERIVED from the children it holds (set after they are placed), never
            // tuned: its note is a reserved two-line box whose floor grows with the measured head line, and
            // a fixed card would clip it silently — a card has no scrollbar to reach the overflow with.
            _card = new Panel
            {
                Location = new Point(UiTheme.S(10), _balance.Bottom + UiTheme.S(8)),
                Size = new Size(inner, UiTheme.S(10)),
                BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle
            };
            int cardInner = inner - UiTheme.S(16);
            var cardTitle = new Label
            {
                Text = "NEXT PURCHASE", AutoSize = true, Font = UiTheme.Chip,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface,
                Location = new Point(UiTheme.S(8), UiTheme.S(6))
            };
            _cardValue = new Label
            {
                AutoSize = false, Size = new Size(cardInner, UiTheme.SText(22)), Font = UiTheme.Bold,
                ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface,
                Location = new Point(UiTheme.S(8), UiTheme.S(6) + UiTheme.HeadPitch)
            };
            _cardCost = new Label
            {
                AutoSize = false, Size = new Size(cardInner, UiTheme.SText(20)), Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface,
                Location = new Point(UiTheme.S(8), _cardValue.Bottom + UiTheme.S(2))
            };
            _cardNote = new Label
            {
                AutoSize = false, Size = new Size(cardInner, UiTheme.SHead(36)), Font = UiTheme.Chip,
                ForeColor = UiTheme.Faint, BackColor = UiTheme.Surface,
                Location = new Point(UiTheme.S(8), _cardCost.Bottom + UiTheme.S(4))
            };
            _card.Controls.Add(cardTitle);
            _card.Controls.Add(_cardValue);
            _card.Controls.Add(_cardCost);
            _card.Controls.Add(_cardNote);
            _card.Height = _cardNote.Bottom + UiTheme.S(8);
            Controls.Add(_card);

            var queueHead = new Label
            {
                Text = "QUEUE BEHIND IT", AutoSize = true, Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), _card.Bottom + UiTheme.S(10))
            };
            Controls.Add(queueHead);

            int rowY = queueHead.Top + UiTheme.HeadPitch;
            for (int i = 0; i < _rows.Length; i++)
            {
                _rows[i] = new Label
                {
                    AutoSize = false, Size = new Size(inner, UiTheme.SText(18)), Font = UiTheme.Ui,
                    ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground,
                    Location = new Point(UiTheme.S(10), rowY)
                };
                Controls.Add(_rows[i]);
                rowY += UiTheme.LinePitch;
            }

            var provenance = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.TextH), Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), rowY + UiTheme.S(6))
            };
            Controls.Add(provenance);
            // Set once, in the ctor: the text is constant, so the height it grows to is part of the
            // panel's derived height rather than something a later refresh could change underneath it.
            UiLayout.FitOrGrow(provenance, Provenance);

            // The panel does not scroll — it is hosted in a scrolling section — so its height must come
            // from its content. Anything hand-tuned here would clip the last rows at real DPI.
            ContentHeight = provenance.Bottom + UiTheme.S(10);

            VisibleChanged += (s, e) => { if (Visible) SyncFromSettings(); };
        }

        // Named for the deferred pass in SettingsForm.UpdateFromSettings, which runs at most once a second
        // on the Unity main thread — the only place live game reads may happen. Every failure is contained
        // here: one throwing read must not abort the rest of that pass.
        public void SyncFromSettings()
        {
            try
            {
                // Hidden panel, no work: UpdateFromSettings calls this every refresh whatever page is on
                // screen, and Queue() asks the game for ownership and a live cost per row. Visible is
                // false while any parent is hidden, so an unselected sub-page counts.
                if (!Visible) return;

                if (Main.Character == null) return;

                _balance.Text = $"BALANCE — {NumberFormatter.Abbrev(ApPurchaseAdvisor.Balance())} AP";

                IReadOnlyList<ApRec> queue = ApPurchaseAdvisor.Queue(QueueDepth);
                RenderCard(queue.Count > 0 ? queue[0] : new ApRec());

                for (int i = 0; i < _rows.Length; i++)
                {
                    int q = i + 1;
                    _rows[i].ForeColor = UiTheme.Ink;
                    if (q >= queue.Count) { _rows[i].Text = ""; UiLayout.Tip(_rows[i], null); continue; }
                    ApRec rec = queue[q];
                    if (!rec.Known || rec.Item == null)
                    {
                        _rows[i].ForeColor = UiTheme.Muted;
                        UiLayout.FitInto(_rows[i], "unresolved entry — cost unknown");
                        continue;
                    }
                    UiLayout.FitInto(_rows[i], $"T{rec.Item.Tier} · {rec.Item.Name} — {Price(rec)}");
                }
            }
            catch (Exception ex) { LogDebug($"AP panel refresh: {ex.Message}"); }
        }

        private void RenderCard(ApRec rec)
        {
            if (!rec.Known || rec.Item == null)
            {
                // Empty queue means every entry in the table reads as owned. A row that is merely
                // unresolvable lands here too, and says so rather than naming a purchase it cannot identify.
                _cardValue.ForeColor = UiTheme.Muted;
                UiLayout.FitInto(_cardValue, "NOTHING LEFT TO RECOMMEND");
                _cardCost.ForeColor = UiTheme.Muted;
                UiLayout.FitInto(_cardCost, "Every entry in the tier list reads as owned.");
                UiLayout.WrapInto(_cardNote, "");
                return;
            }

            _cardValue.ForeColor = UiTheme.Accent;
            UiLayout.FitInto(_cardValue, $"T{rec.Item.Tier} · {rec.Item.Name}");

            // Affordability is stated ONLY beside a price we actually read. ApRec.Affordable is already
            // false when the cost is unknown, and printing "not affordable" there would dress a missing
            // read up as a verdict about the balance.
            _cardCost.ForeColor = rec.CostKnown ? (rec.Affordable ? UiTheme.Cap : UiTheme.Energy) : UiTheme.Muted;
            UiLayout.FitInto(_cardCost, rec.CostKnown
                ? $"{NumberFormatter.Abbrev(rec.Cost)} AP — {(rec.Affordable ? "you can afford this now" : "keep saving")}"
                : "cost unknown");

            UiLayout.WrapInto(_cardNote, rec.Item.Note ?? "");
        }

        private static string Price(ApRec rec)
        {
            if (!rec.CostKnown) return "cost unknown";
            return $"{NumberFormatter.Abbrev(rec.Cost)} AP{(rec.Affordable ? " · affordable" : "")}";
        }
    }
}
