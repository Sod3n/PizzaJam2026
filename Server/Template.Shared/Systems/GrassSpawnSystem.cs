using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Navigation2D.Systems;

namespace Template.Shared.Systems;

public class GrassSpawnSystem : ISystem
{
    private const int MaxFoodPerType = 10;
    private const int SpawnInterval = 600; // 10 seconds @ 60 ticks (x4 slower)

    // Spawn Area (centered at origin, matching StarGrid bounds)
    private readonly Vector2 _minPos = new Vector2(-30, -30);
    private readonly Vector2 _maxPos = new Vector2(30, 30);

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

        // Each farm spawns 1 food per interval (2 farms = 2x spawn rate)
        for (int i = 0; i < carrotFarms && carrotCount + i < MaxFoodPerType; i++)
            SpawnFood(context, baseSeed + 1000u + (uint)i * 100, FoodType.Carrot);

        for (int i = 0; i < appleFarms && appleCount + i < MaxFoodPerType; i++)
            SpawnFood(context, baseSeed + 2000u + (uint)i * 100, FoodType.Apple);

        for (int i = 0; i < mushroomFarms && mushroomCount + i < MaxFoodPerType; i++)
            SpawnFood(context, baseSeed + 3000u + (uint)i * 100, FoodType.Mushroom);
    }

    private const int MaxSpawnAttempts = 10;

    private void SpawnFood(Context context, uint seed, int foodType)
    {
        var navState = context.State.GetCustomData<NavigationState>();
        var random = new DeterministicRandom(seed);

        for (int attempt = 0; attempt < MaxSpawnAttempts; attempt++)
        {
            var x = random.NextInt((int)_minPos.X, (int)_maxPos.X);
            var y = random.NextInt((int)_minPos.Y, (int)_maxPos.Y);
            var pos = new Vector2(x, y);

            // Only spawn on walkable nav mesh (clear of buildings/obstacles)
            if (navState?.Map != null && navState.PhysicsBaked)
            {
                if (navState.Map.FindTriangle(pos) < 0)
                    continue; // position is inside an obstacle, try again
            }

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
}
