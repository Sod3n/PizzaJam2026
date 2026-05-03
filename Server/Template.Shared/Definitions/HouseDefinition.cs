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
        // Sign is spawned when the house gets a cow or helper occupant.
        ctx.AddComponent(entity, new BuildingComponent { Type = LandType.House });
    }
}
