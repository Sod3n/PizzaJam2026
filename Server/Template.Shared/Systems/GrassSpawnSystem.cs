using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Deterministic.GameFramework.DAR;

namespace Template.Shared.Systems;

public class GrassSpawnSystem : ISystem
{
    private const int MaxFoodPerType = 30;
    private const int SpawnInterval = 60; // 1 second @ 60 ticks

    // Spawn Area (roughly matching the walls in GameplayScene)
    private readonly Vector2 _minPos = new Vector2(0, 0);
    private readonly Vector2 _maxPos = new Vector2(55, 55);

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

        // Check which food farm types exist
        bool hasCarrotFarm = false, hasAppleOrchard = false, hasMushroomCave = false;
        foreach (var entity in state.Filter<FoodFarmComponent>())
        {
            var farm = state.GetComponent<FoodFarmComponent>(entity);
            switch (farm.FoodType)
            {
                case FoodType.Carrot: hasCarrotFarm = true; break;
                case FoodType.Apple: hasAppleOrchard = true; break;
                case FoodType.Mushroom: hasMushroomCave = true; break;
            }
        }

        var context = new Context(state, Entity.Null, null!);
        uint baseSeed = (uint)state.NextEntityId + ((uint)gameTime.CurrentTick);

        // Always spawn grass
        if (grassCount < MaxFoodPerType)
            SpawnFood(context, baseSeed, FoodType.Grass);

        // Spawn other foods only if their farm exists
        if (hasCarrotFarm && carrotCount < MaxFoodPerType)
            SpawnFood(context, baseSeed + 1000, FoodType.Carrot);

        if (hasAppleOrchard && appleCount < MaxFoodPerType)
            SpawnFood(context, baseSeed + 2000, FoodType.Apple);

        if (hasMushroomCave && mushroomCount < MaxFoodPerType)
            SpawnFood(context, baseSeed + 3000, FoodType.Mushroom);
    }

    private void SpawnFood(Context context, uint seed, int foodType)
    {
        var random = new DeterministicRandom(seed);
        var x = random.NextInt((int)_minPos.X, (int)_maxPos.X);
        var y = random.NextInt((int)_minPos.Y, (int)_maxPos.Y);

        var entity = GrassDefinition.Create(context, new Vector2(x, y));
        ref var food = ref context.GetComponent<GrassComponent>(entity);
        food.FoodType = foodType;
    }
}
