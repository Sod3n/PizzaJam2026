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
        
        // Create Top-Down Walls (Enclosed Arena 2000x2000)
        CreateWall(state, new Vector2(0, -1000), new Vector2(2200, 100)); // Top
        CreateWall(state, new Vector2(0, 1000), new Vector2(2200, 100));  // Bottom
        CreateWall(state, new Vector2(-1000, 0), new Vector2(100, 2000)); // Left
        CreateWall(state, new Vector2(1000, 0), new Vector2(100, 2000));  // Right

        // Spawn some coins
        var context = new Context(state, Entity.Null, null!);
        for (int i = 0; i < 10; i++)
        {
            var x = (i % 5) * 200 - 400;
            var y = (i / 5) * 200 - 200;
            CoinDefinition.Create(context, new Vector2(x, y), 10);
        }
    }

    private void CreateWall(EntityWorld state, Vector2 position, Vector2 size)
    {
        var wall = state.CreateEntity();
        state.AddComponent(wall, new SceneTag());
        state.AddComponent(wall, new Transform2D(position, 0, Vector2.One));
        state.AddComponent(wall, new StaticBody2D());
        state.AddComponent(wall, CollisionShape2D.CreateRectangle(size));
    }

    public void OnExit(GameSimulation loop)
    {
        Console.WriteLine("[GameplayScene] Exiting scene...");
    }
}
