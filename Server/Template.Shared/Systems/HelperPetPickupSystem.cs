using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;

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

            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var petEntity = req.Target;
            if (!state.HasComponent<HelperPetComponent>(petEntity)) continue;

            HandlePetInteraction(state, playerEntity, petEntity);
        }
    }

    private static void HandlePetInteraction(EntityWorld state, Entity playerEntity, Entity petEntity)
    {
        var ctx = new Context(state, playerEntity, null!);

        Entity carried;
        {
            var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
            carried = ps.CarriedEntity;
        }

        if (carried == petEntity)
        {
            {
                ref var pet = ref state.GetComponent<HelperPetComponent>(petEntity);
                DropPetToIdle(state, petEntity, ref pet);
            }
            {
                ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
                ps.CarriedEntity = Entity.Null;
            }
            ILogger.Log($"[HelperPetPickupSystem] Player {playerEntity.Id} dropped pet {petEntity.Id} at idle spawn");
            InteractFeedback.Success(ctx, playerEntity, petEntity);
            return;
        }

        if (carried != Entity.Null)
        {
            if (state.HasComponent<HelperPetComponent>(carried))
            {
                ref var prev = ref state.GetComponent<HelperPetComponent>(carried);
                DropPetToIdle(state, carried, ref prev);
            }
            {
                ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
                ps.CarriedEntity = Entity.Null;
            }
        }

        {
            ref var pet = ref state.GetComponent<HelperPetComponent>(petEntity);
            pet.State = PetState.Carried;
            pet.FollowTarget = playerEntity;
            pet.AssignedTo = Entity.Null;
        }
        {
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.CarriedEntity = petEntity;
        }
        ILogger.Log($"[HelperPetPickupSystem] Player {playerEntity.Id} picked up pet {petEntity.Id}");
        InteractFeedback.Success(ctx, playerEntity, petEntity);
    }

    private static void DropPetToIdle(EntityWorld state, Entity petEntity, ref HelperPetComponent pet)
    {
        pet.State = PetState.Idle;
        pet.FollowTarget = Entity.Null;
        pet.AssignedTo = Entity.Null;
        if (state.HasComponent<Transform2D>(petEntity))
        {
            ref var t = ref state.GetComponent<Transform2D>(petEntity);
            t.Position = new Vector2((Float)pet.IdleSpawnX, (Float)pet.IdleSpawnY);
        }
        if (state.HasComponent<CharacterBody2D>(petEntity))
        {
            ref var body = ref state.GetComponent<CharacterBody2D>(petEntity);
            body.Velocity = Vector2.Zero;
        }
    }
}
