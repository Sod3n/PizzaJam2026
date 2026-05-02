using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
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
    /// Milk a cow with the selected food. All food types produce general milk.
    /// Returns true if milk was produced this click, false otherwise.
    /// </summary>
    /// <summary>
    /// Milk takes <see cref="ClicksPerMilk"/> clicks per unit produced (alias of
    /// <see cref="GameData.Balance.Cow.ClicksPerMilk"/>). Mid-cycle (counter > 0), additional
    /// clicks are allowed past MaxExhaust so the cow can finish the in-progress milk —
    /// this fixes the "kicked out with X clicks left" bug.
    /// </summary>
    public const int ClicksPerMilk = GameData.Balance.Cow.ClicksPerMilk;

    public static bool MilkCow(EntityWorld state, Entity cowEntity, int hintFoodType, int exhaustPerClick, out bool cowDone)
    {
        cowDone = false;
        if (!state.HasComponent<CowComponent>(cowEntity)) return false;

        ref var globalRes = ref GetGlobalRes(state, out Entity gre);
        if (gre == Entity.Null) return false;

        ref var cow = ref state.GetComponent<CowComponent>(cowEntity);

        // Cow done only when at max AND no mid-cycle progress to flush.
        if (cow.Exhaust >= cow.MaxExhaust && cow.MilkClickCounter == 0)
        {
            cowDone = true;
            return false;
        }

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
                cowDone = true;
                return false;
            }
        }
        else
        {
            cowDone = true;
            return false;
        }

        bool isPreferred = cow.IsFoodPreferred(foodToUse);
        var gameTime = state.GetCustomData<IGameTime>();

        // Allow clicks past MaxExhaust just enough to finish a mid-cycle milk.
        int exhaustHeadroom = System.Math.Max(0, cow.MaxExhaust - cow.Exhaust);
        int clicksToFinishCycle = cow.MilkClickCounter > 0 ? (ClicksPerMilk - cow.MilkClickCounter) : 0;
        int allowedByExhaust = System.Math.Max(exhaustHeadroom, clicksToFinishCycle);
        int availableFood = globalRes.GetFood(foodToUse);
        int clicks = System.Math.Min(exhaustPerClick, System.Math.Min(allowedByExhaust, availableFood));
        if (clicks <= 0) { cowDone = true; return false; }

        int milksProduced = 0;
        for (int i = 0; i < clicks; i++)
        {
            globalRes.ConsumeFood(foodToUse);
            cow.RecordFed(foodToUse);
            if (cow.Exhaust < cow.MaxExhaust) cow.Exhaust++;
            cow.MilkClickCounter++;
            if (cow.MilkClickCounter >= ClicksPerMilk)
            {
                cow.MilkClickCounter = 0;
                bool blocked = false;
                if (!isPreferred)
                {
                    uint milkSeed = (uint)(cowEntity.Id * 31 + (gameTime?.CurrentTick ?? 0) + (uint)(i * 17));
                    var milkRng = new DeterministicRandom(milkSeed);
                    blocked = milkRng.NextInt(100) < GameData.Balance.Cow.NonPreferredFoodFailPercent;
                }
                if (!blocked) milksProduced++;
            }
        }
        if (milksProduced > 0)
            globalRes.AddMilkProduct(MilkProduct.Milk, milksProduced);

        cowDone = (cow.Exhaust >= cow.MaxExhaust && cow.MilkClickCounter == 0)
            || globalRes.GetFood(foodToUse) <= 0
            || (prereqProduct >= 0 && globalRes.GetMilkProduct(prereqProduct) <= 0);
        return milksProduced > 0;
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
    /// Resolve the exact food for a cow based on the cow's selected food.
    /// Strict: only allows the selected food type — no fallback to lower tiers.
    /// Returns -1 if the selected food or its prerequisite is unavailable.
    /// </summary>
    public static int ResolveFoodForCow(EntityWorld state, CowComponent cow, int houseSelectedFood)
    {
        ref var globalRes = ref GetGlobalRes(state, out Entity gre);
        if (gre == Entity.Null) return -1;

        int cowMaxTier = FoodType.MaxTier(cow.PreferredFood);

        // Strict: only allow the selected supported food, no fallback.
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
        if (cow.Exhaust >= cow.MaxExhaust && cow.MilkClickCounter == 0) { cowDone = true; return false; }

        int cowMaxTier = FoodType.MaxTier(cow.PreferredFood);

        int foodToUse;
        int prereqProduct;
        if (hintFoodType >= 0 && hintFoodType <= cowMaxTier && globalRes.GetFood(hintFoodType) > 0)
        {
            int prereq = FoodType.PrerequisiteProduct(hintFoodType);
            if (prereq < 0 || globalRes.GetMilkProduct(prereq) > 0)
            { foodToUse = hintFoodType; prereqProduct = prereq; }
            else { cowDone = true; return false; }
        }
        else { cowDone = true; return false; }

        bool isPreferred = cow.IsFoodPreferred(foodToUse);
        var gameTime = state.GetCustomData<IGameTime>();

        int exhaustHeadroom = System.Math.Max(0, cow.MaxExhaust - cow.Exhaust);
        int clicksToFinishCycle = cow.MilkClickCounter > 0 ? (ClicksPerMilk - cow.MilkClickCounter) : 0;
        int allowedByExhaust = System.Math.Max(exhaustHeadroom, clicksToFinishCycle);
        int availableFood = globalRes.GetFood(foodToUse);
        int clicks = System.Math.Min(exhaustPerClick, System.Math.Min(allowedByExhaust, availableFood));
        if (clicks <= 0) { cowDone = true; return false; }

        int milksProduced = 0;
        for (int i = 0; i < clicks; i++)
        {
            globalRes.ConsumeFood(foodToUse);
            cow.RecordFed(foodToUse);
            if (cow.Exhaust < cow.MaxExhaust) cow.Exhaust++;
            cow.MilkClickCounter++;
            if (cow.MilkClickCounter >= ClicksPerMilk)
            {
                cow.MilkClickCounter = 0;
                bool blocked = false;
                if (!isPreferred)
                {
                    uint milkSeed = (uint)(cowEntity.Id * 31 + (gameTime?.CurrentTick ?? 0) + (uint)(i * 17));
                    var milkRng = new DeterministicRandom(milkSeed);
                    blocked = milkRng.NextInt(100) < GameData.Balance.Cow.NonPreferredFoodFailPercent;
                }
                if (!blocked) milksProduced++;
            }
        }
        if (milksProduced > 0)
            helperBag.AddBagMilkProduct(MilkProduct.Milk, milksProduced);

        cowDone = (cow.Exhaust >= cow.MaxExhaust && cow.MilkClickCounter == 0)
            || globalRes.GetFood(foodToUse) <= 0
            || (prereqProduct >= 0 && globalRes.GetMilkProduct(prereqProduct) <= 0);
        return milksProduced > 0;
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
        if (cow.Exhaust >= cow.MaxExhaust && cow.MilkClickCounter == 0) { cowDone = true; return false; }

        int cowMaxTier = FoodType.MaxTier(cow.PreferredFood);

        int foodToUse;
        int prereqProduct;
        if (hintFoodType >= 0 && hintFoodType <= cowMaxTier && helperBag.GetBagFood(hintFoodType) > 0)
        {
            int prereq = FoodType.PrerequisiteProduct(hintFoodType);
            if (prereq < 0 || helperBag.GetBagMilkProduct(prereq) > 0)
            { foodToUse = hintFoodType; prereqProduct = prereq; }
            else { cowDone = true; return false; }
        }
        else { cowDone = true; return false; }

        bool isPreferred = cow.IsFoodPreferred(foodToUse);
        var gameTime = state.GetCustomData<IGameTime>();

        int exhaustHeadroom = System.Math.Max(0, cow.MaxExhaust - cow.Exhaust);
        int clicksToFinishCycle = cow.MilkClickCounter > 0 ? (ClicksPerMilk - cow.MilkClickCounter) : 0;
        int allowedByExhaust = System.Math.Max(exhaustHeadroom, clicksToFinishCycle);
        int availableFood = helperBag.GetBagFood(foodToUse);
        int clicks = System.Math.Min(exhaustPerClick, System.Math.Min(allowedByExhaust, availableFood));
        if (clicks <= 0) { cowDone = true; return false; }

        int milksProduced = 0;
        for (int i = 0; i < clicks; i++)
        {
            helperBag.ConsumeBagFood(foodToUse);
            cow.RecordFed(foodToUse);
            if (cow.Exhaust < cow.MaxExhaust) cow.Exhaust++;
            cow.MilkClickCounter++;
            if (cow.MilkClickCounter >= ClicksPerMilk)
            {
                cow.MilkClickCounter = 0;
                bool blocked = false;
                if (!isPreferred)
                {
                    uint milkSeed = (uint)(cowEntity.Id * 31 + (gameTime?.CurrentTick ?? 0) + (uint)(i * 17));
                    var milkRng = new DeterministicRandom(milkSeed);
                    blocked = milkRng.NextInt(100) < GameData.Balance.Cow.NonPreferredFoodFailPercent;
                }
                if (!blocked) milksProduced++;
            }
        }
        if (milksProduced > 0)
            helperBag.AddBagMilkProduct(MilkProduct.Milk, milksProduced);

        cowDone = (cow.Exhaust >= cow.MaxExhaust && cow.MilkClickCounter == 0)
            || helperBag.GetBagFood(foodToUse) <= 0
            || (prereqProduct >= 0 && helperBag.GetBagMilkProduct(prereqProduct) <= 0);
        return milksProduced > 0;
    }

    /// <summary>
    /// Find the nearest interactable inside the player's interaction zone.
    /// Single source of truth — used by both <see cref="Systems.InteractHighlightSystem"/>
    /// (to highlight the candidate target) and <see cref="InteractActionService"/>
    /// (to dispatch the interact action). Behaviour stays in lockstep automatically;
    /// just adding a new component to <see cref="IsInteractable"/> wires it up everywhere.
    /// </summary>
    public static Entity FindNearestInteractableInZone(EntityWorld state, Entity playerEntity, Entity zoneEntity)
    {
        if (zoneEntity == Entity.Null || !state.HasComponent<Area2D>(zoneEntity)) return Entity.Null;

        ref var area = ref state.GetComponent<Area2D>(zoneEntity);
        if (!area.HasOverlappingBodies) return Entity.Null;
        if (!state.HasComponent<Transform2D>(playerEntity)) return Entity.Null;

        var playerPos = state.GetComponent<Transform2D>(playerEntity).Position;
        Entity nearest = Entity.Null;
        Float minDistSq = (Float)999999f;

        for (int i = 0; i < area.OverlappingEntities.Count; i++)
        {
            var entity = new Entity(area.OverlappingEntities[i]);
            if (entity == playerEntity) continue;
            if (entity == zoneEntity) continue;
            if (!state.HasComponent<Transform2D>(entity)) continue;
            if (!IsInteractable(state, entity)) continue;

            var pos = state.GetComponent<Transform2D>(entity).Position;
            var distSq = Vector2.DistanceSquared(playerPos, pos);
            if (distSq < minDistSq) { minDistSq = distSq; nearest = entity; }
        }

        return nearest;
    }

    /// <summary>
    /// Returns true if the entity is a valid interact target.
    /// **Single registration point** — adding a new component here makes it
    /// highlightable AND dispatchable simultaneously (the highlight system and
    /// the interact action service both consume <see cref="FindNearestInteractableInZone"/>).
    /// </summary>
    public static bool IsInteractable(EntityWorld state, Entity entity)
    {
        // Cows that have been sold are visible but non-interactable.
        if (state.HasComponent<CowForSaleComponent>(entity)) return false;
        return state.HasComponent<GrassComponent>(entity)
            || state.HasComponent<CowComponent>(entity)
            || state.HasComponent<HouseComponent>(entity)
            || state.HasComponent<LoveHouseComponent>(entity)
            || state.HasComponent<FoodSignComponent>(entity)
            || state.HasComponent<RoleSignComponent>(entity)
            || state.HasComponent<LandSignComponent>(entity)
            || state.HasComponent<LandPriceSignComponent>(entity)
            || state.HasComponent<WarehouseSignComponent>(entity)
            || state.HasComponent<SellPointComponent>(entity)
            || state.HasComponent<FinalStructureComponent>(entity)
            || state.HasComponent<HelperComponent>(entity)
            || state.HasComponent<HelperPetComponent>(entity)
            || state.HasComponent<HelperPlayerComponent>(entity)
            || state.HasComponent<PlayerEntity>(entity)
            || state.HasComponent<CarrotFarmComponent>(entity)
            || state.HasComponent<AppleOrchardComponent>(entity)
            || state.HasComponent<MushroomCaveComponent>(entity)
            || state.HasComponent<HelperAssistantComponent>(entity)
            || state.HasComponent<UpgradeGathererComponent>(entity)
            || state.HasComponent<UpgradeBuilderComponent>(entity)
            || state.HasComponent<UpgradeSellerComponent>(entity)
            || state.HasComponent<UpgradeAssistantComponent>(entity)
            || state.HasComponent<DecorationComponent>(entity)
            || state.HasComponent<WarehouseComponent>(entity)
            || state.HasComponent<LibraryComponent>(entity)
            || state.HasComponent<PlayerHouseComponent>(entity);
    }

    /// <summary>Delete props inside a circle around the given position.</summary>
    public static void DestroyNearbyProps(EntityWorld state, Vector2 position, Float radius)
    {
        Float radiusSq = radius * radius;
        var toDelete = new System.Collections.Generic.List<Entity>();
        foreach (var entity in state.Filter<PropComponent>())
        {
            if (!state.HasComponent<Transform2D>(entity)) continue;
            var propPos = state.GetComponent<Transform2D>(entity).Position;
            var diff = propPos - position;
            if (diff.SqrMagnitude < radiusSq)
                toDelete.Add(entity);
        }
        foreach (var entity in toDelete)
            state.DeleteEntity(entity);
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
