using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class RoleSignDefinition
{
    public static Entity Create(Context ctx, Vector2 position, Entity houseId, int initialRole)
    {
        var entity = Create(ctx, position);
        ref var comp = ref ctx.GetComponent<RoleSignComponent>(entity);
        comp.HouseId = houseId;
        comp.Role = initialRole;
        return entity;
    }
}
