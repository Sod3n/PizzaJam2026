using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;

namespace Template.Shared.Systems;

// When an enabled warehouse exists, helpers skip the "return to player + wait for pickup"
// loop and instead deposit at the warehouse. Idle sellers/builders/milkers also auto-load
// resources from global storage via the warehouse instead of waiting for the player.
[UpdateOrder(2)]
public class HelperWarehouseSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var helperRef in state.Filter<HelperArchetype>())
        {
            if (helperRef.Helper.Type == HelperType.Assistant) continue;
            if (helperRef.Helper.SuppressTickUpdate) continue;
            TryWarehouseAutoDeposit(state, helperRef);
        }
    }

    private static Entity FindEnabledWarehouse(EntityWorld state)
    {
        foreach (var entity in state.Filter<WarehouseComponent>())
        {
            var wh = state.GetComponent<WarehouseComponent>(entity);
            if (wh.Enabled == 1 && state.HasComponent<Transform2D>(entity))
                return entity;
        }
        return Entity.Null;
    }

    private void TryWarehouseAutoDeposit(EntityWorld state, HelperRef helperRef)
    {
        var warehouse = FindEnabledWarehouse(state);
        if (warehouse == Entity.Null) return;

        var warehousePos = state.GetComponent<Transform2D>(warehouse).Position;

        switch (helperRef.Helper.State)
        {
            case HelperState.WaitingForPickup: WarehouseRouteFromPickup(state, helperRef, warehouse, warehousePos); break;
            case HelperState.Returning: WarehouseRouteFromReturning(state, helperRef, warehouse, warehousePos); break;
            case HelperState.Idle: WarehouseAutoLoadIdle(state, helperRef); break;
        }
    }

    private void WarehouseRouteFromPickup(EntityWorld state, HelperRef helperRef, Entity warehouse, Vector2 warehousePos)
    {
        if (!HelperUtilities.NavigateToward(state, helperRef.Entity, warehousePos, HelperSystem.TargetReachedDistSq)) return;
        WarehouseDeposit(state, ref helperRef.Helper);
        state.AddComponent(warehouse, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
        helperRef.Helper.State = helperRef.Helper.Type == HelperType.Gatherer ? HelperState.SeekingTarget : HelperState.Idle;
    }

    private void WarehouseRouteFromReturning(EntityWorld state, HelperRef helperRef, Entity warehouse, Vector2 warehousePos)
    {
        if (!helperRef.Helper.HasAnyResources()) return;
        if (!HelperUtilities.NavigateToward(state, helperRef.Entity, warehousePos, HelperSystem.TargetReachedDistSq)) return;
        WarehouseDeposit(state, ref helperRef.Helper);
        state.AddComponent(warehouse, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
        helperRef.Helper.State = helperRef.Helper.Type == HelperType.Gatherer ? HelperState.SeekingTarget : HelperState.Idle;
    }

    private void WarehouseAutoLoadIdle(EntityWorld state, HelperRef helperRef)
    {
        switch (helperRef.Helper.Type)
        {
            case HelperType.Seller: WarehouseAutoLoadSeller(state, ref helperRef.Helper); break;
            case HelperType.Builder: WarehouseAutoLoadBuilder(state, ref helperRef.Helper); break;
            case HelperType.Milker:
                if (helperRef.Helper.WantedFoodType >= 0 && helperRef.Helper.GetFoodTotal() == 0)
                    WarehouseAutoLoadMilker(state, ref helperRef.Helper);
                break;
        }
    }

    private static void WarehouseAutoLoadSeller(EntityWorld state, ref HelperComponent helper)
    {
        foreach (var grEntity in state.Filter<GlobalResourcesComponent>())
        {
            ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            int transferred = 0;
            int capacity = helper.BagCapacity - helper.GetBagTotal();

            while (transferred < capacity && gr.Milk > 0) { gr.Milk--; helper.BagMilk++; transferred++; }

            if (transferred > 0)
                helper.State = HelperState.SeekingTarget;
            return;
        }
    }

    private static void WarehouseAutoLoadBuilder(EntityWorld state, ref HelperComponent helper)
    {
        foreach (var grEntity in state.Filter<GlobalResourcesComponent>())
        {
            ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            int needed = helper.BagCapacity - helper.BagCoins;
            int toGive = System.Math.Min(needed, gr.Coins);
            if (toGive > 0)
            {
                gr.Coins -= toGive;
                helper.BagCoins += toGive;
                helper.State = HelperState.SeekingTarget;
            }
            return;
        }
    }

    private static void WarehouseAutoLoadMilker(EntityWorld state, ref HelperComponent helper)
    {
        foreach (var grEntity in state.Filter<GlobalResourcesComponent>())
        {
            ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            int foodType = helper.WantedFoodType;
            int capacity = helper.BagCapacity - helper.GetBagTotal();
            int available = gr.GetFood(foodType);
            int toGive = System.Math.Min(capacity, available);
            if (toGive > 0)
            {
                for (int i = 0; i < toGive; i++)
                    gr.ConsumeFood(foodType);
                helper.AddBagFood(foodType, toGive);
                helper.State = HelperState.MovingToTarget;
            }
            return;
        }
    }

    private static void WarehouseDeposit(EntityWorld state, ref HelperComponent helper)
    {
        foreach (var grEntity in state.Filter<GlobalResourcesComponent>())
        {
            ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);

            gr.AddFood(FoodType.Grass, helper.BagGrass);
            gr.AddFood(FoodType.Carrot, helper.BagCarrot);
            gr.AddFood(FoodType.Apple, helper.BagApple);
            gr.AddFood(FoodType.Mushroom, helper.BagMushroom);
            helper.BagGrass = 0;
            helper.BagCarrot = 0;
            helper.BagApple = 0;
            helper.BagMushroom = 0;

            gr.AddMilkProduct(MilkProduct.Milk, helper.BagMilk);
            helper.BagMilk = 0;
            helper.BagCarrotMilkshake = 0;
            helper.BagVitaminMix = 0;
            helper.BagPurplePotion = 0;

            gr.Coins += helper.BagCoins;
            helper.BagCoins = 0;

            return;
        }
    }
}
