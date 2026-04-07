using System;
using System.Reflection;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Factories;
using Template.Shared.Features.Movement;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

/// <summary>
/// Reproduces the player position desync observed in production logs:
/// After a full state sync, Entity 210 (Player) Transform2D and CharacterBody2D
/// diverge between client and server within ~60 ticks. The client retains a small
/// residual velocity while the server has zero, causing position drift.
/// </summary>
[Collection("Sequential")]
public class PlayerDesyncTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public PlayerDesyncTests(ITestOutputHelper output)
    {
        _output = output;
        ILogger.SetLogger(new XunitLogger(output));
    }

    public void Dispose()
    {
        ILogger.SetLogger(null!);
    }

    private class XunitLogger : ILogger
    {
        private readonly ITestOutputHelper _out;
        public XunitLogger(ITestOutputHelper o) => _out = o;
        public void _Log(string message) { try { _out.WriteLine(message); } catch { } }
        public void _LogWarning(string message) { try { _out.WriteLine($"[WARN] {message}"); } catch { } }
        public void _LogError(string message) { try { _out.WriteLine($"[ERROR] {message}"); } catch { } }
    }

    private static readonly object _createLock = new();
    private static bool _servicesReady;

    private void EnsureServicesInitialized()
    {
        if (_servicesReady) return;
        ServiceLocator.Reset();
        var field = typeof(TemplateGameFactory).GetField("_appInitialized", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, false);
        TemplateGameFactory.CreateGame(tickRate: 60);
        _servicesReady = true;
    }

    private Game CreateGame()
    {
        lock (_createLock)
        {
            EnsureServicesInitialized();
            return TemplateGameFactory.CreateGame(tickRate: 60);
        }
    }

    private Entity AddPlayer(Game game, Guid userId)
    {
        Entity worldEntity = Entity.Null;
        foreach (var e in game.State.Filter<World>())
        { worldEntity = e; break; }

        game.State.AddComponent(worldEntity, new AddPlayerAction(userId));
        game.Dispatcher.Update(game.State);
        game.Loop.Simulation.SystemRunner.Update(game.State);

        foreach (var e in game.State.Filter<PlayerEntity>())
        {
            if (game.State.GetComponent<PlayerEntity>(e).UserId == userId)
                return e;
        }
        return Entity.Null;
    }

    private static Guid HashGame(Game game)
    {
        byte[] data = StateSerializer.Serialize(game.State);
        return StateHasher.Hash(data);
    }

    /// <summary>
    /// Replicates the exact desync pattern from production logs:
    /// Two independent game instances (simulating server and client) run with
    /// identical inputs. Player moves, then stops. After stopping, the two
    /// instances should have identical state, but CharacterBody2D residual
    /// velocity causes position drift.
    /// </summary>
    [Fact]
    public void TwoInstances_SameInputs_ShouldProduceSameState()
    {
        // Create two independent game instances (server + client)
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        serverPlayer.Should().NotBe(Entity.Null, "server player should be created");
        clientPlayer.Should().NotBe(Entity.Null, "client player should be created");

        // Verify initial state matches
        var serverHash0 = HashGame(server);
        var clientHash0 = HashGame(client);
        _output.WriteLine($"Initial hashes - Server: {serverHash0}, Client: {clientHash0}");
        serverHash0.Should().Be(clientHash0, "initial state should match");

        // Run both for 10 ticks (idle)
        for (int i = 0; i < 10; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        var serverHash1 = HashGame(server);
        var clientHash1 = HashGame(client);
        _output.WriteLine($"After 10 idle ticks - Server: {serverHash1}, Client: {clientHash1}");
        serverHash1.Should().Be(clientHash1, "state should match after idle ticks");

        // Apply movement: player walks right
        var moveRight = new SetMoveDirectionAction(new Vector2(1, 0), new Float(15));
        server.State.AddComponent(serverPlayer, moveRight);
        client.State.AddComponent(clientPlayer, moveRight);

        server.Dispatcher.Update(server.State);
        client.Dispatcher.Update(client.State);

        // Run 30 ticks while moving
        for (int i = 0; i < 30; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        var serverHash2 = HashGame(server);
        var clientHash2 = HashGame(client);
        _output.WriteLine($"After 30 moving ticks - Server: {serverHash2}, Client: {clientHash2}");

        // Log player positions
        LogPlayerState("Server", server, serverPlayer);
        LogPlayerState("Client", client, clientPlayer);

        serverHash2.Should().Be(clientHash2, "state should match while moving");

        // Stop movement
        var stop = new SetMoveDirectionAction(Vector2.Zero, new Float(0));
        server.State.AddComponent(serverPlayer, stop);
        client.State.AddComponent(clientPlayer, stop);

        server.Dispatcher.Update(server.State);
        client.Dispatcher.Update(client.State);

        // Run 60 more ticks (the desync window from logs)
        for (int tick = 0; tick < 60; tick++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();

            if (tick % 10 == 9)
            {
                var sh = HashGame(server);
                var ch = HashGame(client);
                bool match = sh == ch;
                _output.WriteLine($"Tick +{tick + 1} after stop - Match: {match}");

                if (!match)
                {
                    LogPlayerState("Server", server, serverPlayer);
                    LogPlayerState("Client", client, clientPlayer);
                }
            }
        }

        var serverHash3 = HashGame(server);
        var clientHash3 = HashGame(client);
        _output.WriteLine($"After 60 post-stop ticks - Server: {serverHash3}, Client: {clientHash3}");
        LogPlayerState("Server", server, serverPlayer);
        LogPlayerState("Client", client, clientPlayer);

        serverHash3.Should().Be(clientHash3,
            "state should match after player stops — residual velocity must not cause drift");
    }

    /// <summary>
    /// Simulates the production scenario more closely: player moves in multiple
    /// directions with start/stop cycles, checking hash at every 60-tick interval
    /// (matching the production hash check frequency).
    /// </summary>
    [Fact]
    public void TwoInstances_MoveStartStop_HashEvery60Ticks()
    {
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        int desyncCount = 0;

        // Simulate 600 ticks (10 seconds) with movement patterns
        var movements = new (int startTick, int stopTick, Vector2 direction)[]
        {
            (0, 30, new Vector2(1, 0)),       // right
            (60, 90, new Vector2(0, 1)),       // down
            (120, 180, new Vector2(-1, -1)),   // up-left (diagonal)
            (200, 250, new Vector2(0.5f, 0.8f)), // angled
            (300, 330, new Vector2(1, 0)),     // right again
            (400, 450, new Vector2(-1, 0)),    // left
        };

        int moveIdx = 0;
        bool isMoving = false;

        for (int tick = 0; tick < 600; tick++)
        {
            // Check if we need to start/stop movement
            if (moveIdx < movements.Length)
            {
                var m = movements[moveIdx];
                if (tick == m.startTick)
                {
                    var dir = m.direction;
                    // Normalize like InputManager does
                    float len = (float)System.Math.Sqrt((float)(dir.X * dir.X + dir.Y * dir.Y));
                    if (len > 0.001f) dir = new Vector2(dir.X / len, dir.Y / len);

                    var move = new SetMoveDirectionAction(dir, new Float(15));
                    server.State.AddComponent(serverPlayer, move);
                    client.State.AddComponent(clientPlayer, move);
                    server.Dispatcher.Update(server.State);
                    client.Dispatcher.Update(client.State);
                    isMoving = true;
                }
                else if (tick == m.stopTick)
                {
                    var stop = new SetMoveDirectionAction(Vector2.Zero, new Float(0));
                    server.State.AddComponent(serverPlayer, stop);
                    client.State.AddComponent(clientPlayer, stop);
                    server.Dispatcher.Update(server.State);
                    client.Dispatcher.Update(client.State);
                    isMoving = false;
                    moveIdx++;
                }
            }

            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();

            // Check hash every 60 ticks (matching production frequency)
            if ((tick + 1) % 60 == 0)
            {
                var sh = HashGame(server);
                var ch = HashGame(client);
                bool match = sh == ch;
                _output.WriteLine($"Tick {tick + 1}: Match={match} Moving={isMoving}");

                if (!match)
                {
                    desyncCount++;
                    LogPlayerState("Server", server, serverPlayer);
                    LogPlayerState("Client", client, clientPlayer);

                    // Log diff
                    byte[] serverData = StateSerializer.Serialize(server.State);
                    byte[] clientData = StateSerializer.Serialize(client.State);
                    StateDumper.LogStateDiff("DesyncTest", tick + 1, clientData, serverData);
                }
            }
        }

        _output.WriteLine($"Total desyncs: {desyncCount} out of 10 checkpoints");
        desyncCount.Should().Be(0, "no desyncs should occur between identical game instances with identical inputs");
    }

    /// <summary>
    /// Specifically tests that CharacterBody2D velocity is zeroed after stop action,
    /// and no residual velocity remains to cause position drift.
    /// This is the exact bug pattern from the logs: LOCAL has small velocity,
    /// SERVER has zero.
    /// </summary>
    [Fact]
    public void Player_AfterStop_ShouldHaveZeroVelocity()
    {
        var game = CreateGame();
        var userId = Guid.NewGuid();
        var player = AddPlayer(game, userId);

        // Move right for 30 ticks
        var moveRight = new SetMoveDirectionAction(new Vector2(1, 0), new Float(15));
        game.State.AddComponent(player, moveRight);
        game.Dispatcher.Update(game.State);

        for (int i = 0; i < 30; i++)
            game.Loop.RunSingleTick();

        ref var bodyMoving = ref game.State.GetComponent<CharacterBody2D>(player);
        _output.WriteLine($"Velocity while moving: ({bodyMoving.Velocity.X}, {bodyMoving.Velocity.Y})");
        _output.WriteLine($"RealVelocity while moving: ({bodyMoving.RealVelocity.X}, {bodyMoving.RealVelocity.Y})");

        // Stop
        var stop = new SetMoveDirectionAction(Vector2.Zero, new Float(0));
        game.State.AddComponent(player, stop);
        game.Dispatcher.Update(game.State);

        // Run a few more ticks
        for (int i = 0; i < 5; i++)
        {
            game.Loop.RunSingleTick();

            ref var body = ref game.State.GetComponent<CharacterBody2D>(player);
            ref var transform = ref game.State.GetComponent<Transform2D>(player);
            _output.WriteLine($"Tick +{i + 1} after stop: Vel=({body.Velocity.X}, {body.Velocity.Y}) " +
                              $"RealVel=({body.RealVelocity.X}, {body.RealVelocity.Y}) " +
                              $"Pos=({transform.Position.X}, {transform.Position.Y})");

            body.Velocity.X.Should().Be(new Float(0), $"X velocity should be zero at tick +{i + 1} after stop");
            body.Velocity.Y.Should().Be(new Float(0), $"Y velocity should be zero at tick +{i + 1} after stop");
        }
    }

    /// <summary>
    /// Tests that a single game instance produces identical state when run twice
    /// from the same starting point with the same player movement sequence.
    /// Uses rollback to replay from a checkpoint.
    /// </summary>
    [Fact]
    public void SingleInstance_ReplayMovement_ShouldMatch()
    {
        var game = CreateGame();
        var userId = Guid.NewGuid();
        var player = AddPlayer(game, userId);

        // Run 20 idle ticks to establish baseline
        for (int i = 0; i < 20; i++)
            game.Loop.RunSingleTick();

        // Save checkpoint
        long checkpointTick = game.Loop.CurrentTick;

        // --- First run: move then stop ---
        var moveRight = new SetMoveDirectionAction(new Vector2(1, 0), new Float(15));
        game.Loop.Schedule(moveRight, player);

        for (int i = 0; i < 30; i++)
            game.Loop.RunSingleTick();

        // Schedule stop at current tick
        var stop = new SetMoveDirectionAction(Vector2.Zero, new Float(0));
        game.Loop.Schedule(stop, player);

        for (int i = 0; i < 30; i++)
            game.Loop.RunSingleTick();

        long targetTick = game.Loop.CurrentTick;
        var firstRunHash = HashGame(game);
        byte[] firstRunData = StateSerializer.Serialize(game.State);
        _output.WriteLine($"First run hash at tick {targetTick}: {firstRunHash}");

        // --- Rollback to checkpoint and replay ---
        bool restored = game.Loop.Simulation.History.Retrieve(checkpointTick, game.State);
        restored.Should().BeTrue("checkpoint should be available in history");

        game.Loop.Simulation.History.DiscardFuture(checkpointTick);
        game.Loop.ForceSetTick(checkpointTick);

        while (game.Loop.CurrentTick < targetTick)
            game.Loop.RunSingleTick();

        var replayHash = HashGame(game);
        _output.WriteLine($"Replay hash at tick {game.Loop.CurrentTick}: {replayHash}");

        if (firstRunHash != replayHash)
        {
            byte[] replayData = StateSerializer.Serialize(game.State);
            StateDumper.LogStateDiff("ReplayMovement", targetTick, replayData, firstRunData);
        }

        firstRunHash.Should().Be(replayHash,
            "replaying the same movement sequence from checkpoint should produce identical state");
    }

    private void LogPlayerState(string label, Game game, Entity player)
    {
        if (!game.State.HasComponent<Transform2D>(player)) return;
        ref var transform = ref game.State.GetComponent<Transform2D>(player);
        ref var body = ref game.State.GetComponent<CharacterBody2D>(player);

        _output.WriteLine($"  [{label}] Pos=({transform.Position.X}, {transform.Position.Y}) " +
                          $"Vel=({body.Velocity.X}, {body.Velocity.Y}) " +
                          $"RealVel=({body.RealVelocity.X}, {body.RealVelocity.Y})");
    }
}
