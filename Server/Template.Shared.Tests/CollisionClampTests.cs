using System;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.Navigation2D.Systems;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Physics2D.Systems;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

/// <summary>
/// Minimal reproduction of "player walks through obstacles".
/// Builds an EntityWorld with one StaticBody2D obstacle and one CharacterBody2D
/// player constrained to the navmesh, drives the player straight at the obstacle,
/// and asserts the player never crosses through it.
/// </summary>
public class CollisionClampTests
{
    private readonly ITestOutputHelper _output;

    private static readonly object _initLock = new();
    private static bool _registered;

    public CollisionClampTests(ITestOutputHelper output)
    {
        _output = output;
        lock (_initLock)
        {
            if (_registered) return;
            ServiceLocator.RegisterAssembly(typeof(EntityWorld).Assembly);
            ServiceLocator.RegisterAssembly(typeof(Transform2D).Assembly);
            ServiceLocator.RegisterAssembly(typeof(StaticBody2D).Assembly);
            ServiceLocator.RegisterAssembly(typeof(NavigationWorld2D).Assembly);
            _registered = true;
        }
    }

    private sealed class TestGameTime : IGameTime
    {
        public long CurrentTick { get; set; }
        public Float FixedDeltaTime { get; } = Float.One / (Float)60;
        public int TickRate => 60;
        public bool IsResimulating => false;
    }

    [Fact]
    public void Player_StraightAtObstacle_DoesNotPassThrough()
    {
        var world = new EntityWorld(reserveCapacity: 64);
        var gameTime = new TestGameTime();
        world.SetCustomData<IGameTime>(gameTime);

        // NavigationWorld config: small bounds, agent radius matches player shape radius.
        var navEntity = world.CreateEntity();
        var navCfg = NavigationWorld2D.Default;
        navCfg.BoundsMin = new Vector2(-20, -20);
        navCfg.BoundsMax = new Vector2(20, 20);
        navCfg.CellSize = (Float)2;
        navCfg.AgentRadius = (Float)0.5f;
        navCfg.ChunkSize = (Float)20;
        navCfg.ObstacleMask = 1;
        navCfg.ForceBake = true;
        world.AddComponent(navEntity, navCfg);

        // Obstacle: 2x2 rectangle at (5,0), layer 1.
        var obstacle = world.CreateEntity();
        world.AddComponent(obstacle, new Transform2D(new Vector2(5, 0), 0, Vector2.One));
        world.AddComponent(obstacle, new StaticBody2D { CollisionLayer = 1, CollisionMask = 1 });
        world.AddComponent(obstacle, CollisionShape2D.CreateRectangle(new Vector2(2, 2)));

        // Player at origin, moving +X at 5 units/sec; agent radius 0.5.
        var player = world.CreateEntity();
        world.AddComponent(player, new Transform2D(Vector2.Zero, 0, Vector2.One));
        var body = CharacterBody2D.Default;
        body.CollisionLayer = 1;
        body.CollisionMask = 1;
        body.Velocity = new Vector2(14, 0); // realistic walk speed
        world.AddComponent(player, body);
        world.AddComponent(player, CollisionShape2D.CreateCircle((Float)0.5f));
        world.AddComponent(player, new NavMeshConstraint());

        var runner = new SystemRunner();
        runner.EnableSystem(new CDTNavigationSystem());
        runner.EnableSystem(new SensorQuerySystem());

        // Run for 2 seconds (120 ticks). Player would travel 10 units unblocked,
        // far past the obstacle's right edge at x=6.
        Float maxX = (Float)0;
        for (int i = 0; i < 120; i++)
        {
            gameTime.CurrentTick = i;
            // Re-assert velocity each tick (input would normally drive this).
            ref var b = ref world.GetComponent<CharacterBody2D>(player);
            b.Velocity = new Vector2(14, 0);

            runner.Update(world);

            var pos = world.GetComponent<Transform2D>(player).Position;
            if (pos.X > maxX) maxX = pos.X;
        }

        var finalPos = world.GetComponent<Transform2D>(player).Position;
        _output.WriteLine($"Final pos = ({(float)finalPos.X:F3}, {(float)finalPos.Y:F3})");
        _output.WriteLine($"Max X reached = {(float)maxX:F3}");

        // Obstacle is rect [4,6] x [-1,1]. With agent radius 0.5, inflated to [3.5,6.5] x [-1.5,1.5].
        // Player center must stay left of x = 3.5 (left edge of inflated obstacle).
        ((float)maxX).Should().BeLessThan(4.0f,
            "player center must not enter the inflated obstacle footprint (left edge x=3.5)");
    }
}
