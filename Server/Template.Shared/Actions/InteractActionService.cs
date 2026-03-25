using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Systems;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Utils.Logging;

namespace Template.Shared.Actions;

public class InteractActionService : ActionService<InteractAction, PlayerEntity>
{
    protected override void ExecuteProcess(InteractAction action, ref PlayerEntity playerComp, Context ctx)
    {
        Entity playerEntity = ctx.Entity;

        if (playerComp.UserId != action.UserId) return;
        if (!ctx.State.HasComponent<PlayerStateComponent>(playerEntity)) return;
        if (!ctx.State.HasComponent<StateComponent>(playerEntity)) return;

        ref var playerState = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
        ref var sc = ref ctx.State.GetComponent<StateComponent>(playerEntity);

        // If already milking, use stored target directly (cow is hidden)
        if (sc.Key == "milking" && sc.IsEnabled)
        {
            var cowEntity = playerState.InteractionTarget;
            if (cowEntity == Entity.Null || !ctx.State.HasComponent<CowComponent>(cowEntity)) return;

            Entity globalResEntity = GetGlobalResourcesEntity(ctx);
            if (globalResEntity == Entity.Null) return;
            ref var globalRes = ref ctx.State.GetComponent<GlobalResourcesComponent>(globalResEntity);

            Entity target = cowEntity;
            if (HandleCowInteraction(ctx, playerEntity, cowEntity, ref globalRes, ref target))
            {
                ctx.State.AddComponent(target, new EnterStateComponent { Key = "interacted", Age = 0 });
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
        Entity interactedTarget = nearestTarget;

        if (ctx.State.HasComponent<GrassComponent>(nearestTarget))
        {
            success = HandleGrassInteraction(ctx, nearestTarget, ref globalRes2);
        }
        else if (ctx.State.HasComponent<CowComponent>(nearestTarget))
        {
            success = HandleCowInteraction(ctx, playerEntity, nearestTarget, ref globalRes2, ref interactedTarget);
        }
        else if (ctx.State.HasComponent<SellPointComponent>(nearestTarget))
        {
            success = HandleSellPointInteraction(ctx, ref globalRes2);
        }
        else if (ctx.State.HasComponent<LandComponent>(nearestTarget))
        {
            success = HandleLandInteraction(ctx, nearestTarget, ref globalRes2);
        }
        else if (ctx.State.HasComponent<FinalStructureComponent>(nearestTarget))
        {
            success = HandleFinalStructureInteraction(ctx, nearestTarget, ref globalRes2);
        }

        if (success)
        {
            ctx.State.AddComponent(interactedTarget, new EnterStateComponent { Key = "interacted", Age = 0 });
            ILogger.Log($"[InteractActionService] Marked {interactedTarget.Id} as successfully interacted");
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

    private bool HandleCowInteraction(Context ctx, Entity playerEntity, Entity cowEntity, ref GlobalResourcesComponent globalRes, ref Entity target)
    {
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
        if (!ctx.State.HasComponent<PlayerStateComponent>(playerEntity)) return false;
        if (!ctx.State.HasComponent<StateComponent>(playerEntity)) return false;

        ref var playerState = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
        ref var sc = ref ctx.State.GetComponent<StateComponent>(playerEntity);

        // Attempting to Start Milking (idle = state not enabled)
        if (!sc.IsEnabled)
        {
            if (cow.IsMilking) return false;
            if (globalRes.Grass <= 0 || cow.Exhaust >= cow.MaxExhaust) return false;

            cow.IsMilking = true;

            sc.Key = "milking_enter";
            sc.CurrentTime = 0;
            sc.MaxTime = CowSystem.PhaseDurationTicks;
            sc.IsEnabled = true;

            ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = "milking_enter", Age = 0 });

            playerState.InteractionTarget = cowEntity;

            if (ctx.State.HasComponent<Transform2D>(playerEntity))
                playerState.ReturnPosition = ctx.State.GetComponent<Transform2D>(playerEntity).Position;

            return true;
        }

        // Clicking while Milking
        if (sc.Key == "milking" && sc.IsEnabled)
        {
            if (globalRes.Grass > 0 && cow.Exhaust < cow.MaxExhaust)
            {
                globalRes.Grass--;
                globalRes.Milk++;
                cow.Exhaust++;

                target = ctx.GetComponent<CowComponent>(cowEntity).HouseId;

                if (globalRes.Grass <= 0 || cow.Exhaust >= cow.MaxExhaust)
                {
                    sc.Key = "milking_exit";
                    sc.CurrentTime = 0;
                    sc.MaxTime = CowSystem.PhaseDurationTicks;
                    sc.IsEnabled = true;

                    ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = "milking_exit", Age = 0 });
                }
                return true;
            }
        }

        return false;
    }

    private bool HandleSellPointInteraction(Context ctx, ref GlobalResourcesComponent globalRes)
    {
        if (globalRes.Milk > 0)
        {
            globalRes.Milk -= 1;
            globalRes.Coins += 1;
            return true;
        }
        return false;
    }

    private bool HandleLandInteraction(Context ctx, Entity landEntity, ref GlobalResourcesComponent globalRes)
    {
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

                var houseEntity = HouseDefinition.Create(ctx, position);
                var cowPosition = position + new Vector2(2, 2);
                var cowEntity = CowDefinition.Create(ctx, cowPosition);

                ref var house = ref ctx.State.GetComponent<HouseComponent>(houseEntity);
                house.CowId = cowEntity;

                ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
                cow.HouseId = houseEntity;

                return false; // Entity deleted, skip interacted marker
            }
            return true;
        }
        return false;
    }

    private bool HandleFinalStructureInteraction(Context ctx, Entity finalEntity, ref GlobalResourcesComponent globalRes)
    {
        if (globalRes.Coins <= 0) return false;

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
