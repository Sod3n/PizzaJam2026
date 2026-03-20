using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Template.Shared.Definitions;
using System;

namespace Template.Shared.Actions;

[StableId("34c0145a-7bf6-4f42-bc08-0771b46cd23e")]
public struct AddPlayerAction : IAction
{
    public Guid UserId;

    public AddPlayerAction(Guid userId)
    {
        UserId = userId;
    }
}

public class AddPlayerActionService : ActionService<AddPlayerAction, World>
{
    protected override void ExecuteProcess(AddPlayerAction action, ref World entity, Context ctx)
    {
        System.Console.WriteLine($"[AddPlayerAction] Processing for User {action.UserId} on Tick {ctx.State.GetCustomData<IGameTime>()?.CurrentTick}. NextEntityId: {ctx.State.NextEntityId}");

        // Check if player already exists
        foreach (var existingPlayer in ctx.State.Filter<PlayerEntity>())
        {
            ref var playerComp = ref ctx.State.GetComponent<PlayerEntity>(existingPlayer);
            if (playerComp.UserId == action.UserId)
            {
                System.Console.WriteLine($"[AddPlayerAction] Player {action.UserId} already exists (Entity {existingPlayer.Id})! Skipping.");
                return; // Player already exists
            }
        }
        
        // Create the Player Entity
        // Use deterministic position based on UserId to avoid perfect overlap collisions at (0,0)
        // Perfect overlaps can cause non-deterministic physics resolution
        // ERROR FIX: Guid.GetHashCode() is not stable across processes! Use bytes.
        int seed = BitConverter.ToInt32(action.UserId.ToByteArray(), 0);
        var random = new Deterministic.GameFramework.Types.DeterministicRandom((uint)seed);
        var x = random.NextInt(-200, 200);
        var y = random.NextInt(-200, 200);
        var position = new Vector2(x, y);

        // Use PlayerDefinition to ensure consistent setup
        var playerEntity = PlayerDefinition.Create(ctx, action.UserId, position, 0);
        
        System.Console.WriteLine($"[AddPlayerAction] Created Player Entity {playerEntity.Id} for User {action.UserId} at {position}. NextEntityId After: {ctx.State.NextEntityId}");

        // Add Score Component (not in definition yet, specific to gameplay)
        ctx.State.AddComponent(playerEntity, new ScoreComponent { Value = 0 });
    }
}
