using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Actions;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class LandDefinition
{
    private static readonly Float SignClearRadius = (Float)1.6f;

    public static Entity Create(Context ctx, Vector2 position, int threshold, LandType type = LandType.House, int arm = 0, int ring = 0, int locked = 0)
    {
        var entity = Create(ctx, position);
        ref var land = ref ctx.GetComponent<LandComponent>(entity);
        land.Threshold = threshold;
        land.Type = type;
        land.Arm = arm;
        land.Ring = ring;
        land.Locked = locked;

        // Coin-progress sign — left of the plot. Clear props in its footprint.
        var pricePos = position + new Vector2(-2, 0);
        InteractionLogic.DestroyNearbyProps(ctx.State, pricePos, SignClearRadius);
        LandPriceSignDefinition.Create(ctx, pricePos, entity);

        // Type sign — right of the plot. Always spawned so the player can see what's
        // about to be built. Cycling is gated server-side: fixed positions (PlayerHouse,
        // SellPoint, FinalStructure) reject the cycle in HandleLandSignInteraction.
        var typePos = position + new Vector2(2, 0);
        InteractionLogic.DestroyNearbyProps(ctx.State, typePos, SignClearRadius);
        LandSignDefinition.Create(ctx, typePos, entity);

        return entity;
    }

    public static void DeleteSignsForLand(EntityWorld state, Entity landEntity)
    {
        var toDelete = new System.Collections.Generic.List<Entity>();
        foreach (var e in state.Filter<LandPriceSignComponent>())
        {
            if (state.GetComponent<LandPriceSignComponent>(e).LandId == landEntity)
                toDelete.Add(e);
        }
        foreach (var e in state.Filter<LandSignComponent>())
        {
            if (state.GetComponent<LandSignComponent>(e).LandId == landEntity)
                toDelete.Add(e);
        }
        foreach (var e in toDelete)
            state.DeleteEntity(e);
    }
}
