using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

[UpdateOrder(1)]
public class SellerSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var helperRef in state.Filter<HelperArchetype>())
        {
            if (helperRef.Helper.Type != HelperType.Seller) continue;
            if (helperRef.Helper.SuppressTickUpdate) continue;
            UpdateSeller(state, helperRef, helperRef.Helper.PetCount);
        }
    }

    // ─── Seller: player gives milk → sell at sell point → return with coins → wait for pickup ───

    private void UpdateSeller(EntityWorld state, HelperRef helperRef, int petCount)
    {
        switch (helperRef.Helper.State)
        {
            case HelperState.Idle: SellerIdle(state, helperRef); break;
            case HelperState.SeekingTarget: SellerSeekSellPoint(state, helperRef); break;
            case HelperState.MovingToTarget: SellerMoveToSellPoint(state, helperRef, petCount); break;
            case HelperState.Working: SellerWork(state, helperRef); break;
            case HelperState.Returning: SellerReturn(state, helperRef); break;
            case HelperState.WaitingForPickup: SellerWaitForPickup(state, helperRef); break;
        }
    }

    private void SellerIdle(EntityWorld state, HelperRef helperRef)
    {
        if (state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer))
            SwarmFollow.Follow(state, helperRef.Entity, helperRef.Helper.OwnerPlayer);
        if (HasMilkInBag(ref helperRef.Helper))
            helperRef.Helper.State = HelperState.SeekingTarget;
    }

    private void SellerSeekSellPoint(EntityWorld state, HelperRef helperRef)
    {
        var sellPoint = HelperUtilities.FindNearestEntity<SellPointComponent>(state, helperRef.Entity);
        if (sellPoint == Entity.Null)
        {
            helperRef.Helper.State = HelperState.Idle;
            return;
        }
        helperRef.Helper.TargetEntity = sellPoint;
        helperRef.Helper.State = HelperState.MovingToTarget;
    }

    private void SellerMoveToSellPoint(EntityWorld state, HelperRef helperRef, int petCount)
    {
        if (!state.HasComponent<Transform2D>(helperRef.Helper.TargetEntity))
        {
            helperRef.Helper.State = HelperState.SeekingTarget;
            helperRef.Helper.WorkTimer = 0;
            return;
        }
        var targetPos = state.GetComponent<Transform2D>(helperRef.Helper.TargetEntity).Position;
        if (HelperUtilities.NavigateToward(state, helperRef.Entity, targetPos, HelperSystem.TargetReachedDistSq))
        {
            helperRef.Helper.State = HelperState.Working;
            helperRef.Helper.WorkTimer = 0;
            helperRef.Helper.WorkDuration = HelperSystem.ApplyPetSpeedBoost(HelperSystem.SellWorkDuration, petCount);
        }
    }

    private void SellerWork(EntityWorld state, HelperRef helperRef)
    {
        helperRef.Helper.WorkTimer++;
        if (helperRef.Helper.WorkTimer < helperRef.Helper.WorkDuration) return;

        helperRef.Helper.WorkTimer = 0;
        SellOneItem(ref helperRef.Helper);
        if (helperRef.Helper.TargetEntity != Entity.Null)
            state.AddComponent(helperRef.Helper.TargetEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = StateKeys.Coins, Age = 0 });

        if (!HasMilkInBag(ref helperRef.Helper))
            helperRef.Helper.State = HelperState.Returning;
    }

    private void SellerReturn(EntityWorld state, HelperRef helperRef)
    {
        if (!state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer))
        {
            helperRef.Helper.State = HelperState.Idle;
            return;
        }
        var sellerReturnPos = state.GetComponent<Transform2D>(helperRef.Helper.OwnerPlayer).Position;
        if (HelperUtilities.NavigateToward(state, helperRef.Entity, sellerReturnPos, HelperSystem.PlayerReturnDistSq))
            helperRef.Helper.State = HelperState.WaitingForPickup;
    }

    private void SellerWaitForPickup(EntityWorld state, HelperRef helperRef)
    {
        if (state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer))
            SwarmFollow.Follow(state, helperRef.Entity, helperRef.Helper.OwnerPlayer);
    }

    private bool HasMilkInBag(ref HelperComponent helper)
    {
        return helper.BagMilk > 0;
    }

    private bool SellOneItem(ref HelperComponent helper)
    {
        if (helper.BagMilk > 0) { helper.BagMilk--; helper.BagCoins += MilkProduct.CoinValue(MilkProduct.Milk); return true; }
        return false;
    }
}
