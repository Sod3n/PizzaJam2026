using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Systems;

namespace Template.Shared.Definitions;

public readonly ref partial struct CowRef
{
    private static readonly Float ChainTargetUpdateThresholdSq = (Float)1.5f * (Float)1.5f;

    public void WalkTo(Vector2 targetPos, Float arrivalDistance)
    {
        NavMovement.DriveToward(
            ref NavigationAgent2D,
            ref CharacterBody2D,
            ref Transform2D,
            targetPos,
            desiredDistance: arrivalDistance);
    }

    public void StopMoving()
    {
        if (NavigationAgent2D.IsNavigationFinished) return;
        NavigationAgent2D.IsNavigationFinished = true;
        CharacterBody2D.Velocity = Vector2.Zero;
    }

    public void FollowChain(Vector2 targetPos)
    {
        var distToTargetSq = (targetPos - Transform2D.Position).SqrMagnitude;

        var targetDriftSq = (targetPos - NavigationAgent2D.TargetPosition).SqrMagnitude;
        if (targetDriftSq > ChainTargetUpdateThresholdSq || NavigationAgent2D.IsNavigationFinished)
            NavigationAgent2D.TargetPosition = targetPos;

        if (distToTargetSq > NavigationAgent2D.TargetDesiredDistance * NavigationAgent2D.TargetDesiredDistance)
            NavigationAgent2D.IsNavigationFinished = false;

        // Velocity lerp for smooth chain movement.
        CharacterBody2D.Velocity += (NavigationAgent2D.Velocity - CharacterBody2D.Velocity) * (Float)0.12f;

        if (CharacterBody2D.Velocity.SqrMagnitude > (Float)0.01f)
            Transform2D.Rotation = CharacterBody2D.Velocity.ToAngle();
    }
}
