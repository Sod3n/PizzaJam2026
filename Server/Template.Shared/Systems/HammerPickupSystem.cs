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
            if (!state.HasComponent<HammerComponent>(hammerEntity)) continue;
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;

            {
                var h = state.GetComponent<HammerComponent>(hammerEntity);
                if (h.State != HammerState.Idle) continue;
            }
            {
                var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
                if (ps.CarriedEntity != Entity.Null) continue;
            }

            HandlePickup(state, playerEntity, hammerEntity);
        }
    }

    private static void HandlePickup(EntityWorld state, Entity playerEntity, Entity hammerEntity)
    {
        var ctx = new Context(state, playerEntity, null!);
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
