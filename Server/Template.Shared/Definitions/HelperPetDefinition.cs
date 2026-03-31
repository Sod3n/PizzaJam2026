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

        ref var navAgent = ref ctx.GetComponent<NavigationAgent2D>(entity);
        navAgent.AvoidanceMask = 0;

        return entity;
    }
}
