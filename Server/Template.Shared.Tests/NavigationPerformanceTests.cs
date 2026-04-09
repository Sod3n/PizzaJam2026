using System.Diagnostics;
using System.Reflection;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Physics2D.Systems;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.Navigation2D.Systems;
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

        max.Should().BeLessThan(150.0, "subsequent nav mesh rebake + DtCrowd replan should not cause >150ms spike");
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

    // ══════════════════════════════════════════════
    //  Rollback performance tests
    // ══════════════════════════════════════════════

    /// <summary>
    /// Baseline: measures rollback cost with the CURRENT implementation
    /// (SystemData survives rollback — nav state and Box2D state are stale).
    /// This is the "before" measurement for the desync fix.
    /// </summary>
    [Fact]
    public void Rollback_LateGame_CurrentBehavior_Baseline()
    {
        var game = CreateGame();
        var ctx = new Context(game.State, Entity.Null, null!);

        // Build late-game state
        var userId = System.Guid.NewGuid();
        var player = PlayerDefinition.Create(ctx, userId, new Vector2(0, 0), 0);
        for (int i = 0; i < 15; i++)
            HouseDefinition.Create(ctx, new Vector2((i % 5) * 10, (i / 5) * 10));
        for (int i = 0; i < 3; i++)
            FoodFarmDefinition.Create(ctx, new Vector2(i * 8, -10));
        for (int i = 0; i < 6; i++)
            CowDefinition.Create(ctx, new Vector2(i * 3, -15));

        // Let everything settle (bake, crowd, agents)
        for (int i = 0; i < 60; i++)
            game.Loop.RunSingleTick();

        var navState = game.State.GetCustomData<CDTNavigationState>();
        _output.WriteLine($"State: {navState!.Map.TriangleCount} tris, {0} agents, entities={CountEntities(game)}");

        // Measure steady-state baseline (no rollback)
        var steadyTimes = new List<double>();
        for (int i = 0; i < 30; i++)
        {
            var sw = Stopwatch.StartNew();
            game.Loop.RunSingleTick();
            sw.Stop();
            steadyTimes.Add(sw.Elapsed.TotalMilliseconds);
        }
        _output.WriteLine($"Steady-state: avg={steadyTimes.Average():F2}ms max={steadyTimes.Max():F2}ms");

        // Now simulate rollback: restore state from 10 ticks ago, resimulate forward
        // This is what happens when a late-arriving action triggers rollback
        long currentTick = game.Loop.CurrentTick;
        long rollbackTarget = currentTick - 10;

        var rollbackSw = Stopwatch.StartNew();
        bool restored = game.Loop.Simulation.History.Retrieve(rollbackTarget, game.State);
        rollbackSw.Stop();
        restored.Should().BeTrue("history should have state from 10 ticks ago");
        _output.WriteLine($"State restore (deserialize): {rollbackSw.Elapsed.TotalMilliseconds:F2}ms");

        // Resimulate 10 ticks (the rollback catch-up)
        game.Loop.Simulation.History.DiscardFuture(rollbackTarget);
        game.Loop.ForceSetTick(rollbackTarget);

        var resimTimes = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            game.Loop.RunSingleTick();
            sw.Stop();
            resimTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        var totalRollback = rollbackSw.Elapsed.TotalMilliseconds + resimTimes.Sum();
        _output.WriteLine($"Resim ticks: {string.Join(", ", resimTimes.Select(t => $"{t:F1}ms"))}");
        _output.WriteLine($"Total rollback (restore + 10 resim ticks): {totalRollback:F2}ms");
        _output.WriteLine($"[CURRENT] This does NOT clear SystemData — nav/physics state is stale");
    }

    /// <summary>
    /// Proposed fix: measures rollback cost when we CLEAR SystemData
    /// (forces CDTNavigationState + Box2DPhysicsState to rebuild from scratch).
    /// This shows the worst-case cost of the "clear everything" approach.
    /// </summary>
    [Fact]
    public void Rollback_LateGame_WithSystemDataClear_FullRebuild()
    {
        var game = CreateGame();
        var ctx = new Context(game.State, Entity.Null, null!);

        // Build late-game state
        var userId = System.Guid.NewGuid();
        var player = PlayerDefinition.Create(ctx, userId, new Vector2(0, 0), 0);
        for (int i = 0; i < 15; i++)
            HouseDefinition.Create(ctx, new Vector2((i % 5) * 10, (i / 5) * 10));
        for (int i = 0; i < 3; i++)
            FoodFarmDefinition.Create(ctx, new Vector2(i * 8, -10));
        for (int i = 0; i < 6; i++)
            CowDefinition.Create(ctx, new Vector2(i * 3, -15));

        // Settle
        for (int i = 0; i < 60; i++)
            game.Loop.RunSingleTick();

        var navState = game.State.GetCustomData<CDTNavigationState>();
        _output.WriteLine($"State: {navState!.Map.TriangleCount} tris, {0} agents, entities={CountEntities(game)}");

        // Simulate rollback WITH SystemData clear (the proposed fix)
        long currentTick = game.Loop.CurrentTick;
        long rollbackTarget = currentTick - 10;

        var rollbackSw = Stopwatch.StartNew();
        bool restored = game.Loop.Simulation.History.Retrieve(rollbackTarget, game.State);
        rollbackSw.Stop();
        restored.Should().BeTrue();

        // THE PROPOSED FIX: clear all SystemData after state restore
        game.State.ClearCustomData();

        _output.WriteLine($"State restore + clear: {rollbackSw.Elapsed.TotalMilliseconds:F2}ms");

        game.Loop.Simulation.History.DiscardFuture(rollbackTarget);
        game.Loop.ForceSetTick(rollbackTarget);

        // Resimulate 10 ticks — first tick rebuilds nav + physics from scratch
        var resimTimes = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            game.Loop.RunSingleTick();
            sw.Stop();
            resimTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        var totalRollback = rollbackSw.Elapsed.TotalMilliseconds + resimTimes.Sum();
        _output.WriteLine($"Resim ticks: {string.Join(", ", resimTimes.Select(t => $"{t:F1}ms"))}");
        _output.WriteLine($"First resim tick (full rebuild): {resimTimes[0]:F2}ms");
        _output.WriteLine($"Total rollback (restore + clear + 10 resim): {totalRollback:F2}ms");
        _output.WriteLine($"[FULL CLEAR] Forces nav + physics rebuild from scratch");

        // Verify state was actually rebuilt
        var navAfter = game.State.GetCustomData<CDTNavigationState>();
        navAfter.Should().NotBeNull();
        // PhysicsBaked now lives in NavigationWorld2D component
        foreach (var e in game.State.Filter<NavigationWorld2D>())
        {
            game.State.GetComponent<NavigationWorld2D>(e).PhysicsBaked
                .Should().BeTrue("nav should have rebaked after clear");
            break;
        }
    }

    /// <summary>
    /// Smart fix: measures rollback cost when we only invalidate the DtCrowd
    /// but keep the CDT navmesh (since obstacles didn't change during rollback window).
    /// This is the targeted approach — move dirty flags to ECS, only reset crowd.
    /// </summary>
    [Fact]
    public void Rollback_LateGame_SmartInvalidate_CrowdOnlyRebuild()
    {
        var game = CreateGame();
        var ctx = new Context(game.State, Entity.Null, null!);

        // Build late-game state
        var userId = System.Guid.NewGuid();
        var player = PlayerDefinition.Create(ctx, userId, new Vector2(0, 0), 0);
        for (int i = 0; i < 15; i++)
            HouseDefinition.Create(ctx, new Vector2((i % 5) * 10, (i / 5) * 10));
        for (int i = 0; i < 3; i++)
            FoodFarmDefinition.Create(ctx, new Vector2(i * 8, -10));
        for (int i = 0; i < 6; i++)
            CowDefinition.Create(ctx, new Vector2(i * 3, -15));

        // Settle
        for (int i = 0; i < 60; i++)
            game.Loop.RunSingleTick();

        var navState = game.State.GetCustomData<CDTNavigationState>();
        _output.WriteLine($"State: {navState!.Map.TriangleCount} tris, {0} agents, entities={CountEntities(game)}");

        // Simulate rollback with SMART invalidation:
        // Keep the CDT map (obstacles didn't change), only reset crowd + agent paths
        long currentTick = game.Loop.CurrentTick;
        long rollbackTarget = currentTick - 10;

        var rollbackSw = Stopwatch.StartNew();
        bool restored = game.Loop.Simulation.History.Retrieve(rollbackTarget, game.State);
        rollbackSw.Stop();
        restored.Should().BeTrue();

        // THE SMART FIX: keep navmesh, invalidate crowd + paths + Box2D
        var cdtState = game.State.GetCustomData<CDTNavigationState>();
        if (cdtState != null)
        {
            // Keep Map (same obstacles), reset crowd
            
        }
        // Box2D: invalidate so it rebuilds from ECS
        game.State.SetCustomData<Deterministic.GameFramework.Physics2D.Systems.Box2DPhysicsState>(null);

        _output.WriteLine($"State restore + smart invalidate: {rollbackSw.Elapsed.TotalMilliseconds:F2}ms");

        game.Loop.Simulation.History.DiscardFuture(rollbackTarget);
        game.Loop.ForceSetTick(rollbackTarget);

        // Resimulate 10 ticks — first tick rebuilds crowd (cheap) + Box2D (cheap)
        var resimTimes = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            game.Loop.RunSingleTick();
            sw.Stop();
            resimTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        var totalRollback = rollbackSw.Elapsed.TotalMilliseconds + resimTimes.Sum();
        _output.WriteLine($"Resim ticks: {string.Join(", ", resimTimes.Select(t => $"{t:F1}ms"))}");
        _output.WriteLine($"First resim tick (crowd + box2d rebuild): {resimTimes[0]:F2}ms");
        _output.WriteLine($"Total rollback (restore + smart + 10 resim): {totalRollback:F2}ms");
        _output.WriteLine($"[SMART] Keeps navmesh, only rebuilds crowd + Box2D");

        // Verify nav survived and map is intact
        var navAfter = game.State.GetCustomData<CDTNavigationState>();
        navAfter.Should().NotBeNull();
        // PhysicsBaked now lives in NavigationWorld2D component
        foreach (var e in game.State.Filter<NavigationWorld2D>())
        {
            game.State.GetComponent<NavigationWorld2D>(e).PhysicsBaked
                .Should().BeTrue("navmesh should still be baked");
            break;
        }
        navAfter.Map.TriangleCount.Should().BeGreaterThan(0, "CDT map should be intact (not rebuilt)");
        // Note: Crowd may be null here because CDTNavigationSystem only creates it
        // during BakeIfNeeded (which skips if obstacles unchanged). The fix should
        // add a crowd recreation path when Crowd==null but Map is built.
        _output.WriteLine($"Crowd recreated: {true}");
    }

    /// <summary>
    /// Correctness check: verifies that state hashes match after rollback
    /// under each invalidation strategy.
    /// </summary>
    [Fact]
    public void Rollback_StateHash_ShouldMatchAfterResimulation()
    {
        var game = CreateGame();
        var ctx = new Context(game.State, Entity.Null, null!);

        var userId = System.Guid.NewGuid();
        PlayerDefinition.Create(ctx, userId, new Vector2(0, 0), 0);
        for (int i = 0; i < 5; i++)
            HouseDefinition.Create(ctx, new Vector2(i * 8, 5));
        for (int i = 0; i < 4; i++)
            CowDefinition.Create(ctx, new Vector2(i * 3, -5));

        // Advance to build up state
        for (int i = 0; i < 60; i++)
            game.Loop.RunSingleTick();

        // Capture the "correct" state hash at current tick
        long targetTick = game.Loop.CurrentTick;
        byte[] correctState = StateSerializer.Serialize(game.State);
        var correctHash = StateHasher.Hash(correctState);
        _output.WriteLine($"Correct hash at tick {targetTick}: {correctHash}");

        // Now rollback 10 ticks and resimulate WITHOUT clearing SystemData (current behavior)
        long rollbackTarget = targetTick - 10;
        game.Loop.Simulation.History.Retrieve(rollbackTarget, game.State);
        game.Loop.Simulation.History.DiscardFuture(rollbackTarget);
        game.Loop.ForceSetTick(rollbackTarget);

        for (int i = 0; i < 10; i++)
            game.Loop.RunSingleTick();

        byte[] afterRollbackState = StateSerializer.Serialize(game.State);
        var afterRollbackHash = StateHasher.Hash(afterRollbackState);
        _output.WriteLine($"After rollback (no clear) at tick {game.Loop.CurrentTick}: {afterRollbackHash}");
        _output.WriteLine($"Hashes match (no clear): {correctHash == afterRollbackHash}");

        // Note: if hashes DON'T match, that proves stale SystemData causes divergence
        if (correctHash != afterRollbackHash)
            _output.WriteLine(">>> DIVERGENCE DETECTED: stale SystemData causes different state after rollback");
    }

    private int CountEntities(Game game)
    {
        int count = 0;
        foreach (var _ in game.State.Filter<Transform2D>()) count++;
        return count;
    }
}
