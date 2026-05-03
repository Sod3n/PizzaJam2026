using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Click on the final structure → deposit one coin per click. Caps at the structure's threshold.
public class FinalStructureSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var finalEntity = req.Target;
            if (!state.HasComponent<FinalStructureComponent>(finalEntity)) continue;

            HandleDeposit(state, playerEntity, finalEntity);
        }
    }

    private static void HandleDeposit(EntityWorld state, Entity playerEntity, Entity finalEntity)
    {
        var ctx = new Context(state, playerEntity, null!);
        var grEntity = InteractFeedback.GetGlobalResourcesEntity(state);
        if (grEntity == Entity.Null) return;

        int coins;
        {
            var globalRes = state.GetComponent<GlobalResourcesComponent>(grEntity);
            coins = globalRes.Coins;
        }
        if (coins <= 0)
        {
            InteractFeedback.MissingResource(ctx, playerEntity, finalEntity, StateKeys.Coins);
            return;
        }

        int currentCoins, threshold;
        {
            var final = state.GetComponent<FinalStructureComponent>(finalEntity);
            currentCoins = final.CurrentCoins;
            threshold = final.Threshold;
        }
        if (currentCoins >= threshold) return;

        int deposit = System.Math.Min(1, coins);
        deposit = System.Math.Min(deposit, threshold - currentCoins);
        {
            ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            globalRes.Coins -= deposit;
        }
        {
            ref var final = ref state.GetComponent<FinalStructureComponent>(finalEntity);
            final.CurrentCoins += deposit;
        }
        InteractFeedback.Success(ctx, playerEntity, finalEntity);
    }
}
