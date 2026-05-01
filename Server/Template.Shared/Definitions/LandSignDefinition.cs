using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class LandSignDefinition
{
    public static Entity Create(Context ctx, Vector2 position, Entity landId)
    {
        var entity = Create(ctx, position);
        ref var comp = ref ctx.GetComponent<LandSignComponent>(entity);
        comp.LandId = landId;
        if (landId != Entity.Null && ctx.State.HasComponent<LandComponent>(landId))
            comp.SelectedType = ctx.State.GetComponent<LandComponent>(landId).Type;
        else
            comp.SelectedType = LandType.House;
        return entity;
    }
}
