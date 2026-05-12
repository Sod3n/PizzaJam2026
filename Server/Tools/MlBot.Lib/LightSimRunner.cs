using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;

namespace Template.Shared.Tests;

public class LightSimRunner
{
    private readonly Game _game;
    private readonly ISystem[] _systems;
    private readonly long[] _totalTicks;
    private readonly long[] _callCount;
    private readonly Stopwatch _sw = new();

    public LightSimRunner(Game game)
    {
        _game = game;
        _systems = BuildSystems();
        _totalTicks = new long[_systems.Length];
        _callCount = new long[_systems.Length];
    }

    public void Tick()
    {
        var sim = _game.Loop.Simulation;
        sim.ForceSetTick(sim.CurrentTick + 1);
        RunAll();
    }

    public void RunSystems() => RunAll();

    private void RunAll()
    {
        var state = _game.State;
        for (int i = 0; i < _systems.Length; i++)
        {
            _sw.Restart();
            _systems[i].Update(state);
            _sw.Stop();
            _totalTicks[i] += _sw.ElapsedTicks;
            _callCount[i]++;
        }
    }

    public string PerformanceReport(int topN = 15)
    {
        double freq = Stopwatch.Frequency;
        var rows = _systems
            .Select((s, i) => (Name: s.GetType().Name,
                               TotalMs: _totalTicks[i] / freq * 1000.0,
                               Calls: _callCount[i]))
            .OrderByDescending(r => r.TotalMs)
            .ToList();

        double grand = rows.Sum(r => r.TotalMs);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== LightSimRunner per-system (total={grand:F0} ms across {rows.FirstOrDefault().Calls} calls) ===");
        sb.AppendLine($"{"System",-40} {"Total ms",10} {"Avg µs/call",14} {"%",6}");
        foreach (var r in rows.Take(topN))
        {
            double avgUs = r.Calls > 0 ? r.TotalMs * 1000.0 / r.Calls : 0;
            double pct = grand > 0 ? r.TotalMs / grand * 100.0 : 0;
            sb.AppendLine($"{r.Name,-40} {r.TotalMs,10:F1} {avgUs,14:F2} {pct,6:F1}");
        }
        return sb.ToString();
    }

    private static ISystem[] BuildSystems()
    {
        var templateAssembly = typeof(Template.Shared.Systems.HelperSystem).Assembly;
        var all = ServiceLocator.GetAll<ISystem>()
            .Where(s => s.GetType().Assembly == templateAssembly)
            .ToList();

        Template.Shared.Systems.InteractFallbackSystem fallback = null;
        var ordered = new List<ISystem> { new MockNavigationSystem() };
        foreach (var s in all)
        {
            if (s is Template.Shared.Systems.InteractFallbackSystem fb) { fallback = fb; continue; }
            ordered.Add(s);
        }
        if (fallback != null) ordered.Add(fallback);
        return ordered.ToArray();
    }
}
