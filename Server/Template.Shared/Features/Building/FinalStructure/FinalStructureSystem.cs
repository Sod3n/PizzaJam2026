using System;
using Deterministic.GameFramework.ECS;
using Template.Shared.Actions;
using Template.Shared.Components;

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
        var ctx = state.Ctx(playerEntity);
        var grEntity = InteractFeedback.GetGlobalResourcesEntity(state);
        if (grEntity == Entity.Null) return;

        var coins = state.GetComponent<GlobalResourcesComponent>(grEntity).Coins;
        if (coins <= 0)
        {
            InteractFeedback.MissingResource(ctx, playerEntity, finalEntity, StateKeys.Coins);
            return;
        }

        var final = state.GetComponent<FinalStructureComponent>(finalEntity);
        if (final.CurrentCoins >= final.Threshold) return;

        int deposit = Math.Min(1, coins);
        deposit = Math.Min(deposit, final.Threshold - final.CurrentCoins);

        state.GetComponent<GlobalResourcesComponent>(grEntity).Coins -= deposit;
        state.GetComponent<FinalStructureComponent>(finalEntity).CurrentCoins += deposit;
        InteractFeedback.Success(ctx, playerEntity, finalEntity);
    }
}
