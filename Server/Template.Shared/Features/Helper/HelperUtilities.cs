using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Navigation2D.Components;
using Template.Shared.Actions;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public static class HelperUtilities
{
    public static Entity FindAssignedHouse(EntityWorld state, Entity helperEntity)
    {
        foreach (var houseEntity in state.Filter<HouseComponent>())
        {
            if (state.GetComponent<HouseComponent>(houseEntity).HelperId == helperEntity)
                return houseEntity;
        }
        return Entity.Null;
    }

    public static bool HasAssignedHouse(EntityWorld state, Entity helperEntity)
        => FindAssignedHouse(state, helperEntity) != Entity.Null;

    // True if the player has the resource this helper would ask for; false when the player
    // can't possibly fulfill the request (so the helper should go home and idle instead of
    // chasing the player for nothing).
    public static bool PlayerCanFulfill(EntityWorld state, int helperType, int wantedFoodType)
    {
        var grEntity = InteractFeedback.GetGlobalResourcesEntity(state);
        if (grEntity == Entity.Null) return false;
        var gr = state.GetComponent<GlobalResourcesComponent>(grEntity);
        return helperType switch
        {
            HelperType.Seller => gr.Milk > 0,
            HelperType.Builder => gr.Coins > 0,
            HelperType.Milker => wantedFoodType >= 0 && gr.GetFood(wantedFoodType) > 0,
            _ => false,
        };
    }

    // Navigates the helper toward its assigned house. Returns true if at house (idle there).
    public static bool NavigateHome(EntityWorld state, Entity helperEntity)
    {
        var house = FindAssignedHouse(state, helperEntity);
        if (house == Entity.Null || !state.HasComponent<Transform2D>(house))
        {
            StopMovement(state, helperEntity);
            return false;
        }
        var housePos = state.GetComponent<Transform2D>(house).Position;
        return NavigateToward(state, helperEntity, housePos, HelperSystem.PlayerReturnDistSq);
    }


    // Steers the entity toward targetPos via navigation agent + ORCA velocity blending.
    // Returns true when the entity is within desiredDistSq of the target.
    public static bool NavigateToward(EntityWorld state, Entity entity, Vector2 targetPos, Float desiredDistSq)
    {
        ref var navAgent = ref state.GetComponent<NavigationAgent2D>(entity);
        var myPos = state.GetComponent<Transform2D>(entity).Position;
        var distSq = (targetPos - myPos).SqrMagnitude;

        if (distSq <= desiredDistSq)
        {
            ref var body = ref state.GetComponent<CharacterBody2D>(entity);
            body.Velocity = Vector2.Zero;
            navAgent.IsNavigationFinished = true;
            return true;
        }

        var targetDriftSq = (targetPos - navAgent.TargetPosition).SqrMagnitude;
        if (targetDriftSq > Float.One || navAgent.IsNavigationFinished)
        {
            navAgent.TargetPosition = targetPos;
            navAgent.IsNavigationFinished = false;
        }

        var vel = SwarmFollow.ApplyOrcaForNav(state, entity, myPos, navAgent.Velocity);
        ref var charBody = ref state.GetComponent<CharacterBody2D>(entity);
        charBody.Velocity = vel;

        if (navAgent.Velocity.SqrMagnitude > (Float)0.01f)
        {
            ref var transform = ref state.GetComponent<Transform2D>(entity);
            transform.Rotation = navAgent.Velocity.ToAngle();
        }

        return false;
    }

    public static void StopMovement(EntityWorld state, Entity entity)
    {
        ref var body = ref state.GetComponent<CharacterBody2D>(entity);
        body.Velocity = Vector2.Zero;
    }

    public static Entity FindNearestEntity<T>(EntityWorld state, Entity helper) where T : unmanaged, IComponent
    {
        var myPos = state.GetComponent<Transform2D>(helper).Position;
        Entity nearest = Entity.Null;
        Float minDistSq = 999999f;

        foreach (var entity in state.Filter<T>())
        {
            if (!state.HasComponent<Transform2D>(entity)) continue;
            var pos = state.GetComponent<Transform2D>(entity).Position;
            var distSq = Vector2.DistanceSquared(myPos, pos);
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                nearest = entity;
            }
        }
        return nearest;
    }
}
