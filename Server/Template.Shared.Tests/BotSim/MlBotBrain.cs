using System;
using System.Collections.Generic;
using System.Linq;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Features.Movement;

namespace Template.Shared.Tests;

/// <summary>
/// Lightweight trainable bot for economy simulations.
/// It keeps the client input surface small: move toward a target, then interact.
/// The learned part is target/action selection through a linear Q function.
/// </summary>
public sealed class MlBotBrain
{
    public const int FeatureCount = 10;

    private enum BotAction
    {
        Gather,
        Milk,
        Sell,
        Build,
        Tame,
        AssignHouse,
        Breed,
        Helper
    }

    private readonly Game _game;
    private readonly Entity _player;
    private readonly Guid _userId;
    private readonly Random _random;
    private readonly Dictionary<BotAction, float[]> _weights = new();
    private readonly float _alpha;
    private readonly float _gamma;
    private readonly bool _learningEnabled;
    private float[] _previousFeatures;
    private BotAction? _previousAction;
    private Snapshot _previousSnapshot;
    private int _cooldownTicks;

    public bool WantsToInteract { get; private set; }
    public Entity CurrentTarget { get; private set; }
    public Vector2 DesiredDirection { get; private set; }
    public int DesiredSpeed { get; private set; } = 15;
    public Entity Player => _player;
    public Guid UserId => _userId;

    public SetMoveDirectionAction CreateMoveAction() => new()
    {
        Direction = DesiredDirection,
        Speed = DesiredSpeed
    };

    public MlBotBrain(
        Game game,
        Entity player,
        Guid userId,
        int seed = 12345,
        float alpha = 0.03f,
        float gamma = 0.92f,
        IReadOnlyDictionary<string, float[]> initialWeights = null,
        bool learningEnabled = true)
    {
        _game = game;
        _player = player;
        _userId = userId;
        _random = new Random(seed);
        _alpha = alpha;
        _gamma = gamma;
        _learningEnabled = learningEnabled;

        foreach (BotAction action in Enum.GetValues<BotAction>())
        {
            string key = action.ToString();
            _weights[action] = initialWeights != null && initialWeights.TryGetValue(key, out var weights)
                ? NormalizeWeights(weights)
                : SeedWeights(action);
        }

        _previousSnapshot = Snapshot.Capture(_game);
    }

    public Dictionary<string, float[]> ExportWeights()
    {
        return _weights.ToDictionary(
            kv => kv.Key.ToString(),
            kv => kv.Value.ToArray());
    }

    public void PreTick(int tick)
    {
        WantsToInteract = false;
        CurrentTarget = Entity.Null;
        DesiredDirection = Vector2.Zero;

        if (!_game.State.HasComponent<PlayerStateComponent>(_player)) return;
        if (!_game.State.HasComponent<StateComponent>(_player)) return;

        if (HandleActiveState()) return;
        if (_cooldownTicks-- > 0) return;

        var snapshot = Snapshot.Capture(_game);
        var features = BuildFeatures(snapshot);
        Learn(snapshot, features);

        var candidates = GetCandidates().ToList();
        if (candidates.Count == 0) return;

        var epsilon = Math.Max(0.03f, 0.25f - tick / (60f * 60f * 20f));
        Candidate choice = _random.NextDouble() < epsilon
            ? candidates[_random.Next(candidates.Count)]
            : candidates.OrderByDescending(c => Score(c.Action, features) + c.PriorityBias).First();

        _previousAction = choice.Action;
        _previousFeatures = features;
        _previousSnapshot = snapshot;

        MoveToward(choice.Target);
        WantsToInteract = true;
        CurrentTarget = choice.Target;
        _cooldownTicks = 10;
    }

    private bool HandleActiveState()
    {
        var sc = _game.State.GetComponent<StateComponent>(_player);
        if (!sc.IsEnabled) return false;

        if (sc.Phase == StatePhase.Active && (sc.Key == StateKeys.Milking || sc.Key == StateKeys.Breed))
        {
            WantsToInteract = true;
            CurrentTarget = _game.State.GetComponent<PlayerStateComponent>(_player).InteractionTarget;
            _cooldownTicks = BotConfig.ClickCooldownTicks;
        }
        return true;
    }

    private void Learn(Snapshot current, float[] features)
    {
        if (!_learningEnabled) return;
        if (_previousAction is not { } action || _previousFeatures == null) return;

        float reward = Reward(_previousSnapshot, current);
        float nextBest = Enum.GetValues<BotAction>().Max(a => Score(a, features));
        float prediction = Score(action, _previousFeatures);
        float error = reward + _gamma * nextBest - prediction;

        var weights = _weights[action];
        for (int i = 0; i < weights.Length; i++)
            weights[i] += _alpha * error * _previousFeatures[i];
    }

    private static float Reward(Snapshot before, Snapshot after)
    {
        return (after.BuiltCount - before.BuiltCount) * 1000f
            + (after.FinalBuilt - before.FinalBuilt) * 20000f
            + (after.HelperCount - before.HelperCount) * 550f
            + (after.PetCount - before.PetCount) * 750f
            + (after.CowCount - before.CowCount) * 140f
            + (after.Milk - before.Milk) * 0.08f
            + (after.Coins - before.Coins) * 0.04f
            + (after.Food - before.Food) * 0.03f
            - Math.Max(0, after.RemainingLand - before.RemainingLand) * 100f
            - 0.02f;
    }

    private IEnumerable<Candidate> GetCandidates()
    {
        var ps = _game.State.GetComponent<PlayerStateComponent>(_player);
        var resources = GetGlobalResources();

        if (ps.FollowingCow != Entity.Null)
        {
            var house = FindEmptyHouse();
            if (house != Entity.Null) yield return new Candidate(BotAction.AssignHouse, house, 200f);
        }

        var helper = FindHelperToService();
        if (helper != Entity.Null) yield return new Candidate(BotAction.Helper, helper, 60f);

        var land = FindBestLand();
        if (land != Entity.Null && resources.Coins > 0) yield return new Candidate(BotAction.Build, land, 150f);

        var sell = FindFirst<SellPointComponent>();
        if (sell != Entity.Null && resources.HasAnyMilkProduct()) yield return new Candidate(BotAction.Sell, sell, 50f);

        var milk = FindMilkableHouse(resources);
        if (milk != Entity.Null) yield return new Candidate(BotAction.Milk, milk, 80f);

        var food = FindNearestFood();
        if (food != Entity.Null) yield return new Candidate(BotAction.Gather, food, 20f);

        var wild = FindWildCow();
        if (wild != Entity.Null && Count<HouseComponent>() > Count<CowComponent>())
            yield return new Candidate(BotAction.Tame, wild, 90f);

        var loveHouse = FindBreedableLoveHouse();
        if (loveHouse != Entity.Null && Count<HouseComponent>() > Count<CowComponent>())
            yield return new Candidate(BotAction.Breed, loveHouse, 120f);
    }

    private void MoveToward(Entity target)
    {
        if (!_game.State.HasComponent<Transform2D>(_player) || !_game.State.HasComponent<Transform2D>(target))
            return;

        var playerTransform = _game.State.GetComponent<Transform2D>(_player);
        var targetPos = _game.State.GetComponent<Transform2D>(target).Position;
        var delta = targetPos - playerTransform.Position;
        DesiredDirection = delta.SqrMagnitude > (Float)0.001f ? delta.Normalized : Vector2.Zero;

        ref var mutablePlayerTransform = ref _game.State.GetComponent<Transform2D>(_player);
        mutablePlayerTransform.Position = targetPos + new Vector2(1, 0);

        if (_game.State.HasComponent<CharacterBody2D>(_player))
        {
            ref var body = ref _game.State.GetComponent<CharacterBody2D>(_player);
            body.Velocity = DesiredDirection * DesiredSpeed;
        }
    }

    private float[] BuildFeatures(Snapshot s)
    {
        return new[]
        {
            1f,
            Math.Min(s.Coins / 500f, 5f),
            Math.Min(s.Milk / 300f, 5f),
            Math.Min(s.Food / 120f, 5f),
            Math.Min(s.CowCount / 20f, 3f),
            Math.Min(s.HelperCount / 6f, 2f),
            Math.Min(s.PetCount / 4f, 2f),
            Math.Min(s.BuiltCount / 45f, 2f),
            Math.Min(s.RemainingLand / 45f, 2f),
            s.FinalBuilt > 0 ? 1f : 0f
        };
    }

    private float Score(BotAction action, float[] features)
    {
        var weights = _weights[action];
        float sum = 0f;
        for (int i = 0; i < weights.Length; i++)
            sum += weights[i] * features[i];
        return sum;
    }

    private static float[] SeedWeights(BotAction action)
    {
        var weights = new float[FeatureCount];
        weights[0] = action switch
        {
            BotAction.Build => 4f,
            BotAction.Helper => 3f,
            BotAction.Milk => 2f,
            BotAction.Sell => 2f,
            BotAction.Gather => 1.5f,
            BotAction.Tame => 1.4f,
            BotAction.AssignHouse => 5f,
            BotAction.Breed => 1.2f,
            _ => 1f
        };
        weights[1] = action == BotAction.Build || action == BotAction.Helper ? 1.5f : 0f;
        weights[2] = action == BotAction.Sell ? 1.2f : 0f;
        weights[3] = action == BotAction.Milk ? 1.4f : action == BotAction.Gather ? -0.4f : 0f;
        weights[4] = action == BotAction.Milk || action == BotAction.Breed ? 0.8f : 0f;
        weights[8] = action == BotAction.Build ? 2f : 0f;
        return weights;
    }

    private static float[] NormalizeWeights(float[] weights)
    {
        var normalized = new float[FeatureCount];
        if (weights == null) return normalized;

        int count = Math.Min(weights.Length, normalized.Length);
        Array.Copy(weights, normalized, count);
        return normalized;
    }

    private Entity FindMilkableHouse(GlobalResourcesComponent resources)
    {
        foreach (var houseEntity in _game.State.Filter<HouseComponent>())
        {
            ref var house = ref _game.State.GetComponent<HouseComponent>(houseEntity);
            if (house.CowId == Entity.Null || !_game.State.HasComponent<CowComponent>(house.CowId)) continue;

            var cow = _game.State.GetComponent<CowComponent>(house.CowId);
            if (cow.IsDepressed || cow.IsMilking || cow.Exhaust >= cow.MaxExhaust) continue;
            int foodToUse = resources.FindBestFoodForCow(cow.PreferredFood);
            if (foodToUse < 0) continue;

            house.SelectedFood = foodToUse;
            return houseEntity;
        }
        return Entity.Null;
    }

    private Entity FindBestLand()
    {
        Entity best = Entity.Null;
        int bestScore = int.MaxValue;
        foreach (var e in _game.State.Filter<LandComponent>())
        {
            var land = _game.State.GetComponent<LandComponent>(e);
            if (land.Locked != 0) continue;
            int remaining = land.Threshold - land.CurrentCoins;
            if (remaining <= 0) continue;

            int score = remaining;
            if (land.Type == LandType.HelperAssistant
                || land.Type == LandType.UpgradeGatherer
                || land.Type == LandType.UpgradeBuilder
                || land.Type == LandType.UpgradeSeller
                || land.Type == LandType.UpgradeAssistant)
                score /= 4;
            if (land.Type == LandType.FinalStructure)
                score /= 10;

            if (score < bestScore)
            {
                bestScore = score;
                best = e;
            }
        }
        return best;
    }

    private Entity FindHelperToService()
    {
        foreach (var e in _game.State.Filter<HelperComponent>())
        {
            var helper = _game.State.GetComponent<HelperComponent>(e);
            if (helper.OwnerPlayer != _player) continue;
            if (helper.State == HelperState.WaitingForPickup) return e;
            if (helper.Type == HelperType.Seller && helper.State == HelperState.Idle && GetGlobalResources().HasAnyMilkProduct()) return e;
            if (helper.Type == HelperType.Builder && helper.State == HelperState.Idle && GetGlobalResources().Coins > 0) return e;
            if (helper.Type == HelperType.Milker && helper.State == HelperState.Idle && helper.WantedFoodType >= 0) return e;
        }
        return Entity.Null;
    }

    private Entity FindNearestFood()
    {
        Entity best = Entity.Null;
        Float bestDist = 999999f;
        var playerPos = _game.State.HasComponent<Transform2D>(_player)
            ? _game.State.GetComponent<Transform2D>(_player).Position
            : Vector2.Zero;

        foreach (var e in _game.State.Filter<GrassComponent>())
        {
            if (!_game.State.HasComponent<Transform2D>(e)) continue;
            var dist = Vector2.DistanceSquared(playerPos, _game.State.GetComponent<Transform2D>(e).Position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = e;
            }
        }
        return best;
    }

    private Entity FindWildCow()
    {
        foreach (var e in _game.State.Filter<CowComponent>())
        {
            var cow = _game.State.GetComponent<CowComponent>(e);
            if (cow.HouseId == Entity.Null && cow.FollowingPlayer == Entity.Null)
                return e;
        }
        return Entity.Null;
    }

    private Entity FindEmptyHouse()
    {
        foreach (var e in _game.State.Filter<HouseComponent>())
        {
            if (_game.State.GetComponent<HouseComponent>(e).CowId == Entity.Null)
                return e;
        }
        return Entity.Null;
    }

    private Entity FindBreedableLoveHouse()
    {
        foreach (var e in _game.State.Filter<LoveHouseComponent>())
        {
            var loveHouse = _game.State.GetComponent<LoveHouseComponent>(e);
            if (loveHouse.CowId1 != Entity.Null && loveHouse.CowId2 != Entity.Null && loveHouse.CooldownTicksRemaining <= 0)
                return e;
        }
        return Entity.Null;
    }

    private Entity FindFirst<T>() where T : unmanaged, IComponent
    {
        foreach (var e in _game.State.Filter<T>()) return e;
        return Entity.Null;
    }

    private int Count<T>() where T : unmanaged, IComponent
    {
        int count = 0;
        foreach (var _ in _game.State.Filter<T>()) count++;
        return count;
    }

    private GlobalResourcesComponent GetGlobalResources()
    {
        foreach (var e in _game.State.Filter<GlobalResourcesComponent>())
            return _game.State.GetComponent<GlobalResourcesComponent>(e);
        return default;
    }

    private readonly record struct Candidate(BotAction Action, Entity Target, float PriorityBias);

    private readonly record struct Snapshot(
        int Coins,
        int Milk,
        int Food,
        int CowCount,
        int HelperCount,
        int PetCount,
        int BuiltCount,
        int RemainingLand,
        int FinalBuilt)
    {
        public static Snapshot Capture(Game game)
        {
            var res = default(GlobalResourcesComponent);
            foreach (var e in game.State.Filter<GlobalResourcesComponent>())
            {
                res = game.State.GetComponent<GlobalResourcesComponent>(e);
                break;
            }

            int built = 0;
            built += CountBuilt<HouseComponent>(game);
            built += CountBuilt<LoveHouseComponent>(game);
            built += CountBuilt<SellPointComponent>(game);
            built += CountBuilt<CarrotFarmComponent>(game);
            built += CountBuilt<AppleOrchardComponent>(game);
            built += CountBuilt<MushroomCaveComponent>(game);
            built += CountBuilt<HelperAssistantComponent>(game);
            built += CountBuilt<UpgradeGathererComponent>(game);
            built += CountBuilt<UpgradeBuilderComponent>(game);
            built += CountBuilt<UpgradeSellerComponent>(game);
            built += CountBuilt<UpgradeAssistantComponent>(game);
            built += CountBuilt<WarehouseComponent>(game);
            built += CountBuilt<FinalStructureComponent>(game);

            int remainingLand = 0;
            foreach (var e in game.State.Filter<LandComponent>())
            {
                var land = game.State.GetComponent<LandComponent>(e);
                if (land.Locked == 0 && land.CurrentCoins < land.Threshold)
                    remainingLand++;
            }

            return new Snapshot(
                res.Coins,
                res.Milk,
                res.Grass + res.Carrot + res.Apple + res.Mushroom,
                CountBuilt<CowComponent>(game),
                CountBuilt<HelperComponent>(game),
                CountBuilt<HelperPetComponent>(game),
                built,
                remainingLand,
                CountBuilt<FinalStructureComponent>(game));
        }

        private static int CountBuilt<T>(Game game) where T : unmanaged, IComponent
        {
            int count = 0;
            foreach (var _ in game.State.Filter<T>()) count++;
            return count;
        }
    }
}
