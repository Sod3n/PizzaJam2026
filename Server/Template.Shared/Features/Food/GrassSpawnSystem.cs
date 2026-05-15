using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.GameData;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Navigation2D.Systems;

namespace Template.Shared.Systems;

public class GrassSpawnSystem : ISystem
{
    private readonly Vector2 _minPos = new Vector2(-30, -30);
    private readonly Vector2 _maxPos = new Vector2(30, 30);

    private static readonly Float BuildingClearance = (Float)3;

    public void Update(EntityWorld state)
    {
        var gameTime = state.GetCustomData<IGameTime>();
        if (gameTime == null) return;

        Entity grEntity = Entity.Null;
        foreach (var ge in state.Filter<GlobalResourcesComponent>()) { grEntity = ge; break; }
        if (grEntity == Entity.Null) return;

        // Food only grows during the configured window at the start of the day.
        int spawnWindowEnd = (int)(Balance.Day.LengthTicks * Balance.Day.FoodSpawnFraction);
        if (state.GetComponent<GlobalResourcesComponent>(grEntity).TicksSinceDayStart >= spawnWindowEnd) return;

        int grassCount = 0, carrotCount = 0, appleCount = 0, mushroomCount = 0;
        foreach (var entity in state.Filter<GrassComponent>())
        {
            var food = state.GetComponent<GrassComponent>(entity);
            switch (food.FoodType)
            {
                case FoodType.Grass: grassCount++; break;
                case FoodType.Carrot: carrotCount++; break;
                case FoodType.Apple: appleCount++; break;
                case FoodType.Mushroom: mushroomCount++; break;
            }
        }

        var context = state.Ctx(Entity.Null);
        uint baseSeed = (uint)gameTime.CurrentTick * 2654435761u + (uint)grassCount * 31u + (uint)carrotCount * 37u + (uint)appleCount * 41u + (uint)mushroomCount * 43u;

        long tick = gameTime.CurrentTick;
        TrySpawn(state, context, grEntity, tick, baseSeed, FoodType.Grass, grassCount);
        TrySpawn(state, context, grEntity, tick, baseSeed + 1000u, FoodType.Carrot, carrotCount);
        TrySpawn(state, context, grEntity, tick, baseSeed + 2000u, FoodType.Apple, appleCount);
        TrySpawn(state, context, grEntity, tick, baseSeed + 3000u, FoodType.Mushroom, mushroomCount);
    }

    private void TrySpawn(EntityWorld state, Context context, Entity grEntity, long tick, uint seed, int foodType, int worldCount)
    {
        int cap = SleepLogic.GetFoodCapForToday(state, foodType);
        if (cap <= 0) return;

        // Evenly spread the cap's spawn attempts across the day's spawn window.
        int interval = Balance.FoodSpawn.IntervalTicksForCap(cap);
        if (tick % interval != 0) return;

        int spawnedToday = state.GetComponent<GlobalResourcesComponent>(grEntity).GetFoodSpawnedToday(foodType);
        if (spawnedToday >= cap || worldCount >= cap) return;

        if (SpawnFood(context, seed, foodType))
        {
            ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            gr.IncrementFoodSpawnedToday(foodType);
        }
    }

    private static int MaxSpawnAttempts => Balance.FoodSpawn.MaxSpawnAttempts;

    /// <summary>
    /// Anchors for food spawning — unlocked land plots (player can reach them).
    /// Buffer reused per call by the caller's stack frame; OK to allocate, this only
    /// runs at the food-spawn interval.
    /// </summary>
    private static System.Collections.Generic.List<Vector2> CollectAnchors(EntityWorld state)
    {
        var list = new System.Collections.Generic.List<Vector2>();
        foreach (var landEntity in state.Filter<LandComponent>())
        {
            var land = state.GetComponent<LandComponent>(landEntity);
            if (land.Locked != 0) continue;
            if (!state.TryGetComponent<Transform2D>(landEntity, out var t)) continue;
            list.Add(t.Position);
        }
        return list;
    }

    private bool SpawnFood(Context context, uint seed, int foodType)
    {
        var random = new DeterministicRandom(seed);
        var anchors = CollectAnchors(context.State);
        int radius = Balance.FoodSpawn.AnchorRadius;
        int minDist = Balance.FoodSpawn.AnchorMinDistance;

        for (int attempt = 0; attempt < MaxSpawnAttempts; attempt++)
        {
            Vector2 pos;
            if (anchors.Count > 0)
            {
                var anchor = anchors[random.NextInt(anchors.Count)];
                // Offset from the anchor: pick a point in an annulus [minDist, radius].
                int dx = random.NextInt(-radius, radius + 1);
                int dy = random.NextInt(-radius, radius + 1);
                if (System.Math.Abs(dx) < minDist && System.Math.Abs(dy) < minDist)
                {
                    // Snap to the ring edge if too close to the building footprint.
                    dx = dx >= 0 ? minDist : -minDist;
                }
                pos = new Vector2((int)anchor.X + dx, (int)anchor.Y + dy);
            }
            else
            {
                int x = random.NextInt((int)_minPos.X, (int)_maxPos.X);
                int y = random.NextInt((int)_minPos.Y, (int)_maxPos.Y);
                pos = new Vector2(x, y);
            }

            if (!NavigationQueries.IsWalkable(context.State, pos))
                continue;

            if (IsNearBuilding(context.State, pos))
                continue;

            var entity = foodType switch
            {
                FoodType.Carrot => CarrotDefinition.Create(context, pos),
                FoodType.Apple => AppleDefinition.Create(context, pos),
                FoodType.Mushroom => MushroomDefinition.Create(context, pos),
                _ => GrassDefinition.Create(context, pos),
            };
            ref var food = ref context.GetComponent<GrassComponent>(entity);
            food.FoodType = foodType;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the position overlaps with or is too close to any building.
    /// Checks all StaticBody2D entities on the Physics collision layer (buildings, walls,
    /// land plots, farms, etc.) against their actual collision shapes.
    /// </summary>
    private static bool IsNearBuilding(EntityWorld state, Vector2 pos)
    {
        foreach (var entity in state.Filter<StaticBody2D, Transform2D, CollisionShape2D>())
        {
            var body = state.GetComponent<StaticBody2D>(entity);
            // Only check Physics-layer bodies (buildings, walls, land) — skip Interactable-only entities
            if ((body.CollisionLayer & (uint)CollisionLayer.Physics) == 0)
                continue;

            var transform = state.GetComponent<Transform2D>(entity);
            var shape = state.GetComponent<CollisionShape2D>(entity);
            var buildingPos = transform.Position;

            var diff = pos - buildingPos;

            if (shape.Type == CollisionShapeType.Rectangle)
            {
                // AABB check with clearance margin
                Float halfW = shape.Rectangle.Size.X * (Float)0.5f + BuildingClearance;
                Float halfH = shape.Rectangle.Size.Y * (Float)0.5f + BuildingClearance;
                if (Float.Abs(diff.X) < halfW && Float.Abs(diff.Y) < halfH)
                    return true;
            }
            else if (shape.Type == CollisionShapeType.Circle)
            {
                // Circle check with clearance margin
                Float radius = shape.Circle.Radius + BuildingClearance;
                if (diff.SqrMagnitude < radius * radius)
                    return true;
            }
        }
        return false;
    }
}
