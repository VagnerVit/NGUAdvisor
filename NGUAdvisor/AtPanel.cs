using System;
using System.Drawing;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Economy > PLANNERS — the read-only port of iboj88's AT Calculator: where each slot's
    // blitz-boost ceiling sits at the energy it is holding right now, when a level target lands, how
    // much cap it would take to blitz-boost for a whole hour or a whole day, and the sheet's Time
    // Machine tab.
    //
    // NOTHING HERE FEEDS ENERGY OR WRITES A LEVEL TARGET. AdvancedTrainingBP owns the feed and
    // LevelPlanner owns the targets; a second writer on an advice surface would be two owners for one
    // allocation, and AT levels cannot be un-bought.
    //
    // EVERY NUMBER IS ASKED OF THE GAME, NOT REDERIVED. The rate comes from
    // advancedTrainingController.getProgressPerTick(id) — the game's own progress per tick, energy,
    // energy power, AT speed bonus, wishes and all — and AtMath turns it into levels and seconds. The
    // only raw inputs read here are the ones getProgressPerTick cannot give back: the slot's baseTime,
    // needed for "what cap would blitz-boost this for an hour".
    //
    // TWO GATES COME FIRST, because either one makes every time on this panel fiction:
    //   1. updateAdvancedTraining() returns early unless basic Attack AND Defense training slot 4 are
    //      both >= 25000 — below that AT does not progress at all, whatever energy is in it.
    //   2. wishes[190] forces progressPerTick to 1f, i.e. a level every single tick regardless of
    //      energy. The energy-based figures stop applying and every level costs one tick.
    public class AtPanel : Panel
    {
        private const string Provenance = "Formulas: decompiled AdvancedTrainingController; layout after iboj88's AT Calculator.";

        // The game ticks 50/s, so one tick — and therefore one level while blitz-boosting — is 0.02s.
        private const double TickSeconds = 0.02;

        // updateAdvancedTraining()'s early-out threshold on training.attackTraining[4]/defenseTraining[4].
        private const long BasicTrainingGate = 25000;

        // The wish that pins progressPerTick at 1f.
        private const int BlitzWishId = 190;

        // 1000/0.02 in the AT Calculator's Time Machine tab is the game's own 50000 (TimeMachineBP.cs:71).
        private const double TmUnitScale = 1000.0 / TickSeconds;

        private static readonly double[] Horizons = { 3600, 86400 };
        private static readonly string[] HorizonNames = { "1h", "24h" };

        // Slot ids exactly as AtHourPlanner (0 = Toughness, 1 = Power) and AdvancedTrainingBP use them.
        private static readonly string[] SlotNames = { "Toughness", "Power", "Block", "Wandoos Energy", "Wandoos Magic" };

        // The two slots that feed adventure Power/Toughness, i.e. the only two with a GOAL: the titan
        // ladder is what "does more AT still buy progress" is measured against.
        private static readonly int[] GoalSlots = { 1, 0 };

        // The segment LevelPlanner's TM freeze lives in (LevelPlanner.cs:69-75 gates it on this).
        private const string MarathonSegment = "NGU MARATHON";

        private readonly Label _state;
        private readonly Label[] _slots;
        private readonly Label[] _goals;
        private readonly Label[] _targets;
        private readonly Label _goalNote;
        private readonly Label[] _caps;
        private readonly Label _capNote;
        private readonly Label _tmDemand;
        private readonly Label _tmRate;
        private readonly Label _tmFreeze;
        private readonly Label _tmEnergy;
        private readonly Label _tmMagic;
        private readonly Label _tmNote;

        public int ContentHeight { get; private set; }

        // canvasW: explicit canvas width when hosted in a section column (0 = UiLayout.PanelW).
        public AtPanel(int canvasW = 0)
        {
            int W = canvasW > 0 ? canvasW : UiLayout.PanelW;
            int inner = W - UiTheme.S(20);
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            Label head = Header("ADVANCED TRAINING", UiTheme.S(10), UiTheme.S(10));
            Controls.Add(head);

            // Two lines reserved: the locked gate and the blitz wish can both be true at once, and the
            // whole point of this line is that neither ever goes unsaid.
            _state = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.SLines(2)), Font = UiTheme.Bold,
                ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), UiTheme.S(10) + UiTheme.HeadPitch)
            };
            Controls.Add(_state);

            Label slotsHead = Header("SLOTS", UiTheme.S(10), _state.Bottom + UiTheme.S(8));
            Controls.Add(slotsHead);

            // All five slots always render. The unfed ones say so on their own line rather than
            // vanishing: a slot silently missing from a five-row readout reads as "there is no such
            // slot", and a row count that changes with the feed cannot have a derived height.
            _slots = new Label[SlotNames.Length];
            int y = slotsHead.Bottom + UiTheme.S(4);
            for (int i = 0; i < SlotNames.Length; i++)
            {
                _slots[i] = ValueLine(inner, y);
                Controls.Add(_slots[i]);
                y = _slots[i].Bottom + UiTheme.S(2);
            }

            Label goalHead = Header("GOAL & HELD TARGET — POWER / TOUGHNESS", UiTheme.S(10), y + UiTheme.S(6));
            Controls.Add(goalHead);

            // Two lines per slot, not one: the goal level and the target the advisor is holding answer
            // different questions ("where does AT stop buying progress" vs "what is being fed towards"),
            // and squeezed onto one line at real DPI both end up ellipsized.
            _goals = new Label[GoalSlots.Length];
            _targets = new Label[GoalSlots.Length];
            y = goalHead.Bottom + UiTheme.S(4);
            for (int i = 0; i < GoalSlots.Length; i++)
            {
                _goals[i] = ValueLine(inner, y);
                Controls.Add(_goals[i]);
                _targets[i] = ValueLine(inner, _goals[i].Bottom + UiTheme.S(2));
                Controls.Add(_targets[i]);
                y = _targets[i].Bottom + UiTheme.S(4);
            }

            _goalNote = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.SLines(2)), Font = UiTheme.Chip,
                ForeColor = UiTheme.Faint, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), y - UiTheme.S(2))
            };
            Controls.Add(_goalNote);
            UiLayout.WrapInto(_goalNote,
                "The goal level is NOT a cap: past it AT still buys a bigger number, just no progress — "
                + "the next titan stage's stats are already met there.", 2);

            Label capHead = Header("CAP TO BLITZ-BOOST", UiTheme.S(10), _goalNote.Bottom + UiTheme.S(6));
            Controls.Add(capHead);

            // Per slot, not per horizon: baseTime is a serialized field on each slot's controller (the
            // sheet hardcodes 1e7 for Power/Toughness and 2e7 for the Wandoos pair), so one shared line
            // would have to pick a slot's baseTime arbitrarily.
            _caps = new Label[SlotNames.Length];
            y = capHead.Bottom + UiTheme.S(4);
            for (int i = 0; i < SlotNames.Length; i++)
            {
                _caps[i] = ValueLine(inner, y);
                Controls.Add(_caps[i]);
                y = _caps[i].Bottom + UiTheme.S(2);
            }

            _capNote = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.SHead(18)), Font = UiTheme.Chip,
                ForeColor = UiTheme.Faint, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), y + UiTheme.S(2))
            };
            Controls.Add(_capNote);

            Label tmHead = Header("TIME MACHINE — NORMAL ONLY", UiTheme.S(10), _capNote.Bottom + UiTheme.S(6));
            Controls.Add(tmHead);

            // "Is the TM still worth feeding" DOES have a goal-based answer — TM raises GPS, GPS buys
            // digger upgrades, digger bonuses feed adventure stats, adventure stats kill titans. It is
            // just denominated in GOLD, not in levels, so it is answered with the demand flags the gold
            // consumers already expose plus the live rate.
            _tmDemand = ValueLine(inner, tmHead.Bottom + UiTheme.S(4));
            Controls.Add(_tmDemand);
            _tmRate = ValueLine(inner, _tmDemand.Bottom + UiTheme.S(2));
            Controls.Add(_tmRate);

            _tmFreeze = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.SLines(2)), Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), _tmRate.Bottom + UiTheme.S(2))
            };
            Controls.Add(_tmFreeze);

            _tmEnergy = ValueLine(inner, _tmFreeze.Bottom + UiTheme.S(4));
            Controls.Add(_tmEnergy);
            _tmMagic = ValueLine(inner, _tmEnergy.Bottom + UiTheme.S(2));
            Controls.Add(_tmMagic);

            _tmNote = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.SLines(4)), Font = UiTheme.Chip,
                ForeColor = UiTheme.Faint, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), _tmMagic.Bottom + UiTheme.S(4))
            };
            Controls.Add(_tmNote);
            // Constant text, so it is set once in the ctor and the height it grows to is part of the
            // panel's derived height rather than something a later refresh could move.
            //
            // NO gold-to-titan-stat threshold and no single "TM worth" number, deliberately: the chain
            // above is real but its exchange rate is phase-dependent, which is exactly why this repo
            // refuses a single scalar across gold / boosts / PP / EXP (docs/modules/ItopodFarmAdvisor.md
            // §Open). The demand flags plus the rate give the same decision without a conversion that
            // would be wrong for half a run.
            UiLayout.WrapInto(_tmNote,
                "Gold reaches the titan ladder only indirectly — TM raises GPS, GPS buys digger upgrades, "
                + "digger bonuses feed adventure stats — so this section shows gold demand and rate instead "
                + "of a level threshold. The AT Calculator's Evil column is marked broken by its own author, "
                + "so only the Normal column is ported. The TM speed bonuses from hacks, cards and the TM "
                + "challenge ARE applied.", 4);

            Label provenance = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.TextH), Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), _tmNote.Bottom + UiTheme.S(8))
            };
            Controls.Add(provenance);
            UiLayout.FitOrGrow(provenance, Provenance);

            // The panel does not scroll — it is hosted in a scrolling section — so its height comes from
            // its content. Anything hand-tuned here would clip the last lines at real DPI.
            ContentHeight = provenance.Bottom + UiTheme.S(10);

            VisibleChanged += (s, e) => { if (Visible) SyncFromSettings(); };
        }

        private static Label Header(string text, int x, int y) => new Label
        {
            Text = text, AutoSize = true, Font = UiTheme.ColHeader,
            ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
            Location = new Point(x, y)
        };

        private static Label ValueLine(int width, int y) => new Label
        {
            AutoSize = false, Size = new Size(width, UiTheme.SText(20)), Font = UiTheme.Ui,
            ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground,
            Location = new Point(UiTheme.S(10), y)
        };

        // Named for the deferred pass in SettingsForm.UpdateFromSettings, which runs at most once a
        // second on the Unity main thread — the only place live game reads may happen. Every failure is
        // contained here: one throwing read must not abort the rest of that pass.
        public void SyncFromSettings()
        {
            try
            {
                // A HIDDEN PANEL RENDERS NOTHING, and here that is a performance rule rather than tidiness.
                // UpdateFromSettings calls this every deferred refresh whatever page is on screen, and
                // RenderGoals reaches AtHourPlanner.GoalLevels -> OptimizationAdvisor.ProjectedBestGear,
                // which is TWO gear-optimizer runs behind a 120 s cache. Ungated, that pays for two
                // coordinate-ascent passes over the whole inventory on the Unity main thread every two
                // minutes for a page nobody is looking at. Control.Visible is false whenever any parent
                // is hidden, so this covers sitting on an unselected sub-page too.
                if (!Visible) return;

                Character c = Main.Character;
                if (c == null) return;

                bool locked = AtLocked(c);
                bool blitzWish = BlitzWishActive(c);
                RenderState(locked, blitzWish);

                double epow = Read(() => (double)c.totalEnergyPower());
                double atSpeed = Read(() => (double)c.totalAdvancedTrainingSpeedBonus());

                for (int id = 0; id < SlotNames.Length; id++)
                {
                    RenderSlot(c, id, locked, blitzWish);
                    RenderCap(c, id, locked, epow, atSpeed);
                }
                RenderGoals(c);
                UiLayout.WrapInto(_capNote,
                    $"At one level per tick ({TickSeconds:0.##}s), {HorizonNames[0]} of blitz boost is "
                    + $"{NumberFormatter.Abbrev(Horizons[0] / TickSeconds)} levels and {HorizonNames[1]} is "
                    + $"{NumberFormatter.Abbrev(Horizons[1] / TickSeconds)} — the energy above holds the ceiling there.", 1);

                RenderTimeMachine(c);
            }
            catch (Exception ex) { LogDebug($"AT panel refresh: {ex.Message}"); }
        }

        private void RenderState(bool locked, bool blitzWish)
        {
            if (locked)
            {
                _state.ForeColor = UiTheme.Danger;
                UiLayout.WrapInto(_state,
                    $"AT LOCKED — basic Attack and Defense training rank 5 must both reach {BasicTrainingGate} levels. "
                    + "Advanced Training does not progress at all below that, so no times are shown.", 2);
                return;
            }
            if (blitzWish)
            {
                _state.ForeColor = UiTheme.Cap;
                UiLayout.WrapInto(_state,
                    $"WISH {BlitzWishId} IS ACTIVE — every AT slot gains a level every tick regardless of energy, "
                    + $"so a level costs {TickSeconds:0.##}s and the energy-based ceilings below do not apply.", 2);
                return;
            }
            _state.ForeColor = UiTheme.Ink;
            UiLayout.WrapInto(_state, "AT is progressing. Levels below are live; targets come from the level planner.", 2);
        }

        private void RenderSlot(Character c, int id, bool locked, bool blitzWish)
        {
            Label row = _slots[id];
            long level = Read(() => c.advancedTraining.level[id], -1L);
            if (level < 0)
            {
                row.ForeColor = UiTheme.Faint;
                UiLayout.FitInto(row, $"{SlotNames[id]} — unavailable");
                return;
            }

            long energy = Read(() => c.advancedTraining.energy[id], 0L);
            long target = Read(() => c.advancedTraining.levelTarget[id], 0L);
            double ppt = Read(() => (double)c.advancedTrainingController.getProgressPerTick(id));
            if (double.IsNaN(ppt) || ppt < 0) ppt = 0;

            string text = $"{SlotNames[id]} — Lv {NumberFormatter.Abbrev(level)}"
                + $" · {NumberFormatter.Abbrev(energy)} energy in";

            if (locked)
            {
                row.ForeColor = UiTheme.Faint;
                UiLayout.FitInto(row, text + " · no progress while AT is locked");
                return;
            }

            if (blitzWish)
                text += " · blitz-boosting at every level";
            else
                // M/baseTime == progressPerTick * (level+1), so the ceiling is that product minus one and
                // the ratio the game would divide by collapses to 1 — hence the 1.0 second argument.
                text += $" · BB ceiling Lv {NumberFormatter.Abbrev(AtMath.BbCeiling(ppt * (level + 1.0), 1.0))}";

            row.ForeColor = energy > 0 || target != 0 ? UiTheme.Ink : UiTheme.Muted;

            if (target == -1)
            {
                // -1 is the game's own pause marker (AtHourPlanner.ReadSlot zeroes the rate for it), so
                // there is no rate to build an ETA out of and none is invented.
                UiLayout.FitInto(row, text + " · held at target -1 — no rate");
                return;
            }
            if (target <= 0)
            {
                UiLayout.FitInto(row, text + (energy > 0 ? " · uncapped" : " · not fed"));
                return;
            }
            if (level >= target)
            {
                UiLayout.FitInto(row, text + $" · target {NumberFormatter.Abbrev(target)} reached");
                return;
            }

            // The blitz wish stays a separate, flat regime: it pins progressPerTick at 1f at EVERY level,
            // so the slot never falls out of one-level-per-tick. AtMath.SecondsToTarget cannot know that
            // — from a single sample ppt == 1 looks like a slot sitting exactly on its ceiling, about to
            // slow down. Everything else is SecondsToTarget's three-branch answer: it caps the
            // blitz-boosted stretch at one level per tick before switching to the closed form.
            double? seconds = blitzWish
                ? (double?)((target - level) * TickSeconds)
                : AtMath.SecondsToTarget(level, target, ppt, TickSeconds);

            // A null ETA prints NO duration at all. A 0s or an infinity in this slot reads as a real
            // prediction, and this line's only value is that its numbers can be trusted.
            UiLayout.FitInto(row, text + $" · target {NumberFormatter.Abbrev(target)} in "
                + (seconds.HasValue ? Duration(seconds.Value) : "— no rate"));
        }

        // "Which target is the advisor holding, and up to which level does more AT still buy progress?"
        //
        // The goal levels are asked of AtHourPlanner and NEVER recomputed here: that module derives two
        // different attack references (beast mode in for the titan ladder, divided out for the zone
        // tables) and conflating them once understated attack ~1.5x. The panel is a view.
        private void RenderGoals(Character c)
        {
            bool known = AtHourPlanner.GoalLevels(c, out double atkLevel, out double defLevel, out string label);
            for (int i = 0; i < GoalSlots.Length; i++)
            {
                int id = GoalSlots[i];
                RenderGoal(_goals[i], id, known, id == 1 ? atkLevel : defLevel, label);
                RenderTarget(_targets[i], c, id);
            }
        }

        private static void RenderGoal(Label row, int id, bool known, double level, string label)
        {
            // No objective, or nothing readable to base a level on. A fabricated number here would be
            // read as "feed AT to this", so the row says it does not know instead.
            if (!known && label != AtHourPlanner.GoalMetLabel)
            {
                row.ForeColor = UiTheme.Faint;
                UiLayout.FitInto(row, $"{SlotNames[id]} — goal: unavailable");
                return;
            }
            // Either both needs are met (the planner reported it as the whole answer) or this slot's own
            // need is (NaN level). Both mean the same thing on this row.
            if (!known || double.IsNaN(level))
            {
                row.ForeColor = UiTheme.Muted;
                UiLayout.FitInto(row, $"{SlotNames[id]} — goal: {AtHourPlanner.GoalMetLabel}");
                return;
            }
            row.ForeColor = UiTheme.Ink;
            UiLayout.FitInto(row, $"{SlotNames[id]} — goal: level {NumberFormatter.Abbrev(level)} ({label})");
        }

        private static void RenderTarget(Label row, Character c, int id)
        {
            long target = Read(() => c.advancedTraining.levelTarget[id], long.MinValue);
            if (target == long.MinValue)
            {
                row.ForeColor = UiTheme.Faint;
                UiLayout.FitInto(row, $"{SlotNames[id]} — held target: unavailable");
                return;
            }

            // The game's own semantics, not levels: 0 means no cap at all and -1 is its pause marker.
            string held = target == 0 ? "uncapped"
                : target < 0 ? "paused"
                : $"level {NumberFormatter.Abbrev(target)}";
            // LevelPlanner owns these targets and says why in one string; it is empty when the auto
            // profile is off, in which case the targets are whatever the user set.
            string why = LevelPlanner.Status;
            row.ForeColor = UiTheme.Ink;
            UiLayout.FitInto(row, $"{SlotNames[id]} — held target: {held} · "
                + (string.IsNullOrEmpty(why) ? "level planner off — your own targets" : why));
        }

        private void RenderCap(Character c, int id, bool locked, double epow, double atSpeed)
        {
            Label row = _caps[id];
            double baseTime = Read(() => (double)Controller(c, id).baseTime);
            if (locked)
            {
                row.ForeColor = UiTheme.Faint;
                UiLayout.FitInto(row, $"{SlotNames[id]} — n/a while AT is locked");
                return;
            }
            if (baseTime <= 0 || epow <= 0 || atSpeed <= 0)
            {
                row.ForeColor = UiTheme.Faint;
                UiLayout.FitInto(row, $"{SlotNames[id]} — unavailable");
                return;
            }

            row.ForeColor = UiTheme.Ink;
            string text = SlotNames[id];
            for (int h = 0; h < Horizons.Length; h++)
            {
                // Holding the ceiling at level L needs energy = 50*baseTime*(L+1)/(sqrt(epow)*atSpeed),
                // and L is the level a whole horizon of one-per-tick gains reaches.
                double levels = Horizons[h] / TickSeconds;
                double need = 50.0 * baseTime * (levels + 1.0) / (Math.Sqrt(epow) * atSpeed);
                text += $" · {HorizonNames[h]}: {NumberFormatter.Abbrev(need)} energy";
            }
            UiLayout.FitInto(row, text);
        }

        private void RenderTimeMachine(Character c)
        {
            // The bonuses TimeMachineBP.cs:73-75 divides its own cap by. Asking the game beats the
            // sheet's bonus-free arithmetic, which overstates the cap by whatever the player has earned.
            // The sadistic divider is deliberately NOT applied — this section covers Normal.
            double bonus = Read(() => (double)c.hacksController.totalTMSpeedBonus(), 1.0)
                * Read(() => (double)c.allChallenges.timeMachineChallenge.TMSpeedBonus(), 1.0)
                * Read(() => (double)c.cardsController.getBonus(cardBonus.TMSpeed), 1.0);
            if (bonus <= 0) bonus = 1.0;

            RenderTmDemand(c);

            RenderTmRow(_tmEnergy, "Energy → TM speed",
                Read(() => (double)c.timeMachineController.baseSpeedDivider()),
                Read(() => (double)c.totalEnergyPower()), bonus);
            RenderTmRow(_tmMagic, "Magic → TM gold multi",
                Read(() => (double)c.timeMachineController.baseGoldMultiDivider()),
                Read(() => (double)c.totalMagicPower()), bonus);
        }

        // The gold-denominated answer to "does more TM still buy progress": gold's two consumers are
        // digger upgrades and augments, so being starved for either IS the signal. Asked of the modules
        // that own those budgets rather than re-modelled here.
        private void RenderTmDemand(Character c)
        {
            bool diggers = Read(() => OptimizationAdvisor.GoldStarvedForDiggers(c, 1.0));
            bool augs = Read(() => OptimizationAdvisor.GoldStarvedForAugs(c, 1.0));
            _tmDemand.ForeColor = diggers || augs ? UiTheme.Cap : UiTheme.Ink;
            UiLayout.FitInto(_tmDemand, $"Gold demand — diggers: {(diggers ? "STARVED" : "funded")}"
                + $" · augments: {(augs ? "STARVED" : "funded")}"
                + (diggers || augs ? " · more TM still buys progress" : " · both budgets are covered"));

            // Gross and net are different questions: net is what is left after the diggers' own drain.
            double gross = Read(() => c.grossGoldPerSecond(), double.NaN);
            double net = Read(() => c.goldPerSecond(), double.NaN);
            if (double.IsNaN(gross) && double.IsNaN(net))
            {
                _tmRate.ForeColor = UiTheme.Faint;
                UiLayout.FitInto(_tmRate, "Gold rate — unavailable");
            }
            else
            {
                _tmRate.ForeColor = UiTheme.Ink;
                UiLayout.FitInto(_tmRate, "Gold rate — "
                    + (double.IsNaN(gross) ? "gross unavailable" : $"{NumberFormatter.Abbrev(gross)}/s gross")
                    + " · "
                    + (double.IsNaN(net) ? "net unavailable" : $"{NumberFormatter.Abbrev(net)}/s net of digger drain"));
            }

            // LevelPlanner only manages the TM targets inside the marathon segment (LevelPlanner.cs:69-75),
            // so outside it the panel must not imply the advisor is holding anything.
            string segment = ChallengeOverlay.Segment;
            if (segment != MarathonSegment)
            {
                _tmFreeze.ForeColor = UiTheme.Muted;
                UiLayout.WrapInto(_tmFreeze,
                    $"Level planner: TM targets are not managed in this segment ({(string.IsNullOrEmpty(segment) ? "none" : segment)})"
                    + $" — the freeze applies only during {MarathonSegment}.", 2);
                return;
            }
            bool frozen = Read(() => LevelPlanner.TmFrozen);
            _tmFreeze.ForeColor = frozen ? UiTheme.Cap : UiTheme.Ink;
            UiLayout.WrapInto(_tmFreeze, frozen
                ? "Level planner: TM targets FROZEN — the TM holds gold and augments are affordable, so it is not being fed."
                : "Level planner: TM targets live — the TM is unfunded or augments are unaffordable, so it is still being fed.", 2);
        }

        private void RenderTmRow(Label row, string name, double unitCost, double power, double bonus)
        {
            if (unitCost <= 0 || power <= 0)
            {
                row.ForeColor = UiTheme.Faint;
                UiLayout.FitInto(row, $"{name} — unavailable");
                return;
            }

            row.ForeColor = UiTheme.Ink;
            string text = name;
            for (int h = 0; h < Horizons.Length; h++)
            {
                double levels = Horizons[h] / TickSeconds;
                double need = levels * unitCost * TmUnitScale / (power * bonus);
                text += $" · {HorizonNames[h]} ({NumberFormatter.Abbrev(levels)} lv): {NumberFormatter.Abbrev(need)} cap";
            }
            UiLayout.FitInto(row, text);
        }

        // Both basic-training gates, read as one answer: updateAdvancedTraining() checks them together
        // and an unreadable gate counts as locked, because quoting a time is the failure that matters.
        private static bool AtLocked(Character c)
        {
            try
            {
                return c.training.attackTraining[4] < BasicTrainingGate
                    || c.training.defenseTraining[4] < BasicTrainingGate;
            }
            catch (Exception ex)
            {
                LogDebug($"AT panel gate read: {ex.Message}");
                return true;
            }
        }

        private static bool BlitzWishActive(Character c)
        {
            try { return c.wishes.wishes[BlitzWishId].level >= 1; }
            catch { return false; }
        }

        // Same id -> controller mapping AdvancedTrainingBP.ControllerFor uses; needed here only for
        // baseTime, the one input getProgressPerTick cannot give back.
        private static AdvancedTrainingController Controller(Character c, int id)
        {
            switch (id)
            {
                case 0: return c.advancedTrainingController.defense;
                case 1: return c.advancedTrainingController.attack;
                case 2: return c.advancedTrainingController.block;
                case 3: return c.advancedTrainingController.wandoosEnergy;
                case 4: return c.advancedTrainingController.wandoosMagic;
            }
            return null;
        }

        // Every live game read on this panel goes through here: a slot whose read throws degrades its
        // own row and nothing else, which is the same contract AtHourPlanner.ReadSlot honours.
        private static T Read<T>(Func<T> read, T fallback = default(T))
        {
            try { return read(); }
            catch { return fallback; }
        }

        // Local on purpose: the only other duration formatters in this codebase are private to PpPanel
        // and ProfileValidator, and one caller does not justify a public utility. Seconds rather than
        // hours because a blitz-boosted level lands in 0.02s.
        private static string Duration(double seconds)
        {
            if (seconds >= 3600.0 * 24 * 365) return "over a year";
            if (seconds < 1) return "<1s";
            if (seconds < 60) return $"{seconds:0.#}s";
            long minutes = (long)(seconds / 60.0);
            if (minutes < 60) return $"{minutes}m";
            long h = minutes / 60, m = minutes % 60;
            if (h < 48) return m > 0 ? $"{h}h {m}m" : $"{h}h";
            long d = h / 24;
            h %= 24;
            return h > 0 ? $"{d}d {h}h" : $"{d}d";
        }
    }
}
