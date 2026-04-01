using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Utils.Logging;
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

            ILogger.Log($"[HighlightSystem] Player {playerEntity.Id}: prev={prev.Id} nearest={nearest.Id}");

            // Remove old highlight
            if (prev != Entity.Null && state.HasComponent<InteractHighlightComponent>(prev))
            {
                state.RemoveComponent<InteractHighlightComponent>(prev);
                ILogger.Log($"[HighlightSystem] Removed highlight from {prev.Id}");
            }

            // Add new highlight
            if (nearest != Entity.Null && !state.HasComponent<InteractHighlightComponent>(nearest))
            {
                state.AddComponent(nearest, new InteractHighlightComponent());
                ILogger.Log($"[HighlightSystem] Added highlight to {nearest.Id}");
            }

            // Re-get ref after component changes invalidated it
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.HighlightTarget = nearest;
            ILogger.Log($"[HighlightSystem] HighlightTarget set to {nearest.Id}");
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

            // Skip cows in player's follow chain
            if (state.HasComponent<CowComponent>(entity))
            {
                var cowComp = state.GetComponent<CowComponent>(entity);
                if (cowComp.FollowingPlayer == playerEntity) continue;
            }

            if (!state.HasComponent<Transform2D>(entity)) continue;

            // Only highlight entities that are actually interactable
            if (!IsInteractable(state, entity)) continue;

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

    private static bool IsInteractable(EntityWorld state, Entity entity)
    {
        return state.HasComponent<GrassComponent>(entity)
            || state.HasComponent<CowComponent>(entity)
            || state.HasComponent<HouseComponent>(entity)
            || state.HasComponent<LoveHouseComponent>(entity)
            || state.HasComponent<FoodSignComponent>(entity)
            || state.HasComponent<SellPointComponent>(entity)
            || state.HasComponent<LandComponent>(entity)
            || state.HasComponent<FinalStructureComponent>(entity)
            || state.HasComponent<HelperComponent>(entity);
    }
}
