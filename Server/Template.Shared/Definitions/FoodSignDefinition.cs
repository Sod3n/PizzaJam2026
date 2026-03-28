using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class FoodSignDefinition
{
    public static Entity Create(Context ctx, Vector2 position, Entity houseId)
    {
        var entity = Create(ctx, position);
        ref var comp = ref ctx.GetComponent<FoodSignComponent>(entity);
        comp.HouseId = houseId;
        comp.SelectedFood = FoodType.Grass;
        return entity;
    }
}
