using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.DAR;
using Template.Shared.Components;
using Template.Shared.Definitions;
using System.Runtime.InteropServices;

namespace Template.Shared.Systems;

/// <summary>Tag component so we can count existing debug obstacles.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("e7a1b2c3-d4e5-f6a7-b8c9-d0e1f2a3b4c5")]
public struct DebugObstacleTag : IComponent { }

/// <summary>
/// DEBUG: spawns cows periodically to test cow creation determinism.
/// Fully deterministic — no mutable instance state, rollback-safe.
/// Remove after desync investigation.
/// </summary>
public class DebugObstacleSpawnSystem : ISystem
{
    private const int SpawnInterval = 300; // every 5 seconds @ 60 ticks
    private const int MaxCows = 10;

    public void Update(EntityWorld state)
    {
        var gameTime = state.GetCustomData<IGameTime>();
        if (gameTime == null) return;
        if (gameTime.CurrentTick % SpawnInterval != 0) return;

        // Derive index from tick — no instance state
        int index = (int)(gameTime.CurrentTick / SpawnInterval) - 1;
        if (index < 0 || index >= MaxCows) return;

        // Count existing cows to avoid double-spawn on prediction replay
        int existing = 0;
        state.ForEach((Entity e, ref CowComponent cow) => { existing++; });
        if (existing > index) return;

        // Deterministic position: spiral outward from center
        Float angle = (Float)index * (Float)0.7f;
        Float radius = (Float)3 + (Float)index * (Float)1.5f;
        var x = radius * Float.Cos(angle);
        var y = radius * Float.Sin(angle);

        var ctx = new Context(state, Entity.Null, null!);
        var cow = CowDefinition.Create(ctx, new Vector2(x, y));
        state.GetComponent<CowComponent>(cow).PreferredFood = FoodType.Grass;
    }
}
