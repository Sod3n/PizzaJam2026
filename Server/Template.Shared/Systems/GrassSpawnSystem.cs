using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.Navigation2D.Systems;

namespace Template.Shared.Systems;

public class GrassSpawnSystem : ISystem
{
    private const int MaxFoodPerType = 10;
    private const int SpawnInterval = 600; // 10 seconds @ 60 ticks (x4 slower)

    // Spawn Area (centered at origin, matching StarGrid bounds)
    private readonly Vector2 _minPos = new Vector2(-30, -30);
    private readonly Vector2 _maxPos = new Vector2(30, 30);

    /// <summary>
    /// Minimum clearance distance from any building center.
    /// Buildings use collision shapes up to ~4.6 wide (CarrotFarm),
    /// so half-diagonal + margin keeps food visually outside.
    /// </summary>
    private const float BuildingClearance = 3f;

    public void Update(EntityWorld state)
    {
        var gameTime = state.GetCustomData<IGameTime>();
        if (gameTime == null || gameTime.CurrentTick % SpawnInterval != 0) return;

        // Count existing food per type
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

        // Count farms per food type — each farm adds one spawn per interval
        int carrotFarms = 0, appleFarms = 0, mushroomFarms = 0;
        foreach (var _ in state.Filter<CarrotFarmComponent>()) carrotFarms++;
        foreach (var _ in state.Filter<AppleOrchardComponent>()) appleFarms++;
        foreach (var _ in state.Filter<MushroomCaveComponent>()) mushroomFarms++;

        var context = new Context(state, Entity.Null, null!);
        uint baseSeed = (uint)state.NextEntityId + ((uint)gameTime.CurrentTick);

        // Always spawn 1 grass per interval
        if (grassCount < MaxFoodPerType)
            SpawnFood(context, baseSeed, FoodType.Grass);

        // Non-grass resources spawn at half rate: only every other interval
        if (gameTime.CurrentTick % (SpawnInterval * 2) == 0)
        {
            for (int i = 0; i < carrotFarms && carrotCount + i < MaxFoodPerType; i++)
                SpawnFood(context, baseSeed + 1000u + (uint)i * 100, FoodType.Carrot);

            for (int i = 0; i < appleFarms && appleCount + i < MaxFoodPerType; i++)
                SpawnFood(context, baseSeed + 2000u + (uint)i * 100, FoodType.Apple);

            for (int i = 0; i < mushroomFarms && mushroomCount + i < MaxFoodPerType; i++)
                SpawnFood(context, baseSeed + 3000u + (uint)i * 100, FoodType.Mushroom);
        }
    }

    private const int MaxSpawnAttempts = 10;

    private void SpawnFood(Context context, uint seed, int foodType)
    {
        // Read bake state from NavigationWorld2D (ECS component — survives rollback/sync).
        // Read the CDT map from CDTNavigationState (derived cache — rebuilt after restore).
        var navState = context.State.GetCustomData<CDTNavigationState>();
        bool physicsBaked = false;
        foreach (var navEntity in context.State.Filter<NavigationWorld2D>())
        {
            physicsBaked = context.State.GetComponent<NavigationWorld2D>(navEntity).PhysicsBaked;
            break;
        }
        var random = new DeterministicRandom(seed);

        for (int attempt = 0; attempt < MaxSpawnAttempts; attempt++)
        {
            var x = random.NextInt((int)_minPos.X, (int)_maxPos.X);
            var y = random.NextInt((int)_minPos.Y, (int)_maxPos.Y);
            var pos = new Vector2(x, y);

            // Check 1: Only spawn on walkable CDT nav mesh (clear of obstacles)
            if (navState?.Map != null && physicsBaked)
            {
                if (navState.Map.FindTriangle(pos) < 0)
                    continue; // position is inside an obstacle, try again
            }

            // Check 2: Explicit building proximity check as safety net.
            // Even if the nav mesh is not yet baked, this prevents spawning
            // on or near any building (StaticBody2D entities on the Physics layer).
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
            return;
        }
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

            float dx = (float)(pos.X - buildingPos.X);
            float dy = (float)(pos.Y - buildingPos.Y);

            if (shape.Type == CollisionShapeType.Rectangle)
            {
                // AABB check with clearance margin
                float halfW = (float)shape.Rectangle.Size.X / 2f + BuildingClearance;
                float halfH = (float)shape.Rectangle.Size.Y / 2f + BuildingClearance;
                if (System.Math.Abs(dx) < halfW && System.Math.Abs(dy) < halfH)
                    return true;
            }
            else if (shape.Type == CollisionShapeType.Circle)
            {
                // Circle check with clearance margin
                float radius = (float)shape.Circle.Radius + BuildingClearance;
                if (dx * dx + dy * dy < radius * radius)
                    return true;
            }
        }
        return false;
    }
}
