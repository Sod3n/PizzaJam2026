using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Cycles a role sign through helper roles. If a helper-player or helper currently lives in the
// linked house, also resets that occupant's role/state and clears their bag — Assistant is
// skipped in the cycle when there's a real occupant (cycle goes Gatherer → Seller → Builder
// → Milker → Gatherer).
public class RoleSignSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var signEntity = req.Target;
            if (!state.HasComponent<RoleSignComponent>(signEntity)) continue;

            CycleRoleSign(state, playerEntity, signEntity);
        }
    }

    private static void CycleRoleSign(EntityWorld state, Entity playerEntity, Entity signEntity)
    {
        int prev;
        Entity houseId;
        int next;
        {
            ref var sign = ref state.GetComponent<RoleSignComponent>(signEntity);
            prev = sign.Role;
            next = prev switch
            {
                HelperType.Assistant => HelperType.Gatherer,
                HelperType.Gatherer => HelperType.Seller,
                HelperType.Seller => HelperType.Builder,
                HelperType.Builder => HelperType.Milker,
                HelperType.Milker => HelperType.Assistant,
                _ => HelperType.Assistant,
            };
            sign.Role = next;
            houseId = sign.HouseId;
        }

        if (houseId != Entity.Null && state.HasComponent<HouseComponent>(houseId))
        {
            Entity helperId = state.GetComponent<HouseComponent>(houseId).HelperId;
            if (helperId != Entity.Null && state.HasComponent<HelperPlayerComponent>(helperId))
            {
                int hpNext = prev switch
                {
                    HelperType.Gatherer => HelperType.Seller,
                    HelperType.Seller => HelperType.Builder,
                    HelperType.Builder => HelperType.Milker,
                    HelperType.Milker => HelperType.Gatherer,
                    _ => HelperType.Gatherer,
                };
                {
                    ref var sign = ref state.GetComponent<RoleSignComponent>(signEntity);
                    sign.Role = hpNext;
                }
                next = hpNext;

                ref var hp = ref state.GetComponent<HelperPlayerComponent>(helperId);
                hp.Type = hpNext;
                hp.State = HelperState.Idle;
                hp.WantedFoodType = -1;
                hp.ClearBag();
                hp.BagCapacity = Balance.HelperPlayer.BagCapacity;
            }
            else if (helperId != Entity.Null && state.HasComponent<HelperComponent>(helperId))
            {
                ref var helper = ref state.GetComponent<HelperComponent>(helperId);
                helper.Type = next;
                helper.State = HelperState.Idle;
                helper.WantedFoodType = -1;
                helper.TargetEntity = Entity.Null;
                helper.WorkTimer = 0;
                helper.WorkDuration = 0;
                var info = HelperConfig.GetByType(next);
                helper.BagCapacity = info.BaseCapacity;
                helper.BagGrass = 0;
                helper.BagCarrot = 0;
                helper.BagApple = 0;
                helper.BagMushroom = 0;
                helper.BagMilk = 0;
                helper.BagCarrotMilkshake = 0;
                helper.BagVitaminMix = 0;
                helper.BagPurplePotion = 0;
                helper.BagCoins = 0;
            }
        }

        ILogger.Log($"[RoleSignSystem] Role sign {signEntity.Id} cycled to role {next}");

        var ctx = new Context(state, playerEntity, null!);
        InteractFeedback.Success(ctx, playerEntity, signEntity);
    }
}
