using Deterministic.GameFramework.ECS;
using Template.Shared.Components;
using Deterministic.GameFramework.Utils.Logging;

namespace Template.Shared.Systems;

public static class SleepLogic
{
    public const int FoodCapPerFarmPerDay = 5;
    public const int GrassCapBaseDay = 12;

    public static void AdvanceDay(EntityWorld state)
    {
        int newDay = 0;
        foreach (var grEntity in state.Filter<GlobalResourcesComponent>())
        {
            ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            gr.DayCounter++;
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
        }

        int product = (newDay % 3) == 2 ? SellProduct.Cow : SellProduct.Milk;
        foreach (var signEntity in state.Filter<SellPointSignComponent>())
        {
            ref var sign = ref state.GetComponent<SellPointSignComponent>(signEntity);
            sign.CurrentProduct = product;
        }

        // Love houses also reset their breed cooldown on sleep — same logic as cow exhaust.
        foreach (var lhEntity in state.Filter<LoveHouseComponent>())
        {
            ref var lh = ref state.GetComponent<LoveHouseComponent>(lhEntity);
            lh.CooldownTicksRemaining = 0;
        }
    }

    public static int GetFoodCapForToday(EntityWorld state, int foodType)
    {
        if (foodType == FoodType.Grass) return GrassCapBaseDay;

        int farmCount = 0;
        switch (foodType)
        {
            case FoodType.Carrot:
                foreach (var _ in state.Filter<CarrotFarmComponent>()) farmCount++;
                break;
            case FoodType.Apple:
                foreach (var _ in state.Filter<AppleOrchardComponent>()) farmCount++;
                break;
            case FoodType.Mushroom:
                foreach (var _ in state.Filter<MushroomCaveComponent>()) farmCount++;
                break;
        }
        return farmCount * FoodCapPerFarmPerDay;
    }
}
