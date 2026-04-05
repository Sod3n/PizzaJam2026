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
    /// The cow's PreferredFood determines its max tier. The method automatically selects
    /// the highest tier recipe available based on global resources (food + prerequisite product).
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

        // If the hint food is valid and available, try to use it at its tier
        // Otherwise fall back to highest available tier
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
                // Hint food available but prerequisite product missing — fall back
                foodToUse = ResolveHighestTierFood(ref globalRes, cowMaxTier, out prereqProduct);
            }
        }
        else
        {
            foodToUse = ResolveHighestTierFood(ref globalRes, cowMaxTier, out prereqProduct);
        }

        if (foodToUse < 0) { cowDone = true; return false; }

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

        cowDone = cow.Exhaust >= cow.MaxExhaust || ResolveHighestTierFood(ref globalRes, cowMaxTier, out _) < 0;
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
    /// Pick best food for a cow using the linear chain: tries the highest tier the cow supports
    /// (checking both food and prerequisite availability), falling back to lower tiers.
    /// The houseSelectedFood is tried first if valid.
    /// Returns -1 if no food available.
    /// </summary>
    public static int ResolveFoodForCow(EntityWorld state, CowComponent cow, int houseSelectedFood)
    {
        ref var globalRes = ref GetGlobalRes(state, out Entity gre);
        if (gre == Entity.Null) return -1;

        int cowMaxTier = FoodType.MaxTier(cow.PreferredFood);

        // If house has a selected food, try it first (if cow supports that tier)
        if (houseSelectedFood >= 0 && houseSelectedFood <= cowMaxTier && globalRes.GetFood(houseSelectedFood) > 0)
        {
            int prereq = FoodType.PrerequisiteProduct(houseSelectedFood);
            if (prereq < 0 || globalRes.GetMilkProduct(prereq) > 0)
                return houseSelectedFood;
        }

        // Fall back to highest available tier
        int food = ResolveHighestTierFood(ref globalRes, cowMaxTier, out _);
        return food;
    }

    /// <summary>
    /// Milk a cow, placing the product into a helper's bag instead of global resources.
    /// Consumes food from global resources, produces into helper bag.
    /// Uses the linear chain: auto-selects highest tier based on cow max tier and global resources.
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
                foodToUse = ResolveHighestTierFood(ref globalRes, cowMaxTier, out prereqProduct);
            }
        }
        else
        {
            foodToUse = ResolveHighestTierFood(ref globalRes, cowMaxTier, out prereqProduct);
        }

        if (foodToUse < 0) { cowDone = true; return false; }

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

        cowDone = cow.Exhaust >= cow.MaxExhaust || ResolveHighestTierFood(ref globalRes, cowMaxTier, out _) < 0;
        return producedMilk;
    }

    /// <summary>
    /// Milk a cow using food AND prerequisite products from the helper's own bag.
    /// Places the milk product into the helper's bag.
    /// Uses the linear chain: auto-selects highest tier based on cow max tier and bag contents.
    /// Returns true if milk was produced this click, false otherwise.
    /// </summary>
    public static bool MilkCowFromBag(EntityWorld state, Entity cowEntity, int hintFoodType, int exhaustPerClick, ref HelperComponent helperBag, out bool cowDone)
    {
        cowDone = false;
        if (!state.HasComponent<CowComponent>(cowEntity)) return false;

        ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
        if (cow.Exhaust >= cow.MaxExhaust) { cowDone = true; return false; }

        int cowMaxTier = FoodType.MaxTier(cow.PreferredFood);

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
                foodToUse = ResolveHighestTierFoodFromBag(ref helperBag, cowMaxTier, out prereqProduct);
            }
        }
        else
        {
            foodToUse = ResolveHighestTierFoodFromBag(ref helperBag, cowMaxTier, out prereqProduct);
        }

        if (foodToUse < 0) { cowDone = true; return false; }

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

        cowDone = cow.Exhaust >= cow.MaxExhaust || ResolveHighestTierFoodFromBag(ref helperBag, cowMaxTier, out _) < 0;
        return producedMilk;
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
