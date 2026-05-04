using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;
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

            var helperPlayerEntity = state.GetComponent<InteractRequestComponent>(playerEntity).Target;
            if (!state.TryResolve<HelperPlayerArchetype>(helperPlayerEntity, out var hpRef)) continue;

            TryExchange(state, playerEntity, hpRef);
        }
    }

    private static void TryExchange(EntityWorld state, Entity playerEntity, HelperPlayerRef hpRef)
    {
        var ctx = state.Ctx(playerEntity);

        var grEntity = InteractFeedback.GetGlobalResourcesEntity(state);
        if (grEntity == Entity.Null) return;

        if (hpRef.HelperPlayer.HasAnyResources())
        {
            PickupBagFromHelperPlayer(ctx, playerEntity, hpRef, grEntity);
            return;
        }

        bool didExchange = hpRef.HelperPlayer.Type switch
        {
            HelperType.Seller => LoadMilkIntoSeller(hpRef, grEntity, state),
            HelperType.Builder => LoadCoinsIntoBuilder(hpRef, grEntity, state),
            HelperType.Milker when hpRef.HelperPlayer.WantedFoodType >= 0 => LoadFoodIntoMilker(hpRef, grEntity, state),
            _ => false,
        };

        if (didExchange)
            InteractFeedback.Success(ctx, playerEntity, hpRef.Entity);
    }

    private static void PickupBagFromHelperPlayer(Context ctx, Entity playerEntity, HelperPlayerRef hpRef, Entity grEntity)
    {
        ref var hp = ref hpRef.HelperPlayer;
        ref var globalRes = ref ctx.State.GetComponent<GlobalResourcesComponent>(grEntity);

        string gainedKey =
            hp.BagGrass > 0 ? StateKeys.Grass
            : hp.BagCarrot > 0 ? StateKeys.Carrot
            : hp.BagApple > 0 ? StateKeys.Apple
            : hp.BagMushroom > 0 ? StateKeys.Mushroom
            : hp.BagMilk > 0 ? StateKeys.Milk
            : hp.BagCoins > 0 ? StateKeys.Coins
            : "";

        globalRes.AddFood(FoodType.Grass, hp.BagGrass);
        globalRes.AddFood(FoodType.Carrot, hp.BagCarrot);
        globalRes.AddFood(FoodType.Apple, hp.BagApple);
        globalRes.AddFood(FoodType.Mushroom, hp.BagMushroom);
        globalRes.AddMilkProduct(MilkProduct.Milk, hp.BagMilk);
        globalRes.Coins += hp.BagCoins;
        hp.ClearBag();

        ILogger.Log($"[HelperPlayerExchangeSystem] Main player picked up bag from helper-player {hpRef.Entity.Id}");
        InteractFeedback.Success(ctx, playerEntity, hpRef.Entity, string.IsNullOrEmpty(gainedKey) ? null : gainedKey);
    }

    private static bool LoadMilkIntoSeller(HelperPlayerRef hpRef, Entity grEntity, EntityWorld state)
    {
        ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
        int amount = System.Math.Min(hpRef.HelperPlayer.BagCapacity - hpRef.HelperPlayer.GetBagTotal(), globalRes.Milk);
        if (amount <= 0) return false;
        globalRes.Milk -= amount;
        hpRef.HelperPlayer.BagMilk += amount;
        return true;
    }

    private static bool LoadCoinsIntoBuilder(HelperPlayerRef hpRef, Entity grEntity, EntityWorld state)
    {
        ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
        int amount = System.Math.Max(0, System.Math.Min(hpRef.HelperPlayer.BagCapacity - hpRef.HelperPlayer.BagCoins, globalRes.Coins));
        if (amount <= 0) return false;
        globalRes.Coins -= amount;
        hpRef.HelperPlayer.BagCoins += amount;
        return true;
    }

    private static bool LoadFoodIntoMilker(HelperPlayerRef hpRef, Entity grEntity, EntityWorld state)
    {
        ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
        int foodType = hpRef.HelperPlayer.WantedFoodType;
        int amount = System.Math.Max(0, System.Math.Min(hpRef.HelperPlayer.BagCapacity - hpRef.HelperPlayer.GetBagTotal(), globalRes.GetFood(foodType)));
        if (amount <= 0) return false;

        for (int i = 0; i < amount; i++) globalRes.ConsumeFood(foodType);
        switch (foodType)
        {
            case FoodType.Grass: hpRef.HelperPlayer.BagGrass += amount; break;
            case FoodType.Carrot: hpRef.HelperPlayer.BagCarrot += amount; break;
            case FoodType.Apple: hpRef.HelperPlayer.BagApple += amount; break;
            case FoodType.Mushroom: hpRef.HelperPlayer.BagMushroom += amount; break;
        }

        int prereq = FoodType.PrerequisiteProduct(foodType);
        if (prereq >= 0)
        {
            int prereqNeeded = System.Math.Max(1, amount / 4);
            int prereqCapacity = hpRef.HelperPlayer.BagCapacity - hpRef.HelperPlayer.GetBagTotal();
            int prereqAvailable = globalRes.GetMilkProduct(prereq);
            int prereqAmount = System.Math.Min(prereqNeeded, System.Math.Min(prereqCapacity, prereqAvailable));
            for (int i = 0; i < prereqAmount; i++) globalRes.ConsumeMilkProduct(prereq);
            hpRef.HelperPlayer.AddBagMilkProduct(prereq, prereqAmount);
        }

        return true;
    }
}
