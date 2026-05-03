using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class HelperDefinition
{
    public static Entity Create(Context ctx, Vector2 position, int helperType, Entity ownerPlayer)
    {
        var entity = Create(ctx, position);

        ref var component = ref ctx.GetComponent<HelperComponent>(entity);
        component.Type = helperType;
        component.OwnerPlayer = ownerPlayer;
        component.WantedFoodType = -1;
        component.BagCapacity = helperType switch
        {
            HelperType.Gatherer => 75,
            HelperType.Seller => 500,
            HelperType.Builder => 500,
            HelperType.Milker => 125,
            _ => 0 // Assistant has no bag
        };

        // Name from config
        var info = HelperConfig.GetByType(helperType);
        ctx.AddComponent(entity, new NameComponent { Name = info.Name });

        // Get spawn counts from ECS state for deterministic weight decay
        ref var spawnCounts = ref GetSpawnCounts(ctx);

        // Generate random skin
        var random = new DeterministicRandom((uint)entity.Id + 3000);
        var skinComponent = GameData.GD.SkinsData.GenerateRandomSkin(ref random, ref spawnCounts);
        ctx.AddComponent(entity, skinComponent);

        ref var navAgent = ref ctx.GetComponent<NavigationAgent2D>(entity);
        // Only disable avoidance for assistant (follows closely like cow)
        // Autonomous helpers need avoidance to navigate around obstacles and other entities
        if (helperType == HelperType.Assistant)
            navAgent.AvoidanceMask = 0;

        return entity;
    }

    private static ref SkinSpawnCountsComponent GetSpawnCounts(Context ctx)
    {
        foreach (var e in ctx.State.Filter<SkinSpawnCountsComponent>())
            return ref ctx.State.GetComponent<SkinSpawnCountsComponent>(e);
        throw new System.InvalidOperationException("SkinSpawnCountsComponent entity not found");
    }
}
