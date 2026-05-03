using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Deterministic.GameFramework.Utils.Logging;

namespace Template.Shared.Systems;

// Handles state-completion for the three "single-cow" player interactions:
//   Milking — finish the milk session, unhide cow & player.
//   Taming  — detach cow from its house, attach it to the player's follow chain.
//   Assign  — move the chain head into a house; old occupant joins the chain tail.
// Breeding (regular crossbreed and love-house breed) lives in CowBreedingSystem.
public class CowInteractionSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        // Only process Age==0 (the tick Complete() fired). Age>0 means AnimationsSystem
        // already aged it and will remove it next tick — processing again would double-fire.
        foreach (var playerEntity in state.Filter<ExitStateComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;
            var exit = state.GetComponent<ExitStateComponent>(playerEntity);
            if (exit.Age > 0) continue;

            if (exit.Key == StateKeys.Milking) HandleMilkingComplete(state, playerEntity);
            else if (exit.Key == StateKeys.Taming) HandleTamingComplete(state, playerEntity);
            else if (exit.Key == StateKeys.Assign) HandleAssignComplete(state, playerEntity);
        }
    }

    private static void HandleMilkingComplete(EntityWorld state, Entity playerEntity)
    {
        ILogger.Log($"[CowInteractionSystem] Milking complete for player {playerEntity.Id}");

        state.UnhideEntity(playerEntity);

        Entity cowEntity = state.GetComponent<PlayerStateComponent>(playerEntity).InteractionTarget;

        if (state.HasComponent<CowComponent>(cowEntity))
        {
            state.GetComponent<CowComponent>(cowEntity).EndMilking();
            state.UnhideEntity(cowEntity);
        }

        CowSystemHelpers.ClearInteractionAndIdle(state, playerEntity);
    }

    private static void HandleTamingComplete(EntityWorld state, Entity playerEntity)
    {
        ILogger.Log($"[CowInteractionSystem] Taming complete for player {playerEntity.Id}");

        Entity cowEntity = state.GetComponent<PlayerStateComponent>(playerEntity).InteractionTarget;

        if (state.HasComponent<CowComponent>(cowEntity))
        {
            CowSystemHelpers.DetachCowFromHouse(state, cowEntity, playerEntity);
            CowSystemHelpers.AddCowToFollowChain(state, playerEntity, cowEntity);
        }

        CowSystemHelpers.ClearInteractionAndIdle(state, playerEntity);

        ILogger.Log($"[CowInteractionSystem] Player {playerEntity.Id} now has cow {cowEntity.Id} in follow chain.");
    }

    private static void HandleAssignComplete(EntityWorld state, Entity playerEntity)
    {
        ILogger.Log($"[CowInteractionSystem] Assign complete for player {playerEntity.Id}");

        Entity houseEntity;
        Entity cowEntity;
        {
            var ps0 = state.GetComponent<PlayerStateComponent>(playerEntity);
            houseEntity = ps0.InteractionTarget;
            cowEntity = ps0.FollowingCow;
        }

        Entity oldCow = Entity.Null;

        if (state.TryResolve<HouseArchetype>(houseEntity, out var house) && state.HasComponent<CowComponent>(cowEntity))
        {
            Entity nextCow = CowSystemHelpers.FindNextCowInChain(state, cowEntity);

            // First cow leaves the chain and is bound to the house
            state.GetComponent<CowComponent>(cowEntity).SettleIntoHouse(houseEntity);
            oldCow = house.AssignCow(cowEntity);

            // agent-helpers-in-house: replace any role sign with a food sign reflecting cow.SelectedFood.
            // Resizes possible — no held refs across this call.
            InteractActionService.EnsureFoodSignForHouse(state.Ctx(playerEntity), houseEntity, cowEntity);

            // Promote next-in-chain to head (or null head out if chain is empty now)
            if (nextCow != Entity.Null)
                state.GetComponent<CowComponent>(nextCow).FollowTarget = playerEntity;
            state.GetComponent<PlayerStateComponent>(playerEntity).FollowingCow = nextCow;

            // Displaced cow joins the end of the chain
            if (oldCow != Entity.Null)
            {
                state.GetComponent<CowComponent>(oldCow).HouseId = Entity.Null;
                CowSystemHelpers.AddCowToFollowChain(state, playerEntity, oldCow);
            }
        }

        CowSystemHelpers.ClearInteractionAndIdle(state, playerEntity);

        ILogger.Log($"[CowInteractionSystem] Player {playerEntity.Id} assigned cow to house. Old cow following: {oldCow != Entity.Null}");
    }
}
