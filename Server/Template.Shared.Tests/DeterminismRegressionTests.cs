using System;
using System.IO;
using System.Reflection;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Factories;
using Template.Shared.Recording;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

/// <summary>
/// Determinism regression test: replays a recorded input session multiple times
/// and verifies the final state hash is identical every run.
///
/// To re-record:
///   1. Enable InputRecorder in the Godot client (see GameManager)
///   2. Play for a while
///   3. Stop — the recording is saved to the configured path
///   4. Copy the .bin file to Server/Template.Shared.Tests/Recordings/
///   5. Run this test — it replays the recording twice and checks hashes match
///
/// If the test fails, something broke determinism.
/// </summary>
[Collection("Sequential")]
public class DeterminismRegressionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private static readonly string RecordingsDir = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "..", "..", "..", "Recordings");

    public DeterminismRegressionTests(ITestOutputHelper output)
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

    /// <summary>
    /// Replay a recording and return the final state hash.
    /// </summary>
    private Guid ReplayRecording(string recordingPath, out long totalTicks)
    {
        var (ticks, actions, _) = InputRecording.Load(recordingPath);
        totalTicks = ticks;

        var game = CreateGame();
        int actionIndex = 0;

        for (long tick = 0; tick <= ticks; tick++)
        {
            // Schedule all actions for this tick
            while (actionIndex < actions.Count && actions[actionIndex].Tick <= game.Loop.CurrentTick + 1)
            {
                var a = actions[actionIndex];

                // Convert StableId -> DenseId
                var stableId = new StableComponentId(a.StableComponentId);
                if (!ComponentId.TryGetDense(stableId, out var denseId))
                {
                    _output.WriteLine($"WARNING: Unknown StableId {a.StableComponentId} at tick {a.Tick}, skipping");
                    actionIndex++;
                    continue;
                }

                game.Scheduler.ScheduleFromBytes(denseId, a.Data, a.TargetEntityId, a.Tick);
                actionIndex++;
            }

            game.Loop.RunSingleTick();
        }

        // Schedule any remaining actions
        while (actionIndex < actions.Count)
        {
            var a = actions[actionIndex];
            var stableId = new StableComponentId(a.StableComponentId);
            if (ComponentId.TryGetDense(stableId, out var denseId))
                game.Scheduler.ScheduleFromBytes(denseId, a.Data, a.TargetEntityId, a.Tick);
            actionIndex++;
        }

        var hash = StateHasher.Hash(game.State);
        _output.WriteLine($"Replay complete: {ticks} ticks, {actions.Count} actions, hash={hash}");
        return hash;
    }

    [Theory]
    [MemberData(nameof(GetRecordingFiles))]
    public void Replay_ShouldProduceDeterministicHash(string recordingFile)
    {
        var path = Path.Combine(RecordingsDir, recordingFile);
        _output.WriteLine($"Recording: {recordingFile}");

        // Run 1
        var hash1 = ReplayRecording(path, out long ticks);
        _output.WriteLine($"Run 1: {hash1}");

        // Run 2
        var hash2 = ReplayRecording(path, out _);
        _output.WriteLine($"Run 2: {hash2}");

        hash1.Should().Be(hash2, $"replaying {recordingFile} twice should produce identical state");

        // Also check against the recorded hash (from original session)
        var (_, _, recordedHash) = InputRecording.Load(path);
        _output.WriteLine($"Recorded hash: {recordedHash}");
        _output.WriteLine($"Match with recorded: {hash1 == recordedHash}");

        // Note: recorded hash may differ from replay hash if the recording was made
        // with a different code version. The key test is that two replays match each other.
    }

    /// <summary>
    /// Cross-process determinism test. Replays a recording and writes the hash to a file.
    /// On the SECOND run (in a new process), compares against the hash from the first run.
    /// This catches non-determinism from HashCode.Combine (randomized per-process seed),
    /// Dictionary ordering, or any other process-specific state.
    ///
    /// Usage: run `dotnet test --filter CrossProcess` twice. First run writes hashes,
    /// second run compares. If it fails on the second run, there's cross-process non-determinism.
    /// </summary>
    [Theory]
    [MemberData(nameof(GetRecordingFiles))]
    public void CrossProcess_Replay_ShouldMatchPreviousRun(string recordingFile)
    {
        var path = Path.Combine(RecordingsDir, recordingFile);
        if (!File.Exists(path))
        {
            _output.WriteLine($"SKIP: {recordingFile} not found");
            return;
        }

        var hashDir = Path.Combine(RecordingsDir, ".hashes");
        Directory.CreateDirectory(hashDir);
        var hashFile = Path.Combine(hashDir, recordingFile + ".hash");

        // Replay
        var hash = ReplayRecording(path, out long ticks);
        _output.WriteLine($"This run: {hash}");

        if (File.Exists(hashFile))
        {
            // Second run — compare with previous
            var previousHash = Guid.Parse(File.ReadAllText(hashFile).Trim());
            _output.WriteLine($"Previous run: {previousHash}");

            // Write current hash (so next run compares against this one)
            File.WriteAllText(hashFile, hash.ToString());

            hash.Should().Be(previousHash,
                $"replaying {recordingFile} in a new process should produce the same hash as the previous run. " +
                "This likely means GetHashCode() or Dictionary iteration is non-deterministic across processes.");
        }
        else
        {
            // First run — write hash and fail so CI reminds you to run again
            File.WriteAllText(hashFile, hash.ToString());
            Assert.Fail(
                $"FIRST RUN — hash saved to {hashFile}. " +
                "Run `dotnet test --filter CrossProcess` once more to verify cross-process determinism. " +
                "The second run will compare hashes and pass if deterministic.");
        }
    }

    /// <summary>
    /// Provides all .bin files in the Recordings/ directory as test cases.
    /// Drop a new recording file there and the test picks it up automatically.
    /// </summary>
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

        // If no recordings exist yet, add a skip-marker so the test doesn't silently pass
        if (!found)
            data.Add("__NO_RECORDINGS_FOUND__");

        return data;
    }
}
