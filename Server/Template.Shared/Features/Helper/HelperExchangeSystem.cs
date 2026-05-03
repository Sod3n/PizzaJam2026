using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Resource exchange between main player and a helper (give-to-helper or pickup-from-helper).
//
// Match preconditions (mutually exclusive with HelperDropSystem and HelperPickupSystem):
//   target has HelperComponent
//   playerState.FollowingHelper != target  (HelperDropSystem already handled that)
//   the helper is in a state where exchange would actually do work:
//     - WaitingForPickup (any type)            → pickup helper's bag into global resources
//     - Idle Seller                            → load milk from global into helper's bag
//     - Idle Builder                           → load coins from global into helper's bag
//     - Idle Milker with WantedFoodType >= 0   → load food + prerequisite milk into helper's bag
//
// If none of those apply, this system does NOT match — HelperPickupSystem handles those cases.
//
// On a successful exchange, claims via Success. On no-exchange-possible (e.g. Seller idle but
// global has no milk to load), the system simply doesn't match and HelperPickupSystem gets a
// chance — matching the original "exchange-then-fallthrough-to-pickup" semantics.
public class HelperExchangeSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;

            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var helperEntity = req.Target;
            if (!state.HasComponent<HelperComponent>(helperEntity)) continue;

            var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
            if (ps.FollowingHelper == helperEntity) continue;

            TryExchange(state, playerEntity, helperEntity);
        }
    }

    private static void TryExchange(EntityWorld state, Entity playerEntity, Entity helperEntity)
    {
        var ctx = state.Ctx(playerEntity);

        var grEntity = InteractFeedback.GetGlobalResourcesEntity(state);
        if (grEntity == Entity.Null) return;

        bool didExchange = false;

        var helperSnapshot = state.GetComponent<HelperComponent>(helperEntity);
        if (helperSnapshot.State == HelperState.WaitingForPickup)
        {
            ref var helper = ref state.GetComponent<HelperComponent>(helperEntity);
            ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            didExchange = PickupFromHelper(ctx, helperEntity, ref helper, ref globalRes);
        }
        else if (helperSnapshot.Type == HelperType.Seller && helperSnapshot.State == HelperState.Idle)
        {
            ref var helper = ref state.GetComponent<HelperComponent>(helperEntity);
            ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            int transferred = 0;
            int capacity = helper.BagCapacity - helper.GetBagTotal();
            while (transferred < capacity && globalRes.Milk > 0) { globalRes.Milk--; helper.BagMilk++; transferred++; }
            if (transferred > 0)
            {
                ILogger.Log($"[HelperExchangeSystem] Loaded {transferred} milk into Seller helper {helperEntity.Id}");
                didExchange = true;
            }
        }
        else if (helperSnapshot.Type == HelperType.Builder && helperSnapshot.State == HelperState.Idle)
        {
            ref var helper = ref state.GetComponent<HelperComponent>(helperEntity);
            ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            int needed = helper.BagCapacity - helper.BagCoins;
            int toGive = System.Math.Max(0, System.Math.Min(needed, globalRes.Coins));
            if (toGive > 0)
            {
                globalRes.Coins -= toGive;
                helper.BagCoins += toGive;
                ILogger.Log($"[HelperExchangeSystem] Gave {toGive} coins to Builder helper {helperEntity.Id}");
                didExchange = true;
            }
        }
        else if (helperSnapshot.Type == HelperType.Milker && helperSnapshot.State == HelperState.Idle && helperSnapshot.WantedFoodType >= 0)
        {
            ref var helper = ref state.GetComponent<HelperComponent>(helperEntity);
            ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            int foodType = helper.WantedFoodType;
            int capacity = helper.BagCapacity - helper.GetBagTotal();
            int available = globalRes.GetFood(foodType);
            int toGive = System.Math.Max(0, System.Math.Min(capacity, available));
            if (toGive > 0)
            {
                for (int i = 0; i < toGive; i++)
                    globalRes.ConsumeFood(foodType);
                switch (foodType)
                {
                    case FoodType.Grass: helper.BagGrass += toGive; break;
                    case FoodType.Carrot: helper.BagCarrot += toGive; break;
                    case FoodType.Apple: helper.BagApple += toGive; break;
                    case FoodType.Mushroom: helper.BagMushroom += toGive; break;
                }

                int prereq = FoodType.PrerequisiteProduct(foodType);
                if (prereq >= 0)
                {
                    int prereqNeeded = System.Math.Max(1, toGive / 4);
                    int prereqCapacity = helper.BagCapacity - helper.GetBagTotal();
                    int prereqAvailable = globalRes.GetMilkProduct(prereq);
                    int prereqToGive = System.Math.Min(prereqNeeded, System.Math.Min(prereqCapacity, prereqAvailable));
                    for (int i = 0; i < prereqToGive; i++)
                        globalRes.ConsumeMilkProduct(prereq);
                    helper.AddBagMilkProduct(prereq, prereqToGive);
                }

                ILogger.Log($"[HelperExchangeSystem] Gave {toGive} food (type={foodType}) to Milker helper {helperEntity.Id}");
                didExchange = true;
            }
        }

        if (didExchange)
            InteractFeedback.Success(ctx, playerEntity, helperEntity);
    }

    // Copied from InteractActionService.PickupFromHelper. Player picks up resources from a
    // helper that is in WaitingForPickup state. Transfers the helper's bag into global
    // resources and resets helper to Idle.
    private static bool PickupFromHelper(Context ctx, Entity helperEntity, ref HelperComponent helper, ref GlobalResourcesComponent globalRes)
    {
        bool pickedUp = false;
        string gainedKey = "";

        if (helper.GetFoodTotal() > 0)
        {
            gainedKey = helper.BagGrass > 0 ? StateKeys.Grass
                : helper.BagCarrot > 0 ? StateKeys.Carrot
                : helper.BagApple > 0 ? StateKeys.Apple
                : helper.BagMushroom > 0 ? StateKeys.Mushroom : "";

            globalRes.AddFood(FoodType.Grass, helper.BagGrass);
            globalRes.AddFood(FoodType.Carrot, helper.BagCarrot);
            globalRes.AddFood(FoodType.Apple, helper.BagApple);
            globalRes.AddFood(FoodType.Mushroom, helper.BagMushroom);
            helper.BagGrass = 0;
            helper.BagCarrot = 0;
            helper.BagApple = 0;
            helper.BagMushroom = 0;
            pickedUp = true;
        }

        if (helper.GetMilkTotal() > 0)
        {
            if (string.IsNullOrEmpty(gainedKey))
                gainedKey = StateKeys.Milk;

            globalRes.AddMilkProduct(MilkProduct.Milk, helper.BagMilk);
            helper.BagMilk = 0;
            helper.BagCarrotMilkshake = 0;
            helper.BagVitaminMix = 0;
            helper.BagPurplePotion = 0;
            pickedUp = true;
        }

        if (helper.BagCoins > 0)
        {
            if (string.IsNullOrEmpty(gainedKey))
                gainedKey = StateKeys.Coins;

            globalRes.Coins += helper.BagCoins;
            helper.BagCoins = 0;
            pickedUp = true;
        }

        if (pickedUp)
        {
            if (!string.IsNullOrEmpty(gainedKey))
            {
                ctx.State.AddComponent(ctx.Entity, new EnterStateComponent { Key = StateKeys.GainedResource, Param = gainedKey, Age = 0 });
                helper = ref ctx.State.GetComponent<HelperComponent>(helperEntity);
            }
            helper.State = HelperState.Idle;
            ILogger.Log($"[HelperExchangeSystem] Player picked up resources from helper {helperEntity.Id} (type={helper.Type})");
        }

        return pickedUp;
    }
}
