using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class SellPointDefinition
{
    static partial void OnEntityCreated(Context ctx, Entity entity, ref SellPointComponent component, Dictionary<string, Entity> childEntities)
    {
        var pos = ctx.GetComponent<Transform2D>(entity).Position;

        int product = SellProduct.Milk;
        foreach (var grEntity in ctx.State.Filter<GlobalResourcesComponent>())
        {
            var gr = ctx.State.GetComponent<GlobalResourcesComponent>(grEntity);
            product = (gr.DayCounter % 3) == 2 ? SellProduct.Cow : SellProduct.Milk;
            break;
        }

        SellPointSignDefinition.Create(ctx, pos + new Vector2(-2, 0), entity, product);

        ctx.AddComponent(entity, new BuildingComponent { Type = LandType.SellPoint });
    }
}
