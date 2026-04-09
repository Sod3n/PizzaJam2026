using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using FixedMathSharp;
using Template.Shared.Components;
using System.Runtime.InteropServices;

namespace Template.Shared.Features.Movement;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
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
        // DIAGNOSTIC: absolute minimum — just move position by direction, no math
        ref var transform = ref ctx.State.GetComponent<Transform2D>(ctx.Entity);
        transform.Position += directionAction.Direction;
    }
}
