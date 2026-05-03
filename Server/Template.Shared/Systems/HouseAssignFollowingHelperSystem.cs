using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Owns the "click an empty house while carrying a helper → move helper into the house" interaction.
//
// Match preconditions (mutually exclusive with HouseMilk / HouseAssign / HouseHelperPlayer):
//   target has HouseComponent
//   player is NOT a helper-player
//   player has FollowingHelper != Entity.Null
//   house is empty (CowId == Entity.Null AND HelperId == Entity.Null)
//
// On match: place the helper at the house, clear FollowingHelper, spawn role sign.
public class HouseAssignFollowingHelperSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;

            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var houseEntity = req.Target;
            if (!state.HasComponent<HouseComponent>(houseEntity)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
            var helperEntity = ps.FollowingHelper;
            if (helperEntity == Entity.Null) continue;
            if (!state.HasComponent<HelperComponent>(helperEntity)) continue;

            var house = state.GetComponent<HouseComponent>(houseEntity);
            if (house.CowId != Entity.Null) continue;
            if (house.HelperId != Entity.Null) continue;

            AssignHelperToHouse(state, playerEntity, houseEntity, helperEntity);
        }
    }

    private static void AssignHelperToHouse(EntityWorld state, Entity playerEntity, Entity houseEntity, Entity helperEntity)
    {
        var ctx = new Context(state, playerEntity, null!);

        {
            ref var house = ref state.GetComponent<HouseComponent>(houseEntity);
            house.HelperId = helperEntity;
        }

        if (state.HasComponent<Transform2D>(houseEntity) && state.HasComponent<Transform2D>(helperEntity))
        {
            var housePos = state.GetComponent<Transform2D>(houseEntity).Position;
            ref var ht = ref state.GetComponent<Transform2D>(helperEntity);
            ht.Position = housePos;
        }
        if (state.HasComponent<CharacterBody2D>(helperEntity))
        {
            ref var hb = ref state.GetComponent<CharacterBody2D>(helperEntity);
            hb.Velocity = Vector2.Zero;
        }

        InteractActionService.EnsureRoleSignForHouse(ctx, houseEntity, helperEntity);

        {
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.FollowingHelper = Entity.Null;
        }

        ILogger.Log($"[HouseAssignFollowingHelperSystem] Player {playerEntity.Id} assigned carried helper {helperEntity.Id} to house {houseEntity.Id}");
        InteractFeedback.Success(ctx, playerEntity, houseEntity);
    }
}
