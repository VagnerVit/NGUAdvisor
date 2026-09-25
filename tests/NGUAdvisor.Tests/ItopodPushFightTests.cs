using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    // The push target is the highest floor whose FIGHT is won (decomp EnemyAI), not the one-shot floor.
    public class ItopodPushFightTests
    {
        private static ItopodConstants.Fighter Fighter(double attack, double defense, double hp, double regen, bool beast = false)
            => new ItopodConstants.Fighter
            {
                Attack = attack,
                Defense = defense,
                MaxHp = hp,
                Regen = regen,
                RegularMultiplier = 1.0,
                GlobalCooldownSeconds = 0.8,
                BeastMode = beast,
            };

        [Fact]
        public void WinnableFloorIsAtLeastTheOneShotFloorForASturdyFighter()
        {
            var f = Fighter(1e12, 1e12, 1e13, 1e11);
            int oneShot = ItopodConstants.BestFloor(f.Attack, f.RegularMultiplier, false);
            Assert.True(ItopodConstants.BestWinnableFloor(f) >= oneShot);
        }

        [Fact]
        public void WinningIsMonotoneInTheFloor()
        {
            var f = Fighter(1e9, 5e8, 1e10, 1e8);
            int best = ItopodConstants.BestWinnableFloor(f);
            Assert.True(best > 0);
            for (int floor = 0; floor <= best; floor += 25)
                Assert.True(ItopodConstants.WinsEveryFight(f, floor), $"lost floor {floor} below {best}");
            Assert.False(ItopodConstants.WinsEveryFight(f, best + 1));
        }

        // PlayerController.takeDamage: incoming damage x3 while beast mode is on.
        [Fact]
        public void BeastModeNeverRaisesTheWinnableFloor()
        {
            int calm = ItopodConstants.BestWinnableFloor(Fighter(1e9, 5e8, 1e10, 1e8));
            int beast = ItopodConstants.BestWinnableFloor(Fighter(1e9, 5e8, 1e10, 1e8, beast: true));
            Assert.True(beast <= calm);
        }

        // Poison's extra tick is attack * 0.2 subtracted straight from curHP — defense does not touch it,
        // so a defense that zeroes the regular hit down to its 10% floor still loses a fight to poison
        // that the normal AI cannot win.
        [Fact]
        public void PoisonIsTheHardestAiWhenDefenseFloorsTheRegularHit()
        {
            // Floor 100: a ~290 s fight; regular hits total ~39k, poison adds ~43k on top.
            var f = Fighter(1000.0, 1e15, 60000.0, 0.0);
            int floor = 100;
            Assert.True(ItopodConstants.WinsFight(f, floor, ItopodConstants.PodAi.Normal));
            Assert.False(ItopodConstants.WinsFight(f, floor, ItopodConstants.PodAi.Poison));
        }

        [Fact]
        public void NoDamageThroughEnemyRegenLosesEveryFight()
        {
            var f = Fighter(10.0, 1e15, 1e15, 1e15);
            Assert.False(ItopodConstants.WinsFight(f, 300, ItopodConstants.PodAi.Normal));
        }
    }
}
