using System;
using System.Collections.Generic;
using System.Reflection;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.Navigation2D.Systems;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Factories;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

/// <summary>
/// Tests that CDT navigation pathfinding is deterministic:
/// given the same (position, target, navmesh), ComputeSmoothedPath
/// must produce identical waypoints every time, regardless of whether
/// the navigation system was freshly initialized ("cold") or has been
/// running for a while ("warm").
/// </summary>
[Collection("Sequential")]
public class NavigationDeterminismTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public NavigationDeterminismTests(ITestOutputHelper output)
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

    /// <summary>
    /// Collect all cow navigation paths at the current tick.
    /// Returns (entityId, position, target, velocity, pathIndex) for each navigating cow.
    /// </summary>
    private List<(int entityId, Vector2 pos, Vector2 target, Vector2 velocity, byte pathIndex)>
        SnapshotCowNavState(Game game)
    {
        var result = new List<(int, Vector2, Vector2, Vector2, byte)>();
        foreach (var entity in game.State.Filter<NavigationAgent2D, Transform2D, CowComponent>())
        {
            ref var agent = ref game.State.GetComponent<NavigationAgent2D>(entity);
            ref var transform = ref game.State.GetComponent<Transform2D>(entity);
            result.Add((entity.Id, transform.Position, agent.TargetPosition, agent.Velocity, agent.PathIndex));
        }
        result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return result;
    }

    /// <summary>
    /// Run a game to tick N, snapshot cow nav state.
    /// Then run a SECOND game to tick N from scratch.
    /// Cow nav state should be identical.
    /// </summary>
    [Fact]
    public void TwoFreshGames_SameInputs_ShouldHaveIdenticalNavState()
    {
        var userId = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var game1 = CreateGame();
        AddPlayer(game1, userId);
        for (int i = 0; i < 200; i++)
            game1.Loop.RunSingleTick();

        var game2 = CreateGame();
        AddPlayer(game2, userId);
        for (int i = 0; i < 200; i++)
            game2.Loop.RunSingleTick();

        var state1 = SnapshotCowNavState(game1);
        var state2 = SnapshotCowNavState(game2);

        state1.Count.Should().Be(state2.Count, "same number of cows");

        for (int i = 0; i < state1.Count; i++)
        {
            var s1 = state1[i];
            var s2 = state2[i];
            _output.WriteLine($"Cow {s1.entityId}: pos=({s1.pos.X},{s1.pos.Y}) vel=({s1.velocity.X},{s1.velocity.Y}) idx={s1.pathIndex}");

            s1.entityId.Should().Be(s2.entityId, $"cow {i} entity ID");
            s1.pos.Should().Be(s2.pos, $"cow {i} position");
            s1.target.Should().Be(s2.target, $"cow {i} target");
            s1.velocity.Should().Be(s2.velocity, $"cow {i} velocity");
            s1.pathIndex.Should().Be(s2.pathIndex, $"cow {i} pathIndex");
        }
    }

    /// <summary>
    /// Run a game to tick N. Snapshot state.
    /// Rollback to tick N-K. Resimulate to tick N.
    /// Compare cow nav state: should be identical.
    ///
    /// This tests "cold vs warm" — after rollback the nav system is cold
    /// (AgentPaths cleared, Crowd recreated), but should produce identical
    /// velocity/position as the warm run.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(18)]
    [InlineData(30)]
    public void ColdVsWarm_SameTickSameInputs_ShouldProduceIdenticalNavState(int rollbackTicks)
    {
        var game = CreateGame();
        var userId = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        AddPlayer(game, userId);

        // Run to tick 200 (cows actively navigating)
        for (int i = 0; i < 200; i++)
            game.Loop.RunSingleTick();

        // Snapshot "warm" state
        long targetTick = game.Loop.CurrentTick;
        var warmState = SnapshotCowNavState(game);
        byte[] warmData = StateSerializer.Serialize(game.State);
        var warmHash = StateHasher.Hash(warmData);

        // Rollback
        long rollbackTarget = targetTick - rollbackTicks;
        bool restored = game.Loop.Simulation.History.Retrieve(rollbackTarget, game.State);
        restored.Should().BeTrue($"should be able to rollback {rollbackTicks} ticks");

        game.Loop.Simulation.History.DiscardFuture(rollbackTarget);
        game.Loop.ForceSetTick(rollbackTarget);

        // Resimulate (cold — AgentPaths cleared by Invalidate)
        for (int i = 0; i < rollbackTicks; i++)
            game.Loop.RunSingleTick();

        // Snapshot "cold" state
        var coldState = SnapshotCowNavState(game);
        var coldHash = StateHasher.Hash(game.State);

        _output.WriteLine($"Rollback {rollbackTicks} ticks: warm={warmHash}, cold={coldHash}, match={warmHash == coldHash}");

        // Compare per-cow
        int mismatches = 0;
        for (int i = 0; i < Math.Min(warmState.Count, coldState.Count); i++)
        {
            var w = warmState[i];
            var c = coldState[i];

            bool posMatch = w.pos == c.pos;
            bool velMatch = w.velocity == c.velocity;
            bool idxMatch = w.pathIndex == c.pathIndex;

            if (!posMatch || !velMatch || !idxMatch)
            {
                mismatches++;
                _output.WriteLine($"  COW {w.entityId} MISMATCH:");
                if (!posMatch) _output.WriteLine($"    pos: warm=({w.pos.X},{w.pos.Y}) cold=({c.pos.X},{c.pos.Y})");
                if (!velMatch) _output.WriteLine($"    vel: warm=({w.velocity.X},{w.velocity.Y}) cold=({c.velocity.X},{c.velocity.Y})");
                if (!idxMatch) _output.WriteLine($"    idx: warm={w.pathIndex} cold={c.pathIndex}");
            }
        }

        _output.WriteLine($"Mismatches: {mismatches}");
        warmHash.Should().Be(coldHash,
            $"cold navigation (after {rollbackTicks}-tick rollback) should produce identical state as warm");
    }

    /// <summary>
    /// Two-instance test: both run to tick N. Then one rollbacks K ticks and resimulates.
    /// The other continues normally. At tick N they should have identical state.
    ///
    /// This is the exact scenario that causes production desyncs: server rollbacks
    /// from a late client action, client never rolled back.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(18)]
    [InlineData(30)]
    public void TwoInstances_OneRollsBack_ShouldMatchAtSameTick(int rollbackTicks)
    {
        var userId = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var server = CreateGame();
        var client = CreateGame();

        AddPlayer(server, userId);
        AddPlayer(client, userId);

        // Run both to tick 200
        for (int i = 0; i < 200; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Verify in sync
        var h1 = StateHasher.Hash(server.State);
        var h2 = StateHasher.Hash(client.State);
        h1.Should().Be(h2, "should be in sync at tick 200");

        // Server rollbacks K ticks, resimulates
        long targetTick = server.Loop.CurrentTick;
        long rollbackTarget = targetTick - rollbackTicks;

        bool restored = server.Loop.Simulation.History.Retrieve(rollbackTarget, server.State);
        restored.Should().BeTrue();
        server.Loop.Simulation.History.DiscardFuture(rollbackTarget);
        server.Loop.ForceSetTick(rollbackTarget);

        for (int i = 0; i < rollbackTicks; i++)
            server.Loop.RunSingleTick();

        // Client did nothing — still at tick 200
        // Compare
        var serverHash = StateHasher.Hash(server.State);
        var clientHash = StateHasher.Hash(client.State);

        _output.WriteLine($"Rollback {rollbackTicks}: server={serverHash}, client={clientHash}, match={serverHash == clientHash}");

        if (serverHash != clientHash)
        {
            var serverSnap = SnapshotCowNavState(server);
            var clientSnap = SnapshotCowNavState(client);
            for (int i = 0; i < Math.Min(serverSnap.Count, clientSnap.Count); i++)
            {
                var s = serverSnap[i];
                var c = clientSnap[i];
                if (s.Item2 != c.Item2 || s.Item4 != c.Item4 || s.Item5 != c.Item5)
                {
                    _output.WriteLine($"  COW {s.Item1}:");
                    _output.WriteLine($"    pos: server=({s.Item2.X},{s.Item2.Y}) client=({c.Item2.X},{c.Item2.Y})");
                    _output.WriteLine($"    vel: server=({s.Item4.X},{s.Item4.Y}) client=({c.Item4.X},{c.Item4.Y})");
                    _output.WriteLine($"    idx: server={s.Item5} client={c.Item5}");
                }
            }

            byte[] sd = StateSerializer.Serialize(server.State);
            byte[] cd = StateSerializer.Serialize(client.State);
            StateDumper.LogStateDiff($"TwoInst_Rollback{rollbackTicks}", targetTick, sd, cd);
        }

        serverHash.Should().Be(clientHash,
            $"server (rolled back {rollbackTicks} ticks + resimulated) should match client (no rollback)");
    }

    /// <summary>
    /// Specifically tests ComputeSmoothedPath determinism:
    /// compute a path, then compute it again from the same inputs.
    /// Waypoints must be identical.
    /// </summary>
    [Fact]
    public void ComputeSmoothedPath_SameInputs_ShouldProduceIdenticalWaypoints()
    {
        var game = CreateGame();
        var userId = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        AddPlayer(game, userId);

        // Run to build navmesh
        for (int i = 0; i < 60; i++)
            game.Loop.RunSingleTick();

        var cdtState = game.State.GetCustomData<CDTNavigationState>();
        cdtState.Should().NotBeNull();
        cdtState!.Map.IsBuilt.Should().BeTrue();

        // Pick some test positions
        var testCases = new (Vector2 from, Vector2 to)[]
        {
            (new Vector2(0, 0), new Vector2(10, 10)),
            (new Vector2(-5, -5), new Vector2(5, 5)),
            (new Vector2(2, 2), new Vector2(-10, 8)),
            (new Vector2(-3, 7), new Vector2(8, -4)),
        };

        Float agentRadius = new Float(0.5f);

        foreach (var (from, to) in testCases)
        {
            var path1 = new List<Vector2>();
            var path2 = new List<Vector2>();

            bool found1 = cdtState.Map.ComputeSmoothedPath(from, to, path1, agentRadius);
            bool found2 = cdtState.Map.ComputeSmoothedPath(from, to, path2, agentRadius);

            _output.WriteLine($"Path ({from.X},{from.Y})→({to.X},{to.Y}): found={found1}, waypoints={path1.Count}");

            found1.Should().Be(found2, "both calls should find/not find path");
            path1.Count.Should().Be(path2.Count, "same number of waypoints");

            for (int i = 0; i < path1.Count; i++)
            {
                path1[i].Should().Be(path2[i], $"waypoint {i} should be identical");
            }
        }
    }
}
