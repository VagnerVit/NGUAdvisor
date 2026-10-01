using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using static NGUAdvisor.Main;

namespace NGUAdvisor.Managers
{
    public static class YggdrasilManager
    {
        private static readonly Character _character = Main.Character;
        private static readonly AllYggdrasil _yc = _character.yggdrasilController;
        private static readonly FruitController _fc = _yc.fruits[0];

        private static List<Fruit> Fruits => _character.yggdrasil.fruits;

        public static bool AnyHarvestable()
        {
            for (var i = 0; i < Fruits.Count; i++)
            {
                if (_fc.harvestTier(i) > 0)
                    return true;
            }

            return false;
        }

        // A pass that comes due waits at most this long for another fruit about to max, so both share
        // one gear/beard/digger swap: Power β, Arbitrariness and Rage used to max seconds apart and
        // each paid for a full swap and restore of its own.
        private const double MaxHarvestDeferSeconds = 120;

        // Run clock (rebirthTime) at which the pending pass first came due; -1 = none pending.
        private static double _harvestDueSince = -1;
        private static bool _deferLogged;
        private static long _reminderLoggedRebirth = -1;

        // The gear-spec factors a harvest reads from InventoryController.bonuses (decomp): Seed Gain in
        // every fruit's seed reward, Yggdrasil Yield through Character.yggdrasilYieldBonus(), EXP and AP
        // through addExp/addAP for Knowledge and Arbitrariness.
        private struct HarvestSpecs
        {
            public float Seeds;
            public float Yggdrasil;
            public float Exp;
            public float Ap;
        }

        private static HarvestSpecs WornSpecs()
        {
            var ic = _character.inventoryController;
            return new HarvestSpecs
            {
                Seeds = ic.getBonusFactor(ic.specBonus(specType.Seeds), specType.Seeds),
                Yggdrasil = ic.getBonusFactor(ic.specBonus(specType.Yggdrasil), specType.Yggdrasil),
                Exp = ic.getBonusFactor(ic.specBonus(specType.EXP), specType.EXP),
                Ap = ic.getBonusFactor(ic.specBonus(specType.AP), specType.AP)
            };
        }

        // The specs once LoadoutManager.ChangeGear(gearIds) has run, placed the way it places them: the
        // first item per part, a second weapon into the offhand, accessories into slots 0.. in order
        // (swapping with wherever they sit), every slot the set leaves out keeping what is worn now.
        private static HarvestSpecs SpecsAfterSwap(int[] gearIds)
        {
            var ic = _character.inventoryController;
            var inv = _character.inventory;
            Equipment head = inv.head, chest = inv.chest, legs = inv.legs, boots = inv.boots;
            Equipment weapon = inv.weapon, weapon2 = inv.weapon2;
            bool headSet = false, chestSet = false, legsSet = false, bootsSet = false;
            int weapons = 0, accSlot = 0;
            var accs = new List<Equipment>(inv.accs);
            int accSpaces = Math.Min(ic.accessorySpaces(), accs.Count);

            foreach (var id in gearIds.Where(x => x > 0).Distinct())
            {
                ih item = LoadoutManager.FindItemSlot(id);
                if (item == null || item.equipment == null)
                    continue;
                var e = item.equipment;
                switch (e.type)
                {
                    case part.Head when !headSet: head = e; headSet = true; break;
                    case part.Chest when !chestSet: chest = e; chestSet = true; break;
                    case part.Legs when !legsSet: legs = e; legsSet = true; break;
                    case part.Boots when !bootsSet: boots = e; bootsSet = true; break;
                    case part.Weapon when weapons == 0: weapon = e; weapons++; break;
                    case part.Weapon when weapons == 1 && ic.weapon2Unlocked(): weapon2 = e; weapons++; break;
                    case part.Accessory:
                        if (accSlot < accSpaces)
                        {
                            int from = accs.FindIndex(a => object.ReferenceEquals(a, e));
                            if (from >= 0)
                                accs[from] = accs[accSlot];
                            accs[accSlot] = e;
                        }
                        accSlot++;
                        break;
                }
            }

            float Sum(specType t)
            {
                float sum = ic.equipSpecBonus(t, head) + ic.equipSpecBonus(t, chest) + ic.equipSpecBonus(t, legs)
                          + ic.equipSpecBonus(t, boots) + ic.equipSpecBonus(t, weapon)
                          + ic.equipSpecBonus(t, weapon2) * ic.weapon2Factor();
                foreach (var a in accs)
                    sum += ic.equipSpecBonus(t, a);
                return ic.getBonusFactor(sum, t);
            }

            return new HarvestSpecs
            {
                Seeds = Sum(specType.Seeds),
                Yggdrasil = Sum(specType.Yggdrasil),
                Exp = Sum(specType.EXP),
                Ap = Sum(specType.AP)
            };
        }

        // Character.yggdrasilYieldBonus() with the gear term taken from `specs`.
        private static float YieldBonus(HarvestSpecs specs)
            => (1f + specs.Yggdrasil) * _character.beastQuestPerkController.totalYggYieldBonus();

        // The specs the harvest will ACTUALLY run in, decided exactly the way LockManager decides it:
        // the Yggdrasil set when the pass is going to swap, whatever is on your back when it isn't.
        private static HarvestSpecs PlannedSpecs()
        {
            var gear = SwapGearForPass(false, quiet: true);
            return gear != null ? SpecsAfterSwap(gear) : WornSpecs();
        }

        private static long MacguffinFruit2Bonus(int tier, float yieldBonus, bool firstHarvest = true)
        {
            var fruit = Fruits[13];
            int tierFactor = _fc.tierFactor(tier);
            bool usePoop = fruit.usePoop && (!_character.settings.poopOnlyMaxTier || tier == (int)fruit.maxTier);
            float poopModifier = usePoop ? _character.allArbitrary.poopModifier() : 1f;
            float harvestBonus = firstHarvest ? _character.adventureController.itopod.totalHarvestBonus(13) : 1f;
            var result = (long)Mathf.Ceil(tierFactor * 0.1f * poopModifier * yieldBonus * harvestBonus);
            if (result >= int.MaxValue)
                result = int.MaxValue;
            if (result < 0)
                result = 0L;
            return result;
        }

        private static bool MacguffinFruit2Ready() => MacguffinFruit2Ready(WornSpecs());

        private static bool MacguffinFruit2Ready(HarvestSpecs specs)
        {
            var fruit = Fruits[13];
            if (!fruit.eatFruit)
                return false;

            long maxTier = fruit.maxTier;
            if (maxTier < 1)
                return false;

            int harvestTier = _fc.harvestTier(13);
            if (harvestTier < 1)
                return false;

            if (fruit.usePoop && !_character.settings.poopOnlyMaxTier)
                return false;

            float yieldBonus = YieldBonus(specs);
            var maxBonus = (double)MacguffinFruit2Bonus((int)maxTier, yieldBonus) / maxTier;
            var bonus = (double)MacguffinFruit2Bonus(harvestTier, yieldBonus);
            bonus += (maxTier - harvestTier) * (double)MacguffinFruit2Bonus(1, yieldBonus, false);
            bonus /= maxTier;
            if (bonus <= maxBonus)
                return false;

            return true;
        }

        private static bool QPFruitReady()
        {
            var fruit = Fruits[14];
            if (!fruit.eatFruit)
                return false;

            long maxTier = fruit.maxTier;
            if (maxTier < 1)
                return false;

            int harvestTier = _fc.harvestTier(14);
            if (harvestTier < 1)
                return false;

            if (harvestTier < Settings.YggSwapThreshold && SwapConfigured())
                return false;

            if (fruit.usePoop)
                return false;

            if (_character.adventureController.itopod.totalHarvestBonus(14) > 1f)
                return false;

            return true;
        }

        // Deferral only ever makes this false BEFORE TryYggdrasilSwap acquires anything, so it can delay
        // an acquisition but never strand one: a false here with the Yggdrasil lock held takes the
        // release branch. Forced passes (PreRebirth, the manual buttons) never wait.
        public static bool NeedsHarvest(bool forced = false)
        {
            if (forced)
                return AnyHarvestable();
            if (!(_yc.anyFruitMaxxed() || MacguffinFruit2Ready(PlannedSpecs()) || QPFruitReady()))
            {
                _harvestDueSince = -1;
                _deferLogged = false;
                return false;
            }
            return !DeferForCompanionFruit();
        }

        // True while another growing fruit maxes before the deadline fixed when this pass first came
        // due. The deadline never moves, so the wait is bounded; a run clock that went backwards is a
        // rebirth, which restarts the window instead of carrying it over; and no wait may reach the
        // profile's rebirth target.
        private static bool DeferForCompanionFruit()
        {
            double now = _character.rebirthTime.totalseconds;
            if (_harvestDueSince < 0 || now < _harvestDueSince)
            {
                _harvestDueSince = now;
                _deferLogged = false;
            }
            double deadline = _harvestDueSince + MaxHarvestDeferSeconds;

            double rebirthTarget = Main.Profile != null ? Main.Profile.NextRebirthTargetSeconds() : -1;
            if (rebirthTarget > 0 && deadline >= rebirthTarget)
                return false;

            float tierSeconds = _fc.tierThreshold();
            for (var i = 0; i < Fruits.Count; i++)
            {
                var fruit = Fruits[i];
                if (fruit.maxTier < 1 || !(fruit.activated || fruit.permCostPaid) || _fc.fruitMaxxed(i))
                    continue;
                double maxesIn = fruit.maxTier * tierSeconds - fruit.seconds;
                if (now + maxesIn > deadline)
                    continue;
                if (!_deferLogged)
                {
                    Main.LogYggdrasil($"  harvest waits for {FruitName(i)} (maxes in {Math.Max(0, maxesIn):0}s; waits at most {MaxHarvestDeferSeconds:0}s)");
                    _deferLogged = true;
                }
                return true;
            }
            return false;
        }

        // Same two sources ResolveModeGear swaps from: an objective, or a static loadout.
        private static bool SwapConfigured()
            => Settings.SwapYggdrasilLoadouts
            && (!string.IsNullOrEmpty(Settings.YggdrasilObjective) || Settings.YggdrasilLoadout.Length > 0);

        private static bool SwapThresholdMet()
        {
            int thresh = Math.Max(1, Settings.YggSwapThreshold);
            for (var i = 0; i < Fruits.Count; i++)
            {
                if (_fc.harvestTier(i) >= thresh && _fc.fruitMaxxed(i))
                    return true;
            }

            return false;
        }

        // The Yggdrasil set this pass should put on, or null when the pass runs in the worn gear: swap
        // off, threshold not met (unforced), the set already worn, or the set raising no payout of the
        // fruits this pass takes. Every fruit pays seeds through Seed Gain, so this is a payout test,
        // never a fruit list. A valuation fault falls back to the swap, the pre-gate behaviour.
        public static int[] SwapGearForPass(bool forced, bool quiet)
        {
            if (!Settings.SwapYggdrasilLoadouts || (!forced && !SwapThresholdMet()))
                return null;

            var gear = GearOptimizer.ResolveModeGear(Settings.YggdrasilObjective, Settings.YggdrasilObjectiveRespawn,
                                                     Settings.YggdrasilLoadout, quiet: quiet);
            if (gear == null || gear.Length == 0)
                return null;

            var worn = LoadoutManager.CurrentGearIds().Distinct().OrderBy(x => x);
            if (worn.SequenceEqual(gear.Where(x => x > 0).Distinct().OrderBy(x => x)))
                return null;

            try
            {
                return SwapRaisesPayout(gear, forced) ? gear : null;
            }
            catch (Exception e)
            {
                if (!quiet)
                    Main.LogDebug($"Yggdrasil swap valuation failed, swapping anyway: {e.Message}");
                return gear;
            }
        }

        private static bool SwapRaisesPayout(int[] gear, bool forced)
        {
            var worn = WornSpecs();
            var swapped = SpecsAfterSwap(gear);
            for (var i = 0; i < Fruits.Count; i++)
            {
                if (TakenThisPass(i, forced, swapped) && PayoutRises(i, worn, swapped))
                    return true;
            }
            return false;
        }

        // Mirrors HarvestAll: forced takes every fruit with a tier (consumeAll(true)); a normal pass takes
        // the maxed ones (consumeAll()), plus MacGuffin β and Quirks on their own triggers.
        private static bool TakenThisPass(int i, bool forced, HarvestSpecs specs)
        {
            if (_fc.harvestTier(i) < 1)
                return false;
            if (forced || _fc.fruitMaxxed(i))
                return true;
            if (i == 13)
                return MacguffinFruit2Ready(specs);
            if (i == 14)
                return QPFruitReady();
            return false;
        }

        private static bool PayoutRises(int i, HarvestSpecs worn, HarvestSpecs swapped)
        {
            int tier = _fc.harvestTier(i);
            float poop = PoopFactor(i);
            if (SeedPayout(i, tier, poop, swapped.Seeds) > SeedPayout(i, tier, poop, worn.Seeds))
                return true;
            return Fruits[i].eatFruit && FruitPayout(i, tier, poop, swapped) > FruitPayout(i, tier, poop, worn);
        }

        // FruitController.usePoop without its side effects (it spends poop as it answers).
        private static float PoopFactor(int i)
        {
            var fruit = Fruits[i];
            if (!fruit.usePoop || (_character.settings.poopOnlyMaxTier && !_fc.fruitMaxxed(i)))
                return 1f;
            float modifier = _character.allArbitrary.poopModifier();
            bool poopMaxxed = _character.inventory.itemList.itemMaxxed[162];
            if (poopMaxxed && (_character.arbitrary.poop1Count > 0 || _character.stats.poopUsed % 10 == 0))
                return modifier;
            if (_character.arbitrary.poop1Count > 0)
                return Mathf.Clamp(modifier, 1f, 1.65f);
            return 1f;
        }

        // FruitController.seedReward / harvestSeedReward: Pomegranate, Watermelon and any fruit set to
        // Harvest pay the doubled reward; Quirks pays on the raw tier, the Mayo fruits on tier^1.1.
        private static double SeedPayout(int i, int tier, float poop, float seedSpec)
        {
            var rewards = _yc.baseSeedReward;
            if (i >= rewards.Count)
                return 0;
            bool doubled = !Fruits[i].eatFruit || i == 4 || i == 12;
            int seedTier;
            if (doubled || i < 14)
                seedTier = _fc.tierFactor(tier);
            else if (i == 14)
                seedTier = tier;
            else
                seedTier = (int)Mathf.Pow(tier, 1.1f);
            var itopod = _character.adventureController.itopod;
            return Mathf.Ceil((float)(rewards[i] * seedTier * (doubled ? 2 : 1)) * poop * itopod.totalSeedBonus()
                              * _character.beastQuestPerkController.totalSeedBonus() * (1f + seedSpec)
                              * _character.NGUController.yggdrasilBonus() * itopod.totalHarvestBonus(i));
        }

        // The eaten fruit's own payout by its FruitController.consume* formula, with the gear terms from
        // `specs`. Gold (grossGoldPerSecond), Pomegranate, Watermelon and the Mayo fruits have none.
        private static double FruitPayout(int i, int tier, float poop, HarvestSpecs specs)
        {
            var c = _character;
            var itopod = c.adventureController.itopod;
            int tf = _fc.tierFactor(tier);
            float hb = itopod.totalHarvestBonus(i);
            float ngu = c.NGUController.yggdrasilBonus();
            float y = YieldBonus(specs);
            var rewards = _yc.baseSeedReward;
            switch (i)
            {
                case 1: return Mathf.Ceil(tf * poop * ngu * y * hb);
                case 2: return (int)(Mathf.Pow(c.adventure.defense, 0.2f) * tf * poop * ngu * y * hb);
                case 3: return KnowledgeExp(tf, poop, ngu, y, hb, specs.Exp);
                case 5: return Mathf.Ceil(rewards[5] * 0.7f * tf * poop * ngu * y * hb);
                case 6:
                case 8:
                case 11: return Mathf.Ceil((float)(rewards[i] * tf) * poop * ngu * y * hb);
                case 7: return ArbitrarinessAp(tf, poop, hb, specs.Ap);
                case 9: return Mathf.Ceil(60000 * tf * poop * y * itopod.totalPPBonus(false) * hb);
                case 10: return Mathf.Ceil(tf * 0.5f * poop * y * hb * c.wishesController.totalFruitGuffbonus());
                case 13: return Mathf.Ceil(tf * 0.1f * poop * y * hb);
                case 14: return Mathf.Ceil(3 * tier * poop * y * QuestRewardFactor() * hb);
                default: return 0;
            }
        }

        // consumeKnowledgeFruit then Character.addExp.
        private static double KnowledgeExp(int tf, float poop, float ngu, float yieldBonus, float hb, float expSpec)
        {
            var c = _character;
            var perks = c.adventure.itopod.perkLevel;
            long exp = (int)Mathf.Ceil(5 * tf * poop * ngu * yieldBonus * hb);
            if (perks[19] >= 1)
                exp *= 3;
            if (perks[20] >= 1)
                exp *= 3;
            float num = exp * c.NGUController.expBonus();
            num = c.inventory.itemList.itemMaxxed[119] ? num * 1.1f : num * (1f + expSpec);
            if (perks[94] >= 987)
                num *= 1.05f;
            num *= c.allDiggers.totalEXPBonus();
            num *= c.hacksController.totalEXPBonus();
            num *= c.wishesController.totalExpBonus();
            num *= c.cookingController.totalExpBonus();
            return Math.Floor(num);
        }

        // consumeAPFruit then Character.addAP.
        private static double ArbitrarinessAp(int tf, float poop, float hb, float apSpec)
        {
            var c = _character;
            long amount = (long)Mathf.Ceil(15 * tf * poop * hb);
            float num = Math.Max(0f, amount * c.allAchievements.bonusAP());
            num = c.inventory.itemList.itemMaxxed[129] ? num * 1.2f : num * (1f + apSpec);
            if (c.adventure.itopod.perkLevel[94] >= 89)
                num *= 1.02f;
            return Math.Floor(num);
        }

        private static float QuestRewardFactor()
        {
            float factor = _character.beastQuestController.questRewardFactor();
            if (_character.beastQuest.usedButter)
                factor /= _character.allArbitrary.butterModifier();
            return factor;
        }

        public static void ManageYggHarvest()
        {
            NoteRebirthReminder();
            if (LockManager.TryYggdrasilSwap())
                HarvestAll();
        }

        // Owns the Yggdrasil lock from the moment TryYggdrasilSwap() hands it over: every caller —
        // the automatic pass, PreRebirth, and both manual buttons — does all of its lock-held work
        // right here. A throw used to walk out with the lock still on, and nothing could take it
        // back: the unharvested fruit keeps NeedsHarvest() true, which is precisely the state in
        // which TryYggdrasilSwap() refuses to restore. The lock stayed for the session, CanSwap()
        // stayed false, and RebirthAvailable() (BaseRebirth:157) never returned — the run could not
        // end. So the restoration is reached from both exits now, and the harvest fault survives it.
        public static void HarvestAll(bool tierOver1 = false)
        {
            _harvestDueSince = -1;
            _deferLogged = false;
            try
            {
                LogHarvestHeader(tierOver1);
                ReadTooltipLog(false);
                var macguffinFruit = Fruits[10];
                if (tierOver1)
                {
                    if (_fc.harvestTier(10) > 0 && Settings.FavoredMacguffin >= 0)
                    {
                        InventoryManager.ManageFavoredMacguffin(false, true);
                        _fc.consumeFruit(10);
                    }
                    InventoryManager.RestoreMacguffins();
                    _yc.consumeAll(true);
                }
                else
                {
                    if (_fc.harvestTier(10) > 0 && _fc.harvestTier(10) >= macguffinFruit.maxTier && Settings.FavoredMacguffin >= 0)
                    {
                        InventoryManager.ManageFavoredMacguffin(false, true);
                        _fc.consumeFruit(10);
                    }
                    InventoryManager.RestoreMacguffins();
                    _yc.consumeAll();
                    if (MacguffinFruit2Ready())
                        _fc.consumeFruit(13);
                    if (QPFruitReady())
                        _fc.consumeFruit(14);
                }
                LockManager.RestoreYggdrasilSwap();
                ReadTooltipLog(true);
            }
            catch
            {
                CleanupFailedYggdrasilHarvest();
                throw;
            }
        }

        // The cleanup faults are reported and dropped, never rethrown. RestoreConfiguration transitions
        // the lock inside its own finally (R4), so by the time anything left in it can throw the lock
        // is already safe, and the harvest fault is the one worth keeping — it is why the harvest
        // failed at all. A throw at or after the normal restoration finds no Yggdrasil lock and the
        // guarded call simply no-ops, so there is no second release to get wrong.
        //
        // MacGuffins go back FIRST, matching the successful path (RestoreMacguffins before the eventual
        // Yggdrasil restore) and running while the harvest inventory context is still up, before gear
        // restoration shuffles daycare/inventory slots the MacGuffin restore may need. RestoreMacguffins
        // is self-guarding on _savedMacguffins: it no-ops unless a favored-fruit swap is actually
        // outstanding, so a harvest that never touched MacGuffins passes through it untouched. The two
        // cleanups are independently guarded — one failing must not skip the other, and neither may
        // replace the primary harvest exception that HarvestAll's catch rethrows.
        private static void CleanupFailedYggdrasilHarvest()
        {
            try
            {
                InventoryManager.RestoreMacguffins();
            }
            catch (Exception cleanupEx)
            {
                try
                {
                    LogDebug($"MacGuffin cleanup after failed harvest:\n{cleanupEx}");
                }
                catch
                {
                    // Best-effort diagnostic. Do not block Yggdrasil cleanup.
                }
            }

            try
            {
                LockManager.RestoreYggdrasilSwap();
            }
            catch (Exception cleanupEx)
            {
                try
                {
                    LogDebug($"Yggdrasil cleanup after failed harvest:\n{cleanupEx}");
                }
                catch
                {
                    // Best-effort diagnostic. The original harvest exception remains authoritative.
                }
            }
        }

        // What the harvest is about to take, written BEFORE it runs: the game's own tooltip lines say
        // what each fruit gave but never which tier it was at or what the loadout contributed, and
        // after consumeAll the tiers are gone. The header runs after the swap, so the gear it names and
        // the factors it prints are what the harvest actually reads.
        // The display names live on the controller, not on Fruit itself.
        private static string FruitName(int index)
        {
            try
            {
                var names = _yc.fruitName;
                return index >= 0 && index < names.Count ? names[index] : $"Fruit {index}";
            }
            catch { return $"Fruit {index}"; }
        }

        private static void LogHarvestHeader(bool tierOver1)
        {
            try
            {
                var ready = new List<string>();
                for (var i = 0; i < Fruits.Count; i++)
                {
                    int tier = _fc.harvestTier(i);
                    if (tier <= 0) continue;
                    ready.Add($"{FruitName(i)} T{tier}/{Fruits[i].maxTier}{(_fc.fruitMaxxed(i) ? " MAX" : "")}"
                            + (Fruits[i].eatFruit ? "" : " (harvest: seeds only)"));
                }

                var specs = WornSpecs();
                var gearIds = LoadoutManager.CurrentGearIds();
                Main.LogYggdrasil($"--- HARVEST{(tierOver1 ? " (all tiers)" : "")}"
                                + $" · {(LockManager.YggdrasilGearSwapped ? "Yggdrasil set" : "no gear swap")}"
                                + $" · yield x{YieldBonus(specs):0.###} · seeds x{1f + specs.Seeds:0.###}"
                                + $" · gear [{string.Join(", ", gearIds.Select(id => Main.ItemName(id)).ToArray())}]");
                Main.LogYggdrasil(ready.Count == 0
                    ? "  nothing harvestable"
                    : $"  ready: {string.Join(" · ", ready.ToArray())}");
            }
            catch (Exception e) { Main.LogDebug($"Harvest header: {e.Message}"); }
        }

        // AllGoldDiggerController.upgradeMaxLevel posts one of these per level the digger restore buys.
        private const string DiggerCapRaised = "raised this digger's max level to ";

        public static void ReadTooltipLog(bool doLog)
        {
            var bLog = Main.Character.tooltip.log;
            var log = bLog.GetFieldValue<TooltipLog, List<string>>("Eventlog");
            var diggerCaps = new List<string>();
            // Add something to the end of our logs to mark them as complete
            for (var i = 0; i < log.Count; i++)
            {
                if (log[i].EndsWith("<b></b>"))
                    continue;
                if (doLog)
                {
                    string line = OneLine(log[i]);
                    int at = line.IndexOf(DiggerCapRaised, StringComparison.Ordinal);
                    if (at >= 0)
                        diggerCaps.Add(line.Substring(at + DiggerCapRaised.Length).TrimEnd('!', ' '));
                    else
                        LogYggdrasil(line);
                }
                log[i] += "<b></b>";
            }
            if (diggerCaps.Count > 0)
                LogYggdrasil($"Digger max level raised {diggerCaps.Count}x (to {string.Join(", ", diggerCaps.Distinct().ToArray())})");
        }

        // Multi-line tooltips (Adventure's stat list, the "You also gain:" fruits) become one record.
        private static string OneLine(string tooltip)
        {
            var sb = new StringBuilder(tooltip);
            sb.Replace("<b>", "");
            sb.Replace("</b>", "");
            var parts = sb.ToString().Split('\n').Select(p => p.Trim()).Where(p => p.Length > 0);
            return string.Join(" ", parts.ToArray());
        }

        // AutoRebirth off means PreRebirth's HarvestAll(true) never runs, and a rebirth by hand resets
        // every fruit (Yggdrasil.reset with resetFactor 0): the tiers below max are lost, not banked.
        // Null when nothing would be lost or the advisor rebirths itself. dueNow = the profile's
        // rebirth target has been reached.
        public static string RebirthHarvestReminder(out bool dueNow)
        {
            dueNow = false;
            if (Settings == null || Settings.AutoRebirth || !Settings.ManageYggdrasil)
                return null;

            var held = new List<string>();
            for (var i = 0; i < Fruits.Count; i++)
            {
                int tier = _fc.harvestTier(i);
                if (tier >= 1 && !_fc.fruitMaxxed(i))
                    held.Add($"{FruitName(i)} T{tier}/{Fruits[i].maxTier}");
            }
            if (held.Count == 0)
                return null;

            double target = Main.Profile != null ? Main.Profile.NextRebirthTargetSeconds() : -1;
            dueNow = target > 0 && _character.rebirthTime.totalseconds >= target;
            string fruits = string.Join(", ", held.ToArray());
            return dueNow
                ? $"Rebirth target reached — Harvest Now before you rebirth, or these tiers are lost: {fruits}."
                : $"Rebirthing by hand? Harvest Now first — a rebirth loses these tiers: {fruits}.";
        }

        // One yggdrasil.log line per run once the reminder turns due, so a lost harvest is traceable.
        private static void NoteRebirthReminder()
        {
            string reminder = RebirthHarvestReminder(out bool dueNow);
            long run = _character.stats.rebirthNumber;
            if (reminder == null || !dueNow || _reminderLoggedRebirth == run)
                return;
            _reminderLoggedRebirth = run;
            Main.LogYggdrasil(reminder);
        }

        public static void CheckFruits()
        {
            if (!Settings.ActivateFruits)
                return;
            int curPage = _yc.curPage;
            for (var i = 0; i < Fruits.Count; i++)
            {
                var fruit = Fruits[i];
                // Skip inactive fruits
                if (fruit.maxTier == 0L)
                    continue;

                // Skip fruits that are permed
                if (fruit.permCostPaid)
                    continue;

                if (fruit.activated)
                    continue;

                if (_yc.usesEnergy[i] &&
                    _character.curEnergy >= _yc.activationCost[i])
                {
                    Log($"Removing energy for fruit {i}");
                    _character.removeMostEnergy();
                    var slot = ChangePage(i);
                    _yc.fruits[slot].activate(i);
                    continue;
                }

                if (!_yc.usesEnergy[i] &&
                    _character.magic.curMagic >= _yc.activationCost[i])
                {
                    Log($"Removing magic for fruit {i}");
                    _character.removeMostMagic();
                    var slot = ChangePage(i);
                    _yc.fruits[slot].activate(i);
                }
            }
            _yc.changePage(curPage);
        }

        private static int ChangePage(int slot)
        {
            var page = slot / 9;
            _yc.changePage(page);
            return slot - (page * 9);
        }
    }
}
