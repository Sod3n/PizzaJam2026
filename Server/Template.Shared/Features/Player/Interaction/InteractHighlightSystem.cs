using Deterministic.GameFramework.ECS;
using Template.Shared.Actions;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public class InteractHighlightSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        bool anyCaught = false;
        foreach (var _ in state.Filter<CaughtComponent>()) { anyCaught = true; break; }

        foreach (var playerEntity in state.Filter<PlayerStateComponent>())
        {
            if (!state.HasComponent<StateComponent>(playerEntity)) continue;

            var sc = state.GetComponent<StateComponent>(playerEntity);
            var playerState = state.GetComponent<PlayerStateComponent>(playerEntity);

            // Shared with InteractActionService — adding a new component to
            // InteractionLogic.IsInteractable wires it up here automatically.
            Entity nearest = Entity.Null;
            if (!sc.IsEnabled && !anyCaught)
                nearest = InteractionLogic.FindNearestInteractableInZone(state, playerEntity, playerState.InteractionZone);

            var prev = playerState.HighlightTarget;
            if (prev == nearest) continue;

            if (prev != Entity.Null && state.HasComponent<InteractHighlightComponent>(prev))
                state.RemoveComponent<InteractHighlightComponent>(prev);

            if (nearest != Entity.Null && !state.HasComponent<InteractHighlightComponent>(nearest))
            {
                var highlight = new InteractHighlightComponent();
                if (TryResolveLandType(state, nearest, out var landType))
                {
                    highlight.HintLandType = landType;
                    highlight.HasHintLandType = true;
                }
                state.AddComponent(nearest, highlight);
            }

            // Re-get ref after component changes invalidated it
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.HighlightTarget = nearest;
        }
    }

    private static bool TryResolveLandType(EntityWorld state, Entity entity, out LandType type)
    {
        type = default;
        if (state.HasComponent<BuildingComponent>(entity))
        {
            type = state.GetComponent<BuildingComponent>(entity).Type;
            return true;
        }
        if (state.HasComponent<LandSignComponent>(entity))
        {
            type = state.GetComponent<LandSignComponent>(entity).SelectedType;
            return true;
        }
        if (state.HasComponent<LandPriceSignComponent>(entity))
        {
            var landId = state.GetComponent<LandPriceSignComponent>(entity).LandId;
            if (state.HasComponent<LandComponent>(landId))
            {
                type = state.GetComponent<LandComponent>(landId).Type;
                return true;
            }
        }
        if (state.HasComponent<LandComponent>(entity))
        {
            type = state.GetComponent<LandComponent>(entity).Type;
            return true;
        }
        return false;
    }
}
