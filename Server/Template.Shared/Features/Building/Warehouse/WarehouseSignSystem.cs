using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;

namespace Template.Shared.Systems;

// Toggles a warehouse sign on/off and propagates Enabled to the linked WarehouseComponent.
public class WarehouseSignSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var signEntity = req.Target;
            if (!state.HasComponent<WarehouseSignComponent>(signEntity)) continue;

            ToggleWarehouseSign(state, playerEntity, signEntity);
        }
    }

    private static void ToggleWarehouseSign(EntityWorld state, Entity playerEntity, Entity signEntity)
    {
        int newEnabled;
        Entity warehouseId;
        {
            ref var sign = ref state.GetComponent<WarehouseSignComponent>(signEntity);
            sign.Enabled = sign.Enabled == 0 ? 1 : 0;
            newEnabled = sign.Enabled;
            warehouseId = sign.WarehouseId;
        }

        if (state.HasComponent<WarehouseComponent>(warehouseId))
        {
            ref var warehouse = ref state.GetComponent<WarehouseComponent>(warehouseId);
            warehouse.Enabled = newEnabled;
        }

        ILogger.Log($"[WarehouseSignSystem] Warehouse sign {signEntity.Id} toggled to {(newEnabled == 1 ? "ENABLED" : "DISABLED")}");

        var ctx = state.Ctx(playerEntity);
        InteractFeedback.Success(ctx, playerEntity, signEntity);
    }
}
