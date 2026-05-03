using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Systems;

/// <summary>
/// Decrements <see cref="CooldownComponent.TicksRemaining"/> on every entity each tick when
/// <see cref="CooldownComponent.Unit"/> is <see cref="CooldownUnit.Ticks"/>.
/// Day-unit cooldowns are not touched here — they're cleared by <see cref="SleepLogic.AdvanceDay"/>.
/// The component itself stays attached at zero so MaxTicks is preserved for future demolish/rebuild.
/// </summary>
public class CooldownSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var entity in state.Filter<CooldownComponent>())
        {
            ref var cd = ref state.GetComponent<CooldownComponent>(entity);
            if (cd.Unit != CooldownUnit.Ticks) continue;
            if (cd.TicksRemaining > 0) cd.TicksRemaining--;
        }
    }
}
