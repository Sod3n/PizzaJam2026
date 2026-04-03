using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Actions;

/// <summary>
/// Shared interaction operations used by both player (InteractActionService) and helpers (HelperSystem).
/// Single source of truth for milking, selling, building, and food harvesting logic.
/// </summary>
public static class InteractionLogic
{
    /// <summary>
    /// Milk a cow: consume food, produce milk product, advance exhaust.
    /// Returns true if milk was produced, false if blocked or no resources.
    /// </summary>
    public static bool MilkCow(EntityWorld state, Entity cowEntity, int foodToUse, int exhaustPerClick, out bool cowDone)
    {
        cowDone = false;
        if (!state.HasComponent<CowComponent>(cowEntity)) return false;

        ref var globalRes = ref GetGlobalRes(state, out Entity gre);
        if (gre == Entity.Null) return false;

        ref var cow = ref state.GetComponent<CowComponent>(cowEntity);

        if (globalRes.GetFood(foodToUse) <= 0 || cow.Exhaust >= cow.MaxExhaust) return false;

        bool isPreferred = foodToUse == cow.PreferredFood;
        int milkAmount = isPreferred ? 5 : 1;

        // Non-preferred food: 50% chance to produce nothing (food still consumed)
        bool milkBlocked = false;
        if (!isPreferred)
        {
            var gameTime = state.GetCustomData<IGameTime>();
            uint milkSeed = (uint)(cowEntity.Id * 31 + (gameTime?.CurrentTick ?? 0));
            var milkRng = new DeterministicRandom(milkSeed);
            milkBlocked = milkRng.NextInt(100) < 50;
        }

        int remaining = cow.MaxExhaust - cow.Exhaust;
        int availableFood = globalRes.GetFood(foodToUse);
        int clicks = System.Math.Min(exhaustPerClick, System.Math.Min(remaining, availableFood));
        for (int i = 0; i < clicks; i++)
            globalRes.ConsumeFood(foodToUse);
        int milkProduct = FoodType.ToMilkProduct(foodToUse);
        if (!milkBlocked)
            globalRes.AddMilkProduct(milkProduct, milkAmount * clicks);
        cow.Exhaust += clicks;

        cowDone = cow.Exhaust >= cow.MaxExhaust || globalRes.GetFood(foodToUse) <= 0;
        return !milkBlocked;
    }

    /// <summary>
    /// Sell milk products from global resources. Returns total coins earned.
    /// </summary>
    public static int SellFromGlobal(EntityWorld state, int count)
    {
        ref var globalRes = ref GetGlobalRes(state, out Entity gre);
        if (gre == Entity.Null) return 0;

        int totalCoins = 0;
        for (int i = 0; i < count; i++)
        {
            int price = globalRes.ConsumeAndPriceMilkProduct();
            if (price <= 0) break;
            totalCoins += price;
        }
        if (totalCoins > 0)
            globalRes.Coins += totalCoins;
        return totalCoins;
    }

    /// <summary>
    /// Deposit coins into a land plot. Returns actual amount deposited.
    /// If leaveOneForPlayer is true, stops at Threshold-1.
    /// Sets landComplete=true if land reached its threshold.
    /// </summary>
    public static int DepositToLand(EntityWorld state, Entity landEntity, int coins, bool leaveOneForPlayer, out bool landComplete)
    {
        landComplete = false;
        if (!state.HasComponent<LandComponent>(landEntity)) return 0;

        ref var land = ref state.GetComponent<LandComponent>(landEntity);
        int maxDeposit = land.Threshold - land.CurrentCoins;
        if (leaveOneForPlayer)
            maxDeposit = System.Math.Max(0, maxDeposit - 1);
        int deposit = System.Math.Min(coins, maxDeposit);
        if (deposit <= 0) return 0;

        land.CurrentCoins += deposit;
        landComplete = land.CurrentCoins >= land.Threshold;
        return deposit;
    }

    /// <summary>
    /// Harvest food from a food entity. Returns the food type harvested.
    /// </summary>
    public static bool HarvestFood(EntityWorld state, Entity foodEntity, int amount, out int foodType, out bool destroyed)
    {
        foodType = FoodType.Grass;
        destroyed = false;
        if (!state.HasComponent<GrassComponent>(foodEntity)) return false;

        ref var grass = ref state.GetComponent<GrassComponent>(foodEntity);
        foodType = grass.FoodType;
        int actual = System.Math.Min(amount, grass.Durability);
        grass.Durability -= actual;
        destroyed = grass.Durability <= 0;
        return actual > 0;
    }

    /// <summary>
    /// Pick best food for a cow: house selection → preferred → any available (highest tier first).
    /// Returns -1 if no food available.
    /// </summary>
    public static int ResolveFoodForCow(EntityWorld state, CowComponent cow, int houseSelectedFood)
    {
        ref var globalRes = ref GetGlobalRes(state, out Entity gre);
        if (gre == Entity.Null) return -1;

        if (houseSelectedFood >= 0 && globalRes.GetFood(houseSelectedFood) > 0)
            return houseSelectedFood;
        if (globalRes.GetFood(cow.PreferredFood) > 0)
            return cow.PreferredFood;
        for (int f = FoodType.Mushroom; f >= FoodType.Grass; f--)
        {
            if (globalRes.GetFood(f) > 0) return f;
        }
        return -1;
    }

    /// <summary>Fire visual feedback on an entity.</summary>
    public static void FireInteracted(EntityWorld state, Entity target, string param = "")
    {
        state.AddComponent(target, new EnterStateComponent { Key = StateKeys.Interacted, Param = param, Age = 0 });
    }

    /// <summary>Fire gained-resource icon on an entity.</summary>
    public static void FireGainedResource(EntityWorld state, Entity target, string resourceKey)
    {
        state.AddComponent(target, new EnterStateComponent { Key = StateKeys.GainedResource, Param = resourceKey, Age = 0 });
    }

    /// <summary>Get ref to the singleton GlobalResourcesComponent.</summary>
    public static ref GlobalResourcesComponent GetGlobalRes(EntityWorld state, out Entity entity)
    {
        foreach (var ge in state.Filter<GlobalResourcesComponent>())
        {
            entity = ge;
            return ref state.GetComponent<GlobalResourcesComponent>(ge);
        }
        entity = Entity.Null;
        // This should never happen in normal gameplay
        throw new System.InvalidOperationException("No GlobalResourcesComponent found");
    }
}
