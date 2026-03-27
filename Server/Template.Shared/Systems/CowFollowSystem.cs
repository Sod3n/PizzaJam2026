using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Navigation2D.Components;
using Template.Shared.Components;
using Deterministic.GameFramework.Types;

namespace Template.Shared.Systems;

public class CowFollowSystem : ISystem
{
    /// <summary>Only update nav target when player has moved this far from the last target.</summary>
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

            if (cow.FollowingPlayer == Entity.Null)
            {
                if (!navAgent.IsNavigationFinished)
                {
                    navAgent.IsNavigationFinished = true;
                    cowBody.Velocity = Vector2.Zero;
                }
                continue;
            }

            if (!state.HasComponent<Transform2D>(cow.FollowingPlayer) || !state.HasComponent<Transform2D>(cowEntity))
            {
                cowBody.Velocity = Vector2.Zero;
                continue;
            }

            var playerPos = state.GetComponent<Transform2D>(cow.FollowingPlayer).Position;
            var cowPos = state.GetComponent<Transform2D>(cowEntity).Position;
            var distToPlayerSq = (playerPos - cowPos).SqrMagnitude;

            // Only update nav target when player has moved significantly from last target
            // This prevents path recomputation every tick which kills nav velocity
            var targetDriftSq = (playerPos - navAgent.TargetPosition).SqrMagnitude;
            if (targetDriftSq > TargetUpdateThresholdSq || navAgent.IsNavigationFinished)
            {
                navAgent.TargetPosition = playerPos;
            }

            // Keep navigation active when not close enough
            if (distToPlayerSq > navAgent.TargetDesiredDistance * navAgent.TargetDesiredDistance)
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
