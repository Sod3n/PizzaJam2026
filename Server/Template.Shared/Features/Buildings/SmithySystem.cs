using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;

namespace Template.Shared.Systems;

// Click on the smithy → spawn a hammer at the player's feet and put it in their carry slot.
// Requires empty hands and a known position.
public class SmithySystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var smithyEntity = req.Target;
            if (!state.HasComponent<SmithyComponent>(smithyEntity)) continue;
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;

            {
                var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
                if (ps.CarriedEntity != Entity.Null) continue;
            }
            if (!state.HasComponent<Transform2D>(playerEntity)) continue;

            HandleSmithy(state, playerEntity, smithyEntity);
        }
    }

    private static void HandleSmithy(EntityWorld state, Entity playerEntity, Entity smithyEntity)
    {
        var ctx = state.Ctx(playerEntity);
        var pp = state.GetComponent<Transform2D>(playerEntity).Position;

        SpawnAndCarryHammer(ctx, playerEntity, pp);
        InteractFeedback.Success(ctx, playerEntity, smithyEntity);
    }

    private static void SpawnAndCarryHammer(Context ctx, Entity playerEntity, Vector2 position)
    {
        var hammer = HammerDefinition.Create(ctx, position);
        {
            ref var h = ref ctx.State.GetComponent<HammerComponent>(hammer);
            h.State = HammerState.Carried;
        }
        {
            ref var ps = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
            ps.CarriedEntity = hammer;
        }
    }
}
