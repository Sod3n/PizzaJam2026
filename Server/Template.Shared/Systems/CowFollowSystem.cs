using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Deterministic.GameFramework.Types;

namespace Template.Shared.Systems;

public class CowFollowSystem : ISystem
{
    /// <summary>Only update nav target when target has moved this far from the last nav target.</summary>
    private static readonly Float TargetUpdateThreshold = 1.5f;
    private static readonly Float TargetUpdateThresholdSq = TargetUpdateThreshold * TargetUpdateThreshold;

    public void Update(EntityWorld state)
    {
        foreach (var cowRef in state.Filter<CowArchetype>())
        {
            // FollowTarget chains: head cow follows player, others follow the cow ahead.
            var followTarget = cowRef.Cow.FollowTarget;

            if (followTarget == Entity.Null || cowRef.Cow.FollowingPlayer == Entity.Null)
            {
                // Idle — walk back to assigned house if there is one.
                if (!cowRef.Cow.IsMilking
                    && state.TryGetComponent<Transform2D>(cowRef.Cow.HouseId, out var houseTransform))
                {
                    var offset = new Vector2(2, 2);
                    // Love house: pair-bonded cows stand on opposite sides.
                    if (state.TryGetComponent<LoveHouseComponent>(cowRef.Cow.HouseId, out var lh)
                        && lh.CowId2 == cowRef.Entity)
                    {
                        offset = new Vector2(-2, 2);
                    }
                    var targetHousePos = houseTransform.Position + offset;
                    var distSq = (targetHousePos - cowRef.Transform2D.Position).SqrMagnitude;

                    if (distSq > (Float)0.01f)
                    {
                        // Tight arrival distance so cow plants itself at the house.
                        cowRef.NavigationAgent2D.TargetDesiredDistance = 0.1f;
                        cowRef.NavigationAgent2D.TargetPosition = targetHousePos;
                        cowRef.NavigationAgent2D.IsNavigationFinished = false;
                        cowRef.CharacterBody2D.Velocity = cowRef.NavigationAgent2D.Velocity;
                        if (cowRef.NavigationAgent2D.Velocity.SqrMagnitude > (Float)0.01f)
                            cowRef.Transform2D.Rotation = cowRef.NavigationAgent2D.Velocity.ToAngle();
                        continue;
                    }
                    else
                    {
                        cowRef.NavigationAgent2D.TargetDesiredDistance = 2f;
                    }
                }

                if (!cowRef.NavigationAgent2D.IsNavigationFinished)
                {
                    cowRef.NavigationAgent2D.IsNavigationFinished = true;
                    cowRef.CharacterBody2D.Velocity = Vector2.Zero;
                }
                continue;
            }

            if (!state.TryGetComponent<Transform2D>(followTarget, out var followTargetTransform))
            {
                cowRef.CharacterBody2D.Velocity = Vector2.Zero;
                continue;
            }

            // Head cow follows player via flow field; chained cows follow their target directly.
            if (state.HasComponent<PlayerEntity>(followTarget))
            {
                SwarmFollow.Follow(state, cowRef.Entity, followTarget);
            }
            else
            {
                var targetPos = followTargetTransform.Position;
                var distToTargetSq = (targetPos - cowRef.Transform2D.Position).SqrMagnitude;

                var targetDriftSq = (targetPos - cowRef.NavigationAgent2D.TargetPosition).SqrMagnitude;
                if (targetDriftSq > TargetUpdateThresholdSq || cowRef.NavigationAgent2D.IsNavigationFinished)
                    cowRef.NavigationAgent2D.TargetPosition = targetPos;

                if (distToTargetSq > cowRef.NavigationAgent2D.TargetDesiredDistance * cowRef.NavigationAgent2D.TargetDesiredDistance)
                    cowRef.NavigationAgent2D.IsNavigationFinished = false;

                // Velocity lerp for smooth chain movement.
                cowRef.CharacterBody2D.Velocity += (cowRef.NavigationAgent2D.Velocity - cowRef.CharacterBody2D.Velocity) * (Float)0.12f;

                if (cowRef.CharacterBody2D.Velocity.SqrMagnitude > (Float)0.01f)
                    cowRef.Transform2D.Rotation = cowRef.CharacterBody2D.Velocity.ToAngle();
            }
        }
    }
}
