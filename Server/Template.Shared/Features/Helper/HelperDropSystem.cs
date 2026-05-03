using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Click the helper you're already carrying → drop in place.
//
// Match preconditions:
//   target has HelperComponent
//   playerState.FollowingHelper == target
//
// Drops the helper out of the FollowingHelper slot. Marks the helper with an Interacted
// state with Param="drop" (visual cue) and removes the marker. Does NOT use
// InteractFeedback.Success because Success uses Param for the GainedResource popup, but
// "drop" here is purely a visual hint on the helper itself, not a player gain popup.
public class HelperDropSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            if (!state.TryGetComponent<PlayerStateComponent>(playerEntity, out var ps)) continue;

            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var helperEntity = req.Target;
            if (!state.HasComponent<HelperComponent>(helperEntity)) continue;

            if (ps.FollowingHelper != helperEntity) continue;

            DropFollowingHelper(state, playerEntity, helperEntity);
        }
    }

    private static void DropFollowingHelper(EntityWorld state, Entity playerEntity, Entity helperEntity)
    {
        {
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.FollowingHelper = Entity.Null;
        }
        ILogger.Log($"[HelperDropSystem] Dropped carried helper {helperEntity.Id} in place");
        state.AddComponent(helperEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = "drop", Age = 0 });
        state.RemoveComponent<InteractRequestComponent>(playerEntity);
    }
}
