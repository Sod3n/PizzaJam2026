using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class HouseDefinition
{
    static partial void OnEntityCreated(Context ctx, Entity entity, ref HouseComponent component, Dictionary<string, Entity> childEntities)
    {
        // Spawn a food selection sign next to the house
        var housePos = ctx.GetComponent<Transform2D>(entity).Position;
        FoodSignDefinition.Create(ctx, housePos + new Vector2(-2, 0), entity);
    }
}
