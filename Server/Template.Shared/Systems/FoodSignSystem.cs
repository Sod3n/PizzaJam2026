using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;

namespace Template.Shared.Systems;

// Cycles a food sign's selected food (Grass → Carrot → Apple → Mushroom → Grass) and
// propagates the change to the linked house's cow (cow.SelectedFood is source of truth).
public class FoodSignSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var signEntity = req.Target;
            if (!state.HasComponent<FoodSignComponent>(signEntity)) continue;

            CycleFoodSign(state, playerEntity, signEntity);
        }
    }

    private static void CycleFoodSign(EntityWorld state, Entity playerEntity, Entity signEntity)
    {
        int newFood;
        Entity houseId;
        {
            ref var sign = ref state.GetComponent<FoodSignComponent>(signEntity);
            sign.SelectedFood = (sign.SelectedFood + 1) % 4;
            newFood = sign.SelectedFood;
            houseId = sign.HouseId;
        }

        if (houseId != Entity.Null && state.HasComponent<HouseComponent>(houseId))
        {
            Entity cowId;
            {
                ref var house = ref state.GetComponent<HouseComponent>(houseId);
                house.SelectedFood = newFood;
                cowId = house.CowId;
            }
            if (cowId != Entity.Null && state.HasComponent<CowComponent>(cowId))
            {
                ref var cow = ref state.GetComponent<CowComponent>(cowId);
                cow.SelectedFood = newFood;
            }
        }

        ILogger.Log($"[FoodSignSystem] Food sign {signEntity.Id} cycled to food type {newFood}");

        var ctx = new Context(state, playerEntity, null!);
        InteractFeedback.Success(ctx, playerEntity, signEntity);
    }
}
