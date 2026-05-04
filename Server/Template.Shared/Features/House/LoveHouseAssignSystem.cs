using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;

namespace Template.Shared.Systems;

// Assigns the player's lead following cow into an empty slot on the love house. Promotes the
// next cow in the chain to lead. Strategic — main player only, no cooldown, requires at least
// one empty slot.
public class LoveHouseAssignSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;

            var loveHouseEntity = state.GetComponent<InteractRequestComponent>(playerEntity).Target;
            if (!state.TryResolve<LoveHouseArchetype>(loveHouseEntity, out var loveHouseRef)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            var ctx = state.Ctx(playerEntity);
            if (InteractActionService.IsOnCooldown(ctx, loveHouseEntity)) continue;

            if (state.GetComponent<PlayerStateComponent>(playerEntity).FollowingCow == Entity.Null) continue;

            if (loveHouseRef.CowSlot1 != Entity.Null && loveHouseRef.CowSlot2 != Entity.Null) continue;

            AssignCowToLoveHouse(state, playerEntity, loveHouseRef);
        }
    }

    private static void AssignCowToLoveHouse(EntityWorld state, Entity playerEntity, LoveHouseRef loveHouseRef)
    {
        Entity cowToAssign = state.GetComponent<PlayerStateComponent>(playerEntity).FollowingCow;
        if (cowToAssign == Entity.Null) return;

        var loveHouseEntity = loveHouseRef.Entity;
        Entity nextCow = CowSystemHelpers.FindNextCowInChain(state, cowToAssign);

        {
            ref var cow = ref state.GetComponent<CowComponent>(cowToAssign);
            if (cow.PreviousHouseId == Entity.Null)
                cow.PreviousHouseId = cow.HouseId;
            cow.ClearFollowChain();
            cow.HouseId = loveHouseEntity;
        }

        if (state.HasComponent<CharacterBody2D>(cowToAssign))
            state.GetComponent<CharacterBody2D>(cowToAssign).Velocity = Vector2.Zero;

        {
            ref var lh = ref loveHouseRef.LoveHouse;
            if (lh.CowId1 == Entity.Null) lh.CowId1 = cowToAssign;
            else lh.CowId2 = cowToAssign;
        }

        if (nextCow != Entity.Null)
            state.GetComponent<CowComponent>(nextCow).FollowTarget = playerEntity;
        state.GetComponent<PlayerStateComponent>(playerEntity).FollowingCow = nextCow;

        ILogger.Log($"[LoveHouseAssignSystem] Assigned cow {cowToAssign.Id} to love house {loveHouseEntity.Id}");

        InteractFeedback.Success(state.Ctx(playerEntity), playerEntity, loveHouseEntity);
    }
}
