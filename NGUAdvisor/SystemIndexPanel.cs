using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // SETTINGS › SYSTEM INDEX — the read-only half of Settings.
    //
    // It answers four questions and refuses the fifth. What is this system? What state is it in? Is
    // anything blocking it? Where do I configure it? It does NOT let you configure it here: the panels
    // own their state, and slice 6 spent eight migrations establishing exactly that. A second editable
    // surface would undo the whole thing.
    //
    // Which is why the state is rendered as CHIPS, not as disabled buttons. A greyed-out button says
    // "you should be able to click this but can't"; a chip says "this is how things are". The only
    // interactive control on a row is the one that takes you to the owner.
    //
    // Presentation only: the catalogue lives in SettingsIndex, the matcher lives in SettingsIndex, the
    // routes live in Destinations, the identities live in SettingsIndex.SystemIds. This panel invents
    // none of them — it supplies STATE, and nothing else.
    //
    // COMPLETE FOR SYSTEMS (slice 7.5C): all nine catalogue systems render, and two of them are not
    // two-layer systems at all — Loadouts has no single gate and five independently-chosen sources, Pit
    // has rival execution paths and no permission layer. Neither was forced into ON/OFF + ADVISOR/MANUAL
    // to make the column tidy; the row model bent, not the truth.
    //
    // REFERENCES join the list in 7.6C4B — searchable things whose owner is elsewhere (Cooking, Wishes,
    // Cards) or nowhere (Hotkeys). They are SEARCH RESULTS, not navigation: a blank query shows the nine
    // systems and nothing else, so the default index keeps the height and density it was accepted with.
    //
    // The 28 APPLICATION SETTINGS are NOT rows here and never will be: they are rendered by
    // BasicSettingsPanel below, as the canonical controls themselves (7.6C4C2). So this panel's empty state
    // answers for the whole CATALOGUE, not for its own rows — a Setting-only query renders nothing here, and
    // that is success, not failure. The index simply gets out of the way and collapses to its header.
    public class SystemIndexPanel : Panel
    {
        private static readonly int RowH = UiTheme.S(36), RowGap = UiTheme.S(6), FirstY = UiTheme.S(68),
            PadBottom = UiTheme.S(8), SideMargin = UiTheme.S(16), EmptyH = UiTheme.S(28);

        // Cost of the REFERENCES caption when it appears: a gap above it (only if something precedes it)
        // and the standard header-to-content pitch below it.
        private static readonly int RefCapGap = UiTheme.S(8);
        private static readonly int RefCapH = UiTheme.HeadPitch;

        // SHARED ROW GEOMETRY. SysCard and RefCard are two kinds of row in ONE list, so the eye has to be
        // able to run down a single left edge — the title starts at the same x, and the Open → button lands
        // in the same place. Hoisted out of SysCard rather than copied: same numbers, same rendered result,
        // one definition. (A nested type can read its enclosing type's privates, so both cards see these.)
        private static readonly int TitleX = UiTheme.S(10), TitleY = UiTheme.S(7);
        // 100 was "measured longest title + a small pad", and a small pad is not a pad: row titles are
        // BOLD, Mono paints bold wider than TextRenderer reports, and YGGDRASIL / ADVENTURE / MONEY PIT
        // overran this column by ~2px — enough to print AUTOMATION's "A" as "ʌ" (seen on screen 1.2.17).
        // The auditor cannot catch this class at all: TEXT CLIPPED compares the control against the same
        // measurement that is short. So the clearance is explicit and generous instead of tight, and it
        // is paid for out of the status column, where every string already goes through FitText.
        private static readonly int ColX = UiTheme.S(112);   // where a system row's AUTOMATION caption begins; a reference's blurb
        private static readonly int OpenW = UiLayout.BtnWidth("Open →") + UiTheme.S(6);

        // ONE empty state. 7.6C4C2 retired the apology this used to carry for Setting matches: a Setting is no
        // longer unrenderable, it is rendered by BasicSettingsPanel below — so this line means what it says.
        private const string NoMatch = "No matching systems, settings or references.";

        // The query, broadcast — NOT the TextBox, and not this panel's filtering state. Settings live in a
        // different panel and are rendered by it; this one just says what was typed. Both sides then ask the
        // SAME pure matcher the same question, so there is exactly one set of matching rules in the product.
        public event Action<string> SearchChanged;

        private readonly List<SysCard> _cards = new List<SysCard>();
        private readonly List<RefCard> _refs = new List<RefCard>();
        private readonly TextBox _search;
        private readonly Label _empty;
        private readonly Label _refsCap;
        private int _laidOutWidth = -1;

        public SystemIndexPanel()
        {
            Dock = DockStyle.Top;
            BackColor = UiTheme.Ground;

            Controls.Add(new Label
            {
                Text = "SYSTEM INDEX", AutoSize = true, Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(SideMargin, UiTheme.S(10))
            });

            _search = new TextBox { Location = new Point(SideMargin, UiTheme.S(32)), Width = UiTheme.S(300), Font = UiTheme.Ui };
            _search.TextChanged += (s, e) =>
            {
                Filter();                                       // this panel's own rows
                var h = SearchChanged;                          // …and whoever else renders this query
                if (h != null) h(_search.Text);
            };
            Controls.Add(_search);

            Controls.Add(new Label
            {
                // The point of the alias set, said where someone will actually read it.
                Text = "Search by any name it has ever had — \"cast blood spells\", \"auto quest\", \"combat enabled\".",
                AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Faint, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(324), UiTheme.S(36))
            });

            // ---- state providers: the ONLY system-specific knowledge in this file ----
            //
            // A provider returns the row's TRUTH, already in presentation terms. It is not two booleans:
            // two booleans could not have described Yggdrasil (no decisions layer), and they would have
            // forced Loadouts and Pit to invent states they do not have. The factories below build the
            // truthful RowState; the card just paints whatever it is handed.
            var providers = new Dictionary<string, Func<RowState>>
            {
                { SettingsIndex.SystemIds.Titans,    () => RowState.TwoLayer(Settings.ManageTitans,       Settings.AdvisorTitans, Blurb(SettingsIndex.SystemIds.Titans)) },

                // AUTOMATION only. Nothing reads an "AdvisorYggdrasil" because none exists, so there is
                // no fourth state to be idle in — turning automation off here is state C, full stop.
                { SettingsIndex.SystemIds.Yggdrasil, () => RowState.AutomationOnly(Settings.ManageYggdrasil, "No advisor strategy layer exists for it.",
                                                                                   "Automation is off — the tool will not harvest Yggdrasil.") },

                { SettingsIndex.SystemIds.Quests,    () => RowState.TwoLayer(Settings.AutoQuest,          Settings.AdvisorQuests, Blurb(SettingsIndex.SystemIds.Quests)) },
                { SettingsIndex.SystemIds.Blood,     () => RowState.TwoLayer(Settings.CastBloodSpells,    Settings.AdvisorBlood,  Blurb(SettingsIndex.SystemIds.Blood))  },
                { SettingsIndex.SystemIds.Gold,      () => RowState.TwoLayer(Settings.ManageGoldLoadouts, Settings.AdvisorGold,   Blurb(SettingsIndex.SystemIds.Gold))   },

                // BOOSTS — the two layers do NOT govern the same things. AUTOMATION (ManageInventory) is
                // inventory-wide: boosts, merges, filters, convertibles. DECISIONS (AutoBoostPriority)
                // only chooses the boost priority. A row that said "AUTOMATION OFF / DECISIONS ADVISOR /
                // advisor idle — automation is off" would be true and useless: it would let you think one
                // feature was asleep when four are. So every line names its own scope, and both OFF states
                // enumerate ALL FOUR — a shorter list reads as an exhaustive one, and convertibles would
                // be the casualty. (State D drops the adjective "inventory" rather than an area: naming
                // the four IS the scope, and the pair together will not fit the status column.)
                { SettingsIndex.SystemIds.Boosts,    () => RowState.TwoLayer(
                    Settings.ManageInventory, Settings.AutoBoostPriority,
                    "Inventory-wide automation; advisor sets boost priority.",
                    "Inventory-wide automation; your list sets boost priority.",
                    "Inventory automation off — boosts, merges, filters and convertibles stop.",
                    "Advisor idle — automation off: no boosts, merges, filters or convertibles.") },

                // ADVENTURE — two scope lies to avoid, and the chips alone cannot avoid either.
                //
                // (1) AUTOMATION (CombatEnabled) gates adventure ROUTING, not combat: Main.cs returns at
                //     :1218 only after titan and quest routing have already run. "OFF" must not read as
                //     "the tool stops fighting".
                // (2) MANUAL is the NORMAL routing source, not a guarantee: Gear Hunt and Target ITOPOD
                //     outrank the zone choice (Main.cs:1223-1225). They sit BEHIND the gate, though — so
                //     they are disclosed while it is open and correctly go unmentioned once it is shut,
                //     because then they do not run either.
                { SettingsIndex.SystemIds.Adventure, () => RowState.TwoLayer(
                    Settings.CombatEnabled, Settings.AdvisorZones,
                    "Advisor picks the farm zone; Gear Hunt and ITOPOD outrank it.",
                    "Your zone is the farm zone; Gear Hunt and ITOPOD outrank it.",
                    "Adventure routing off — titan and quest zones still run.",
                    "Advisor idle — adventure routing off (titans/quests still run).") },

                // LOADOUTS — NOT a two-layer system, and not seven decision sources either.
                //
                // There is no panel-wide gate: slice 6.6 disproved the ManageGear premise (it gates none
                // of the seven mode swaps — each mode swaps on its OWN setting), so AUTOMATION says
                // PER MODE rather than inventing an ON/OFF that governs nothing.
                //
                // And the DECISIONS summary counts FIVE, not seven: a mode's source is whether its
                // objective string is non-empty, and Loot Hunter and Shockwave have inert objective
                // getters, so they can never be anything but "MANUAL". Counting them would manufacture a
                // statistic. The five that genuinely choose are the ones summarised; the other two are
                // named as having no choice at all, so nobody reads their absence as manual preference.
                { SettingsIndex.SystemIds.Loadouts,  () =>
                    {
                        int adv = 0;
                        if (!string.IsNullOrEmpty(Settings.TitanObjective)) adv++;
                        if (!string.IsNullOrEmpty(Settings.GoldObjective)) adv++;
                        if (!string.IsNullOrEmpty(Settings.QuestObjective)) adv++;
                        if (!string.IsNullOrEmpty(Settings.YggdrasilObjective)) adv++;
                        if (!string.IsNullOrEmpty(Settings.CookingObjective)) adv++;
                        return RowState.Distributed(adv, 5,
                            $"{adv} of 5 modes on ADVISOR · Loot Hunter, Shockwave: no source choice.");
                    } },

                // MONEY PIT — still deliberately NOT normalised, but no longer UNRESOLVED, and the row now
                // says the settled thing instead of promising an answer later (slice 7.6C2B).
                //
                // What 6C2A settled: the two throw paths no longer race. AdvisorPit OWNS automatic throw
                // timing, and the standard AUTO THROW path (Settings.AutoMoneyPit) yields to it while it is
                // on (Main.cs:821) — its configuration is kept, not rewritten. That is a PRIORITY rule
                // between two rival paths; it is not a permission/strategy pair, so the row keeps its
                // INDEPENDENT chip and grows no DECISIONS half. Rendering "AUTOMATION OFF / DECISIONS
                // ADVISOR" here would still be a lie — AUTO THROW is not a master gate over the advisor,
                // and neither switch governs AutoSpin or MoneyPitRunMode, which stand entirely apart.
                //
                // So: informational still, INDEPENDENT still, no live state still. Only the words change,
                // from "we haven't decided" to what was decided.
                { SettingsIndex.SystemIds.Pit,       () => RowState.Informational(RowState.Independent,
                    "Auto throw yields to advisor; daily spin and pit-run independent.") },
            };

            // Order is the CATALOGUE's, not this file's: we walk the index and render the entries we can,
            // so a row cannot drift out of canonical order by being added in the wrong place, and the
            // remaining systems arrive by adding a provider — not by touching layout.
            foreach (var entry in SettingsIndex.All)
            {
                if (entry.Kind != EntryKind.System) continue;
                Func<RowState> state;
                if (!providers.TryGetValue(entry.Id, out state)) continue;   // not rendered in this build

                var card = new SysCard(entry, state);
                _cards.Add(card);
                Controls.Add(card);
            }

            // COMPLETENESS, both ways. A provider whose ID matches no catalogue entry would otherwise
            // just... not appear; a catalogue system with no provider would be searchable but unrenderable
            // — which was true by design until this stage and must never be true silently again.
            if (_cards.Count != providers.Count)
                LogDebug($"SystemIndex: {providers.Count - _cards.Count} state provider(s) matched no catalogue entry");

            int systems = SettingsIndex.All.Count(e => e.Kind == EntryKind.System);
            if (_cards.Count != systems)
                LogDebug($"SystemIndex: {systems - _cards.Count} system entr(ies) have no rendered provider");

            // REFERENCES (slice 7.6C4B) — same lifecycle law as the system cards: constructed ONCE, then only
            // shown, hidden and repositioned. Every Reference in the catalogue gets a row, and unlike systems
            // there is no provider to match: a Reference has no live state to fetch. That is the whole reason
            // it is a different card and not a bent RowState — no AUTOMATION, no DECISIONS, no state D, no
            // chips. Forcing one through SysCard would mean inventing the very layers Pit and Yggdrasil were
            // allowed to refuse.
            foreach (var entry in SettingsIndex.All)
            {
                if (entry.Kind != EntryKind.Reference) continue;
                var card = new RefCard(entry);
                _refs.Add(card);
                Controls.Add(card);
            }

            _refsCap = new Label
            {
                Text = "REFERENCES", AutoSize = true, Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Visible = false
            };
            Controls.Add(_refsCap);

            _empty = new Label
            {
                // ONE string, one condition: the matcher found nothing anywhere in the catalogue.
                Text = NoMatch,
                AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(SideMargin, FirstY + UiTheme.S(4)), Visible = false
            };
            Controls.Add(_empty);

            // Width is the parent's to decide (Dock.Top); re-place the rows when it changes, and only
            // then — Relayout writes Height, and a naive handler would recurse.
            SizeChanged += (s, e) => { if (ClientSize.Width != _laidOutWidth) Relayout(); };
            VisibleChanged += (s, e) => { if (Visible) Sync(); };

            Relayout();
            Sync();
            Filter();
        }

        private static string Blurb(string id) => SettingsIndex.ById(id)?.Blurb ?? "";

        // Live state. Called from SettingsForm.UpdateFromSettings (the same path that keeps every panel
        // in step) and on becoming visible. Constructs nothing, re-binds nothing, rebuilds nothing.
        public void Sync()
        {
            if (Settings == null) return;
            foreach (var c in _cards) c.Sync();
        }

        // The REAL matcher decides; this only asks which of the RENDERED rows survived it. No forked
        // search, no special-cased query strings, no second catalogue.
        private void Filter()
        {
            try
            {
                var hits = SettingsIndex.Search(_search.Text);
                bool blank = string.IsNullOrWhiteSpace(_search.Text);

                int shown = 0;
                foreach (var c in _cards)
                {
                    bool show = hits.Contains(c.Entry);
                    if (c.Visible != show) c.Visible = show;
                    if (show) shown++;
                }

                // REFERENCES ARE SEARCH RESULTS, NOT NAVIGATION. A blank query hides every one of them, and
                // that is a PRESENTATION policy enforced here — not a special case bolted onto the matcher.
                // Search("") deliberately returns the whole catalogue ("browse the index"), and it stays that
                // way: the model answers what MATCHES, this panel decides what it is willing to SHOW. Bending
                // the matcher so a blank query stopped matching References would have made a UI decision
                // untestable outside the game, which is precisely what the split exists to prevent.
                int refs = 0;
                foreach (var r in _refs)
                {
                    bool show = !blank && hits.Contains(r.Entry);
                    if (r.Visible != show) r.Visible = show;
                    if (show) refs++;
                }

                // THE EMPTY STATE NOW ANSWERS FOR THE WHOLE CATALOGUE, not for this panel's rows. A query
                // like "DiggerCap" renders nothing HERE and that is not a failure — BasicSettingsPanel below
                // is showing it. So the message appears only when the matcher found nothing ANYWHERE; when it
                // found Settings, this panel simply gets out of the way and collapses to its header.
                bool nothingAtAll = hits.Count == 0;
                if (_empty.Visible != nothingAtAll) _empty.Visible = nothingAtAll;

                Relayout();   // survivors close the gaps; nobody is destroyed or rebuilt
            }
            catch (Exception e) { LogDebug($"SystemIndex filter: {e.Message}"); }
        }

        // One deterministic pass: visible rows stack from the top in catalogue order, hidden ones are
        // skipped (they keep their last bounds — invisible controls neither paint nor overlap).
        //
        // HEIGHT NOW FOLLOWS THE VISIBLE ROWS (slice 7.5A, reversing stage 4). Stage 4 pinned the height
        // so filtering could not reflow the settings below — which was right when the index was a fixed
        // strip stealing from a separate scroll viewport, and wrong now: Settings is ONE scrolling
        // document, so a filtered index that kept nine rows' worth of height would just be a void. The
        // list is allowed to behave like a list. Nothing the user can be touching moves: the header and
        // the search box sit ABOVE the rows.
        private void Relayout()
        {
            try
            {
                _laidOutWidth = ClientSize.Width;
                int w = Math.Max(UiTheme.S(520), _laidOutWidth - SideMargin * 2);

                int y = FirstY, shown = 0;
                foreach (var c in _cards)
                {
                    if (!c.Visible) continue;
                    c.SetWidth(w);
                    c.Location = new Point(SideMargin, y);
                    y += RowH + RowGap;
                    shown++;
                }

                // The caption is EARNED, not reserved: it exists only while at least one Reference is on
                // screen, and it costs nothing — no height, no gap — on every other query, which is most of
                // them. It also only takes its leading gap when something is actually above it to be
                // separated from, so a references-only result starts at FirstY like any other list.
                int refs = 0;
                foreach (var r in _refs) if (r.Visible) refs++;

                if (refs > 0)
                {
                    if (shown > 0) y += RefCapGap;
                    _refsCap.Location = new Point(SideMargin, y);
                    if (!_refsCap.Visible) _refsCap.Visible = true;
                    y += RefCapH;

                    foreach (var r in _refs)
                    {
                        if (!r.Visible) continue;
                        r.SetWidth(w);
                        r.Location = new Point(SideMargin, y);
                        y += RowH + RowGap;
                    }
                }
                else if (_refsCap.Visible) _refsCap.Visible = false;

                // Height follows the rows that are actually there — the same law as before, now summed over
                // two kinds. y already carries every visible row, the caption and its gap.
                //
                // THREE OUTCOMES, not two. With nothing rendered the panel either carries the no-match line
                // (104) or collapses to header + search box (76) — the latter being the Setting-only case,
                // where reserving a message row would push the real results down to make room for an
                // apology nobody needs.
                int h;
                if (shown + refs > 0) h = y + PadBottom;
                else if (_empty.Visible) h = FirstY + EmptyH + PadBottom;
                else h = FirstY + PadBottom;
                if (Height != h) Height = h;
            }
            catch (Exception e) { LogDebug($"SystemIndex relayout: {e.Message}"); }
        }

        // -----------------------------------------------------------------------------------------
        // The row's truth, in presentation terms. A VALUE, not a set of switches: there is no
        // IsYggdrasil, no HideDecisions, no IsPit. A system that has no second layer supplies no right
        // pair; a system whose status is a warning supplies the warning colour. The card cannot tell
        // which system it is drawing, and that is the point — every conditional it does not have is a
        // system it cannot misrepresent.
        // -----------------------------------------------------------------------------------------
        private class RowState
        {
            // Chip words the index needs and the two-layer vocabulary does not have. They are NOT
            // alternatives to ON/OFF/ADVISOR/MANUAL — they are what a system says when it HAS no such
            // layer, so they live here rather than polluting SystemControlBar's contract.
            public const string PerMode = "PER MODE";        // no single automation gate exists
            public const string Independent = "INDEPENDENT"; // rival switches, no permission layer at all
            public const string AllAdvisor = "ALL ADVISOR";
            public const string AllManual = "ALL MANUAL";
            public const string Mixed = "MIXED";

            public string LeftCaption, LeftChip;
            public Color LeftColor;
            public string RightCaption, RightChip;   // RightCaption == null => this system has no second layer
            public Color RightColor;
            public string Status;
            public Color StatusColor;                // Ink = act on this; Faint = just describing itself

            // The canonical two-layer system: a permission the managers read, ANDed with a strategy the
            // advisor reads. Four states, one line each — the SAME four-string contract SystemControlBar
            // takes, so a system's row and its panel cannot end up telling different stories.
            //
            // A system whose permission covers exactly its own panel says the same thing in states A, B
            // and C (its blurb) and the canonical line in D. A system whose permission is BROADER than
            // its panel (Boosts) or whose layers mean something narrower than their names suggest
            // (Adventure) writes its own four lines. That is a truth table, not a special case: the card
            // never learns which kind it is holding.
            public static RowState TwoLayer(bool auto, bool advisor, string blurb)
                => TwoLayer(auto, advisor, blurb, blurb, blurb, SystemControlBar.AdvisorIdle);

            public static RowState TwoLayer(bool auto, bool advisor,
                string whenAdvisor, string whenManual, string whenOff, string whenIdle)
            {
                bool idle = !auto && advisor;   // state D: asked for advisor decisions on a system the tool may not touch
                return new RowState
                {
                    LeftCaption = SystemControlBar.AutomationCap,
                    LeftChip = auto ? SystemControlBar.On : SystemControlBar.Off,
                    LeftColor = auto ? UiTheme.Cap : UiTheme.Danger,

                    RightCaption = SystemControlBar.DecisionsCap,
                    RightChip = advisor ? SystemControlBar.Advisor : SystemControlBar.Manual,
                    RightColor = advisor ? UiTheme.Accent : UiTheme.Energy,

                    // Only state D is a warning. State B (automation on, decisions manual) is a perfectly
                    // good way to run a system and gets no warning — that was the whole point of keeping
                    // both layers — and an unusual SCOPE is not a fault either: Adventure's overrides and
                    // Boosts' wider permission are disclosures, so they stay Faint.
                    Status = auto ? (advisor ? whenAdvisor : whenManual) : (idle ? whenIdle : whenOff),
                    StatusColor = idle ? UiTheme.Ink : UiTheme.Faint,
                };
            }

            // A system with a permission and no strategy. The right chip still says MANUAL ONLY — the
            // same words SystemControlBar already uses in the panel this row links to — but in FAINT,
            // not the amber of a chosen MANUAL: amber means "you picked manual", grey means "there is
            // no choice to make". There is no state D, because there is no advisor to leave idle.
            public static RowState AutomationOnly(bool auto, string note, string whenOff)
            {
                return new RowState
                {
                    LeftCaption = SystemControlBar.AutomationCap,
                    LeftChip = auto ? SystemControlBar.On : SystemControlBar.Off,
                    LeftColor = auto ? UiTheme.Cap : UiTheme.Danger,

                    RightCaption = SystemControlBar.DecisionsCap,
                    RightChip = SystemControlBar.ManualOnly,
                    RightColor = UiTheme.Faint,

                    Status = auto ? note : whenOff,
                    StatusColor = UiTheme.Faint,
                };
            }

            // DISTRIBUTED decisions: no panel-wide gate, and the strategy is chosen n separate times. The
            // chip summarises the n; the status carries the arithmetic. The left chip is FAINT because
            // "PER MODE" is not a state you can turn on or off — it is the shape of the system.
            public static RowState Distributed(int advisor, int total, string status)
            {
                string chip = advisor == total ? AllAdvisor : advisor == 0 ? AllManual : Mixed;

                return new RowState
                {
                    LeftCaption = SystemControlBar.AutomationCap,
                    LeftChip = PerMode,
                    LeftColor = UiTheme.Faint,

                    RightCaption = SystemControlBar.DecisionsCap,
                    RightChip = chip,
                    // Accent whenever the advisor decides ANY of them, amber only when every one is
                    // yours — the same reading as a Pattern A row, applied to a set instead of a bool.
                    RightColor = advisor > 0 ? UiTheme.Accent : UiTheme.Energy,

                    Status = status,
                    StatusColor = UiTheme.Faint,
                };
            }

            // INFORMATIONAL: a system that does not fit the model, saying so. No right pair — inventing
            // a DECISIONS chip here is exactly the fabrication this row exists to refuse — and no live
            // state, because summarising rival switches would force one of them to pose as the master.
            // Faint, not Ink: it is a deferral, not an error, and nothing is broken.
            public static RowState Informational(string leftChip, string status)
            {
                return new RowState
                {
                    LeftCaption = SystemControlBar.AutomationCap,
                    LeftChip = leftChip,
                    LeftColor = UiTheme.Faint,

                    RightCaption = null,   // the "no second layer" case, already supported by the card

                    Status = status,
                    StatusColor = UiTheme.Faint,
                };
            }

            public bool Same(RowState o) =>
                o != null &&
                LeftCaption == o.LeftCaption && LeftChip == o.LeftChip && LeftColor == o.LeftColor &&
                RightCaption == o.RightCaption && RightChip == o.RightChip && RightColor == o.RightColor &&
                Status == o.Status && StatusColor == o.StatusColor;
        }

        // -----------------------------------------------------------------------------------------
        // The row. Presentation ONLY: a title, two labelled chips, one status line, one way out.
        //
        // It owns no settings field, no destination literal, no matcher and no business rule — the entry
        // carries identity and route, the provider hands it a finished RowState. That is the whole
        // contract, and it is deliberately too thin to become a dashboard framework.
        // -----------------------------------------------------------------------------------------
        private class SysCard : Panel
        {
            public readonly IndexEntry Entry;

            // Column geometry is FIXED and shared by every row, so the chips and the status text line up
            // down the index instead of ragging out with each system's caption widths.
            //
            // Each chip is sized to the WIDEST word it can ever hold, not the widest it holds today: a
            // chip is a fixed-size label, and under this Mono a fixed label whose text overflows paints
            // NOTHING at all. Loadouts and Pit put words in these chips (PER MODE, INDEPENDENT, ALL
            // ADVISOR) that ON/OFF never needed, which costs the status column ~30px — paid for below.
            // CapX moved out to SystemIndexPanel.ColX (7.6C4B) so RefCard can start its blurb on the same
            // column. See the clearance note at ColX for why it is not sized to the measured longest
            // title: that is exactly what it used to be, and the bold titles overran it.
            private static readonly int CapX = ColX;
            private static readonly int LChipX = CapX + UiLayout.MeasureText(SystemControlBar.AutomationCap, UiTheme.ColHeader) + UiTheme.S(8);
            private static readonly int LChipW = Widest(SystemControlBar.On, SystemControlBar.Off, RowState.PerMode, RowState.Independent) + UiTheme.S(20);
            private static readonly int RCapX = LChipX + LChipW + UiTheme.S(12);
            private static readonly int RChipX = RCapX + UiLayout.MeasureText(SystemControlBar.DecisionsCap, UiTheme.ColHeader) + UiTheme.S(8);
            private static readonly int RChipW = Widest(SystemControlBar.Advisor, SystemControlBar.Manual, SystemControlBar.ManualOnly,
                                                        RowState.AllAdvisor, RowState.AllManual, RowState.Mixed) + UiTheme.S(20);
            private static readonly int StatusX = RChipX + RChipW + UiTheme.S(14);

            private static int Widest(params string[] texts)
            {
                int w = 0;
                foreach (var t in texts) w = Math.Max(w, UiLayout.MeasureText(t, UiTheme.Chip));
                return w;
            }

            private readonly Func<RowState> _state;
            private readonly Label _leftCap, _rightCap, _leftChip, _rightChip, _status;
            private readonly Button _open;

            private RowState _shown;   // what is currently PAINTED — Sync compares against this
            private string _statusRaw = "";

            public SysCard(IndexEntry entry, Func<RowState> state)
            {
                Entry = entry;
                _state = state;

                Height = RowH;
                BackColor = UiTheme.Surface;
                BorderStyle = BorderStyle.FixedSingle;

                Controls.Add(new Label
                {
                    Text = entry.Title.ToUpperInvariant(), AutoSize = true, Font = UiTheme.Bold,
                    ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(TitleX, TitleY)
                });

                _leftCap = MkCap(CapX);
                _leftChip = MkChip(LChipX, LChipW);
                _rightCap = MkCap(RCapX);
                _rightChip = MkChip(RChipX, RChipW);
                foreach (var c in new Control[] { _leftCap, _leftChip, _rightCap, _rightChip }) Controls.Add(c);

                // Status line: fixed width, so every string goes through FitText — an overflowing Mono
                // label paints NOTHING at all.
                _status = new Label
                {
                    AutoSize = false, Height = UiTheme.TextH, Font = UiTheme.Ui,
                    ForeColor = UiTheme.Faint, BackColor = UiTheme.Surface,
                    Location = new Point(StatusX, UiTheme.S(7))
                };
                Controls.Add(_status);

                // The ONLY interactive thing on the row.
                //
                // UNIFORM "Open →", not "Open <System> →" (slice 7.5C). Naming the system in the button
                // made a row's own title eat its own status column — ADVENTURE and MONEY PIT, the two
                // with the most to explain, had the least room to explain it. The title is already the
                // first thing on the row; the button only has to be the way out. Uniform width also means
                // every status column is now exactly the same, so the wording budget is one number.
                _open = new Button
                {
                    Text = "Open →",
                    Size = new Size(OpenW, UiTheme.SCtl(24)),
                    Font = UiTheme.Ui,
                    Top = UiTheme.S(5)
                };
                UiTheme.StyleGhost(_open);
                _open.Click += (s, e) =>
                {
                    // Semantic destination off the entry, never a rail path: slice 8 retargets
                    // Destinations, not this file.
                    try { (FindForm() as SettingsForm)?.NavigateTo(Entry.Destination); }
                    catch (Exception ex) { LogDebug($"SystemIndex open {Entry.Id}: {ex.Message}"); }
                };
                Controls.Add(_open);
            }

            public void SetWidth(int w)
            {
                if (Width != w) Width = w;
                int cw = ClientSize.Width;

                _open.Left = cw - _open.Width - UiTheme.S(10);
                _status.Width = Math.Max(UiTheme.S(80), _open.Left - UiTheme.S(12) - StatusX);
                UiLayout.FitInto(_status, _statusRaw);
            }

            public void Sync()
            {
                try
                {
                    var s = _state();
                    if (s == null || s.Same(_shown)) return;   // nothing moved
                    _shown = s;

                    _leftCap.Text = s.LeftCaption;
                    _leftChip.Text = s.LeftChip;
                    _leftChip.BackColor = s.LeftColor;

                    // A system with no second layer supplies no right pair, and the pair simply is not
                    // there — no empty chip, no "N/A", nothing to misread.
                    bool hasRight = s.RightCaption != null;
                    _rightCap.Visible = _rightChip.Visible = hasRight;
                    if (hasRight)
                    {
                        _rightCap.Text = s.RightCaption;
                        _rightChip.Text = s.RightChip;
                        _rightChip.BackColor = s.RightColor;
                    }

                    _statusRaw = s.Status ?? "";
                    _status.ForeColor = s.StatusColor;
                    UiLayout.FitInto(_status, _statusRaw);
                }
                catch (Exception e) { LogDebug($"SystemIndex sync {Entry.Id}: {e.Message}"); }
            }

            private static Label MkCap(int x) => new Label
            {
                Text = "", AutoSize = true, Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface,
                Location = new Point(x, UiTheme.S(7))
            };

            // A CHIP, not a button. It cannot be clicked, hovered or focused, and it does not pretend to
            // be disabled — it simply reports.
            private static Label MkChip(int x, int w) => new Label
            {
                Text = "", AutoSize = false, Size = new Size(w, UiTheme.SHead(20)),
                Font = UiTheme.Chip, ForeColor = Color.White, BackColor = UiTheme.Faint,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(x, UiTheme.S(7))
            };
        }

        // -----------------------------------------------------------------------------------------
        // A REFERENCE ROW: a searchable thing whose owner is somewhere else, or nowhere.
        //
        // Deliberately NOT a SysCard with the chips turned off. A Reference has no AUTOMATION, no DECISIONS,
        // no live state and no state D — and RowState exists precisely because Yggdrasil, Loadouts and Pit
        // were allowed to NOT have layers they do not have. Threading a chip-less state through it would
        // reintroduce, in the search UI, the same "make the column tidy" pressure the row model was built to
        // resist. Different truth, different card. They share geometry, not semantics.
        //
        // No provider, no Sync: there is nothing live to read. A Reference says what it is and where it
        // lives, and neither answer changes while the game runs.
        private sealed class RefCard : Panel
        {
            public readonly IndexEntry Entry;

            private readonly Label _blurb;
            private readonly Button _open;   // null when the entry has nowhere to send you
            private readonly string _blurbRaw;

            public RefCard(IndexEntry entry)
            {
                Entry = entry;
                _blurbRaw = entry.Blurb ?? "";

                Height = RowH;
                BackColor = UiTheme.Surface;
                BorderStyle = BorderStyle.FixedSingle;

                Controls.Add(new Label
                {
                    Text = entry.Title.ToUpperInvariant(), AutoSize = true, Font = UiTheme.Bold,
                    ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(TitleX, TitleY)
                });

                // The blurb starts on the same column a system's chips do and simply keeps going. A system
                // spends that space saying what state it is in; a Reference has no state, so it spends it
                // saying what it is — which is why these rows can afford a whole sentence.
                _blurb = new Label
                {
                    AutoSize = false, Height = UiTheme.TextH, Font = UiTheme.Ui,
                    ForeColor = UiTheme.Faint, BackColor = UiTheme.Surface,
                    Location = new Point(ColX, TitleY)
                };
                Controls.Add(_blurb);

                // NO DESTINATION, NO BUTTON. Not a disabled one, not an empty frame, not a placeholder: a
                // greyed-out Open → would claim there is somewhere to go and that you are merely not allowed
                // — and for Hotkeys the truth is that the blurb IS the answer. Absence is the honest render,
                // and the width it gives back is exactly what the longest blurb needs.
                if (!string.IsNullOrEmpty(entry.Destination))
                {
                    _open = new Button
                    {
                        Text = "Open →",
                        Size = new Size(OpenW, UiTheme.SCtl(24)),
                        Font = UiTheme.Ui,
                        Top = UiTheme.S(5)
                    };
                    UiTheme.StyleGhost(_open);
                    // Routes by DESTINATION, never by branching on which reference this is — the same
                    // discipline SysCard follows, so slice 8 retargets Destinations and nothing here moves.
                    _open.Click += (s, e) =>
                    {
                        try { (FindForm() as SettingsForm)?.NavigateTo(Entry.Destination); }
                        catch (Exception ex) { LogDebug($"SystemIndex open {Entry.Id}: {ex.Message}"); }
                    };
                    Controls.Add(_open);
                }
            }

            public void SetWidth(int w)
            {
                if (Width != w) Width = w;
                int cw = ClientSize.Width;

                int right;
                if (_open != null)
                {
                    _open.Left = cw - _open.Width - UiTheme.S(10);
                    right = _open.Left - UiTheme.S(12);
                }
                else right = cw - UiTheme.S(10);   // no button, no reserved gap where one would have been

                _blurb.Width = Math.Max(UiTheme.S(80), right - ColX);
                UiLayout.FitInto(_blurb, _blurbRaw);
            }
        }
    }
}
