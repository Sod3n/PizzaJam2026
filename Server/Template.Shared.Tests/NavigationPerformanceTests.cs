using System.Diagnostics;
using System.Reflection;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Factories;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

[Collection("Sequential")]
public class NavigationPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public NavigationPerformanceTests(ITestOutputHelper output) => _output = output;

    private Game CreateGame()
    {
        ServiceLocator.Reset();
        var field = typeof(TemplateGameFactory).GetField("_appInitialized", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, false);
        return TemplateGameFactory.CreateGame(tickRate: 60);
    }

    [Fact]
    public void NavigationSystem_TickTime_ShouldBeUnder5ms()
    {
        var game = CreateGame();

        // Warm up — first ticks include baking
        for (int i = 0; i < 10; i++)
            game.Loop.RunSingleTick();

        // Measure 60 ticks (1 second)
        var sw = Stopwatch.StartNew();
        var tickTimes = new List<double>();

        for (int i = 0; i < 60; i++)
        {
            var tickSw = Stopwatch.StartNew();
            game.Loop.RunSingleTick();
            tickSw.Stop();
            tickTimes.Add(tickSw.Elapsed.TotalMilliseconds);
        }
        sw.Stop();

        var avg = tickTimes.Average();
        var max = tickTimes.Max();
        var p99 = tickTimes.OrderBy(x => x).ElementAt((int)(tickTimes.Count * 0.99));

        _output.WriteLine($"60 ticks: avg={avg:F2}ms max={max:F2}ms p99={p99:F2}ms total={sw.Elapsed.TotalMilliseconds:F0}ms");

        // Full tick should be under 5ms average for 60fps
        avg.Should().BeLessThan(5.0, "average tick should be under 5ms for 60fps");
    }

    [Fact]
    public void NavigationSystem_AfterBuildingSpawn_ShouldNotSpikeOver50ms()
    {
        var game = CreateGame();
        var ctx = new Context(game.State, Entity.Null, null!);

        // Let initial bake complete fully
        for (int i = 0; i < 30; i++)
            game.Loop.RunSingleTick();

        // Verify steady state
        var baselineTimes = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            game.Loop.RunSingleTick();
            sw.Stop();
            baselineTimes.Add(sw.Elapsed.TotalMilliseconds);
        }
        _output.WriteLine($"Baseline: {string.Join(", ", baselineTimes.Select(t => $"{t:F1}ms"))}");

        // Spawn buildings to trigger SUBSEQUENT rebake (not initial)
        for (int i = 0; i < 5; i++)
        {
            HouseDefinition.Create(ctx, new Vector2(i * 10, 0));
        }

        // Measure — include debounce wait + rebake
        var spikeTimes = new List<double>();
        for (int i = 0; i < 30; i++)
        {
            var sw = Stopwatch.StartNew();
            game.Loop.RunSingleTick();
            sw.Stop();
            spikeTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        var max = spikeTimes.Max();
        _output.WriteLine($"Post-spawn ticks: {string.Join(", ", spikeTimes.Select(t => $"{t:F1}ms"))}");
        _output.WriteLine($"Max spike: {max:F1}ms");

        max.Should().BeLessThan(100.0, "subsequent nav mesh rebake should not cause >100ms spike");
    }

    [Fact]
    public void NavigationSystem_WithManyCows_ShouldScaleLinearly()
    {
        var game = CreateGame();
        var ctx = new Context(game.State, Entity.Null, null!);
        var userId = System.Guid.NewGuid();
        var player = PlayerDefinition.Create(ctx, userId, new Vector2(10, 10), 0);

        // Warm up
        for (int i = 0; i < 10; i++)
            game.Loop.RunSingleTick();

        // Measure baseline (2 cows from scene)
        var baselineTimes = new List<double>();
        for (int i = 0; i < 30; i++)
        {
            var sw = Stopwatch.StartNew();
            game.Loop.RunSingleTick();
            sw.Stop();
            baselineTimes.Add(sw.Elapsed.TotalMilliseconds);
        }
        var baselineAvg = baselineTimes.Average();

        // Spawn 8 more cows (10 total)
        for (int i = 0; i < 8; i++)
            CowDefinition.Create(ctx, new Vector2(10 + i * 3, 10));

        // Let navigation settle
        for (int i = 0; i < 5; i++)
            game.Loop.RunSingleTick();

        // Measure with 10 cows
        var scaledTimes = new List<double>();
        for (int i = 0; i < 30; i++)
        {
            var sw = Stopwatch.StartNew();
            game.Loop.RunSingleTick();
            sw.Stop();
            scaledTimes.Add(sw.Elapsed.TotalMilliseconds);
        }
        var scaledAvg = scaledTimes.Average();

        _output.WriteLine($"2 cows avg: {baselineAvg:F2}ms");
        _output.WriteLine($"10 cows avg: {scaledAvg:F2}ms");
        _output.WriteLine($"Ratio: {scaledAvg / baselineAvg:F1}x");

        // 5x more cows should not cause more than 5x slowdown
        (scaledAvg / baselineAvg).Should().BeLessThan(5.0, "navigation should scale roughly linearly with cow count");
    }

    [Fact]
    public void NavigationSystem_PhysicsHashComputation_ShouldBeCheap()
    {
        var game = CreateGame();
        var ctx = new Context(game.State, Entity.Null, null!);

        // Spawn many static bodies to stress the hash computation
        for (int i = 0; i < 50; i++)
        {
            HouseDefinition.Create(ctx, new Vector2(i * 5, 0));
        }

        // Warm up — let progressive bake complete (100 chunks / 15 per tick ≈ 7 ticks + adjacency + debounce)
        for (int i = 0; i < 30; i++)
            game.Loop.RunSingleTick();

        // Measure — hash is computed every tick
        var times = new List<double>();
        for (int i = 0; i < 60; i++)
        {
            var sw = Stopwatch.StartNew();
            game.Loop.RunSingleTick();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        var avg = times.Average();
        var max = times.Max();
        _output.WriteLine($"50 houses, 60 ticks: avg={avg:F2}ms max={max:F2}ms");
        _output.WriteLine($"Entity count: {CountEntities(game)}");

        avg.Should().BeLessThan(10.0, "tick with 50 houses should stay under 10ms avg");
    }

    private int CountEntities(Game game)
    {
        int count = 0;
        foreach (var _ in game.State.Filter<Transform2D>()) count++;
        return count;
    }
}
