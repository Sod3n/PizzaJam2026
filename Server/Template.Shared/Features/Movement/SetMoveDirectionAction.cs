using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using FixedMathSharp;
using Template.Shared.Components;

namespace Template.Shared.Features.Movement;

[StableId("495f96cc-8e29-4901-9e83-bd6b5cb0c293")]
public struct SetMoveDirectionAction : IAction
{
    public Vector2 Direction;
    public Float Speed;
    
    public SetMoveDirectionAction(Vector2 direction, Float speed)
    {
        Direction = direction;
        Speed = speed;
    }
}

public class SetMoveDirectionActionService : ActionService<SetMoveDirectionAction, CharacterBody2D>
{
    protected override void ExecuteProcess(SetMoveDirectionAction directionAction, ref CharacterBody2D body, Context ctx)
    {
        // Check if player is milking
        if (ctx.State.HasComponent<PlayerStateComponent>(ctx.Entity))
        {
            ref var playerState = ref ctx.State.GetComponent<PlayerStateComponent>(ctx.Entity);
            if (playerState.State == (int)PlayerState.Milking)
            {
                body.Velocity = Vector2.Zero;
                return;
            }
        }

        // Rapier expects Velocity in Units/Second.
        // Reverted to specific client perspective logic: (Speed / TickRate) * 20 -> Displacement/Tick
        body.Velocity = new Vector2(directionAction.Direction.X, directionAction.Direction.Y * 2) * directionAction.Speed / 2;
        
        // Update Rotation to face direction (only if moving)
        if (directionAction.Speed <= new Float(0.01f) ||
            directionAction.Direction.SqrMagnitude <= new Float(0.01f)) return;
        
        var angle = directionAction.Direction.ToAngle();
        ref var transform = ref ctx.State.GetComponent<Transform2D>(ctx.Entity);
        
        transform.Rotation = angle;
    }
}
