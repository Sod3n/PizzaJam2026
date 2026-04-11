using System;
using System.Collections.Generic;
using System.Reflection;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Factories;
using Template.Shared.Features.Movement;
using Template.Shared.Recording;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

/// <summary>
/// Catches the production desync: two identical Game instances running 1200+ ticks
/// with continuous player input and 300ms (18-tick) delayed action delivery to the
/// "server" side. The server rolls back on each late arrival.
///
/// In production, this manifests as cow position divergence after ~20 seconds of play.
/// The test verifies state hashes at 60-tick intervals, mirroring the real
/// StateVerificationService.
///
/// This test is intentionally flaky-tolerant: it runs multiple iterations because
/// the desync may not trigger every run (depends on exact cow following behavior).
/// </summary>
[Collection("Sequential")]
public class LongRunDesyncTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public LongRunDesyncTests(ITestOutputHelper output)
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

    private static Guid HashAtTick(Game game, long tick)
    {
        if (game.Loop.Simulation.History.TryGetSnapshotData(tick, out byte[]? data))
            return StateHasher.Hash(data!);
        throw new Exception($"Tick {tick} not in history (current={game.Loop.CurrentTick})");
    }

    /// <summary>
    /// Generates a realistic movement pattern: walk in various directions with
    /// pauses, direction changes, and interact actions. Mirrors actual player
    /// behavior that triggers cow following and nav agent pathfinding.
    /// </summary>
    private static List<(int tick, Vector2 dir, Float speed)> GenerateMovementPattern(int seed, int totalTicks)
    {
        var pattern = new List<(int tick, Vector2 dir, Float speed)>();
        var directions = new Vector2[]
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
            new Vector2(1, 1).Normalized, new Vector2(-1, 1).Normalized,
            new Vector2(1, -1).Normalized, new Vector2(-0.5f, 0.8f).Normalized,
        };

        int t = 10;
        int dirIdx = seed % directions.Length;

        while (t < totalTicks - 60)
        {
            // Move in a direction for 20-80 ticks
            int moveDuration = 20 + ((t * 7 + seed) % 60);
            pattern.Add((t, directions[dirIdx % directions.Length], new Float(15)));

            t += moveDuration;

            // Pause for 5-30 ticks
            int pauseDuration = 5 + ((t * 3 + seed) % 25);
            pattern.Add((t, Vector2.Zero, (Float)0));
            t += pauseDuration;

            dirIdx++;
        }

        // Final stop
        pattern.Add((totalTicks - 30, Vector2.Zero, (Float)0));
        return pattern;
    }

    /// <summary>
    /// Simulates production scenario: client predicts immediately, server receives
    /// actions with 300ms delay and rolls back. Runs for 1200+ ticks (20+ seconds)
    /// to catch gradual cow position divergence.
    ///
    /// Checks state hash every 60 ticks (same as StateVerificationService).
    /// </summary>
    [Theory]
    [InlineData(18, 1800, 0, "300ms_run0")]
    [InlineData(18, 1800, 1, "300ms_run1")]
    [InlineData(18, 1800, 2, "300ms_run2")]
    [InlineData(18, 2400, 3, "300ms_long_run3")]
    [InlineData(30, 1800, 0, "500ms_run0")]
    public void LongRun_WithLatency_ShouldNotDesync(int networkDelay, int totalTicks, int seed, string label)
    {
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        // Verify initial sync
        server.Loop.RunSingleTick();
        client.Loop.RunSingleTick();
        var sh = HashAtTick(server, server.Loop.CurrentTick);
        var ch = HashAtTick(client, client.Loop.CurrentTick);
        sh.Should().Be(ch, "should be in sync after first tick");

        // Generate movement pattern
        var movements = GenerateMovementPattern(seed, totalTicks);
        int moveIdx = 0;

        var pendingActions = new Queue<(int deliveryTick, long execTick, SetMoveDirectionAction action)>();

        int desyncCount = 0;
        int checksPerformed = 0;
        long firstDesyncTick = -1;

        for (int tick = 1; tick < totalTicks; tick++)
        {
            // Client sends actions from movement pattern
            while (moveIdx < movements.Count && movements[moveIdx].tick <= tick)
            {
                var m = movements[moveIdx];
                long execTick = client.Loop.CurrentTick + 5;

                var action = new SetMoveDirectionAction(m.dir, m.speed);
                client.Loop.ScheduleOnTick(execTick, action, clientPlayer);
                pendingActions.Enqueue((tick + networkDelay, execTick, action));

                moveIdx++;
            }

            // Deliver to server (delayed — triggers rollback)
            while (pendingActions.Count > 0 && pendingActions.Peek().deliveryTick <= tick)
            {
                var d = pendingActions.Dequeue();
                server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
            }

            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();

            // Check hash every 60 ticks (mirrors StateVerificationService)
            if (tick % 60 == 0 && tick >= 120)
            {
                // Check a confirmed tick (60 ticks behind current — past any prediction window)
                long confirmTick = server.Loop.CurrentTick - 60;
                if (confirmTick <= 0) continue;

                try
                {
                    var sHash = HashAtTick(server, confirmTick);
                    var cHash = HashAtTick(client, confirmTick);
                    checksPerformed++;

                    if (sHash != cHash)
                    {
                        desyncCount++;
                        if (firstDesyncTick < 0) firstDesyncTick = confirmTick;

                        _output.WriteLine($"  DESYNC at tick {tick}, confirmed tick {confirmTick}");

                        // Log diff for first 2 desyncs
                        if (desyncCount <= 2)
                        {
                            server.Loop.Simulation.History.TryGetSnapshotData(confirmTick, out byte[]? sd);
                            client.Loop.Simulation.History.TryGetSnapshotData(confirmTick, out byte[]? cd);
                            if (sd != null && cd != null)
                                StateDumper.LogStateDiff($"LongRun_{label}_{confirmTick}", confirmTick, cd, sd);
                        }
                    }
                }
                catch
                {
                    // Tick not in history — skip
                }
            }
        }

        _output.WriteLine($"[{label}] {totalTicks} ticks, {checksPerformed} hash checks, {desyncCount} desyncs" +
            (firstDesyncTick >= 0 ? $" (first at tick {firstDesyncTick})" : ""));

        desyncCount.Should().Be(0,
            $"[{label}] after {totalTicks} ticks with {networkDelay}-tick network delay, " +
            $"state should remain in sync (first desync at tick {firstDesyncTick})");
    }

    /// <summary>
    /// Tests breeding at a love house with network latency.
    /// Sets up breed state directly (2 cows in love house, player in breed state),
    /// then spams breed click InteractActions with delayed delivery to the server.
    /// This reproduces the FollowingCow + LoveHouseComponent off-by-1 desync.
    /// </summary>
    [Theory]
    [InlineData(18, "breed_300ms")]
    [InlineData(30, "breed_500ms")]
    [InlineData(0, "breed_nolatency")]
    public void Breed_WithLatency_ShouldNotDesync(int networkDelay, string label)
    {
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        // Run a few ticks to stabilize
        for (int i = 0; i < 5; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        // Set up breed scenario on both games identically
        SetupBreedState(server, serverPlayer, userId);
        SetupBreedState(client, clientPlayer, userId);

        // Verify sync before breeding
        var sh = StateHasher.Hash(server.State);
        var ch = StateHasher.Hash(client.State);
        sh.Should().Be(ch, "should be in sync before breeding starts");

        // Spam breed click InteractActions
        int breedClicks = 30; // More than enough to complete any breed
        int clickInterval = 3; // Every 3 ticks
        var pendingActions = new Queue<(int deliveryTick, long execTick, InteractAction action)>();
        int desyncCount = 0;
        long firstDesyncTick = -1;
        int totalTicks = breedClicks * clickInterval + 300; // Extra ticks for completion + cooldown

        // Track rollbacks
        int serverRollbacks = 0;
        long serverDirtyBefore = long.MaxValue;
        int serverDuplicates = 0;

        for (int tick = 0; tick < totalTicks; tick++)
        {
            // Schedule breed clicks
            if (tick % clickInterval == 0 && tick < breedClicks * clickInterval)
            {
                long execTick = client.Loop.CurrentTick + 5;
                var action = new InteractAction { UserId = userId };

                client.Loop.ScheduleOnTick(execTick, action, clientPlayer);

                if (networkDelay > 0)
                    pendingActions.Enqueue((tick + networkDelay, execTick, action));
                else
                    server.Loop.ScheduleOnTick(execTick, action, serverPlayer);
            }

            // Deliver delayed actions to server
            while (pendingActions.Count > 0 && pendingActions.Peek().deliveryTick <= tick)
            {
                var d = pendingActions.Dequeue();
                long dirtyBefore = server.Scheduler.EarliestDirtyTick;
                server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
                long dirtyAfter = server.Scheduler.EarliestDirtyTick;
                bool willRollback = dirtyAfter < server.Loop.CurrentTick;
                bool wasDuplicate = dirtyBefore == dirtyAfter && dirtyAfter == long.MaxValue;
                _output.WriteLine($"  DELIVER execTick={d.execTick} serverTick={server.Loop.CurrentTick} dirty={dirtyAfter} rollback={willRollback} dup={wasDuplicate}");
            }

            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();

            // Track breed completion on both sides
            foreach (var lh in server.State.Filter<LoveHouseComponent>())
            {
                var slh = server.State.GetComponent<LoveHouseComponent>(lh);
                if (slh.CooldownTicksRemaining == LoveHouseComponent.BreedCooldownTicks)
                {
                    _output.WriteLine($"  SERVER breed complete at serverTick={server.Loop.CurrentTick} testTick={tick}");
                    // Only log once
                    server.State.GetComponent<LoveHouseComponent>(lh).CooldownTicksRemaining--;
                    server.State.GetComponent<LoveHouseComponent>(lh).CooldownTicksRemaining++;
                }
            }
            foreach (var lh in client.State.Filter<LoveHouseComponent>())
            {
                var clh = client.State.GetComponent<LoveHouseComponent>(lh);
                if (clh.CooldownTicksRemaining == LoveHouseComponent.BreedCooldownTicks)
                {
                    _output.WriteLine($"  CLIENT breed complete at clientTick={client.Loop.CurrentTick} testTick={tick}");
                    client.State.GetComponent<LoveHouseComponent>(lh).CooldownTicksRemaining--;
                    client.State.GetComponent<LoveHouseComponent>(lh).CooldownTicksRemaining++;
                }
            }

            // Check hash every 10 ticks for fine-grained detection
            if (tick > 0 && tick % 10 == 0)
            {
                long checkTick = server.Loop.CurrentTick - System.Math.Max(networkDelay + 10, 30);
                if (checkTick <= 0) continue;

                try
                {
                    var sHash = HashAtTick(server, checkTick);
                    var cHash = HashAtTick(client, checkTick);

                    if (sHash != cHash)
                    {
                        desyncCount++;
                        if (firstDesyncTick < 0) firstDesyncTick = checkTick;
                        _output.WriteLine($"  DESYNC at tick {tick}, confirmed tick {checkTick}");

                        if (desyncCount <= 3)
                        {
                            server.Loop.Simulation.History.TryGetSnapshotData(checkTick, out byte[]? sd);
                            client.Loop.Simulation.History.TryGetSnapshotData(checkTick, out byte[]? cd);
                            if (sd != null && cd != null)
                                StateDumper.LogStateDiff($"Breed_{label}_{checkTick}", checkTick, cd, sd);
                        }
                    }
                }
                catch { /* Tick not in history */ }
            }
        }

        _output.WriteLine($"[{label}] {totalTicks} ticks, {desyncCount} desyncs" +
            (firstDesyncTick >= 0 ? $" (first at tick {firstDesyncTick})" : ""));

        // Log breed progress on both sides at the divergence point
        if (firstDesyncTick >= 0)
        {
            foreach (var lh in server.State.Filter<LoveHouseComponent>())
            {
                var slh = server.State.GetComponent<LoveHouseComponent>(lh);
                _output.WriteLine($"  Server LoveHouse {lh.Id}: Progress={slh.BreedProgress}/{slh.BreedCost} Cooldown={slh.CooldownTicksRemaining}");
            }
            foreach (var lh in client.State.Filter<LoveHouseComponent>())
            {
                var clh = client.State.GetComponent<LoveHouseComponent>(lh);
                _output.WriteLine($"  Client LoveHouse {lh.Id}: Progress={clh.BreedProgress}/{clh.BreedCost} Cooldown={clh.CooldownTicksRemaining}");
            }
        }

        desyncCount.Should().Be(0,
            $"[{label}] breeding with {networkDelay}-tick delay should not desync (first at tick {firstDesyncTick})");
    }

    /// <summary>
    /// Sets up a love house with 2 cows assigned and the player in breed state.
    /// Must be called identically on both server and client.
    /// </summary>
    private void SetupBreedState(Game game, Entity playerEntity, Guid userId)
    {
        var state = game.State;
        var ctx = new Context(state, playerEntity, null!);

        // Create a love house near origin
        var loveHouse = LoveHouseDefinition.Create(ctx, new Vector2(5, 0));

        // Create 2 extra houses so cowCount < houseCount check passes
        HouseDefinition.Create(ctx, new Vector2(10, 0));
        HouseDefinition.Create(ctx, new Vector2(10, 5));
        HouseDefinition.Create(ctx, new Vector2(10, -5));

        // Create 2 cows and assign them to the love house
        var cow1 = CowDefinition.Create(ctx, new Vector2(4, 0));
        state.GetComponent<CowComponent>(cow1).PreferredFood = FoodType.Grass;

        var cow2 = CowDefinition.Create(ctx, new Vector2(6, 0));
        state.GetComponent<CowComponent>(cow2).PreferredFood = FoodType.Grass;

        ref var lh = ref state.GetComponent<LoveHouseComponent>(loveHouse);
        lh.CowId1 = cow1;
        lh.CowId2 = cow2;

        // Move player near love house so sensor picks it up
        ref var playerTransform = ref state.GetComponent<Transform2D>(playerEntity);
        playerTransform.Position = new Vector2(5, 1);

        // Set up player in breed state
        ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
        StateDefinitions.Begin(ref sc, StateKeys.Breed);

        ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
        ps.InteractionTarget = loveHouse;
        ps.ReturnPosition = playerTransform.Position;

        state.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Breed, Phase = sc.Phase, Age = 0 });

        // Set breed cost
        lh = ref state.GetComponent<LoveHouseComponent>(loveHouse);
        lh.BreedCost = 10;
        lh.BreedProgress = 0;
        lh.HeartPercent = 70;

        // Note: in real game, player + cows are hidden during breed.
        // Skipping here — doesn't affect determinism.
    }

    /// <summary>
    /// Pinpoints the exact system that causes breed desync.
    /// Enables per-system hashing around the divergence tick to find which system
    /// produces different state after rollback.
    /// </summary>
    [Fact]
    public void Breed_PinpointDivergence()
    {
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        for (int i = 0; i < 5; i++)
        {
            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();
        }

        SetupBreedState(server, serverPlayer, userId);
        SetupBreedState(client, clientPlayer, userId);

        int networkDelay = 18;
        int breedClicks = 30;
        int clickInterval = 3;
        var pendingActions = new Queue<(int deliveryTick, long execTick, InteractAction action)>();
        int totalTicks = breedClicks * clickInterval + 300;
        long firstDesyncTick = -1;

        // Enable per-system diagnostics — output goes to console, captured by xunit
        server.Loop.Simulation.SystemRunner.DiagnosticHashPerSystem = true;
        client.Loop.Simulation.SystemRunner.DiagnosticHashPerSystem = true;
        // Only enable around expected divergence area (tick 90-100 based on previous test)
        server.Loop.Simulation.SystemRunner.DiagnosticTickMin = 85;
        server.Loop.Simulation.SystemRunner.DiagnosticTickMax = 105;
        client.Loop.Simulation.SystemRunner.DiagnosticTickMin = 85;
        client.Loop.Simulation.SystemRunner.DiagnosticTickMax = 105;

        var serverHashes = new Dictionary<long, Guid>();
        var clientHashes = new Dictionary<long, Guid>();

        for (int tick = 0; tick < totalTicks; tick++)
        {
            if (tick % clickInterval == 0 && tick < breedClicks * clickInterval)
            {
                long execTick = client.Loop.CurrentTick + 5;
                var action = new InteractAction { UserId = userId };
                client.Loop.ScheduleOnTick(execTick, action, clientPlayer);
                pendingActions.Enqueue((tick + networkDelay, execTick, action));
            }

            while (pendingActions.Count > 0 && pendingActions.Peek().deliveryTick <= tick)
            {
                var d = pendingActions.Dequeue();
                server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
            }

            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();

            // Store hashes for every tick
            long st = server.Loop.CurrentTick;
            serverHashes[st] = StateHasher.Hash(server.State);
            clientHashes[st] = StateHasher.Hash(client.State);

            // Stop after finding divergence
            if (firstDesyncTick < 0 && serverHashes[st] != clientHashes[st])
            {
                firstDesyncTick = st;
                _output.WriteLine($"=== FIRST LIVE DIVERGENCE at tick {st} ===");
                _output.WriteLine($"  Server: {serverHashes[st]}");
                _output.WriteLine($"  Client: {clientHashes[st]}");

                // Dump state diff
                byte[] sd = StateSerializer.Serialize(server.State);
                byte[] cd = StateSerializer.Serialize(client.State);
                StateDumper.LogStateDiff($"Breed_Pinpoint_{st}", st, cd, sd);

                // Also check the previous tick from history
                if (server.Loop.Simulation.History.TryGetSnapshotData(st - 1, out byte[]? prevSd) &&
                    client.Loop.Simulation.History.TryGetSnapshotData(st - 1, out byte[]? prevCd))
                {
                    var prevSh = StateHasher.Hash(prevSd!);
                    var prevCh = StateHasher.Hash(prevCd!);
                    _output.WriteLine($"  Previous tick {st - 1}: server={prevSh} client={prevCh} {(prevSh == prevCh ? "MATCH" : "DIVERGE")}");
                    if (prevSh != prevCh)
                        StateDumper.LogStateDiff($"Breed_Pinpoint_{st - 1}", st - 1, prevCd!, prevSd!);
                }

                // Run a few more ticks for context then stop
                for (int extra = 0; extra < 5; extra++)
                {
                    while (pendingActions.Count > 0 && pendingActions.Peek().deliveryTick <= tick + extra + 1)
                    {
                        var d = pendingActions.Dequeue();
                        server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
                    }
                    server.Loop.RunSingleTick();
                    client.Loop.RunSingleTick();
                }
                break;
            }
        }

        firstDesyncTick.Should().Be(-1, $"should not diverge (first at tick {firstDesyncTick})");
    }

    /// <summary>
    /// Tests if rollback+replay produces identical state to forward simulation.
    /// One game runs forward. Another runs forward, then rolls back 10 ticks, replays.
    /// Hashes must match — if not, something in the rollback/replay path is lossy.
    /// </summary>
    [Fact]
    public void Rollback_Replay_ShouldMatchForward()
    {
        var forward = CreateGame();
        var rollback = CreateGame();

        var userId = Guid.NewGuid();
        var fwdPlayer = AddPlayer(forward, userId);
        var rbkPlayer = AddPlayer(rollback, userId);

        // Schedule some actions on both identically
        for (int i = 0; i < 30; i++)
        {
            long execTick = forward.Loop.CurrentTick + 5;
            var move = new SetMoveDirectionAction(new Vector2(1, 0).Normalized, (Float)15);
            forward.Loop.ScheduleOnTick(execTick, move, fwdPlayer);
            rollback.Loop.ScheduleOnTick(execTick, move, rbkPlayer);

            if (i % 3 == 0)
            {
                var interact = new InteractAction { UserId = userId };
                forward.Loop.ScheduleOnTick(execTick, interact, fwdPlayer);
                rollback.Loop.ScheduleOnTick(execTick, interact, rbkPlayer);
            }

            forward.Loop.RunSingleTick();
            rollback.Loop.RunSingleTick();
        }

        // Verify they're in sync
        var h1 = StateHasher.Hash(forward.State);
        var h2 = StateHasher.Hash(rollback.State);
        h1.Should().Be(h2, "should be in sync before rollback");

        // Now force a rollback on the "rollback" game by scheduling a late action
        // Simulate: action for 10 ticks ago arrives now
        long pastTick = rollback.Loop.CurrentTick - 10;
        var lateAction = new SetMoveDirectionAction(new Vector2(0, 1).Normalized, (Float)15);
        rollback.Loop.ScheduleOnTick(pastTick, lateAction, rbkPlayer);
        // Also schedule on forward at the same tick (it's already past, but forward never rolled back)
        forward.Loop.ScheduleOnTick(pastTick, lateAction, fwdPlayer);

        // Run both forward for more ticks
        for (int i = 0; i < 30; i++)
        {
            forward.Loop.RunSingleTick();
            rollback.Loop.RunSingleTick();
        }

        // Compare: rollback game should match forward game
        // (rollback replayed with the late action from pastTick,
        //  forward also has it but applied it "retroactively")
        var fHash = StateHasher.Hash(forward.State);
        var rHash = StateHasher.Hash(rollback.State);

        _output.WriteLine($"Forward:  {fHash}");
        _output.WriteLine($"Rollback: {rHash}");

        if (fHash != rHash)
        {
            byte[] fd = StateSerializer.Serialize(forward.State);
            byte[] rd = StateSerializer.Serialize(rollback.State);
            StateDumper.LogStateDiff("Rollback_vs_Forward", forward.Loop.CurrentTick, fd, rd);
        }

        rHash.Should().Be(fHash, "rollback+replay must produce identical state to forward simulation");
    }

    /// <summary>
    /// Tests if serialization roundtrip is lossless.
    /// If this fails, rollback (which serializes+deserializes state) corrupts state.
    /// </summary>
    [Fact]
    public void Serialization_Roundtrip_ShouldBeLossless()
    {
        var game = CreateGame();
        var userId = Guid.NewGuid();
        AddPlayer(game, userId);

        // Run some ticks to build up state
        for (int i = 0; i < 60; i++)
            game.Loop.RunSingleTick();

        // Hash before
        var hashBefore = StateHasher.Hash(game.State);

        // Serialize → deserialize (same as rollback)
        byte[] serialized = StateSerializer.Serialize(game.State);
        StateSerializer.Deserialize(game.State, serialized, serialized.Length, syncComponentIds: false);

        // Hash after
        var hashAfter = StateHasher.Hash(game.State);

        _output.WriteLine($"Before: {hashBefore}");
        _output.WriteLine($"After:  {hashAfter}");

        hashAfter.Should().Be(hashBefore, "serialization roundtrip must be lossless");
    }

    /// <summary>
    /// Same test but using NO network delay — pure determinism check.
    /// Both instances get actions at the same tick. If this fails, the simulation
    /// itself is non-deterministic (not a rollback issue).
    /// </summary>
    [Theory]
    [InlineData(1800, 0, "nolatency_run0")]
    [InlineData(1800, 1, "nolatency_run1")]
    [InlineData(2400, 2, "nolatency_long")]
    public void LongRun_NoLatency_ShouldNotDesync(int totalTicks, int seed, string label)
    {
        var server = CreateGame();
        var client = CreateGame();

        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);
        var clientPlayer = AddPlayer(client, userId);

        var movements = GenerateMovementPattern(seed, totalTicks);
        int moveIdx = 0;

        int desyncCount = 0;
        int checksPerformed = 0;
        long firstDesyncTick = -1;

        for (int tick = 0; tick < totalTicks; tick++)
        {
            // Both get actions simultaneously
            while (moveIdx < movements.Count && movements[moveIdx].tick <= tick)
            {
                var m = movements[moveIdx];
                long execTick = server.Loop.CurrentTick + 1;

                var action = new SetMoveDirectionAction(m.dir, m.speed);
                server.Loop.ScheduleOnTick(execTick, action, serverPlayer);
                client.Loop.ScheduleOnTick(execTick, action, clientPlayer);

                moveIdx++;
            }

            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();

            // Check every 60 ticks
            if (tick > 0 && tick % 60 == 0)
            {
                var sHash = StateHasher.Hash(server.State);
                var cHash = StateHasher.Hash(client.State);
                checksPerformed++;

                if (sHash != cHash)
                {
                    desyncCount++;
                    if (firstDesyncTick < 0) firstDesyncTick = server.Loop.CurrentTick;

                    _output.WriteLine($"  DESYNC at tick {server.Loop.CurrentTick}");

                    if (desyncCount <= 2)
                    {
                        byte[] sd = StateSerializer.Serialize(server.State);
                        byte[] cd = StateSerializer.Serialize(client.State);
                        StateDumper.LogStateDiff($"NoLatency_{label}_{tick}", server.Loop.CurrentTick, cd, sd);
                    }
                }
            }
        }

        _output.WriteLine($"[{label}] {totalTicks} ticks, {checksPerformed} checks, {desyncCount} desyncs" +
            (firstDesyncTick >= 0 ? $" (first at tick {firstDesyncTick})" : ""));

        desyncCount.Should().Be(0,
            $"[{label}] two instances with identical inputs must produce identical state");
    }

    /// <summary>
    /// Replay-based test: runs the same recording twice and compares hashes at
    /// every 60-tick checkpoint (not just the final hash). Catches transient
    /// divergence that self-corrects by end of recording.
    /// </summary>
    [Theory]
    [MemberData(nameof(GetRecordingFiles))]
    public void ReplayRecording_CheckpointHashes_ShouldMatch(string recordingFile)
    {
        var recordingsDir = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "Recordings");
        var path = System.IO.Path.Combine(recordingsDir, recordingFile);

        if (!System.IO.File.Exists(path))
        {
            _output.WriteLine($"SKIP: {recordingFile} not found");
            return;
        }

        var (ticks, actions, _, _, _, _) = InputRecording.Load(path);
        _output.WriteLine($"Recording: {recordingFile}, {ticks} ticks, {actions.Count} actions");

        // Run 1: collect checkpoint hashes
        var checkpoints1 = ReplayWithCheckpoints(actions, ticks);

        // Run 2: collect checkpoint hashes
        var checkpoints2 = ReplayWithCheckpoints(actions, ticks);

        int mismatches = 0;
        foreach (var tick in checkpoints1.Keys)
        {
            if (!checkpoints2.ContainsKey(tick)) continue;

            if (checkpoints1[tick] != checkpoints2[tick])
            {
                mismatches++;
                _output.WriteLine($"  Checkpoint mismatch at tick {tick}: {checkpoints1[tick]} vs {checkpoints2[tick]}");
            }
        }

        _output.WriteLine($"{checkpoints1.Count} checkpoints, {mismatches} mismatches");
        mismatches.Should().Be(0, $"replaying {recordingFile} should produce identical checkpoints");
    }

    private Dictionary<long, Guid> ReplayWithCheckpoints(List<RecordedAction> actions, long totalTicks)
    {
        var game = CreateGame();
        var checkpoints = new Dictionary<long, Guid>();
        int actionIndex = 0;

        for (long tick = 0; tick <= totalTicks; tick++)
        {
            while (actionIndex < actions.Count && actions[actionIndex].Tick <= game.Loop.CurrentTick + 1)
            {
                var a = actions[actionIndex];
                var stableId = new StableComponentId(a.StableComponentId);
                if (ComponentId.TryGetDense(stableId, out var denseId))
                    game.Scheduler.ScheduleFromBytes(denseId, a.Data, a.TargetEntityId, a.Tick);
                actionIndex++;
            }

            game.Loop.RunSingleTick();

            if (game.Loop.CurrentTick % 60 == 0 && game.Loop.CurrentTick > 0)
            {
                checkpoints[game.Loop.CurrentTick] = StateHasher.Hash(game.State);
            }
        }

        return checkpoints;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Cross-process determinism: catches .NET JIT/runtime differences
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs a latency simulation, saves checkpoint hashes to a file.
    /// On the SECOND run (new process), compares with the first run's hashes.
    ///
    /// This catches:
    ///   - HashCode.Combine randomization (per-process seed)
    ///   - Dictionary iteration order differences
    ///   - JIT-level floating point differences across process runs
    ///
    /// Usage: run `dotnet test --filter CrossProcess_LongRun` twice.
    /// First run writes hashes, second run compares.
    /// </summary>
    [Theory]
    [InlineData(18, 1800, 0, "crossproc_300ms")]
    [InlineData(0, 1800, 0, "crossproc_nolatency")]
    public void CrossProcess_LongRun_ShouldMatchPreviousRun(int networkDelay, int totalTicks, int seed, string label)
    {
        var hashDir = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "Recordings", ".hashes");
        System.IO.Directory.CreateDirectory(hashDir);
        var hashFile = System.IO.Path.Combine(hashDir, $"longrun_{label}.hashes");

        // Run simulation and collect checkpoint hashes
        var checkpoints = RunSimulationWithCheckpoints(networkDelay, totalTicks, seed);

        // Serialize checkpoints
        var lines = new List<string>();
        foreach (var kv in checkpoints)
            lines.Add($"{kv.Key}:{kv.Value}");
        string currentData = string.Join("\n", lines);

        _output.WriteLine($"[{label}] {checkpoints.Count} checkpoints collected");

        if (System.IO.File.Exists(hashFile))
        {
            // Second run — compare with previous
            var previousLines = System.IO.File.ReadAllLines(hashFile);
            var previousCheckpoints = new Dictionary<long, Guid>();
            foreach (var line in previousLines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(':');
                previousCheckpoints[long.Parse(parts[0])] = Guid.Parse(parts[1]);
            }

            // Write current (so next run compares against this one)
            System.IO.File.WriteAllText(hashFile, currentData);

            int mismatches = 0;
            long firstMismatchTick = -1;
            foreach (var kv in checkpoints)
            {
                if (previousCheckpoints.TryGetValue(kv.Key, out var prevHash) && prevHash != kv.Value)
                {
                    mismatches++;
                    if (firstMismatchTick < 0) firstMismatchTick = kv.Key;
                    _output.WriteLine($"  MISMATCH at tick {kv.Key}: prev={prevHash}, curr={kv.Value}");
                }
            }

            _output.WriteLine($"[{label}] {mismatches} cross-process mismatches");
            mismatches.Should().Be(0,
                $"[{label}] simulation should produce identical hashes across process runs. " +
                $"First mismatch at tick {firstMismatchTick}. " +
                "This likely means GetHashCode, Dictionary order, or JIT differs across processes.");
        }
        else
        {
            // First run — write hashes
            System.IO.File.WriteAllText(hashFile, currentData);
            _output.WriteLine($"[{label}] FIRST RUN — hashes saved to {hashFile}. " +
                "Run again to compare.");
            // Don't fail — just inform. The second run will do the real comparison.
        }
    }

    private Dictionary<long, Guid> RunSimulationWithCheckpoints(int networkDelay, int totalTicks, int seed)
    {
        var game = CreateGame();
        var userId = new Guid("12345678-1234-1234-1234-123456789abc"); // Fixed userId for reproducibility
        var player = AddPlayer(game, userId);

        var movements = GenerateMovementPattern(seed, totalTicks);
        int moveIdx = 0;

        // For latency simulation: a second game as "server"
        Game? server = null;
        Entity serverPlayer = Entity.Null;
        Queue<(int deliveryTick, long execTick, SetMoveDirectionAction action)>? pendingActions = null;

        if (networkDelay > 0)
        {
            server = CreateGame();
            serverPlayer = AddPlayer(server, userId);
            pendingActions = new Queue<(int, long, SetMoveDirectionAction)>();
        }

        var checkpoints = new Dictionary<long, Guid>();

        for (int tick = 0; tick < totalTicks; tick++)
        {
            while (moveIdx < movements.Count && movements[moveIdx].tick <= tick)
            {
                var m = movements[moveIdx];
                long execTick = game.Loop.CurrentTick + 1;
                var action = new SetMoveDirectionAction(m.dir, m.speed);

                if (networkDelay > 0)
                {
                    // Client predicts, server gets late
                    game.Loop.ScheduleOnTick(execTick, action, player);
                    pendingActions!.Enqueue((tick + networkDelay, execTick, action));
                }
                else
                {
                    // No latency: schedule directly
                    game.Loop.ScheduleOnTick(execTick, action, player);
                }

                moveIdx++;
            }

            if (networkDelay > 0)
            {
                while (pendingActions!.Count > 0 && pendingActions.Peek().deliveryTick <= tick)
                {
                    var d = pendingActions.Dequeue();
                    server!.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
                }
                server!.Loop.RunSingleTick();
            }

            game.Loop.RunSingleTick();

            if (game.Loop.CurrentTick % 60 == 0 && game.Loop.CurrentTick > 0)
            {
                checkpoints[game.Loop.CurrentTick] = StateHasher.Hash(game.State);
            }
        }

        return checkpoints;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Per-tick divergence finder: pinpoints exact tick + component
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Replays a Godot recording and compares state hash at EVERY tick against
    /// the Godot checkpoints. When the first 60-tick checkpoint diverges, reruns
    /// with per-tick hashing in that 60-tick window to find the exact tick.
    /// Then dumps the state diff at that tick.
    /// </summary>
    [Fact]
    public void FindExactDivergenceTick()
    {
        var recordingsDir = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "Recordings");
        var path = System.IO.Path.Combine(recordingsDir, "recording_latest_godot.bin");
        if (!System.IO.File.Exists(path))
        {
            _output.WriteLine("SKIP: recording_latest_godot.bin not found");
            return;
        }

        var (ticks, actions, _, godotCheckpoints, initialState, startTick) = InputRecording.Load(path);
        _output.WriteLine($"Recording: {ticks} ticks, {actions.Count} actions, {godotCheckpoints.Count} checkpoints");

        // Build Godot checkpoint lookup
        var godotByTick = new Dictionary<long, RecordedCheckpoint>();
        foreach (var cp in godotCheckpoints)
            godotByTick[cp.Tick] = cp; // Last wins (post-rollback)

        // Find the first divergent 60-tick checkpoint
        long firstDivergeTick = -1;
        long lastMatchTick = startTick;

        var game = CreateGame();
        if (initialState != null && initialState.Length > 0)
        {
            StateSerializer.AdoptMappingsFrom(initialState);
            StateSerializer.Deserialize(game.State, initialState, syncComponentIds: false, fullInvalidate: true);
            game.Loop.ForceSetTick(startTick);
            game.Loop.Simulation.History.Store(startTick, game.State);
        }

        int actionIndex = 0;
        while (actionIndex < actions.Count && actions[actionIndex].Tick < startTick)
            actionIndex++;

        for (long tick = startTick; tick <= ticks; tick++)
        {
            while (actionIndex < actions.Count && actions[actionIndex].Tick <= game.Loop.CurrentTick + 1)
            {
                var a = actions[actionIndex];
                var stableId = new StableComponentId(a.StableComponentId);
                if (ComponentId.TryGetDense(stableId, out var denseId))
                    game.Scheduler.ScheduleFromBytes(denseId, a.Data, a.TargetEntityId, a.Tick);
                actionIndex++;
            }

            game.Loop.RunSingleTick();

            long ct = game.Loop.CurrentTick;
            if (godotByTick.TryGetValue(ct, out var gcp))
            {
                var replayHash = StateHasher.Hash(game.State);
                if (replayHash == gcp.Hash)
                {
                    lastMatchTick = ct;
                    _output.WriteLine($"  Tick {ct}: MATCH");
                }
                else
                {
                    firstDivergeTick = ct;
                    _output.WriteLine($"  Tick {ct}: DIVERGE (last match: {lastMatchTick})");
                    break;
                }
            }
        }

        if (firstDivergeTick < 0)
        {
            _output.WriteLine("All checkpoints matched! No divergence.");
            return;
        }

        // Now we know divergence is between lastMatchTick and firstDivergeTick.
        // Re-run that window comparing EVERY tick using Godot's stored state data.
        _output.WriteLine($"\n=== Narrowing: divergence between tick {lastMatchTick} and {firstDivergeTick} ===");

        // We need Godot state data at the divergent checkpoint to compare
        if (godotByTick.TryGetValue(firstDivergeTick, out var divergeCp) && divergeCp.StateData != null)
        {
            byte[] replayState = StateSerializer.Serialize(game.State);
            _output.WriteLine($"Godot state: {divergeCp.StateData.Length} bytes");
            _output.WriteLine($"Replay state: {replayState.Length} bytes");

            // Raw byte diff (first 30 diffs)
            int minLen = Math.Min(replayState.Length, divergeCp.StateData.Length);
            int diffs = 0;
            for (int b = 0; b < minLen && diffs < 30; b++)
            {
                if (replayState[b] != divergeCp.StateData[b])
                {
                    _output.WriteLine($"  RAW offset {b}: godot=0x{divergeCp.StateData[b]:X2} replay=0x{replayState[b]:X2}");
                    diffs++;
                }
            }
            if (replayState.Length != divergeCp.StateData.Length)
                _output.WriteLine($"  SIZE: godot={divergeCp.StateData.Length} replay={replayState.Length}");

            // Component-level diff
            StateDumper.LogStateDiff("ExactDivergence", firstDivergeTick, divergeCp.StateData, replayState);
        }

        // Also: re-run from lastMatchTick with PER-TICK hashing to find exact tick.
        // Use Godot state from lastMatchTick as starting point (guaranteed identical).
        byte[]? matchStateData = null;
        if (godotByTick.TryGetValue(lastMatchTick, out var matchCp) && matchCp.StateData != null)
            matchStateData = matchCp.StateData;
        else if (lastMatchTick == startTick && initialState != null)
            matchStateData = initialState; // Use initial state if no checkpoint at tick 0

        if (matchStateData != null)
        {
            _output.WriteLine($"\n=== Per-tick scan from tick {lastMatchTick} ===");
            var game2 = CreateGame();
            StateSerializer.AdoptMappingsFrom(matchStateData);
            StateSerializer.Deserialize(game2.State, matchStateData, syncComponentIds: false, fullInvalidate: true);
            game2.Loop.ForceSetTick(lastMatchTick);
            game2.Loop.Simulation.History.Store(lastMatchTick, game2.State);

            // Verify starting point
            var startHash = StateHasher.Hash(matchStateData);
            var replayStartHash = StateHasher.Hash(game2.State);
            _output.WriteLine($"Start state: godot={startHash} replay={replayStartHash} match={startHash == replayStartHash}");

            int ai2 = 0;
            while (ai2 < actions.Count && actions[ai2].Tick <= lastMatchTick)
                ai2++;

            Guid prevHash = replayStartHash;
            for (long t = lastMatchTick; t < firstDivergeTick; t++)
            {
                while (ai2 < actions.Count && actions[ai2].Tick <= game2.Loop.CurrentTick + 1)
                {
                    var a = actions[ai2];
                    var stableId = new StableComponentId(a.StableComponentId);
                    if (ComponentId.TryGetDense(stableId, out var denseId))
                    {
                        game2.Scheduler.ScheduleFromBytes(denseId, a.Data, a.TargetEntityId, a.Tick);
                        string typeName = ComponentId.TryGetType(denseId, out var type) ? (type?.Name ?? "?") : "?";
                        _output.WriteLine($"  Action at tick {a.Tick}: {typeName} target={a.TargetEntityId}");
                    }
                    ai2++;
                }

                game2.Loop.RunSingleTick();

                // Log entity count every tick to detect when extra entities appear
                int entityCount = game2.State.NextEntityId;
                _output.WriteLine($"  Tick {game2.Loop.CurrentTick}: entities={entityCount}");

                if (game2.Loop.CurrentTick == firstDivergeTick)
                {
                    var hash = StateHasher.Hash(game2.State);
                    if (godotByTick.TryGetValue(game2.Loop.CurrentTick, out var gcp2))
                    {
                        _output.WriteLine($"  Tick {game2.Loop.CurrentTick}: hash={hash} godot={gcp2.Hash} {(hash == gcp2.Hash ? "MATCH" : "DIVERGE")}");
                    }
                }
            }
        }
        else
        {
            _output.WriteLine("No Godot state data at last match tick — can't do per-tick scan. Re-record with CaptureStateAtCheckpoints=true.");
        }

        firstDivergeTick.Should().Be(-1, "should not diverge");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Simulate REAL client sync flow (Phase 2)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Simulates the EXACT production flow:
    ///   1. Server runs solo for N ticks (scene setup + gameplay)
    ///   2. Client receives full state sync at tick T
    ///   3. Client also receives TickSnapshot actions from server (simulating what
    ///      arrives during WaitForSyncAsync and catch-up)
    ///   4. Client catches up from tick T to server's current tick
    ///   5. Both run in lockstep with identical actions
    ///   6. Compare hashes at every checkpoint
    ///
    /// This reproduces the EXACT sequence: ApplyFullState → ForceSetTick →
    /// PruneHistory → re-add predictions → InitializeLoop → History.Store →
    /// catch-up ticks. If this desyncs, the bug is in the framework.
    /// </summary>
    [Theory]
    [InlineData(0, 1800, "sync_at_0")]
    [InlineData(100, 1800, "sync_at_100")]
    [InlineData(200, 1800, "sync_at_200")]
    public void SimulateRealSyncFlow_ShouldNotDesync(int syncTick, int totalTicks, string label)
    {
        // === SERVER: Run from tick 0, add player, play ===
        var server = CreateGame();
        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);

        // Server runs to syncTick (produces the state that the client receives)
        for (int i = 0; i < syncTick; i++)
            server.Loop.RunSingleTick();

        // Server serializes state for the client (this is the full state sync payload)
        long serverTickAtSync = server.Loop.CurrentTick;
        byte[] syncState = StateSerializer.Serialize(server.State);
        _output.WriteLine($"[{label}] Server at tick {serverTickAtSync}, sync state: {syncState.Length} bytes");

        // Server continues running (client hasn't caught up yet)
        // Collect actions the server processes — these become TickSnapshot actions
        var serverActions = new List<(long tick, DenseComponentId id, byte[] data, int entityId)>();
        server.Scheduler.OnActionScheduled += (id, data, entityId, tick, origTick) =>
        {
            if (tick >= serverTickAtSync) // Only capture actions after sync point
                serverActions.Add((tick, id, data.ToArray(), entityId));
        };

        // Generate movement pattern and schedule on server
        var movements = GenerateMovementPattern(0, totalTicks);
        int moveIdx = 0;

        for (int tick = (int)serverTickAtSync; tick < totalTicks; tick++)
        {
            while (moveIdx < movements.Count && movements[moveIdx].tick <= tick)
            {
                var m = movements[moveIdx];
                long execTick = server.Loop.CurrentTick + 1;
                var action = new SetMoveDirectionAction(m.dir, m.speed);
                server.Loop.ScheduleOnTick(execTick, action, serverPlayer);
                moveIdx++;
            }
            server.Loop.RunSingleTick();
        }

        _output.WriteLine($"[{label}] Server finished at tick {server.Loop.CurrentTick}, captured {serverActions.Count} actions");

        // === CLIENT: Simulate the EXACT sync flow ===
        var client = CreateGame();

        // Step 1: Deserialize server state (ApplyFullState)
        StateSerializer.AdoptMappingsFrom(syncState);
        StateSerializer.Deserialize(client.State, syncState, syncComponentIds: false, fullInvalidate: true);

        // Step 2: ForceSetTick (GameClient.ApplyFullState line 370)
        client.Loop.ForceSetTick(serverTickAtSync);

        // Step 3: Store in history (GameClient.ApplyFullState line 373)
        client.Loop.Simulation.History.Store(serverTickAtSync, client.State);

        // Step 4: PruneHistory (GameClient.ApplyFullState line 380)
        client.Scheduler.PruneHistory(serverTickAtSync);

        // Step 5: Schedule TickSnapshot actions
        // In production, these arrive during WaitForSyncAsync and the first few ticks
        foreach (var a in serverActions)
        {
            client.Scheduler.ScheduleFromBytes(a.id, a.data, a.entityId, a.tick);
        }

        // Step 6: InitializeLoop double-store (GameLoop.cs line 139)
        // This happens when Loop.Start() is called — simulate it
        client.Loop.Simulation.History.Store(client.Loop.CurrentTick, client.State);

        // Step 7: Verify starting state matches
        var serverHashAtSync = StateHasher.Hash(syncState);
        var clientHashAtSync = StateHasher.Hash(StateSerializer.Serialize(client.State));
        _output.WriteLine($"[{label}] Sync state: server={serverHashAtSync} client={clientHashAtSync} match={serverHashAtSync == clientHashAtSync}");
        serverHashAtSync.Should().Be(clientHashAtSync, "client state after sync should match server");

        // Step 8: Client catches up and then runs in lockstep
        int desyncCount = 0;
        long firstDesyncTick = -1;

        for (long t = serverTickAtSync; t < totalTicks; t++)
        {
            client.Loop.RunSingleTick();

            // Check every 60 ticks
            if (client.Loop.CurrentTick % 60 == 0 && client.Loop.CurrentTick > serverTickAtSync)
            {
                long checkTick = client.Loop.CurrentTick;

                // Get server hash from history
                Guid sHash, cHash;
                try
                {
                    if (!server.Loop.Simulation.History.TryGetSnapshotData(checkTick, out var sd))
                        continue;
                    sHash = StateHasher.Hash(sd!);
                    cHash = StateHasher.Hash(client.State);
                }
                catch { continue; }

                if (sHash != cHash)
                {
                    desyncCount++;
                    if (firstDesyncTick < 0)
                    {
                        firstDesyncTick = checkTick;
                        _output.WriteLine($"[{label}] DESYNC at tick {checkTick}");

                        // Diff
                        server.Loop.Simulation.History.TryGetSnapshotData(checkTick, out var sd2);
                        byte[] cd = StateSerializer.Serialize(client.State);
                        StateDumper.LogStateDiff($"SyncFlow_{label}", checkTick, cd, sd2!);
                    }
                }
                else
                {
                    _output.WriteLine($"[{label}] Tick {checkTick}: MATCH");
                }
            }
        }

        _output.WriteLine($"[{label}] {desyncCount} desyncs" +
            (firstDesyncTick >= 0 ? $" (first at tick {firstDesyncTick})" : ""));

        desyncCount.Should().Be(0,
            $"[{label}] real sync flow should not desync (first at tick {firstDesyncTick})");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Godot-recorded hash verification: catches client↔server divergence
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Replays a Godot-recorded session and asserts the final hash matches
    /// what the Godot client produced.
    ///
    /// If this fails, the Godot .NET runtime produces different Fixed64 math
    /// results than the standalone .NET 8 test runner. That IS the desync.
    ///
    /// To use:
    ///   1. Enable InputRecorder in Godot client
    ///   2. Play a session (20+ seconds with movement)
    ///   3. Copy the .bin to Server/Template.Shared.Tests/Recordings/
    ///   4. Run this test
    /// </summary>
    [Theory]
    [MemberData(nameof(GetRecordingFiles))]
    public void ReplayRecording_ShouldMatchGodotHash(string recordingFile)
    {
        var recordingsDir = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "Recordings");
        var path = System.IO.Path.Combine(recordingsDir, recordingFile);

        if (!System.IO.File.Exists(path) || recordingFile.StartsWith("__"))
        {
            _output.WriteLine($"SKIP: {recordingFile}");
            return;
        }

        var (ticks, actions, godotHash, godotCheckpoints, initialState, startTick) = InputRecording.Load(path);
        _output.WriteLine($"Recording: {recordingFile}, {ticks} ticks, {actions.Count} actions, " +
            $"{godotCheckpoints.Count} checkpoints, startTick={startTick}, " +
            $"initialState={(initialState != null ? $"{initialState.Length} bytes" : "none")}");
        _output.WriteLine($"Godot final hash: {godotHash}");

        // Build a lookup of Godot checkpoint hashes by tick for quick comparison
        var godotHashByTick = new Dictionary<long, Guid>();
        foreach (var cp in godotCheckpoints)
            godotHashByTick[cp.Tick] = cp.Hash;

        // Create game and load initial state if available (v3 recording)
        var game = CreateGame();
        if (initialState != null && initialState.Length > 0)
        {
            _output.WriteLine($"Loading initial state from recording (startTick={startTick}, {initialState.Length} bytes)");
            StateSerializer.AdoptMappingsFrom(initialState);
            StateSerializer.Deserialize(game.State, initialState, syncComponentIds: false, fullInvalidate: true);
            game.Loop.ForceSetTick(startTick);
            game.Loop.Simulation.History.Store(startTick, game.State);

            // Verify initial state hash matches Godot's
            var initialHash = StateHasher.Hash(game.State);
            var godotInitialHash = StateHasher.Hash(initialState);
            _output.WriteLine($"Initial state hash: replay={initialHash}, godot={godotInitialHash}, match={initialHash == godotInitialHash}");
        }

        // Replay actions, starting from startTick
        int actionIndex = 0;
        // Skip actions before startTick (already baked into initial state)
        while (actionIndex < actions.Count && actions[actionIndex].Tick < startTick)
            actionIndex++;

        var replayCheckpoints = new Dictionary<long, Guid>();
        bool firstDivergenceDumped = false;

        for (long tick = startTick; tick <= ticks; tick++)
        {
            while (actionIndex < actions.Count && actions[actionIndex].Tick <= game.Loop.CurrentTick + 1)
            {
                var a = actions[actionIndex];
                var stableId = new StableComponentId(a.StableComponentId);
                if (ComponentId.TryGetDense(stableId, out var denseId))
                    game.Scheduler.ScheduleFromBytes(denseId, a.Data, a.TargetEntityId, a.Tick);
                actionIndex++;
            }

            game.Loop.RunSingleTick();

            // Capture checkpoint at same interval as recorder
            if (game.Loop.CurrentTick > 0 && game.Loop.CurrentTick % 60 == 0)
            {
                var hash = StateHasher.Hash(game.State);
                replayCheckpoints[game.Loop.CurrentTick] = hash;

                // Dump state at first divergence for debugging
                if (!firstDivergenceDumped && godotHashByTick.TryGetValue(game.Loop.CurrentTick, out var godotH) && godotH != hash)
                {
                    firstDivergenceDumped = true;
                    byte[] replayState = StateSerializer.Serialize(game.State);
                    _output.WriteLine($"\n=== FIRST DIVERGENCE at tick {game.Loop.CurrentTick} ===");
                    _output.WriteLine($"Godot hash: {godotH}");
                    _output.WriteLine($"Replay hash: {hash}");
                    _output.WriteLine($"Replay state: {replayState.Length} bytes");

                    // If Godot checkpoint has state data, do byte-level diff
                    var godotCp = godotCheckpoints.Find(c => c.Tick == game.Loop.CurrentTick);
                    if (godotCp.StateData != null && godotCp.StateData.Length > 0)
                    {
                        _output.WriteLine($"Godot state: {godotCp.StateData.Length} bytes");

                        // Raw byte diff first — catches metadata/mapping differences
                        int minLen = Math.Min(replayState.Length, godotCp.StateData.Length);
                        int rawDiffs = 0;
                        int firstDiffOffset = -1;
                        for (int b = 0; b < minLen && rawDiffs < 20; b++)
                        {
                            if (replayState[b] != godotCp.StateData[b])
                            {
                                if (firstDiffOffset < 0) firstDiffOffset = b;
                                _output.WriteLine($"  RAW DIFF offset {b}: godot=0x{godotCp.StateData[b]:X2} replay=0x{replayState[b]:X2}");
                                rawDiffs++;
                            }
                        }
                        if (replayState.Length != godotCp.StateData.Length)
                            _output.WriteLine($"  SIZE DIFF: godot={godotCp.StateData.Length} replay={replayState.Length}");
                        if (rawDiffs == 0 && replayState.Length == godotCp.StateData.Length)
                            _output.WriteLine("  RAW BYTES IDENTICAL — hash algorithm difference?");
                        else
                            _output.WriteLine($"  {rawDiffs}+ raw byte diffs (first at offset {firstDiffOffset})");

                        // Component-level diff
                        StateDumper.LogStateDiff("GodotVsReplay", game.Loop.CurrentTick,
                            godotCp.StateData, replayState);
                    }
                    else
                    {
                        _output.WriteLine("No Godot state data at this checkpoint.");
                    }
                }
            }
        }

        var replayHash = StateHasher.Hash(game.State);
        _output.WriteLine($"Replay final hash: {replayHash}");
        _output.WriteLine($"Final match: {replayHash == godotHash}");

        // Compare ALL checkpoints. Any divergence means the Godot .NET runtime
        // and dotnet test produce different simulation results from identical
        // inputs — this IS the production desync root cause.
        if (godotCheckpoints.Count > 0)
        {
            int matched = 0;
            int diverged = 0;
            long firstDivergenceTick = -1;

            _output.WriteLine("Checkpoint comparison:");
            foreach (var cp in godotCheckpoints)
            {
                if (!replayCheckpoints.TryGetValue(cp.Tick, out var rh)) continue;
                bool match = cp.Hash == rh;

                if (match)
                {
                    matched++;
                    _output.WriteLine($"  Tick {cp.Tick}: MATCH");
                }
                else
                {
                    diverged++;
                    if (firstDivergenceTick < 0)
                        firstDivergenceTick = cp.Tick;
                    _output.WriteLine($"  Tick {cp.Tick}: DIVERGE godot={cp.Hash} replay={rh}");
                }
            }

            _output.WriteLine($"Summary: {matched} matched, {diverged} diverged" +
                (firstDivergenceTick >= 0 ? $" (first at tick {firstDivergenceTick})" : ""));

            diverged.Should().Be(0,
                $"Godot and dotnet test diverge at tick {firstDivergenceTick}. " +
                $"{matched} checkpoints matched before that. " +
                "Same code, same inputs, different results = cross-runtime non-determinism.");
        }
        else
        {
            _output.WriteLine("No checkpoints in recording (v1 format). Re-record with latest InputRecorder for per-tick comparison.");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Full state sync + catch-up flow (mirrors GameClient.ApplyFullState)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Simulates the exact production flow:
    ///   1. Server runs from tick 0
    ///   2. Client joins mid-game via full state sync (like a real connection)
    ///   3. Client catches up from sync tick to server's current tick
    ///   4. Both run with 300ms latency for 1200+ ticks
    ///   5. Verify hash every 60 ticks
    ///
    /// This catches issues with:
    ///   - State deserialization losing derived state (DtCrowd, nav mesh)
    ///   - Catch-up resimulation diverging from linear execution
    ///   - InteractHighlightSystem divergence during catch-up
    /// </summary>
    [Theory]
    [InlineData(18, 1800, "join_300ms")]
    [InlineData(30, 1800, "join_500ms")]
    public void MidGameJoin_WithLatency_ShouldNotDesync(int networkDelay, int totalTicks, string label)
    {
        var server = CreateGame();
        var userId = Guid.NewGuid();
        var serverPlayer = AddPlayer(server, userId);

        // Server runs alone for 100 ticks (scene setup, cows moving, grass growing)
        for (int i = 0; i < 100; i++)
            server.Loop.RunSingleTick();

        _output.WriteLine($"[{label}] Server at tick {server.Loop.CurrentTick}, client joining...");

        // --- Client joins via full state sync ---
        var client = CreateGame();

        // Server serializes current state for client
        long syncTick = server.Loop.CurrentTick;
        byte[] syncState = StateSerializer.Serialize(server.State);

        // Client deserializes (mirrors GameClient.ApplyFullState)
        StateSerializer.AdoptMappingsFrom(syncState);
        StateSerializer.Deserialize(client.State, syncState, syncComponentIds: false, fullInvalidate: true);
        client.Loop.ForceSetTick(syncTick);
        client.Loop.Simulation.History.Store(syncTick, client.State);

        // Find client's player entity (same entity ID as server)
        Entity clientPlayer = Entity.Null;
        foreach (var e in client.State.Filter<PlayerEntity>())
        {
            if (client.State.GetComponent<PlayerEntity>(e).UserId == userId)
            { clientPlayer = e; break; }
        }
        clientPlayer.Should().NotBe(Entity.Null, "client should find player entity after sync");

        // Verify sync state matches
        var serverHashAtSync = StateHasher.Hash(syncState);
        var clientHashAtSync = StateHasher.Hash(StateSerializer.Serialize(client.State));
        serverHashAtSync.Should().Be(clientHashAtSync, "client state should match server after sync");

        // --- Server continues running during client catch-up ---
        // Simulate network delay: server runs ahead while client catches up
        for (int i = 0; i < networkDelay; i++)
            server.Loop.RunSingleTick();

        // Client catches up to server's tick
        long targetTick = server.Loop.CurrentTick;
        _output.WriteLine($"[{label}] Client catching up: {client.Loop.CurrentTick} → {targetTick}");
        while (client.Loop.CurrentTick < targetTick)
            client.Loop.RunSingleTick();

        // Verify still in sync after catch-up
        var sh = StateHasher.Hash(server.State);
        var ch = StateHasher.Hash(client.State);
        sh.Should().Be(ch, "should be in sync after catch-up");

        _output.WriteLine($"[{label}] In sync after catch-up at tick {client.Loop.CurrentTick}");

        // --- Now run with latency (same as LongRun_WithLatency) ---
        var movements = GenerateMovementPattern(0, totalTicks);
        int moveIdx = 0;
        var pendingActions = new Queue<(int deliveryTick, long execTick, SetMoveDirectionAction action)>();

        int desyncCount = 0;
        long firstDesyncTick = -1;
        int startTick = (int)(client.Loop.CurrentTick - syncTick);

        for (int tick = startTick; tick < totalTicks; tick++)
        {
            while (moveIdx < movements.Count && movements[moveIdx].tick <= tick)
            {
                var m = movements[moveIdx];
                long execTick = client.Loop.CurrentTick + 5;
                var action = new SetMoveDirectionAction(m.dir, m.speed);
                client.Loop.ScheduleOnTick(execTick, action, clientPlayer);
                pendingActions.Enqueue((tick + networkDelay, execTick, action));
                moveIdx++;
            }

            while (pendingActions.Count > 0 && pendingActions.Peek().deliveryTick <= tick)
            {
                var d = pendingActions.Dequeue();
                server.Loop.ScheduleOnTick(d.execTick, d.action, serverPlayer);
            }

            server.Loop.RunSingleTick();
            client.Loop.RunSingleTick();

            if (tick % 60 == 0 && tick >= startTick + 120)
            {
                long confirmTick = server.Loop.CurrentTick - 60;
                if (confirmTick <= syncTick) continue;

                try
                {
                    var sHash = HashAtTick(server, confirmTick);
                    var cHash = HashAtTick(client, confirmTick);

                    if (sHash != cHash)
                    {
                        desyncCount++;
                        if (firstDesyncTick < 0) firstDesyncTick = confirmTick;
                        _output.WriteLine($"  DESYNC at confirmed tick {confirmTick}");

                        if (desyncCount <= 2)
                        {
                            server.Loop.Simulation.History.TryGetSnapshotData(confirmTick, out byte[]? sd);
                            client.Loop.Simulation.History.TryGetSnapshotData(confirmTick, out byte[]? cd);
                            if (sd != null && cd != null)
                                StateDumper.LogStateDiff($"MidJoin_{label}_{confirmTick}", confirmTick, cd, sd);
                        }
                    }
                }
                catch { }
            }
        }

        _output.WriteLine($"[{label}] {desyncCount} desyncs" +
            (firstDesyncTick >= 0 ? $" (first at tick {firstDesyncTick})" : ""));

        desyncCount.Should().Be(0,
            $"[{label}] mid-game join + latency should not desync (first at tick {firstDesyncTick})");
    }

    public static TheoryData<string> GetRecordingFiles()
    {
        var data = new TheoryData<string>();
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "Recordings");

        bool found = false;
        if (System.IO.Directory.Exists(dir))
        {
            foreach (var file in System.IO.Directory.GetFiles(dir, "*.bin"))
            {
                data.Add(System.IO.Path.GetFileName(file));
                found = true;
            }
        }

        if (!found)
            data.Add("__NO_RECORDINGS__");

        return data;
    }
}
