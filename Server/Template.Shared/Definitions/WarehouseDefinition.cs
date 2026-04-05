using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class WarehouseDefinition
{
    static partial void OnEntityCreated(Context ctx, Entity entity, ref WarehouseComponent component, Dictionary<string, Entity> childEntities)
    {
        // Spawn an enable/disable sign next to the warehouse
        var warehousePos = ctx.GetComponent<Transform2D>(entity).Position;
        WarehouseSignDefinition.Create(ctx, warehousePos + new Vector2(-2, 0), entity);
    }
}
