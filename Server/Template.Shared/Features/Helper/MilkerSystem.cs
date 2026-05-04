using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Actions;

namespace Template.Shared.Systems;

[UpdateOrder(1)]
public class MilkerSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var helperRef in state.Filter<HelperArchetype>())
        {
            if (helperRef.Helper.Type != HelperType.Milker) continue;
            if (helperRef.Helper.SuppressTickUpdate) continue;
            UpdateMilker(state, helperRef, helperRef.Helper.PetCount);
        }
    }

    // ─── Milker: find house → follow player asking for food → receive food → go milk → return with milk → wait for pickup ───

    private void UpdateMilker(EntityWorld state, HelperRef helperRef, int petCount)
    {
        switch (helperRef.Helper.State)
        {
            case HelperState.Idle: MilkerIdle(state, helperRef); break;
            case HelperState.SeekingTarget: MilkerReevaluate(helperRef); break;
            case HelperState.MovingToTarget: MilkerMoveToHouse(state, helperRef, petCount); break;
            case HelperState.Working: MilkerWork(state, helperRef); break;
            case HelperState.Returning: MilkerReturn(state, helperRef); break;
            case HelperState.WaitingForPickup: MilkerWaitForPickup(state, helperRef); break;
        }
    }

    private void MilkerIdle(EntityWorld state, HelperRef helperRef)
    {
        if (!state.HasComponent<HouseComponent>(helperRef.Helper.TargetEntity))
        {
            var milkTarget = FindMilkableHouse(state, helperRef.Entity);
            if (milkTarget == Entity.Null)
            {
                helperRef.Helper.WantedFoodType = -1;
                if (state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer))
                    SwarmFollow.Follow(state, helperRef.Entity, helperRef.Helper.OwnerPlayer);
                return;
            }
            helperRef.Helper.TargetEntity = milkTarget;

            var house = state.GetComponent<HouseComponent>(milkTarget);
            if (state.HasComponent<CowComponent>(house.CowId))
            {
                var cow = state.GetComponent<CowComponent>(house.CowId);
                helperRef.Helper.WantedFoodType = ResolveMilkerFoodType(cow, cow.SelectedFood);
            }
            else
            {
                helperRef.Helper.WantedFoodType = FoodType.Grass;
            }
        }

        if (helperRef.Helper.GetFoodTotal() > 0)
        {
            helperRef.Helper.State = HelperState.MovingToTarget;
            return;
        }

        if (state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer))
            SwarmFollow.Follow(state, helperRef.Entity, helperRef.Helper.OwnerPlayer);
    }

    private void MilkerReevaluate(HelperRef helperRef)
    {
        helperRef.Helper.TargetEntity = Entity.Null;
        helperRef.Helper.WantedFoodType = -1;
        helperRef.Helper.State = HelperState.Idle;
    }

    private void MilkerMoveToHouse(EntityWorld state, HelperRef helperRef, int petCount)
    {
        if (!state.HasComponent<HouseComponent>(helperRef.Helper.TargetEntity))
        {
            helperRef.Helper.State = HelperState.SeekingTarget;
            helperRef.Helper.WorkTimer = 0;
            return;
        }
        var houseCheck = state.GetComponent<HouseComponent>(helperRef.Helper.TargetEntity);
        if (!state.HasComponent<CowComponent>(houseCheck.CowId))
        {
            helperRef.Helper.State = HelperState.SeekingTarget;
            helperRef.Helper.WorkTimer = 0;
            return;
        }
        var cowCheck = state.GetComponent<CowComponent>(houseCheck.CowId);
        if (cowCheck.IsDepressed || cowCheck.IsMilking || cowCheck.Exhaust >= cowCheck.MaxExhaust)
        {
            helperRef.Helper.State = HelperState.SeekingTarget;
            helperRef.Helper.WorkTimer = 0;
            return;
        }
        var housePos = state.GetComponent<Transform2D>(helperRef.Helper.TargetEntity).Position;
        if (HelperUtilities.NavigateToward(state, helperRef.Entity, housePos, HelperSystem.TargetReachedDistSq))
        {
            if (state.HasComponent<HouseComponent>(helperRef.Helper.TargetEntity))
            {
                var house = state.GetComponent<HouseComponent>(helperRef.Helper.TargetEntity);
                if (state.HasComponent<CowComponent>(house.CowId))
                {
                    ref var cow = ref state.GetComponent<CowComponent>(house.CowId);
                    if (!cow.IsDepressed && !cow.IsMilking && cow.Exhaust <= cow.MaxExhaust / 2)
                    {
                        cow.IsMilking = true;
                        state.HideEntity(helperRef.Entity);
                        state.HideEntity(house.CowId);
                    }
                }
            }
            helperRef.Helper.State = HelperState.Working;
            helperRef.Helper.WorkTimer = 0;
            helperRef.Helper.WorkDuration = HelperSystem.ApplyPetSpeedBoost(HelperSystem.MilkWorkDuration, petCount);
        }
    }

    private void MilkerWork(EntityWorld state, HelperRef helperRef)
    {
        helperRef.Helper.WorkTimer++;
        if (helperRef.Helper.WorkTimer < helperRef.Helper.WorkDuration) return;

        helperRef.Helper.WorkTimer = 0;
        bool milked = false;

        if (state.HasComponent<HouseComponent>(helperRef.Helper.TargetEntity))
        {
            var house = state.GetComponent<HouseComponent>(helperRef.Helper.TargetEntity);
            if (state.HasComponent<CowComponent>(house.CowId))
            {
                var cow = state.GetComponent<CowComponent>(house.CowId);
                if (cow.Exhaust < cow.MaxExhaust)
                {
                    int foodToUse = ResolveFoodFromBag(ref helperRef.Helper, cow.SelectedFood, cow.PreferredFood);
                    if (foodToUse >= 0)
                    {
                        int milkPower = 1;
                        bool produced = InteractionLogic.MilkCowFromBag(state, house.CowId, foodToUse, milkPower, ref helperRef.Helper, out bool cowDone);
                        state.AddComponent(helperRef.Helper.TargetEntity, new EnterStateComponent
                        {
                            Key = StateKeys.Interacted, Param = produced ? "milk_ok" : "milk_fail", Age = 0
                        });
                        milked = !cowDone;
                    }
                }
            }
        }

        if (!milked)
        {
            MilkerFinishMilking(state, helperRef);
            helperRef.Helper.TargetEntity = Entity.Null;
            helperRef.Helper.WantedFoodType = -1;
            helperRef.Helper.State = helperRef.Helper.GetMilkTotal() > 0 ? HelperState.Returning : HelperState.SeekingTarget;
        }
    }

    private void MilkerReturn(EntityWorld state, HelperRef helperRef)
    {
        if (!state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer))
        {
            helperRef.Helper.State = HelperState.Idle;
            return;
        }
        var milkerReturnPos = state.GetComponent<Transform2D>(helperRef.Helper.OwnerPlayer).Position;
        if (HelperUtilities.NavigateToward(state, helperRef.Entity, milkerReturnPos, HelperSystem.PlayerReturnDistSq))
            helperRef.Helper.State = HelperState.WaitingForPickup;
    }

    private void MilkerWaitForPickup(EntityWorld state, HelperRef helperRef)
    {
        if (state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer))
            SwarmFollow.Follow(state, helperRef.Entity, helperRef.Helper.OwnerPlayer);
    }

    /// <summary>
    /// Determine which food type the milker should request from the player for a given cow/house.
    /// Uses the house's selected food if the cow supports that tier, otherwise the cow's preferred food.
    /// </summary>
    private static int ResolveMilkerFoodType(CowComponent cow, int houseSelectedFood)
    {
        int cowMaxTier = FoodType.MaxTier(cow.PreferredFood);
        if (houseSelectedFood >= 0 && houseSelectedFood <= cowMaxTier)
            return houseSelectedFood;
        return cow.PreferredFood;
    }

    /// <summary>
    /// Determine which food to use from the milker's bag.
    /// Strict: only allows the house's selected food — no fallback to lower tiers.
    /// Returns -1 if the selected food or its prerequisite is unavailable in the bag.
    /// </summary>
    private static int ResolveFoodFromBag(ref HelperComponent helper, int houseSelectedFood, int cowPreferredFood)
    {
        int cowMaxTier = FoodType.MaxTier(cowPreferredFood);

        if (houseSelectedFood >= 0 && houseSelectedFood <= cowMaxTier && helper.GetBagFood(houseSelectedFood) > 0)
        {
            int prereq = FoodType.PrerequisiteProduct(houseSelectedFood);
            if (prereq < 0 || helper.GetBagMilkProduct(prereq) > 0)
                return houseSelectedFood;
        }

        return -1;
    }

    /// <summary>
    /// Unhide the milker and cow when milking is done.
    /// </summary>
    private static void MilkerFinishMilking(EntityWorld state, HelperRef helperRef)
    {
        state.UnhideEntity(helperRef.Entity);

        if (state.HasComponent<HouseComponent>(helperRef.Helper.TargetEntity))
        {
            var house = state.GetComponent<HouseComponent>(helperRef.Helper.TargetEntity);
            if (state.HasComponent<CowComponent>(house.CowId))
            {
                ref var cow = ref state.GetComponent<CowComponent>(house.CowId);
                cow.IsMilking = false;
                state.UnhideEntity(house.CowId);
            }
        }
    }

    private Entity FindMilkableHouse(EntityWorld state, Entity helper)
    {
        var myPos = state.GetComponent<Transform2D>(helper).Position;
        Entity best = Entity.Null;
        Float bestScore = -1f;

        foreach (var houseEntity in state.Filter<HouseComponent>())
        {
            var house = state.GetComponent<HouseComponent>(houseEntity);
            if (house.CowId == Entity.Null) continue;
            if (!state.HasComponent<CowComponent>(house.CowId)) continue;

            var cow = state.GetComponent<CowComponent>(house.CowId);
            if (cow.IsDepressed || cow.IsMilking || cow.Exhaust >= cow.MaxExhaust) continue;
            if (cow.Exhaust > cow.MaxExhaust / 2) continue;

            var housePos = state.GetComponent<Transform2D>(houseEntity).Position;
            var distSq = Vector2.DistanceSquared(myPos, housePos);
            if (distSq < 1f) distSq = 1f;

            Float score = (Float)(cow.MaxExhaust - cow.Exhaust) / distSq;

            if (score > bestScore)
            {
                bestScore = score;
                best = houseEntity;
            }
        }
        return best;
    }
}
