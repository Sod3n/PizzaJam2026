using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public class InteractMarkersSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var entity in state.Filter<InteractedComponent>())
        {
            ref var interacted = ref state.GetComponent<InteractedComponent>(entity);
            interacted.Lifetime++;
            if (interacted.Lifetime > 2)
                state.RemoveComponent<InteractedComponent>(entity);
        }
    }
}