using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;

namespace Template.Shared.Systems;

// Pick up an idle helper into the player's FollowingHelper slot (helper trails the player
// like a cow in a follow chain).
//
// Match preconditions (mutually exclusive with HelperDropSystem and HelperExchangeSystem):
//   target has HelperComponent
//   playerState.FollowingHelper == Entity.Null  (hands free for helper)
//   helper is NOT assigned to a house (assigned helpers belong to their house)
//
// In the original code, the dispatcher tried HelperExchange first and only fell through to
// pickup if exchange returned false. To preserve that ordering in the marker pattern, this
// system runs AFTER HelperExchangeSystem in the system registration order, and HelperExchange
// only matches when an exchange would actually take effect. So if HelperExchange claimed the
// marker, this system sees no marker. If exchange wouldn't have done anything, the marker
// is still there for this system to claim.
public class HelperPickupSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;

            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var helperEntity = req.Target;
            if (!state.HasComponent<HelperComponent>(helperEntity)) continue;

            var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
            if (ps.FollowingHelper != Entity.Null) continue;

            var ctx = state.Ctx(playerEntity);
            if (IsHelperAssignedToHouse(ctx, helperEntity)) continue;

            PickupHelper(state, ctx, playerEntity, helperEntity);
        }
    }

    private static void PickupHelper(EntityWorld state, Context ctx, Entity playerEntity, Entity helperEntity)
    {
        {
            ref var helper = ref state.GetComponent<HelperComponent>(helperEntity);
            helper.OwnerPlayer = playerEntity;
        }
        {
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.FollowingHelper = helperEntity;
        }

        if (state.HasComponent<CharacterBody2D>(helperEntity))
        {
            ref var hb = ref state.GetComponent<CharacterBody2D>(helperEntity);
            hb.Velocity = Vector2.Zero;
        }

        ILogger.Log($"[HelperPickupSystem] Player {playerEntity.Id} picked up helper {helperEntity.Id}");
        InteractFeedback.Success(ctx, playerEntity, helperEntity);
    }

    private static bool IsHelperAssignedToHouse(Context ctx, Entity helperEntity)
    {
        foreach (var he in ctx.State.Filter<HouseComponent>())
        {
            if (ctx.State.GetComponent<HouseComponent>(he).HelperId == helperEntity) return true;
        }
        return false;
    }
}
