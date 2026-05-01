using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class LandPriceSignDefinition
{
    public static Entity Create(Context ctx, Vector2 position, Entity landId)
    {
        var entity = Create(ctx, position);
        ref var comp = ref ctx.GetComponent<LandPriceSignComponent>(entity);
        comp.LandId = landId;
        return entity;
    }
}
