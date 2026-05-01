using Deterministic.GameFramework.ECS;
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

            // Shared with InteractActionService — adding a new component to
            // InteractionLogic.IsInteractable wires it up here automatically.
            Entity nearest = Entity.Null;
            if (!sc.IsEnabled)
                nearest = InteractionLogic.FindNearestInteractableInZone(state, playerEntity, playerState.InteractionZone);

            var prev = playerState.HighlightTarget;
            if (prev == nearest) continue;

            if (prev != Entity.Null && state.HasComponent<InteractHighlightComponent>(prev))
                state.RemoveComponent<InteractHighlightComponent>(prev);

            if (nearest != Entity.Null && !state.HasComponent<InteractHighlightComponent>(nearest))
                state.AddComponent(nearest, new InteractHighlightComponent());

            // Re-get ref after component changes invalidated it
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.HighlightTarget = nearest;
        }
    }
}
