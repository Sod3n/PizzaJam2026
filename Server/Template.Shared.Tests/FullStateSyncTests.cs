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
/// Simulates the full state sync flow:
///   1. Server and client run from same initial state
///   2. Client predicts actions ahead of server
///   3. Desync is detected
///   4. Server sends confirmed state (120 ticks in the past)
///   5. Client applies confirmed state, re-adds predicted actions, resimulates forward
///   6. After settling, client and server should match
/// </summary>
[Collection("Sequential")]
public class FullStateSyncTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public FullStateSyncTests(ITestOutputHelper output)
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

    private static Guid HashState(Game game)
    {
        return StateHasher.Hash(StateSerializer.Serialize(game.State));
    }

    private static Guid HashAtTick(Game game, long tick)
    {
        if (game.Loop.Simulation.GetHistory().TryGetSnapshotData(tick, out byte[]? data))
            return StateHasher.Hash(data!);
        throw new Exception($"Tick {tick} not in history");
    }

    /// <summary>
    /// Simulates the full state sync flow with confirmed state + resimulation.
    ///
    /// Server runs normally. Client predicts player movement, gets ahead.
    /// Then we simulate "sync": client receives server's confirmed state,
    /// re-adds predicted actions, and resimulates forward. Result should
    /// match the server.
    /// </summary>
    [Theory]
    [InlineData(18, "300ms")]
    [InlineData(30, "500ms")]
    public void FullStateSync_WithPredictedActions_ShouldConverge(int networkDelay, string label)
    {
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        const int confirmationWindow = 120;

        // Run both to tick 100
        for (int i = 0; i < 100; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Verify in sync
        var hs = HashAtTick(server, server.Loop.CurrentTick);
        var hc = HashAtTick(client, client.Loop.CurrentTick);
        hs.Should().Be(hc, "should be in sync before actions");

        // --- Client predicts multiple actions, server gets them late ---
        var actions = new (int sendTick, Vector2 dir)[]
        {
            (100, new Vector2(1, 0)),
            (130, new Vector2(0, 1)),
            (160, Vector2.Zero),
            (200, new Vector2(-1, 0)),
        };

        var pendingServerDeliveries = new System.Collections.Generic.Queue<(int deliveryTick, long execTick, SetMoveDirectionAction action)>();

        // Track client's predicted actions (what GameClient._unconfirmedPredictions would hold)
        var predictedActions = new System.Collections.Generic.List<(long execTick, SetMoveDirectionAction action)>();

        for (int tick = 100; tick < 300; tick++)
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
                    pendingServerDeliveries.Enqueue((tick + networkDelay, execTick, action));
                    predictedActions.Add((execTick, action));
                }
            }

            // Deliver to server (delayed)
            while (pendingServerDeliveries.Count > 0 && pendingServerDeliveries.Peek().deliveryTick <= tick)
            {
                var d = pendingServerDeliveries.Dequeue();
                server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
            }

            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Deliver remaining actions to server
        while (pendingServerDeliveries.Count > 0)
        {
            var d = pendingServerDeliveries.Dequeue();
            server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
        }

        // Run server to tick 350 so all actions are fully processed
        while (server.Loop.CurrentTick < 350)
            server.Loop.RunSingleTick();

        // Client is at tick 300, server at tick 350

        _output.WriteLine($"[{label}] Pre-sync: server at tick {server.Loop.CurrentTick}, " +
            $"client at tick {client.Loop.CurrentTick}");

        // --- Simulate full state sync with confirmed state ---
        // Server sends confirmed state (120 ticks back from server's current tick)
        long confirmedTick = server.Loop.CurrentTick - confirmationWindow;
        _output.WriteLine($"[{label}] Confirmed tick: {confirmedTick}");

        // Get server's confirmed state from history
        server.Loop.Simulation.GetHistory().TryGetSnapshotData(confirmedTick, out byte[]? confirmedState)
            .Should().BeTrue($"server should have tick {confirmedTick} in history");

        // Client applies the confirmed state
        StateSerializer.Deserialize(client.State, confirmedState!, syncComponentIds: false);
        client.Loop.ForceSetTick(confirmedTick);
        client.Loop.Simulation.GetHistory().Store(confirmedTick, client.State);

        // Prune old actions
        client.Scheduler.PruneHistory(confirmedTick);

        // Re-add predicted actions with execTick >= confirmedTick
        // (actions before confirmedTick are already baked into the confirmed state)
        int reAdded = 0;
        foreach (var p in predictedActions)
        {
            if (p.execTick < confirmedTick)
                continue; // Already in confirmed state

            var result = client.Scheduler.Schedule(p.action,
                ComponentId<SetMoveDirectionAction>.DenseId,
                clientPlayer, p.execTick);
            if (result == ActionScheduler.ScheduleResult.Success)
                reAdded++;
        }
        _output.WriteLine($"[{label}] Re-added {reAdded} predicted actions (execTick >= {confirmedTick})");

        // Resimulate from confirmed tick to server's current tick
        long targetTick = server.Loop.CurrentTick;
        _output.WriteLine($"[{label}] Resimulating {confirmedTick} → {targetTick}");
        while (client.Loop.CurrentTick < targetTick)
            client.Loop.RunSingleTick();

        // --- Verify convergence ---
        var finalServer = HashState(server);
        var finalClient = HashState(client);

        _output.WriteLine($"[{label}] Post-sync: server={finalServer}, client={finalClient}, " +
            $"match={finalServer == finalClient}");

        finalClient.Should().Be(finalServer,
            $"[{label}] after full state sync + resimulation with predicted actions, " +
            $"client should match server");
    }

    /// <summary>
    /// Verify that confirmed state from history matches live state hashed
    /// at the same tick. This validates the server is sending correct data.
    /// </summary>
    [Fact]
    public void ConfirmedState_ShouldMatchLiveStateAtSameTick()
    {
        var game = CreateGame();
        var userId = Guid.NewGuid();
        AddPlayer(game, userId);

        // Run to tick 200
        for (int i = 0; i < 200; i++)
            game.Loop.RunSingleTick();

        // Hash from history at tick 150
        var historyHash = HashAtTick(game, 150);

        // Run a fresh game to tick 150 and hash live state
        var fresh = CreateGame();
        AddPlayer(fresh, userId);
        for (int i = 0; i < 150; i++)
            fresh.Loop.RunSingleTick();

        var liveHash = HashState(fresh);

        historyHash.Should().Be(liveHash,
            "confirmed state from history should match a fresh game at the same tick");
    }

    /// <summary>
    /// After sync + resim, run both forward and verify they stay in sync.
    /// </summary>
    [Fact]
    public void AfterSync_ContinuedPlay_ShouldRemainInSync()
    {
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        const int confirmationWindow = 120;

        // Run both to tick 200
        for (int i = 0; i < 200; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Client predicts a move, server gets it late
        long execTick = client.Loop.CurrentTick + 5;
        var moveAction = new SetMoveDirectionAction(new Vector2(1, 0), new Float(15));
        client.Loop.ScheduleOnTick(execTick, moveAction, clientPlayer);

        // Run both forward 30 ticks (server doesn't have the action)
        for (int i = 0; i < 30; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Now simulate full state sync
        long confirmedTick = server.Loop.CurrentTick - confirmationWindow;
        server.Loop.Simulation.GetHistory().TryGetSnapshotData(confirmedTick, out byte[]? confirmedState)
            .Should().BeTrue();

        // Apply confirmed state to client
        StateSerializer.Deserialize(client.State, confirmedState!, syncComponentIds: false);
        client.Loop.ForceSetTick(confirmedTick);
        client.Loop.Simulation.GetHistory().Store(confirmedTick, client.State);
        client.Scheduler.PruneHistory(confirmedTick);

        // Re-add predicted action only if it's at or after confirmed tick
        if (execTick >= confirmedTick)
        {
            client.Scheduler.Schedule(moveAction,
                ComponentId<SetMoveDirectionAction>.DenseId,
                clientPlayer, execTick);
        }

        // Resimulate to server tick
        while (client.Loop.CurrentTick < server.Loop.CurrentTick)
            client.Loop.RunSingleTick();

        // Now deliver the action to server too (it finally arrives)
        server.Loop.ScheduleOnTick(execTick, moveAction, serverPlayer);

        // Run both forward 100 more ticks
        for (int i = 0; i < 100; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Check confirmed tick hash
        long checkTick = server.Loop.CurrentTick - 60;
        var serverHash = HashAtTick(server, checkTick);
        var clientHash = HashAtTick(client, checkTick);

        _output.WriteLine($"Check tick {checkTick}: server={serverHash}, client={clientHash}");

        serverHash.Should().Be(clientHash,
            "after sync + continued play with action delivery, should converge");
    }

    /// <summary>
    /// Simulates high-ping (300ms) full state sync scenario.
    ///
    /// Timeline:
    ///   1. Client and server run in sync to tick 200
    ///   2. Client predicts actions continuously (every 30 ticks)
    ///   3. Server receives actions with 18-tick delay (300ms)
    ///   4. Desync detected → client requests full state
    ///   5. Full state arrives 18 ticks later (300ms round trip)
    ///   6. Client applies confirmed state + resimulates
    ///
    /// Verifies:
    ///   - Client tick never goes backwards (no tick regression)
    ///   - After sync + resim, client matches server at confirmed ticks
    ///   - Player position is continuous (no large teleport)
    /// </summary>
    [Theory]
    [InlineData(18, "300ms")]
    [InlineData(30, "500ms")]
    public void HighPing_FullStateSync_ShouldNotJumpBackByTicks(int networkDelay, string label)
    {
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        const int confirmationWindow = 120;

        // Run both to tick 100
        for (int i = 0; i < 100; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Client predicts actions, server gets them late
        var predictedActions = new System.Collections.Generic.List<(long execTick, SetMoveDirectionAction action)>();
        var pendingDeliveries = new System.Collections.Generic.Queue<(int deliveryTick, long execTick, SetMoveDirectionAction action)>();

        var directions = new Vector2[]
        {
            new Vector2(1, 0), new Vector2(0, 1), Vector2.Zero,
            new Vector2(-1, 0), new Vector2(0, -1), Vector2.Zero,
        };

        for (int tick = 100; tick < 400; tick++)
        {
            // Client sends action every 30 ticks
            if (tick % 30 == 0)
            {
                int dirIdx = (tick / 30) % directions.Length;
                long execTick = client.Loop.CurrentTick + 5;
                var action = new SetMoveDirectionAction(directions[dirIdx],
                    directions[dirIdx] == Vector2.Zero ? new Float(0) : new Float(15));

                client.Loop.ScheduleOnTick(execTick, action, clientPlayer);
                pendingDeliveries.Enqueue((tick + networkDelay, execTick, action));
                predictedActions.Add((execTick, action));
            }

            // Deliver to server (delayed)
            while (pendingDeliveries.Count > 0 && pendingDeliveries.Peek().deliveryTick <= tick)
            {
                var d = pendingDeliveries.Dequeue();
                server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
            }

            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Deliver remaining to server
        while (pendingDeliveries.Count > 0)
        {
            var d = pendingDeliveries.Dequeue();
            server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
        }

        // Run server ahead a bit (simulating server being slightly ahead)
        for (int i = 0; i < 20; i++)
            server.Loop.RunSingleTick();

        long clientTickBeforeSync = client.Loop.CurrentTick;
        long serverTickNow = server.Loop.CurrentTick;
        _output.WriteLine($"[{label}] Before sync: client={clientTickBeforeSync}, server={serverTickNow}");

        // --- Simulate the full state sync flow ---

        // Server sends confirmed state
        long confirmedTick = serverTickNow - confirmationWindow;
        server.Loop.Simulation.GetHistory().TryGetSnapshotData(confirmedTick, out byte[]? confirmedState)
            .Should().BeTrue($"server should have tick {confirmedTick} in history");

        // Simulate network delay: client keeps running while waiting for response
        for (int i = 0; i < networkDelay; i++)
        {
            client.Loop.RunSingleTick();
            server.Loop.RunSingleTick();
        }

        long clientTickAtReceive = client.Loop.CurrentTick;
        long serverTickAtSend = serverTickNow; // server sent it at serverTickNow
        _output.WriteLine($"[{label}] At receive: client={clientTickAtReceive}, confirmed={confirmedTick}, serverTick in header={serverTickAtSend}");

        // Client applies confirmed state
        StateSerializer.Deserialize(client.State, confirmedState!, syncComponentIds: false);
        client.Loop.ForceSetTick(confirmedTick);
        client.Loop.Simulation.GetHistory().Store(confirmedTick, client.State);
        client.Scheduler.PruneHistory(confirmedTick);

        // Re-add predicted actions >= confirmedTick
        int reAdded = 0;
        foreach (var p in predictedActions)
        {
            if (p.execTick < confirmedTick) continue;
            var result = client.Scheduler.Schedule(p.action,
                ComponentId<SetMoveDirectionAction>.DenseId,
                clientPlayer, p.execTick);
            if (result == ActionScheduler.ScheduleResult.Success)
                reAdded++;
        }
        _output.WriteLine($"[{label}] Re-added {reAdded} predicted actions");

        // Resimulate to max(serverTick, clientTickAtReceive)
        long resimTarget = Math.Max(serverTickAtSend, clientTickAtReceive);
        _output.WriteLine($"[{label}] Resimulating {confirmedTick} → {resimTarget} ({resimTarget - confirmedTick} ticks)");
        while (client.Loop.CurrentTick < resimTarget)
            client.Loop.RunSingleTick();

        long clientTickAfterSync = client.Loop.CurrentTick;
        _output.WriteLine($"[{label}] After sync: client={clientTickAfterSync}");

        // === VERIFY: no tick regression ===
        clientTickAfterSync.Should().BeGreaterThanOrEqualTo(clientTickAtReceive,
            $"[{label}] client should never jump backwards in ticks after sync");

        // === VERIFY: convergence after settling ===
        // Run both forward to let everything settle
        while (server.Loop.CurrentTick < client.Loop.CurrentTick + 20)
            server.Loop.RunSingleTick();
        while (client.Loop.CurrentTick < server.Loop.CurrentTick)
            client.Loop.RunSingleTick();

        // Run both in lockstep for 80 more ticks
        for (int i = 0; i < 80; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        long checkTick = server.Loop.CurrentTick - 60;
        var serverHash = HashAtTick(server, checkTick);
        var clientHash = HashAtTick(client, checkTick);
        _output.WriteLine($"[{label}] Convergence check at tick {checkTick}: match={serverHash == clientHash}");

        serverHash.Should().Be(clientHash,
            $"[{label}] after high-ping sync + resim, should converge");
    }

    /// <summary>
    /// Verify player position doesn't teleport after sync.
    /// Compare player position before and after sync — the delta
    /// should be small (within a few ticks of movement), not a large jump.
    /// </summary>
    [Fact]
    public void FullStateSync_PlayerPosition_ShouldNotTeleport()
    {
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        const int confirmationWindow = 120;
        const int networkDelay = 18;

        // Run both to tick 100
        for (int i = 0; i < 100; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Client predicts continuous movement right
        var moveRight = new SetMoveDirectionAction(new Vector2(1, 0), new Float(15));
        var predictedActions = new System.Collections.Generic.List<(long execTick, SetMoveDirectionAction action)>();
        var pendingDeliveries = new System.Collections.Generic.Queue<(int deliveryTick, long execTick, SetMoveDirectionAction action)>();

        // Send move action
        long moveExecTick = client.Loop.CurrentTick + 5;
        client.Loop.ScheduleOnTick(moveExecTick, moveRight, clientPlayer);
        pendingDeliveries.Enqueue((100 + networkDelay, moveExecTick, moveRight));
        predictedActions.Add((moveExecTick, moveRight));

        // Run both to tick 300 (player moving on client, delayed on server)
        for (int tick = 100; tick < 300; tick++)
        {
            while (pendingDeliveries.Count > 0 && pendingDeliveries.Peek().deliveryTick <= tick)
            {
                var d = pendingDeliveries.Dequeue();
                server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
            }
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Run server a bit ahead
        for (int i = 0; i < 20; i++)
            server.Loop.RunSingleTick();

        // Capture client player position BEFORE sync at the current tick
        long clientTickBefore = client.Loop.CurrentTick;
        var playerPosBefore = client.State.GetComponent<Transform2D>(clientPlayer).Position;
        _output.WriteLine($"Before sync: tick={clientTickBefore}, pos={playerPosBefore}");

        // Simulate sync
        long confirmedTick = server.Loop.CurrentTick - confirmationWindow;
        server.Loop.Simulation.GetHistory().TryGetSnapshotData(confirmedTick, out byte[]? confirmedState)
            .Should().BeTrue();

        StateSerializer.Deserialize(client.State, confirmedState!, syncComponentIds: false);
        client.Loop.ForceSetTick(confirmedTick);
        client.Loop.Simulation.GetHistory().Store(confirmedTick, client.State);
        client.Scheduler.PruneHistory(confirmedTick);

        // Re-add predicted actions
        foreach (var p in predictedActions)
        {
            if (p.execTick < confirmedTick) continue;
            client.Scheduler.Schedule(p.action,
                ComponentId<SetMoveDirectionAction>.DenseId,
                clientPlayer, p.execTick);
        }

        // Resimulate back to the SAME tick the client was at before sync
        while (client.Loop.CurrentTick < clientTickBefore)
            client.Loop.RunSingleTick();

        // Compare position at the SAME tick — should be identical or very close
        var playerPosAfter = client.State.GetComponent<Transform2D>(clientPlayer).Position;

        var positionDelta = Vector2.Distance(playerPosBefore, playerPosAfter);
        _output.WriteLine($"After sync:  tick={client.Loop.CurrentTick}, pos={playerPosAfter}");
        _output.WriteLine($"Delta: {positionDelta} (at same tick {clientTickBefore})");

        // At the same tick, with the same predicted action replayed,
        // the position should be exactly zero (deterministic simulation).
        ((float)positionDelta).Should().Be(0f,
            "player position at the same tick after sync+resim should be identical " +
            "(confirmed state + replayed predictions = same result)");
    }

    /// <summary>
    /// Scenario: TickSnapshot arrives BEFORE full state due to network reordering.
    ///
    /// Timeline:
    ///   1. Client at tick 200, server at tick 200
    ///   2. Desync detected, client requests full state
    ///   3. Server sends confirmed state for tick 120 (with ServerTick=200)
    ///   4. While waiting, TickSnapshot for tick 140 arrives → client applies
    ///      server actions at tick 140 and keeps running (now at tick ~218)
    ///   5. Full state for tick 120 arrives → client resets to tick 120
    ///   6. TargetTick catch-up advances client, receiving more TickSnapshots
    ///   7. The tick-140 actions that were scheduled earlier survive the prune
    ///      (140 >= 120) and fire correctly during catch-up
    ///   8. Client should converge with server
    /// </summary>
    [Fact]
    public void TickSnapshotBeforeFullState_ShouldStillConverge()
    {
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        const int confirmationWindow = 120;

        // Run both to tick 200 in sync
        for (int i = 0; i < 200; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Verify in sync
        HashState(server).Should().Be(HashState(client), "should be in sync at tick 200");

        long clientTickAtDesync = client.Loop.CurrentTick;
        _output.WriteLine($"Both at tick {clientTickAtDesync}");

        // --- Server schedules an action at tick 140 (another player's action) ---
        // This simulates a server-side action that the client doesn't know about yet.
        var serverAction = new SetMoveDirectionAction(new Vector2(0, 1), new Float(10));
        // Use a different entity — create a dummy target (use serverPlayer for simplicity,
        // the point is the action exists on server but client gets it late)
        server.Loop.ScheduleOnTick(140, serverAction, serverPlayer);

        // Server runs a few more ticks (processes the rollback from the action at 140)
        for (int i = 0; i < 20; i++)
            server.Loop.RunSingleTick();

        // Server prepares confirmed state at tick 100 (server at 220, 220-120=100)
        long serverTickNow = server.Loop.CurrentTick;
        long confirmedTick = serverTickNow - confirmationWindow;
        _output.WriteLine($"Server at {serverTickNow}, confirmed tick={confirmedTick}");

        server.Loop.Simulation.GetHistory().TryGetSnapshotData(confirmedTick, out byte[]? confirmedState)
            .Should().BeTrue($"server should have tick {confirmedTick} in history");

        // --- Step 1: TickSnapshot for tick 140 arrives FIRST (out of order) ---
        // Client applies the server action at tick 140 to its scheduler
        var actionBytes = new byte[System.Runtime.InteropServices.Marshal.SizeOf<SetMoveDirectionAction>()];
        System.Runtime.InteropServices.MemoryMarshal.Write(new Span<byte>(actionBytes), ref serverAction);
        client.Scheduler.ScheduleFromBytes(
            ComponentId<SetMoveDirectionAction>.DenseId,
            actionBytes, serverPlayer.Id, 140);

        // Client keeps running (doesn't know full state is coming)
        // This triggers a rollback to tick 140 and resimulates to current
        for (int i = 0; i < 18; i++)
            client.Loop.RunSingleTick();

        long clientTickBeforeFullState = client.Loop.CurrentTick;
        _output.WriteLine($"Client at {clientTickBeforeFullState} after TickSnapshot (before full state)");

        // --- Step 2: Full state for confirmed tick arrives ---
        StateSerializer.Deserialize(client.State, confirmedState!, syncComponentIds: false);
        client.Loop.ForceSetTick(confirmedTick);
        client.Loop.Simulation.GetHistory().Store(confirmedTick, client.State);
        client.Scheduler.PruneHistory(confirmedTick);

        _output.WriteLine($"Applied full state at tick {confirmedTick}, client now at {client.Loop.CurrentTick}");

        // The action at tick 140 should survive the prune (140 >= confirmedTick)
        // because PruneHistory keeps actions with ExecuteTick >= confirmedTick.

        // --- Step 3: Catch up by running ticks (simulating TargetTick catch-up) ---
        // During catch-up, the action at tick 140 will fire naturally
        while (client.Loop.CurrentTick < serverTickNow)
            client.Loop.RunSingleTick();

        _output.WriteLine($"Client caught up to {client.Loop.CurrentTick}");

        // --- Step 4: Run both forward in lockstep to settle ---
        for (int i = 0; i < 80; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Verify convergence
        long checkTick = server.Loop.CurrentTick - 30;
        var serverHash = HashAtTick(server, checkTick);
        var clientHash = HashAtTick(client, checkTick);

        _output.WriteLine($"Check tick {checkTick}: server={serverHash}, client={clientHash}, match={serverHash == clientHash}");

        serverHash.Should().Be(clientHash,
            "after out-of-order TickSnapshot + full state sync, client should converge with server");
    }
}
