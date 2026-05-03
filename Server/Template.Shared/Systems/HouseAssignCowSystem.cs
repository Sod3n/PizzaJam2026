using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Owns the "click a house while leading a cow chain → assign first cow to house" interaction.
//
// Match preconditions (mutually exclusive with HouseMilk / HouseAssignFollowingHelper / HouseHelperPlayer):
//   target has HouseComponent
//   player is NOT a helper-player
//   player has FollowingCow != Entity.Null
//
// On match: begin Assign state on the player and stash the house as InteractionTarget.
// CowInteractionSystem completes the assign when the state finishes.
public class HouseAssignCowSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;
            if (!state.HasComponent<StateComponent>(playerEntity)) continue;

            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var houseEntity = req.Target;
            if (!state.HasComponent<HouseComponent>(houseEntity)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
            if (ps.FollowingCow == Entity.Null) continue;

            BeginAssign(state, playerEntity, houseEntity);
        }
    }

    private static void BeginAssign(EntityWorld state, Entity playerEntity, Entity houseEntity)
    {
        var ctx = new Context(state, playerEntity, null!);

        StatePhase phase;
        {
            ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
            StateDefinitions.Begin(ref sc, StateKeys.Assign);
            phase = sc.Phase;
        }

        Entity followingCow;
        {
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.InteractionTarget = houseEntity;
            followingCow = ps.FollowingCow;
        }

        state.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Assign, Phase = phase, Age = 0 });

        ILogger.Log($"[HouseAssignCowSystem] Player {playerEntity.Id} assigning cow {followingCow.Id} to house {houseEntity.Id}");
        InteractFeedback.Success(ctx, playerEntity, houseEntity);
    }
}
