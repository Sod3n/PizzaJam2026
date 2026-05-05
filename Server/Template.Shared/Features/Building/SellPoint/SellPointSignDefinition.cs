using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class SellPointSignDefinition
{
    public static Entity Create(Context ctx, Vector2 position, Entity sellPointId, int initialProduct)
    {
        var entity = Create(ctx, position);
        ref var comp = ref ctx.GetComponent<SellPointSignComponent>(entity);
        comp.SellPointId = sellPointId;
        comp.CurrentProduct = initialProduct;
        return entity;
    }
}
