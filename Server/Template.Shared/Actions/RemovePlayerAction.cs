using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Template.Shared.Components;
using System;

namespace Template.Shared.Actions;

[StableId("3a95595e-d3b2-4694-b9a9-b0d2bde23223")]
public struct RemovePlayerAction : IAction
{
    public Guid UserId;

    public RemovePlayerAction(Guid userId)
    {
        UserId = userId;
    }
}

public class RemovePlayerActionService : ActionService<RemovePlayerAction, World>
{
    protected override void ExecuteProcess(RemovePlayerAction action, ref World entity, Context ctx)
    {
        foreach (var playerEntity in ctx.State.Filter<PlayerEntity>())
        {
            ref var playerComp = ref ctx.State.GetComponent<PlayerEntity>(playerEntity);
            if (playerComp.UserId == action.UserId)
            {
                ctx.State.DeleteEntity(playerEntity);
                return;
            }
        }
    }
}
