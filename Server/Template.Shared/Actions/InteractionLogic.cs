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
    /// Resolve the highest tier recipe the cow can produce given global resources.
    /// Searches from the cow's max tier down to tier 0 (Grass/Milk).
    /// Returns the food type to use for that tier, or -1 if nothing is possible.
    /// Also outputs the prerequisite milk product that will be consumed (-1 for tier 0).
    /// </summary>
    public static int ResolveHighestTierFood(ref GlobalResourcesComponent globalRes, int cowMaxTier, out int prereqProduct)
    {
        prereqProduct = -1;
        // Try from highest tier the cow supports down to Grass
        for (int tier = cowMaxTier; tier >= FoodType.Grass; tier--)
        {
            if (globalRes.GetFood(tier) <= 0) continue;

            int prereq = FoodType.PrerequisiteProduct(tier);
            if (prereq >= 0 && globalRes.GetMilkProduct(prereq) <= 0) continue;

            prereqProduct = prereq;
            return tier;
        }
        return -1;
    }

    /// <summary>
    /// Resolve the highest tier recipe from a helper's bag.
    /// </summary>
    public static int ResolveHighestTierFoodFromBag(ref HelperComponent bag, int cowMaxTier, out int prereqProduct)
    {
        prereqProduct = -1;
        for (int tier = cowMaxTier; tier >= FoodType.Grass; tier--)
        {
            if (bag.GetBagFood(tier) <= 0) continue;

            int prereq = FoodType.PrerequisiteProduct(tier);
            if (prereq >= 0 && bag.GetBagMilkProduct(prereq) <= 0) continue;

            prereqProduct = prereq;
            return tier;
        }
        return -1;
    }

    /// <summary>
    /// Milk a cow using the LINEAR PROGRESSION chain.
    /// The cow's PreferredFood determines its max tier. The hintFoodType (from the house food sign)
    /// is used as the EXACT recipe — no fallback to lower tiers.
    /// If the hint food or its prerequisite is missing, milking stops (cowDone = true).
    /// Consumes food and prerequisite product, produces the tier's milk product.
    /// Returns true if milk was produced this click, false otherwise.
    /// </summary>
    public static bool MilkCow(EntityWorld state, Entity cowEntity, int hintFoodType, int exhaustPerClick, out bool cowDone)
    {
        cowDone = false;
        if (!state.HasComponent<CowComponent>(cowEntity)) return false;

        ref var globalRes = ref GetGlobalRes(state, out Entity gre);
        if (gre == Entity.Null) return false;

        ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
        if (cow.Exhaust >= cow.MaxExhaust) { cowDone = true; return false; }


        int cowMaxTier = FoodType.MaxTier(cow.PreferredFood);

        // Strict: use ONLY the hint food type (house food sign selection). No fallback.
        int foodToUse;
        int prereqProduct;

        if (hintFoodType >= 0 && hintFoodType <= cowMaxTier && globalRes.GetFood(hintFoodType) > 0)
        {
            int prereq = FoodType.PrerequisiteProduct(hintFoodType);
            if (prereq < 0 || globalRes.GetMilkProduct(prereq) > 0)
            {
                foodToUse = hintFoodType;
                prereqProduct = prereq;
            }
            else
            {
                // Prerequisite product missing — do NOT fall back, stop milking
                cowDone = true;
                return false;
            }
        }
        else
        {
            // Selected food not available or not supported by cow — stop milking
            cowDone = true;
            return false;
        }

        bool isPreferred = foodToUse == cow.PreferredFood;
        int milkAmount = isPreferred ? 2 : 1;

        // Non-preferred food: 50% chance to produce nothing (food still consumed)
        bool milkBlocked = false;
        if (!isPreferred)
        {
            var gameTime = state.GetCustomData<IGameTime>();
            uint milkSeed = (uint)(cowEntity.Id * 31 + (gameTime?.CurrentTick ?? 0));
            var milkRng = new DeterministicRandom(milkSeed);
            milkBlocked = milkRng.NextInt(100) < 50;
        }

        // Consume food and advance exhaust
        int remaining = cow.MaxExhaust - cow.Exhaust;
        int availableFood = globalRes.GetFood(foodToUse);
        int clicks = System.Math.Min(exhaustPerClick, System.Math.Min(remaining, availableFood));
        for (int i = 0; i < clicks; i++)
            globalRes.ConsumeFood(foodToUse);
        cow.Exhaust += clicks;

        // Only produce milk every 4th click (when exhaust reaches a multiple of 4)
        bool producedMilk = false;
        if (!milkBlocked && cow.Exhaust % 4 == 0)
        {
            // Consume prerequisite product (if any)
            if (prereqProduct >= 0)
                globalRes.ConsumeMilkProduct(prereqProduct);

            int milkProduct = FoodType.ToMilkProduct(foodToUse);
            globalRes.AddMilkProduct(milkProduct, milkAmount * 4);
            producedMilk = true;
        }

        // Check if we can continue with the SAME recipe (no fallback)
        cowDone = cow.Exhaust >= cow.MaxExhaust
            || (cow.MaxExhaust - cow.Exhaust) < 4
            || globalRes.GetFood(foodToUse) <= 0
            || (prereqProduct >= 0 && globalRes.GetMilkProduct(prereqProduct) <= 0);
        return producedMilk;
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
    /// Resolve the exact food for a cow based on the house's selected food.
    /// Strict: only allows the selected food type — no fallback to lower tiers.
    /// Returns -1 if the selected food or its prerequisite is unavailable.
    /// </summary>
    public static int ResolveFoodForCow(EntityWorld state, CowComponent cow, int houseSelectedFood)
    {
        ref var globalRes = ref GetGlobalRes(state, out Entity gre);
        if (gre == Entity.Null) return -1;

        int cowMaxTier = FoodType.MaxTier(cow.PreferredFood);

        // Strict: only allow the house's selected food, no fallback
        if (houseSelectedFood >= 0 && houseSelectedFood <= cowMaxTier && globalRes.GetFood(houseSelectedFood) > 0)
        {
            int prereq = FoodType.PrerequisiteProduct(houseSelectedFood);
            if (prereq < 0 || globalRes.GetMilkProduct(prereq) > 0)
                return houseSelectedFood;
        }

        // No fallback — selected food or prerequisite not available
        return -1;
    }

    /// <summary>
    /// Milk a cow, placing the product into a helper's bag instead of global resources.
    /// Consumes food from global resources, produces into helper bag.
    /// Uses the hint food type strictly — no fallback to lower tiers.
    /// Returns true if milk was produced this click, false otherwise.
    /// </summary>
    public static bool MilkCowToBag(EntityWorld state, Entity cowEntity, int hintFoodType, int exhaustPerClick, ref HelperComponent helperBag, out bool cowDone)
    {
        cowDone = false;
        if (!state.HasComponent<CowComponent>(cowEntity)) return false;

        ref var globalRes = ref GetGlobalRes(state, out Entity gre);
        if (gre == Entity.Null) return false;

        ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
        if (cow.Exhaust >= cow.MaxExhaust) { cowDone = true; return false; }


        int cowMaxTier = FoodType.MaxTier(cow.PreferredFood);

        // Strict: use ONLY the hint food type. No fallback.
        int foodToUse;
        int prereqProduct;

        if (hintFoodType >= 0 && hintFoodType <= cowMaxTier && globalRes.GetFood(hintFoodType) > 0)
        {
            int prereq = FoodType.PrerequisiteProduct(hintFoodType);
            if (prereq < 0 || globalRes.GetMilkProduct(prereq) > 0)
            {
                foodToUse = hintFoodType;
                prereqProduct = prereq;
            }
            else
            {
                // Prerequisite product missing — do NOT fall back, stop milking
                cowDone = true;
                return false;
            }
        }
        else
        {
            // Selected food not available or not supported by cow — stop milking
            cowDone = true;
            return false;
        }

        bool isPreferred = foodToUse == cow.PreferredFood;
        int milkAmount = isPreferred ? 2 : 1;

        // Non-preferred food: 50% chance to produce nothing (food still consumed)
        bool milkBlocked = false;
        if (!isPreferred)
        {
            var gameTime = state.GetCustomData<IGameTime>();
            uint milkSeed = (uint)(cowEntity.Id * 31 + (gameTime?.CurrentTick ?? 0));
            var milkRng = new DeterministicRandom(milkSeed);
            milkBlocked = milkRng.NextInt(100) < 50;
        }

        // Consume food from global and advance exhaust
        int remaining = cow.MaxExhaust - cow.Exhaust;
        int availableFood = globalRes.GetFood(foodToUse);
        int clicks = System.Math.Min(exhaustPerClick, System.Math.Min(remaining, availableFood));
        for (int i = 0; i < clicks; i++)
            globalRes.ConsumeFood(foodToUse);
        cow.Exhaust += clicks;

        // Only produce milk every 4th click
        bool producedMilk = false;
        if (!milkBlocked && cow.Exhaust % 4 == 0)
        {
            // Consume prerequisite product from global
            if (prereqProduct >= 0)
                globalRes.ConsumeMilkProduct(prereqProduct);

            int milkProduct = FoodType.ToMilkProduct(foodToUse);
            int totalMilk = milkAmount * 4;
            helperBag.AddBagMilkProduct(milkProduct, totalMilk);
            producedMilk = true;
        }

        // Check if we can continue with the SAME recipe (no fallback)
        cowDone = cow.Exhaust >= cow.MaxExhaust
            || (cow.MaxExhaust - cow.Exhaust) < 4
            || globalRes.GetFood(foodToUse) <= 0
            || (prereqProduct >= 0 && globalRes.GetMilkProduct(prereqProduct) <= 0);
        return producedMilk;
    }

    /// <summary>
    /// Milk a cow using food AND prerequisite products from the helper's own bag.
    /// Places the milk product into the helper's bag.
    /// Uses the hint food type strictly — no fallback to lower tiers.
    /// Returns true if milk was produced this click, false otherwise.
    /// </summary>
    public static bool MilkCowFromBag(EntityWorld state, Entity cowEntity, int hintFoodType, int exhaustPerClick, ref HelperComponent helperBag, out bool cowDone)
    {
        cowDone = false;
        if (!state.HasComponent<CowComponent>(cowEntity)) return false;

        ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
        if (cow.Exhaust >= cow.MaxExhaust) { cowDone = true; return false; }


        int cowMaxTier = FoodType.MaxTier(cow.PreferredFood);

        // Strict: use ONLY the hint food type. No fallback.
        int foodToUse;
        int prereqProduct;

        if (hintFoodType >= 0 && hintFoodType <= cowMaxTier && helperBag.GetBagFood(hintFoodType) > 0)
        {
            int prereq = FoodType.PrerequisiteProduct(hintFoodType);
            if (prereq < 0 || helperBag.GetBagMilkProduct(prereq) > 0)
            {
                foodToUse = hintFoodType;
                prereqProduct = prereq;
            }
            else
            {
                // Prerequisite product missing — do NOT fall back, stop milking
                cowDone = true;
                return false;
            }
        }
        else
        {
            // Selected food not available or not supported by cow — stop milking
            cowDone = true;
            return false;
        }

        bool isPreferred = foodToUse == cow.PreferredFood;
        int milkAmount = isPreferred ? 2 : 1;

        // Non-preferred food: 50% chance to produce nothing (food still consumed)
        bool milkBlocked = false;
        if (!isPreferred)
        {
            var gameTime = state.GetCustomData<IGameTime>();
            uint milkSeed = (uint)(cowEntity.Id * 31 + (gameTime?.CurrentTick ?? 0));
            var milkRng = new DeterministicRandom(milkSeed);
            milkBlocked = milkRng.NextInt(100) < 50;
        }

        // Consume food from bag and advance exhaust
        int remaining = cow.MaxExhaust - cow.Exhaust;
        int availableFood = helperBag.GetBagFood(foodToUse);
        int clicks = System.Math.Min(exhaustPerClick, System.Math.Min(remaining, availableFood));
        for (int i = 0; i < clicks; i++)
            helperBag.ConsumeBagFood(foodToUse);
        cow.Exhaust += clicks;

        // Only produce milk every 4th click
        bool producedMilk = false;
        if (!milkBlocked && cow.Exhaust % 4 == 0)
        {
            // Consume prerequisite product from bag
            if (prereqProduct >= 0)
                helperBag.ConsumeBagMilkProduct(prereqProduct);

            int milkProduct = FoodType.ToMilkProduct(foodToUse);
            int totalMilk = milkAmount * 4;
            helperBag.AddBagMilkProduct(milkProduct, totalMilk);
            producedMilk = true;
        }

        // Check if we can continue with the SAME recipe (no fallback)
        cowDone = cow.Exhaust >= cow.MaxExhaust
            || (cow.MaxExhaust - cow.Exhaust) < 4
            || helperBag.GetBagFood(foodToUse) <= 0
            || (prereqProduct >= 0 && helperBag.GetBagMilkProduct(prereqProduct) <= 0);
        return producedMilk;
    }

    /// <summary>
    /// Returns true if the entity is a valid interact target (used by both the
    /// highlight system and the interact action service so they agree on which
    /// entities are interactable).
    /// </summary>
    public static bool IsInteractable(EntityWorld state, Entity entity)
    {
        return state.HasComponent<GrassComponent>(entity)
            || state.HasComponent<CowComponent>(entity)
            || state.HasComponent<HouseComponent>(entity)
            || state.HasComponent<LoveHouseComponent>(entity)
            || state.HasComponent<FoodSignComponent>(entity)
            || state.HasComponent<WarehouseSignComponent>(entity)
            || state.HasComponent<SellPointComponent>(entity)
            || state.HasComponent<LandComponent>(entity)
            || state.HasComponent<FinalStructureComponent>(entity)
            || state.HasComponent<HelperComponent>(entity)
            || state.HasComponent<CarrotFarmComponent>(entity)
            || state.HasComponent<AppleOrchardComponent>(entity)
            || state.HasComponent<MushroomCaveComponent>(entity)
            || state.HasComponent<HelperAssistantComponent>(entity)
            || state.HasComponent<UpgradeGathererComponent>(entity)
            || state.HasComponent<UpgradeBuilderComponent>(entity)
            || state.HasComponent<UpgradeSellerComponent>(entity)
            || state.HasComponent<UpgradeAssistantComponent>(entity)
            || state.HasComponent<DecorationComponent>(entity)
            || state.HasComponent<WarehouseComponent>(entity);
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
