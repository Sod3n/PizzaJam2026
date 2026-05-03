using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Owns the helper-player branch of "click a house" — moving a helper-player into / out of a house slot.
//
// Match preconditions (mutually exclusive with HouseMilk / HouseAssign / HouseAssignFollowingHelper):
//   target has HouseComponent
//   player IS a helper-player
//
// Sub-paths:
//   house already occupied by this helper-player → move out, despawn signs.
//   house empty (no cow, no helper)              → move in, spawn role sign for current role.
//   anything else                                → silent skip (handled by fallback).
public class HouseHelperPlayerSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            if (!state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var houseEntity = req.Target;
            if (!state.HasComponent<HouseComponent>(houseEntity)) continue;

            TryHandleHouseClick(state, playerEntity, houseEntity);
        }
    }

    private static void TryHandleHouseClick(EntityWorld state, Entity playerEntity, Entity houseEntity)
    {
        var ctx = new Context(state, playerEntity, null!);
        var house = state.GetComponent<HouseComponent>(houseEntity);

        if (house.HelperId == playerEntity)
        {
            {
                ref var h = ref state.GetComponent<HouseComponent>(houseEntity);
                h.HelperId = Entity.Null;
            }
            InteractActionService.DespawnSignsForHouse(ctx, houseEntity);
            InteractFeedback.Success(ctx, playerEntity, houseEntity);
            ILogger.Log($"[HouseHelperPlayerSystem] Helper-player {playerEntity.Id} moved out of house {houseEntity.Id}");
            return;
        }

        if (house.CowId == Entity.Null && house.HelperId == Entity.Null)
        {
            int currentRole = state.GetComponent<HelperPlayerComponent>(playerEntity).Type;
            {
                ref var h = ref state.GetComponent<HouseComponent>(houseEntity);
                h.HelperId = playerEntity;
            }
            InteractActionService.EnsureRoleSignForHouseHelperPlayer(ctx, houseEntity, currentRole);
            InteractFeedback.Success(ctx, playerEntity, houseEntity);
            ILogger.Log($"[HouseHelperPlayerSystem] Helper-player {playerEntity.Id} moved into house {houseEntity.Id}");
        }
    }
}
