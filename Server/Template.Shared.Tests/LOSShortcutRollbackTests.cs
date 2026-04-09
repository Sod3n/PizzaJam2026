using System;
using System.Collections.Generic;
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
/// Simulates the full production scenario: a server + two clients in a lobby.
/// Server receives actions late, rollbacks. Both clients predict locally.
/// After settling, all three must have identical state.
/// </summary>
[Collection("Sequential")]
public class LOSShortcutRollbackTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public LOSShortcutRollbackTests(ITestOutputHelper output)
    {
        _output = output;
        ILogger.SetLogger(new XunitLogger(output));
    }

    public void Dispose() => ILogger.SetLogger(null!);

    private class XunitLogger : ILogger
    {
        private readonly ITestOutputHelper _out;
        public XunitLogger(ITestOutputHelper o) => _out = o;
        public void _Log(string msg) { try { _out.WriteLine(msg); } catch { } }
        public void _LogWarning(string msg) { try { _out.WriteLine($"[WARN] {msg}"); } catch { } }
        public void _LogError(string msg) { try { _out.WriteLine($"[ERROR] {msg}"); } catch { } }
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
            if (game.State.GetComponent<PlayerEntity>(e).UserId == userId)
                return e;
        return Entity.Null;
    }

    static Guid HashGame(Game g) => StateHasher.Hash(StateSerializer.Serialize(g.State));

    /// <summary>
    /// Core production scenario: server + client, client sends actions with delay,
    /// server rollbacks. After all actions delivered and settled, both must match.
    ///
    /// This differs from ServerRollbackDesyncTests because here the server also
    /// BROADCASTS the action back to the client (echo), causing the client to
    /// also rollback if the echoed action arrives for a past tick.
    /// </summary>
    [Theory]
    [InlineData(10, "167ms")]
    [InlineData(18, "300ms")]
    [InlineData(30, "500ms")]
    public void ServerRollback_WithEcho_BothShouldConverge(int delay, string label)
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

        HashGame(server).Should().Be(HashGame(client), "should start in sync");

        var actions = new (int sendTick, Vector2 dir)[]
        {
            (100, new Vector2(1, 0)),
            (130, new Vector2(0, 1)),
            (160, Vector2.Zero),
            (200, new Vector2(-1, 0)),
            (240, Vector2.Zero),
            (270, new Vector2(0.5f, -0.8f).Normalized),
            (310, Vector2.Zero),
            (340, new Vector2(-0.3f, 0.9f).Normalized),
            (380, Vector2.Zero),
        };

        // Pending: (deliveryTick, execTick, action)
        // Two queues: client→server (delayed), server→client echo (delayed again)
        var toServer = new Queue<(int deliveryTick, long execTick, SetMoveDirectionAction action)>();
        var toClient = new Queue<(int deliveryTick, long execTick, SetMoveDirectionAction action)>();

        int desyncCount = 0;

        for (int tick = 100; tick < 500; tick++)
        {
            // Client sends actions with prediction
            foreach (var a in actions)
            {
                if (tick == a.sendTick)
                {
                    long execTick = client.Loop.CurrentTick + 5;
                    var action = new SetMoveDirectionAction(a.dir,
                        a.dir == Vector2.Zero ? new Float(0) : new Float(15));

                    // Client predicts immediately
                    client.Loop.ScheduleOnTick(execTick, action, clientPlayer);

                    // Queue for server (delayed)
                    toServer.Enqueue((tick + delay, execTick, action));
                }
            }

            // Server receives delayed actions
            while (toServer.Count > 0 && toServer.Peek().deliveryTick <= tick)
            {
                var d = toServer.Dequeue();
                server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);

                // Server echoes back to client (another delay)
                toClient.Enqueue((tick + delay, d.execTick, d.action));
            }

            // Client receives echo from server
            // The client already has this action (predicted), so it's a duplicate.
            // But if the execTick differs (server bumped it), it causes a rollback.
            while (toClient.Count > 0 && toClient.Peek().deliveryTick <= tick)
            {
                var d = toClient.Dequeue();
                // Client already has this action — scheduler deduplicates.
                // But schedule it anyway to simulate the real flow.
                client.Loop.ScheduleOnTick(d.execTick, d.action, clientPlayer);
            }

            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();

            // Check every 60 ticks
            if (tick % 60 == 0 && tick > 160)
            {
                var sh = HashGame(server);
                var ch = HashGame(client);
                bool match = sh == ch;
                if (!match) desyncCount++;
                _output.WriteLine($"  Tick {tick}: {(match ? "MATCH" : "DESYNC")} (pending: s={toServer.Count} c={toClient.Count})");
            }
        }

        // Drain all
        while (toServer.Count > 0)
        {
            var d = toServer.Dequeue();
            server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
        }
        while (toClient.Count > 0)
        {
            var d = toClient.Dequeue();
            client.Loop.ScheduleOnTick(d.execTick, d.action, clientPlayer);
        }

        // Settle
        for (int i = 0; i < 120; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        var finalServer = HashGame(server);
        var finalClient = HashGame(client);
        _output.WriteLine($"[{label}] Transient mismatches: {desyncCount}");
        _output.WriteLine($"[{label}] Final: server={finalServer} client={finalClient} match={finalServer == finalClient}");

        if (finalServer != finalClient)
        {
            byte[] sd = StateSerializer.Serialize(server.State);
            byte[] cd = StateSerializer.Serialize(client.State);
            StateDumper.LogStateDiff($"Echo_{label}", server.Loop.CurrentTick, sd, cd);
        }

        finalServer.Should().Be(finalClient,
            $"[{label}] after all actions delivered and settled, server and client must converge");
    }
}
