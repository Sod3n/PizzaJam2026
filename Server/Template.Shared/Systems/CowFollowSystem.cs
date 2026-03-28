using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Navigation2D.Components;
using Template.Shared.Components;
using Deterministic.GameFramework.Types;

namespace Template.Shared.Systems;

public class CowFollowSystem : ISystem
{
    /// <summary>Only update nav target when target has moved this far from the last nav target.</summary>
    private static readonly Float TargetUpdateThreshold = 1.5f;
    private static readonly Float TargetUpdateThresholdSq = TargetUpdateThreshold * TargetUpdateThreshold;

    public void Update(EntityWorld state)
    {
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            if (!state.HasComponent<NavigationAgent2D>(cowEntity)) continue;
            if (!state.HasComponent<CharacterBody2D>(cowEntity)) continue;

            ref var navAgent = ref state.GetComponent<NavigationAgent2D>(cowEntity);
            ref var cowBody = ref state.GetComponent<CharacterBody2D>(cowEntity);

            // Use FollowTarget for chain following (could be player or another cow)
            var followTarget = cow.FollowTarget;

            if (followTarget == Entity.Null || cow.FollowingPlayer == Entity.Null)
            {
                if (!navAgent.IsNavigationFinished)
                {
                    navAgent.IsNavigationFinished = true;
                    cowBody.Velocity = Vector2.Zero;
                }
                continue;
            }

            if (!state.HasComponent<Transform2D>(followTarget) || !state.HasComponent<Transform2D>(cowEntity))
            {
                cowBody.Velocity = Vector2.Zero;
                continue;
            }

            var targetPos = state.GetComponent<Transform2D>(followTarget).Position;
            var cowPos = state.GetComponent<Transform2D>(cowEntity).Position;
            var distToTargetSq = (targetPos - cowPos).SqrMagnitude;

            // Only update nav target when target has moved significantly from last nav target
            var targetDriftSq = (targetPos - navAgent.TargetPosition).SqrMagnitude;
            if (targetDriftSq > TargetUpdateThresholdSq || navAgent.IsNavigationFinished)
            {
                navAgent.TargetPosition = targetPos;
            }

            // Keep navigation active when not close enough
            if (distToTargetSq > navAgent.TargetDesiredDistance * navAgent.TargetDesiredDistance)
            {
                navAgent.IsNavigationFinished = false;
            }

            // Apply navigation velocity to character body
            cowBody.Velocity = navAgent.Velocity;

            // Face movement direction
            if (navAgent.Velocity.SqrMagnitude > (Float)0.01f)
            {
                ref var cowTransform = ref state.GetComponent<Transform2D>(cowEntity);
                cowTransform.Rotation = navAgent.Velocity.ToAngle();
            }
        }
    }
}
