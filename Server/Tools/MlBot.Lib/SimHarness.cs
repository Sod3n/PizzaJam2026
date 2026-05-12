using System;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Actions;
using Template.Shared.Components;

namespace MlBot.Lib;

public static class SimHarness
{
    public static Guid NewSeededGuid(int seed)
    {
        var bytes = new byte[16];
        new Random(seed).NextBytes(bytes);
        return new Guid(bytes);
    }

    public static Entity AddBotPlayer(Game game, Guid userId)
    {
        Entity worldEntity = Entity.Null;
        foreach (var e in game.State.Filter<World>()) { worldEntity = e; break; }
        if (worldEntity == Entity.Null) return Entity.Null;

        game.State.AddComponent(worldEntity, new AddPlayerAction(userId));
        game.Dispatcher.Update(game.State);
        game.Loop.Simulation.SystemRunner.Update(game.State);

        foreach (var e in game.State.Filter<PlayerEntity>())
        {
            if (game.State.GetComponent<PlayerEntity>(e).UserId == userId) return e;
        }
        return Entity.Null;
    }

    public static void InjectOverlap(Game game, Entity player, Entity target)
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

    public static void MockNavigation(Game game)
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
            float dist = MathF.Sqrt(dx * dx + dy * dy);
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
            float nx = dx / dist;
            float ny = dy / dist;

            if (game.State.HasComponent<CharacterBody2D>(entity))
            {
                ref var body = ref game.State.GetComponent<CharacterBody2D>(entity);
                body.Velocity = new Vector2(nx * speed, ny * speed);
            }
            else
            {
                float move = MathF.Min(speed * dt, dist);
                transform.Position = new Vector2(
                    (float)current.X + nx * move,
                    (float)current.Y + ny * move);
            }

            nav.Velocity = new Vector2(nx * speed, ny * speed);
            nav.DistanceToTarget = (Float)dist;
        }
    }

    public readonly record struct Snapshot(
        int Coins, int Milk, int Food, int Cows, int Helpers, int Pets,
        int Built, int FinalBuilt, int RemainingLand);

    public static Snapshot Capture(Game game)
    {
        var res = default(GlobalResourcesComponent);
        foreach (var e in game.State.Filter<GlobalResourcesComponent>())
        { res = game.State.GetComponent<GlobalResourcesComponent>(e); break; }

        int cows = Count<CowComponent>(game);
        int helpers = Count<HelperComponent>(game);
        int pets = Count<HelperPetComponent>(game);
        int built = Count<HouseComponent>(game)
                  + Count<LoveHouseComponent>(game)
                  + Count<SellPointComponent>(game)
                  + Count<CarrotFarmComponent>(game)
                  + Count<AppleOrchardComponent>(game)
                  + Count<MushroomCaveComponent>(game)
                  + Count<HelperAssistantComponent>(game)
                  + Count<WarehouseComponent>(game)
                  + Count<FinalStructureComponent>(game);
        int finalBuilt = Count<FinalStructureComponent>(game);

        int remaining = 0;
        foreach (var e in game.State.Filter<LandComponent>())
        {
            var land = game.State.GetComponent<LandComponent>(e);
            if (land.Locked == 0 && land.CurrentCoins < land.Threshold) remaining++;
        }

        return new Snapshot(
            res.Coins,
            res.Milk + res.CarrotMilkshake + res.VitaminMix + res.PurplePotion,
            res.Grass + res.Carrot + res.Apple + res.Mushroom,
            cows, helpers, pets, built, finalBuilt, remaining);
    }

    public static Deltas ComputeDeltas(Snapshot prev, Snapshot cur, int ticks) => new()
    {
        Coins = cur.Coins - prev.Coins,
        Milk = cur.Milk - prev.Milk,
        Food = cur.Food - prev.Food,
        Built = cur.Built - prev.Built,
        FinalBuilt = cur.FinalBuilt - prev.FinalBuilt,
        Helpers = cur.Helpers - prev.Helpers,
        Cows = cur.Cows - prev.Cows,
        Pets = cur.Pets - prev.Pets,
        LandLost = Math.Max(0, cur.RemainingLand - prev.RemainingLand),
        TicksElapsed = ticks,
    };

    private static int Count<T>(Game game) where T : unmanaged, IComponent
    {
        int count = 0;
        foreach (var _ in game.State.Filter<T>()) count++;
        return count;
    }
}
