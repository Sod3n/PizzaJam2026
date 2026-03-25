using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Physics2D.Systems;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Types;
using FluentAssertions;
using Xunit;

namespace Template.Shared.Tests;

/// <summary>
/// Documents Area2D detection capabilities and limitations.
/// These tests verify what the physics engine can and cannot detect,
/// informing interaction system design decisions.
/// </summary>
[Collection("Sequential")]
public class InteractionZoneTests
{
    private (EntityWorld state, GameLoop gameLoop) CreateWorld()
    {
        ServiceLocator.Reset();
        ServiceLocator.RegisterAssembly(typeof(EntityWorld).Assembly);
        ServiceLocator.RegisterAssembly(typeof(Transform2D).Assembly);
        ServiceLocator.RegisterAssembly(typeof(Area2D).Assembly);

        var state = new EntityWorld();
        var dispatcher = new Dispatcher();
        var scheduler = new ActionScheduler();
        var gameLoop = new GameLoop(state, dispatcher, scheduler);
        gameLoop.SetTickRate(60);

        gameLoop.Simulation.SystemRunner.EnableSystem(new TransformSystem());
        gameLoop.Simulation.SystemRunner.EnableSystem(new RapierPhysicsSystem());

        return (state, gameLoop);
    }

    [Fact]
    public void Area2D_ShouldDetect_RigidBody2D()
    {
        var (state, gameLoop) = CreateWorld();

        var zone = state.CreateEntity();
        state.AddComponent(zone, new Transform2D(Vector2.Zero, 0, Vector2.One));
        state.AddComponent(zone, CollisionShape2D.CreateCircle(2.0f));
        state.AddComponent(zone, new Area2D
        {
            Monitoring = true,
            CollisionLayer = 0,
            CollisionMask = 1
        });

        var target = state.CreateEntity();
        state.AddComponent(target, new Transform2D(new Vector2(1, 0), 0, Vector2.One));
        state.AddComponent(target, CollisionShape2D.CreateCircle(0.5f));
        var rb = RigidBody2D.Default;
        rb.CollisionLayer = 1;
        state.AddComponent(target, rb);

        gameLoop.RunSingleTick();

        var area = state.GetComponent<Area2D>(zone);
        area.HasOverlappingBodies.Should().BeTrue();
        area.OverlappingEntities.Count.Should().Be(1);
        area.OverlappingEntities[0].Should().Be(target.Id);
    }

    [Fact]
    public void Area2D_ShouldDetect_CharacterBody2D()
    {
        var (state, gameLoop) = CreateWorld();

        var zone = state.CreateEntity();
        state.AddComponent(zone, new Transform2D(Vector2.Zero, 0, Vector2.One));
        state.AddComponent(zone, CollisionShape2D.CreateCircle(2.0f));
        state.AddComponent(zone, new Area2D
        {
            Monitoring = true,
            CollisionLayer = 0,
            CollisionMask = 1
        });

        var cow = state.CreateEntity();
        state.AddComponent(cow, new Transform2D(new Vector2(1, 0), 0, Vector2.One));
        state.AddComponent(cow, CollisionShape2D.CreateCircle(0.5f));
        var body = CharacterBody2D.Default;
        body.CollisionLayer = 1;
        body.CollisionMask = 1;
        state.AddComponent(cow, body);

        gameLoop.RunSingleTick();

        var area = state.GetComponent<Area2D>(zone);
        area.HasOverlappingBodies.Should().BeTrue();
        area.OverlappingEntities.Count.Should().Be(1);
        area.OverlappingEntities[0].Should().Be(cow.Id);
    }

    /// <summary>
    /// Area2D cannot detect other Area2D (sensors), but CAN detect StaticBody2D.
    /// Interactables use StaticBody2D on layer 4 with CollisionMask=0 so they
    /// don't physically block the player but are detectable by the interaction zone.
    /// </summary>
    [Fact]
    public void Area2D_CannotDetect_AnotherArea2D()
    {
        var (state, gameLoop) = CreateWorld();

        var zone = state.CreateEntity();
        state.AddComponent(zone, new Transform2D(Vector2.Zero, 0, Vector2.One));
        state.AddComponent(zone, CollisionShape2D.CreateCircle(2.0f));
        state.AddComponent(zone, new Area2D
        {
            Monitoring = true,
            CollisionLayer = 0,
            CollisionMask = 4
        });

        var grass = state.CreateEntity();
        state.AddComponent(grass, new Transform2D(new Vector2(1, 0), 0, Vector2.One));
        state.AddComponent(grass, CollisionShape2D.CreateCircle(1.0f));
        state.AddComponent(grass, new Area2D
        {
            Monitoring = true,
            Monitorable = true,
            CollisionLayer = 4,
            CollisionMask = 0
        });

        gameLoop.RunSingleTick();

        var area = state.GetComponent<Area2D>(zone);
        area.HasOverlappingBodies.Should().BeFalse("Sensors cannot detect other sensors in Rapier");
    }

    /// <summary>
    /// Area2D detects StaticBody2D on layer 4 — the actual interactable setup.
    /// </summary>
    [Fact]
    public void Area2D_ShouldDetect_StaticBody2D()
    {
        var (state, gameLoop) = CreateWorld();

        var zone = state.CreateEntity();
        state.AddComponent(zone, new Transform2D(Vector2.Zero, 0, Vector2.One));
        state.AddComponent(zone, CollisionShape2D.CreateCircle(2.0f));
        state.AddComponent(zone, new Area2D
        {
            Monitoring = true,
            CollisionLayer = 8,  // Zone layer
            CollisionMask = 4    // Detect interactables
        });

        // Grass-like interactable: StaticBody2D on layer 4, mask 0
        var grass = state.CreateEntity();
        state.AddComponent(grass, new Transform2D(new Vector2(1, 0), 0, Vector2.One));
        state.AddComponent(grass, CollisionShape2D.CreateCircle(1.0f));
        // CollisionMask must include zone's layer (8) — Rapier uses bidirectional group filtering
        var body = new StaticBody2D();
        body.CollisionLayer = 4;
        body.CollisionMask = 8;
        state.AddComponent(grass, body);

        gameLoop.RunSingleTick();

        var area = state.GetComponent<Area2D>(zone);
        area.HasOverlappingBodies.Should().BeTrue("Area2D should detect StaticBody2D");
        area.OverlappingEntities.Count.Should().Be(1);
        area.OverlappingEntities[0].Should().Be(grass.Id);
    }

    [Fact]
    public void Area2D_ShouldNotDetect_EntitiesOutOfRange()
    {
        var (state, gameLoop) = CreateWorld();

        var zone = state.CreateEntity();
        state.AddComponent(zone, new Transform2D(Vector2.Zero, 0, Vector2.One));
        state.AddComponent(zone, CollisionShape2D.CreateCircle(2.0f));
        state.AddComponent(zone, new Area2D
        {
            Monitoring = true,
            CollisionLayer = 0,
            CollisionMask = 1
        });

        var target = state.CreateEntity();
        state.AddComponent(target, new Transform2D(new Vector2(10, 0), 0, Vector2.One));
        state.AddComponent(target, CollisionShape2D.CreateCircle(0.5f));
        var rb = RigidBody2D.Default;
        rb.CollisionLayer = 1;
        state.AddComponent(target, rb);

        gameLoop.RunSingleTick();

        var area = state.GetComponent<Area2D>(zone);
        area.HasOverlappingBodies.Should().BeFalse();
    }

    [Fact]
    public void ChildArea2D_ShouldFollowParent_AndDetectNearbyEntities()
    {
        var (state, gameLoop) = CreateWorld();

        var parent = state.CreateEntity();
        state.AddComponent(parent, new Transform2D(new Vector2(10, 0), 0, Vector2.One));
        state.AddComponent(parent, CollisionShape2D.CreateCircle(0.5f));
        var body = CharacterBody2D.Default;
        body.CollisionLayer = 1;
        body.CollisionMask = 1;
        state.AddComponent(parent, body);

        var zone = state.CreateEntity();
        var zoneTransform = new Transform2D(Vector2.Zero, 0, Vector2.One);
        zoneTransform.Parent = parent;
        zoneTransform.DestroyOnUnparent = true;
        state.AddComponent(zone, zoneTransform);
        state.AddComponent(zone, CollisionShape2D.CreateCircle(2.0f));
        state.AddComponent(zone, new Area2D
        {
            Monitoring = true,
            CollisionLayer = 0,
            CollisionMask = 1
        });

        var target = state.CreateEntity();
        state.AddComponent(target, new Transform2D(new Vector2(11, 0), 0, Vector2.One));
        state.AddComponent(target, CollisionShape2D.CreateCircle(0.5f));
        var rb = RigidBody2D.Default;
        rb.CollisionLayer = 1;
        state.AddComponent(target, rb);

        gameLoop.RunSingleTick();

        var zoneT = state.GetComponent<Transform2D>(zone);
        ((float)zoneT.GlobalPosition.X).Should().BeApproximately(10f, 0.1f);

        var area = state.GetComponent<Area2D>(zone);
        area.HasOverlappingBodies.Should().BeTrue("Child area should detect entities near parent");
        // Note: zone also detects its parent (both on layer 1)
        area.OverlappingEntities.Count.Should().BeGreaterOrEqualTo(1);
    }
}
