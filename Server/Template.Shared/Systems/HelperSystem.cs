using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Actions;

namespace Template.Shared.Systems;

public class HelperSystem : ISystem
{
    private const float TargetReachedDistSq = 9f;    // 3^2 — for sell points and land
    private const float GatherReachedDistSq = 4f;   // 2^2 — closer for food collection
    private const int GatherWorkDuration = 30;        // 0.5 sec
    private const int SellWorkDuration = 10;          // per item
    private const int BuildWorkDuration = 15;         // per coin

    public void Update(EntityWorld state)
    {
        foreach (var entity in state.Filter<HelperComponent>())
        {
            if (!state.HasComponent<Transform2D>(entity)) continue;
            if (!state.HasComponent<CharacterBody2D>(entity)) continue;
            if (!state.HasComponent<NavigationAgent2D>(entity)) continue;

            ref var helper = ref state.GetComponent<HelperComponent>(entity);

            // Update owner to closest player (with hysteresis to prevent flip-flopping)
            var closestPlayer = FindClosestPlayer(state, entity);
            if (closestPlayer != Entity.Null && closestPlayer != helper.OwnerPlayer)
            {
                // Only switch if new player is significantly closer (>5 units closer)
                const float switchThresholdSq = 25f; // 5^2
                var myPos = state.GetComponent<Transform2D>(entity).Position;
                var newDist = Vector2.DistanceSquared(myPos, state.GetComponent<Transform2D>(closestPlayer).Position);
                var oldDist = helper.OwnerPlayer != Entity.Null && state.HasComponent<Transform2D>(helper.OwnerPlayer)
                    ? Vector2.DistanceSquared(myPos, state.GetComponent<Transform2D>(helper.OwnerPlayer).Position)
                    : (Float)999999f;
                if ((float)(oldDist - newDist) > switchThresholdSq)
                    helper.OwnerPlayer = closestPlayer;
            }
            else if (helper.OwnerPlayer == Entity.Null)
            {
                helper.OwnerPlayer = closestPlayer;
            }

            // If owner player is hidden (milking, breeding), helpers wait
            bool ownerHidden = helper.OwnerPlayer != Entity.Null
                && state.HasComponent<HiddenComponent>(helper.OwnerPlayer);

            if (ownerHidden && helper.Type != HelperType.Gatherer)
            {
                // Seller/Builder: don't take/give resources while player hidden. Just idle in place.
                // Assistant: wait near hidden player.
                ref var body = ref state.GetComponent<CharacterBody2D>(entity);
                body.Velocity = body.Velocity * (Float)0.8f;
                if ((float)body.Velocity.SqrMagnitude < 0.05f)
                    body.Velocity = Vector2.Zero;
                continue;
            }

            // Check if this helper has an upgrade pet — apply x2 boost
            bool upgraded = HasUpgradePet(state, entity);
            var config = HelperConfig.GetByType(helper.Type);
            helper.BagCapacity = upgraded ? config.UpgradedCapacity : config.BaseCapacity;

            ref var nav = ref state.GetComponent<NavigationAgent2D>(entity);
            nav.MaxSpeed = (Float)(upgraded ? config.UpgradedSpeed : config.BaseSpeed);

            switch (helper.Type)
            {
                case HelperType.Assistant:
                    UpdateAssistant(state, entity, ref helper);
                    break;
                case HelperType.Gatherer:
                    UpdateGatherer(state, entity, ref helper, upgraded);
                    break;
                case HelperType.Seller:
                    UpdateSeller(state, entity, ref helper);
                    break;
                case HelperType.Builder:
                    UpdateBuilder(state, entity, ref helper);
                    break;
            }
        }

        // Update pets: follow their target helper
        foreach (var petEntity in state.Filter<HelperPetComponent>())
        {
            if (!state.HasComponent<Transform2D>(petEntity)) continue;
            var pet = state.GetComponent<HelperPetComponent>(petEntity);
            if (pet.FollowTarget == Entity.Null || !state.HasComponent<Transform2D>(pet.FollowTarget)) continue;
            SwarmFollow.Follow(state, petEntity, pet.FollowTarget);
        }
    }

    private static bool HasUpgradePet(EntityWorld state, Entity helperEntity)
    {
        foreach (var pe in state.Filter<HelperPetComponent>())
        {
            var pet = state.GetComponent<HelperPetComponent>(pe);
            if (pet.FollowTarget == helperEntity) return true;
        }
        return false;
    }

    // ─── Assistant: follow player closely ───

    private void UpdateAssistant(EntityWorld state, Entity entity, ref HelperComponent helper)
    {
        if (helper.OwnerPlayer == Entity.Null) return;
        if (!state.HasComponent<Transform2D>(helper.OwnerPlayer)) return;

        SwarmFollow.Follow(state, entity, helper.OwnerPlayer);
    }

    // ─── Gatherer: find food → harvest → return to player → deposit ───

    private void UpdateGatherer(EntityWorld state, Entity entity, ref HelperComponent helper, bool upgraded = false)
    {
        int workDuration = upgraded ? GatherWorkDuration / 2 : GatherWorkDuration;
        switch (helper.State)
        {
            case HelperState.Idle:
            case HelperState.SeekingTarget:
                // Find nearest food
                var foodEntity = FindNearestFood(state, entity);
                if (foodEntity == Entity.Null)
                {
                    StopMovement(state, entity);
                    return;
                }
                helper.TargetEntity = foodEntity;
                helper.State = HelperState.MovingToTarget;
                break;

            case HelperState.MovingToTarget:
                if (helper.TargetEntity == Entity.Null || !state.HasComponent<GrassComponent>(helper.TargetEntity))
                {
                    helper.State = HelperState.SeekingTarget;
                    helper.WorkTimer = 0;
                    return;
                }
                var foodPos = state.GetComponent<Transform2D>(helper.TargetEntity).Position;
                if (NavigateToward(state, entity, foodPos, GatherReachedDistSq))
                {
                    helper.State = HelperState.Working;
                    helper.WorkTimer = 0;
                    helper.WorkDuration = workDuration;
                }
                break;

            case HelperState.Working:
                helper.WorkTimer++;
                if (helper.WorkTimer >= helper.WorkDuration)
                {
                    // Harvest
                    if (state.HasComponent<GrassComponent>(helper.TargetEntity))
                    {
                        ref var grass = ref state.GetComponent<GrassComponent>(helper.TargetEntity);
                        grass.Durability--;

                        // Visual feedback on the food entity
                        state.AddComponent(helper.TargetEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });

                        // Re-get helper ref after touching other component
                        helper = ref state.GetComponent<HelperComponent>(entity);
                        int harvestedFoodType = state.GetComponent<GrassComponent>(helper.TargetEntity).FoodType;
                        AddFoodToBag(ref helper, harvestedFoodType);

                        // Icon on helper — it received the food
                        string harvestKey = harvestedFoodType switch
                        {
                            FoodType.Carrot => StateKeys.Carrot,
                            FoodType.Apple => StateKeys.Apple,
                            FoodType.Mushroom => StateKeys.Mushroom,
                            _ => StateKeys.Grass
                        };
                        state.AddComponent(entity, new EnterStateComponent { Key = StateKeys.GainedResource, Param = harvestKey, Age = 0 });

                        if (grass.Durability <= 0)
                            state.DeleteEntity(helper.TargetEntity);
                    }

                    helper = ref state.GetComponent<HelperComponent>(entity);
                    helper.TargetEntity = Entity.Null;

                    if (helper.IsBagFull())
                        helper.State = HelperState.Returning;
                    else
                        helper.State = HelperState.SeekingTarget;
                }
                break;

            case HelperState.Returning:
                if (helper.OwnerPlayer == Entity.Null || !state.HasComponent<Transform2D>(helper.OwnerPlayer))
                {
                    helper.State = HelperState.Idle;
                    return;
                }
                var playerPos = state.GetComponent<Transform2D>(helper.OwnerPlayer).Position;
                if (NavigateToward(state, entity, playerPos, TargetReachedDistSq))
                {
                    helper.State = HelperState.Depositing;
                }
                break;

            case HelperState.Depositing:
                // Show gained icon on player (player receives the resource)
                string foodKey = helper.BagGrass > 0 ? StateKeys.Grass
                    : helper.BagCarrot > 0 ? StateKeys.Carrot
                    : helper.BagApple > 0 ? StateKeys.Apple
                    : helper.BagMushroom > 0 ? StateKeys.Mushroom : "";
                var ownerPlayer = helper.OwnerPlayer;
                DepositFoodToGlobal(state, ref helper);
                if (!string.IsNullOrEmpty(foodKey) && ownerPlayer != Entity.Null)
                    state.AddComponent(ownerPlayer, new EnterStateComponent { Key = StateKeys.GainedResource, Param = foodKey, Age = 0 });
                helper = ref state.GetComponent<HelperComponent>(entity);
                helper.State = HelperState.Idle;
                break;
        }
    }

    // ─── Seller: player gives milk → sell at sell point → deposit coins ───

    private void UpdateSeller(EntityWorld state, Entity entity, ref HelperComponent helper)
    {
        switch (helper.State)
        {
            case HelperState.Idle:
                // Follow player while waiting
                if (helper.OwnerPlayer != Entity.Null && state.HasComponent<Transform2D>(helper.OwnerPlayer))
                {
                    SwarmFollow.Follow(state, entity, helper.OwnerPlayer);
                    helper = ref state.GetComponent<HelperComponent>(entity);
                }
                // Auto-take milk products from global resources
                {
                    int prevMilk = helper.BagMilk, prevShake = helper.BagVitaminShake;
                    int prevYogurt = helper.BagAppleYogurt, prevPotion = helper.BagPurplePotion;
                    if (TakeMilkFromGlobal(state, ref helper))
                    {
                        // Show icon for the first type actually taken
                        string takenKey = (helper.BagMilk > prevMilk) ? StateKeys.Milk
                            : (helper.BagVitaminShake > prevShake) ? StateKeys.VitaminShake
                            : (helper.BagAppleYogurt > prevYogurt) ? StateKeys.AppleYogurt
                            : (helper.BagPurplePotion > prevPotion) ? StateKeys.PurplePotion
                            : StateKeys.Milk;
                        state.AddComponent(entity, new EnterStateComponent { Key = StateKeys.GainedResource, Param = takenKey, Age = 0 });
                        helper = ref state.GetComponent<HelperComponent>(entity);
                        helper.State = HelperState.SeekingTarget;
                    }
                }
                break;

            case HelperState.SeekingTarget:
                var sellPoint = FindNearestEntity<SellPointComponent>(state, entity);
                if (sellPoint == Entity.Null)
                {
                    helper.State = HelperState.Idle;
                    return;
                }
                helper.TargetEntity = sellPoint;
                helper.State = HelperState.MovingToTarget;
                break;

            case HelperState.MovingToTarget:
                if (helper.TargetEntity == Entity.Null || !state.HasComponent<Transform2D>(helper.TargetEntity))
                {
                    helper.State = HelperState.SeekingTarget;
                    helper.WorkTimer = 0;
                    return;
                }
                var targetPos = state.GetComponent<Transform2D>(helper.TargetEntity).Position;
                if (NavigateToward(state, entity, targetPos, TargetReachedDistSq))
                {
                    helper.State = HelperState.Working;
                    helper.WorkTimer = 0;
                    helper.WorkDuration = SellWorkDuration;
                }
                break;

            case HelperState.Working:
                helper.WorkTimer++;
                if (helper.WorkTimer >= helper.WorkDuration)
                {
                    helper.WorkTimer = 0;
                    // Sell one item
                    if (SellOneItem(ref helper))
                    {
                        // Visual feedback on sell point
                        if (helper.TargetEntity != Entity.Null)
                            state.AddComponent(helper.TargetEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = StateKeys.Coins, Age = 0 });
                        helper = ref state.GetComponent<HelperComponent>(entity);

                        if (!HasMilkInBag(ref helper))
                            helper.State = HelperState.Returning;
                    }
                    else
                    {
                        helper.State = HelperState.Returning;
                    }
                }
                break;

            case HelperState.Returning:
                if (helper.OwnerPlayer == Entity.Null || !state.HasComponent<Transform2D>(helper.OwnerPlayer))
                {
                    helper.State = HelperState.Idle;
                    return;
                }
                var sellerReturnPos = state.GetComponent<Transform2D>(helper.OwnerPlayer).Position;
                if (NavigateToward(state, entity, sellerReturnPos, TargetReachedDistSq))
                {
                    helper.State = HelperState.Depositing;
                }
                break;

            case HelperState.Depositing:
                // Icon on player — player receives the coins
                if (helper.BagCoins > 0 && helper.OwnerPlayer != Entity.Null)
                    state.AddComponent(helper.OwnerPlayer, new EnterStateComponent { Key = StateKeys.GainedResource, Param = StateKeys.Coins, Age = 0 });
                DepositCoinsToGlobal(state, ref helper);
                helper = ref state.GetComponent<HelperComponent>(entity);
                helper.State = HelperState.Idle;
                break;
        }
    }

    // ─── Builder: player gives coins → walk to land → contribute coins ───

    private void UpdateBuilder(EntityWorld state, Entity entity, ref HelperComponent helper)
    {
        switch (helper.State)
        {
            case HelperState.Idle:
                // Wait near player for interaction to load bag
                if (helper.OwnerPlayer != Entity.Null && state.HasComponent<Transform2D>(helper.OwnerPlayer))
                {
                    SwarmFollow.Follow(state, entity, helper.OwnerPlayer);
                    helper = ref state.GetComponent<HelperComponent>(entity);
                }
                // If bag has coins (loaded by player interact), go build
                if (helper.BagCoins > 0)
                    helper.State = HelperState.SeekingTarget;
                break;

            case HelperState.SeekingTarget:
                var land = FindFarthestUnlockedLand(state, entity);
                if (land == Entity.Null)
                {
                    // No land to build — return coins
                    helper.State = HelperState.Returning;
                    return;
                }
                helper.TargetEntity = land;
                helper.State = HelperState.MovingToTarget;
                break;

            case HelperState.MovingToTarget:
                if (helper.TargetEntity == Entity.Null || !state.HasComponent<LandComponent>(helper.TargetEntity))
                {
                    helper.State = HelperState.SeekingTarget;
                    helper.WorkTimer = 0;
                    return;
                }
                var builderTargetPos = state.GetComponent<Transform2D>(helper.TargetEntity).Position;
                if (NavigateToward(state, entity, builderTargetPos, TargetReachedDistSq))
                {
                    helper.State = HelperState.Working;
                    helper.WorkTimer = 0;
                    helper.WorkDuration = BuildWorkDuration;
                }
                break;

            case HelperState.Working:
                helper.WorkTimer++;
                if (helper.WorkTimer >= helper.WorkDuration)
                {
                    helper.WorkTimer = 0;

                    if (helper.BagCoins > 0 && state.HasComponent<LandComponent>(helper.TargetEntity))
                    {
                        var landEntity = helper.TargetEntity;

                        // Visual feedback
                        state.AddComponent(landEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = StateKeys.Coins, Age = 0 });

                        ref var targetLand = ref state.GetComponent<LandComponent>(landEntity);
                        targetLand.CurrentCoins++;
                        helper = ref state.GetComponent<HelperComponent>(entity);
                        helper.BagCoins--;

                        // Check if land is complete — trigger building creation
                        targetLand = ref state.GetComponent<LandComponent>(landEntity);
                        if (targetLand.CurrentCoins >= targetLand.Threshold)
                        {
                            var transform = state.GetComponent<Transform2D>(landEntity);
                            var position = transform.Position;
                            var landType = targetLand.Type;
                            int gridX = targetLand.Arm;
                            int gridY = targetLand.Ring;
                            state.DeleteEntity(landEntity);

                            // Build the structure
                            var ctx = new Context(state, helper.OwnerPlayer, null!);
                            InteractActionService.CompleteLandBuilding(ctx, position, landType, gridX, gridY);

                            helper = ref state.GetComponent<HelperComponent>(entity);
                            helper.TargetEntity = Entity.Null;
                            helper.State = helper.BagCoins > 0 ? HelperState.SeekingTarget : HelperState.Returning;
                            return;
                        }
                    }

                    helper = ref state.GetComponent<HelperComponent>(entity);
                    if (helper.BagCoins <= 0)
                    {
                        helper.TargetEntity = Entity.Null;
                        helper.State = HelperState.Idle; // Go get more coins
                    }
                }
                break;

            case HelperState.Returning:
                if (helper.OwnerPlayer == Entity.Null || !state.HasComponent<Transform2D>(helper.OwnerPlayer))
                {
                    helper.State = HelperState.Idle;
                    return;
                }
                var builderReturnPos = state.GetComponent<Transform2D>(helper.OwnerPlayer).Position;
                if (NavigateToward(state, entity, builderReturnPos, TargetReachedDistSq))
                {
                    // Return unused coins
                    DepositCoinsToGlobal(state, ref helper);
                    helper.State = HelperState.Idle;
                }
                break;
        }
    }

    // ─── Navigation helper ───

    private bool NavigateToward(EntityWorld state, Entity entity, Vector2 targetPos, float desiredDistSq)
    {
        ref var navAgent = ref state.GetComponent<NavigationAgent2D>(entity);
        var myPos = state.GetComponent<Transform2D>(entity).Position;
        var distSq = (targetPos - myPos).SqrMagnitude;

        if ((float)distSq <= desiredDistSq)
        {
            ref var body = ref state.GetComponent<CharacterBody2D>(entity);
            body.Velocity = Vector2.Zero;
            navAgent.IsNavigationFinished = true;
            return true; // reached
        }

        var targetDriftSq = (targetPos - navAgent.TargetPosition).SqrMagnitude;
        if ((float)targetDriftSq > 1f || navAgent.IsNavigationFinished)
        {
            navAgent.TargetPosition = targetPos;
            navAgent.IsNavigationFinished = false;
        }

        // Apply navigation velocity to character body (computed by NavigationSystem)
        ref var charBody = ref state.GetComponent<CharacterBody2D>(entity);
        charBody.Velocity = navAgent.Velocity;

        // Face movement direction
        if (navAgent.Velocity.SqrMagnitude > (Float)0.01f)
        {
            ref var transform = ref state.GetComponent<Transform2D>(entity);
            transform.Rotation = navAgent.Velocity.ToAngle();
        }

        return false;
    }

    private static void StopMovement(EntityWorld state, Entity entity)
    {
        ref var body = ref state.GetComponent<CharacterBody2D>(entity);
        body.Velocity = Vector2.Zero;
    }

    // ─── Player finding ───

    private static Entity FindClosestPlayer(EntityWorld state, Entity helper)
    {
        if (!state.HasComponent<Transform2D>(helper)) return Entity.Null;
        var myPos = state.GetComponent<Transform2D>(helper).Position;
        Entity nearest = Entity.Null;
        Float minDistSq = 999999f;

        foreach (var player in state.Filter<PlayerEntity>())
        {
            if (!state.HasComponent<Transform2D>(player)) continue;
            var pos = state.GetComponent<Transform2D>(player).Position;
            var distSq = Vector2.DistanceSquared(myPos, pos);
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                nearest = player;
            }
        }
        return nearest;
    }

    // ─── Entity finding ───

    private Entity FindNearestFood(EntityWorld state, Entity helper)
    {
        var myPos = state.GetComponent<Transform2D>(helper).Position;
        Entity nearest = Entity.Null;
        Float minDistSq = 999999f;

        foreach (var entity in state.Filter<GrassComponent>())
        {
            if (!state.HasComponent<Transform2D>(entity)) continue;
            var pos = state.GetComponent<Transform2D>(entity).Position;
            var distSq = Vector2.DistanceSquared(myPos, pos);
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                nearest = entity;
            }
        }
        return nearest;
    }

    private Entity FindNearestEntity<T>(EntityWorld state, Entity helper) where T : unmanaged, IComponent
    {
        var myPos = state.GetComponent<Transform2D>(helper).Position;
        Entity nearest = Entity.Null;
        Float minDistSq = 999999f;

        foreach (var entity in state.Filter<T>())
        {
            if (!state.HasComponent<Transform2D>(entity)) continue;
            var pos = state.GetComponent<Transform2D>(entity).Position;
            var distSq = Vector2.DistanceSquared(myPos, pos);
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                nearest = entity;
            }
        }
        return nearest;
    }

    /// <summary>
    /// Builder targets the FARTHEST unlocked land — expands the frontier
    /// while the player builds nearby cheap plots manually.
    /// </summary>
    private Entity FindFarthestUnlockedLand(EntityWorld state, Entity helper)
    {
        // Use center (0,0) as reference — farthest from center = frontier expansion
        Entity farthest = Entity.Null;
        Float maxDistSq = 0f;

        foreach (var entity in state.Filter<LandComponent>())
        {
            var land = state.GetComponent<LandComponent>(entity);
            if (land.Locked != 0) continue;
            if (land.CurrentCoins >= land.Threshold) continue;
            if (!state.HasComponent<Transform2D>(entity)) continue;

            var pos = state.GetComponent<Transform2D>(entity).Position;
            var distSq = Vector2.DistanceSquared(Vector2.Zero, pos);
            if (distSq > maxDistSq)
            {
                maxDistSq = distSq;
                farthest = entity;
            }
        }
        return farthest;
    }

    // ─── Resource bag operations ───

    private void AddFoodToBag(ref HelperComponent helper, int foodType)
    {
        switch (foodType)
        {
            case FoodType.Grass: helper.BagGrass++; break;
            case FoodType.Carrot: helper.BagCarrot++; break;
            case FoodType.Apple: helper.BagApple++; break;
            case FoodType.Mushroom: helper.BagMushroom++; break;
        }
    }

    private void DepositFoodToGlobal(EntityWorld state, ref HelperComponent helper)
    {
        foreach (var entity in state.Filter<GlobalResourcesComponent>())
        {
            ref var global = ref state.GetComponent<GlobalResourcesComponent>(entity);
            global.AddFood(FoodType.Grass, helper.BagGrass);
            global.AddFood(FoodType.Carrot, helper.BagCarrot);
            global.AddFood(FoodType.Apple, helper.BagApple);
            global.AddFood(FoodType.Mushroom, helper.BagMushroom);
            break;
        }
        helper.BagGrass = 0;
        helper.BagCarrot = 0;
        helper.BagApple = 0;
        helper.BagMushroom = 0;
    }

    private bool TakeMilkFromGlobal(EntityWorld state, ref HelperComponent helper)
    {
        foreach (var entity in state.Filter<GlobalResourcesComponent>())
        {
            ref var global = ref state.GetComponent<GlobalResourcesComponent>(entity);
            int taken = 0;
            int capacity = helper.BagCapacity;

            // Take milk products up to capacity
            while (taken < capacity && global.Milk > 0) { global.Milk--; helper.BagMilk++; taken++; }
            while (taken < capacity && global.VitaminShake > 0) { global.VitaminShake--; helper.BagVitaminShake++; taken++; }
            while (taken < capacity && global.AppleYogurt > 0) { global.AppleYogurt--; helper.BagAppleYogurt++; taken++; }
            while (taken < capacity && global.PurplePotion > 0) { global.PurplePotion--; helper.BagPurplePotion++; taken++; }

            return taken > 0;
        }
        return false;
    }

    private bool HasMilkInBag(ref HelperComponent helper)
    {
        return helper.BagMilk > 0 || helper.BagVitaminShake > 0
            || helper.BagAppleYogurt > 0 || helper.BagPurplePotion > 0;
    }

    private bool SellOneItem(ref HelperComponent helper)
    {
        // Sell most valuable first: PurplePotion(18) > AppleYogurt(6) > VitaminShake(2) > Milk(1)
        if (helper.BagPurplePotion > 0) { helper.BagPurplePotion--; helper.BagCoins += 18; return true; }
        if (helper.BagAppleYogurt > 0) { helper.BagAppleYogurt--; helper.BagCoins += 6; return true; }
        if (helper.BagVitaminShake > 0) { helper.BagVitaminShake--; helper.BagCoins += 2; return true; }
        if (helper.BagMilk > 0) { helper.BagMilk--; helper.BagCoins += 1; return true; }
        return false;
    }

    private void DepositCoinsToGlobal(EntityWorld state, ref HelperComponent helper)
    {
        if (helper.BagCoins <= 0) return;
        foreach (var entity in state.Filter<GlobalResourcesComponent>())
        {
            ref var global = ref state.GetComponent<GlobalResourcesComponent>(entity);
            global.Coins += helper.BagCoins;
            break;
        }
        helper.BagCoins = 0;
    }

    private bool TakeCoinsFromGlobal(EntityWorld state, ref HelperComponent helper)
    {
        foreach (var entity in state.Filter<GlobalResourcesComponent>())
        {
            ref var global = ref state.GetComponent<GlobalResourcesComponent>(entity);
            int toTake = System.Math.Min(helper.BagCapacity, global.Coins);
            if (toTake <= 0) return false;
            global.Coins -= toTake;
            helper.BagCoins = toTake;
            return true;
        }
        return false;
    }
}
