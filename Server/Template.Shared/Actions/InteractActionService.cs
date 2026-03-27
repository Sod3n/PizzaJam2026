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

        // If actively milking, use stored target directly (cow is hidden)
        if (sc.Key == StateKeys.Milking && sc.Phase == StatePhase.Active && sc.IsEnabled)
        {
            var cowEntity = playerState.InteractionTarget;
            if (cowEntity == Entity.Null || !ctx.State.HasComponent<CowComponent>(cowEntity)) return;

            Entity globalResEntity = GetGlobalResourcesEntity(ctx);
            if (globalResEntity == Entity.Null) return;
            ref var globalRes = ref ctx.State.GetComponent<GlobalResourcesComponent>(globalResEntity);

            Entity target = cowEntity;
            if (HandleCowInteraction(ctx, playerEntity, cowEntity, ref globalRes, ref target, out _))
            {
                ctx.State.AddComponent(target, new EnterStateComponent { Key = StateKeys.Interacted, Age = 0 });
                ILogger.Log($"[InteractActionService] Marked {target.Id} as successfully interacted");
            }
            return;
        }

        // Skip interaction if in any other active state (entering/exiting)
        if (sc.IsEnabled) return;

        // Find nearest interactable from interaction zone overlaps
        Entity nearestTarget = FindNearestFromZone(ctx, playerEntity, ref playerState);
        if (nearestTarget == Entity.Null) return;

        Entity globalResEntity2 = GetGlobalResourcesEntity(ctx);
        if (globalResEntity2 == Entity.Null) return;
        ref var globalRes2 = ref ctx.State.GetComponent<GlobalResourcesComponent>(globalResEntity2);

        bool success = false;
        string missingResource = null;
        Entity interactedTarget = nearestTarget;

        // If player has a following cow, check for special interactions first
        if (playerState.FollowingCow != Entity.Null)
        {
            if (ctx.State.HasComponent<HouseComponent>(nearestTarget))
            {
                success = HandleHouseAssign(ctx, playerEntity, nearestTarget, ref playerState, ref sc);
                if (success) return; // State transition handles everything
            }
            else if (ctx.State.HasComponent<CowComponent>(nearestTarget) && nearestTarget != playerState.FollowingCow)
            {
                success = HandleCrossbreed(ctx, playerEntity, nearestTarget, ref playerState, ref sc, ref globalRes2, out missingResource);
                if (success) return; // State transition handles everything
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
        }

        if (ctx.State.HasComponent<GrassComponent>(nearestTarget))
        {
            success = HandleGrassInteraction(ctx, nearestTarget, ref globalRes2);
        }
        else if (ctx.State.HasComponent<CowComponent>(nearestTarget))
        {
            success = HandleCowInteraction(ctx, playerEntity, nearestTarget, ref globalRes2, ref interactedTarget, out missingResource);
        }
        else if (ctx.State.HasComponent<SellPointComponent>(nearestTarget))
        {
            success = HandleSellPointInteraction(ctx, ref globalRes2, out missingResource);
        }
        else if (ctx.State.HasComponent<LandComponent>(nearestTarget))
        {
            success = HandleLandInteraction(ctx, nearestTarget, ref globalRes2, out missingResource);
        }
        else if (ctx.State.HasComponent<FinalStructureComponent>(nearestTarget))
        {
            success = HandleFinalStructureInteraction(ctx, nearestTarget, ref globalRes2, out missingResource);
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
            // Skip our own following cow for normal interactions
            if (entity == playerState.FollowingCow) continue;
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
            return false; // Entity deleted, skip interacted marker
        }
        return true;
    }

    private bool HandleCowInteraction(Context ctx, Entity playerEntity, Entity cowEntity, ref GlobalResourcesComponent globalRes, ref Entity target, out string missingResource)
    {
        missingResource = null;
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
        if (!ctx.State.HasComponent<PlayerStateComponent>(playerEntity)) return false;
        if (!ctx.State.HasComponent<StateComponent>(playerEntity)) return false;

        ref var playerState = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
        ref var sc = ref ctx.State.GetComponent<StateComponent>(playerEntity);

        // Attempting to Start Milking (idle = state not enabled)
        if (!sc.IsEnabled)
        {
            if (cow.IsMilking) return false;
            // Can't milk a cow that's following someone or without a house
            if (cow.FollowingPlayer != Entity.Null) return false;
            if (cow.HouseId == Entity.Null) return false;
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

            return true;
        }

        // Clicking while Milking
        if (sc.Key == StateKeys.Milking && sc.Phase == StatePhase.Active && sc.IsEnabled)
        {
            if (globalRes.Grass > 0 && cow.Exhaust < cow.MaxExhaust)
            {
                globalRes.Grass--;
                globalRes.Milk++;
                cow.Exhaust++;

                var houseId = ctx.GetComponent<CowComponent>(cowEntity).HouseId;
                if (houseId != Entity.Null)
                    target = houseId;

                if (globalRes.Grass <= 0 || cow.Exhaust >= cow.MaxExhaust)
                {
                    StateDefinitions.BeginExit(ref sc);
                    ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = sc.Key, Phase = sc.Phase, Age = 0 });
                }
                return true;
            }
        }

        return false;
    }

    private bool HandleHouseAssign(Context ctx, Entity playerEntity, Entity houseEntity, ref PlayerStateComponent playerState, ref StateComponent sc)
    {
        StateDefinitions.Begin(ref sc, StateKeys.Assign);
        playerState.InteractionTarget = houseEntity;

        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Assign, Phase = sc.Phase, Age = 0 });

        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} assigning cow {playerState.FollowingCow.Id} to house {houseEntity.Id}");
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
            globalRes.Coins -= 1;
            land.CurrentCoins += 1;

            if (land.CurrentCoins >= land.Threshold)
            {
                var transform = ctx.State.GetComponent<Transform2D>(landEntity);
                var position = transform.Position;
                ctx.State.DeleteEntity(landEntity);

                HouseDefinition.Create(ctx, position);

                return false; // Entity deleted, skip interacted marker
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
