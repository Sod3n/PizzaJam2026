using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;

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

            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var loveHouseEntity = req.Target;
            if (!state.HasComponent<LoveHouseComponent>(loveHouseEntity)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            var ctx = new Context(state, playerEntity, null!);
            if (InteractActionService.IsOnCooldown(ctx, loveHouseEntity)) continue;

            var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
            if (ps.FollowingCow == Entity.Null) continue;

            var lh = state.GetComponent<LoveHouseComponent>(loveHouseEntity);
            bool slotAvailable = lh.CowId1 == Entity.Null || lh.CowId2 == Entity.Null;
            if (!slotAvailable) continue;

            AssignCowToLoveHouse(state, playerEntity, loveHouseEntity);
        }
    }

    private static void AssignCowToLoveHouse(EntityWorld state, Entity playerEntity, Entity loveHouseEntity)
    {
        Entity cowToAssign = state.GetComponent<PlayerStateComponent>(playerEntity).FollowingCow;
        if (cowToAssign == Entity.Null) return;

        Entity nextCow = Entity.Null;
        foreach (var ce in state.Filter<CowComponent>())
        {
            var c = state.GetComponent<CowComponent>(ce);
            if (c.FollowTarget == cowToAssign && c.FollowingPlayer != Entity.Null)
            { nextCow = ce; break; }
        }

        {
            ref var cow = ref state.GetComponent<CowComponent>(cowToAssign);
            if (cow.PreviousHouseId == Entity.Null)
                cow.PreviousHouseId = cow.HouseId;
            cow.FollowingPlayer = Entity.Null;
            cow.FollowTarget = Entity.Null;
            cow.HouseId = loveHouseEntity;
        }

        if (state.HasComponent<CharacterBody2D>(cowToAssign))
        {
            ref var body = ref state.GetComponent<CharacterBody2D>(cowToAssign);
            body.Velocity = Vector2.Zero;
        }

        {
            ref var loveHouse = ref state.GetComponent<LoveHouseComponent>(loveHouseEntity);
            bool isFirstSlot = loveHouse.CowId1 == Entity.Null;
            if (isFirstSlot)
                loveHouse.CowId1 = cowToAssign;
            else
                loveHouse.CowId2 = cowToAssign;
        }

        if (nextCow != Entity.Null)
        {
            {
                ref var nextCowComp = ref state.GetComponent<CowComponent>(nextCow);
                nextCowComp.FollowTarget = playerEntity;
            }
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.FollowingCow = nextCow;
        }
        else
        {
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.FollowingCow = Entity.Null;
        }

        ILogger.Log($"[LoveHouseAssignSystem] Assigned cow {cowToAssign.Id} to love house {loveHouseEntity.Id}");

        var ctx = new Context(state, playerEntity, null!);
        InteractFeedback.Success(ctx, playerEntity, loveHouseEntity);
    }
}
