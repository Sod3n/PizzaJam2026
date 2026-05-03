using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Resource exchange between the main player and a helper-player (the helper-player is
// treated like a helper for exchange purposes).
//
// Match preconditions:
//   target has HelperPlayerComponent
//   player is NOT a helper-player (helper-players don't exchange with each other)
//
// Priority order:
//   1. helper-player has any resources → dump entire bag into global, popup the gained resource on the main player
//   2. Seller hp idle                  → load milk from global into helper's bag
//   3. Builder hp idle                 → load coins from global into helper's bag
//   4. Milker hp idle + WantedFoodType → load food + prerequisite milk into helper's bag
//
// On a successful exchange claims via Success (with the optional gained-resource key for path 1).
// If no path applies, falls through to InteractFallbackSystem (BuildingInfo popup if applicable).
public class HelperPlayerExchangeSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var helperPlayerEntity = req.Target;
            if (!state.HasComponent<HelperPlayerComponent>(helperPlayerEntity)) continue;

            TryExchange(state, playerEntity, helperPlayerEntity);
        }
    }

    private static void TryExchange(EntityWorld state, Entity playerEntity, Entity helperPlayerEntity)
    {
        var ctx = new Context(state, playerEntity, null!);

        var grEntity = InteractFeedback.GetGlobalResourcesEntity(state);
        if (grEntity == Entity.Null) return;

        var hpSnapshot = state.GetComponent<HelperPlayerComponent>(helperPlayerEntity);

        if (hpSnapshot.HasAnyResources())
        {
            string gainedKey = "";
            {
                ref var hp = ref state.GetComponent<HelperPlayerComponent>(helperPlayerEntity);
                ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
                if (hp.BagGrass > 0) gainedKey = StateKeys.Grass;
                else if (hp.BagCarrot > 0) gainedKey = StateKeys.Carrot;
                else if (hp.BagApple > 0) gainedKey = StateKeys.Apple;
                else if (hp.BagMushroom > 0) gainedKey = StateKeys.Mushroom;
                else if (hp.BagMilk > 0) gainedKey = StateKeys.Milk;
                else if (hp.BagCoins > 0) gainedKey = StateKeys.Coins;

                globalRes.AddFood(FoodType.Grass, hp.BagGrass);
                globalRes.AddFood(FoodType.Carrot, hp.BagCarrot);
                globalRes.AddFood(FoodType.Apple, hp.BagApple);
                globalRes.AddFood(FoodType.Mushroom, hp.BagMushroom);
                globalRes.AddMilkProduct(MilkProduct.Milk, hp.BagMilk);
                globalRes.Coins += hp.BagCoins;
                hp.ClearBag();
            }

            ILogger.Log($"[HelperPlayerExchangeSystem] Main player picked up bag from helper-player {helperPlayerEntity.Id}");
            InteractFeedback.Success(ctx, playerEntity, helperPlayerEntity, string.IsNullOrEmpty(gainedKey) ? null : gainedKey);
            return;
        }

        bool didExchange = false;

        if (hpSnapshot.Type == HelperType.Seller)
        {
            ref var hp = ref state.GetComponent<HelperPlayerComponent>(helperPlayerEntity);
            ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            int capacity = hp.BagCapacity - hp.GetBagTotal();
            int transferred = 0;
            while (transferred < capacity && globalRes.Milk > 0) { globalRes.Milk--; hp.BagMilk++; transferred++; }
            if (transferred > 0) didExchange = true;
        }
        else if (hpSnapshot.Type == HelperType.Builder)
        {
            ref var hp = ref state.GetComponent<HelperPlayerComponent>(helperPlayerEntity);
            ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            int needed = hp.BagCapacity - hp.BagCoins;
            int toGive = System.Math.Max(0, System.Math.Min(needed, globalRes.Coins));
            if (toGive > 0) { globalRes.Coins -= toGive; hp.BagCoins += toGive; didExchange = true; }
        }
        else if (hpSnapshot.Type == HelperType.Milker && hpSnapshot.WantedFoodType >= 0)
        {
            ref var hp = ref state.GetComponent<HelperPlayerComponent>(helperPlayerEntity);
            ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            int foodType = hp.WantedFoodType;
            int capacity = hp.BagCapacity - hp.GetBagTotal();
            int available = globalRes.GetFood(foodType);
            int toGive = System.Math.Max(0, System.Math.Min(capacity, available));
            if (toGive > 0)
            {
                for (int i = 0; i < toGive; i++) globalRes.ConsumeFood(foodType);
                switch (foodType)
                {
                    case FoodType.Grass: hp.BagGrass += toGive; break;
                    case FoodType.Carrot: hp.BagCarrot += toGive; break;
                    case FoodType.Apple: hp.BagApple += toGive; break;
                    case FoodType.Mushroom: hp.BagMushroom += toGive; break;
                }
                int prereq = FoodType.PrerequisiteProduct(foodType);
                if (prereq >= 0)
                {
                    int prereqNeeded = System.Math.Max(1, toGive / 4);
                    int prereqCapacity = hp.BagCapacity - hp.GetBagTotal();
                    int prereqAvailable = globalRes.GetMilkProduct(prereq);
                    int prereqToGive = System.Math.Min(prereqNeeded, System.Math.Min(prereqCapacity, prereqAvailable));
                    for (int i = 0; i < prereqToGive; i++) globalRes.ConsumeMilkProduct(prereq);
                    hp.AddBagMilkProduct(prereq, prereqToGive);
                }
                didExchange = true;
            }
        }

        if (didExchange)
            InteractFeedback.Success(ctx, playerEntity, helperPlayerEntity);
    }
}
