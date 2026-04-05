using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class WarehouseSignDefinition
{
    public static Entity Create(Context ctx, Vector2 position, Entity warehouseId)
    {
        var entity = Create(ctx, position);
        ref var comp = ref ctx.GetComponent<WarehouseSignComponent>(entity);
        comp.WarehouseId = warehouseId;
        comp.Enabled = 0;
        return entity;
    }
}
