using System;
using System.Threading.Tasks;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Factories;
using Template.Shared.Features.Movement;

namespace Template.Client;

public class CollisionTest
{
    public async Task Run()
    {
        Console.WriteLine("=== Physics Collision Test ===");

        var game = TemplateGameFactory.CreateGame(tickRate: 60);
        var ctx = new Context(game.State, Entity.Null, null!);

        // Spawn a house at (5, 0) — should block the player
        var houseEntity = HouseDefinition.Create(ctx, new Vector2(5, 0));
        Console.WriteLine($"House at (5, 0)");

        // Verify house has physics
        Console.WriteLine($"  StaticBody2D: {game.State.HasComponent<StaticBody2D>(houseEntity)}");
        Console.WriteLine($"  CollisionShape2D: {game.State.HasComponent<CollisionShape2D>(houseEntity)}");
        if (game.State.HasComponent<StaticBody2D>(houseEntity))
        {
            var sb = game.State.GetComponent<StaticBody2D>(houseEntity);
            Console.WriteLine($"  Layer={sb.CollisionLayer} Mask={sb.CollisionMask}");
        }

        // Spawn player at origin
        var playerId = System.Guid.NewGuid();
        var playerEntity = PlayerDefinition.Create(ctx, playerId, new Vector2(0, 0), new Float(0));
        Console.WriteLine($"Player at (0, 0)");
        if (game.State.HasComponent<CharacterBody2D>(playerEntity))
        {
            var cb = game.State.GetComponent<CharacterBody2D>(playerEntity);
            Console.WriteLine($"  Layer={cb.CollisionLayer} Mask={cb.CollisionMask}");
        }

        // Start game loop
        _ = game.Loop.Start();

        // Set velocity toward house
        game.Dispatcher.Execute(
            new SetMoveDirectionAction { Direction = new Vector2(1, 0), Speed = 10 },
            game.State, playerEntity);

        // Let physics run for 3 seconds
        await Task.Delay(3000);

        // Stop
        game.Dispatcher.Execute(
            new SetMoveDirectionAction { Direction = Vector2.Zero, Speed = 0 },
            game.State, playerEntity);
        await Task.Delay(100);

        var finalPos = game.State.GetComponent<Transform2D>(playerEntity).Position;
        Console.WriteLine($"\nPlayer final pos: ({(float)finalPos.X:F2}, {(float)finalPos.Y:F2})");
        Console.WriteLine($"Tick: {game.Loop.CurrentTick}");

        if ((float)finalPos.X < 4.0f)
            Console.WriteLine("PASS: Player was blocked by house");
        else if ((float)finalPos.X > 6.0f)
            Console.WriteLine("FAIL: Player walked through house");
        else
            Console.WriteLine("PARTIAL: Player near house, collision may be partial");
    }
}
