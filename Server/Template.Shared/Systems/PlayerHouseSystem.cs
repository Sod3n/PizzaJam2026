using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public class PlayerHouseSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var entity in state.Filter<PlayerHouseComponent>())
        {
            ref var ph = ref state.GetComponent<PlayerHouseComponent>(entity);
            if (ph.CooldownTicksRemaining > 0)
                ph.CooldownTicksRemaining--;
        }
    }
}
