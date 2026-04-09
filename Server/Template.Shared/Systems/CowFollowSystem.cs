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
                // Not following anyone — check if cow needs to walk to its house
                if (cow.HouseId != Entity.Null && !cow.IsMilking
                    && state.HasComponent<Transform2D>(cow.HouseId) && state.HasComponent<Transform2D>(cowEntity))
                {
                    var housePos = state.GetComponent<Transform2D>(cow.HouseId).Position;
                    var offset = new Vector2(2, 2);
                    // Love house: second cow goes to the left
                    if (state.HasComponent<LoveHouseComponent>(cow.HouseId))
                    {
                        var lh = state.GetComponent<LoveHouseComponent>(cow.HouseId);
                        if (lh.CowId2 == cowEntity) offset = new Vector2(-2, 2);
                    }
                    var targetHousePos = housePos + offset;
                    var curPos = state.GetComponent<Transform2D>(cowEntity).Position;
                    var distSq = (targetHousePos - curPos).SqrMagnitude;

                    if (distSq > (Float)0.01f) // 0.1 * 0.1
                    {
                        // Use tight desired distance for house arrival
                        navAgent.TargetDesiredDistance = 0.1f;
                        navAgent.TargetPosition = targetHousePos;
                        navAgent.IsNavigationFinished = false;
                        cowBody.Velocity = navAgent.Velocity;
                        if (navAgent.Velocity.SqrMagnitude > (Float)0.01f)
                        {
                            ref var ct = ref state.GetComponent<Transform2D>(cowEntity);
                            ct.Rotation = navAgent.Velocity.ToAngle();
                        }
                        continue;
                    }
                    else
                    {
                        // Arrived — restore default desired distance
                        navAgent.TargetDesiredDistance = 2f;
                    }
                }

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

            // Use SwarmFollow for crowd behavior
            // First cow follows player via flow field, chained cows follow their target directly
            if (state.HasComponent<PlayerEntity>(followTarget))
            {
                SwarmFollow.Follow(state, cowEntity, followTarget);
            }
            else
            {
                // Chained cow: simple nav follow toward the cow ahead
                var targetPos = state.GetComponent<Transform2D>(followTarget).Position;
                var cowPos = state.GetComponent<Transform2D>(cowEntity).Position;
                var distToTargetSq = (targetPos - cowPos).SqrMagnitude;

                var targetDriftSq = (targetPos - navAgent.TargetPosition).SqrMagnitude;
                if (targetDriftSq > TargetUpdateThresholdSq || navAgent.IsNavigationFinished)
                    navAgent.TargetPosition = targetPos;

                if (distToTargetSq > navAgent.TargetDesiredDistance * navAgent.TargetDesiredDistance)
                    navAgent.IsNavigationFinished = false;

                // Velocity lerp for smooth movement
                var desiredVel = navAgent.Velocity;
                cowBody.Velocity = cowBody.Velocity + (desiredVel - cowBody.Velocity) * (Float)0.12f;

                if (cowBody.Velocity.SqrMagnitude > (Float)0.01f)
                {
                    ref var cowTransform = ref state.GetComponent<Transform2D>(cowEntity);
                    cowTransform.Rotation = cowBody.Velocity.ToAngle();
                }
            }
        }
    }
}
