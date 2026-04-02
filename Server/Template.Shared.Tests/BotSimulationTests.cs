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
using Deterministic.GameFramework.TwoD;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Factories;
using Template.Shared.Systems;
using Deterministic.GameFramework.Navigation2D.Components;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

public class BotSimulationTests
{
    private static readonly object _createLock = new();
    private static bool _servicesReady;
    private readonly ITestOutputHelper _output;

    public BotSimulationTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Ensure ServiceLocator is initialized exactly once — Reset is destructive
    /// and must not run while other threads are mid-simulation.
    /// </summary>
    private static void EnsureServicesInitialized()
    {
        if (_servicesReady) return;
        ServiceLocator.Reset();
        var field = typeof(TemplateGameFactory).GetField("_appInitialized", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, false);
        // Create and discard a game to force all static registration
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
    /// Mock navigation: move all NavigationAgent2D entities directly toward their target.
    /// Replaces navmesh pathfinding so helpers can actually move in the sim.
    /// </summary>
    private static void MockNavigation(Game game)
    {
        float dt = 1f / 60f; // 60 tps
        foreach (var entity in game.State.Filter<NavigationAgent2D>())
        {
            if (!game.State.HasComponent<Transform2D>(entity)) continue;

            ref var nav = ref game.State.GetComponent<NavigationAgent2D>(entity);
            ref var transform = ref game.State.GetComponent<Transform2D>(entity);

            var current = transform.Position;
            var target = nav.TargetPosition;
            float dx = (float)(target.X - current.X);
            float dy = (float)(target.Y - current.Y);
            float dist = (float)System.Math.Sqrt(dx * dx + dy * dy);

            float threshold = (float)nav.TargetDesiredDistance;
            if (threshold <= 0) threshold = 2f;

            if (dist <= threshold)
            {
                nav.IsNavigationFinished = true;
                nav.Velocity = new Vector2(0, 0);
                if (game.State.HasComponent<CharacterBody2D>(entity))
                {
                    ref var body = ref game.State.GetComponent<CharacterBody2D>(entity);
                    body.Velocity = new Vector2(0, 0);
                }
                continue;
            }

            nav.IsNavigationFinished = false;
            nav.IsTargetReachable = true;
            float speed = (float)nav.MaxSpeed;
            float move = System.Math.Min(speed * dt, dist);
            float nx = dx / dist;
            float ny = dy / dist;

            // Set velocity on CharacterBody2D so physics moves the entity
            if (game.State.HasComponent<CharacterBody2D>(entity))
            {
                ref var body = ref game.State.GetComponent<CharacterBody2D>(entity);
                body.Velocity = new Vector2(nx * speed, ny * speed);
            }
            else
            {
                // No physics body — move transform directly
                transform.Position = new Vector2(
                    (float)current.X + nx * move,
                    (float)current.Y + ny * move);
            }

            nav.Velocity = new Vector2(nx * speed, ny * speed);
            nav.DistanceToTarget = (Float)dist;
        }
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
        int MilkClicks, int BreedClicks, int GatherClicks, int TotalClicks,
        float ClickPct, int CumMilk, int CumCoins,
        int FoodFarms, float LastFarmMinute, int Mushroom,
        float FirstCarrotMinute, float FirstAppleMinute, float FirstMushroomMinute,
        int CowsGrass, int CowsCarrot, int CowsApple, int CowsMushroom, int TotalBreeds,
        float EraGrassMinute, float EraCarrotMinute, float EraAppleMinute, float EraMushroomMinute);

    private SimResult RunSingleSim(int botCount, int maxMinutes, bool selectiveBreeding, bool helpersEnabled, int breedLevel = 1, bool directionalExpansion = false)
    {
        Game game;
        lock (_createLock)
        {
            EnsureServicesInitialized();
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
            bots.Add(new BotBrain(game, player, userId, i, coordinator, selectiveBreeding, breedLevel, directionalExpansion));
        }

        CowSystem.SetHelpersEnabled(game.State, helpersEnabled);
        var runner = new LightSimRunner(game);
        for (int i = 0; i < 10; i++) game.Loop.RunSingleTick();

        var metrics = new SimulationMetrics { BotCount = botCount };
        int maxTicks = 60 * 60 * maxMinutes;
        bool completed = false;
        int endTick = maxTicks;
        int lastFarmCount = 0;
        int lastFarmTick = 0;
        int firstCarrotTick = -1, firstAppleTick = -1, firstMushroomTick = -1;
        // Era reach timing: when bot first has a built house/building at each distance tier
        int eraGrassTick = 0; // always start in grass era
        int eraCarrotTick = -1, eraAppleTick = -1, eraMushroomTick = -1;

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
            MockNavigation(game);
            runner.Tick();

            if (tick % 60 == 0)
            {
                metrics.RecordSnapshot(game, tick);
                // Track when new farms are built and per-type first appearance
                int farms = 0;
                foreach (var e in game.State.Filter<FoodFarmComponent>())
                {
                    farms++;
                    var ff = game.State.GetComponent<FoodFarmComponent>(e);
                    if (ff.FoodType == FoodType.Carrot && firstCarrotTick < 0) firstCarrotTick = tick;
                    if (ff.FoodType == FoodType.Apple && firstAppleTick < 0) firstAppleTick = tick;
                    if (ff.FoodType == FoodType.Mushroom && firstMushroomTick < 0) firstMushroomTick = tick;
                }
                if (farms > lastFarmCount) { lastFarmCount = farms; lastFarmTick = tick; }

                // Track era reach: scan built houses for max grid distance
                if (eraCarrotTick < 0 || eraAppleTick < 0 || eraMushroomTick < 0)
                {
                    foreach (var e in game.State.Filter<HouseComponent>())
                    {
                        if (!game.State.HasComponent<Transform2D>(e)) continue;
                        var pos = game.State.GetComponent<Transform2D>(e).Position;
                        int gx = (int)System.Math.Round((float)pos.X / StarGrid.GridStep);
                        int gy = (int)System.Math.Round((float)pos.Y / StarGrid.GridStep);
                        int d = System.Math.Abs(gx) + System.Math.Abs(gy);
                        if (d >= 3 && eraCarrotTick < 0) eraCarrotTick = tick;
                        if (d >= 4 && eraAppleTick < 0) eraAppleTick = tick;
                        if (d >= 5 && eraMushroomTick < 0) eraMushroomTick = tick;
                    }
                }
            }

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
        int totalClicks = bots.Sum(b => b.TotalClicks);
        int ticksClicking = bots.Sum(b => b.TicksClicking);
        float clickPct = endTick > 0 ? ticksClicking * 100f / endTick : 0;

        // Count cow tiers
        int[] cowTiers = new int[4];
        foreach (var e in game.State.Filter<CowComponent>())
        {
            int tier = game.State.GetComponent<CowComponent>(e).PreferredFood;
            if (tier >= 0 && tier < 4) cowTiers[tier]++;
        }
        // Get total breed count
        int totalBreeds = 0;
        foreach (var e in game.State.Filter<GlobalResourcesComponent>())
        { totalBreeds = game.State.GetComponent<GlobalResourcesComponent>(e).TotalBreedCount; break; }

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
            BreedClicks: bots.Sum(b => b.TotalBreedClicks),
            GatherClicks: bots.Sum(b => b.TotalGatherClicks),
            TotalClicks: totalClicks,
            ClickPct: clickPct,
            CumMilk: snap?.CumMilk ?? 0,
            CumCoins: snap?.CumCoins ?? 0,
            FoodFarms: lastFarmCount,
            LastFarmMinute: lastFarmTick / 3600f,
            Mushroom: snap?.Mushroom ?? 0,
            FirstCarrotMinute: firstCarrotTick >= 0 ? firstCarrotTick / 3600f : -1f,
            FirstAppleMinute: firstAppleTick >= 0 ? firstAppleTick / 3600f : -1f,
            FirstMushroomMinute: firstMushroomTick >= 0 ? firstMushroomTick / 3600f : -1f,
            CowsGrass: cowTiers[0], CowsCarrot: cowTiers[1], CowsApple: cowTiers[2], CowsMushroom: cowTiers[3],
            TotalBreeds: totalBreeds,
            EraGrassMinute: 0f,
            EraCarrotMinute: eraCarrotTick >= 0 ? eraCarrotTick / 3600f : -1f,
            EraAppleMinute: eraAppleTick >= 0 ? eraAppleTick / 3600f : -1f,
            EraMushroomMinute: eraMushroomTick >= 0 ? eraMushroomTick / 3600f : -1f
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

        _output.WriteLine($"Starting simulation: {botCount} bot(s), {maxMinutes} min, breeding={(selectiveBreeding ? "selective" : "random")}, helpers={(helpersEnabled ? "ON" : "OFF")}...");

        // Toggle helper spawning — when off, breeding always produces cows instead
        CowSystem.SetHelpersEnabled(game.State, helpersEnabled);

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
            MockNavigation(game);
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
    // Breed level 0 = never breed, 1 = breed to half houses, 2 = breed to fill houses
    // selective + helpers — all breed levels
    [InlineData(6, 1, 45, true, true, 0)]    // selective + helpers + NO breeding
    [InlineData(6, 1, 45, true, true, 1)]    // selective + helpers + half
    [InlineData(6, 1, 45, true, true, 2)]    // selective + helpers + full
    // random + helpers — all breed levels
    [InlineData(6, 1, 45, false, true, 0)]   // random + helpers + NO breeding
    [InlineData(6, 1, 45, false, true, 1)]   // random + helpers + half
    [InlineData(6, 1, 45, false, true, 2)]   // random + helpers + full
    // NO helpers — breed level 1 (representative)
    [InlineData(6, 1, 45, true, false, 1)]   // selective + NO helpers + half
    [InlineData(6, 1, 45, false, false, 1)]  // random + NO helpers + half
    public void RunSimulationAveraged(int runs, int botCount, int maxMinutes, bool selectiveBreeding, bool helpersEnabled, int breedLevel)
    {
        string breedTag = $"_breed{breedLevel}";
        string tag = $"{(selectiveBreeding ? "selective" : "random")}+{(helpersEnabled ? "helpers" : "nohelpers")}{breedTag}";
        _output.WriteLine($"Running {runs}x: {tag}, {maxMinutes} min...\n");

        // Run N simulations in parallel
        var results = new SimResult[runs];
        Parallel.For(0, runs, i =>
        {
            results[i] = RunSingleSim(botCount, maxMinutes, selectiveBreeding, helpersEnabled, breedLevel);
        });

        // Individual results
        for (int i = 0; i < runs; i++)
        {
            var r = results[i];
            _output.WriteLine($"  Run {i + 1}: {(r.Completed ? $"DONE {r.Minutes:F1}m" : $"timeout")}  " +
                $"Houses={r.Houses}  Cows={r.Cows}[G{r.CowsGrass}/C{r.CowsCarrot}/A{r.CowsApple}/M{r.CowsMushroom}]  " +
                $"Breeds={r.TotalBreeds}  Helpers={r.Helpers}  Coins={r.Coins}  " +
                $"Eras[C={r.EraCarrotMinute:F1} A={r.EraAppleMinute:F1} M={r.EraMushroomMinute:F1}]  " +
                $"Farms[C={r.FirstCarrotMinute:F1} A={r.FirstAppleMinute:F1} M={r.FirstMushroomMinute:F1}]  " +
                $"Click%={r.ClickPct:F0}%");
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
        CheckVariance("TotalClicks", results.Select(r => (float)r.TotalClicks).ToArray());
        CheckVariance("ClickPct", results.Select(r => r.ClickPct).ToArray());
        CheckVariance("LandRemaining", results.Select(r => (float)r.LandRemaining).ToArray());
        if (completions > 0 && completions < runs)
            _output.WriteLine($"  WARNING: Only {completions}/{runs} runs completed — high completion variance!");

        // Export CSV — individual runs + averages
        string csvDir = Path.Combine(Path.GetDirectoryName(typeof(BotSimulationTests).Assembly.Location)!, "sim_results");
        Directory.CreateDirectory(csvDir);
        string csvTag = $"{botCount}bot_{maxMinutes}min_{(selectiveBreeding ? "selective" : "random")}_{(helpersEnabled ? "helpers" : "nohelpers")}_breed{breedLevel}";
        string csvPath = Path.Combine(csvDir, $"averaged_{csvTag}.csv");

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Run,Completed,Minutes,Houses,Cows,Helpers,Coins,Food,LandRemaining,LandCost,MilkClicks,BreedClicks,GatherClicks,TotalClicks,ClickPct,CumMilk,CumCoins,CowsGrass,CowsCarrot,CowsApple,CowsMushroom,TotalBreeds,FirstCarrotMin,FirstAppleMin,FirstMushroomMin,EraCarrotMin,EraAppleMin,EraMushroomMin");
        for (int i = 0; i < runs; i++)
        {
            var r = results[i];
            csv.AppendLine(string.Format(inv, "{0},{1},{2:F2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14:F0},{15},{16},{17},{18},{19},{20},{21},{22:F2},{23:F2},{24:F2},{25:F2},{26:F2},{27:F2}",
                i + 1, r.Completed ? 1 : 0, r.Minutes, r.Houses, r.Cows, r.Helpers, r.Coins, r.Food, r.LandRemaining, r.LandCost,
                r.MilkClicks, r.BreedClicks, r.GatherClicks, r.TotalClicks, r.ClickPct, r.CumMilk, r.CumCoins,
                r.CowsGrass, r.CowsCarrot, r.CowsApple, r.CowsMushroom, r.TotalBreeds,
                r.FirstCarrotMinute, r.FirstAppleMinute, r.FirstMushroomMinute,
                r.EraCarrotMinute, r.EraAppleMinute, r.EraMushroomMinute));
        }
        float avgClickPct = (float)results.Average(r => r.ClickPct);
        csv.AppendLine(string.Format(inv, "AVG,{0}/{1},{2:F2},{3:F1},{4:F1},{5:F1},{6:F0},{7:F0},{8:F1},,{9:F0},{10:F0},{11:F0},{12:F0},{13:F0},{14:F0},{15:F0}",
            completions, runs, avgMinutes, avgHouses, avgCows, avgHelpers, avgCoins, (float)results.Average(r => r.Food), avgLand,
            avgMilkClicks, (float)results.Average(r => r.BreedClicks), (float)results.Average(r => r.GatherClicks),
            (float)results.Average(r => r.TotalClicks), avgClickPct, avgCumMilk, avgCumCoins));
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
            MockNavigation(game);
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
        var bot = new BotBrain(game, player, userId, 0, coordinator, selectiveBreeding: true, breedLevel: 2);

        CowSystem.SetHelpersEnabled(game.State, true);
        var runner = new LightSimRunner(game);
        for (int i = 0; i < 10; i++) game.Loop.RunSingleTick();

        int maxTicks = 60 * 60 * 30; // 30 minutes

        for (int tick = 0; tick < maxTicks; tick++)
        {
            coordinator.ResetClaims();

            // Log state every 1 minute
            if (tick % 3600 == 0)
            {
                var globalRes = bot.GetGlobalResources();
                int cows = 0, housed = 0, exhausted = 0, wild = 0, following = 0;
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
                int helpers = 0;
                foreach (var _ in game.State.Filter<HelperComponent>()) helpers++;
                int lands = 0;
                foreach (var _ in game.State.Filter<LandComponent>()) lands++;

                // Check FinalStructure state
                string fsState = "not_spawned";
                foreach (var e in game.State.Filter<LandComponent>())
                {
                    var land = game.State.GetComponent<LandComponent>(e);
                    if (land.Type == LandType.FinalStructure)
                    { fsState = $"land({land.CurrentCoins}/{land.Threshold})"; break; }
                }
                if (fsState == "not_spawned")
                    foreach (var _ in game.State.Filter<FinalStructureComponent>())
                    { fsState = "DONE"; break; }

                _output.WriteLine($"[{tick / 3600f:F0}m] houses={houses} cows={cows}(housed={housed},exhaust={exhausted},wild={wild},follow={following}) helpers={helpers} lands={lands} coins={globalRes.Coins} food={globalRes.Grass + globalRes.Carrot + globalRes.Apple + globalRes.Mushroom} milk={globalRes.Milk + globalRes.VitaminShake + globalRes.AppleYogurt + globalRes.PurplePotion} FS={fsState} | last={bot.LastAction} | {bot.ActionStats()}");
            }

            bot.PreTick(tick);

            if (bot.WantsToInteract)
            {
                InjectOverlap(game, bot.Player, bot.CurrentTarget);
                game.State.AddComponent(bot.Player, new InteractAction { UserId = bot.UserId });
                game.Dispatcher.Update(game.State);
                runner.RunSystems();
            }
            MockNavigation(game);
            runner.Tick();
        }

        _output.WriteLine($"\nFinal: {bot.ActionStats()}");
    }

    // ─── Strategy comparison: broad vs directional expansion ───

    [Theory]
    [InlineData(5, 1, 30, false, true)]   // random breeding, helpers ON
    [InlineData(5, 1, 30, true, true)]    // selective breeding, helpers ON
    [InlineData(5, 1, 30, false, false)]  // random breeding, helpers OFF
    [InlineData(5, 1, 30, true, false)]   // selective breeding, helpers OFF
    public void CompareExpansionStrategy(int runs, int botCount, int maxMinutes, bool selectiveBreeding, bool helpersEnabled)
    {
        string tag = $"{(selectiveBreeding ? "selective" : "random")}+{(helpersEnabled ? "helpers" : "nohelpers")}";
        _output.WriteLine($"=== Strategy Comparison: {tag}, {runs} runs ===\n");

        var broadResults = new SimResult[runs];
        var directionalResults = new SimResult[runs];

        Parallel.For(0, runs * 2, i =>
        {
            bool directional = i >= runs;
            int idx = directional ? i - runs : i;
            var result = RunSingleSim(botCount, maxMinutes, selectiveBreeding, helpersEnabled,
                directionalExpansion: directional);
            if (directional)
                directionalResults[idx] = result;
            else
                broadResults[idx] = result;
        });

        // Print individual results
        void PrintResults(string label, SimResult[] results)
        {
            _output.WriteLine($"── {label} ──");
            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                _output.WriteLine($"  Run {i + 1}: Carrot={r.FirstCarrotMinute:F1}m  Apple={r.FirstAppleMinute:F1}m  Mushroom={r.FirstMushroomMinute:F1}m  " +
                    $"Farms={r.FoodFarms}  Houses={r.Houses}  Cows={r.Cows}  Coins={r.Coins}  CumCoins={r.CumCoins}");
            }
            float avgCarrot = (float)results.Where(r => r.FirstCarrotMinute >= 0).DefaultIfEmpty().Average(r => r.FirstCarrotMinute);
            float avgApple = (float)results.Where(r => r.FirstAppleMinute >= 0).DefaultIfEmpty().Average(r => r.FirstAppleMinute);
            float avgMushroom = (float)results.Where(r => r.FirstMushroomMinute >= 0).DefaultIfEmpty().Average(r => r.FirstMushroomMinute);
            float avgHouses = (float)results.Average(r => r.Houses);
            float avgCumCoins = (float)results.Average(r => r.CumCoins);
            int allFarms = results.Count(r => r.FoodFarms >= 6);
            int gotCarrot = results.Count(r => r.FirstCarrotMinute >= 0);
            int gotApple = results.Count(r => r.FirstAppleMinute >= 0);
            int gotMushroom = results.Count(r => r.FirstMushroomMinute >= 0);
            _output.WriteLine($"  AVG: Carrot={avgCarrot:F1}m({gotCarrot}/{runs})  Apple={avgApple:F1}m({gotApple}/{runs})  Mushroom={avgMushroom:F1}m({gotMushroom}/{runs})  " +
                $"Houses={avgHouses:F0}  CumCoins={avgCumCoins:F0}  AllFarms={allFarms}/{runs}");
        }

        PrintResults("BROAD (default)", broadResults);
        _output.WriteLine("");
        PrintResults("DIRECTIONAL (strategic)", directionalResults);

        // Summary comparison
        _output.WriteLine($"\n── Comparison ({tag}) ──");
        float bC = (float)broadResults.Where(r => r.FirstCarrotMinute >= 0).DefaultIfEmpty().Average(r => r.FirstCarrotMinute);
        float dC = (float)directionalResults.Where(r => r.FirstCarrotMinute >= 0).DefaultIfEmpty().Average(r => r.FirstCarrotMinute);
        float bA = (float)broadResults.Where(r => r.FirstAppleMinute >= 0).DefaultIfEmpty().Average(r => r.FirstAppleMinute);
        float dA = (float)directionalResults.Where(r => r.FirstAppleMinute >= 0).DefaultIfEmpty().Average(r => r.FirstAppleMinute);
        float bM = (float)broadResults.Where(r => r.FirstMushroomMinute >= 0).DefaultIfEmpty().Average(r => r.FirstMushroomMinute);
        float dM = (float)directionalResults.Where(r => r.FirstMushroomMinute >= 0).DefaultIfEmpty().Average(r => r.FirstMushroomMinute);
        _output.WriteLine($"  Carrot:    BROAD={bC:F1}m  DIR={dC:F1}m");
        _output.WriteLine($"  Apple:     BROAD={bA:F1}m  DIR={dA:F1}m");
        _output.WriteLine($"  Mushroom:  BROAD={bM:F1}m  DIR={dM:F1}m");
        _output.WriteLine($"  CumCoins:  BROAD={broadResults.Average(r => r.CumCoins):F0}  DIR={directionalResults.Average(r => r.CumCoins):F0}");
    }
}
