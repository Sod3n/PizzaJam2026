using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Deterministic.GameFramework.Types;

namespace Template.Shared.Systems;

public class CowFollowSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var cowRef in state.Filter<CowArchetype>())
        {
            if (cowRef.Cow.IsAttacking || cowRef.Cow.IsExhausted) continue;
            if (cowRef.Cow.FollowTarget == Entity.Null || cowRef.Cow.FollowingPlayer == Entity.Null)
                UpdateIdle(state, cowRef);
            else
                UpdateFollowing(state, cowRef);
        }
    }

    private static void UpdateIdle(EntityWorld state, CowRef cowRef)
    {
        if (cowRef.Cow.IsMilking) return;

        if (TryGetHouseStandPosition(state, cowRef, out var standPos))
        {
            var distSq = (standPos - cowRef.Transform2D.Position).SqrMagnitude;
            if (distSq > (Float)0.01f)
            {
                cowRef.WalkTo(standPos, arrivalDistance: 0.1f);
                return;
            }
            cowRef.NavigationAgent2D.TargetDesiredDistance = 2f;
        }

        cowRef.StopMoving();
    }

    internal static bool TryGetHouseStandPosition(EntityWorld state, CowRef cowRef, out Vector2 standPos)
    {
        if (!state.TryGetComponent<Transform2D>(cowRef.Cow.HouseId, out var houseTransform))
        {
            standPos = default;
            return false;
        }
        var offset = new Vector2(2, 2);
        // Love house: pair-bonded cows stand on opposite sides.
        if (state.TryGetComponent<LoveHouseComponent>(cowRef.Cow.HouseId, out var lh)
            && lh.CowId2 == cowRef.Entity)
        {
            offset = new Vector2(-2, 2);
        }
        standPos = houseTransform.Position + offset;
        return true;
    }

    private static void UpdateFollowing(EntityWorld state, CowRef cowRef)
    {
        var followTarget = cowRef.Cow.FollowTarget;
        var target = state.ResolveFollowTarget(followTarget);
        switch (target.Kind)
        {
            case FollowTargetKind.Missing:
                cowRef.CharacterBody2D.Velocity = Vector2.Zero;
                break;
            case FollowTargetKind.Player:
                SwarmFollow.Follow(state, cowRef.Entity, followTarget);
                break;
            case FollowTargetKind.Cow:
                if (target.TryGetPosition(out var targetPos))
                    cowRef.FollowChain(targetPos);
                break;
        }
    }
}
