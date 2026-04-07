using System;
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
/// Generates a bot-driven recording that can be used by DeterminismRegressionTests.
/// Run this to create/refresh the recording file.
/// </summary>
[Collection("Sequential")]
public class RecordingGeneratorTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private static readonly string RecordingsDir = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "..", "..", "..", "Recordings");

    public RecordingGeneratorTests(ITestOutputHelper output)
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

    /// <summary>
    /// Generate a recording with scripted player movement.
    /// Simulates ~30 seconds of gameplay with movement patterns.
    /// </summary>
    [Fact]
    public void GenerateBotRecording()
    {
        var game = CreateGame();
        var recorder = new InputRecorder(game);

        // Add player
        var userId = new Guid("12345678-1234-1234-1234-123456789abc");
        Entity worldEntity = Entity.Null;
        foreach (var e in game.State.Filter<World>()) { worldEntity = e; break; }

        game.State.AddComponent(worldEntity, new AddPlayerAction(userId));
        game.Dispatcher.Update(game.State);
        game.Loop.Simulation.SystemRunner.Update(game.State);

        Entity player = Entity.Null;
        foreach (var e in game.State.Filter<PlayerEntity>())
        {
            if (game.State.GetComponent<PlayerEntity>(e).UserId == userId)
            { player = e; break; }
        }
        player.Should().NotBe(Entity.Null);

        // Start recording AFTER initial setup (just like real game)
        recorder.Start();

        // Movement script: simulate real player inputs
        var movements = new (int startTick, int stopTick, Vector2 direction, Float speed)[]
        {
            (10, 50, new Vector2(1, 0), new Float(15)),
            (80, 130, new Vector2(0, 1), new Float(15)),
            (160, 220, new Vector2(-1, -1).Normalized, new Float(20)), // sprint diagonal
            (260, 310, new Vector2(0.7f, 0.3f).Normalized, new Float(15)),
            (350, 400, new Vector2(-1, 0), new Float(15)),
            (430, 500, new Vector2(0, -1), new Float(20)),
            (540, 600, new Vector2(1, 1).Normalized, new Float(15)),
            (640, 700, new Vector2(-0.5f, 0.8f).Normalized, new Float(15)),
            (740, 800, new Vector2(1, -0.5f).Normalized, new Float(20)),
            // idle stretch 800-900
            (900, 980, new Vector2(-1, 0), new Float(15)),
            (1020, 1100, new Vector2(0, 1), new Float(15)),
            (1140, 1200, new Vector2(1, 0), new Float(20)),
            // idle until end
        };

        int totalTicks = 1800; // 30 seconds at 60 tps
        int moveIdx = 0;

        for (int tick = 0; tick < totalTicks; tick++)
        {
            if (moveIdx < movements.Length)
            {
                var m = movements[moveIdx];
                if (tick == m.startTick)
                {
                    var move = new SetMoveDirectionAction(m.direction, m.speed);
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
        }

        recorder.Stop();

        // Save
        Directory.CreateDirectory(RecordingsDir);
        var path = Path.Combine(RecordingsDir, "bot_30s.bin");
        recorder.Save(path);

        var hash = StateHasher.Hash(game.State);
        _output.WriteLine($"Recording saved: {path}");
        _output.WriteLine($"Ticks: {totalTicks}, Actions: {recorder.ActionCount}, Hash: {hash}");

        File.Exists(path).Should().BeTrue();
    }
}
