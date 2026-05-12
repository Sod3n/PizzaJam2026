using System;
using System.IO;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

public sealed class MlBotTrainingTests
{
    private readonly ITestOutputHelper _output;

    public MlBotTrainingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TrainMlBotGenerations()
    {
        if (Environment.GetEnvironmentVariable("RUN_ML_BOT") != "1")
        {
            _output.WriteLine("Skipped. Set RUN_ML_BOT=1 to run generational ML bot training.");
            return;
        }

        int generations = ReadInt("ML_BOT_GENERATIONS", 8);
        int population = ReadInt("ML_BOT_POPULATION", 8);
        int maxMinutes = ReadInt("ML_BOT_MAX_MINUTES", 20);
        int seed = ReadInt("ML_BOT_SEED", 20260429);

        string outputDir = Path.Combine(
            Path.GetDirectoryName(typeof(MlBotTrainingTests).Assembly.Location)!,
            "sim_results");

        var trainer = new MlBotTrainer(_output.WriteLine);
        var best = trainer.Train(generations, population, maxMinutes, seed, outputDir);

        _output.WriteLine($"Best policy: fitness={best.Fitness:F1}, all={best.OpenedAllBuildings}, final={best.OpenedFinalStructure}, built={best.BuiltCount}, rem={best.RemainingLandCount}, path={Path.Combine(outputDir, "ml_policy_best.json")}");
        best.Fitness.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EvaluateMlBotPolicy()
    {
        if (Environment.GetEnvironmentVariable("RUN_ML_BOT_EVAL") != "1")
        {
            _output.WriteLine("Skipped. Set RUN_ML_BOT_EVAL=1 and ML_BOT_POLICY=/path/to/ml_policy_best.json to evaluate a policy.");
            return;
        }

        string policy = Environment.GetEnvironmentVariable("ML_BOT_POLICY");
        policy.Should().NotBeNullOrWhiteSpace("ML_BOT_POLICY must point to a saved policy json");
        File.Exists(policy).Should().BeTrue($"policy file must exist: {policy}");

        int maxMinutes = ReadInt("ML_BOT_MAX_MINUTES", 20);
        int seed = ReadInt("ML_BOT_SEED", 20260429);

        var trainer = new MlBotTrainer(_output.WriteLine);
        var result = trainer.Evaluate(policy!, maxMinutes, seed);

        _output.WriteLine($"Eval: fitness={result.Fitness:F1}, all={result.OpenedAllBuildings}, final={result.OpenedFinalStructure}, built={result.BuiltCount}, rem={result.RemainingLandCount}, ticks={result.Ticks}");
        result.Fitness.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BenchmarkSimWallTime()
    {
        if (Environment.GetEnvironmentVariable("RUN_BOT_BENCH") != "1")
        {
            _output.WriteLine("Skipped. Set RUN_BOT_BENCH=1 to time a 30-min sim episode.");
            return;
        }

        int maxMinutes = ReadInt("ML_BOT_MAX_MINUTES", 30);
        int seed = ReadInt("ML_BOT_SEED", 20260429);

        var trainer = new MlBotTrainer(_output.WriteLine);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = trainer.Evaluate_NoPolicy(maxMinutes, seed);
        sw.Stop();

        double simSeconds = result.Ticks / 60.0;
        double wallSeconds = sw.Elapsed.TotalSeconds;
        _output.WriteLine($"Sim: ticks={result.Ticks} simSec={simSeconds:F1} wallSec={wallSeconds:F2} ratio={simSeconds / wallSeconds:F1}x realtime  all={result.OpenedAllBuildings} final={result.OpenedFinalStructure} built={result.BuiltCount} rem={result.RemainingLandCount}");
        _output.WriteLine(MlBotTrainer.LastPerformanceReport);
    }

    [Fact]
    public void BalanceJsonOverride_AppliesValues()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(MlBotTrainingTests).Assembly.Location)!,
            "..", "..", "..", "..", "Template.Shared", "GameData", "balance_easy.json"));
        System.IO.File.Exists(path).Should().BeTrue($"balance_easy.json should exist at {path}");

        int beforeCoins = Template.Shared.GameData.Balance.Match.StartingCoins;
        int beforeClicks = Template.Shared.GameData.Balance.Cow.ClicksPerMilk;

        Template.Shared.GameData.Balance.LoadFromJson(System.IO.File.ReadAllText(path));

        Template.Shared.GameData.Balance.Match.StartingCoins.Should().Be(300);
        Template.Shared.GameData.Balance.Cow.ClicksPerMilk.Should().Be(1);
        Template.Shared.GameData.Balance.JsonHash.Should().NotBeEmpty();

        _output.WriteLine($"StartingCoins {beforeCoins} → {Template.Shared.GameData.Balance.Match.StartingCoins}");
        _output.WriteLine($"ClicksPerMilk {beforeClicks} → {Template.Shared.GameData.Balance.Cow.ClicksPerMilk}");
        _output.WriteLine($"JsonHash = {Template.Shared.GameData.Balance.JsonHash}");
    }

    private static int ReadInt(string key, int fallback)
    {
        string value = Environment.GetEnvironmentVariable(key);
        return int.TryParse(value, out int parsed) ? parsed : fallback;
    }
}
