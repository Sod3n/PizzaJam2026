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
    private static int SpawnInterval => Balance.FoodSpawn.IntervalTicks;

    private readonly Vector2 _minPos = new Vector2(-30, -30);
    private readonly Vector2 _maxPos = new Vector2(30, 30);

    private static readonly Float BuildingClearance = (Float)3;

    public void Update(EntityWorld state)
    {
        var gameTime = state.GetCustomData<IGameTime>();
        if (gameTime == null || gameTime.CurrentTick % SpawnInterval != 0) return;

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

        int carrotFarms = 0, appleFarms = 0, mushroomFarms = 0;
        foreach (var _ in state.Filter<CarrotFarmComponent>()) carrotFarms++;
        foreach (var _ in state.Filter<AppleOrchardComponent>()) appleFarms++;
        foreach (var _ in state.Filter<MushroomCaveComponent>()) mushroomFarms++;

        var context = state.Ctx(Entity.Null);
        uint baseSeed = (uint)gameTime.CurrentTick * 2654435761u + (uint)grassCount * 31u + (uint)carrotCount * 37u + (uint)appleCount * 41u + (uint)mushroomCount * 43u;

        Entity grEntity = Entity.Null;
        foreach (var ge in state.Filter<GlobalResourcesComponent>()) { grEntity = ge; break; }
        if (grEntity == Entity.Null) return;

        int grassCap = SleepLogic.GetFoodCapForToday(state, FoodType.Grass);
        int grassToday = state.GetComponent<GlobalResourcesComponent>(grEntity).FoodSpawnedTodayGrass;
        if (grassToday < grassCap && grassCount < grassCap)
        {
            if (SpawnFood(context, baseSeed, FoodType.Grass))
            {
                ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
                gr.IncrementFoodSpawnedToday(FoodType.Grass);
            }
        }

        if (gameTime.CurrentTick % (SpawnInterval * 2) == 0)
        {
            int carrotCap = SleepLogic.GetFoodCapForToday(state, FoodType.Carrot);
            for (int i = 0; i < carrotFarms; i++)
            {
                int spawnedToday = state.GetComponent<GlobalResourcesComponent>(grEntity).FoodSpawnedTodayCarrot;
                if (spawnedToday >= carrotCap || carrotCount + i >= carrotCap) break;
                if (SpawnFood(context, baseSeed + 1000u + (uint)i * 100, FoodType.Carrot))
                {
                    ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
                    gr.IncrementFoodSpawnedToday(FoodType.Carrot);
                }
            }

            int appleCap = SleepLogic.GetFoodCapForToday(state, FoodType.Apple);
            for (int i = 0; i < appleFarms; i++)
            {
                int spawnedToday = state.GetComponent<GlobalResourcesComponent>(grEntity).FoodSpawnedTodayApple;
                if (spawnedToday >= appleCap || appleCount + i >= appleCap) break;
                if (SpawnFood(context, baseSeed + 2000u + (uint)i * 100, FoodType.Apple))
                {
                    ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
                    gr.IncrementFoodSpawnedToday(FoodType.Apple);
                }
            }

            int mushroomCap = SleepLogic.GetFoodCapForToday(state, FoodType.Mushroom);
            for (int i = 0; i < mushroomFarms; i++)
            {
                int spawnedToday = state.GetComponent<GlobalResourcesComponent>(grEntity).FoodSpawnedTodayMushroom;
                if (spawnedToday >= mushroomCap || mushroomCount + i >= mushroomCap) break;
                if (SpawnFood(context, baseSeed + 3000u + (uint)i * 100, FoodType.Mushroom))
                {
                    ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
                    gr.IncrementFoodSpawnedToday(FoodType.Mushroom);
                }
            }
        }
    }

    private static int MaxSpawnAttempts => Balance.FoodSpawn.MaxSpawnAttempts;

    private bool SpawnFood(Context context, uint seed, int foodType)
    {
        var random = new DeterministicRandom(seed);

        for (int attempt = 0; attempt < MaxSpawnAttempts; attempt++)
        {
            var x = random.NextInt((int)_minPos.X, (int)_maxPos.X);
            var y = random.NextInt((int)_minPos.Y, (int)_maxPos.Y);
            var pos = new Vector2(x, y);

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
