using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class SmithyDefinition
{
    static partial void OnEntityCreated(Context ctx, Entity entity, ref SmithyComponent component, Dictionary<string, Entity> childEntities)
    {
        ctx.AddComponent(entity, new BuildingComponent { Type = LandType.Smithy });
    }
}
