using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;

namespace Template.Shared.Tests;

public class LightSimRunner
{
    private readonly Game _game;
    private readonly ISystem[] _systems;

    public LightSimRunner(Game game)
    {
        _game = game;
        // Game-logic systems + mock navigation (no real physics/navmesh).
        _systems = new ISystem[]
        {
            new MockNavigationSystem(),
            new Template.Shared.Systems.AnimationsSystem(),
            new Template.Shared.Systems.CowSystem(),
            new Template.Shared.Systems.HelperSystem(),
            new Template.Shared.Systems.GrassSpawnSystem(),
            new Template.Shared.Systems.CoinCollectionSystem(),
        };
    }

    /// <summary>Advance one tick: increment time, run game systems only.</summary>
    public void Tick()
    {
        var sim = _game.Loop.Simulation;
        sim.ForceSetTick(sim.CurrentTick + 1);
        foreach (var sys in _systems)
            sys.Update(_game.State);
    }

    /// <summary>Run game systems without advancing time (for post-dispatch processing).</summary>
    public void RunSystems()
    {
        foreach (var sys in _systems)
            sys.Update(_game.State);
    }
}
