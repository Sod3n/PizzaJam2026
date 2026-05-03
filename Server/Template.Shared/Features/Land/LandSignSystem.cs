using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Cycles a land sign's selected building type for the linked plot, recomputing the price
// threshold. Locked once the plot has any deposited coins or its position is fixed by the
// star grid (PlayerHouse, SellPoint, FinalStructure). Helper-players cannot interact —
// strategic choice.
public class LandSignSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var signEntity = req.Target;
            if (!state.HasComponent<LandSignComponent>(signEntity)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            CycleLandSign(state, playerEntity, signEntity);
        }
    }

    private static void CycleLandSign(EntityWorld state, Entity playerEntity, Entity signEntity)
    {
        Entity landId;
        LandType selectedType;
        {
            ref var sign = ref state.GetComponent<LandSignComponent>(signEntity);
            landId = sign.LandId;
            selectedType = sign.SelectedType;
        }
        if (!state.HasComponent<LandComponent>(landId)) return;

        int arm, ring, currentCoins;
        {
            ref var land = ref state.GetComponent<LandComponent>(landId);
            arm = land.Arm;
            ring = land.Ring;
            currentCoins = land.CurrentCoins;
        }

        if (StarGrid.GetFixedType(arm, ring).HasValue) return;
        if (currentCoins > 0) return;

        int ringDist = System.Math.Abs(arm) + System.Math.Abs(ring);
        var pool = StarGrid.GetCycleableTypesForRing(state, ringDist, arm, ring);
        if (pool.Length == 0) return;

        int idx = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == selectedType) { idx = i; break; }
        }
        var next = pool[(idx + 1) % pool.Length];

        {
            ref var sign = ref state.GetComponent<LandSignComponent>(signEntity);
            sign.SelectedType = next;
        }
        {
            ref var land = ref state.GetComponent<LandComponent>(landId);
            land.Type = next;
            int pm = StarGrid.GetPriceMultiplier(next);
            int gridDist = System.Math.Max(1, ringDist);
            land.Threshold = pm < 0
                ? gridDist * StarGrid.GetEraMultiplier(gridDist) * Balance.Build.BasePriceMultiplier / 4
                : gridDist * StarGrid.GetEraMultiplier(gridDist) * pm * Balance.Build.BasePriceMultiplier;
        }

        state.AddComponent(landId, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });

        ILogger.Log($"[LandSignSystem] Land sign {signEntity.Id} cycled to type {next} (ring {ringDist})");

        var ctx = state.Ctx(playerEntity);
        InteractFeedback.Success(ctx, playerEntity, signEntity);
    }
}
