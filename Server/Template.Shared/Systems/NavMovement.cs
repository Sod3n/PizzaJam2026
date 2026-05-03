using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.Types;

namespace Template.Shared.Systems;

public static class NavMovement
{
    private static readonly Float StationarySqMag = (Float)0.01f;

    /// <summary>
    /// Set nav target and step the body using the nav agent's current velocity, facing the velocity direction.
    /// No velocity smoothing. Use this for "plant on a fixed point" behaviors (e.g. arriving at a house).
    /// </summary>
    public static void DriveToward(
        ref NavigationAgent2D nav,
        ref CharacterBody2D body,
        ref Transform2D transform,
        Vector2 targetPos,
        Float desiredDistance)
    {
        nav.TargetDesiredDistance = desiredDistance;
        nav.TargetPosition = targetPos;
        nav.IsNavigationFinished = false;
        body.Velocity = nav.Velocity;
        if (nav.Velocity.SqrMagnitude > StationarySqMag)
            transform.Rotation = nav.Velocity.ToAngle();
    }
}
