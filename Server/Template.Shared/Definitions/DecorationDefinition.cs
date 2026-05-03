using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class DecorationDefinition
{
    static partial void OnEntityCreated(Context ctx, Entity entity, ref DecorationComponent component, Dictionary<string, Entity> childEntities)
    {
        ctx.AddComponent(entity, new BuildingComponent { Type = LandType.Decoration });
    }
}
