using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class PlayerDefinition
{
    public static Entity Create(Context ctx, System.Guid userId, Vector2 position, Float angle)
    {
        var entity = Create(ctx, position);

        ref var player = ref ctx.GetComponent<PlayerEntity>(entity);
        player.UserId = userId;
        player.Id = entity;
        player.Name = new FixedString32("Player");

        ref var transform = ref ctx.GetComponent<Transform2D>(entity);
        transform.Rotation = angle;

        ref var body = ref ctx.GetComponent<CharacterBody2D>(entity);
        body.Velocity = Vector2.Zero;
        body.UpDirection = new Vector2(0, -1);

        // Add a NameComponent so UI lookups (e.g. breed result "Pet of …") resolve the player name
        ctx.AddComponent(entity, new NameComponent { Name = new FixedString32("Andrey") });

        // Set return position now that we have the final position
        ref var ps = ref ctx.GetComponent<PlayerStateComponent>(entity);
        ps.ReturnPosition = position;

        return entity;
    }

    static partial void OnEntityCreated(Context ctx, Entity entity, ref PlayerEntity component, Dictionary<string, Entity> childEntities)
    {
        // Get spawn counts from ECS state for deterministic weight decay
        ref var spawnCounts = ref GetSpawnCounts(ctx);

        // Generate random skin
        var random = new DeterministicRandom((uint)entity.Id + 1000);
        var skinComponent = GameData.GD.SkinsData.GenerateRandomSkin(ref random, ref spawnCounts);
        ctx.AddComponent(entity, skinComponent);
    }

    private static ref SkinSpawnCountsComponent GetSpawnCounts(Context ctx)
    {
        foreach (var e in ctx.State.Filter<SkinSpawnCountsComponent>())
            return ref ctx.State.GetComponent<SkinSpawnCountsComponent>(e);
        throw new System.InvalidOperationException("SkinSpawnCountsComponent entity not found");
    }
}
