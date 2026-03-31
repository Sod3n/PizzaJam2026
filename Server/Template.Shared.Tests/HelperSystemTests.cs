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
public class HelperSystemTests
{
    private readonly ITestOutputHelper _output;

    public HelperSystemTests(ITestOutputHelper output) => _output = output;

    private Game CreateGame()
    {
        ServiceLocator.Reset();
        var field = typeof(TemplateGameFactory).GetField("_appInitialized", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, false);
        return TemplateGameFactory.CreateGame(tickRate: 60);
    }

    private Entity SpawnPlayer(Game game, System.Guid userId)
    {
        var ctx = new Context(game.State, Entity.Null, null!);
        return PlayerDefinition.Create(ctx, userId, new Vector2(0, 0), 0);
    }

    private void RunTicks(Game game, int count)
    {
        for (int i = 0; i < count; i++)
            game.Loop.RunSingleTick();
    }

    // ─── Assistant Tests ───

    [Fact]
    public void Assistant_ShouldFollowPlayer()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId);
        var ctx = new Context(game.State, Entity.Null, null!);

        var assistant = HelperDefinition.Create(ctx, new Vector2(0, 0), HelperType.Assistant, player);

        // Move player far away (set velocity briefly so followers detect movement)
        ref var pt = ref game.State.GetComponent<Transform2D>(player);
        pt.Position = new Vector2(15, 0);
        ref var pb = ref game.State.GetComponent<CharacterBody2D>(player);
        pb.Velocity = new Vector2(5, 0);

        RunTicks(game, 5);

        // Stop player and let assistant catch up
        pb = ref game.State.GetComponent<CharacterBody2D>(player);
        pb.Velocity = Vector2.Zero;

        RunTicks(game, 180);

        var assistantPos = game.State.GetComponent<Transform2D>(assistant).Position;
        var playerPos = game.State.GetComponent<Transform2D>(player).Position;
        var dist = Vector2.Distance(assistantPos, playerPos);

        _output.WriteLine($"Player: ({playerPos.X}, {playerPos.Y})");
        _output.WriteLine($"Assistant: ({assistantPos.X}, {assistantPos.Y})");
        _output.WriteLine($"Distance: {dist}");

        // Assistant should have reached near the player
        ((float)dist).Should().BeLessThan(8f, "Assistant should be near player after 3 seconds");
    }

    // ─── Gatherer Tests ───

    [Fact]
    public void Gatherer_ShouldCollectFoodAndDeposit()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId);
        var ctx = new Context(game.State, Entity.Null, null!);

        // Spawn gatherer far from existing entities to avoid interference
        var spawnPos = new Vector2(-50, -50);
        ref var pt = ref game.State.GetComponent<Transform2D>(player);
        pt.Position = spawnPos;

        var gatherer = HelperDefinition.Create(ctx, spawnPos + new Vector2(1, 0), HelperType.Gatherer, player);

        // Spawn several food nearby (high durability so they survive)
        for (int i = 0; i < 5; i++)
        {
            var food = GrassDefinition.Create(ctx, spawnPos + new Vector2(3 + i, 0));
            ref var grass = ref game.State.GetComponent<GrassComponent>(food);
            grass.FoodType = FoodType.Grass;
            grass.Durability = 20;
            grass.MaxDurability = 20;
        }

        // Get initial global resources
        int initialGrass = 0;
        foreach (var e in game.State.Filter<GlobalResourcesComponent>())
        {
            initialGrass = game.State.GetComponent<GlobalResourcesComponent>(e).Grass;
            break;
        }

        _output.WriteLine($"Initial grass: {initialGrass}");

        for (int tick = 0; tick < 600; tick++)
        {
            game.Loop.RunSingleTick();

            if (tick % 60 == 0)
            {
                var helper = game.State.GetComponent<HelperComponent>(gatherer);
                var pos = game.State.GetComponent<Transform2D>(gatherer).Position;
                _output.WriteLine($"T{tick}: State={helper.State} Pos=({pos.X:F1},{pos.Y:F1}) BagGrass={helper.BagGrass} Target={helper.TargetEntity.Id}");
            }
        }

        int finalGrass = 0;
        foreach (var e in game.State.Filter<GlobalResourcesComponent>())
        {
            finalGrass = game.State.GetComponent<GlobalResourcesComponent>(e).Grass;
            break;
        }

        _output.WriteLine($"Final grass: {finalGrass}");
        finalGrass.Should().BeGreaterThan(initialGrass, "Gatherer should have deposited food");
    }

    [Fact]
    public void Gatherer_ShouldTransitionThroughStates()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId);
        var ctx = new Context(game.State, Entity.Null, null!);

        var gatherer = HelperDefinition.Create(ctx, new Vector2(0, 0), HelperType.Gatherer, player);

        // Spawn food very close
        GrassDefinition.Create(ctx, new Vector2(1, 0));

        // Should start Idle
        var helper = game.State.GetComponent<HelperComponent>(gatherer);
        helper.State.Should().Be(HelperState.Idle);

        // After a tick, should find food and start moving
        RunTicks(game, 1);
        helper = game.State.GetComponent<HelperComponent>(gatherer);
        _output.WriteLine($"After 1 tick: State={helper.State} Target={helper.TargetEntity.Id}");
        helper.State.Should().BeOneOf(new[] { HelperState.SeekingTarget, HelperState.MovingToTarget, HelperState.Working },
            "Gatherer should have started seeking or moving to food");
    }

    // ─── Seller Tests ───

    [Fact]
    public void Seller_ShouldSellMilkAndDepositCoins()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId);
        var ctx = new Context(game.State, Entity.Null, null!);

        // Give player some milk
        foreach (var e in game.State.Filter<GlobalResourcesComponent>())
        {
            ref var global = ref game.State.GetComponent<GlobalResourcesComponent>(e);
            global.Milk = 5;
            break;
        }

        // Spawn seller near player
        var seller = HelperDefinition.Create(ctx, new Vector2(0, 0), HelperType.Seller, player);

        // Need a sell point nearby
        SellPointDefinition.Create(ctx, new Vector2(10, 0));

        int initialCoins = 0;
        foreach (var e in game.State.Filter<GlobalResourcesComponent>())
        {
            initialCoins = game.State.GetComponent<GlobalResourcesComponent>(e).Coins;
            break;
        }

        _output.WriteLine($"Initial coins: {initialCoins}, milk: 5");

        for (int tick = 0; tick < 300; tick++)
        {
            game.Loop.RunSingleTick();

            if (tick % 30 == 0)
            {
                var helper = game.State.GetComponent<HelperComponent>(seller);
                var pos = game.State.GetComponent<Transform2D>(seller).Position;
                _output.WriteLine($"T{tick}: State={helper.State} Pos=({pos.X:F1},{pos.Y:F1}) BagMilk={helper.BagMilk} BagCoins={helper.BagCoins}");
            }
        }

        int finalCoins = 0;
        int finalMilk = 0;
        foreach (var e in game.State.Filter<GlobalResourcesComponent>())
        {
            var global = game.State.GetComponent<GlobalResourcesComponent>(e);
            finalCoins = global.Coins;
            finalMilk = global.Milk;
            break;
        }

        _output.WriteLine($"Final coins: {finalCoins}, milk: {finalMilk}");
        finalCoins.Should().BeGreaterThan(initialCoins, "Seller should have earned coins from selling milk");
    }

    // ─── Builder Tests ───

    [Fact]
    public void Builder_ShouldContributeCoinsToLand()
    {
        var game = CreateGame();
        var userId = System.Guid.NewGuid();
        var player = SpawnPlayer(game, userId);
        var ctx = new Context(game.State, Entity.Null, null!);

        // Move player far from game entities
        var spawnPos = new Vector2(-50, -50);
        ref var pt = ref game.State.GetComponent<Transform2D>(player);
        pt.Position = spawnPos;

        // Give player coins
        foreach (var e in game.State.Filter<GlobalResourcesComponent>())
        {
            ref var global = ref game.State.GetComponent<GlobalResourcesComponent>(e);
            global.Coins = 100;
            break;
        }

        // Spawn builder near player and directly load coins into its bag
        var builder = HelperDefinition.Create(ctx, spawnPos + new Vector2(1, 0), HelperType.Builder, player);
        ref var builderComp = ref game.State.GetComponent<HelperComponent>(builder);
        builderComp.BagCoins = 20;

        // Spawn a cheap land plot nearby (far from other lands)
        var land = LandDefinition.Create(ctx, spawnPos + new Vector2(5, 0), 5, LandType.House, 99, 99, 0);

        int initialLandCoins = game.State.GetComponent<LandComponent>(land).CurrentCoins;
        _output.WriteLine($"Initial land coins: {initialLandCoins}/5");

        for (int tick = 0; tick < 600; tick++)
        {
            game.Loop.RunSingleTick();

            if (!game.State.HasComponent<LandComponent>(land))
            {
                _output.WriteLine($"T{tick}: Land was completed and deleted!");
                break;
            }

            if (tick % 60 == 0)
            {
                var helper = game.State.GetComponent<HelperComponent>(builder);
                var pos = game.State.GetComponent<Transform2D>(builder).Position;
                var lc = game.State.GetComponent<LandComponent>(land);
                _output.WriteLine($"T{tick}: State={helper.State} Pos=({pos.X:F1},{pos.Y:F1}) BagCoins={helper.BagCoins} LandCoins={lc.CurrentCoins}/{lc.Threshold}");
            }
        }

        // Either land was completed (deleted) or coins were contributed
        if (game.State.HasComponent<LandComponent>(land))
        {
            var lc = game.State.GetComponent<LandComponent>(land);
            lc.CurrentCoins.Should().BeGreaterThan(initialLandCoins, "Builder should have contributed coins to land");
        }
        // If land was deleted, it was completed — that's success
    }
}
