using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Definitions;

public static partial class HelperPlayerDefinition
{
    public static Entity Create(Context ctx, System.Guid userId, Vector2 position, Float angle)
    {
        var entity = Create(ctx, position);

        ref var player = ref ctx.GetComponent<PlayerEntity>(entity);
        player.UserId = userId;
        player.Id = entity;
        player.Name = new FixedString32("HelperPlayer");

        ref var transform = ref ctx.GetComponent<Transform2D>(entity);
        transform.Rotation = angle;

        ref var body = ref ctx.GetComponent<CharacterBody2D>(entity);
        body.Velocity = Vector2.Zero;
        body.UpDirection = new Vector2(0, -1);

        ctx.AddComponent(entity, new NameComponent { Name = new FixedString32("Maid") });

        ref var ps = ref ctx.GetComponent<PlayerStateComponent>(entity);
        ps.ReturnPosition = position;
        ps.ClickMultiplier = Balance.HelperPlayer.ClickMultiplier;

        ref var hp = ref ctx.GetComponent<HelperPlayerComponent>(entity);
        hp.Type = HelperType.Gatherer;
        hp.State = HelperState.Idle;
        hp.BagCapacity = Balance.HelperPlayer.BagCapacity;
        hp.WantedFoodType = -1;

        return entity;
    }

    static partial void OnEntityCreated(Context ctx, Entity entity, ref PlayerEntity component, Dictionary<string, Entity> childEntities)
    {
        ref var spawnCounts = ref GetSpawnCounts(ctx);

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
