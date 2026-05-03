using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class FoodFarmDefinition
{
    public static Entity Create(Context ctx, Vector2 position, int foodType)
    {
        var entity = Create(ctx, position);
        ref var comp = ref ctx.GetComponent<FoodFarmComponent>(entity);
        comp.FoodType = foodType;
        return entity;
    }
}
