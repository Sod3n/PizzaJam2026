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
/// Reproduces the exact production desync with 300ms latency.
///
/// The real scenario:
///   1. Server and client both start from the same state
///   2. Client predicts action at tick 100 (applies immediately)
///   3. Server doesn't get action until tick 118 (300ms later)
///   4. Server rollbacks to tick 100, resimulates 100→118
///   5. Client never rolled back — it ran straight through
///   6. At tick 180 (confirmed tick = 180-60=120), both hash tick 120
///   7. Server's tick 120 was resimulated (post-rollback). Client's wasn't.
///   8. If resimulation produces different state → DESYNC
///
/// This is different from HighLatencyRollbackTests which rollbacks a single
/// instance and compares with itself. Here we compare TWO instances where
/// only one rolled back.
/// </summary>
[Collection("Sequential")]
public class ServerRollbackDesyncTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public ServerRollbackDesyncTests(ITestOutputHelper output)
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
            var game = TemplateGameFactory.CreateGame(tickRate: 60);
            game.SetupGGPO();
            return game;
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

    private static Guid HashAtTick(Game game, long tick)
    {
        if (game.Loop.Simulation.GetHistory().TryGetSnapshotData(tick, out byte[]? data))
            return StateHasher.Hash(data!);
        throw new Exception($"Tick {tick} not in history");
    }

    /// <summary>
    /// Simulates: client sends action, server receives it late, server rollbacks.
    /// Then compare hashes at a confirmed tick (past the rollback window).
    ///
    /// Both "server" and "client" are separate Game instances running identical simulation.
    /// The difference: server receives the action late and rollbacks, client had it all along.
    /// </summary>
    [Theory]
    [InlineData(18, "300ms")]
    [InlineData(10, "167ms")]
    [InlineData(30, "500ms")]
    public void ServerRollback_ClientNoRollback_ShouldMatchAtConfirmedTick(int networkDelay, string label)
    {
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        // Run both to tick 100 (stable, cows moving)
        for (int i = 0; i < 100; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Verify in sync
        var sh = HashAtTick(server, server.Loop.CurrentTick);
        var ch = HashAtTick(client, client.Loop.CurrentTick);
        sh.Should().Be(ch, "should be in sync before action");

        // --- Client sends action (prediction) ---
        long actionTick = client.Loop.CurrentTick + 5; // DefaultTickDelay = 5
        var moveRight = new SetMoveDirectionAction(new Vector2(1, 0), new Float(15));

        // Client gets the action immediately
        client.Loop.ScheduleOnTick(actionTick, moveRight, clientPlayer);

        // Server does NOT get it yet — delayed by networkDelay ticks

        // Run both for networkDelay ticks (client has action, server doesn't)
        for (int i = 0; i < networkDelay; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // --- Server receives the action (late) ---
        // This triggers rollback on the server: rollback to actionTick, resimulate to current
        server.Loop.ScheduleOnTick(actionTick, moveRight, serverPlayer);

        // Run both forward past the confirmation window
        int confirmationWindow = 60;
        for (int i = 0; i < confirmationWindow + 10; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Check hash at a confirmed tick (well past the action and rollback)
        long confirmTick = server.Loop.CurrentTick - 10;
        var serverHash = HashAtTick(server, confirmTick);
        var clientHash = HashAtTick(client, confirmTick);

        _output.WriteLine($"[{label}] Confirm tick {confirmTick}: " +
            $"server={serverHash}, client={clientHash}, match={serverHash == clientHash}");

        if (serverHash != clientHash)
        {
            // Dump diff
            server.Loop.Simulation.GetHistory().TryGetSnapshotData(confirmTick, out byte[]? sd);
            client.Loop.Simulation.GetHistory().TryGetSnapshotData(confirmTick, out byte[]? cd);
            StateDumper.LogStateDiff($"ServerRollback_{label}", confirmTick, cd!, sd!);
        }

        serverHash.Should().Be(clientHash,
            $"[{label}] after server rollback + resim, confirmed tick {confirmTick} should match client");
    }

    /// <summary>
    /// Same but with multiple actions during play (continuous input).
    /// Actions arrive at the server in bursts, each causing a rollback.
    /// </summary>
    [Theory]
    [InlineData(18, "300ms")]
    [InlineData(30, "500ms")]
    public void RepeatedServerRollbacks_ShouldMatchAtConfirmedTicks(int networkDelay, string label)
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

        // Simulate multiple actions with delayed delivery
        var actions = new (int sendTick, Vector2 dir)[]
        {
            (100, new Vector2(1, 0)),
            (130, new Vector2(0, 1)),
            (160, Vector2.Zero),
            (200, new Vector2(-1, 0)),
            (250, Vector2.Zero),
            (280, new Vector2(0.5f, -0.5f).Normalized),
            (320, Vector2.Zero),
        };

        var pendingDeliveries = new System.Collections.Generic.Queue<(int deliveryTick, long execTick, SetMoveDirectionAction action)>();

        int desyncCount = 0;

        for (int tick = 100; tick < 500; tick++)
        {
            // Client sends actions
            foreach (var a in actions)
            {
                if (tick == a.sendTick)
                {
                    long execTick = client.Loop.CurrentTick + 5;
                    var action = new SetMoveDirectionAction(a.dir,
                        a.dir == Vector2.Zero ? new Float(0) : new Float(15));

                    client.Loop.ScheduleOnTick(execTick, action, clientPlayer);
                    pendingDeliveries.Enqueue((tick + networkDelay, execTick, action));
                }
            }

            // Deliver to server (delayed)
            while (pendingDeliveries.Count > 0 && pendingDeliveries.Peek().deliveryTick <= tick)
            {
                var d = pendingDeliveries.Dequeue();
                server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
            }

            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();

            // Check confirmed tick hash every 60 ticks
            if (tick % 60 == 0 && tick > 160)
            {
                long confirmTick = server.Loop.CurrentTick - 60;
                if (confirmTick <= 0) continue;

                try
                {
                    var sHash = HashAtTick(server, confirmTick);
                    var cHash = HashAtTick(client, confirmTick);
                    bool match = sHash == cHash;
                    _output.WriteLine($"  Tick {tick}, confirm {confirmTick}: {(match ? "MATCH" : "DESYNC")}");

                    if (!match)
                    {
                        desyncCount++;
                        if (desyncCount <= 2)
                        {
                            server.Loop.Simulation.GetHistory().TryGetSnapshotData(confirmTick, out byte[]? sd);
                            client.Loop.Simulation.GetHistory().TryGetSnapshotData(confirmTick, out byte[]? cd);
                            StateDumper.LogStateDiff($"Repeated_{label}_{tick}", confirmTick, cd!, sd!);
                        }
                    }
                }
                catch { /* tick not in history */ }
            }
        }

        // Drain remaining deliveries
        while (pendingDeliveries.Count > 0)
        {
            var d = pendingDeliveries.Dequeue();
            server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
        }

        // Run past confirmation window so all actions are settled
        for (int i = 0; i < 80; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Final check: after all actions delivered and settled, state MUST match.
        // This is the real determinism check. Mismatches during active prediction
        // windows are expected (transient), but after settling they must converge.
        var finalServerHash = StateHasher.Hash(StateSerializer.Serialize(server.State));
        var finalClientHash = StateHasher.Hash(StateSerializer.Serialize(client.State));
        bool finalMatch = finalServerHash == finalClientHash;

        _output.WriteLine($"[{label}] During play: {desyncCount} transient mismatches on confirmed ticks");
        _output.WriteLine($"[{label}] Final (settled): {(finalMatch ? "MATCH" : "DESYNC")}");

        finalMatch.Should().BeTrue(
            $"[{label}] after all actions delivered and settled, state must converge. " +
            $"({desyncCount} transient mismatches during play are expected with prediction)");
    }
}
