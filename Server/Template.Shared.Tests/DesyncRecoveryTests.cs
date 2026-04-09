using System;
using System.Collections.Generic;
using System.IO;
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
using Template.Shared.Recording;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

/// <summary>
/// Tests determinism after desync recovery (full state sync).
/// Simulates the production scenario:
///   1. Server runs normally
///   2. Client desyncs, requests full state from server
///   3. Client restores server's state, continues running
///   4. Both should produce identical state from that point forward
///
/// This catches non-determinism in systems that rebuild internal state
/// from the ECS snapshot (DtCrowd, physics world, etc).
/// </summary>
[Collection("Sequential")]
public class DesyncRecoveryTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public DesyncRecoveryTests(ITestOutputHelper output)
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
    /// Serialize state from source, create a fresh game, deserialize into it.
    /// This is exactly what happens during a full state sync in production.
    /// </summary>
    private Game RestoreFromState(Game source)
    {
        byte[] stateData = StateSerializer.Serialize(source.State);
        long tick = source.Loop.CurrentTick;

        var restored = CreateGame();
        StateSerializer.Deserialize(restored.State, stateData);
        restored.Loop.ForceSetTick(tick);

        return restored;
    }

    /// <summary>
    /// Core scenario: server runs continuously, client restores from server state
    /// at a specific tick, then both run identical inputs. Hashes should match.
    /// </summary>
    [Theory]
    [InlineData(120, 300)]   // restore early, run for 5s
    [InlineData(300, 300)]   // restore after cows have moved a bit
    [InlineData(600, 300)]   // restore after grass spawn tick
    [InlineData(120, 600)]   // restore early, long run (10s)
    public void AfterStateRestore_BothInstances_ShouldMatch(int restoreTick, int runAfterRestore)
    {
        var server = CreateGame();
        var userId = Guid.NewGuid();
        var player = AddPlayer(server, userId);

        // Run server to the restore point
        for (int i = 0; i < restoreTick; i++)
            server.Loop.RunSingleTick();

        _output.WriteLine($"Server at tick {server.Loop.CurrentTick}, restoring client...");

        // Simulate full state sync: client gets server's state
        var client = RestoreFromState(server);

        // Verify they start identical
        var serverHash0 = HashGame(server);
        var clientHash0 = HashGame(client);
        serverHash0.Should().Be(clientHash0, "state should match immediately after restore");

        // Find the player in the client (same entity ID)
        Entity clientPlayer = Entity.Null;
        foreach (var e in client.State.Filter<PlayerEntity>())
        {
            if (client.State.GetComponent<PlayerEntity>(e).UserId == userId)
            { clientPlayer = e; break; }
        }

        // Run both with identical inputs
        int desyncCount = 0;
        int checkInterval = 60;

        // Schedule some movement on both
        var movements = new (int tick, Vector2 dir, Float speed)[]
        {
            (10, new Vector2(1, 0), new Float(15)),
            (50, Vector2.Zero, new Float(0)),
            (90, new Vector2(0, 1), new Float(20)),
            (150, Vector2.Zero, new Float(0)),
            (180, new Vector2(-1, -1).Normalized, new Float(15)),
            (230, Vector2.Zero, new Float(0)),
        };

        for (int tick = 0; tick < runAfterRestore; tick++)
        {
            // Apply same inputs to both
            foreach (var m in movements)
            {
                if (tick == m.tick)
                {
                    var action = new SetMoveDirectionAction(m.dir, m.speed);
                    server.Loop.Schedule(action, player);
                    client.Loop.Schedule(action, clientPlayer);
                }
            }

            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();

            // Check hash at intervals
            if ((tick + 1) % checkInterval == 0)
            {
                var sh = HashGame(server);
                var ch = HashGame(client);
                bool match = sh == ch;
                _output.WriteLine($"  Tick +{tick + 1}: {(match ? "MATCH" : "DESYNC")}");

                if (!match)
                {
                    desyncCount++;
                    // Log what's different
                    byte[] serverData = StateSerializer.Serialize(server.State);
                    byte[] clientData = StateSerializer.Serialize(client.State);
                    StateDumper.LogStateDiff($"Restore@{restoreTick}+{tick + 1}",
                        server.Loop.CurrentTick, clientData, serverData);
                }
            }
        }

        _output.WriteLine($"Result: {desyncCount} desyncs in {runAfterRestore / checkInterval} checks");
        desyncCount.Should().Be(0,
            $"after restoring state at tick {restoreTick}, running {runAfterRestore} more ticks should stay in sync");
    }

    /// <summary>
    /// Same test but using a real recorded session. Restores mid-recording
    /// and checks remaining ticks stay in sync.
    /// </summary>
    [Theory]
    [MemberData(nameof(GetRecordingFiles))]
    public void RecordedSession_RestoreMidway_ShouldMatch(string recordingFile)
    {
        var recordingsDir = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "Recordings");
        var path = Path.Combine(recordingsDir, recordingFile);

        if (!File.Exists(path))
        {
            _output.WriteLine($"SKIP: {recordingFile} not found");
            return;
        }

        var (totalTicks, actions, _, _, _, _) = InputRecording.Load(path);
        _output.WriteLine($"Recording: {recordingFile} ({totalTicks} ticks, {actions.Count} actions)");

        // Restore point: 1/3 into the recording
        long restoreTick = totalTicks / 3;

        // Run the "server" up to the restore point
        var server = CreateGame();
        int actionIdx = 0;
        for (long tick = 0; tick <= restoreTick; tick++)
        {
            while (actionIdx < actions.Count && actions[actionIdx].Tick <= server.Loop.CurrentTick + 1)
            {
                var a = actions[actionIdx];
                var stableId = new StableComponentId(a.StableComponentId);
                if (ComponentId.TryGetDense(stableId, out var denseId))
                    server.Scheduler.ScheduleFromBytes(denseId, a.Data, a.TargetEntityId, a.Tick);
                actionIdx++;
            }
            server.Loop.RunSingleTick();
        }

        _output.WriteLine($"Server at tick {server.Loop.CurrentTick}, restoring client...");

        // Restore client from server state
        var client = RestoreFromState(server);

        // Continue both with the remaining actions
        int desyncCount = 0;
        int serverActionIdx = actionIdx;
        int clientActionIdx = actionIdx;

        for (long tick = restoreTick + 1; tick <= totalTicks; tick++)
        {
            // Schedule actions on both
            while (serverActionIdx < actions.Count && actions[serverActionIdx].Tick <= server.Loop.CurrentTick + 1)
            {
                var a = actions[serverActionIdx];
                var stableId = new StableComponentId(a.StableComponentId);
                if (ComponentId.TryGetDense(stableId, out var denseId))
                    server.Scheduler.ScheduleFromBytes(denseId, a.Data, a.TargetEntityId, a.Tick);
                serverActionIdx++;
            }
            while (clientActionIdx < actions.Count && actions[clientActionIdx].Tick <= client.Loop.CurrentTick + 1)
            {
                var a = actions[clientActionIdx];
                var stableId = new StableComponentId(a.StableComponentId);
                if (ComponentId.TryGetDense(stableId, out var denseId))
                    client.Scheduler.ScheduleFromBytes(denseId, a.Data, a.TargetEntityId, a.Tick);
                clientActionIdx++;
            }

            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();

            // Check every 60 ticks
            if (tick % 60 == 0)
            {
                var sh = HashGame(server);
                var ch = HashGame(client);
                if (sh != ch)
                {
                    desyncCount++;
                    if (desyncCount <= 3) // Only log first 3
                    {
                        _output.WriteLine($"  DESYNC at tick {tick}");
                        byte[] sd = StateSerializer.Serialize(server.State);
                        byte[] cd = StateSerializer.Serialize(client.State);
                        StateDumper.LogStateDiff($"RecRestore@{tick}", tick, cd, sd);
                    }
                }
            }
        }

        _output.WriteLine($"Result: {desyncCount} desyncs after restore at tick {restoreTick}");
        desyncCount.Should().Be(0,
            $"after restoring state at tick {restoreTick}, remaining {totalTicks - restoreTick} ticks should stay in sync");
    }

    public static TheoryData<string> GetRecordingFiles()
    {
        var data = new TheoryData<string>();
        var dir = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "Recordings");

        bool found = false;
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir, "*.bin"))
            {
                data.Add(Path.GetFileName(file));
                found = true;
            }
        }

        if (!found)
            data.Add("__NO_RECORDINGS_FOUND__");

        return data;
    }
}
