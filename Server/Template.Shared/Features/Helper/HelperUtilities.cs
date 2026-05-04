using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Navigation2D.Components;

namespace Template.Shared.Systems;

public static class HelperUtilities
{
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
