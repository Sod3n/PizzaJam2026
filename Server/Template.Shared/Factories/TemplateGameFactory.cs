using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Physics2D.Systems;
using Template.Shared.Scenes;

namespace Template.Shared.Factories;

public static class TemplateGameFactory
{
    private static bool _appInitialized = false;

    public static Game CreateGame(int tickRate = 60, string? gameDataPath = null, System.Collections.Generic.Dictionary<string, string>? gameDataJson = null)
    {
        if (!_appInitialized)
        {
            _appInitialized = true;
            ServiceLocator.RegisterAssembly(typeof(EntityWorld).Assembly); // Deterministic.GameFramework.ECS
            ServiceLocator.RegisterAssembly(typeof(Game).Assembly); // Deterministic.GameFramework.Common
            ServiceLocator.RegisterAssembly(typeof(Deterministic.GameFramework.DAR.Dispatcher).Assembly); // Deterministic.GameFramework.DAR
            ServiceLocator.RegisterAssembly(typeof(Deterministic.GameFramework.TwoD.Transform2D).Assembly); // Deterministic.GameFramework.TwoD
            ServiceLocator.RegisterAssembly(typeof(RapierPhysicsSystem).Assembly); // Deterministic.GameFramework.TwoD.Physics
            ServiceLocator.RegisterAssembly(typeof(Deterministic.GameFramework.Navigation2D.Systems.NavigationSystem).Assembly); // Deterministic.GameFramework.TwoD.Navigation
            ServiceLocator.RegisterAssembly(typeof(TemplateGameFactory).Assembly); // Template.Shared

            // Initialize GameData
            if (gameDataJson != null)
            {
                Template.Shared.GameData.GD.LoadFromJson(gameDataJson);
            }
            else
            {
                Template.Shared.GameData.GD.Load(gameDataPath);
            }
        }

        // 1. Create Game (encapsulates State, Loop, Dispatcher, Scheduler, SceneManager)
        var game = new Game(tickRate: tickRate);

        // 2. Register physics system (Box2D = deterministic Fixed64, Rapier = float FFI)
        game.Loop.Simulation.SystemRunner.EnableSystem(new Box2DPhysicsSystem());

        // 3. Load Initial Scene
        game.SceneManager.LoadScene(new GameplayScene());

        return game;
    }
}
