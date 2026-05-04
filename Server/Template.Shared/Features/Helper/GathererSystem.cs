using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Actions;

namespace Template.Shared.Systems;

[UpdateOrder(1)]
public class GathererSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var helperRef in state.Filter<HelperArchetype>())
        {
            if (helperRef.Helper.Type != HelperType.Gatherer) continue;
            if (helperRef.Helper.SuppressTickUpdate) continue;
            UpdateGatherer(state, helperRef, helperRef.Helper.PetCount);
        }
    }

    // ─── Gatherer: find food → harvest → return to player → wait for pickup ───

    private void UpdateGatherer(EntityWorld state, HelperRef helperRef, int petCount)
    {
        int workDuration = HelperSystem.ApplyPetSpeedBoost(HelperSystem.GatherWorkDuration, petCount);
        switch (helperRef.Helper.State)
        {
            case HelperState.Idle:
            case HelperState.SeekingTarget: GathererSeekFood(state, helperRef); break;
            case HelperState.MovingToTarget: GathererMoveToFood(state, helperRef, workDuration); break;
            case HelperState.Working: GathererWork(state, helperRef); break;
            case HelperState.Returning: GathererReturn(state, helperRef); break;
            case HelperState.WaitingForPickup: GathererWaitForPickup(state, helperRef); break;
        }
    }

    private void GathererSeekFood(EntityWorld state, HelperRef helperRef)
    {
        var foodEntity = FindNearestFood(state, helperRef.Entity);
        if (foodEntity == Entity.Null)
        {
            if (helperRef.Helper.GetBagTotal() > 0)
            {
                helperRef.Helper.State = HelperState.Returning;
                return;
            }
            HelperUtilities.StopMovement(state, helperRef.Entity);
            return;
        }
        helperRef.Helper.TargetEntity = foodEntity;
        helperRef.Helper.State = HelperState.MovingToTarget;
    }

    private void GathererMoveToFood(EntityWorld state, HelperRef helperRef, int workDuration)
    {
        if (!state.HasComponent<GrassComponent>(helperRef.Helper.TargetEntity))
        {
            helperRef.Helper.State = HelperState.SeekingTarget;
            helperRef.Helper.WorkTimer = 0;
            return;
        }
        var foodPos = state.GetComponent<Transform2D>(helperRef.Helper.TargetEntity).Position;
        if (HelperUtilities.NavigateToward(state, helperRef.Entity, foodPos, HelperSystem.GatherReachedDistSq))
        {
            helperRef.Helper.State = HelperState.Working;
            helperRef.Helper.WorkTimer = 0;
            helperRef.Helper.WorkDuration = workDuration;
        }
    }

    private void GathererWork(EntityWorld state, HelperRef helperRef)
    {
        helperRef.Helper.WorkTimer++;
        if (helperRef.Helper.WorkTimer < helperRef.Helper.WorkDuration) return;

        int bagSpace = helperRef.Helper.BagCapacity - helperRef.Helper.GetBagTotal();
        int amount = bagSpace > 0 ? 1 : 0;

        if (InteractionLogic.HarvestFood(state, helperRef.Helper.TargetEntity, amount, out int foodType, out bool destroyed))
        {
            InteractionLogic.FireInteracted(state, helperRef.Helper.TargetEntity);
            helperRef.Helper.AddBagFood(foodType, amount);

            string harvestKey = foodType switch
            {
                FoodType.Carrot => StateKeys.Carrot,
                FoodType.Apple => StateKeys.Apple,
                FoodType.Mushroom => StateKeys.Mushroom,
                _ => StateKeys.Grass
            };
            InteractionLogic.FireGainedResource(state, helperRef.Entity, harvestKey);

            if (destroyed)
                state.DeleteEntity(helperRef.Helper.TargetEntity);
        }

        helperRef.Helper.TargetEntity = Entity.Null;
        helperRef.Helper.State = helperRef.Helper.IsBagFull() ? HelperState.Returning : HelperState.SeekingTarget;
    }

    private void GathererReturn(EntityWorld state, HelperRef helperRef)
    {
        if (!state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer))
        {
            helperRef.Helper.State = HelperState.Idle;
            return;
        }
        var playerPos = state.GetComponent<Transform2D>(helperRef.Helper.OwnerPlayer).Position;
        if (HelperUtilities.NavigateToward(state, helperRef.Entity, playerPos, HelperSystem.PlayerReturnDistSq))
            helperRef.Helper.State = HelperState.WaitingForPickup;
    }

    private void GathererWaitForPickup(EntityWorld state, HelperRef helperRef)
    {
        if (state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer))
            SwarmFollow.Follow(state, helperRef.Entity, helperRef.Helper.OwnerPlayer);
    }

    private Entity FindNearestFood(EntityWorld state, Entity helper)
    {
        var myPos = state.GetComponent<Transform2D>(helper).Position;
        Entity best = Entity.Null;
        Float bestScore = -1f;

        foreach (var entity in state.Filter<GrassComponent>())
        {
            if (!state.HasComponent<Transform2D>(entity)) continue;
            var food = state.GetComponent<GrassComponent>(entity);
            var pos = state.GetComponent<Transform2D>(entity).Position;
            var distSq = Vector2.DistanceSquared(myPos, pos);
            if (distSq < 1f) distSq = 1f;

            int value = food.FoodType switch
            {
                FoodType.Mushroom => 200,
                FoodType.Apple => 20,
                FoodType.Carrot => 6,
                _ => 1
            };
            Float score = (Float)value / distSq;
            if (score > bestScore)
            {
                bestScore = score;
                best = entity;
            }
        }
        return best;
    }
}
