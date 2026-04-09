using System.Collections.Generic;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Physics2D.Systems;
using Template.Shared.Actions;
using Template.Shared.Definitions;
using Template.Shared.Debugging;
using Template.Shared.Components;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.Navigation2D.Systems;

namespace Template.Shared.Scenes;

public class GameplayScene : IScene
{
    // Use CDT navigation instead of grid-based — filter out the old NavigationSystem
    public IEnumerable<ISystem> RegisterSystems(GameSimulation loop)
    {
        // DIAGNOSTIC: disable ALL systems to isolate desync to action/prediction layer
        yield break;
        foreach (var system in ServiceLocator.GetAll<ISystem>())
        {
            if (system is NavigationSystem)
                continue;
            yield return system;
        }
    }
    public IEnumerable<IActionService> RegisterActionServices(GameSimulation loop) => ServiceLocator.GetAll<IActionService>();
    public IEnumerable<IReactionService> RegisterReactionServices(GameSimulation loop) => ServiceLocator.GetAll<IReactionService>();
    public void OnEnter(GameSimulation loop)
    {
        Console.WriteLine("[GameplayScene] Entering scene...");

        var state = loop.State;

        Float center = (Float)0;
        Float halfSize = (Float)(StarGrid.OuterRadius + StarGrid.GridStep); // Ensure bounds cover farthest grid position
        Float wallThickness = (Float)1;

        // Top Wall (Horizontal)
        // Position: (X = CenterX, Y = CenterY - HalfSize)
        CreateWall(state, new Vector2(center, center - halfSize), new Vector2(halfSize * 2, wallThickness));

        // Bottom Wall (Horizontal)
        // Position: (X = CenterX, Y = CenterY + HalfSize)
        CreateWall(state, new Vector2(center, center + halfSize), new Vector2(halfSize * 2, wallThickness));

        // Left Wall (Vertical)
        // Position: (X = CenterX - HalfSize, Y = CenterY)
        CreateWall(state, new Vector2(center - halfSize, center), new Vector2(wallThickness, halfSize * 2));

        // Right Wall (Vertical)
        // Position: (X = CenterX + halfSize, Y = CenterY)
        CreateWall(state, new Vector2(center + halfSize, center), new Vector2(wallThickness, halfSize * 2));

        // Spawn some coins
        var context = new Context(state, Entity.Null, null!);

        // DEBUG: Spawn all skins in a line
        // SkinDebugSpawner.SpawnAllSkinsInLine(context, new Vector2(0, -5), 2);

        // Navigation world - auto-bakes nav mesh from physics obstacles
        var navWorld = state.CreateEntity();
        var navWorldComp = NavigationWorld2D.Default;
        navWorldComp.BoundsMin = new Vector2(center - halfSize, center - halfSize);
        navWorldComp.BoundsMax = new Vector2(center + halfSize, center + halfSize);
        navWorldComp.CellSize = 2.0f;
        navWorldComp.AgentRadius = 0.5f;
        navWorldComp.ChunkSize = 20f;
        
        navWorldComp.ObstacleMask = (uint)CollisionLayer.Physics;
        state.AddComponent(navWorld, navWorldComp);

        // Initialize Global Resources
        var globalRes = state.CreateEntity();
        state.AddComponent(globalRes, new GlobalResourcesComponent { Grass = 0, Milk = 0, Coins = 50, HelpersEnabled = 1 }); // Start with some coins to buy land

        // Initialize Metrics tracking
        var metricsEntity = state.CreateEntity();
        state.AddComponent(metricsEntity, new MetricsComponent());

        // Initialize skin spawn counts (deterministic weight decay, stored in ECS state)
        var skinSpawnEntity = state.CreateEntity();
        state.AddComponent(skinSpawnEntity, new SkinSpawnCountsComponent());

        // Single starting land plot at center — builds into a sell point
        // Buying it spawns 4 neighbors, which spawn their neighbors, etc.
        StarGrid.TrySpawnLand(context, 0, 0);

        // Spawn 2 initial cows: both Grass (must breed up to unlock higher tiers)
        var cow1 = CowDefinition.Create(context, new Vector2(2, 2));
        state.GetComponent<CowComponent>(cow1).PreferredFood = FoodType.Grass;

        var cow2 = CowDefinition.Create(context, new Vector2(-2, 2));
        state.GetComponent<CowComponent>(cow2).PreferredFood = FoodType.Grass;
    }

    private void CreateWall(EntityWorld state, Vector2 position, Vector2 size)
    {
        Float chunkSize = 10f;

        // Determine if horizontal or vertical wall
        bool isHorizontal = size.X > size.Y;
        Float length = isHorizontal ? size.X : size.Y;
        Float thickness = isHorizontal ? size.Y : size.X;

        int chunkCount = (int)Float.Ceil(length / chunkSize);
        Float actualChunkLength = length / chunkCount;

        // Start position (left/top edge of the wall)
        Float startOffset = -length / 2 + actualChunkLength / 2;

        for (int i = 0; i < chunkCount; i++)
        {
            Float offset = startOffset + i * actualChunkLength;
            Vector2 chunkPos = isHorizontal
                ? new Vector2(position.X + offset, position.Y)
                : new Vector2(position.X, position.Y + offset);
            Vector2 chunkSize2 = isHorizontal
                ? new Vector2(actualChunkLength, thickness)
                : new Vector2(thickness, actualChunkLength);

            var wall = state.CreateEntity();
            state.AddComponent(wall, new SceneTag());
            state.AddComponent(wall, new WallComponent());
            state.AddComponent(wall, new Transform2D(chunkPos, 0, Vector2.One));
            state.AddComponent(wall, new StaticBody2D());
            state.AddComponent(wall, CollisionShape2D.CreateRectangle(chunkSize2));
        }
    }

    public void OnExit(GameSimulation loop)
    {
        Console.WriteLine("[GameplayScene] Exiting scene...");
    }
}
