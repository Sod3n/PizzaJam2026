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

namespace Template.Shared.Scenes;

public class GameplayScene : IScene
{
    public IEnumerable<ISystem> RegisterSystems(GameSimulation loop) => ServiceLocator.GetAll<ISystem>();
    public IEnumerable<IActionService> RegisterActionServices(GameSimulation loop) => ServiceLocator.GetAll<IActionService>();
    public IEnumerable<IReactionService> RegisterReactionServices(GameSimulation loop) => ServiceLocator.GetAll<IReactionService>();
    public void OnEnter(GameSimulation loop)
    {
        Console.WriteLine("[GameplayScene] Entering scene...");

        var state = loop.State;

        float center = 25f;
        float halfSize = 40f; // 140 / 2
        float wallThickness = 1f;

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
        navWorldComp.CellSize = 0.5f;
        navWorldComp.AgentRadius = 0.5f;
        navWorldComp.ObstacleMask = (uint)CollisionLayer.Physics;
        state.AddComponent(navWorld, navWorldComp);

        // Initialize Global Resources
        var globalRes = state.CreateEntity();
        state.AddComponent(globalRes, new GlobalResourcesComponent { Grass = 0, Milk = 0, Coins = 5 }); // Start with some coins to buy land

        // Single starting land plot at center — builds into a sell point
        // Buying it spawns 4 neighbors, which spawn their neighbors, etc.
        StarGrid.TrySpawnLand(context, 0, 0);

        // Spawn 2 initial cows near center
        CowDefinition.Create(context, new Vector2(2, 2));
        CowDefinition.Create(context, new Vector2(-2, 2));
    }

    private void CreateWall(EntityWorld state, Vector2 position, Vector2 size)
    {
        Float chunkSize = 10f;

        // Determine if horizontal or vertical wall
        bool isHorizontal = size.X > size.Y;
        Float length = isHorizontal ? size.X : size.Y;
        Float thickness = isHorizontal ? size.Y : size.X;

        int chunkCount = (int)Math.Ceiling((float)length / (float)chunkSize);
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
