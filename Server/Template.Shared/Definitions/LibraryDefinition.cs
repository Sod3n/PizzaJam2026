using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class LibraryDefinition
{
    static partial void OnEntityCreated(Context ctx, Entity entity, ref LibraryComponent component, Dictionary<string, Entity> childEntities)
    {
        ctx.AddComponent(entity, new BuildingComponent { Type = LandType.Library });
    }
}
