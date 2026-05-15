using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Actions;

namespace Template.Shared.Systems;

[UpdateOrder(1)]
public class BuilderSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var helperRef in state.Filter<HelperArchetype>())
        {
            if (helperRef.Helper.Type != HelperType.Builder) continue;
            if (helperRef.Helper.SuppressTickUpdate) continue;
            helperRef.Helper.IsAsking = false;
            helperRef.Helper.IsSleeping = false;
            if (!HelperUtilities.HasAssignedHouse(state, helperRef.Entity))
            {
                helperRef.Helper.State = HelperState.Idle;
                if (state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer))
                    SwarmFollow.Follow(state, helperRef.Entity, helperRef.Helper.OwnerPlayer);
                continue;
            }
            UpdateBuilder(state, helperRef, helperRef.Helper.PetCount);
        }
    }

    // ─── Builder: player gives coins → walk to land → contribute coins ───

    private void UpdateBuilder(EntityWorld state, HelperRef helperRef, int petCount)
    {
        switch (helperRef.Helper.State)
        {
            case HelperState.Idle: BuilderIdle(state, helperRef); break;
            case HelperState.SeekingTarget: BuilderSeekLand(state, helperRef); break;
            case HelperState.MovingToTarget: BuilderMoveToLand(state, helperRef, petCount); break;
            case HelperState.Working: BuilderWork(state, helperRef); break;
            case HelperState.Returning: BuilderReturn(state, helperRef); break;
            case HelperState.WaitingForPickup: BuilderWaitForPickup(state, helperRef); break;
        }
    }

    private void BuilderIdle(EntityWorld state, HelperRef helperRef)
    {
        if (helperRef.Helper.BagCoins > 0)
        {
            helperRef.Helper.State = HelperState.SeekingTarget;
            return;
        }
        if (HelperUtilities.PlayerCanFulfill(state, HelperType.Builder, -1)
            && state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer))
        {
            helperRef.Helper.IsAsking = true;
            SwarmFollow.Follow(state, helperRef.Entity, helperRef.Helper.OwnerPlayer);
        }
        else
        {
            helperRef.Helper.IsSleeping = true;
            HelperUtilities.NavigateHome(state, helperRef.Entity);
        }
    }

    private void BuilderSeekLand(EntityWorld state, HelperRef helperRef)
    {
        var land = FindFarthestUnlockedLand(state);
        if (land == Entity.Null)
        {
            helperRef.Helper.State = HelperState.Returning;
            return;
        }
        helperRef.Helper.TargetEntity = land;
        helperRef.Helper.State = HelperState.MovingToTarget;
    }

    private void BuilderMoveToLand(EntityWorld state, HelperRef helperRef, int petCount)
    {
        if (!state.HasComponent<LandComponent>(helperRef.Helper.TargetEntity))
        {
            helperRef.Helper.State = HelperState.SeekingTarget;
            helperRef.Helper.WorkTimer = 0;
            return;
        }
        var builderTargetPos = state.GetComponent<Transform2D>(helperRef.Helper.TargetEntity).Position;
        if (HelperUtilities.NavigateToward(state, helperRef.Entity, builderTargetPos, HelperSystem.TargetReachedDistSq))
        {
            helperRef.Helper.State = HelperState.Working;
            helperRef.Helper.WorkTimer = 0;
            helperRef.Helper.WorkDuration = HelperSystem.ApplyPetSpeedBoost(HelperSystem.BuildWorkDuration, petCount);
        }
    }

    private void BuilderWork(EntityWorld state, HelperRef helperRef)
    {
        helperRef.Helper.WorkTimer++;
        if (helperRef.Helper.WorkTimer < helperRef.Helper.WorkDuration) return;

        helperRef.Helper.WorkTimer = 0;

        if (helperRef.Helper.BagCoins > 0 && state.HasComponent<LandComponent>(helperRef.Helper.TargetEntity))
        {
            var landEntity = helperRef.Helper.TargetEntity;
            int buildAmount = System.Math.Min(1, helperRef.Helper.BagCoins);

            int deposited = InteractionLogic.DepositToLand(state, landEntity, buildAmount, leaveOneForPlayer: true, out bool landComplete);
            if (deposited <= 0)
            {
                helperRef.Helper.State = HelperState.SeekingTarget;
                helperRef.Helper.TargetEntity = Entity.Null;
                return;
            }
            InteractionLogic.FireInteracted(state, landEntity, StateKeys.Coins);
            helperRef.Helper.BagCoins -= deposited;

            if (landComplete)
            {
                var position = state.GetComponent<Transform2D>(landEntity).Position;
                var landComp = state.GetComponent<LandComponent>(landEntity);
                var landType = landComp.Type;
                int gridX = landComp.Arm;
                int gridY = landComp.Ring;
                CooldownComponent? carry = null;
                if (state.HasComponent<CooldownComponent>(landEntity))
                    carry = state.GetComponent<CooldownComponent>(landEntity);
                Definitions.LandDefinition.DeleteSignsForLand(state, landEntity);
                state.DeleteEntity(landEntity);

                var ctx = state.Ctx(helperRef.Helper.OwnerPlayer);
                InteractActionService.CompleteLandBuilding(ctx, position, landType, gridX, gridY, carry);

                helperRef.Helper.TargetEntity = Entity.Null;
                helperRef.Helper.State = helperRef.Helper.BagCoins > 0 ? HelperState.SeekingTarget : HelperState.Returning;
                return;
            }
        }

        if (helperRef.Helper.BagCoins <= 0)
        {
            helperRef.Helper.TargetEntity = Entity.Null;
            helperRef.Helper.State = HelperState.Idle;
        }
    }

    private void BuilderReturn(EntityWorld state, HelperRef helperRef)
    {
        if (!state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer))
        {
            helperRef.Helper.State = HelperState.Idle;
            return;
        }
        var builderReturnPos = state.GetComponent<Transform2D>(helperRef.Helper.OwnerPlayer).Position;
        if (HelperUtilities.NavigateToward(state, helperRef.Entity, builderReturnPos, HelperSystem.PlayerReturnDistSq))
            helperRef.Helper.State = helperRef.Helper.BagCoins > 0 ? HelperState.WaitingForPickup : HelperState.Idle;
    }

    private void BuilderWaitForPickup(EntityWorld state, HelperRef helperRef)
    {
        if (state.HasComponent<Transform2D>(helperRef.Helper.OwnerPlayer))
            SwarmFollow.Follow(state, helperRef.Entity, helperRef.Helper.OwnerPlayer);
    }

    /// <summary>
    /// Builder targets the FARTHEST plot the player has already committed to —
    /// CurrentCoins > 0 means the player dropped a fixation coin, so the build type
    /// is locked. Builder will not invest in plots that are still cycling type.
    /// </summary>
    private Entity FindFarthestUnlockedLand(EntityWorld state)
    {
        Entity farthest = Entity.Null;
        Float maxDistSq = 0f;

        foreach (var entity in state.Filter<LandComponent>())
        {
            var land = state.GetComponent<LandComponent>(entity);
            if (land.Locked != 0) continue;
            if (land.CurrentCoins <= 0) continue;
            if (land.CurrentCoins >= land.Threshold - 1) continue;
            if (!state.HasComponent<Transform2D>(entity)) continue;

            var pos = state.GetComponent<Transform2D>(entity).Position;
            var distSq = Vector2.DistanceSquared(Vector2.Zero, pos);
            if (distSq > maxDistSq)
            {
                maxDistSq = distSq;
                farthest = entity;
            }
        }
        return farthest;
    }
}
