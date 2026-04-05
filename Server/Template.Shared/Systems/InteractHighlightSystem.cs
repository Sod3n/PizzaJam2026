using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Types;
using Template.Shared.Actions;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public class InteractHighlightSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<PlayerStateComponent>())
        {
            if (!state.HasComponent<StateComponent>(playerEntity)) continue;

            var sc = state.GetComponent<StateComponent>(playerEntity);
            var playerState = state.GetComponent<PlayerStateComponent>(playerEntity);

            // Don't highlight during active states (milking, taming, etc.)
            Entity nearest = Entity.Null;
            if (!sc.IsEnabled)
                nearest = FindNearestFromZone(state, playerEntity, playerState);

            var prev = playerState.HighlightTarget;

            if (prev == nearest) continue;

            // Remove old highlight
            if (prev != Entity.Null && state.HasComponent<InteractHighlightComponent>(prev))
            {
                state.RemoveComponent<InteractHighlightComponent>(prev);
            }

            // Add new highlight
            if (nearest != Entity.Null && !state.HasComponent<InteractHighlightComponent>(nearest))
            {
                state.AddComponent(nearest, new InteractHighlightComponent());
            }

            // Re-get ref after component changes invalidated it
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.HighlightTarget = nearest;
        }
    }

    private static Entity FindNearestFromZone(EntityWorld state, Entity playerEntity, PlayerStateComponent playerState)
    {
        var zoneEntity = playerState.InteractionZone;
        if (zoneEntity == Entity.Null || !state.HasComponent<Area2D>(zoneEntity))
            return Entity.Null;

        ref var area = ref state.GetComponent<Area2D>(zoneEntity);
        if (!area.HasOverlappingBodies)
            return Entity.Null;

        var playerPos = state.GetComponent<Transform2D>(playerEntity).Position;
        Entity nearest = Entity.Null;
        Float minDistSq = 999999f;

        for (int i = 0; i < area.OverlappingEntities.Count; i++)
        {
            var entity = new Entity(area.OverlappingEntities[i]);
            if (entity == playerEntity) continue;
            if (entity == zoneEntity) continue;

            if (!state.HasComponent<Transform2D>(entity)) continue;

            // Only highlight entities that are actually interactable
            if (!InteractionLogic.IsInteractable(state, entity)) continue;

            var pos = state.GetComponent<Transform2D>(entity).Position;
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
