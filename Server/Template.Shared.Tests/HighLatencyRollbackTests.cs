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
/// Simulates high-latency rollback scenarios.
///
/// With 300ms latency, player inputs arrive 18 ticks late on the server.
/// The server rolls back 18 ticks, resimulates forward. During resimulation,
/// CDTNavigationState.Invalidate() fires (StateSerializer.Deserialize calls
/// InvalidateDerivedState), which clears the DtCrowd. The fresh crowd produces
/// different cow velocities than the warm crowd, causing desyncs.
///
/// This test reproduces the exact flow:
///   1. Run a single game to tick N
///   2. Save state at tick N (simulating the history snapshot)
///   3. Run forward to tick N+18 (server advanced while packet was in flight)
///   4. Restore state to tick N (rollback)
///   5. Resimulate N → N+18 (with the same actions)
///   6. Compare hash with the original run at N+18
/// </summary>
[Collection("Sequential")]
public class HighLatencyRollbackTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public HighLatencyRollbackTests(ITestOutputHelper output)
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
    /// Simulate a rollback of `rollbackTicks` ticks at various points in the game.
    /// This is exactly what the server does when a client input arrives late.
    /// </summary>
    [Theory]
    [InlineData(5, "50ms")]     // ~50ms latency (very low)
    [InlineData(10, "100ms")]   // ~100ms latency
    [InlineData(18, "300ms")]   // ~300ms latency (remote server)
    [InlineData(30, "500ms")]   // ~500ms latency (extreme)
    public void Rollback_WithLatency_ShouldReproduceSameState(int rollbackTicks, string label)
    {
        var game = CreateGame();
        var userId = Guid.NewGuid();
        var player = AddPlayer(game, userId);

        int desyncCount = 0;

        // Movement schedule: player moves around during the test
        var movements = new (int startTick, int stopTick, Vector2 direction)[]
        {
            (10, 40, new Vector2(1, 0)),
            (60, 100, new Vector2(0, 1)),
            (130, 170, new Vector2(-1, -1).Normalized),
            (200, 240, new Vector2(0.5f, 0.8f).Normalized),
            (270, 310, new Vector2(-1, 0)),
        };

        int moveIdx = 0;

        // Run game and periodically test rollback
        for (int tick = 0; tick < 600; tick++)
        {
            // Apply movement inputs
            if (moveIdx < movements.Length)
            {
                var m = movements[moveIdx];
                if (tick == m.startTick)
                {
                    var move = new SetMoveDirectionAction(m.direction, new Float(15));
                    game.Loop.Schedule(move, player);
                }
                else if (tick == m.stopTick)
                {
                    var stop = new SetMoveDirectionAction(Vector2.Zero, new Float(0));
                    game.Loop.Schedule(stop, player);
                    moveIdx++;
                }
            }

            game.Loop.RunSingleTick();

            // Every 60 ticks, simulate a rollback scenario
            if (tick > 0 && tick % 60 == 0 && tick > rollbackTicks)
            {
                long currentTick = game.Loop.CurrentTick;
                long rollbackTarget = currentTick - rollbackTicks;

                // Save the "correct" state at current tick
                var correctHash = HashGame(game);
                byte[] correctData = StateSerializer.Serialize(game.State);

                // Rollback to past tick
                bool restored = game.Loop.Simulation.History.Retrieve(rollbackTarget, game.State);
                if (!restored)
                {
                    _output.WriteLine($"  Tick {tick}: can't rollback to {rollbackTarget}, skipping");
                    // Restore correct state to continue
                    StateSerializer.Deserialize(game.State, correctData, syncComponentIds: false);
                    game.Loop.ForceSetTick(currentTick);
                    continue;
                }

                game.Loop.Simulation.History.DiscardFuture(rollbackTarget);
                game.Loop.ForceSetTick(rollbackTarget);

                // Resimulate forward (same as server rollback path)
                for (int r = 0; r < rollbackTicks; r++)
                    game.Loop.RunSingleTick();

                var afterHash = HashGame(game);
                bool match = correctHash == afterHash;

                if (!match)
                {
                    desyncCount++;
                    _output.WriteLine($"  Tick {tick}: DESYNC after {rollbackTicks}-tick rollback ({label})");

                    byte[] afterData = StateSerializer.Serialize(game.State);
                    StateDumper.LogStateDiff($"Rollback@{tick}", currentTick, afterData, correctData);
                }
            }
        }

        _output.WriteLine($"[{label}] {desyncCount} desyncs in {600 / 60} rollback tests ({rollbackTicks}-tick rollback)");
        desyncCount.Should().Be(0,
            $"rolling back {rollbackTicks} ticks ({label} latency) and resimulating should produce identical state");
    }

    /// <summary>
    /// Simulates repeated rollbacks (what happens when a client keeps sending
    /// inputs with high latency — every tick snapshot causes a rollback).
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(18)]
    public void RepeatedRollbacks_ShouldStayDeterministic(int rollbackTicks)
    {
        var game = CreateGame();
        var userId = Guid.NewGuid();
        var player = AddPlayer(game, userId);

        // Move the player
        var move = new SetMoveDirectionAction(new Vector2(1, 0), new Float(15));
        game.Loop.Schedule(move, player);

        // Run to tick 60 (well past initial setup)
        for (int i = 0; i < 60; i++)
            game.Loop.RunSingleTick();

        // Stop moving
        var stop = new SetMoveDirectionAction(Vector2.Zero, new Float(0));
        game.Loop.Schedule(stop, player);

        // Now simulate rapid rollbacks — every 5 ticks, rollback and resimulate
        int desyncCount = 0;
        for (int tick = 60; tick < 300; tick++)
        {
            game.Loop.RunSingleTick();

            if (tick % 5 == 0 && tick > 60 + rollbackTicks)
            {
                long currentTick = game.Loop.CurrentTick;
                long rollbackTarget = currentTick - rollbackTicks;

                var correctHash = HashGame(game);
                byte[] correctData = StateSerializer.Serialize(game.State);

                bool restored = game.Loop.Simulation.History.Retrieve(rollbackTarget, game.State);
                if (!restored) continue;

                game.Loop.Simulation.History.DiscardFuture(rollbackTarget);
                game.Loop.ForceSetTick(rollbackTarget);

                for (int r = 0; r < rollbackTicks; r++)
                    game.Loop.RunSingleTick();

                var afterHash = HashGame(game);
                if (correctHash != afterHash)
                {
                    desyncCount++;
                    if (desyncCount <= 3)
                    {
                        _output.WriteLine($"  DESYNC at tick {tick} after {rollbackTicks}-tick rollback");
                        byte[] afterData = StateSerializer.Serialize(game.State);
                        StateDumper.LogStateDiff($"Repeated@{tick}", currentTick, afterData, correctData);
                    }

                    // Restore correct state to continue testing
                    StateSerializer.Deserialize(game.State, correctData, syncComponentIds: false);
                    game.Loop.ForceSetTick(currentTick);
                }
            }
        }

        _output.WriteLine($"RepeatedRollbacks({rollbackTicks}): {desyncCount} desyncs in {(300 - 60) / 5} rollback tests");
        desyncCount.Should().Be(0,
            $"repeated {rollbackTicks}-tick rollbacks should always reproduce identical state");
    }
}
