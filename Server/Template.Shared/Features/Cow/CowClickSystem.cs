using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;

namespace Template.Shared.Systems;

// Owns the "click a cow" interaction for the main player.
//
// Match preconditions (mutually exclusive with HouseAssign / HouseAssignHelper / HouseHelperPlayer):
//   target has CowComponent
//   player is NOT a helper-player
//
// Sub-paths:
//   cow already follows player + has LoveTarget → confession popup (first click) and stay following.
//   cow already follows player + no LoveTarget → dismiss from follow chain.
//   cow.IsMilking                               → silent skip (fallback shows InfoHouse via cow's house).
//   cow.IsDepressed                             → BuildingInfo popup, claim.
//   another player following cow                → silent skip.
//   idle cow                                    → begin Taming state on player.
public class CowClickSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;
            if (!state.HasComponent<StateComponent>(playerEntity)) continue;

            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var cowEntity = req.Target;
            if (!state.HasComponent<CowComponent>(cowEntity)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            TryHandleCowClick(state, playerEntity, cowEntity);
        }
    }

    private static void TryHandleCowClick(EntityWorld state, Entity playerEntity, Entity cowEntity)
    {
        var ctx = state.Ctx(playerEntity);
        var cow = state.GetComponent<CowComponent>(cowEntity);

        if (cow.FollowingPlayer == playerEntity)
        {
            if (cow.LoveTarget != Entity.Null)
                HandleLoveCow(state, ctx, playerEntity, cowEntity);
            else
                HandleDismissFromChain(state, ctx, playerEntity, cowEntity);
            return;
        }

        if (cow.IsMilking) return;

        if (cow.IsDepressed)
        {
            InteractFeedback.Success(ctx, playerEntity, cowEntity);
            state.AddComponent(cowEntity, new EnterStateComponent { Key = StateKeys.BuildingInfo, Param = StateKeys.InfoDepressed, Age = 0 });
            return;
        }

        if (cow.FollowingPlayer != Entity.Null) return;

        BeginTaming(state, ctx, playerEntity, cowEntity);
    }

    private static void BeginTaming(EntityWorld state, Context ctx, Entity playerEntity, Entity cowEntity)
    {
        StatePhase phase;
        {
            ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
            StateDefinitions.Begin(ref sc, StateKeys.Taming);
            phase = sc.Phase;
        }

        state.GetComponent<PlayerStateComponent>(playerEntity).InteractionTarget = cowEntity;
        state.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Taming, Phase = phase, Age = 0 });
        InteractFeedback.Success(ctx, playerEntity, cowEntity);

        ILogger.Log($"[CowClickSystem] Player {playerEntity.Id} taming cow {cowEntity.Id}");
    }

    private static void HandleLoveCow(EntityWorld state, Context ctx, Entity playerEntity, Entity cowEntity)
    {
        var cow = state.GetComponent<CowComponent>(cowEntity);

        if (cow.LoveConfessed)
        {
            InteractFeedback.Success(ctx, playerEntity, cowEntity);
            ILogger.Log($"[CowClickSystem] Love cow {cowEntity.Id} already confessed — still following player");
            return;
        }

        string targetName = "???";
        if (state.TryGetComponent<NameComponent>(cow.LoveTarget, out var loveTargetName))
            targetName = loveTargetName.Name.ToString();

        state.GetComponent<CowComponent>(cowEntity).LoveConfessed = true;

        InteractFeedback.Success(ctx, playerEntity, cowEntity);
        state.AddComponent(cowEntity, new EnterStateComponent { Key = StateKeys.LoveCow, Param = targetName, Age = 0 });

        ILogger.Log($"[CowClickSystem] Love cow {cowEntity.Id} confessed — loves {targetName} (cow {cow.LoveTarget.Id})");
    }

    private static void HandleDismissFromChain(EntityWorld state, Context ctx, Entity playerEntity, Entity cowEntity)
    {
        var cow = state.GetComponent<CowComponent>(cowEntity);

        Entity next = FindNextChainLink(state, playerEntity, cowEntity);
        bool isHead = state.GetComponent<PlayerStateComponent>(playerEntity).FollowingCow == cowEntity;

        if (isHead)
        {
            state.GetComponent<PlayerStateComponent>(playerEntity).FollowingCow = next;
            if (next != Entity.Null)
                state.GetComponent<CowComponent>(next).FollowTarget = playerEntity;
        }
        else if (next != Entity.Null)
        {
            state.GetComponent<CowComponent>(next).FollowTarget = cow.FollowTarget;
        }

        state.GetComponent<CowComponent>(cowEntity).ClearFollowChain();

        InteractFeedback.Success(ctx, playerEntity, cowEntity);
        ILogger.Log($"[CowClickSystem] Player {playerEntity.Id} dismissed cow {cowEntity.Id} from follow chain");
    }

    private static Entity FindNextChainLink(EntityWorld state, Entity playerEntity, Entity cowEntity)
    {
        foreach (var ce in state.Filter<CowComponent>())
        {
            if (ce == cowEntity) continue;
            var c = state.GetComponent<CowComponent>(ce);
            if (c.FollowTarget == cowEntity && c.FollowingPlayer == playerEntity)
                return ce;
        }
        return Entity.Null;
    }
}
