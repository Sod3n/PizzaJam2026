using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;
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

            var houseEntity = state.GetComponent<InteractRequestComponent>(playerEntity).Target;
            if (!state.TryResolve<HouseArchetype>(houseEntity, out var houseRef)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            var helperEntity = state.GetComponent<PlayerStateComponent>(playerEntity).FollowingHelper;
            if (helperEntity == Entity.Null) continue;
            if (!state.HasComponent<HelperComponent>(helperEntity)) continue;

            if (houseRef.CowSlot != Entity.Null) continue;
            if (houseRef.House.HelperId != Entity.Null) continue;

            AssignHelperToHouse(state, playerEntity, houseRef, helperEntity);
        }
    }

    private static void AssignHelperToHouse(EntityWorld state, Entity playerEntity, HouseRef houseRef, Entity helperEntity)
    {
        var ctx = state.Ctx(playerEntity);
        var houseEntity = houseRef.Entity;

        houseRef.House.HelperId = helperEntity;

        // Don't teleport — helper navigates to its house on the next tick via NavigateHome.
        // Just clear leftover follow-velocity so it doesn't drift before nav kicks in.
        if (state.HasComponent<CharacterBody2D>(helperEntity))
            state.GetComponent<CharacterBody2D>(helperEntity).Velocity = Vector2.Zero;

        InteractActionService.EnsureRoleSignForHouse(ctx, houseEntity, helperEntity);

        state.GetComponent<PlayerStateComponent>(playerEntity).FollowingHelper = Entity.Null;

        ILogger.Log($"[HouseAssignFollowingHelperSystem] Player {playerEntity.Id} assigned carried helper {helperEntity.Id} to house {houseEntity.Id}");
        InteractFeedback.Success(ctx, playerEntity, houseEntity);
    }

}
