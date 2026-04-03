using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Navigation2D.Systems;
using Deterministic.GameFramework.Physics2D.Systems;
using Deterministic.GameFramework.TwoD;

namespace Template.Shared.Tests;

/// <summary>
/// Performance-testing sim runner that uses real physics, navigation, and transform
/// systems instead of mocks. Collects per-tick and per-phase timing data.
/// </summary>
public class FullSimRunner : IDisposable
{
    private readonly Game _game;

    // Infrastructure systems (order matters)
    private readonly TransformSystem _transformSystem;
    private readonly RapierPhysicsSystem _physicsSystem;
    private readonly ISystem _navigationSystem;

    // Game-logic systems (same set as LightSimRunner)
    private readonly ISystem[] _gameSystems;

    // --- Timing bookkeeping ---
    private readonly Stopwatch _sw = new();
    private readonly Stopwatch _wallClock = new();

    // Cumulative phase totals (ticks, not ms — convert on report)
    private long _totalTransformTicks;
    private long _totalPhysicsTicks;
    private long _totalNavigationTicks;
    private long _totalGameSystemsTicks;

    // Last-tick phase times (Stopwatch ticks)
    public long LastTransformTicks { get; private set; }
    public long LastPhysicsTicks { get; private set; }
    public long LastNavigationTicks { get; private set; }
    public long LastGameSystemsTicks { get; private set; }

    // Min/max per-phase (Stopwatch ticks)
    private long _minTransform = long.MaxValue, _maxTransform;
    private long _minPhysics = long.MaxValue, _maxPhysics;
    private long _minNavigation = long.MaxValue, _maxNavigation;
    private long _minGameSystems = long.MaxValue, _maxGameSystems;

    // Per-tick total times in milliseconds (for percentile calculations)
    private readonly List<double> _tickTimesMs = new();

    public int TotalTicks { get; private set; }

    public FullSimRunner(Game game, bool useCDTNavigation = false)
    {
        _game = game;

        _transformSystem = new TransformSystem();       // order -1000
        _physicsSystem = new RapierPhysicsSystem();      // order 0
        _navigationSystem = useCDTNavigation
            ? new CDTNavigationSystem()                  // order 500 (CDT-based)
            : new NavigationSystem();                    // order 500 (grid-based)

        _gameSystems = new ISystem[]
        {
            new Template.Shared.Systems.AnimationsSystem(),
            new Template.Shared.Systems.CowSystem(),
            new Template.Shared.Systems.HelperSystem(),
            new Template.Shared.Systems.GrassSpawnSystem(),
            new Template.Shared.Systems.CoinCollectionSystem(),
            new Template.Shared.Systems.MetricsSystem(),
        };

        _wallClock.Start();
    }

    /// <summary>Advance one tick: increment time, run all systems with timing.</summary>
    public void Tick()
    {
        var sim = _game.Loop.Simulation;
        sim.ForceSetTick(sim.CurrentTick + 1);
        RunAllSystemsTimed();
    }

    /// <summary>Run all systems without advancing time (for post-dispatch processing).</summary>
    public void RunSystems()
    {
        RunAllSystemsTimed();
    }

    private void RunAllSystemsTimed()
    {
        var state = _game.State;
        long tickStart = Stopwatch.GetTimestamp();

        // --- Transform ---
        _sw.Restart();
        _transformSystem.Update(state);
        _sw.Stop();
        LastTransformTicks = _sw.ElapsedTicks;

        // --- Physics ---
        _sw.Restart();
        _physicsSystem.Update(state);
        _sw.Stop();
        LastPhysicsTicks = _sw.ElapsedTicks;

        // --- Navigation ---
        _sw.Restart();
        _navigationSystem.Update(state);
        _sw.Stop();
        LastNavigationTicks = _sw.ElapsedTicks;

        // --- Game systems ---
        _sw.Restart();
        foreach (var sys in _gameSystems)
            sys.Update(state);
        _sw.Stop();
        LastGameSystemsTicks = _sw.ElapsedTicks;

        // Accumulate totals
        _totalTransformTicks += LastTransformTicks;
        _totalPhysicsTicks += LastPhysicsTicks;
        _totalNavigationTicks += LastNavigationTicks;
        _totalGameSystemsTicks += LastGameSystemsTicks;

        // Min/max
        if (LastTransformTicks < _minTransform) _minTransform = LastTransformTicks;
        if (LastTransformTicks > _maxTransform) _maxTransform = LastTransformTicks;
        if (LastPhysicsTicks < _minPhysics) _minPhysics = LastPhysicsTicks;
        if (LastPhysicsTicks > _maxPhysics) _maxPhysics = LastPhysicsTicks;
        if (LastNavigationTicks < _minNavigation) _minNavigation = LastNavigationTicks;
        if (LastNavigationTicks > _maxNavigation) _maxNavigation = LastNavigationTicks;
        if (LastGameSystemsTicks < _minGameSystems) _minGameSystems = LastGameSystemsTicks;
        if (LastGameSystemsTicks > _maxGameSystems) _maxGameSystems = LastGameSystemsTicks;

        // Per-tick total (ms)
        long tickEnd = Stopwatch.GetTimestamp();
        double tickMs = (tickEnd - tickStart) * 1000.0 / Stopwatch.Frequency;
        _tickTimesMs.Add(tickMs);

        TotalTicks++;
    }

    /// <summary>Generate a human-readable performance report with percentiles and per-phase breakdown.</summary>
    public string PerformanceReport()
    {
        if (TotalTicks == 0)
            return "No ticks recorded.";

        _wallClock.Stop();
        double wallSeconds = _wallClock.Elapsed.TotalSeconds;
        double simSeconds = TotalTicks / 60.0; // 60 ticks/sec

        double freq = Stopwatch.Frequency;

        double avgTransformMs = _totalTransformTicks / (double)TotalTicks / freq * 1000.0;
        double avgPhysicsMs = _totalPhysicsTicks / (double)TotalTicks / freq * 1000.0;
        double avgNavigationMs = _totalNavigationTicks / (double)TotalTicks / freq * 1000.0;
        double avgGameMs = _totalGameSystemsTicks / (double)TotalTicks / freq * 1000.0;
        double avgTotalMs = avgTransformMs + avgPhysicsMs + avgNavigationMs + avgGameMs;

        // Percentiles from per-tick list
        var sorted = _tickTimesMs.OrderBy(x => x).ToList();
        double p50 = Percentile(sorted, 0.50);
        double p95 = Percentile(sorted, 0.95);
        double p99 = Percentile(sorted, 0.99);
        double maxMs = sorted[^1];
        double minMs = sorted[0];

        double pctTransform = avgTotalMs > 0 ? avgTransformMs / avgTotalMs * 100.0 : 0;
        double pctPhysics = avgTotalMs > 0 ? avgPhysicsMs / avgTotalMs * 100.0 : 0;
        double pctNavigation = avgTotalMs > 0 ? avgNavigationMs / avgTotalMs * 100.0 : 0;
        double pctGame = avgTotalMs > 0 ? avgGameMs / avgTotalMs * 100.0 : 0;

        double realtimeRatio = wallSeconds > 0 ? simSeconds / wallSeconds : 0;

        return $"""
            === FullSimRunner Performance Report ===
            Total ticks:    {TotalTicks}
            Wall time:      {wallSeconds:F2}s
            Sim time:       {simSeconds:F2}s
            Realtime ratio: {realtimeRatio:F2}x (sim/wall)

            --- Tick Time (ms) ---
            Avg:  {avgTotalMs:F3}
            P50:  {p50:F3}
            P95:  {p95:F3}
            P99:  {p99:F3}
            Min:  {minMs:F3}
            Max:  {maxMs:F3}

            --- Per-Phase Breakdown (avg ms / % of tick) ---
            Transform:    {avgTransformMs,8:F3} ms  ({pctTransform,5:F1}%)
            Physics:      {avgPhysicsMs,8:F3} ms  ({pctPhysics,5:F1}%)
            Navigation:   {avgNavigationMs,8:F3} ms  ({pctNavigation,5:F1}%)
            GameSystems:  {avgGameMs,8:F3} ms  ({pctGame,5:F1}%)
            """;
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        double index = p * (sorted.Count - 1);
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        double frac = index - lower;
        return sorted[lower] * (1.0 - frac) + sorted[upper] * frac;
    }

    public void Dispose()
    {
        _wallClock.Stop();
        _physicsSystem.Dispose();
    }
}
