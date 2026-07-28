using System;
using System.Collections.Generic;
using System.Linq;
using static NGUAdvisor.Main;

namespace NGUAdvisor.Managers
{
    public static class ConsumablesManager
    {
        private static readonly Character _character = Main.Character;

        private static Arbitrary Arbitrary => _character.arbitrary;

        private static Consumable[] _lastConsumables = new Consumable[0];
        private static double _lastTime = 0;

        public abstract class Consumable
        {
            protected readonly string name;
            protected readonly double? time;
            protected readonly string useMethod;
            protected readonly ArbitraryController pod;

            public Consumable(int id, string useMethod, double? time = null)
            {
                pod = _character.allArbitrary.arbitraryPods.Find(x => x.id == id);
                if (pod == null)
                    throw new ArgumentException($"No arbitrary pod found for consumable id {id} ({useMethod}); it may not be unlocked yet.");
                name = pod.itemName;
                this.useMethod = useMethod;
                this.time = time;
            }

            public static Consumable CreateInstance(string name)
            {
                try
                {
                    switch (name)
                    {
                        case "EPOT-A":
                            return new EnergyPotionA();
                        case "EPOT-B":
                            return new EnergyPotionB();
                        case "EPOT-C":
                            return new EnergyPotionD();
                        case "MPOT-A":
                            return new MagicPotionA();
                        case "MPOT-B":
                            return new MagicPotionB();
                        case "MPOT-C":
                            return new MagicPotionD();
                        case "R3POT-A":
                            return new R3PotionA();
                        case "R3POT-B":
                            return new R3PotionB();
                        case "R3POT-C":
                            return new R3PotionD();
                        case "EBARBAR":
                            return new EnergyBarBar();
                        case "MBARBAR":
                            return new MagicBarBar();
                        case "MUFFIN":
                            return new Muffin();
                        case "LC":
                            return new LootCharm();
                        case "SLC":
                            return new SuperLootCharm();
                        case "MAYO":
                            return new Mayo();
                    }
                }
                catch (ArgumentException e)
                {
                    Log($"ConsumablesManager - Could not create consumable '{name}': {e.Message}");
                    return null;
                }
                return null;
            }

            protected virtual bool? IsActive() => null;

            protected virtual double? TimeLeft() => null;

            private bool HasEnoughCount(int quantity) => pod.count() >= quantity;

            private bool HasEnoughAP(int quantity, out long amountNeeded)
            {
                amountNeeded = pod.cost() * quantity;
                return Arbitrary.curArbitraryPoints >= amountNeeded;
            }

            protected abstract void BuyConsumable();

            public bool Buy(int quantity, out long amountNeeded)
            {
                if (!HasEnoughAP(quantity, out amountNeeded))
                {
                    Log($"ConsumablesManager - Not enough AP to buy {quantity} {name} ({Arbitrary.curArbitraryPoints:N0} out of {amountNeeded:N0} AP)!");
                    return false;
                }

                Log($"ConsumablesManager - Buying {quantity} {name} for {pod.cost() * quantity} AP");

                for (int i = 0; i < quantity; i++)
                {
                    if (HasEnoughAP(1, out _))
                    {
                        BuyConsumable();
                    }
                    else
                    {
                        Log($"ConsumablesManager - Unexpected error, ran out of AP before buying {quantity} {name}");
                        return false;
                    }
                }

                return true;
            }

            public bool ShouldUse(int quantity, double breakpointTime, out int quantityToUse)
            {
                quantityToUse = 0;

                // This will be null for all timed consumables except muffin
                // Muffin is a special case where it will show as "Active" until the first rebirth after activating
                // Afterwards, it will show the remaining time left (if any) of 24 hours since use
                bool? isActive = IsActive();
                bool isRebirth = isActive.HasValue;
                bool isTimed = time.HasValue;

                // Consumables which aren't timed but last as long as the rebirth
                if (isRebirth)
                {
                    if (isActive.Value)
                    {
                        Log($"ConsumablesManager - {name} is already active");
                        return false;
                    }

                    // If MacGuffin Muffin isnt active, fall through to the time check
                    // Otherwise only use one
                    if (!isTimed)
                    {
                        quantityToUse = 1;
                        if (quantity > 1)
                            Log($"ConsumablesManager - Rebirth based consumable detected! Only consuming {quantityToUse} {name} instead of {quantity}");
                        return true;
                    }
                }

                // All other timed consumables
                double totalConsumableTime = (time ?? 0) * quantity;
                double timeAtConsumableEnd = breakpointTime + totalConsumableTime;
                double rebirthTime = _character.rebirthTime.totalseconds;
                if (rebirthTime + 60 >= timeAtConsumableEnd)
                {
                    Log($"ConsumablesManager - Reload detected! {name} configured to expire at {GetTimeDisplay(TimeSpan.FromSeconds(timeAtConsumableEnd))} which is before or near the current time of {GetTimeDisplay(TimeSpan.FromSeconds(rebirthTime))}");
                    return false;
                }

                double timeSinceBreakpoint = rebirthTime - breakpointTime;
                if (timeSinceBreakpoint < 0)
                {
                    Log($"ConsumablesManager - Unexpected scenario with {name}! Current rebirth of {GetTimeDisplay(TimeSpan.FromSeconds(rebirthTime))} has not yet reached the breakpoint of {GetTimeDisplay(TimeSpan.FromSeconds(breakpointTime))}");
                    return false;
                }

                double timeLeft = TimeLeft() ?? 0;
                if (!Settings.ConsumeIfAlreadyRunning && timeLeft > 0)
                {
                    Log($"ConsumablesManager - {name} is already running, enable \"Use Consumables if already running\" to enable consumption of currently running consumables");
                    return false;
                }

                double expectedTimeLeft = totalConsumableTime - timeSinceBreakpoint;
                if (timeLeft + 60 >= expectedTimeLeft)
                {
                    Log($"ConsumablesManager - Active buffs detected! {name} will expire at {GetTimeDisplay(TimeSpan.FromSeconds(rebirthTime + timeLeft))} which is beyond or near the expected end time of {GetTimeDisplay(TimeSpan.FromSeconds(rebirthTime + expectedTimeLeft))}");
                    return false;
                }

                double amountOfTimeToActivate = expectedTimeLeft - timeLeft;
                quantityToUse = (int)Math.Round(amountOfTimeToActivate / time.Value, 0, MidpointRounding.AwayFromZero);
                if (quantityToUse != quantity)
                {
                    if (quantityToUse == 0)
                        Log($"ConsumablesManager - Reload or previously active buffs detected! Using {name} would go beyond the breakpoint's configured limit");
                    else if (quantityToUse > 0)
                        Log($"ConsumablesManager - Reload or previously active buffs detected! Only consuming {quantityToUse} {name} instead of {quantity}");
                }

                if (isRebirth && quantityToUse > 1)
                {
                    quantityToUse = 1;
                    Log($"ConsumablesManager - Rebirth based consumable detected! Only consuming {quantityToUse} {name} instead of {quantity}");
                }

                return quantityToUse > 0;
            }

            public void Use(int quantity)
            {
                if (!HasEnoughCount(quantity))
                {
                    Log($"ConsumablesManager - Not enough {name}! Owned: {pod.count()}, Needed:{quantity}");
                    return;
                }

                if (IsActive().HasValue)
                {
                    if (!time.HasValue)
                        Log($"ConsumablesManager - Eating {quantity} {name} (active until next rebirth)");
                    else
                        Log($"ConsumablesManager - Eating {quantity} {name} (active for {GetTimeDisplay(TimeSpan.FromSeconds(quantity * time.Value))} and at least until next rebirth)");
                }
                else
                {
                    Log($"ConsumablesManager - Eating {quantity} {name} (active for {GetTimeDisplay(TimeSpan.FromSeconds(quantity * time.Value))})");
                }

                for (int i = 0; i < quantity; i++)
                {
                    if (HasEnoughCount(1))
                    {
                        pod.CallMethod(useMethod);
                    }
                    else
                    {
                        Log($"ConsumablesManager - Unexpected error, ran out of {name} before consuming {quantity}");
                        return;
                    }
                }
            }

            public bool CanUse(int quantity)
            {
                if (!HasEnoughCount(quantity))
                {
                    int quantityToBuy = quantity - pod.count();
                    bool hasEnoughAP = HasEnoughAP(quantityToBuy, out long amountNeeded);

                    Log($"ConsumablesManager - Not enough {name}, need to buy {quantityToBuy} more");

                    if (Settings.AutoBuyConsumables && hasEnoughAP)
                    {
                        Buy(quantityToBuy, out _);
                    }
                    else
                    {
                        string message = "ConsumablesManager - ";
                        message += !hasEnoughAP
                            ? $"Not enough AP to purchase {quantityToBuy} {name} ({Arbitrary.curArbitraryPoints:N0} out of {amountNeeded:N0} AP)!"
                            : $"Not enough {name} ({pod.count()} out of {quantity})! Buy more manually, or turn on Auto Buy Consumables";

                        Log(message);
                        return false;
                    }
                }

                return true;
            }

            public void Eat(int quantity, double bpTime)
            {
                if (ShouldUse(quantity, bpTime, out int quantityToUse) && CanUse(quantityToUse))
                    Use(quantityToUse);
            }

            public static string GetTimeDisplay(TimeSpan time)
            {
                string display = "";
                if (time.TotalDays >= 1)
                    display = $"{Math.Floor(time.TotalDays)} day{(time.TotalDays > 1 ? "s" : "")} ";
                display += time.ToString(@"hh\:mm\:ss");
                return display;
            }
        }

        public class EnergyPotionA : Consumable
        {
            public EnergyPotionA() : base(0, "useEnergyPotion1", 3600) { }

            protected override double? TimeLeft() => Arbitrary.energyPotion1Time.totalseconds;

            protected override void BuyConsumable() => pod.buyEnergyPotion1AP();
        }

        public class EnergyPotionB : Consumable
        {
            public EnergyPotionB() : base(1, "useEnergyPotion2") { }

            protected override bool? IsActive() => Arbitrary.energyPotion2InUse;

            protected override void BuyConsumable() => pod.buyEnergyPotion2AP();
        }

        public class EnergyPotionD : Consumable
        {
            public EnergyPotionD() : base(26, "useEnergyPotion3", 86400) { }

            protected override double? TimeLeft() => Arbitrary.energyPotion1Time.totalseconds;

            protected override void BuyConsumable() => pod.buyEnergyPotion3();
        }

        public class MagicPotionA : Consumable
        {
            public MagicPotionA() : base(2, "useMagicPotion1", 3600) { }

            protected override double? TimeLeft() => Arbitrary.magicPotion1Time.totalseconds;

            protected override void BuyConsumable() => pod.buyMagicPotion1AP();
        }

        public class MagicPotionB : Consumable
        {
            public MagicPotionB() : base(3, "useMagicPotion2") { }

            protected override bool? IsActive() => Arbitrary.magicPotion2InUse;

            protected override void BuyConsumable() => pod.buyMagicPotion2AP();
        }

        public class MagicPotionD : Consumable
        {
            public MagicPotionD() : base(27, "useMagicPotion3", 86400) { }

            protected override double? TimeLeft() => Arbitrary.magicPotion1Time.totalseconds;

            protected override void BuyConsumable() => pod.buyMagicPotion3();
        }

        public class R3PotionA : Consumable
        {
            public R3PotionA() : base(59, "useRes3Potion1", 3600) { }

            protected override double? TimeLeft() => Arbitrary.res3Potion1Time.totalseconds;

            protected override void BuyConsumable() => pod.buyRes3Potion1();
        }

        public class R3PotionB : Consumable
        {
            public R3PotionB() : base(60, "useRes3Potion2") { }

            protected override bool? IsActive() => Arbitrary.res3Potion2InUse;

            protected override void BuyConsumable() => pod.buyRes3Potion2();
        }

        public class R3PotionD : Consumable
        {
            public R3PotionD() : base(61, "useRes3Potion3", 86400) { }

            protected override double? TimeLeft() => Arbitrary.res3Potion1Time.totalseconds;

            protected override void BuyConsumable() => pod.buyRes3Potion3();
        }

        public class EnergyBarBar : Consumable
        {
            public EnergyBarBar() : base(5, "useEnergyBarBar1", 3600) { }

            protected override double? TimeLeft() => Arbitrary.energyBarBar1Time.totalseconds;

            protected override void BuyConsumable() => pod.buyEnergyBarBar1AP();
        }

        public class MagicBarBar : Consumable
        {
            public MagicBarBar() : base(6, "useMagicBarBar1", 3600) { }

            protected override double? TimeLeft() => Arbitrary.magicBarBar1Time.totalseconds;

            protected override void BuyConsumable() => pod.buyMagicBarBar1AP();
        }

        public class Muffin : Consumable
        {
            public Muffin() : base(43, "useMacguffinBooster1", 86400) { }

            protected override bool? IsActive() => Arbitrary.macGuffinBooster1InUse;

            public bool MuffinIsActive() => IsActive() ?? false;

            protected override double? TimeLeft() => Arbitrary.macGuffinBooster1Time.totalseconds;

            public double MuffinTimeLeft() => TimeLeft() ?? 0.0;

            protected override void BuyConsumable() => pod.buyMacguffinBooster1AP();
        }

        public class LootCharm : Consumable
        {
            public LootCharm() : base(4, "useLootCharm1", 1800) { }

            protected override double? TimeLeft() => Arbitrary.lootcharm1Time.totalseconds;

            protected override void BuyConsumable() => pod.buyLootCharm1AP();
        }

        public class SuperLootCharm : Consumable
        {
            public SuperLootCharm() : base(30, "useLootCharm2", 43200) { }

            protected override double? TimeLeft() => Arbitrary.lootcharm1Time.totalseconds;

            protected override void BuyConsumable() => pod.buyLootCharm2AP();
        }

        public class Mayo : Consumable
        {
            public Mayo() : base(79, "useMayoSpeedPot", 86400) { }

            protected override double? TimeLeft() => Arbitrary.mayoSpeedPotTime.totalseconds;

            protected override void BuyConsumable() => pod.buyMayoSpeedConsumableAP();
        }

        public static void EatConsumables(Dictionary<Consumable, int> consumables, double time)
        {
            if (Enumerable.SequenceEqual(consumables.Keys, _lastConsumables) && (Math.Abs(time - _lastTime) < 1))
                // We already did this set of consumables, wait for next one
                return;

            foreach (var kvp in consumables)
                kvp.Key.Eat(kvp.Value, time);

            _lastConsumables = consumables.Keys.ToArray();
            _lastTime = time;
        }

        public static void ResetLastConsumables()
        {
            _lastConsumables = new Consumable[0];
            _lastTime = 0;
        }
    }
}