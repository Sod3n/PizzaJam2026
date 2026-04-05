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
    private const float PlayerReturnDistSq = 36f;   // 6^2 — helpers stop farther from player (2x normal)
    private const float GatherReachedDistSq = 4f;   // 2^2 — closer for food collection
    private const int GatherWorkDuration = 30;        // 0.5 sec
    private const int SellWorkDuration = 10;          // per item
    private const int BuildWorkDuration = 15;         // per coin
    private const int MilkWorkDuration = 20;          // per milk action

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
                    UpdateSeller(state, entity, ref helper, upgraded);
                    break;
                case HelperType.Builder:
                    UpdateBuilder(state, entity, ref helper, upgraded);
                    break;
                case HelperType.Milker:
                    UpdateMilker(state, entity, ref helper);
                    break;
            }

            // ─── Warehouse auto-deposit: when warehouse is enabled, helpers skip player pickup ───
            helper = ref state.GetComponent<HelperComponent>(entity);
            if (helper.Type != HelperType.Assistant)
                TryWarehouseAutoDeposit(state, entity, ref helper);
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

    // ─── Gatherer: find food → harvest → return to player → wait for pickup ───

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
                    int harvestAmount = upgraded ? 5 : 1;
                    int bagSpace = helper.BagCapacity - helper.GetBagTotal();
                    int amount = System.Math.Min(harvestAmount, bagSpace);

                    if (InteractionLogic.HarvestFood(state, helper.TargetEntity, amount, out int foodType, out bool destroyed))
                    {
                        InteractionLogic.FireInteracted(state, helper.TargetEntity);
                        helper = ref state.GetComponent<HelperComponent>(entity);
                        for (int h = 0; h < amount; h++)
                            AddFoodToBag(ref helper, foodType);

                        string harvestKey = foodType switch
                        {
                            FoodType.Carrot => StateKeys.Carrot,
                            FoodType.Apple => StateKeys.Apple,
                            FoodType.Mushroom => StateKeys.Mushroom,
                            _ => StateKeys.Grass
                        };
                        InteractionLogic.FireGainedResource(state, entity, harvestKey);

                        helper = ref state.GetComponent<HelperComponent>(entity);
                        if (destroyed)
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
                if (NavigateToward(state, entity, playerPos, PlayerReturnDistSq))
                {
                    // Wait for player to interact and pick up resources
                    helper.State = HelperState.WaitingForPickup;
                }
                break;

            case HelperState.WaitingForPickup:
                // Follow player while waiting for pickup interaction
                if (helper.OwnerPlayer != Entity.Null && state.HasComponent<Transform2D>(helper.OwnerPlayer))
                {
                    SwarmFollow.Follow(state, entity, helper.OwnerPlayer);
                    helper = ref state.GetComponent<HelperComponent>(entity);
                }
                // Resources are picked up via HandleHelperInteraction in InteractActionService
                break;
        }
    }

    // ─── Seller: player gives milk → sell at sell point → return with coins → wait for pickup ───

    private void UpdateSeller(EntityWorld state, Entity entity, ref HelperComponent helper, bool upgraded = false)
    {
        switch (helper.State)
        {
            case HelperState.Idle:
                // Wait near player for interaction to load bag with milk (like builder waits for coins)
                if (helper.OwnerPlayer != Entity.Null && state.HasComponent<Transform2D>(helper.OwnerPlayer))
                {
                    SwarmFollow.Follow(state, entity, helper.OwnerPlayer);
                    helper = ref state.GetComponent<HelperComponent>(entity);
                }
                // If bag has milk products (loaded by player interact), go sell
                if (HasMilkInBag(ref helper))
                    helper.State = HelperState.SeekingTarget;
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
                    // Sell 1 item per work cycle (1x speed)
                    SellOneItem(ref helper);
                    // Visual feedback on sell point
                    if (helper.TargetEntity != Entity.Null)
                        state.AddComponent(helper.TargetEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = StateKeys.Coins, Age = 0 });
                    helper = ref state.GetComponent<HelperComponent>(entity);

                    if (!HasMilkInBag(ref helper))
                        helper.State = HelperState.Returning;
                }
                break;

            case HelperState.Returning:
                if (helper.OwnerPlayer == Entity.Null || !state.HasComponent<Transform2D>(helper.OwnerPlayer))
                {
                    helper.State = HelperState.Idle;
                    return;
                }
                var sellerReturnPos = state.GetComponent<Transform2D>(helper.OwnerPlayer).Position;
                if (NavigateToward(state, entity, sellerReturnPos, PlayerReturnDistSq))
                {
                    // Wait for player to interact and pick up coins
                    helper.State = HelperState.WaitingForPickup;
                }
                break;

            case HelperState.WaitingForPickup:
                // Follow player while waiting for pickup interaction
                if (helper.OwnerPlayer != Entity.Null && state.HasComponent<Transform2D>(helper.OwnerPlayer))
                {
                    SwarmFollow.Follow(state, entity, helper.OwnerPlayer);
                    helper = ref state.GetComponent<HelperComponent>(entity);
                }
                // Coins are picked up via HandleHelperInteraction in InteractActionService
                break;
        }
    }

    // ─── Builder: player gives coins → walk to land → contribute coins ───

    private void UpdateBuilder(EntityWorld state, Entity entity, ref HelperComponent helper, bool upgraded = false)
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
                        int buildAmount = System.Math.Min(3, helper.BagCoins);

                        int deposited = InteractionLogic.DepositToLand(state, landEntity, buildAmount, leaveOneForPlayer: true, out bool landComplete);
                        if (deposited <= 0)
                        {
                            helper.State = HelperState.SeekingTarget;
                            helper.TargetEntity = Entity.Null;
                            break;
                        }
                        InteractionLogic.FireInteracted(state, landEntity, StateKeys.Coins);
                        helper = ref state.GetComponent<HelperComponent>(entity);
                        helper.BagCoins -= deposited;

                        if (landComplete)
                        {
                            var transform = state.GetComponent<Transform2D>(landEntity);
                            var position = transform.Position;
                            var landComp = state.GetComponent<LandComponent>(landEntity);
                            int landType = landComp.Type;
                            int gridX = landComp.Arm;
                            int gridY = landComp.Ring;
                            state.DeleteEntity(landEntity);

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
                if (NavigateToward(state, entity, builderReturnPos, PlayerReturnDistSq))
                {
                    // If carrying unused coins, wait for player to pick them up
                    if (helper.BagCoins > 0)
                        helper.State = HelperState.WaitingForPickup;
                    else
                        helper.State = HelperState.Idle;
                }
                break;

            case HelperState.WaitingForPickup:
                // Follow player while waiting for pickup interaction
                if (helper.OwnerPlayer != Entity.Null && state.HasComponent<Transform2D>(helper.OwnerPlayer))
                {
                    SwarmFollow.Follow(state, entity, helper.OwnerPlayer);
                    helper = ref state.GetComponent<HelperComponent>(entity);
                }
                // Coins are picked up via HandleHelperInteraction in InteractActionService
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

        // Apply navigation velocity with ORCA avoidance against the player.
        // Without this, autonomous helpers steer straight through the player
        // since the player is not a static obstacle in the navmesh.
        var vel = SwarmFollow.ApplyOrcaForNav(state, entity, myPos, navAgent.Velocity);
        ref var charBody = ref state.GetComponent<CharacterBody2D>(entity);
        charBody.Velocity = vel;

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

    // ─── Milker: find house → follow player asking for food → receive food → go milk → return with milk → wait for pickup ───

    private void UpdateMilker(EntityWorld state, Entity entity, ref HelperComponent helper)
    {
        switch (helper.State)
        {
            case HelperState.Idle:
                // Find a milkable house and determine what food it needs
                if (helper.TargetEntity == Entity.Null || !state.HasComponent<HouseComponent>(helper.TargetEntity))
                {
                    var milkTarget = FindMilkableHouse(state, entity, helper.OwnerPlayer);
                    if (milkTarget == Entity.Null)
                    {
                        helper.WantedFoodType = -1;
                        // Follow player while searching
                        if (helper.OwnerPlayer != Entity.Null && state.HasComponent<Transform2D>(helper.OwnerPlayer))
                        {
                            SwarmFollow.Follow(state, entity, helper.OwnerPlayer);
                            helper = ref state.GetComponent<HelperComponent>(entity);
                        }
                        return;
                    }
                    helper.TargetEntity = milkTarget;

                    // Determine which food the house/cow needs
                    var house = state.GetComponent<HouseComponent>(milkTarget);
                    if (house.CowId != Entity.Null && state.HasComponent<CowComponent>(house.CowId))
                    {
                        var cow = state.GetComponent<CowComponent>(house.CowId);
                        helper.WantedFoodType = ResolveMilkerFoodType(cow, house.SelectedFood);
                    }
                    else
                    {
                        helper.WantedFoodType = FoodType.Grass;
                    }
                }

                // If we already have food in bag for this house, go milk immediately
                if (helper.GetFoodTotal() > 0)
                {
                    helper.State = HelperState.MovingToTarget;
                    break;
                }

                // Follow player while waiting for food interaction
                if (helper.OwnerPlayer != Entity.Null && state.HasComponent<Transform2D>(helper.OwnerPlayer))
                {
                    SwarmFollow.Follow(state, entity, helper.OwnerPlayer);
                    helper = ref state.GetComponent<HelperComponent>(entity);
                }
                // Food is loaded via HandleHelperInteraction in InteractActionService
                break;

            case HelperState.SeekingTarget:
                // Re-evaluate target house after milking
                helper.TargetEntity = Entity.Null;
                helper.WantedFoodType = -1;
                helper.State = HelperState.Idle;
                break;

            case HelperState.MovingToTarget:
                if (helper.TargetEntity == Entity.Null || !state.HasComponent<HouseComponent>(helper.TargetEntity))
                {
                    helper.State = HelperState.SeekingTarget;
                    helper.WorkTimer = 0;
                    return;
                }
                // Validate the house still has a milkable cow
                {
                    var houseCheck = state.GetComponent<HouseComponent>(helper.TargetEntity);
                    if (houseCheck.CowId == Entity.Null || !state.HasComponent<CowComponent>(houseCheck.CowId))
                    {
                        helper.State = HelperState.SeekingTarget;
                        helper.WorkTimer = 0;
                        return;
                    }
                    var cowCheck = state.GetComponent<CowComponent>(houseCheck.CowId);
                    if (cowCheck.IsDepressed || cowCheck.IsMilking || cowCheck.Exhaust >= cowCheck.MaxExhaust)
                    {
                        helper.State = HelperState.SeekingTarget;
                        helper.WorkTimer = 0;
                        return;
                    }
                }
                var housePos = state.GetComponent<Transform2D>(helper.TargetEntity).Position;
                if (NavigateToward(state, entity, housePos, TargetReachedDistSq))
                {
                    // Arrived at house — hide milker and cow, begin milking
                    if (state.HasComponent<HouseComponent>(helper.TargetEntity))
                    {
                        var house = state.GetComponent<HouseComponent>(helper.TargetEntity);
                        if (house.CowId != Entity.Null && state.HasComponent<CowComponent>(house.CowId))
                        {
                            ref var cow = ref state.GetComponent<CowComponent>(house.CowId);
                            if (!cow.IsDepressed && !cow.IsMilking && cow.Exhaust <= cow.MaxExhaust / 2)
                            {
                                cow.IsMilking = true;
                                // Hide both milker and cow (like player milking does)
                                state.HideEntity(entity);
                                state.HideEntity(house.CowId);
                                helper = ref state.GetComponent<HelperComponent>(entity);
                            }
                        }
                    }
                    helper.State = HelperState.Working;
                    helper.WorkTimer = 0;
                    helper.WorkDuration = MilkWorkDuration;
                }
                break;

            case HelperState.Working:
                // Milker and cow are hidden during this phase
                helper.WorkTimer++;
                if (helper.WorkTimer >= helper.WorkDuration)
                {
                    helper.WorkTimer = 0;
                    bool milked = false;

                    if (state.HasComponent<HouseComponent>(helper.TargetEntity))
                    {
                        var house = state.GetComponent<HouseComponent>(helper.TargetEntity);
                        if (house.CowId != Entity.Null && state.HasComponent<CowComponent>(house.CowId))
                        {
                            var cow = state.GetComponent<CowComponent>(house.CowId);
                            if (cow.Exhaust < cow.MaxExhaust)
                            {
                                // Determine food to use from the helper's bag
                                int foodToUse = ResolveFoodFromBag(ref helper, house.SelectedFood, cow.PreferredFood);
                                if (foodToUse >= 0)
                                {
                                    int milkPower = 1; // helpers always milk at 1x speed (no player click multiplier)

                                    // Milk into helper's own bag, consuming food from helper's bag
                                    bool produced = InteractionLogic.MilkCowFromBag(state, house.CowId, foodToUse, milkPower, ref helper, out bool cowDone);

                                    // Fire interaction visual on house (squish + heart burst)
                                    state.AddComponent(helper.TargetEntity, new EnterStateComponent
                                    {
                                        Key = StateKeys.Interacted, Param = produced ? "milk_ok" : "milk_fail", Age = 0
                                    });
                                    helper = ref state.GetComponent<HelperComponent>(entity);
                                    milked = !cowDone;
                                }
                            }
                        }
                    }

                    if (!milked)
                    {
                        // Done milking — unhide milker and cow
                        MilkerFinishMilking(state, entity, ref helper);
                        helper = ref state.GetComponent<HelperComponent>(entity);
                        helper.TargetEntity = Entity.Null;
                        helper.WantedFoodType = -1;

                        // If we have milk in bag, return to player for pickup
                        if (helper.GetMilkTotal() > 0)
                            helper.State = HelperState.Returning;
                        else
                            helper.State = HelperState.SeekingTarget;
                    }
                }
                break;

            case HelperState.Returning:
                if (helper.OwnerPlayer == Entity.Null || !state.HasComponent<Transform2D>(helper.OwnerPlayer))
                {
                    helper.State = HelperState.Idle;
                    return;
                }
                var milkerReturnPos = state.GetComponent<Transform2D>(helper.OwnerPlayer).Position;
                if (NavigateToward(state, entity, milkerReturnPos, PlayerReturnDistSq))
                {
                    // Wait for player to interact and pick up milk
                    helper.State = HelperState.WaitingForPickup;
                }
                break;

            case HelperState.WaitingForPickup:
                // Follow player while waiting for pickup interaction
                if (helper.OwnerPlayer != Entity.Null && state.HasComponent<Transform2D>(helper.OwnerPlayer))
                {
                    SwarmFollow.Follow(state, entity, helper.OwnerPlayer);
                    helper = ref state.GetComponent<HelperComponent>(entity);
                }
                // Milk is picked up via HandleHelperInteraction in InteractActionService
                break;
        }
    }

    /// <summary>
    /// Determine which food type the milker should request from the player for a given cow/house.
    /// Uses the house's selected food if the cow supports that tier, otherwise the cow's preferred food.
    /// </summary>
    private static int ResolveMilkerFoodType(CowComponent cow, int houseSelectedFood)
    {
        int cowMaxTier = FoodType.MaxTier(cow.PreferredFood);
        if (houseSelectedFood >= 0 && houseSelectedFood <= cowMaxTier)
            return houseSelectedFood;
        return cow.PreferredFood;
    }

    /// <summary>
    /// Determine which food to use from the milker's bag.
    /// Strict: only allows the house's selected food — no fallback to lower tiers.
    /// Returns -1 if the selected food or its prerequisite is unavailable in the bag.
    /// </summary>
    private static int ResolveFoodFromBag(ref HelperComponent helper, int houseSelectedFood, int cowPreferredFood)
    {
        int cowMaxTier = FoodType.MaxTier(cowPreferredFood);

        // Strict: only allow the house's selected food, no fallback
        if (houseSelectedFood >= 0 && houseSelectedFood <= cowMaxTier && helper.GetBagFood(houseSelectedFood) > 0)
        {
            int prereq = FoodType.PrerequisiteProduct(houseSelectedFood);
            if (prereq < 0 || helper.GetBagMilkProduct(prereq) > 0)
                return houseSelectedFood;
        }

        // No fallback — selected food or prerequisite not available in bag
        return -1;
    }

    /// <summary>
    /// Unhide the milker and cow when milking is done.
    /// </summary>
    private static void MilkerFinishMilking(EntityWorld state, Entity milkerEntity, ref HelperComponent helper)
    {
        // Unhide milker
        state.UnhideEntity(milkerEntity);

        // Unhide the cow and mark it as no longer milking
        if (helper.TargetEntity != Entity.Null && state.HasComponent<HouseComponent>(helper.TargetEntity))
        {
            var house = state.GetComponent<HouseComponent>(helper.TargetEntity);
            if (house.CowId != Entity.Null && state.HasComponent<CowComponent>(house.CowId))
            {
                ref var cow = ref state.GetComponent<CowComponent>(house.CowId);
                cow.IsMilking = false;
                state.UnhideEntity(house.CowId);
            }
        }
    }


    private Entity FindMilkableHouse(EntityWorld state, Entity helper, Entity ownerPlayer)
    {
        var myPos = state.GetComponent<Transform2D>(helper).Position;
        Entity best = Entity.Null;
        Float bestScore = -1f;

        foreach (var houseEntity in state.Filter<HouseComponent>())
        {
            var house = state.GetComponent<HouseComponent>(houseEntity);
            if (house.CowId == Entity.Null) continue;
            if (!state.HasComponent<CowComponent>(house.CowId)) continue;

            var cow = state.GetComponent<CowComponent>(house.CowId);
            if (cow.IsDepressed || cow.IsMilking || cow.Exhaust >= cow.MaxExhaust) continue;
            // Only target cows with more than half their capacity remaining
            if (cow.Exhaust > cow.MaxExhaust / 2) continue;

            var housePos = state.GetComponent<Transform2D>(houseEntity).Position;
            var distSq = Vector2.DistanceSquared(myPos, housePos);
            if (distSq < 1f) distSq = 1f;

            // Score: prefer higher-tier cows
            int tierValue = cow.PreferredFood switch
            {
                FoodType.Mushroom => 200,
                FoodType.Apple => 20,
                FoodType.Carrot => 6,
                _ => 1
            };
            Float score = (Float)(tierValue / (float)distSq);

            if (score > bestScore)
            {
                bestScore = score;
                best = houseEntity;
            }
        }
        return best;
    }

    // ─── Entity finding ───

    private Entity FindNearestFood(EntityWorld state, Entity helper)
    {
        var myPos = state.GetComponent<Transform2D>(helper).Position;
        Entity best = Entity.Null;
        Float bestScore = -1f;

        foreach (var entity in state.Filter<GrassComponent>())
        {
            if (!state.HasComponent<Transform2D>(entity)) continue;
            var food = state.GetComponent<GrassComponent>(entity);
            var pos = state.GetComponent<Transform2D>(entity).Position;
            var distSq = Vector2.DistanceSquared(myPos, pos);
            if (distSq < 1f) distSq = 1f;

            // Prefer valuable food: value weight / distance
            int value = food.FoodType switch
            {
                FoodType.Mushroom => 200,
                FoodType.Apple => 20,
                FoodType.Carrot => 6,
                _ => 1
            };
            Float score = (Float)value / distSq;
            if (score > bestScore)
            {
                bestScore = score;
                best = entity;
            }
        }
        return best;
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
            if (land.CurrentCoins >= land.Threshold - 1) continue; // Skip buildings at threshold-1 (builder leaves last coin for player)
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
            while (taken < capacity && global.CarrotMilkshake > 0) { global.CarrotMilkshake--; helper.BagCarrotMilkshake++; taken++; }
            while (taken < capacity && global.VitaminMix > 0) { global.VitaminMix--; helper.BagVitaminMix++; taken++; }
            while (taken < capacity && global.PurplePotion > 0) { global.PurplePotion--; helper.BagPurplePotion++; taken++; }

            return taken > 0;
        }
        return false;
    }

    private bool HasMilkInBag(ref HelperComponent helper)
    {
        return helper.BagMilk > 0 || helper.BagCarrotMilkshake > 0
            || helper.BagVitaminMix > 0 || helper.BagPurplePotion > 0;
    }

    private bool SellOneItem(ref HelperComponent helper)
    {
        // Sell most valuable first
        if (helper.BagPurplePotion > 0) { helper.BagPurplePotion--; helper.BagCoins += MilkProduct.CoinValue(MilkProduct.PurplePotion); return true; }
        if (helper.BagVitaminMix > 0) { helper.BagVitaminMix--; helper.BagCoins += MilkProduct.CoinValue(MilkProduct.VitaminMix); return true; }
        if (helper.BagCarrotMilkshake > 0) { helper.BagCarrotMilkshake--; helper.BagCoins += MilkProduct.CoinValue(MilkProduct.CarrotMilkshake); return true; }
        if (helper.BagMilk > 0) { helper.BagMilk--; helper.BagCoins += MilkProduct.CoinValue(MilkProduct.Milk); return true; }
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

    // ─── Warehouse auto-deposit ──��

    /// <summary>
    /// Find an enabled warehouse entity. Returns Entity.Null if none exists or none is enabled.
    /// </summary>
    private static Entity FindEnabledWarehouse(EntityWorld state)
    {
        foreach (var entity in state.Filter<WarehouseComponent>())
        {
            var wh = state.GetComponent<WarehouseComponent>(entity);
            if (wh.Enabled == 1 && state.HasComponent<Transform2D>(entity))
                return entity;
        }
        return Entity.Null;
    }

    /// <summary>
    /// When an enabled warehouse exists, helpers skip the "return to player + wait for pickup" loop.
    /// Instead they navigate to the warehouse and auto-deposit resources into global storage.
    /// For Idle sellers/builders/milkers, auto-load resources from global storage without player interaction.
    /// </summary>
    private void TryWarehouseAutoDeposit(EntityWorld state, Entity entity, ref HelperComponent helper)
    {
        var warehouse = FindEnabledWarehouse(state);
        if (warehouse == Entity.Null) return;

        var warehousePos = state.GetComponent<Transform2D>(warehouse).Position;

        switch (helper.State)
        {
            case HelperState.WaitingForPickup:
                // Instead of following player, navigate to warehouse
                if (NavigateToward(state, entity, warehousePos, TargetReachedDistSq))
                {
                    // Arrived at warehouse — auto-deposit all resources to global
                    WarehouseDeposit(state, entity, ref helper);
                    helper = ref state.GetComponent<HelperComponent>(entity);

                    // Fire visual feedback on warehouse
                    state.AddComponent(warehouse, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
                    helper = ref state.GetComponent<HelperComponent>(entity);

                    // Reset to idle/seeking for next task
                    helper.State = helper.Type == HelperType.Gatherer ? HelperState.SeekingTarget : HelperState.Idle;
                }
                break;

            case HelperState.Returning:
                // Redirect to warehouse instead of player
                if (helper.HasAnyResources())
                {
                    if (NavigateToward(state, entity, warehousePos, TargetReachedDistSq))
                    {
                        WarehouseDeposit(state, entity, ref helper);
                        helper = ref state.GetComponent<HelperComponent>(entity);

                        state.AddComponent(warehouse, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
                        helper = ref state.GetComponent<HelperComponent>(entity);

                        helper.State = helper.Type == HelperType.Gatherer ? HelperState.SeekingTarget : HelperState.Idle;
                    }
                }
                break;

            case HelperState.Idle:
                // Auto-load resources for sellers/builders/milkers without player interaction
                if (helper.Type == HelperType.Seller)
                {
                    // Auto-load milk products from global
                    foreach (var grEntity in state.Filter<GlobalResourcesComponent>())
                    {
                        ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
                        int transferred = 0;
                        int capacity = helper.BagCapacity - helper.GetBagTotal();

                        while (transferred < capacity && gr.Milk > 0) { gr.Milk--; helper.BagMilk++; transferred++; }
                        while (transferred < capacity && gr.CarrotMilkshake > 0) { gr.CarrotMilkshake--; helper.BagCarrotMilkshake++; transferred++; }
                        while (transferred < capacity && gr.VitaminMix > 0) { gr.VitaminMix--; helper.BagVitaminMix++; transferred++; }
                        while (transferred < capacity && gr.PurplePotion > 0) { gr.PurplePotion--; helper.BagPurplePotion++; transferred++; }

                        if (transferred > 0)
                            helper.State = HelperState.SeekingTarget;
                        break;
                    }
                }
                else if (helper.Type == HelperType.Builder)
                {
                    // Auto-load coins from global
                    foreach (var grEntity in state.Filter<GlobalResourcesComponent>())
                    {
                        ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
                        int needed = helper.BagCapacity - helper.BagCoins;
                        int toGive = System.Math.Min(needed, gr.Coins);
                        if (toGive > 0)
                        {
                            gr.Coins -= toGive;
                            helper.BagCoins += toGive;
                            helper.State = HelperState.SeekingTarget;
                        }
                        break;
                    }
                }
                else if (helper.Type == HelperType.Milker && helper.WantedFoodType >= 0 && helper.GetFoodTotal() == 0)
                {
                    // Auto-load food for milkers from global
                    foreach (var grEntity in state.Filter<GlobalResourcesComponent>())
                    {
                        ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
                        int foodType = helper.WantedFoodType;
                        int capacity = helper.BagCapacity - helper.GetBagTotal();
                        int available = gr.GetFood(foodType);
                        int toGive = System.Math.Min(capacity, available);
                        if (toGive > 0)
                        {
                            for (int i = 0; i < toGive; i++)
                                gr.ConsumeFood(foodType);
                            switch (foodType)
                            {
                                case FoodType.Grass: helper.BagGrass += toGive; break;
                                case FoodType.Carrot: helper.BagCarrot += toGive; break;
                                case FoodType.Apple: helper.BagApple += toGive; break;
                                case FoodType.Mushroom: helper.BagMushroom += toGive; break;
                            }
                            helper.State = HelperState.MovingToTarget;
                        }
                        break;
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Deposit all helper bag contents into global resources.
    /// </summary>
    private static void WarehouseDeposit(EntityWorld state, Entity helperEntity, ref HelperComponent helper)
    {
        foreach (var grEntity in state.Filter<GlobalResourcesComponent>())
        {
            ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);

            // Food
            gr.AddFood(FoodType.Grass, helper.BagGrass);
            gr.AddFood(FoodType.Carrot, helper.BagCarrot);
            gr.AddFood(FoodType.Apple, helper.BagApple);
            gr.AddFood(FoodType.Mushroom, helper.BagMushroom);
            helper.BagGrass = 0;
            helper.BagCarrot = 0;
            helper.BagApple = 0;
            helper.BagMushroom = 0;

            // Milk products
            gr.AddMilkProduct(MilkProduct.Milk, helper.BagMilk);
            gr.AddMilkProduct(MilkProduct.CarrotMilkshake, helper.BagCarrotMilkshake);
            gr.AddMilkProduct(MilkProduct.VitaminMix, helper.BagVitaminMix);
            gr.AddMilkProduct(MilkProduct.PurplePotion, helper.BagPurplePotion);
            helper.BagMilk = 0;
            helper.BagCarrotMilkshake = 0;
            helper.BagVitaminMix = 0;
            helper.BagPurplePotion = 0;

            // Coins
            gr.Coins += helper.BagCoins;
            helper.BagCoins = 0;

            break;
        }
    }
}
