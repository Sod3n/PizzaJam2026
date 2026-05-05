using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Template.Shared.Actions;
using Template.Shared.Components;

namespace Template.Shared.Systems;

// Click on a dropped hammer → carry it. No-op silently if hands are full or the hammer is
// already in someone's hand.
public class HammerPickupSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var hammerEntity = req.Target;
            if (!state.TryGetComponent<HammerComponent>(hammerEntity, out var h)) continue;
            if (!state.TryGetComponent<PlayerStateComponent>(playerEntity, out var ps)) continue;
            if (h.State != HammerState.Idle) continue;
            if (ps.CarriedEntity != Entity.Null) continue;

            HandlePickup(state, playerEntity, hammerEntity);
        }
    }

    private static void HandlePickup(EntityWorld state, Entity playerEntity, Entity hammerEntity)
    {
        var ctx = state.Ctx(playerEntity);
        {
            ref var h = ref state.GetComponent<HammerComponent>(hammerEntity);
            h.State = HammerState.Carried;
        }
        {
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.CarriedEntity = hammerEntity;
        }
        InteractFeedback.Success(ctx, playerEntity, hammerEntity);
    }
}
