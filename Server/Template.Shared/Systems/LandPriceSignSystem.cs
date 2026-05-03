using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;

namespace Template.Shared.Systems;

// Deposits one coin from the player's global resources onto the linked land plot. Animates the
// sign visually (interactedTarget = sign), but logic acts on the linked LandComponent. On
// completion, deletes the land entity and constructs the chosen building via
// InteractActionService.CompleteLandBuilding.
public class LandPriceSignSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var signEntity = req.Target;
            if (!state.HasComponent<LandPriceSignComponent>(signEntity)) continue;

            var landId = state.GetComponent<LandPriceSignComponent>(signEntity).LandId;
            if (landId == Entity.Null || !state.HasComponent<LandComponent>(landId)) continue;

            DepositOnLand(state, playerEntity, signEntity, landId);
        }
    }

    private static void DepositOnLand(EntityWorld state, Entity playerEntity, Entity signEntity, Entity landEntity)
    {
        var ctx = new Context(state, playerEntity, null!);

        var grEntity = InteractFeedback.GetGlobalResourcesEntity(state);
        if (grEntity == Entity.Null) return;

        int coins;
        {
            ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            coins = globalRes.Coins;
        }

        if (coins <= 0)
        {
            InteractFeedback.MissingResource(ctx, playerEntity, signEntity, StateKeys.Coins);
            return;
        }

        int toDeposit = System.Math.Min(1, coins);
        int deposited = InteractionLogic.DepositToLand(state, landEntity, toDeposit, leaveOneForPlayer: false, out bool landComplete);
        {
            ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            globalRes.Coins -= deposited;
        }

        if (landComplete)
        {
            var position = state.GetComponent<Transform2D>(landEntity).Position;
            var land = state.GetComponent<LandComponent>(landEntity);
            var landType = land.Type;
            int gridX = land.Arm;
            int gridY = land.Ring;
            CooldownComponent? carry = null;
            if (state.HasComponent<CooldownComponent>(landEntity))
                carry = state.GetComponent<CooldownComponent>(landEntity);
            LandDefinition.DeleteSignsForLand(state, landEntity);
            state.DeleteEntity(landEntity);

            InteractActionService.CompleteLandBuilding(ctx, position, landType, gridX, gridY, carry);

            // Land + sign are gone — silently consume the marker without emitting feedback on a deleted entity.
            state.RemoveComponent<InteractRequestComponent>(playerEntity);
            return;
        }

        if (deposited > 0)
        {
            InteractFeedback.Success(ctx, playerEntity, signEntity);
        }
        else
        {
            InteractFeedback.MissingResource(ctx, playerEntity, signEntity, StateKeys.Coins);
        }
    }
}
