using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Factories;
using Template.Shared.Features.Movement;
using Deterministic.GameFramework.Navigation2D.Components;

namespace Template.Shared.Tests;

public sealed class MlBotTrainer
{
    private static readonly object CreateLock = new();
    private static bool _servicesReady;
    private readonly Action<string> _log;
    public static string LastPerformanceReport { get; private set; } = "";

    public MlBotTrainer(Action<string> log = null)
    {
        _log = log ?? (_ => { });
    }

    public MlBotTrainingResult Train(
        int generations,
        int population,
        int maxMinutes,
        int seed,
        string outputDirectory)
    {
        generations = Math.Max(1, generations);
        population = Math.Max(1, population);
        maxMinutes = Math.Max(1, maxMinutes);

        Directory.CreateDirectory(outputDirectory);

        var random = new Random(seed);
        Dictionary<string, float[]> bestWeights = null;
        MlBotTrainingResult best = null;

        for (int generation = 0; generation < generations; generation++)
        {
            var generationResults = new List<MlBotTrainingResult>();
            for (int episode = 0; episode < population; episode++)
            {
                var candidateWeights = bestWeights == null
                    ? null
                    : Mutate(bestWeights, random, generation == 0 ? 0f : 0.18f);

                int episodeSeed = random.Next();
                var result = RunEpisode(generation, episode, maxMinutes, episodeSeed, candidateWeights, learningEnabled: true);
                generationResults.Add(result);

                if (best == null || result.Fitness > best.Fitness)
                {
                    best = result;
                    bestWeights = CloneWeights(result.Weights);
                    WritePolicy(best, outputDirectory, "ml_policy_best.json");
                }
            }

            var genBest = generationResults.OrderByDescending(r => r.Fitness).First();
            _log($"GEN {generation + 1}/{generations}: best={genBest.Fitness:F1} all={genBest.OpenedAllBuildings} final={genBest.OpenedFinalStructure} built={genBest.BuiltCount} rem={genBest.RemainingLandCount} ticks={genBest.Ticks}");
        }

        WritePolicy(best, outputDirectory, $"ml_policy_best_g{best.Generation}_e{best.Episode}.json");
        return best;
    }

    public MlBotTrainingResult Evaluate(string policyPath, int maxMinutes, int seed)
    {
        var policy = LoadPolicy(policyPath);
        return RunEpisode(0, 0, maxMinutes, seed, policy.Weights, learningEnabled: false);
    }

    public MlBotTrainingResult Evaluate_NoPolicy(int maxMinutes, int seed)
        => RunEpisode(0, 0, maxMinutes, seed, null, learningEnabled: false);

    public static MlBotTrainingResult LoadPolicy(string policyPath)
    {
        var json = File.ReadAllText(policyPath);
        return JsonSerializer.Deserialize<MlBotTrainingResult>(json, JsonOptions())!;
    }

    private MlBotTrainingResult RunEpisode(
        int generation,
        int episode,
        int maxMinutes,
        int seed,
        IReadOnlyDictionary<string, float[]> initialWeights,
        bool learningEnabled)
    {
        var game = CreateGame();
        var userId = Guid.NewGuid();
        var player = AddBotPlayer(game, userId);
        var bot = new MlBotBrain(game, player, userId, seed, initialWeights: initialWeights, learningEnabled: learningEnabled);
        var runner = new LightSimRunner(game);

        for (int i = 0; i < 10; i++) game.Loop.RunSingleTick();

        int maxTicks = 60 * 60 * maxMinutes;
        int tick;
        bool openedAll = false;

        for (tick = 0; tick < maxTicks; tick++)
        {
            bot.PreTick(tick);

            if (bot.DesiredDirection.SqrMagnitude > (Float)0.001f)
                game.State.AddComponent(player, bot.CreateMoveAction());

            if (bot.WantsToInteract)
            {
                InjectOverlap(game, bot.Player, bot.CurrentTarget);
                game.State.AddComponent(bot.Player, new InteractAction { UserId = bot.UserId });
            }

            game.Dispatcher.Update(game.State);
            MockNavigation(game);
            runner.Tick();

            openedAll = CountRemainingLand(game, out _) == 0 && CountBuilt<FinalStructureComponent>(game) > 0;
            if (openedAll) break;
        }

        LastPerformanceReport = runner.PerformanceReport();
        var snapshot = Capture(game);
        bool openedFinal = CountBuilt<FinalStructureComponent>(game) > 0;
        float fitness = CalculateFitness(snapshot, tick, openedFinal, openedAll);

        return new MlBotTrainingResult
        {
            Generation = generation,
            Episode = episode,
            Seed = seed,
            Fitness = fitness,
            OpenedFinalStructure = openedFinal,
            OpenedAllBuildings = openedAll,
            Ticks = tick,
            BuiltCount = snapshot.BuiltCount,
            RemainingLandCount = snapshot.RemainingLandCount,
            RemainingLandCost = snapshot.RemainingLandCost,
            Coins = snapshot.Coins,
            Milk = snapshot.Milk,
            Food = snapshot.Food,
            Cows = snapshot.Cows,
            Helpers = snapshot.Helpers,
            Pets = snapshot.Pets,
            Weights = bot.ExportWeights()
        };
    }

    private static float CalculateFitness(EpisodeSnapshot s, int ticks, bool openedFinal, bool openedAll)
    {
        return s.BuiltCount * 1200f
            + s.Helpers * 700f
            + s.Pets * 900f
            + s.Cows * 150f
            + s.Coins * 0.08f
            + s.Milk * 0.05f
            + s.Food * 0.03f
            - s.RemainingLandCount * 350f
            - s.RemainingLandCost * 0.25f
            + (openedFinal ? 25000f : 0f)
            + (openedAll ? 50000f : 0f)
            - ticks * 0.03f;
    }

    private static EpisodeSnapshot Capture(Game game)
    {
        var res = default(GlobalResourcesComponent);
        foreach (var e in game.State.Filter<GlobalResourcesComponent>())
        {
            res = game.State.GetComponent<GlobalResourcesComponent>(e);
            break;
        }

        CountRemainingLand(game, out int remainingCost);

        return new EpisodeSnapshot(
            res.Coins,
            res.Milk,
            res.Grass + res.Carrot + res.Apple + res.Mushroom,
            CountBuilt<CowComponent>(game),
            CountBuilt<HelperComponent>(game),
            CountBuilt<HelperPetComponent>(game),
            CountAllBuildings(game),
            CountRemainingLand(game, out _),
            remainingCost);
    }

    private static int CountAllBuildings(Game game)
    {
        return CountBuilt<HouseComponent>(game)
            + CountBuilt<LoveHouseComponent>(game)
            + CountBuilt<SellPointComponent>(game)
            + CountBuilt<CarrotFarmComponent>(game)
            + CountBuilt<AppleOrchardComponent>(game)
            + CountBuilt<MushroomCaveComponent>(game)
            + CountBuilt<HelperAssistantComponent>(game)
            + CountBuilt<WarehouseComponent>(game)
            + CountBuilt<FinalStructureComponent>(game);
    }

    private static int CountBuilt<T>(Game game) where T : unmanaged, IComponent
    {
        int count = 0;
        foreach (var _ in game.State.Filter<T>()) count++;
        return count;
    }

    private static int CountRemainingLand(Game game, out int remainingCost)
    {
        int count = 0;
        remainingCost = 0;
        foreach (var e in game.State.Filter<LandComponent>())
        {
            var land = game.State.GetComponent<LandComponent>(e);
            if (land.Locked != 0) continue;
            int remaining = land.Threshold - land.CurrentCoins;
            if (remaining <= 0) continue;
            count++;
            remainingCost += remaining;
        }
        return count;
    }

    private static Dictionary<string, float[]> Mutate(IReadOnlyDictionary<string, float[]> source, Random random, float sigma)
    {
        var mutated = CloneWeights(source);
        foreach (var weights in mutated.Values)
        {
            for (int i = 0; i < weights.Length; i++)
                weights[i] += NextGaussian(random) * sigma;
        }
        return mutated;
    }

    private static float NextGaussian(Random random)
    {
        double u1 = 1.0 - random.NextDouble();
        double u2 = 1.0 - random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
    }

    private static Dictionary<string, float[]> CloneWeights(IReadOnlyDictionary<string, float[]> source)
    {
        if (source == null) return null;
        return source.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    private static void WritePolicy(MlBotTrainingResult result, string outputDirectory, string fileName)
    {
        var path = Path.Combine(outputDirectory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true
    };

    private static Game CreateGame()
    {
        lock (CreateLock)
        {
            EnsureServicesInitialized();
            return TemplateGameFactory.CreateGame(tickRate: 60);
        }
    }

    private static void EnsureServicesInitialized()
    {
        if (_servicesReady) return;
        ServiceLocator.Reset();
        var field = typeof(TemplateGameFactory).GetField("_appInitialized", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(null, false);
        TemplateGameFactory.CreateGame(tickRate: 60);
        _servicesReady = true;
    }

    private static Entity AddBotPlayer(Game game, Guid userId)
    {
        Entity worldEntity = Entity.Null;
        foreach (var e in game.State.Filter<World>())
        {
            worldEntity = e;
            break;
        }

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

    private static void InjectOverlap(Game game, Entity player, Entity target)
    {
        if (target == Entity.Null) return;
        if (!game.State.HasComponent<PlayerStateComponent>(player)) return;

        var ps = game.State.GetComponent<PlayerStateComponent>(player);
        if (ps.InteractionZone == Entity.Null) return;
        if (!game.State.HasComponent<Area2D>(ps.InteractionZone)) return;

        ref var area = ref game.State.GetComponent<Area2D>(ps.InteractionZone);
        area.OverlappingEntities = new List8<int>();
        area.OverlappingEntities.Add(target.Id);
        area.HasOverlappingBodies = true;
    }

    private static void MockNavigation(Game game)
    {
        float dt = 1f / 60f;
        foreach (var entity in game.State.Filter<NavigationAgent2D>())
        {
            if (!game.State.HasComponent<Transform2D>(entity)) continue;

            ref var nav = ref game.State.GetComponent<NavigationAgent2D>(entity);
            ref var transform = ref game.State.GetComponent<Transform2D>(entity);

            var current = transform.Position;
            var target = nav.TargetPosition;
            float dx = (float)(target.X - current.X);
            float dy = (float)(target.Y - current.Y);
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            float threshold = (float)nav.TargetDesiredDistance;
            if (threshold <= 0) threshold = 2f;

            if (dist <= threshold)
            {
                nav.IsNavigationFinished = true;
                nav.Velocity = Vector2.Zero;
                if (game.State.HasComponent<CharacterBody2D>(entity))
                {
                    ref var body = ref game.State.GetComponent<CharacterBody2D>(entity);
                    body.Velocity = Vector2.Zero;
                }
                continue;
            }

            nav.IsNavigationFinished = false;
            nav.IsTargetReachable = true;
            float speed = (float)nav.MaxSpeed;
            float move = Math.Min(speed * dt, dist);
            float nx = dx / dist;
            float ny = dy / dist;

            if (game.State.HasComponent<CharacterBody2D>(entity))
            {
                ref var body = ref game.State.GetComponent<CharacterBody2D>(entity);
                body.Velocity = new Vector2(nx * speed, ny * speed);
            }
            else
            {
                transform.Position = new Vector2(
                    (float)current.X + nx * move,
                    (float)current.Y + ny * move);
            }

            nav.Velocity = new Vector2(nx * speed, ny * speed);
            nav.DistanceToTarget = (Float)dist;
        }
    }

    private readonly record struct EpisodeSnapshot(
        int Coins,
        int Milk,
        int Food,
        int Cows,
        int Helpers,
        int Pets,
        int BuiltCount,
        int RemainingLandCount,
        int RemainingLandCost);
}
