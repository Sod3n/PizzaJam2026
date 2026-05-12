using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Factories;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

/// <summary>
/// End-to-end determinism tests: run two independent Game instances with the same
/// inputs and verify their state hashes match at every checkpoint. Simulates the
/// server/client desync scenario by using BotBrain to drive actions.
/// </summary>
[Collection("Sequential")]
public class DeterminismTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public DeterminismTests(ITestOutputHelper output)
    {
        _output = output;
        ILogger.SetLogger(new XunitLogger(output));
    }

    public void Dispose()
    {
        // Clear logger so it doesn't reference a disposed ITestOutputHelper
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
        TemplateGameFactory.CreateGame(tickRate: 60); // Force registration
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

    /// <summary>
    /// Tests rollback determinism when entities have CharacterBody2D (cows, player).
    /// Box2D world is destroyed and recreated on rollback, which causes physics
    /// divergence due to different solver warm-starting. This is a known limitation
    /// tracked separately from the GrassSpawnSystem desync fix.
    /// </summary>
    [Fact]
    public void SingleGame_Rollback_ShouldReproduceSameState()
    {
        var game = CreateGame();

        var userId = Guid.NewGuid();
        AddPlayer(game, userId);

        // Run to tick 80
        for (int i = 0; i < 80; i++)
            game.Loop.RunSingleTick();

        // Save hash at tick 80, then run 10 more to tick 90
        long checkpointTick = game.Loop.CurrentTick;
        for (int i = 0; i < 10; i++)
            game.Loop.RunSingleTick();

        long targetTick = game.Loop.CurrentTick;
        byte[] correctData = StateSerializer.Serialize(game.State);
        var correctHash = StateHasher.Hash(correctData);
        _output.WriteLine($"Correct hash at tick {targetTick}: {correctHash}");

        // Rollback to tick 80 (checkpoint), resimulate 10 ticks
        bool restored = game.Loop.Simulation.GetHistory().Retrieve(checkpointTick, game.State);
        restored.Should().BeTrue();

        game.Loop.Simulation.GetHistory().DiscardFuture(checkpointTick);
        game.Loop.ForceSetTick(checkpointTick);

        for (int i = 0; i < 10; i++)
            game.Loop.RunSingleTick();

        var afterHash = HashGame(game);
        _output.WriteLine($"After rollback hash at tick {game.Loop.CurrentTick}: {afterHash}");
        _output.WriteLine($"Match: {correctHash == afterHash}");

        if (correctHash != afterHash)
        {
            // Run a few more ticks to check convergence
            _output.WriteLine("Checking convergence...");
            for (int extra = 0; extra < 20; extra++)
            {
                game.Loop.RunSingleTick();
                var h = HashGame(game);
                _output.WriteLine($"  tick {game.Loop.CurrentTick}: {h}");
            }
        }

        if (correctHash != afterHash)
        {
            // Find first byte difference between serialized states
            byte[] afterData = StateSerializer.Serialize(game.State);
            int minLen = System.Math.Min(correctData.Length, afterData.Length);
            int firstDiff = -1;
            for (int b = 0; b < minLen; b++)
            {
                if (correctData[b] != afterData[b]) { firstDiff = b; break; }
            }
            _output.WriteLine($"DIVERGENCE: sizes correct={correctData.Length} after={afterData.Length} firstDiffByte={firstDiff}");

            // Dump via StateDumper to stderr
            StateDumper.LogStateDiff("RollbackTest", (long)game.Loop.CurrentTick, afterData, correctData);
        }
        correctHash.Should().Be(afterHash, "state must be identical after rollback + resimulation");
    }

    /// <summary>
    /// Rollback across GrassSpawnSystem trigger (tick % 600 == 0).
    /// This is the exact scenario that caused the original desync.
    /// </summary>
    [Fact]
    public void Rollback_AcrossGrassSpawnTick_ShouldReproduceSameState()
    {
        var game = CreateGame();

        var userId = Guid.NewGuid();
        AddPlayer(game, userId);

        // Build some houses so there are obstacles for nav mesh
        var ctx = new Deterministic.GameFramework.DAR.Context(game.State, Entity.Null, null!);
        for (int i = 0; i < 5; i++)
            HouseDefinition.Create(ctx, new Vector2(i * 8, 5));

        // Run to just past tick 600 (GrassSpawnSystem trigger)
        while (game.Loop.CurrentTick < 610)
            game.Loop.RunSingleTick();

        long targetTick = game.Loop.CurrentTick;
        var correctHash = HashGame(game);
        _output.WriteLine($"Correct hash at tick {targetTick}: {correctHash}");

        // Rollback to BEFORE the grass spawn tick (590), resimulate through it
        long rollbackTarget = 590;
        bool restored = game.Loop.Simulation.GetHistory().Retrieve(rollbackTarget, game.State);
        restored.Should().BeTrue();

        game.Loop.Simulation.GetHistory().DiscardFuture(rollbackTarget);
        game.Loop.ForceSetTick(rollbackTarget);

        while (game.Loop.CurrentTick < targetTick)
            game.Loop.RunSingleTick();

        var afterHash = HashGame(game);
        _output.WriteLine($"After rollback hash at tick {game.Loop.CurrentTick}: {afterHash}");
        _output.WriteLine($"Match: {correctHash == afterHash}");

        correctHash.Should().Be(afterHash,
            "state must be identical after rollback across GrassSpawnSystem tick (tick 600)");
    }


    // ── Helpers ──

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
}
