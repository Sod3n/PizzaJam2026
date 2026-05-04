using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;

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
            if (!state.TryGetComponent<PlayerStateComponent>(playerEntity, out var ps)) continue;

            var helperEntity = state.GetComponent<InteractRequestComponent>(playerEntity).Target;
            if (!state.TryResolve<HelperArchetype>(helperEntity, out var helperRef)) continue;

            if (ps.FollowingHelper != Entity.Null) continue;

            var ctx = state.Ctx(playerEntity);
            if (IsHelperAssignedToHouse(ctx, helperEntity)) continue;

            PickupHelper(ctx, playerEntity, helperRef);
        }
    }

    private static void PickupHelper(Context ctx, Entity playerEntity, HelperRef helperRef)
    {
        helperRef.Helper.OwnerPlayer = playerEntity;
        ctx.State.GetComponent<PlayerStateComponent>(playerEntity).FollowingHelper = helperRef.Entity;
        helperRef.CharacterBody2D.Velocity = Vector2.Zero;

        ILogger.Log($"[HelperPickupSystem] Player {playerEntity.Id} picked up helper {helperRef.Entity.Id}");
        InteractFeedback.Success(ctx, playerEntity, helperRef.Entity);
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
