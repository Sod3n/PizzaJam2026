using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public class PlayerHouseSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        // Cooldown is now driven by CooldownSystem decrementing CooldownComponent (Unit=Ticks).
        // PlayerHouseSystem reserved for any future per-tick player-house logic.
    }
}
