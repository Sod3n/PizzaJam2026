using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Factories;
using Template.Shared.Systems;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

public class BotSimulationTests
{
    private static readonly object _createLock = new();
    private readonly ITestOutputHelper _output;

    public BotSimulationTests(ITestOutputHelper output) => _output = output;

    private Game CreateGame()
    {
        // Lock around static state (ServiceLocator + _appInitialized) — game creation is fast,
        // the expensive simulation loop runs fully in parallel after this.
        lock (_createLock)
        {
            ServiceLocator.Reset();
            var field = typeof(TemplateGameFactory).GetField("_appInitialized", BindingFlags.Static | BindingFlags.NonPublic);
            field?.SetValue(null, false);
            return TemplateGameFactory.CreateGame(tickRate: 60);
        }
    }

    private Entity AddBotPlayer(Game game, Guid userId)
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

    private bool HasFinalStructure(Game game)
    {
        foreach (var _ in game.State.Filter<FinalStructureComponent>())
            return true;
        return false;
    }

    /// <summary>
    /// Inject a target entity into the player's interaction zone Area2D overlap list.
    /// This bypasses the physics system which may not detect teleport-based positioning.
    /// </summary>
    private static void InjectOverlap(Game game, Entity player, Entity target)
    {
        if (target == Entity.Null) return;
        if (!game.State.HasComponent<PlayerStateComponent>(player)) return;

        var ps = game.State.GetComponent<PlayerStateComponent>(player);
        if (ps.InteractionZone == Entity.Null) return;
        if (!game.State.HasComponent<Area2D>(ps.InteractionZone)) return;

        ref var area = ref game.State.GetComponent<Area2D>(ps.InteractionZone);

        // Clear old entries and add the target
        area.OverlappingEntities = new List8<int>();
        area.OverlappingEntities.Add(target.Id);
        area.HasOverlappingBodies = true;
    }

    // ─── Core simulation runner (shared by single and multi-run tests) ───

    private record struct SimResult(
        bool Completed, float Minutes, int Houses, int Cows, int Helpers,
        int Coins, int Food, int LandRemaining, int LandCost,
        int MilkClicks, int CumMilk, int CumCoins);

    private SimResult RunSingleSim(int botCount, int maxMinutes, bool selectiveBreeding, bool helpersEnabled)
    {
        Game game;
        lock (_createLock)
        {
            ServiceLocator.Reset();
            var field = typeof(TemplateGameFactory).GetField("_appInitialized", BindingFlags.Static | BindingFlags.NonPublic);
            field?.SetValue(null, false);
            game = TemplateGameFactory.CreateGame(tickRate: 60);
        }

        var bots = new List<BotBrain>();
        var coordinator = new BotCoordinator();
        for (int i = 0; i < botCount; i++)
        {
            Guid userId;
            Entity player;
            lock (_createLock)
            {
                userId = Guid.NewGuid();
                Entity worldEntity = Entity.Null;
                foreach (var e in game.State.Filter<World>()) { worldEntity = e; break; }
                game.State.AddComponent(worldEntity, new AddPlayerAction(userId));
                game.Dispatcher.Update(game.State);
                game.Loop.Simulation.SystemRunner.Update(game.State);
                player = Entity.Null;
                foreach (var e in game.State.Filter<PlayerEntity>())
                {
                    if (game.State.GetComponent<PlayerEntity>(e).UserId == userId)
                    { player = e; break; }
                }
            }
            bots.Add(new BotBrain(game, player, userId, i, coordinator, selectiveBreeding));
        }

        CowSystem.HelpersEnabled = helpersEnabled;
        var runner = new LightSimRunner(game);
        for (int i = 0; i < 10; i++) game.Loop.RunSingleTick();

        var metrics = new SimulationMetrics { BotCount = botCount };
        int maxTicks = 60 * 60 * maxMinutes;
        bool completed = false;
        int endTick = maxTicks;

        for (int tick = 0; tick < maxTicks; tick++)
        {
            coordinator.ResetClaims();
            foreach (var bot in bots) bot.PreTick(tick);

            bool anyAction = false;
            foreach (var bot in bots)
            {
                if (bot.WantsToInteract)
                {
                    InjectOverlap(game, bot.Player, bot.CurrentTarget);
                    game.State.AddComponent(bot.Player, new InteractAction { UserId = bot.UserId });
                    anyAction = true;
                }
            }
            if (anyAction)
            {
                game.Dispatcher.Update(game.State);
                runner.RunSystems();
            }
            runner.Tick();

            if (tick % 60 == 0) metrics.RecordSnapshot(game, tick);

            if (HasFinalStructure(game))
            {
                completed = true;
                endTick = tick;
                metrics.RecordSnapshot(game, tick);
                break;
            }
            metrics.TotalTicks = tick;
        }

        var snap = metrics.Snapshots.LastOrDefault();
        return new SimResult(
            Completed: completed,
            Minutes: endTick / 3600f,
            Houses: snap?.Houses ?? 0,
            Cows: snap?.Cows ?? 0,
            Helpers: snap?.Helpers ?? 0,
            Coins: snap?.Coins ?? 0,
            Food: snap?.TotalFood ?? 0,
            LandRemaining: snap?.LandPlots ?? 0,
            LandCost: snap?.TotalLandRemaining ?? 0,
            MilkClicks: bots.Sum(b => b.TotalMilkClicks),
            CumMilk: snap?.CumMilk ?? 0,
            CumCoins: snap?.CumCoins ?? 0
        );
    }

    // ─── Single-run test (existing) ───

    [Theory]
    [InlineData(1, 30, true, true)]   // selective + helpers
    [InlineData(1, 30, true, false)]  // selective + NO helpers
    [InlineData(1, 30, false, true)]  // random + helpers
    [InlineData(1, 30, false, false)] // random + NO helpers
    public void RunSimulation(int botCount, int maxMinutes, bool selectiveBreeding, bool helpersEnabled)
    {
        var game = CreateGame();
        var metrics = new SimulationMetrics { BotCount = botCount };
        var bots = new List<BotBrain>();
        var coordinator = new BotCoordinator();

        // Add bot players
        for (int i = 0; i < botCount; i++)
        {
            var userId = Guid.NewGuid();
            var player = AddBotPlayer(game, userId);
            player.Should().NotBe(Entity.Null, $"Bot {i} should be created");
            bots.Add(new BotBrain(game, player, userId, i, coordinator, selectiveBreeding));
        }

        _output.WriteLine($"Starting simulation: {botCount} bot(s), {maxMinutes} min, breeding={( selectiveBreeding ? "selective" : "random")}, helpers={( helpersEnabled ? "ON" : "OFF")}...");

        // Toggle helper spawning — when off, breeding always produces cows instead
        CowSystem.HelpersEnabled = helpersEnabled;

        var runner = new LightSimRunner(game);

        // Use a few real ticks at start so physics bakes the navmesh and overlap detection initializes
        for (int i = 0; i < 10; i++)
            game.Loop.RunSingleTick();

        // Main simulation loop — lightweight ticks (no physics/nav)
        int maxTicks = 60 * 60 * maxMinutes;
        int snapshotInterval = 60;
        int logInterval = 60 * 30;

        for (int tick = 0; tick < maxTicks; tick++)
        {

            coordinator.ResetClaims();

            foreach (var bot in bots)
                bot.PreTick(tick);

            // Dispatch bot actions
            bool anyAction = false;
            foreach (var bot in bots)
            {
                if (bot.WantsToInteract)
                {
                    InjectOverlap(game, bot.Player, bot.CurrentTarget);
                    game.State.AddComponent(bot.Player, new InteractAction { UserId = bot.UserId });
                    anyAction = true;
                }
            }
            if (anyAction)
            {
                game.Dispatcher.Update(game.State);
                runner.RunSystems(); // process action effects immediately
            }

            // Lightweight tick — game systems only, no physics/nav/history
            runner.Tick();

            // Record metrics
            if (tick % snapshotInterval == 0)
                metrics.RecordSnapshot(game, tick);

            // Progress logging
            if (tick % logInterval == 0 && tick > 0)
            {
                var snap = metrics.Snapshots.LastOrDefault();
                if (snap != null)
                {
                    var ps0 = game.State.GetComponent<PlayerStateComponent>(bots[0].Player);
                    _output.WriteLine($"  [{tick / 3600f:F1}m] Coins={snap.Coins} Houses={snap.Houses} Cows={snap.Cows} Housed={snap.HousedCows} Follow={snap.FollowingCows} Wild={snap.WildCows} Food={snap.TotalFood} Milk={snap.TotalMilk} | {bots[0].LastAction}");
                }
            }

            // Check end condition
            if (HasFinalStructure(game))
            {
                metrics.SessionEndTick = tick;
                metrics.RecordSnapshot(game, tick);
                _output.WriteLine($"  FINAL STRUCTURE BUILT at tick {tick} ({tick / 3600f:F1} min)");
                break;
            }

            metrics.TotalTicks = tick;
        }

        // Generate report
        string report = metrics.GenerateReport(bots);
        _output.WriteLine(report);

        // Report completion status
        if (metrics.SessionEndTick > 0)
            _output.WriteLine($"GAME COMPLETED at {metrics.SessionEndTick / 3600f:F1} min");
        else
            _output.WriteLine($"GAME DID NOT COMPLETE in {maxMinutes} min — partial metrics reported above");

        // Export CSV files
        string csvDir = Path.Combine(Path.GetDirectoryName(typeof(BotSimulationTests).Assembly.Location)!, "sim_results");
        Directory.CreateDirectory(csvDir);
        string helpers = helpersEnabled ? "helpers" : "nohelpers";
        string tag = $"{botCount}bot_{maxMinutes}min_{(selectiveBreeding ? "selective" : "random")}_{helpers}";
        string resourcesPath = Path.Combine(csvDir, $"resources_{tag}.csv");
        string buildingsPath = Path.Combine(csvDir, $"buildings_{tag}.csv");
        File.WriteAllText(resourcesPath, metrics.ExportCsv());
        File.WriteAllText(buildingsPath, metrics.ExportBuildingsCsv());
        File.WriteAllText(Path.Combine(csvDir, $"actions_{tag}.txt"), report);
        _output.WriteLine($"CSV exported to: {csvDir}");

    }

    // ─── Multi-run averaged test ───

    [Theory]
    [InlineData(5, 1, 30, true, true)]   // selective + helpers
    [InlineData(5, 1, 30, true, false)]  // selective + NO helpers
    [InlineData(5, 1, 30, false, true)]  // random + helpers
    [InlineData(5, 1, 30, false, false)] // random + NO helpers
    public void RunSimulationAveraged(int runs, int botCount, int maxMinutes, bool selectiveBreeding, bool helpersEnabled)
    {
        string tag = $"{(selectiveBreeding ? "selective" : "random")}+{(helpersEnabled ? "helpers" : "nohelpers")}";
        _output.WriteLine($"Running {runs}x: {tag}, {maxMinutes} min...\n");

        // Run N simulations in parallel
        var results = new SimResult[runs];
        Parallel.For(0, runs, i =>
        {
            results[i] = RunSingleSim(botCount, maxMinutes, selectiveBreeding, helpersEnabled);
        });

        // Individual results
        for (int i = 0; i < runs; i++)
        {
            var r = results[i];
            _output.WriteLine($"  Run {i + 1}: {(r.Completed ? $"DONE {r.Minutes:F1}m" : $"timeout")}  " +
                $"Houses={r.Houses}  Cows={r.Cows}  Helpers={r.Helpers}  Coins={r.Coins}  " +
                $"Land={r.LandRemaining}  MilkClicks={r.MilkClicks}  CumMilk={r.CumMilk}");
        }

        // Averages
        int completions = results.Count(r => r.Completed);
        float avgMinutes = completions > 0 ? (float)results.Where(r => r.Completed).Average(r => r.Minutes) : maxMinutes;
        float avgHouses = (float)results.Average(r => r.Houses);
        float avgCows = (float)results.Average(r => r.Cows);
        float avgHelpers = (float)results.Average(r => r.Helpers);
        float avgCoins = (float)results.Average(r => r.Coins);
        float avgLand = (float)results.Average(r => r.LandRemaining);
        float avgMilkClicks = (float)results.Average(r => r.MilkClicks);
        float avgCumMilk = (float)results.Average(r => r.CumMilk);
        float avgCumCoins = (float)results.Average(r => r.CumCoins);

        _output.WriteLine($"\n── Averages ({tag}, {runs} runs) ──");
        _output.WriteLine($"  Completed:   {completions}/{runs}" +
            (completions > 0 ? $" (avg {avgMinutes:F1} min)" : ""));
        _output.WriteLine($"  Houses:      {avgHouses:F1}");
        _output.WriteLine($"  Cows:        {avgCows:F1}");
        _output.WriteLine($"  Helpers:     {avgHelpers:F1}");
        _output.WriteLine($"  Coins:       {avgCoins:F0}");
        _output.WriteLine($"  Land left:   {avgLand:F1}");
        _output.WriteLine($"  MilkClicks:  {avgMilkClicks:F0}");
        _output.WriteLine($"  CumMilk:     {avgCumMilk:F0}");
        _output.WriteLine($"  CumCoins:    {avgCumCoins:F0}");

        // Variance warnings — flag metrics where max/min differ by >50% of the mean
        _output.WriteLine($"\n── Variance Check ──");
        CheckVariance("Houses", results.Select(r => (float)r.Houses).ToArray());
        CheckVariance("Cows", results.Select(r => (float)r.Cows).ToArray());
        CheckVariance("Coins", results.Select(r => (float)r.Coins).ToArray());
        CheckVariance("CumMilk", results.Select(r => (float)r.CumMilk).ToArray());
        CheckVariance("CumCoins", results.Select(r => (float)r.CumCoins).ToArray());
        CheckVariance("MilkClicks", results.Select(r => (float)r.MilkClicks).ToArray());
        CheckVariance("LandRemaining", results.Select(r => (float)r.LandRemaining).ToArray());
        if (completions > 0 && completions < runs)
            _output.WriteLine($"  WARNING: Only {completions}/{runs} runs completed — high completion variance!");

        // Export CSV — individual runs + averages
        string csvDir = Path.Combine(Path.GetDirectoryName(typeof(BotSimulationTests).Assembly.Location)!, "sim_results");
        Directory.CreateDirectory(csvDir);
        string csvTag = $"{botCount}bot_{maxMinutes}min_{(selectiveBreeding ? "selective" : "random")}_{(helpersEnabled ? "helpers" : "nohelpers")}";
        string csvPath = Path.Combine(csvDir, $"averaged_{csvTag}.csv");

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Run,Completed,Minutes,Houses,Cows,Helpers,Coins,Food,LandRemaining,LandCost,MilkClicks,CumMilk,CumCoins");
        for (int i = 0; i < runs; i++)
        {
            var r = results[i];
            csv.AppendLine(string.Format(inv, "{0},{1},{2:F2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12}",
                i + 1, r.Completed ? 1 : 0, r.Minutes, r.Houses, r.Cows, r.Helpers, r.Coins, r.Food, r.LandRemaining, r.LandCost, r.MilkClicks, r.CumMilk, r.CumCoins));
        }
        csv.AppendLine(string.Format(inv, "AVG,{0}/{1},{2:F2},{3:F1},{4:F1},{5:F1},{6:F0},{7:F0},{8:F1},,{9:F0},{10:F0},{11:F0}",
            completions, runs, avgMinutes, avgHouses, avgCows, avgHelpers, avgCoins, (float)results.Average(r => r.Food), avgLand, avgMilkClicks, avgCumMilk, avgCumCoins));
        File.WriteAllText(csvPath, csv.ToString());
        _output.WriteLine($"\nCSV exported to: {csvPath}");
    }

    private void CheckVariance(string name, float[] values)
    {
        float mean = values.Average();
        if (mean < 1f) return; // skip near-zero metrics
        float min = values.Min();
        float max = values.Max();
        float spread = max - min;
        float pct = spread / mean * 100f;
        string status = pct > 50 ? "HIGH VARIANCE" : pct > 25 ? "moderate" : "ok";
        if (pct > 25)
            _output.WriteLine($"  {name}: avg={mean:F0} min={min:F0} max={max:F0} spread={pct:F0}% — {status}");
    }

    /// <summary>
    /// Quick smoke test — runs a short simulation to verify the bot works.
    /// </summary>
    [Fact]
    public void SmokeTest_BotCanMilkAndBuild()
    {
        var game = CreateGame();
        var userId = Guid.NewGuid();
        var player = AddBotPlayer(game, userId);
        var bot = new BotBrain(game, player, userId, 0, new BotCoordinator());

        int startCoins = 0;
        foreach (var e in game.State.Filter<GlobalResourcesComponent>())
        {
            startCoins = game.State.GetComponent<GlobalResourcesComponent>(e).Coins;
            break;
        }
        _output.WriteLine($"Starting coins: {startCoins}");

        var runner = new LightSimRunner(game);
        for (int i = 0; i < 10; i++) game.Loop.RunSingleTick(); // bootstrap physics

        var coordinator = new BotCoordinator();
        // Re-create bot with coordinator we can access
        bot = new BotBrain(game, player, userId, 0, coordinator);
        int dispatchCount = 0;
        // Run for 30 seconds of game time
        for (int tick = 0; tick < 60 * 30; tick++)
        {
            coordinator.ResetClaims();
            bot.PreTick(tick);
            if (bot.WantsToInteract)
            {
                dispatchCount++;
                InjectOverlap(game, player, bot.CurrentTarget);
                game.State.AddComponent(player, new InteractAction { UserId = userId });
                game.Dispatcher.Update(game.State);
                runner.RunSystems();
            }
            runner.Tick();


            if (tick % 300 == 0)
            {
                foreach (var e in game.State.Filter<GlobalResourcesComponent>())
                {
                    var r = game.State.GetComponent<GlobalResourcesComponent>(e);
                    int houses = 0;
                    foreach (var _ in game.State.Filter<HouseComponent>()) houses++;
                    _output.WriteLine($"  T{tick}: Coins={r.Coins} Grass={r.Grass} Milk={r.Milk} Houses={houses} MilkClicks={bot.TotalMilkClicks}");
                    break;
                }
            }
        }

        // Should have done something useful
        bot.TotalMilkClicks.Should().BeGreaterThan(0, "Bot should have milked at least once");

        int finalHouses = 0;
        foreach (var _ in game.State.Filter<HouseComponent>()) finalHouses++;
        _output.WriteLine($"Final: MilkClicks={bot.TotalMilkClicks} Houses={finalHouses}\n  {bot.ActionStats()}");
    }

    [Fact]
    public void DebugBreeding()
    {
        var game = CreateGame();
        var coordinator = new BotCoordinator();
        var userId = Guid.NewGuid();
        var player = AddBotPlayer(game, userId);
        var bot = new BotBrain(game, player, userId, 0, coordinator, selectiveBreeding: false);

        var runner = new LightSimRunner(game);
        for (int i = 0; i < 10; i++) game.Loop.RunSingleTick();

        // Enable debug logging on breed-relevant ticks
        int maxTicks = 60 * 60 * 10; // 10 minutes
        int breedLogCount = 0;

        bot.DebugLog = msg =>
        {
            if (breedLogCount < 200) // cap output
            {
                _output.WriteLine(msg);
                breedLogCount++;
            }
        };

        for (int tick = 0; tick < maxTicks; tick++)
        {
            coordinator.ResetClaims();

            // Log state every 30 seconds + on every Deciding tick after love house exists
            bool hasLoveHouse = false;
            foreach (var _ in game.State.Filter<LoveHouseComponent>()) { hasLoveHouse = true; break; }

            bool isDeciding = bot.LastAction == "IDLE" || tick % (60 * 30) == 0;

            if (hasLoveHouse && isDeciding && breedLogCount < 200)
            {
                var globalRes = bot.GetGlobalResources();
                int cows = 0, housed = 0, wild = 0, following = 0, exhausted = 0;
                foreach (var e in game.State.Filter<CowComponent>())
                {
                    cows++;
                    var cow = game.State.GetComponent<CowComponent>(e);
                    if (cow.HouseId != Entity.Null) { housed++; if (cow.Exhaust >= cow.MaxExhaust) exhausted++; }
                    else if (cow.FollowingPlayer != Entity.Null) following++;
                    else wild++;
                }
                int houses = 0;
                foreach (var _ in game.State.Filter<HouseComponent>()) houses++;

                // Love house state
                foreach (var e in game.State.Filter<LoveHouseComponent>())
                {
                    var lh = game.State.GetComponent<LoveHouseComponent>(e);
                    _output.WriteLine($"[{tick / 3600f:F2}m] cows={cows}(housed={housed},exhaust={exhausted},wild={wild},follow={following}) houses={houses} coins={globalRes.Coins} food={globalRes.Grass + globalRes.Carrot + globalRes.Apple + globalRes.Mushroom} | LoveHouse: cow1={lh.CowId1.Id} cow2={lh.CowId2.Id} progress={lh.BreedProgress}/{lh.BreedCost} | last={bot.LastAction}");
                    break;
                }
            }

            bot.PreTick(tick);

            if (bot.WantsToInteract)
            {
                InjectOverlap(game, bot.Player, bot.CurrentTarget);
                game.State.AddComponent(bot.Player, new InteractAction { UserId = bot.UserId });
                game.Dispatcher.Update(game.State);
                runner.RunSystems();
            }
            runner.Tick();
        }

        _output.WriteLine($"\nFinal action stats: {bot.ActionStats()}");
    }
}
