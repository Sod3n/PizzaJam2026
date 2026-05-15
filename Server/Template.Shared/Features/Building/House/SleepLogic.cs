using Deterministic.GameFramework.ECS;
using Template.Shared.Components;
using Template.Shared.GameData;
using Deterministic.GameFramework.Utils.Logging;

namespace Template.Shared.Systems;

public static class SleepLogic
{

    public static void AdvanceDay(EntityWorld state)
    {
        int newDay = 0;
        foreach (var grEntity in state.Filter<GlobalResourcesComponent>())
        {
            ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            gr.DayCounter++;
            gr.TicksSinceDayStart = 0;
            gr.FoodSpawnedTodayGrass = 0;
            gr.FoodSpawnedTodayCarrot = 0;
            gr.FoodSpawnedTodayApple = 0;
            gr.FoodSpawnedTodayMushroom = 0;
            newDay = gr.DayCounter;
            ILogger.Log($"[SleepLogic] Advanced to day {gr.DayCounter}. Food caps reset.");
            break;
        }

        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            if (cow.IsMilking) continue;
            cow.Exhaust = 0;
            cow.MilkClickCounter = 0;
            cow.IsExhausted = false;
            cow.IsAttacking = false;
            cow.Horny = 0;
        }

        int product = (newDay % Balance.Sell.DayCycle) == Balance.Sell.CowDayRemainder ? SellProduct.Cow : SellProduct.Milk;
        foreach (var signEntity in state.Filter<SellPointSignComponent>())
        {
            ref var sign = ref state.GetComponent<SellPointSignComponent>(signEntity);
            sign.CurrentProduct = product;
        }

        // Sweep all ground food at end-of-day so the world resets cleanly and the next day's
        // spawn caps aren't competing with stale leftovers.
        var foodToRemove = new System.Collections.Generic.List<Entity>();
        foreach (var foodEntity in state.Filter<GrassComponent>())
            foodToRemove.Add(foodEntity);
        foreach (var foodEntity in foodToRemove)
            state.DeleteEntity(foodEntity);

        // Day-unit cooldowns decrement by 1 per sleep — MaxTicks=N means N sleeps to clear.
        // Tick-unit cooldowns are handled by CooldownSystem each frame.
        foreach (var entity in state.Filter<CooldownComponent>())
        {
            ref var cd = ref state.GetComponent<CooldownComponent>(entity);
            if (cd.Unit == CooldownUnit.Days && cd.TicksRemaining > 0)
                cd.TicksRemaining--;
        }
    }

    public static int GetFoodCapForToday(EntityWorld state, int foodType)
    {
        // Per-food formula: BasePerDay + PerFarm * (count of matching farm building).
        switch (foodType)
        {
            case FoodType.Grass:
                return Balance.Sleep.Grass.BasePerDay + Balance.Sleep.Grass.PerFarm * 0;
            case FoodType.Carrot:
                return Balance.Sleep.Carrot.BasePerDay + Balance.Sleep.Carrot.PerFarm * CountFarms<CarrotFarmComponent>(state);
            case FoodType.Apple:
                return Balance.Sleep.Apple.BasePerDay + Balance.Sleep.Apple.PerFarm * CountFarms<AppleOrchardComponent>(state);
            case FoodType.Mushroom:
                return Balance.Sleep.Mushroom.BasePerDay + Balance.Sleep.Mushroom.PerFarm * CountFarms<MushroomCaveComponent>(state);
            default:
                return 0;
        }
    }

    private static int CountFarms<T>(EntityWorld state) where T : unmanaged, Deterministic.GameFramework.ECS.IComponent
    {
        int count = 0;
        foreach (var _ in state.Filter<T>()) count++;
        return count;
    }
}
