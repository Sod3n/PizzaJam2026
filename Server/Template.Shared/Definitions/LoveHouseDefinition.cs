using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class LoveHouseDefinition
{
    static partial void OnEntityCreated(Context ctx, Entity entity, ref LoveHouseComponent component, Dictionary<string, Entity> childEntities)
    {
        var staticBody = StaticBody2D.Default;
        ctx.AddComponent(entity, staticBody);
        ctx.AddComponent(entity, CollisionShape2D.CreateRectangle(new Vector2(2, 2)));
    }
}
