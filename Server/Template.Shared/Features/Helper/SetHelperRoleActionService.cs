using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Actions;

public class SetHelperRoleActionService : ActionService<SetHelperRoleAction, PlayerEntity>
{
    protected override void ExecuteProcess(SetHelperRoleAction action, ref PlayerEntity playerComp, Context ctx)
    {
        if (playerComp.UserId != action.UserId) return;
        var playerEntity = ctx.Entity;
        var helper = new Entity(action.HelperEntityId);

        if (ctx.State.HasComponent<HelperComponent>(helper))
        {
            ref var h = ref ctx.State.GetComponent<HelperComponent>(helper);
            int prev = h.Type;
            int next = prev switch
            {
                HelperType.Gatherer => HelperType.Seller,
                HelperType.Seller => HelperType.Builder,
                HelperType.Builder => HelperType.Milker,
                HelperType.Milker => HelperType.Gatherer,
                _ => HelperType.Gatherer,
            };

            h.Type = next;
            h.WantedFoodType = -1;
            h.TargetEntity = Entity.Null;
            h.WorkTimer = 0;
            h.WorkDuration = 0;
            var info = HelperConfig.GetByType(next);
            h.BagCapacity = info.BaseCapacity;
            // Keep the bag intact and route through the normal pickup flow: helper
            // waits at its current spot following the player, who picks up via
            // HelperExchangeSystem.PickupFromHelper just like any other return.
            h.State = h.GetBagTotal() > 0 ? HelperState.WaitingForPickup : HelperState.Idle;

            ILogger.Log($"[SetHelperRole] Player {playerEntity.Id} cycled helper {helper.Id} role to {next}");
        }
        else if (helper == playerEntity && ctx.State.HasComponent<HelperPlayerComponent>(playerEntity))
        {
            ref var hp = ref ctx.State.GetComponent<HelperPlayerComponent>(playerEntity);
            int prev = hp.Type;
            int next = prev switch
            {
                HelperType.Gatherer => HelperType.Seller,
                HelperType.Seller => HelperType.Builder,
                HelperType.Builder => HelperType.Milker,
                HelperType.Milker => HelperType.Gatherer,
                _ => HelperType.Gatherer,
            };

            hp.Type = next;
            hp.State = HelperState.Idle;
            hp.WantedFoodType = -1;
            hp.ClearBag();
            hp.BagCapacity = Balance.HelperPlayer.BagCapacity;

            ILogger.Log($"[SetHelperRole] HelperPlayer {playerEntity.Id} self-cycled role to {next}");
        }
    }
}
