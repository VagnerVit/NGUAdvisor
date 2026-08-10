using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Strangler step 1: a clean, code-only "Settings" tab surfacing the master toggles that users
    // actually flip day to day, grouped by system. The legacy resx tabs remain for detailed/numeric
    // configuration ("advanced"); over time more of them migrate here. Plain bound checkboxes only —
    // the one control pattern proven reliable under the injected Mono.
    public class BasicSettingsPanel : Panel
    {
        private class Bind
        {
            public CheckBox Box;
            public Func<bool> Get;
        }

        // ---- the canonical surface map (slice 7.6C4C1) ----
        //
        // LAYOUT METADATA, AND NOTHING ELSE. A SettingSurface does not read a setting, does not write one,
        // does not own a value and does not replace _binds or _numSyncs — those still do the entire job of
        // keeping the controls and Settings in step, exactly as before. This model answers one question the
        // panel previously could not answer at all: "which control(s) ARE the surface called
        // Setting.DiggerCap, and where does it sit in the accepted grid?"
        //
        // It exists because search needs to show and hide CANONICAL controls, and there was nothing to
        // address them by: everything was built from local variables and thrown into Controls, so after the
        // constructor returned, the panel had no idea which checkbox was which. Nothing here is a second
        // owner — these are references to the SAME control objects the user has always clicked.
        //
        // The Id IS the catalogue's Setting ID. Not a parallel enum, not a numeric handle, not a label —
        // one identity, so a lookup key cannot disagree with the entry it was meant to find.
        private sealed class SettingSurface
        {
            public readonly string Id;          // "Setting.DiggerCap" — the catalogue's, verbatim
            public readonly string Group;       // visible heading caption; null for the master button
            public readonly Control[] Controls; // ONE logical surface, one or more real controls
            public Rectangle[] GridBounds;      // where they sit in the accepted grid, captured once

            public SettingSurface(string id, string group, Control[] controls)
            {
                Id = id; Group = group; Controls = controls;
            }
        }

        // A visible heading and the surfaces under it. These four captions are the layout contract, and the
        // panel does not read SettingsIndex.Entry.Group to get them — that field is SEARCH text. (As of
        // 7.6C4D the two agree, because the catalogue was corrected to name these same four headings; they
        // agree by reconciliation, not by dependency, and layout must not start depending on search metadata.)
        private sealed class SettingGroup
        {
            public readonly string Caption;
            public readonly Label Header;
            public readonly List<SettingSurface> Surfaces = new List<SettingSurface>();
            public Rectangle HeaderBounds;

            public SettingGroup(string caption, Label header) { Caption = caption; Header = header; }
        }

        private readonly List<Bind> _binds = new List<Bind>();
        private readonly List<Action> _numSyncs = new List<Action>();
        private bool _syncing;
        private Button _master;

        private readonly List<SettingSurface> _surfaces = new List<SettingSurface>();
        private readonly Dictionary<string, SettingSurface> _byId = new Dictionary<string, SettingSurface>();
        private readonly List<SettingGroup> _groups = new List<SettingGroup>();

        // Everything that is NOT a Setting surface and NOT a heading, but still has to land back in the
        // right place: today that is the footer line, alone. It is not part of the 28 and never counts
        // toward catalogue parity.
        private readonly List<Control> _chrome = new List<Control>();
        private Rectangle[] _chromeBounds;

        private int _gridHeight;

        private static readonly int RowStep = UiTheme.S(24);

        public BasicSettingsPanel()
        {
            // SCROLL OWNERSHIP (slice 7.5A): this panel no longer scrolls itself. Settings is ONE
            // document — SYSTEM INDEX above, APPLICATION SETTINGS below — and the section that hosts both
            // owns the single vertical scrollbar. A Dock.Fill panel with its own AutoScroll would have
            // pinned this content into whatever the index left over (306px of viewport for ~500px of
            // content once nine system rows land, with Reload/Unload below the fold), which is the
            // nested-scroll trap the migration exists to avoid. So: Dock.Top + an EXPLICIT height that
            // covers all of the content, and the section scrolls the pair as one surface.
            //
            // Nothing else about this panel changes: same controls, same bindings, same order.
            Dock = DockStyle.Top;
            BackColor = UiTheme.Ground;
            AutoScroll = false;

            // Verb-in-the-header layout (review decision 3): the group header carries the shared verb
            // ("MANAGE Energy", "AUTO Quest"), checkboxes carry only what differs.
            // Round-3 re-space: four columns spread across the 1030 M1 canvas (was 16/180/344/508
            // for the old 664 window) — pure x-arithmetic, no control changes.
            int x0 = UiTheme.S(16), x1 = UiTheme.S(272), x2 = UiTheme.S(528), x3 = UiTheme.S(784);

            // Phase D: the MASTER kill-switch, re-homed from the retired General page (also on the
            // F1 hotkey). Everything below obeys it.
            _master = new Button { Text = "ADVISOR ACTIVE", Size = new Size(UiLayout.BtnWidth("ADVISOR ACTIVE"), UiTheme.SCtl(26)), Location = new Point(x0, UiTheme.S(8)), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat };
            _master.FlatAppearance.BorderColor = UiTheme.Border;
            _master.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.GlobalEnabled = !Settings.GlobalEnabled;
                Sync();
            };
            Controls.Add(_master);
            // The master has no heading above it — it is not in a group, it IS the thing every group obeys.
            Reg("GlobalEnabled", null, _master);

            // Canonical vocabulary (slice 1): every switch in this column is a PERMISSION — the layer
            // the managers read in Main.Update — so it is AUTOMATION, the same word the system panels
            // now use. (The AUTO column below is a mixed bag — some permissions, some one-shot actions
            // — and gets split in the Settings-index slice, not here.)
            // SLICE 7.6B — the fifteen proven duplicates are GONE from this panel. Each one had a
            // reachable, editable system owner writing the same field (the 6A ledger), so removing the
            // copy here changes the owner from "system panel + a Settings twin" to "system panel", and
            // strands nothing. The FIELDS all still exist; only these UI surfaces went away.
            //
            // What stayed, and why, because the absences are the interesting part:
            //   ManageGear    — no other reachable owner (its legacy twin died with the Allocation page).
            //                   RELABELLED: "Gear" under an AUTOMATION header read as "swap gear for
            //                   loadouts", which it has never done — it gates the advisor's gear refresh,
            //                   the Loot Hunter equip and the profile's gear timeline (slice 6.6).
            //   Cooking · Cooking gear · Wishes · Cast Cards · Titan Gold — real duplicates, but their
            //                   owners are re-hosted LEGACY pages (and Top Actions). Whether Settings
            //                   should yield to those is a policy call, not a tracing one: DEFERred.
            //
            // SLICE 7.6C2B — Money Pit (AutoMoneyPit) is GONE from here too. 6B kept it because PitPanel did
            // not expose it and this was the only switch gating MoneyPitManager.CheckMoneyPit(); that is no
            // longer true. PitPanel now owns it as AUTO THROW, in the manual strip beside the min tier,
            // Predict + Prep and Pit Run Mode that configure the very loop it starts. The field, its default,
            // its persistence and its runtime read (Main.cs:821) are all untouched — only this surface died.
            //
            // SLICE 7.6C3 — four of the five DEFERs resolve, and the policy call went AGAINST Settings.
            // Cooking, Cooking gear, Wishes and Cast Cards are gone: their dedicated pages (Systems/Cooking,
            // Cards/Wishes, Cards/Cards) are unconditionally reachable, editable and two-way synced, which is
            // the whole of the ownership test. A page being LEGACY is a fact about its code, not about the
            // user's experience of it, and code age is not an ownership criterion.
            //
            // TITAN GOLD IS THE ONE THAT STAYS, and the reason is worth keeping: its Top Actions chip lives
            // on an advisor RECOMMENDATION CARD that is only emitted while ManageGoldLoadouts is on AND a
            // titan sits within auto-kill reach (OptimizationAdvisor:439-452) — while the field itself gates
            // AdvisorApply:55 unconditionally. A switch whose only off button disappears along with the
            // advice is not an owner. That is why the test says UNCONDITIONALLY reachable.
            int y0 = Build(x0, UiTheme.S(44), "AUTOMATION", new[]
            {
                Mk("ManageEnergy", "Energy", () => Settings.ManageEnergy, v => Settings.ManageEnergy = v),
                Mk("ManageMagic", "Magic", () => Settings.ManageMagic, v => Settings.ManageMagic = v),
                Mk("ManageR3", "R3", () => Settings.ManageR3, v => Settings.ManageR3 = v),
                Mk("ManageGear", "Advisor gear refresh", () => Settings.ManageGear, v => Settings.ManageGear = v),
                Mk("ManageDiggers", "Diggers", () => Settings.ManageDiggers, v => Settings.ManageDiggers = v),
                Mk("ManageBeards", "Beards", () => Settings.ManageBeards, v => Settings.ManageBeards = v),
                Mk("ManageWandoos", "Wandoos", () => Settings.ManageWandoos, v => Settings.ManageWandoos = v),
                Mk("ManageNGUDiff", "NGU Diff", () => Settings.ManageNGUDiff, v => Settings.ManageNGUDiff = v),
                Mk("ManageConsumables", "Consumables", () => Settings.ManageConsumables, v => Settings.ManageConsumables = v),
            });

            int y1 = Build(x1, UiTheme.S(44), "AUTO", new[]
            {
                Mk("AutoFight", "Fight Bosses", () => Settings.AutoFight, v => Settings.AutoFight = v),
                Mk("AutoRebirth", "Rebirth", () => Settings.AutoRebirth, v => Settings.AutoRebirth = v),
                Mk("AutoConvertBoosts", "Convert Boosts", () => Settings.AutoConvertBoosts, v => Settings.AutoConvertBoosts = v),
                Mk("AutoTitanGold", "Titan Gold", () => Settings.AutoTitanGold, v => Settings.AutoTitanGold = v),
                Mk("UpgradeDiggers", "Digger Upgrades", () => Settings.UpgradeDiggers, v => Settings.UpgradeDiggers = v),
                Mk("AutoBuyEM", "Buy E/M (EXP)", () => Settings.AutoBuyEM, v => Settings.AutoBuyEM = v),
                Mk("AutoBuyAdventure", "Buy Adventure (EXP)", () => Settings.AutoBuyAdventure, v => Settings.AutoBuyAdventure = v),
                Mk("Autosave", "Daily Save", () => Settings.Autosave, v => Settings.Autosave = v),
                Mk("AutoBuyConsumables", "Buy Consumables", () => Settings.AutoBuyConsumables, v => Settings.AutoBuyConsumables = v),
                Mk("ConsumeIfAlreadyRunning", "Consume Mid-Run", () => Settings.ConsumeIfAlreadyRunning, v => Settings.ConsumeIfAlreadyRunning = v),
            });

            int y2 = Build(x2, UiTheme.S(44), "SWAP GEAR FOR", new[]
            {
                Mk("SwapTitanLoadouts", "Titans", () => Settings.SwapTitanLoadouts, v => Settings.SwapTitanLoadouts = v),
                Mk("SwapTitanDiggers", "Titan Diggers", () => Settings.SwapTitanDiggers, v => Settings.SwapTitanDiggers = v),
                Mk("SwapTitanBeards", "Titan Beards", () => Settings.SwapTitanBeards, v => Settings.SwapTitanBeards = v),
            });

            // COMBAT + ITOPOD is GONE, heading and all: every one of its five rows (Adventure Combat,
            // Snipe Boss Only, Beast Mode, Target ITOPOD, ITOPOD Auto-Push) is owned by AdventurePanel,
            // so the group emptied completely. An empty heading is worse than no heading. MISC now heads
            // this column at the same y as its neighbours.
            int y4 = Build(x3, UiTheme.S(44), "MISC", new[]
            {
                Mk("DisableOverlay", "Disable Overlay", () => Settings.DisableOverlay, v => Settings.DisableOverlay = v),
                // Wide Layout toggle retired: the M1 Control Room window has ONE designed size.
            });
            y4 = MkDouble(x3, y4 + UiTheme.S(2), "DiggerCap", "MISC", "Digger cap %", () => Settings.DiggerCap, v => Settings.DiggerCap = Math.Max(0, Math.Min(100, v)));
            var folderBtn = new Button { Text = "Settings Folder", Size = new Size(UiTheme.S(140), UiTheme.SCtl(26)), Location = new Point(x3, y4 + UiTheme.S(2)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(folderBtn);
            folderBtn.Click += (s, e) => { try { System.Diagnostics.Process.Start(GetSettingsDir()); } catch (Exception ex) { LogDebug($"Settings folder: {ex.Message}"); } };
            Controls.Add(folderBtn);
            Reg("SettingsFolder", "MISC", folderBtn);
            y4 += UiTheme.S(36);

            // Unload, re-homed from the retired General page. Safety-gated exactly like the
            // legacy pair: the button only arms while the checkbox is ticked.
            var unloadSafety = new ScaledCheckBox { Text = "Arm unload", AutoSize = true, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(x3, y4 + UiTheme.S(6)) };
            var unloadBtn = new Button { Text = "Unload Advisor", Size = new Size(UiTheme.S(140), UiTheme.SCtl(26)), Location = new Point(x3, y4 + UiTheme.S(30)), Font = UiTheme.Ui, Enabled = false };
            UiTheme.StyleFlat(unloadBtn);
            unloadSafety.CheckedChanged += (s, e) => unloadBtn.Enabled = unloadSafety.Checked;
            unloadBtn.Click += (s, e) => { try { Loader.Unload(); } catch (Exception ex) { LogDebug($"Unload: {ex.Message}"); } };
            Controls.Add(unloadSafety);
            Controls.Add(unloadBtn);
            // ONE GUARDED SURFACE, TWO CONTROLS — and this registration is what makes the safety survive
            // search. The arm checkbox and the button are inseparable: any future layout shows both or
            // neither, so there is no reachable state in which a lone "Unload Advisor" button exists to be
            // clicked. The enablement wiring above is untouched, and no layout pass may write .Enabled or
            // .Checked, so restoring the grid can never arm or disarm it.
            Reg("UnloadAdvisor", "MISC", unloadSafety, unloadBtn);
            y4 += UiTheme.S(66);

            // Blood inputs moved to Systems › Blood (advisor status + manual thresholds together).
            // Four columns now, not five: COMBAT + ITOPOD emptied out in 7.6B and took its heading with it.
            int bottomY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y4)) + UiTheme.S(14);

            // ---- ALWAYS EQUIP ----
            // A FIFTH heading, and deliberately not a fifth column: the pinned list is a list, not a
            // checkbox, so it takes a full-width band below the grid rather than being squeezed into the
            // MISC stack. The four captions above remain the column contract; this one is a band.
            var pinsHeader = new Label
            {
                Text = "ALWAYS EQUIP",
                Location = new Point(x0, bottomY),
                AutoSize = true,
                Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground
            };
            Controls.Add(pinsHeader);
            _groups.Add(new SettingGroup("ALWAYS EQUIP", pinsHeader));   // exists before Reg, so Reg can find it
            var pins = BuildPins(x0, bottomY + UiTheme.S(24));
            Controls.Add(pins);
            // ONE SURFACE, ONE CONTROL — the band is a container, so the filter moves it whole and there is
            // no way for the list to appear without its buttons, or a button without the list it edits.
            Reg("PinnedGearIds", "ALWAYS EQUIP", pins);
            bottomY = pins.Bottom + UiTheme.S(14);

            int footerY = bottomY + UiTheme.S(8);
            var footer = new Label
            {
                Text = "Detailed options (loadout IDs, zones, thresholds, priorities) live in the tabs to the right.",
                Location = new Point(UiTheme.S(20), footerY),
                AutoSize = true,
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground
            };
            Controls.Add(footer);
            _chrome.Add(footer);   // not a surface, not a heading — but it still has to land back here

            // The panel no longer scrolls, so its HEIGHT is now load-bearing: whatever is past it is
            // simply clipped. Derive it from the same arithmetic that placed the content — bottomY is
            // already the lowest of the five columns (including Reload / Arm unload / Unload in the x3
            // stack) — plus the footer line and the bottom margin. Never a magic number: add a control
            // below and this follows it. LinePitch, not Font.Height: this Mono renders a 9pt AutoSize
            // label ~25px tall, and the 96-DPI guess would clip the footer.
            Height = footerY + UiTheme.LinePitch + UiTheme.S(14);

            // SNAPSHOT, NOT RE-DERIVATION — and the choice matters.
            //
            // The alternative was to extract the placement arithmetic into a RelayoutFull() and re-run it.
            // But that arithmetic is not a function: it is thirty lines of the constructor, interleaved with
            // control CREATION (Build returns a y that MkDouble consumes, which folderBtn consumes, which
            // the unload stack consumes), and re-deriving it would mean maintaining a second copy that has
            // to agree with the first forever. A copy that can drift is a copy that will.
            //
            // The accepted grid is not something to be recomputed — it is something that ALREADY HAPPENED,
            // right here, a few lines ago, and was signed off at 370px. So record it. Whatever the
            // constructor produced IS the contract, by definition, and restoration cannot disagree with it
            // because it never recalculates it.
            CaptureGrid();
            AuditAgainstCatalogue();
            RestoreFullGrid();   // a no-op today, on purpose: 6C4C2's restore path is EXERCISED from day one
        }

        // Record the accepted grid: every control's bounds, every heading's bounds, the panel height.
        // Visibility is not recorded because in the grid there is nothing to record — everything is visible,
        // so restoration simply asserts that.
        private void CaptureGrid()
        {
            foreach (var s in _surfaces)
            {
                s.GridBounds = new Rectangle[s.Controls.Length];
                for (int i = 0; i < s.Controls.Length; i++) s.GridBounds[i] = s.Controls[i].Bounds;
            }
            foreach (var g in _groups) g.HeaderBounds = g.Header.Bounds;

            _chromeBounds = new Rectangle[_chrome.Count];
            for (int i = 0; i < _chrome.Count; i++) _chromeBounds[i] = _chrome[i].Bounds;

            _gridHeight = Height;
        }

        // Put the accepted grid back, exactly. Touches BOUNDS and VISIBLE and nothing else — never .Checked,
        // never .Enabled, never a value — so no CheckedChanged fires, no Click is synthesised, the unload arm
        // state survives untouched, and no control is destroyed or rebuilt. The canonical objects never move
        // parent and never die; they are simply told where they live.
        private void RestoreFullGrid()
        {
            try
            {
                foreach (var g in _groups)
                {
                    if (g.Header.Bounds != g.HeaderBounds) g.Header.Bounds = g.HeaderBounds;
                    if (!g.Header.Visible) g.Header.Visible = true;
                }

                foreach (var s in _surfaces)
                    for (int i = 0; i < s.Controls.Length; i++)
                    {
                        var c = s.Controls[i];
                        if (c.Bounds != s.GridBounds[i]) c.Bounds = s.GridBounds[i];
                        if (!c.Visible) c.Visible = true;
                    }

                for (int i = 0; i < _chrome.Count; i++)
                {
                    var c = _chrome[i];
                    if (c.Bounds != _chromeBounds[i]) c.Bounds = _chromeBounds[i];
                    if (!c.Visible) c.Visible = true;
                }

                if (Height != _gridHeight) Height = _gridHeight;
            }
            catch (Exception e) { LogDebug($"BasicSettings restore: {e.Message}"); }
        }

        // 28 <-> 28, checked against the real catalogue at construction. LOGS a mismatch, never throws —
        // the same practice SystemIndexPanel uses for its provider/entry completeness checks. A settings
        // panel that refuses to open because an audit is unhappy would be a worse bug than the one it found.
        private void AuditAgainstCatalogue()
        {
            try
            {
                int catalogue = 0;
                foreach (var e in SettingsIndex.All)
                {
                    if (e.Kind != EntryKind.Setting) continue;
                    catalogue++;
                    if (!_byId.ContainsKey(e.Id))
                        LogDebug($"BasicSettings: catalogue setting {e.Id} has no registered surface");
                }
                foreach (var s in _surfaces)
                    if (SettingsIndex.ById(s.Id) == null)
                        LogDebug($"BasicSettings: surface {s.Id} has no catalogue entry");

                if (_surfaces.Count != catalogue)
                    LogDebug($"BasicSettings: {_surfaces.Count} registered surface(s) vs {catalogue} catalogue setting(s)");
            }
            catch (Exception e) { LogDebug($"BasicSettings audit: {e.Message}"); }
        }

        // Setting ID -> the one canonical surface that owns it. Deliberately not public, and it hands back
        // the surface, not the controls.
        private SettingSurface Surface(string settingId)
        {
            SettingSurface s;
            return _byId.TryGetValue(settingId, out s) ? s : null;
        }

        // ---- search (slice 7.6C4C2) ----

        // Filtered-list metrics. ListX is x0 — the filtered column starts exactly where the AUTOMATION
        // column always did, so the left edge does not jump when a query is typed or cleared.
        private static readonly int ListX = UiTheme.S(16);
        private static readonly int ListTop = UiTheme.S(8);
        private static readonly int GroupGap = UiTheme.S(12);
        private static readonly int ResultGap = UiTheme.S(6);
        private static readonly int ListPad = UiTheme.S(8);

        // LAYOUT ONLY. Never reads a setting, never writes one, never touches .Checked or .Enabled, never
        // constructs, wraps, disposes or re-parents a control. It moves and hides the SAME objects the user
        // has always clicked — which is why a Setting result is not a representation of the canonical
        // control. It IS the canonical control, with its real binding, its real validation and its real
        // safety interlock, standing somewhere else on the page.
        public void Filter(string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    if (!Visible) Visible = true;
                    RestoreFullGrid();
                    AuditLayout(true);
                    return;
                }

                // The SAME pure matcher the index asks. No second search, no forked rules, no ranking.
                var hits = SettingsIndex.Search(query);
                var matched = new HashSet<string>();
                foreach (var e in hits)
                    if (e.Kind == EntryKind.Setting) matched.Add(e.Id);

                if (matched.Count == 0)
                {
                    // Nothing here matched, so this panel has nothing to say and says it by not being there.
                    // Hidden is EXCLUDED FROM THE DOCK PASS — zero footprint, no reserved gap (the same law
                    // the ActivityRibbon relies on). Nothing is disposed, the surface map survives intact, and
                    // the captured grid is still waiting for the next blank query.
                    if (Visible) Visible = false;
                    return;
                }

                LayoutFiltered(matched);
                if (!Visible) Visible = true;
                AuditLayout(false);
            }
            catch (Exception e) { LogDebug($"BasicSettings filter: {e.Message}"); }
        }

        // SILENT WHEN CORRECT. It runs on every query — which is every keystroke — so it says nothing at all
        // unless something is actually wrong, and then it says exactly what. A log line per keypress would
        // bury the one line that mattered.
        private void AuditLayout(bool grid)
        {
            try
            {
                var live = new List<Control>();

                foreach (var s in _surfaces)
                {
                    // A multi-control surface is a unit: half of DiggerCap on screen is a naked textbox, and
                    // half of Unload is the failure mode the whole surface model exists to prevent.
                    int vis = 0;
                    foreach (var c in s.Controls) if (c.Visible) vis++;
                    if (vis != 0 && vis != s.Controls.Length)
                        LogDebug($"UI AUDIT [Settings]: {s.Id} partially visible ({vis}/{s.Controls.Length})");

                    for (int i = 0; i < s.Controls.Length; i++)
                    {
                        var c = s.Controls[i];
                        if (!c.Visible) continue;
                        live.Add(c);
                        if (grid && c.Bounds != s.GridBounds[i])
                            LogDebug($"UI AUDIT [Settings]: {s.Id} control {i} at {c.Bounds}, snapshot {s.GridBounds[i]}");
                    }
                }

                foreach (var g in _groups)
                {
                    int vis = 0;
                    foreach (var s in g.Surfaces)
                        if (s.Controls[0].Visible) vis++;
                    if (g.Header.Visible && vis == 0)
                        LogDebug($"UI AUDIT [Settings]: heading {g.Caption} visible with no members");
                    if (!g.Header.Visible && vis > 0)
                        LogDebug($"UI AUDIT [Settings]: heading {g.Caption} hidden with {vis} visible member(s)");
                    if (g.Header.Visible) live.Add(g.Header);
                }
                foreach (var c in _chrome) if (c.Visible) live.Add(c);

                foreach (var c in live)
                {
                    if (c.Right > ClientSize.Width)
                        LogDebug($"UI AUDIT [Settings]: '{Desc(c)}' right={c.Right} past width {ClientSize.Width}");
                    if (c.Bottom > Height)
                        LogDebug($"UI AUDIT [Settings]: '{Desc(c)}' bottom={c.Bottom} past height {Height}");
                }

                for (int i = 0; i < live.Count; i++)
                    for (int j = i + 1; j < live.Count; j++)
                        if (UiLayout.Overlaps(live[i], live[j]))
                            LogDebug($"UI AUDIT [Settings]: '{Desc(live[i])}' overlaps '{Desc(live[j])}'");
            }
            catch (Exception e) { LogDebug($"BasicSettings layout audit: {e.Message}"); }
        }

        private static string Desc(Control c) => string.IsNullOrEmpty(c.Text) ? c.GetType().Name : c.Text;

        // The union of a surface's controls in the accepted grid — its FRAME. Placement moves the whole frame
        // and every control keeps its size and its offset within it, which is what holds "Digger cap %" beside
        // its box and the Unload button under its arm checkbox. A surface is moved, never disassembled.
        private static Rectangle Frame(SettingSurface s)
        {
            var r = s.GridBounds[0];
            for (int i = 1; i < s.GridBounds.Length; i++) r = Rectangle.Union(r, s.GridBounds[i]);
            return r;
        }

        // Place one surface with its top-left at (x, y); return how far the next row must advance.
        //
        // The advance is derived from the surface's own frame, not from its TYPE — no `if (id == "DiggerCap")`
        // anywhere. A checkbox is short and lands on the grid's own 24px rhythm (RowStep is the floor); a
        // button is taller and takes what it needs; Unload's two-control stack is ~50px and takes that. The
        // geometry is a fact about the surface, already measured by Mono and captured, so the list cannot
        // develop opinions about individual settings.
        private int Place(SettingSurface s, int x, int y)
        {
            var f = Frame(s);
            int dx = x - f.X, dy = y - f.Y;

            for (int i = 0; i < s.Controls.Length; i++)
            {
                var b = s.GridBounds[i];
                var nb = new Rectangle(b.X + dx, b.Y + dy, b.Width, b.Height);
                var c = s.Controls[i];
                if (c.Bounds != nb) c.Bounds = nb;
                if (!c.Visible) c.Visible = true;
            }
            return Math.Max(RowStep, f.Height + ResultGap);
        }

        private void Hide(SettingSurface s)
        {
            foreach (var c in s.Controls)
                if (c.Visible) c.Visible = false;
        }

        // ORDER IS THE PANEL'S, MEMBERSHIP IS THE MATCHER'S. The catalogue decides WHICH settings matched;
        // where they appear is decided here, by the registered group order and the row order inside each
        // group — the same order the grid has always used. A search for "digger" therefore reads
        // AUTOMATION > AUTO > SWAP GEAR FOR > MISC, which is where the user's memory of the panel already
        // is, rather than whatever sequence the matcher happened to walk.
        private void LayoutFiltered(HashSet<string> matched)
        {
            int y = ListTop;

            // The master first if it matched, and with no invented heading over it: it is not IN a group,
            // and fabricating a MASTER caption to make the list look uniform would be exactly the kind of
            // tidy lie this migration keeps refusing.
            var master = Surface("Setting.GlobalEnabled");
            if (master != null)
            {
                if (matched.Contains(master.Id))
                {
                    y += Place(master, ListX, y);
                    y += GroupGap;
                }
                else Hide(master);
            }

            foreach (var g in _groups)
            {
                int hits = 0;
                foreach (var s in g.Surfaces)
                    if (matched.Contains(s.Id)) hits++;

                if (hits == 0)
                {
                    if (g.Header.Visible) g.Header.Visible = false;
                    foreach (var s in g.Surfaces) Hide(s);
                    continue;
                }

                var hp = new Point(ListX, y);
                if (g.Header.Location != hp) g.Header.Location = hp;
                if (!g.Header.Visible) g.Header.Visible = true;
                y += UiTheme.HeadPitch;

                foreach (var s in g.Surfaces)
                {
                    if (matched.Contains(s.Id)) y += Place(s, ListX, y);
                    else Hide(s);
                }
                y += GroupGap;
            }

            // The footer advertises the tabs to the right. It is chrome, not a result, and in a result list
            // it is noise that also costs a line — so it stands down until the grid comes back.
            foreach (var c in _chrome)
                if (c.Visible) c.Visible = false;

            int h = y + ListPad;
            if (Height != h) Height = h;
        }

        // ---- pinned items (Settings.PinnedGearIds) ----

        private ListBox _pinsList;
        private Label _pinsStatus;
        private Button _pinsPaste, _pinsCopy, _pinsClear, _pinsUndo;
        private Action _pinsSync;
        private int _pinsRowY;
        // The list as this panel last rendered it. A settings reload is only a FOREIGN change when it
        // disagrees with this — which is what separates "someone edited settings.json" from the echo of
        // our own save.
        private int[] _pinsKnown = new int[0];

        // SINGLE-LEVEL UNDO, same contract as the profile editor's paste (ui-panels.md:65) and the same
        // mechanism: PLAIN DATA, no timer. These windows have no WinForms message pump — WM_TIMER never
        // arrives — so the DEADLINE is the only authority and is evaluated when the user reaches for the
        // button, never on a schedule (GearEditorPanel.cs:178).
        private const int PinsUndoSeconds = 20;
        private List<int> _pinsUndoIds;
        private DateTime _pinsUndoDeadline;

        private static int[] CurrentPins() => Settings?.PinnedGearIds ?? new int[0];

        // The band: a note, the list, and the paste/copy/clear/undo row. Sized from what it holds — the
        // list in ROWS (a pixel height means a different row count at every scale), the buttons from
        // BtnWidth, and the container from where its children actually end.
        private Panel BuildPins(int x, int y)
        {
            // S(700), not S(560): the status line below carries variable prose (paste refusals run to ~300px
            // at the tuning baseline) and it has to fit BESIDE nothing — it gets its own line — but the note
            // and the list want the room too. The canvas is ~1030 wide and x0 is S(16), so this is well
            // inside it.
            int w = UiTheme.S(700);
            var host = new Panel { Location = new Point(x, y), Width = w, BackColor = UiTheme.Ground };

            var note = new Label
            {
                Text = "Kept in every optimized loadout, ahead of the objective's own picks.",
                Location = new Point(0, 0),
                AutoSize = true,
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground
            };
            host.Controls.Add(note);

            _pinsList = new ListBox
            {
                Location = new Point(0, UiTheme.LinePitch),
                Size = new Size(w, UiTheme.ListH(4)),
                Font = UiTheme.Ui,
                BorderStyle = BorderStyle.FixedSingle,
                SelectionMode = SelectionMode.None
            };
            UiTheme.StyleList(_pinsList);
            host.Controls.Add(_pinsList);

            _pinsPaste = new Button { Text = "Paste IDs", Width = UiLayout.BtnWidth("Paste IDs"), Height = UiTheme.SCtl(24), Font = UiTheme.Ui };
            _pinsCopy = new Button { Text = "Copy IDs", Width = UiLayout.BtnWidth("Copy IDs"), Height = UiTheme.SCtl(24), Font = UiTheme.Ui };
            _pinsClear = new Button { Text = "Clear", Width = UiLayout.BtnWidth("Clear"), Height = UiTheme.SCtl(24), Font = UiTheme.Ui };
            _pinsUndo = new Button { Text = "Undo paste", Width = UiLayout.BtnWidth("Undo paste") + UiTheme.S(6), Height = UiTheme.SCtl(24), Font = UiTheme.Ui, Visible = false };
            UiTheme.StyleFlat(_pinsPaste); UiTheme.StyleFlat(_pinsCopy); UiTheme.StyleFlat(_pinsClear);
            UiTheme.StyleGhost(_pinsUndo);
            // OWN LINE, and NOT AutoSize. Trailing the button row it started at x≈335 in a 560px band while
            // the refusal strings measure up to ~304px — the ~79px cut off was exactly the text explaining
            // why a paste was refused, and it read as a PAST PARENT EDGE audit line too. On its own line,
            // full band width, it goes through FitOrGrow: it word-wraps rather than ellipsizing.
            _pinsStatus = new Label
            {
                AutoSize = false,
                Width = w,
                Height = UiTheme.SText(20),
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Faint,
                BackColor = UiTheme.Ground
            };

            _pinsPaste.Click += (s, e) => PastePins();
            _pinsCopy.Click += (s, e) => CopyPins();
            _pinsClear.Click += (s, e) =>
            {
                var before = new List<int>(CurrentPins());
                if (before.Count == 0 || !ApplyPins(new List<int>())) return;
                SetPinsUndo(before);
                RefreshPins($"Cleared — undo for {PinsUndoSeconds}s.");
            };
            // Reaching for the button is the moment to find out the window has closed, so the dead
            // affordance clears itself before it can be clicked; the click re-checks anyway.
            _pinsUndo.MouseEnter += (s, e) => ExpirePinsUndoIfStale();
            _pinsUndo.Click += (s, e) => UndoPins();

            foreach (var c in new Control[] { _pinsPaste, _pinsCopy, _pinsClear, _pinsUndo, _pinsStatus }) host.Controls.Add(c);
            _pinsRowY = _pinsList.Bottom + UiTheme.S(6);
            _pinsStatus.Location = new Point(0, LayoutPinsRow() + UiTheme.S(2));

            // The band's height is FIXED by construction, not re-derived after a paste: the status is
            // reserved TWO text lines whatever FitOrGrow does with it (FitOrGrow caps at maxLines = 2, so
            // its tallest result is 2*LinePitch - S(4) and always lands inside). A band that resized itself
            // later would contradict the grid snapshot CaptureGrid takes a few lines from here.
            host.Height = _pinsStatus.Top + UiTheme.SLines(2, 0) + UiTheme.S(4);

            // A settings reload that did NOT change the list must leave both the status message and a live
            // undo token alone — UpdateFromSettings runs up to once a second (Main.cs:418-424), and our own
            // save flags it, so an unconditional refresh would wipe the message we just wrote. A reload that
            // DID change the list is a foreign edit, and then the snapshot is stale: same rule the gear card
            // enforces with ClearUndo() on every manual edit.
            _pinsSync = () =>
            {
                if (SameIds(CurrentPins(), _pinsKnown)) return;
                ClearPinsUndo();
                RefreshPins();
            };
            RefreshPins();
            return host;
        }

        private static bool SameIds(int[] a, int[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // Measured left-to-right (overlap impossible by construction), rerun whenever the undo button
        // appears or disappears so the row closes up instead of leaving a hole. Returns the row's bottom;
        // the status label sits BELOW it, never in it.
        private int LayoutPinsRow()
        {
            var items = new List<Control> { _pinsPaste, _pinsCopy, _pinsClear };
            if (_pinsUndo.Visible) items.Add(_pinsUndo);
            return UiLayout.Row(0, _pinsRowY, UiTheme.S(8), items.ToArray());
        }

        private void RefreshPins(string message = null)
        {
            try
            {
                var ids = CurrentPins();
                _pinsKnown = (int[])ids.Clone();   // what WE last rendered — the change detector's baseline
                _pinsList.BeginUpdate();
                _pinsList.Items.Clear();
                foreach (var id in ids) _pinsList.Items.Add($"{id}  —  {ItemName(id)}");
                _pinsList.EndUpdate();
                UiLayout.FitOrGrow(_pinsStatus, message ?? $"{ids.Length} pinned item(s)");
                LayoutPinsRow();
            }
            catch (Exception e) { LogDebug($"Pinned items refresh: {e.Message}"); }
        }

        // The single mutation path — paste, clear and undo all go through it, so they cannot diverge.
        private bool ApplyPins(List<int> ids)
        {
            if (Settings == null) return false;
            try { Settings.PinnedGearIds = ids.ToArray(); return true; }
            catch (Exception e) { LogDebug($"Pinned items apply: {e.Message}"); return false; }
        }

        // Reuses the profile editor's paste contract verbatim (GearEditorPanel.AskPaste): parse+validate
        // first, preview the result, change nothing on invalid, empty or cancelled input. No clamp
        // disclosure — this list has no NumericUpDown ceiling to clamp against.
        private void PastePins()
        {
            var before = new List<int>(CurrentPins());
            string message;
            var ids = GearEditorPanel.AskPaste(before.Count, "Paste pinned item IDs", 0, out message);
            if (ids == null)
            {
                if (message != null) RefreshPins(message);   // Cancel is a true no-op and says nothing
                return;
            }
            if (!ApplyPins(ids)) { ClearPinsUndo(); RefreshPins("Could not save — see debug.log."); return; }
            SetPinsUndo(before);
            RefreshPins($"Pinned {ids.Count} ID(s) — undo for {PinsUndoSeconds}s.");
        }

        private void CopyPins()
        {
            try
            {
                Clipboard.SetText(string.Join(", ", Array.ConvertAll(CurrentPins(), i => i.ToString())));
                RefreshPins("Copied IDs to clipboard.");
            }
            catch (Exception e) { LogDebug($"Pinned items copy: {e.Message}"); }
        }

        private void SetPinsUndo(List<int> before)
        {
            _pinsUndoIds = before;
            _pinsUndoDeadline = DateTime.UtcNow.AddSeconds(PinsUndoSeconds);
            _pinsUndo.Visible = true;
            LayoutPinsRow();
        }

        private void ClearPinsUndo()
        {
            _pinsUndoIds = null;
            if (_pinsUndo.Visible) { _pinsUndo.Visible = false; LayoutPinsRow(); }
        }

        // Lazy expiry: nothing schedules, so the deadline is evaluated whenever the user comes near the
        // control. Returns true if the token was expired and cleared.
        private bool ExpirePinsUndoIfStale()
        {
            if (_pinsUndoIds == null || DateTime.UtcNow <= _pinsUndoDeadline) return false;
            ClearPinsUndo();
            RefreshPins("Undo window closed — the change stands.");
            return true;
        }

        private void UndoPins()
        {
            if (_pinsUndoIds == null) return;
            if (ExpirePinsUndoIfStale()) return;
            var snapshot = _pinsUndoIds;
            ClearPinsUndo();               // consume first: single level, never reusable
            if (!ApplyPins(snapshot)) { RefreshPins("Undo failed — see debug.log."); return; }
            RefreshPins($"Undone — {snapshot.Count} pinned item(s) restored.");
        }

        // A measured label+numeric pair for NumRow (inline horizontal layout).
        private Control[] MkPair(string label, int min, int max, Func<int> get, Action<int> set)
        {
            var l = new Label { Text = label, AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            var n = new NumericUpDown { Width = UiTheme.S(66), Minimum = min, Maximum = max, Font = UiTheme.Ui };
            UiTheme.StyleNum(n);
            n.ValueChanged += (s, e) =>
            {
                if (_syncing || Settings == null) return;
                try { set((int)n.Value); } catch (Exception ex) { LogDebug($"Basic num '{label}': {ex.Message}"); }
            };
            _numSyncs.Add(() =>
            {
                int v;
                try { v = Math.Max(min, Math.Min(max, get())); } catch { return; }
                if ((int)n.Value != v) n.Value = v;
            });
            return new Control[] { l, n };
        }

        private int NumRow(int x, int y, params Control[][] pairs)
        {
            var flat = new List<Control>();
            foreach (var p in pairs)
                foreach (var c in p) { Controls.Add(c); flat.Add(c); }
            return UiLayout.Row(x, y, UiTheme.S(8), flat.ToArray());
        }

        // Label + NumericUpDown pair; returns the y below the row. Synced via _numSyncs.
        private int MkNum(int x, int y, string label, int min, int max, Func<int> get, Action<int> set)
        {
            var l = new Label { Text = label, AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(x, y + UiTheme.S(4)) };
            Controls.Add(l);
            var n = new NumericUpDown { Width = UiTheme.S(74), Minimum = min, Maximum = max, Font = UiTheme.Ui, Location = new Point(x + UiTheme.S(104), y) };
            UiTheme.StyleNum(n);
            n.ValueChanged += (s, e) =>
            {
                if (_syncing || Settings == null) return;
                try { set((int)n.Value); } catch (Exception ex) { LogDebug($"Basic num '{label}': {ex.Message}"); }
            };
            Controls.Add(n);
            _numSyncs.Add(() =>
            {
                int v;
                try { v = Math.Max(min, Math.Min(max, get())); } catch { return; }
                if ((int)n.Value != v) n.Value = v;
            });
            return y + UiTheme.S(28);
        }

        // Label + TextBox for doubles (scientific notation accepted, e.g. 1.5E+8).
        //
        // ONE SURFACE, TWO CONTROLS. The label is not decoration — "Digger cap %" is the only thing that
        // says what the number means, so a search result that showed the box alone would be a naked
        // textbox. They show, hide and move together or the surface is a lie. Commit-on-Leave, invariant
        // parsing, the silent ignore of unparseable text and the 0-100 clamp all stay exactly as they are:
        // this registers the pair, it does not touch how they behave.
        private int MkDouble(int x, int y, string id, string group, string label, Func<double> get, Action<double> set)
        {
            var l = new Label { Text = label, AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(x, y + UiTheme.S(4)) };
            Controls.Add(l);
            var t = new TextBox { Width = UiTheme.S(90), Font = UiTheme.Ui, Location = new Point(x + UiTheme.S(104), y) };
            Reg(id, group, l, t);
            t.Leave += (s, e) =>
            {
                if (_syncing || Settings == null) return;
                if (double.TryParse(t.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    try { set(v); } catch (Exception ex) { LogDebug($"Basic double '{label}': {ex.Message}"); }
            };
            Controls.Add(t);
            _numSyncs.Add(() =>
            {
                double v;
                try { v = get(); } catch { return; }
                string txt = v == 0 ? "" : v >= 1e6 ? v.ToString("#.###E+0") : v.ToString("0.##");
                if (!t.Focused && t.Text != txt) t.Text = txt;
            });
            return y + UiTheme.S(28);
        }

        // The KeyValuePair's key used to be the label and nothing read it. It is now the catalogue ID, which
        // is the one piece of information the panel was missing: Build registers each box under it.
        private KeyValuePair<string, Bind> Mk(string id, string label, Func<bool> get, Action<bool> set)
        {
            var cb = new ScaledCheckBox { Text = label, AutoSize = true, ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground };
            var bind = new Bind { Box = cb, Get = get };
            cb.CheckedChanged += (s, e) =>
            {
                if (_syncing || Settings == null) return;
                try { set(cb.Checked); }
                catch (Exception ex) { LogDebug($"Basic settings toggle '{label}': {ex.Message}"); }
            };
            _binds.Add(bind);
            return new KeyValuePair<string, Bind>(id, bind);
        }

        // Registers one logical surface: one ID, one group, and the one-or-more REAL controls that are it.
        private SettingSurface Reg(string id, string group, params Control[] controls)
        {
            var s = new SettingSurface("Setting." + id, group, controls);
            _surfaces.Add(s);
            if (_byId.ContainsKey(s.Id)) LogDebug($"BasicSettings: duplicate surface id {s.Id}");
            else _byId[s.Id] = s;

            if (group != null)
                foreach (var g in _groups)
                    if (g.Caption == group) { g.Surfaces.Add(s); break; }
            return s;
        }

        private int Build(int x, int y, string caption, KeyValuePair<string, Bind>[] items)
        {
            var header = new Label
            {
                Text = caption,
                Location = new Point(x, y),
                AutoSize = true,
                Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground
            };
            Controls.Add(header);
            _groups.Add(new SettingGroup(caption, header));   // exists before Reg, so Reg can find it

            y += UiTheme.S(24);
            foreach (var it in items)
            {
                it.Value.Box.Location = new Point(x, y);
                Controls.Add(it.Value.Box);
                Reg(it.Key, caption, it.Value.Box);
                y += RowStep;
            }
            return y;
        }

        // Pull current values from Settings (called on load and whenever settings reload from disk).
        public void Sync()
        {
            if (Settings == null) return;
            _syncing = true;
            try
            {
                foreach (var b in _binds)
                {
                    bool v;
                    try { v = b.Get(); } catch { continue; }
                    if (b.Box.Checked != v) b.Box.Checked = v;
                }
                foreach (var ns in _numSyncs)
                    try { ns(); } catch { }
                // Not a _numSyncs tenant: the pinned list is rebuilt, not clamped, and a settings reload
                // from disk is exactly when it has to catch up.
                if (_pinsSync != null) try { _pinsSync(); } catch { }
                bool on = Settings.GlobalEnabled;
                _master.Text = on ? "ADVISOR ACTIVE" : "ADVISOR PAUSED";
                UiTheme.ApplyState(_master, on ? UiTheme.Cap : UiTheme.Danger, Color.White);
            }
            finally { _syncing = false; }
        }
    }
}
