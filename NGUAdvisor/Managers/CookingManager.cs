using System.Collections.Generic;
using static NGUAdvisor.Main;

namespace NGUAdvisor.Managers
{
    public static class CookingManager
    {
        public static void ManageFood()
        {
            var controller = Main.Character.cookingController;
            var cooking = Main.Character.cooking;

            if (cooking.unlocked && cooking.cookTimer >= controller.eatRate())
            {
                if (controller.getCurScore() < controller.getOptimalScore())
                {
                    var pairs = new List<int>[]
                    {
                        cooking.pair1,
                        cooking.pair2,
                        cooking.pair3,
                        cooking.pair4
                    };

                    for (int index = 0; index < pairs.Length; index++)
                    {
                        var pair = pairs[index];
                        var max = 0f;
                        for (int i = 0; i <= controller.maxIngredientLevel(); i++)
                        {
                            for (int j = 0; j <= controller.maxIngredientLevel(); j++)
                            {
                                var cur = 0f;

                                if (controller.ingredientUnlocked(pair[0]))
                                    cur += controller.getLocalScore(pair[0], i) + controller.getLocalScore(pair[1], i);

                                if (controller.ingredientUnlocked(pair[1]))
                                    cur += controller.getLocalScore(pair[0], j) + controller.getLocalScore(pair[1], j);

                                if (controller.ingredientUnlocked(pair[0]) && controller.ingredientUnlocked(pair[1]))
                                    cur += controller.getPairedScore(index + 1, i + j);

                                if (cur > max)
                                {
                                    if (controller.ingredientUnlocked(pair[0]))
                                        cooking.ingredients[pair[0]].curLevel = i;
                                    if (controller.ingredientUnlocked(pair[1]))
                                        cooking.ingredients[pair[1]].curLevel = j;

                                    max = cur;
                                }
                            }
                        }
                    }
                }

                if (Settings.ManageCookingLoadouts && Settings.CookingLoadout.Length > 0)
                {
                    if (!LockManager.TryCookingSwap())
                    {
                        Log("Unable to acquire lock for gear, waiting a cycle to equip gears");
                        return;
                    }
                }

                controller.consumeDish();
            }

            if (LockManager.HasCookingLock())
                LockManager.TryCookingSwap();
        }
    }
}