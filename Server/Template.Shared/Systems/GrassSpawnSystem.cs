using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Deterministic.GameFramework.DAR;

namespace Template.Shared.Systems;

public class GrassSpawnSystem : ISystem
{
    private const int MaxGrass = 50;
    private const int SpawnInterval = 300; // 5 seconds @ 60 ticks
    
    // Spawn Area (roughly matching the walls in GameplayScene)
    private readonly Vector2 _minPos = new Vector2(-900, -900);
    private readonly Vector2 _maxPos = new Vector2(900, 900);

    public void Update(EntityWorld state)
    {
        var gameTime = state.GetCustomData<IGameTime>();
        if (gameTime == null || gameTime.CurrentTick % SpawnInterval != 0) return;

        // Count existing grass
        int grassCount = 0;
        foreach (var _ in state.Filter<GrassComponent>())
        {
            grassCount++;
        }

        if (grassCount >= MaxGrass) return;

        // Spawn new grass
        var context = new Context(state, Entity.Null, null!);
        
        // Random position
        uint seed = (uint)state.NextEntityId + (gameTime != null ? (uint)gameTime.CurrentTick : 0);
        var random = new DeterministicRandom(seed);
        
        var x = random.NextInt((int)_minPos.X, (int)_maxPos.X);
        var y = random.NextInt((int)_minPos.Y, (int)_maxPos.Y);
        
        GrassDefinition.Create(context, new Vector2(x, y));
        
        // System.Console.WriteLine($"[GrassSystem] Spawned grass at ({x}, {y}). Total: {grassCount + 1}");
    }
}
