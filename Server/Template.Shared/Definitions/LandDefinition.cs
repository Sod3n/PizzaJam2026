using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class LandDefinition
{
    public static Entity Create(Context ctx, Vector2 position, int threshold)
    {
        var entity = Create(ctx, position);
        ref var land = ref ctx.GetComponent<LandComponent>(entity);
        land.Threshold = threshold;
        return entity;
    }
}
