using System.Reflection;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Factories;
using Template.Shared.Systems;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

[Collection("Sequential")]
public class SwarmFollowTests
{
    private readonly ITestOutputHelper _output;

    public SwarmFollowTests(ITestOutputHelper output) => _output = output;

    private Game CreateGame()
    {
        ServiceLocator.Reset();
        SwarmFollow.Reset();
        var field = typeof(TemplateGameFactory).GetField("_appInitialized", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, false);
        return TemplateGameFactory.CreateGame(tickRate: 60);
    }

    private Entity SpawnPlayer(Game game, System.Guid userId, Vector2 pos)
    {
        var ctx = new Context(game.State, Entity.Null, null!);
        return PlayerDefinition.Create(ctx, userId, pos, 0);
    }

    private void RunTicks(Game game, int count)
    {
        for (int i = 0; i < count; i++)
            game.Loop.RunSingleTick();
    }

    // ─── Following Tests ───

    [Fact]
    public void Followers_ShouldConvergeOnPlayer()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId, new Vector2(-40, -40));
        var ctx = new Context(game.State, Entity.Null, null!);

        // Spawn 4 helpers at various distances (>10 units to be outside idle range)
        var h1 = HelperDefinition.Create(ctx, new Vector2(-40, -25), HelperType.Assistant, player);
        var h2 = HelperDefinition.Create(ctx, new Vector2(-25, -40), HelperType.Assistant, player);
        var h3 = HelperDefinition.Create(ctx, new Vector2(-55, -40), HelperType.Assistant, player);
        var h4 = HelperDefinition.Create(ctx, new Vector2(-40, -55), HelperType.Assistant, player);

        RunTicks(game, 180); // 3 seconds

        var playerPos = game.State.GetComponent<Transform2D>(player).Position;

        foreach (var (label, entity) in new[] { ("h1", h1), ("h2", h2), ("h3", h3), ("h4", h4) })
        {
            var pos = game.State.GetComponent<Transform2D>(entity).Position;
            var dist = Vector2.Distance(pos, playerPos);
            _output.WriteLine($"{label}: pos=({pos.X:F1},{pos.Y:F1}) dist={dist:F1}");
            ((float)dist).Should().BeLessThan(8f, $"{label} should be near player after 2 seconds");
        }
    }

    [Fact]
    public void Followers_ShouldFollowMovingPlayer()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId, new Vector2(-40, -40));
        var ctx = new Context(game.State, Entity.Null, null!);

        var helper = HelperDefinition.Create(ctx, new Vector2(-40, -38), HelperType.Assistant, player);

        // Let helper settle near player
        RunTicks(game, 30);

        // Move player to a new position (set velocity so followers detect movement)
        ref var pt = ref game.State.GetComponent<Transform2D>(player);
        pt.Position = new Vector2(-20, -40);
        ref var pb = ref game.State.GetComponent<CharacterBody2D>(player);
        pb.Velocity = new Vector2(5, 0);

        RunTicks(game, 90);

        // Stop player, let followers catch up
        pb = ref game.State.GetComponent<CharacterBody2D>(player);
        pb.Velocity = Vector2.Zero;
        RunTicks(game, 60);

        var playerPos = game.State.GetComponent<Transform2D>(player).Position;
        var helperPos = game.State.GetComponent<Transform2D>(helper).Position;
        var dist = Vector2.Distance(helperPos, playerPos);

        _output.WriteLine($"Player: ({playerPos.X:F1},{playerPos.Y:F1})");
        _output.WriteLine($"Helper: ({helperPos.X:F1},{helperPos.Y:F1})");
        _output.WriteLine($"Distance: {dist:F1}");

        ((float)dist).Should().BeLessThan(8f, "Helper should have followed the player to the new position");
        ((float)helperPos.X).Should().BeGreaterThan(-30f, "Helper should have moved toward player's new X position");
    }

    // ─── No Jitter Tests ───

    [Fact]
    public void Followers_ShouldNotJitterWhenIdle()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId, new Vector2(-40, -40));
        var ctx = new Context(game.State, Entity.Null, null!);

        var helper = HelperDefinition.Create(ctx, new Vector2(-40, -38), HelperType.Assistant, player);

        // Let helper settle
        RunTicks(game, 120);

        // Record position over 60 ticks
        var positions = new System.Collections.Generic.List<(float x, float y)>();
        for (int i = 0; i < 60; i++)
        {
            game.Loop.RunSingleTick();
            var pos = game.State.GetComponent<Transform2D>(helper).Position;
            positions.Add(((float)pos.X, (float)pos.Y));
        }

        // Calculate max displacement from average position
        float avgX = positions.Average(p => p.x);
        float avgY = positions.Average(p => p.y);
        float maxDisplacement = positions.Max(p =>
        {
            float dx = p.x - avgX;
            float dy = p.y - avgY;
            return (float)System.Math.Sqrt(dx * dx + dy * dy);
        });

        _output.WriteLine($"Average pos: ({avgX:F2},{avgY:F2})");
        _output.WriteLine($"Max displacement from average: {maxDisplacement:F3}");

        maxDisplacement.Should().BeLessThan(0.5f, "Helper should not jitter when idle near player");
    }

    [Fact]
    public void MultipleFollowers_ShouldNotJitterWhenIdle()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId, new Vector2(-40, -40));
        var ctx = new Context(game.State, Entity.Null, null!);

        // Spawn a crowd
        var helpers = new Entity[6];
        for (int i = 0; i < helpers.Length; i++)
            helpers[i] = HelperDefinition.Create(ctx, new Vector2(-40 + i, -38), HelperType.Assistant, player);

        // Also add a cow
        var cow = CowDefinition.Create(ctx, new Vector2(-40, -42));
        ref var cowComp = ref game.State.GetComponent<CowComponent>(cow);
        cowComp.FollowingPlayer = player;
        cowComp.FollowTarget = player;
        ref var ps = ref game.State.GetComponent<PlayerStateComponent>(player);
        ps.FollowingCow = cow;

        // Let everyone settle
        RunTicks(game, 180);

        // Measure jitter over 60 ticks for each entity
        float worstJitter = 0;
        string worstLabel = "";

        foreach (var (label, entity) in helpers.Select((e, i) => ($"helper{i}", e)).Append(("cow", cow)))
        {
            var positions = new System.Collections.Generic.List<(float x, float y)>();
            for (int i = 0; i < 60; i++)
            {
                game.Loop.RunSingleTick();
                var pos = game.State.GetComponent<Transform2D>(entity).Position;
                positions.Add(((float)pos.X, (float)pos.Y));
            }

            float avgX = positions.Average(p => p.x);
            float avgY = positions.Average(p => p.y);
            float maxDisp = positions.Max(p =>
            {
                float dx = p.x - avgX;
                float dy = p.y - avgY;
                return (float)System.Math.Sqrt(dx * dx + dy * dy);
            });

            _output.WriteLine($"{label}: avg=({avgX:F1},{avgY:F1}) jitter={maxDisp:F3}");

            if (maxDisp > worstJitter)
            {
                worstJitter = maxDisp;
                worstLabel = label;
            }
        }

        _output.WriteLine($"\nWorst jitter: {worstLabel} = {worstJitter:F3}");
        worstJitter.Should().BeLessThan(1f, "No follower should jitter more than 1 unit when crowd is idle");
    }

    // ─── Separation Tests ───

    [Fact]
    public void Followers_ShouldNotOverlap()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId, new Vector2(-40, -40));
        var ctx = new Context(game.State, Entity.Null, null!);

        // Spawn helpers all at the same position
        var helpers = new Entity[5];
        for (int i = 0; i < helpers.Length; i++)
            helpers[i] = HelperDefinition.Create(ctx, new Vector2(-40, -38), HelperType.Assistant, player);

        RunTicks(game, 600);

        // Check that no two helpers are stacked (within 0.2 units)
        int overlaps = 0;
        for (int i = 0; i < helpers.Length; i++)
        {
            var posA = game.State.GetComponent<Transform2D>(helpers[i]).Position;
            for (int j = i + 1; j < helpers.Length; j++)
            {
                var posB = game.State.GetComponent<Transform2D>(helpers[j]).Position;
                var dist = Vector2.Distance(posA, posB);
                if ((float)dist < 0.2f)
                    overlaps++;
                _output.WriteLine($"h{i}-h{j}: dist={dist:F2}");
            }
        }

        _output.WriteLine($"\nOverlapping pairs: {overlaps}");
        overlaps.Should().Be(0, "Followers spawned at same position should separate via ORCA");
    }

    // ─── Obstacle Avoidance Tests ───

    [Fact]
    public void Follower_ShouldReachPlayerAroundObstacle()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId, new Vector2(-40, -40));
        var ctx = new Context(game.State, Entity.Null, null!);

        // Place a wall between helper start and player
        var wall = game.State.CreateEntity();
        game.State.AddComponent(wall, new Transform2D(new Vector2(-40, -35), 0, Vector2.One));
        game.State.AddComponent(wall, new StaticBody2D());
        game.State.AddComponent(wall, CollisionShape2D.CreateRectangle(new Vector2(10, 1)));
        game.State.AddComponent(wall, new WallComponent());

        // Force nav mesh rebake
        foreach (var e in game.State.Filter<Deterministic.GameFramework.Navigation2D.Components.NavigationWorld2D>())
        {
            ref var nw = ref game.State.GetComponent<Deterministic.GameFramework.Navigation2D.Components.NavigationWorld2D>(e);
            nw.ForceBake = true;
        }
        RunTicks(game, 5);

        // Spawn helper on the other side of the wall (>10 units from player)
        var helper = HelperDefinition.Create(ctx, new Vector2(-40, -25), HelperType.Assistant, player);

        var startPos = game.State.GetComponent<Transform2D>(helper).Position;
        _output.WriteLine($"Start: ({startPos.X:F1},{startPos.Y:F1})");
        _output.WriteLine($"Player: (-40,-40), Wall at (-40,-35)");

        RunTicks(game, 300); // 5 seconds

        var endPos = game.State.GetComponent<Transform2D>(helper).Position;
        var playerPos = game.State.GetComponent<Transform2D>(player).Position;
        var dist = Vector2.Distance(endPos, playerPos);

        _output.WriteLine($"End: ({endPos.X:F1},{endPos.Y:F1}) dist={dist:F1}");

        ((float)dist).Should().BeLessThan(10f, "Helper should navigate around the wall to reach the player");
        ((float)endPos.Y).Should().BeLessThan(-35f, "Helper should have moved past the wall toward the player");
    }

    // ─── Velocity Smoothness Test ───

    [Fact]
    public void Follower_VelocityShouldBeSmoothDuringMovement()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId, new Vector2(-40, -40));
        var ctx = new Context(game.State, Entity.Null, null!);

        // Start helper far away so it needs to move
        var helper = HelperDefinition.Create(ctx, new Vector2(-40, -20), HelperType.Assistant, player);

        // Record velocity over time
        var velocities = new System.Collections.Generic.List<(float vx, float vy)>();
        for (int i = 0; i < 120; i++)
        {
            game.Loop.RunSingleTick();
            var body = game.State.GetComponent<CharacterBody2D>(helper);
            velocities.Add(((float)body.Velocity.X, (float)body.Velocity.Y));
        }

        // Measure velocity change between consecutive frames
        float maxAccel = 0;
        for (int i = 1; i < velocities.Count; i++)
        {
            float dvx = velocities[i].vx - velocities[i - 1].vx;
            float dvy = velocities[i].vy - velocities[i - 1].vy;
            float accel = (float)System.Math.Sqrt(dvx * dvx + dvy * dvy);
            if (accel > maxAccel)
                maxAccel = accel;
        }

        _output.WriteLine($"Max frame-to-frame velocity change: {maxAccel:F3}");
        _output.WriteLine($"First velocity: ({velocities[0].vx:F2},{velocities[0].vy:F2})");
        _output.WriteLine($"Last velocity: ({velocities[^1].vx:F2},{velocities[^1].vy:F2})");

        // With velocity lerp, max acceleration should be bounded
        // Without lerp, instant velocity changes would be ~8 units/s
        maxAccel.Should().BeLessThan(3f, "Velocity lerp should prevent sharp acceleration changes");
    }
}
