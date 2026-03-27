using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Utils.Logging;

namespace Template.Shared.Actions;

public class AltInteractActionService : ActionService<AltInteractAction, PlayerEntity>
{
    protected override void ExecuteProcess(AltInteractAction action, ref PlayerEntity playerComp, Context ctx)
    {
        Entity playerEntity = ctx.Entity;

        if (playerComp.UserId != action.UserId) return;
        if (!ctx.State.HasComponent<PlayerStateComponent>(playerEntity)) return;
        if (!ctx.State.HasComponent<StateComponent>(playerEntity)) return;

        ref var playerState = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
        ref var sc = ref ctx.State.GetComponent<StateComponent>(playerEntity);

        // Can't alt-interact while in any active state
        if (sc.IsEnabled) return;

        // Already following a cow? Release it
        if (playerState.FollowingCow != Entity.Null)
        {
            if (ctx.State.HasComponent<CowComponent>(playerState.FollowingCow))
            {
                StateDefinitions.Begin(ref sc, StateKeys.Release);
                ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Release, Phase = sc.Phase, Age = 0 });

                ILogger.Log($"[AltInteractActionService] Player {playerEntity.Id} releasing cow {playerState.FollowingCow.Id}");
            }
            else
            {
                // Cow entity gone, just clear
                playerState.FollowingCow = Entity.Null;
            }
            return;
        }

        // Find nearest cow in zone
        Entity nearestCow = FindNearestCow(ctx, playerEntity, ref playerState);
        if (nearestCow == Entity.Null) return;

        ref var cow = ref ctx.State.GetComponent<CowComponent>(nearestCow);

        // Can't tame a cow that's being milked or already following someone
        if (cow.IsMilking) return;
        if (cow.FollowingPlayer != Entity.Null) return;

        StateDefinitions.Begin(ref sc, StateKeys.Taming);
        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Taming, Phase = sc.Phase, Age = 0 });

        // Store the target cow for when taming completes
        playerState.InteractionTarget = nearestCow;

        ILogger.Log($"[AltInteractActionService] Player {playerEntity.Id} taming cow {nearestCow.Id}");
    }

    private Entity FindNearestCow(Context ctx, Entity playerEntity, ref PlayerStateComponent playerState)
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
            if (!ctx.State.HasComponent<CowComponent>(entity)) continue;
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
}
