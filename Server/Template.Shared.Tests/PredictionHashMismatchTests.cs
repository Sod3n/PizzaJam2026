using System;
using System.Reflection;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
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
/// Reproduces the hash mismatch caused by client-side prediction with network delay.
///
/// The scenario:
///   1. Client predicts an action at tick 105
///   2. Server hasn't received it yet
///   3. Server computes hash at tick 110 (without the action)
///   4. Client computes hash at tick 110 (WITH the action)
///   5. Mismatch! → full state resync → teleport
///
/// This is not a determinism bug — both sides are correct for their view of the world.
/// The fix must tolerate this transient mismatch without triggering a full resync.
/// </summary>
[Collection("Sequential")]
public class PredictionHashMismatchTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public PredictionHashMismatchTests(ITestOutputHelper output)
    {
        _output = output;
        ILogger.SetLogger(new XunitLogger(output));
    }

    public void Dispose() => ILogger.SetLogger(null!);

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
    /// Simulates the exact production scenario:
    /// - Client and server run in sync
    /// - Client predicts an action (applies locally)
    /// - Server hasn't received it yet (delayed by simulated RTT)
    /// - Hash check happens between prediction and server receiving the action
    /// - After the server receives and rollbacks, the NEXT hash check should match
    /// </summary>
    [Theory]
    [InlineData(5, 18, "300ms RTT, 5-tick delay")]
    [InlineData(5, 10, "167ms RTT, 5-tick delay")]
    [InlineData(5, 30, "500ms RTT, 5-tick delay")]
    public void PredictionDuringHashCheck_ShouldEventuallyConverge(
        int tickDelay, int networkDelayTicks, string label)
    {
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        // Run both to tick 100 (stable state)
        for (int i = 0; i < 100; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Verify in sync
        HashGame(server).Should().Be(HashGame(client), "should be in sync before prediction");

        // --- Client predicts: player moves right ---
        long clientTick = client.Loop.CurrentTick;
        long executeTick = clientTick + tickDelay;
        var moveRight = new SetMoveDirectionAction(new Vector2(1, 0), new Float(15));

        // Client applies locally (prediction)
        client.Loop.ScheduleOnTick(executeTick, moveRight, clientPlayer);

        // Server does NOT have the action yet (in-flight)

        // Run both past the execute tick
        int ticksToRun = tickDelay + 5; // Run a bit past execution
        for (int i = 0; i < ticksToRun; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // --- Hash check happens NOW (server hasn't received the action) ---
        var serverHash = HashGame(server);
        var clientHash = HashGame(client);
        bool matchBeforeDelivery = serverHash == clientHash;
        _output.WriteLine($"[{label}] Hash before action delivery: match={matchBeforeDelivery}");
        _output.WriteLine($"  Server: {serverHash}");
        _output.WriteLine($"  Client: {clientHash}");

        // This SHOULD mismatch — client has predicted movement, server doesn't
        matchBeforeDelivery.Should().BeFalse(
            "client predicted an action the server hasn't seen — hashes should differ");

        // --- Network delivers the action to server (after delay) ---
        server.Loop.ScheduleOnTick(executeTick, moveRight, serverPlayer);

        // Run both forward — server will rollback and resimulate
        for (int i = 0; i < networkDelayTicks; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // --- Second hash check — should now match ---
        var serverHash2 = HashGame(server);
        var clientHash2 = HashGame(client);
        bool matchAfterDelivery = serverHash2 == clientHash2;
        _output.WriteLine($"[{label}] Hash after action delivery + resim: match={matchAfterDelivery}");
        _output.WriteLine($"  Server: {serverHash2}");
        _output.WriteLine($"  Client: {clientHash2}");

        matchAfterDelivery.Should().BeTrue(
            "after server receives the action and rollbacks, both should converge");
    }

    /// <summary>
    /// Simulates continuous input (player holding a key) with network delay.
    /// Actions arrive at the server in bursts, causing repeated rollbacks.
    /// Checks that despite the constant prediction/rollback cycle, the state
    /// converges after each delivery batch.
    /// </summary>
    [Theory]
    [InlineData(18, "300ms")]
    [InlineData(30, "500ms")]
    public void ContinuousInput_WithDelay_ShouldConvergeAfterDelivery(int delayTicks, string label)
    {
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        // Run to stable state
        for (int i = 0; i < 100; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Simulate: client sends actions every 30 ticks (direction changes)
        // Server receives them `delayTicks` later
        var actions = new (int sendTick, Vector2 dir)[]
        {
            (100, new Vector2(1, 0)),
            (130, new Vector2(0, 1)),
            (160, Vector2.Zero),
            (200, new Vector2(-1, 0)),
            (230, Vector2.Zero),
        };

        // Queue of pending deliveries: (deliveryTick, executeTick, action)
        var pendingDeliveries = new System.Collections.Generic.Queue<(int deliveryTick, long executeTick, SetMoveDirectionAction action)>();

        int convergenceCount = 0;
        int divergenceCount = 0;

        for (int tick = 100; tick < 350; tick++)
        {
            // Client sends actions
            foreach (var a in actions)
            {
                if (tick == a.sendTick)
                {
                    long execTick = client.Loop.CurrentTick + 5;
                    var action = new SetMoveDirectionAction(a.dir, a.dir == Vector2.Zero ? new Float(0) : new Float(15));

                    // Client predicts immediately
                    client.Loop.ScheduleOnTick(execTick, action, clientPlayer);

                    // Queue for delayed delivery to server
                    pendingDeliveries.Enqueue((tick + delayTicks, execTick, action));
                }
            }

            // Deliver actions that have arrived at the server
            while (pendingDeliveries.Count > 0 && pendingDeliveries.Peek().deliveryTick <= tick)
            {
                var delivery = pendingDeliveries.Dequeue();
                server.Loop.ScheduleOnTick(delivery.executeTick, delivery.action, serverPlayer);
            }

            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();

            // Check hash every 60 ticks
            if (tick % 60 == 0 && tick > 100)
            {
                var sh = HashGame(server);
                var ch = HashGame(client);
                bool match = sh == ch;
                _output.WriteLine($"  Tick {tick}: {(match ? "MATCH" : "DIVERGED")} (pending: {pendingDeliveries.Count})");

                if (match) convergenceCount++;
                else divergenceCount++;
            }
        }

        _output.WriteLine($"[{label}] Converged: {convergenceCount}, Diverged: {divergenceCount}");

        // After all deliveries complete (tick 350, last action sent at 230 + delay),
        // the final state should match
        // Drain remaining deliveries
        while (pendingDeliveries.Count > 0)
        {
            var delivery = pendingDeliveries.Dequeue();
            server.Loop.ScheduleOnTick(delivery.executeTick, delivery.action, serverPlayer);
        }

        for (int i = 0; i < delayTicks + 10; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        var finalServerHash = HashGame(server);
        var finalClientHash = HashGame(client);
        _output.WriteLine($"[{label}] Final: {(finalServerHash == finalClientHash ? "MATCH" : "DIVERGED")}");

        finalServerHash.Should().Be(finalClientHash,
            "after all actions are delivered and processed, state should converge");
    }
}
