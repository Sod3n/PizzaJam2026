using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class HelperPetDefinition
{
    public static Entity Create(Context ctx, Vector2 position, int helperType, Entity followTarget)
    {
        var entity = Create(ctx, position);

        ref var component = ref ctx.GetComponent<HelperPetComponent>(entity);
        component.HelperType = helperType;
        component.FollowTarget = followTarget;
        component.AssignedTo = followTarget;
        component.State = followTarget == Entity.Null ? PetState.Idle : PetState.Assigned;
        component.IdleSpawnX = (int)position.X;
        component.IdleSpawnY = (int)position.Y;

        var random = new DeterministicRandom((uint)entity.Id + 4000);
        ctx.AddComponent(entity, NameComponent.RandomPet(ref random));

        ref var navAgent = ref ctx.GetComponent<NavigationAgent2D>(entity);
        navAgent.AvoidanceMask = 0;

        return entity;
    }

    public static Entity CreateIdle(Context ctx, Vector2 position, int helperType)
    {
        var entity = Create(ctx, position);

        ref var component = ref ctx.GetComponent<HelperPetComponent>(entity);
        component.HelperType = helperType;
        component.FollowTarget = Entity.Null;
        component.AssignedTo = Entity.Null;
        component.State = PetState.Idle;
        component.IdleSpawnX = (int)position.X;
        component.IdleSpawnY = (int)position.Y;

        var random = new DeterministicRandom((uint)entity.Id + 4000);
        ctx.AddComponent(entity, NameComponent.RandomPet(ref random));

        ref var navAgent = ref ctx.GetComponent<NavigationAgent2D>(entity);
        navAgent.AvoidanceMask = 0;

        return entity;
    }
}
