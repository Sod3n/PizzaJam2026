using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Template.Shared.Definitions;
using System;
using System.Runtime.InteropServices;

namespace Template.Shared.Actions;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
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
        // Perfect overlaps can cause non-deterministic physics resolution
        // ERROR FIX: Guid.GetHashCode() is not stable across processes! Use bytes.
        int seed = BitConverter.ToInt32(action.UserId.ToByteArray(), 0);
        var random = new Deterministic.GameFramework.Types.DeterministicRandom((uint)seed);
        
        // Find a valid spawn position that doesn't overlap with existing players
        var position = FindValidSpawnPosition(ctx, ref random);

        // Use PlayerDefinition to ensure consistent setup
        var playerEntity = PlayerDefinition.Create(ctx, action.UserId, position, 0);
        
        System.Console.WriteLine($"[AddPlayerAction] Created Player Entity {playerEntity.Id} for User {action.UserId} at {position}. NextEntityId After: {ctx.State.NextEntityId}");

        // Add Score Component (not in definition yet, specific to gameplay)
        ctx.State.AddComponent(playerEntity, new ScoreComponent { Value = 0 });

        // Collect unowned cows first, then assign (avoid stale refs during iteration)
        var unownedCows = new System.Collections.Generic.List<Entity>();
        foreach (var cowEntity in ctx.State.Filter<CowComponent>())
        {
            var cow = ctx.State.GetComponent<CowComponent>(cowEntity);
            if (cow.FollowingPlayer == Entity.Null && cow.HouseId == Entity.Null)
                unownedCows.Add(cowEntity);
        }

        Entity lastInChain = Entity.Null;
        for (int i = 0; i < unownedCows.Count; i++)
        {
            var cowEntity = unownedCows[i];
            ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
            cow.FollowingPlayer = playerEntity;

            if (i == 0)
            {
                ref var ps = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
                ps.FollowingCow = cowEntity;
                cow = ref ctx.State.GetComponent<CowComponent>(cowEntity); // re-get after touching other component
                cow.FollowTarget = playerEntity;
            }
            else
            {
                cow.FollowTarget = lastInChain;
            }
            lastInChain = cowEntity;
        }
    }

    private Vector2 FindValidSpawnPosition(Context ctx, ref Deterministic.GameFramework.Types.DeterministicRandom random)
    {
        const int MaxAttempts = 10;
        const float MinDistance = 5f; // Radius is 2, so 4 is touching. 5 is safe.
        const float MinDistanceSq = MinDistance * MinDistance;

        for (int i = 0; i < MaxAttempts; i++)
        {
            // Spawn left of center to avoid landing on the starting land plot
            var x = random.NextInt(-15, -5);
            var y = random.NextInt(-5, 5);
            var candidate = new Vector2(x, y);

            if (!IsPositionOccupied(ctx, candidate, MinDistanceSq))
            {
                return candidate;
            }
        }

        // Fallback if crowded: expand range slightly
        return new Vector2(random.NextInt(-20, 20), random.NextInt(-20, 20));
    }

    private bool IsPositionOccupied(Context ctx, Vector2 position, float minDistanceSq)
    {
        foreach (var entity in ctx.State.Filter<PlayerEntity>())
        {
            if (ctx.State.HasComponent<Transform2D>(entity))
            {
                ref var transform = ref ctx.State.GetComponent<Transform2D>(entity);
                if (Vector2.DistanceSquared(position, transform.Position) < minDistanceSq)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
