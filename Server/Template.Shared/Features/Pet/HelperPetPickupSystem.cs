using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;

namespace Template.Shared.Systems;

// Pet pickup is a strategic action (cat distribution = late-game build identity).
//
// Match preconditions:
//   target has HelperPetComponent
//   player is NOT a helper-player (helper-players cannot carry pets)
//
// On match: drops any previously-carried pet to idle, picks up the clicked pet into
// playerState.CarriedEntity. Click on the same pet you're carrying drops it at its idle
// spawn position. Always claims via Success.
public class HelperPetPickupSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            var petEntity = state.GetComponent<InteractRequestComponent>(playerEntity).Target;
            if (!state.TryResolve<HelperPetArchetype>(petEntity, out var petRef)) continue;

            HandlePetInteraction(state, playerEntity, petRef);
        }
    }

    private static void HandlePetInteraction(EntityWorld state, Entity playerEntity, HelperPetRef petRef)
    {
        var ctx = state.Ctx(playerEntity);
        var carried = state.GetComponent<PlayerStateComponent>(playerEntity).CarriedEntity;

        if (carried == petRef.Entity)
        {
            DropCarriedPet(ctx, playerEntity, petRef);
            return;
        }

        if (carried != Entity.Null)
            DropPreviouslyCarried(state, playerEntity, carried);

        PickupPet(ctx, playerEntity, petRef);
    }

    private static void DropCarriedPet(Context ctx, Entity playerEntity, HelperPetRef petRef)
    {
        DropPetToIdle(petRef);
        ctx.State.GetComponent<PlayerStateComponent>(playerEntity).CarriedEntity = Entity.Null;
        ILogger.Log($"[HelperPetPickupSystem] Player {playerEntity.Id} dropped pet {petRef.Entity.Id} at idle spawn");
        InteractFeedback.Success(ctx, playerEntity, petRef.Entity);
    }

    private static void DropPreviouslyCarried(EntityWorld state, Entity playerEntity, Entity carried)
    {
        if (state.TryResolve<HelperPetArchetype>(carried, out var prevRef))
            DropPetToIdle(prevRef);
        state.GetComponent<PlayerStateComponent>(playerEntity).CarriedEntity = Entity.Null;
    }

    private static void PickupPet(Context ctx, Entity playerEntity, HelperPetRef petRef)
    {
        ref var pet = ref petRef.HelperPet;
        pet.State = PetState.Carried;
        pet.FollowTarget = playerEntity;
        pet.AssignedTo = Entity.Null;
        ctx.State.GetComponent<PlayerStateComponent>(playerEntity).CarriedEntity = petRef.Entity;
        ILogger.Log($"[HelperPetPickupSystem] Player {playerEntity.Id} picked up pet {petRef.Entity.Id}");
        InteractFeedback.Success(ctx, playerEntity, petRef.Entity);
    }

    private static void DropPetToIdle(HelperPetRef petRef)
    {
        ref var pet = ref petRef.HelperPet;
        pet.State = PetState.Idle;
        pet.FollowTarget = Entity.Null;
        pet.AssignedTo = Entity.Null;
        petRef.Transform2D.Position = new Vector2((Float)pet.IdleSpawnX, (Float)pet.IdleSpawnY);
        petRef.CharacterBody2D.Velocity = Vector2.Zero;
    }
}
