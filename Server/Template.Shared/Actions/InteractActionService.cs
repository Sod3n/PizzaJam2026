using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Utils.Logging;

namespace Template.Shared.Actions;

public class InteractActionService : ActionService<InteractAction, PlayerEntity>
{
    public const int NotEnoughResourceDurationTicks = 2;

    protected override void ExecuteProcess(InteractAction action, ref PlayerEntity playerComp, Context ctx)
    {
        Entity playerEntity = ctx.Entity;

        if (playerComp.UserId != action.UserId) return;
        if (!ctx.State.HasComponent<PlayerStateComponent>(playerEntity)) return;
        if (!ctx.State.HasComponent<StateComponent>(playerEntity)) return;

        ref var playerState = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
        ref var sc = ref ctx.State.GetComponent<StateComponent>(playerEntity);

        // If actively milking, handle milk clicks
        if (sc.Key == StateKeys.Milking && sc.Phase == StatePhase.Active && sc.IsEnabled)
        {
            HandleMilkingClick(ctx, playerEntity, ref playerState, ref sc);
            return;
        }

        // Skip interaction if in any other active state
        if (sc.IsEnabled) return;

        // Find nearest interactable from interaction zone overlaps
        Entity nearestTarget = FindNearestFromZone(ctx, playerEntity, ref playerState);
        if (nearestTarget == Entity.Null) return;

        Entity globalResEntity = GetGlobalResourcesEntity(ctx);
        if (globalResEntity == Entity.Null) return;
        ref var globalRes = ref ctx.State.GetComponent<GlobalResourcesComponent>(globalResEntity);

        bool success = false;
        string missingResource = null;
        Entity interactedTarget = nearestTarget;

        // Cow interaction → tame (add to follow chain)
        if (ctx.State.HasComponent<CowComponent>(nearestTarget))
        {
            ref var targetCow = ref ctx.State.GetComponent<CowComponent>(nearestTarget);

            // If target cow is housed and player has following cow, try crossbreed
            if (targetCow.HouseId != Entity.Null && playerState.FollowingCow != Entity.Null)
            {
                success = HandleCrossbreed(ctx, playerEntity, nearestTarget, ref playerState, ref sc, ref globalRes, out missingResource);
                if (success) return;
                if (missingResource != null)
                {
                    sc.Key = StateKeys.NotEnoughResource;
                    sc.CurrentTime = 0;
                    sc.MaxTime = NotEnoughResourceDurationTicks;
                    sc.IsEnabled = true;
                    ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.NotEnoughResource, Param = missingResource, Age = 0 });
                    return;
                }
            }
            else
            {
                // Free cow → tame it (add to follow chain)
                success = HandleCowTame(ctx, playerEntity, nearestTarget, ref playerState, ref sc);
                if (success) return;
            }
        }
        // House interaction
        else if (ctx.State.HasComponent<HouseComponent>(nearestTarget))
        {
            if (playerState.FollowingCow != Entity.Null)
            {
                // Have following cows → assign first cow to house
                success = HandleHouseAssign(ctx, playerEntity, nearestTarget, ref playerState, ref sc);
                if (success) return;
            }
            else
            {
                // No following cows → milk the cow in this house
                ref var house = ref ctx.State.GetComponent<HouseComponent>(nearestTarget);
                if (house.CowId != Entity.Null && ctx.State.HasComponent<CowComponent>(house.CowId))
                {
                    success = HandleHouseMilk(ctx, playerEntity, nearestTarget, house.CowId, ref playerState, ref sc, ref globalRes, out missingResource);
                    if (success) return;
                }
            }
        }
        // Love House interaction → breed two following cows
        else if (ctx.State.HasComponent<LoveHouseComponent>(nearestTarget))
        {
            if (playerState.FollowingCow != Entity.Null)
            {
                success = HandleLoveHouseBreed(ctx, playerEntity, nearestTarget, ref playerState, ref sc, ref globalRes, out missingResource);
                if (success) return;
            }
        }
        else if (ctx.State.HasComponent<GrassComponent>(nearestTarget))
        {
            success = HandleGrassInteraction(ctx, nearestTarget, ref globalRes);
        }
        else if (ctx.State.HasComponent<SellPointComponent>(nearestTarget))
        {
            success = HandleSellPointInteraction(ctx, ref globalRes, out missingResource);
        }
        else if (ctx.State.HasComponent<LandComponent>(nearestTarget))
        {
            success = HandleLandInteraction(ctx, nearestTarget, ref globalRes, out missingResource);
        }
        else if (ctx.State.HasComponent<FinalStructureComponent>(nearestTarget))
        {
            success = HandleFinalStructureInteraction(ctx, nearestTarget, ref globalRes, out missingResource);
        }

        if (success)
        {
            ctx.State.AddComponent(interactedTarget, new EnterStateComponent { Key = StateKeys.Interacted, Age = 0 });
            ILogger.Log($"[InteractActionService] Marked {interactedTarget.Id} as successfully interacted");
        }
        else if (missingResource != null)
        {
            sc.Key = StateKeys.NotEnoughResource;
            sc.CurrentTime = 0;
            sc.MaxTime = NotEnoughResourceDurationTicks;
            sc.IsEnabled = true;

            ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.NotEnoughResource, Param = missingResource, Age = 0 });
            ILogger.Log($"[InteractActionService] Not enough {missingResource} for interaction with {nearestTarget.Id}");
        }
    }

    private void HandleMilkingClick(Context ctx, Entity playerEntity, ref PlayerStateComponent playerState, ref StateComponent sc)
    {
        var cowEntity = playerState.InteractionTarget;
        if (cowEntity == Entity.Null || !ctx.State.HasComponent<CowComponent>(cowEntity)) return;

        Entity globalResEntity = GetGlobalResourcesEntity(ctx);
        if (globalResEntity == Entity.Null) return;
        ref var globalRes = ref ctx.State.GetComponent<GlobalResourcesComponent>(globalResEntity);
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);

        if (globalRes.Grass > 0 && cow.Exhaust < cow.MaxExhaust)
        {
            globalRes.Grass--;
            globalRes.Milk++;
            cow.Exhaust++;

            Entity target = cowEntity;
            if (cow.HouseId != Entity.Null)
                target = cow.HouseId;

            ctx.State.AddComponent(target, new EnterStateComponent { Key = StateKeys.Interacted, Age = 0 });
            ILogger.Log($"[InteractActionService] Milking click on cow {cowEntity.Id}");

            if (globalRes.Grass <= 0 || cow.Exhaust >= cow.MaxExhaust)
            {
                StateDefinitions.BeginExit(ref sc);
                ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = sc.Key, Phase = sc.Phase, Age = 0 });
            }
        }
    }

    private bool HandleCowTame(Context ctx, Entity playerEntity, Entity cowEntity, ref PlayerStateComponent playerState, ref StateComponent sc)
    {
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);

        // Can't tame a cow that's being milked or already following someone
        if (cow.IsMilking) return false;
        if (cow.FollowingPlayer != Entity.Null) return false;

        StateDefinitions.Begin(ref sc, StateKeys.Taming);
        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Taming, Phase = sc.Phase, Age = 0 });

        playerState.InteractionTarget = cowEntity;

        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} taming cow {cowEntity.Id}");
        return true;
    }

    private bool HandleHouseMilk(Context ctx, Entity playerEntity, Entity houseEntity, Entity cowEntity, ref PlayerStateComponent playerState, ref StateComponent sc, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);

        if (cow.IsMilking) return false;
        if (globalRes.Grass <= 0 || cow.Exhaust >= cow.MaxExhaust)
        {
            missingResource = StateKeys.Grass;
            return false;
        }

        cow.IsMilking = true;

        StateDefinitions.Begin(ref sc, StateKeys.Milking);
        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Milking, Phase = sc.Phase, Age = 0 });

        playerState.InteractionTarget = cowEntity;

        if (ctx.State.HasComponent<Transform2D>(playerEntity))
            playerState.ReturnPosition = ctx.State.GetComponent<Transform2D>(playerEntity).Position;

        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} milking cow {cowEntity.Id} at house {houseEntity.Id}");
        return true;
    }

    private bool IsCowInChain(Context ctx, Entity firstCow, Entity target)
    {
        var current = firstCow;
        int safety = 0;
        while (current != Entity.Null && safety < 100)
        {
            if (current == target) return true;
            if (!ctx.State.HasComponent<CowComponent>(current)) break;
            // Walk the chain: find the next cow whose FollowTarget is this cow
            Entity next = Entity.Null;
            foreach (var cowEntity in ctx.State.Filter<CowComponent>())
            {
                ref var c = ref ctx.State.GetComponent<CowComponent>(cowEntity);
                if (c.FollowTarget == current)
                {
                    next = cowEntity;
                    break;
                }
            }
            current = next;
            safety++;
        }
        return false;
    }

    private Entity FindNearestFromZone(Context ctx, Entity playerEntity, ref PlayerStateComponent playerState)
    {
        var zoneEntity = playerState.InteractionZone;
        if (zoneEntity == Entity.Null || !ctx.State.HasComponent<Area2D>(zoneEntity)) return Entity.Null;

        ref var area = ref ctx.State.GetComponent<Area2D>(zoneEntity);
        if (!area.HasOverlappingBodies) return Entity.Null;

        var playerPos = ctx.State.GetComponent<Transform2D>(playerEntity).Position;
        Entity nearest = Entity.Null;
        Float minDistSq = 999999f;

        for (int i = 0; i < area.OverlappingEntities.Count; i++)
        {
            var entity = new Entity(area.OverlappingEntities[i]);
            if (entity == playerEntity) continue;
            if (entity == zoneEntity) continue;
            // Skip cows in our follow chain
            if (ctx.State.HasComponent<CowComponent>(entity))
            {
                ref var cowComp = ref ctx.State.GetComponent<CowComponent>(entity);
                if (cowComp.FollowingPlayer == playerEntity) continue;
            }
            if (!ctx.State.HasComponent<Transform2D>(entity)) continue;

            var pos = ctx.State.GetComponent<Transform2D>(entity).Position;
            var distSq = Vector2.DistanceSquared(playerPos, pos);

            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                nearest = entity;
            }
        }

        return nearest;
    }

    private bool HandleGrassInteraction(Context ctx, Entity grassEntity, ref GlobalResourcesComponent globalRes)
    {
        ref var grass = ref ctx.State.GetComponent<GrassComponent>(grassEntity);
        grass.Durability -= 1;
        globalRes.Grass += 1;

        if (grass.Durability <= 0)
        {
            ctx.State.DeleteEntity(grassEntity);
            return false;
        }
        return true;
    }

    private bool HandleHouseAssign(Context ctx, Entity playerEntity, Entity houseEntity, ref PlayerStateComponent playerState, ref StateComponent sc)
    {
        StateDefinitions.Begin(ref sc, StateKeys.Assign);
        playerState.InteractionTarget = houseEntity;

        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Assign, Phase = sc.Phase, Age = 0 });

        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} assigning cow {playerState.FollowingCow.Id} to house {houseEntity.Id}");
        return true;
    }

    private bool HandleLoveHouseBreed(Context ctx, Entity playerEntity, Entity loveHouseEntity, ref PlayerStateComponent playerState, ref StateComponent sc, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;

        var cowToAssign = playerState.FollowingCow;
        if (cowToAssign == Entity.Null) return false;

        ref var loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);

        // Check if love house already has 2 cows (full)
        if (loveHouse.CowId1 != Entity.Null && loveHouse.CowId2 != Entity.Null) return false;

        // Find next cow in chain (to promote after removing first)
        Entity nextCow = Entity.Null;
        foreach (var ce in ctx.State.Filter<CowComponent>())
        {
            var c = ctx.State.GetComponent<CowComponent>(ce);
            if (c.FollowTarget == cowToAssign && c.FollowingPlayer != Entity.Null)
            { nextCow = ce; break; }
        }

        // Remove cow from follow chain
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowToAssign);
        cow.FollowingPlayer = Entity.Null;
        cow.FollowTarget = Entity.Null;
        cow.HouseId = loveHouseEntity;

        if (ctx.State.HasComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowToAssign))
        {
            ref var body = ref ctx.State.GetComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowToAssign);
            body.Velocity = Deterministic.GameFramework.Types.Vector2.Zero;
        }

        // Move cow to love house position
        if (ctx.State.HasComponent<Transform2D>(loveHouseEntity) && ctx.State.HasComponent<Transform2D>(cowToAssign))
        {
            var housePos = ctx.State.GetComponent<Transform2D>(loveHouseEntity).Position;
            ref var cowTransform = ref ctx.State.GetComponent<Transform2D>(cowToAssign);
            cowTransform.Position = housePos + new Deterministic.GameFramework.Types.Vector2(2, 2);
        }

        // Re-get love house ref after touching other components
        loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);

        // Assign to first empty slot
        if (loveHouse.CowId1 == Entity.Null)
            loveHouse.CowId1 = cowToAssign;
        else
            loveHouse.CowId2 = cowToAssign;

        // Promote next cow in chain
        if (nextCow != Entity.Null)
        {
            ref var nextCowComp = ref ctx.State.GetComponent<CowComponent>(nextCow);
            nextCowComp.FollowTarget = playerEntity;
            playerState.FollowingCow = nextCow;
        }
        else
        {
            playerState.FollowingCow = Entity.Null;
        }

        // Re-check: if both slots now filled, trigger breeding
        loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);
        if (loveHouse.CowId1 != Entity.Null && loveHouse.CowId2 != Entity.Null)
        {
            // Count cows vs houses to check room for calf
            int cowCount = 0;
            foreach (var _ in ctx.State.Filter<CowComponent>())
                cowCount++;
            int houseCount = 0;
            foreach (var _ in ctx.State.Filter<HouseComponent>())
                houseCount++;

            if (cowCount >= houseCount)
            {
                missingResource = StateKeys.Houses;
                return false;
            }

            // Start breed state with love house as target
            StateDefinitions.Begin(ref sc, StateKeys.Breed);
            playerState.InteractionTarget = loveHouseEntity;

            ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Breed, Phase = sc.Phase, Age = 0 });

            ILogger.Log($"[InteractActionService] Breeding cows {loveHouse.CowId1.Id} and {loveHouse.CowId2.Id} at love house {loveHouseEntity.Id}");
        }
        else
        {
            // Just assigned first cow, show interacted feedback
            ctx.State.AddComponent(loveHouseEntity, new EnterStateComponent { Key = StateKeys.Interacted, Age = 0 });
            ILogger.Log($"[InteractActionService] Assigned cow {cowToAssign.Id} to love house {loveHouseEntity.Id}");
        }

        return true;
    }

    private bool HandleCrossbreed(Context ctx, Entity playerEntity, Entity targetCow, ref PlayerStateComponent playerState, ref StateComponent sc, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;

        ref var targetCowComp = ref ctx.State.GetComponent<CowComponent>(targetCow);

        // Can't breed with a cow that's being milked or following someone else
        if (targetCowComp.IsMilking) return false;
        if (targetCowComp.FollowingPlayer != Entity.Null) return false;

        // Count total cows and houses to check if there's room
        int cowCount = 0;
        foreach (var _ in ctx.State.Filter<CowComponent>())
            cowCount++;

        int houseCount = 0;
        foreach (var _ in ctx.State.Filter<HouseComponent>())
            houseCount++;

        if (cowCount >= houseCount)
        {
            missingResource = StateKeys.Houses;
            return false;
        }

        StateDefinitions.Begin(ref sc, StateKeys.Breed);
        playerState.InteractionTarget = targetCow;

        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Breed, Phase = sc.Phase, Age = 0 });

        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} breeding cow {playerState.FollowingCow.Id} with {targetCow.Id}");
        return true;
    }

    private bool HandleSellPointInteraction(Context ctx, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;
        if (globalRes.Milk > 0)
        {
            globalRes.Milk -= 1;
            globalRes.Coins += 1;
            return true;
        }
        missingResource = StateKeys.Milk;
        return false;
    }

    private bool HandleLandInteraction(Context ctx, Entity landEntity, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;

        if (globalRes.Coins > 0)
        {
            ref var land = ref ctx.State.GetComponent<LandComponent>(landEntity);
            land.CurrentCoins += 1;
            globalRes.Coins -= 1;

            if (land.CurrentCoins >= land.Threshold)
            {
                var transform = ctx.State.GetComponent<Transform2D>(landEntity);
                var position = transform.Position;
                var landType = land.Type;
                int gridX = land.Arm;  // grid X coord stored in Arm
                int gridY = land.Ring; // grid Y coord stored in Ring
                ctx.State.DeleteEntity(landEntity);

                switch (landType)
                {
                    case LandType.LoveHouse:
                        LoveHouseDefinition.Create(ctx, position);
                        break;
                    case LandType.SellPoint:
                        SellPointDefinition.Create(ctx, position);
                        break;
                    case LandType.FinalStructure:
                        FinalStructureDefinition.Create(ctx, position, 0);
                        break;
                    default:
                        HouseDefinition.Create(ctx, position);
                        break;
                }

                // Spawn new land plots at the 4 cardinal neighbors
                StarGrid.SpawnNeighbors(ctx, gridX, gridY);

                return false;
            }
            return true;
        }
        missingResource = StateKeys.Coins;
        return false;
    }

    private bool HandleFinalStructureInteraction(Context ctx, Entity finalEntity, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;
        if (globalRes.Coins <= 0)
        {
            missingResource = StateKeys.Coins;
            return false;
        }

        ref var final = ref ctx.State.GetComponent<FinalStructureComponent>(finalEntity);

        if (final.CurrentCoins >= final.Threshold) return false;

        globalRes.Coins -= 1;
        final.CurrentCoins += 1;
        return true;
    }

    private Entity GetGlobalResourcesEntity(Context ctx)
    {
        foreach (var entity in ctx.State.Filter<GlobalResourcesComponent>())
        {
            return entity;
        }
        return Entity.Null;
    }
}
