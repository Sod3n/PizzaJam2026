using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using MlBot.Lib;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Factories;
using Template.Shared.Tests;

namespace MlBotHost;

internal sealed class SimSession
{
    private Game? _game;
    private Template.Shared.Tests.MlBotBrain? _brain;
    private LightSimRunner? _runner;
    private Entity _player;
    private Guid _userId;
    private int _tick;
    private int _maxTicks;
    private SimHarness.Snapshot _prevSnapshot;

    public Response Reset(int seed, int maxTicks)
    {
        _maxTicks = maxTicks > 0 ? maxTicks : 108_000;
        _tick = 0;

        _game = TemplateGameFactory.CreateGame(tickRate: 60);
        _userId = SimHarness.NewSeededGuid(seed);
        _player = SimHarness.AddBotPlayer(_game, _userId);
        if (_player == Entity.Null)
            return new Response { Ok = false, Error = "failed to add bot player" };

        _brain = new Template.Shared.Tests.MlBotBrain(_game, _player, _userId, seed, learningEnabled: false);
        _runner = new LightSimRunner(_game);

        for (int i = 0; i < 10; i++) _game.Loop.RunSingleTick();

        _prevSnapshot = SimHarness.Capture(_game);
        return new Response
        {
            Ok = true,
            Obs = ObservationBuilder.Build(_game, _player, _tick),
            ValidActions = _brain.GetValidActions(),
            Tick = _tick,
            BalanceHash = Template.Shared.GameData.Balance.JsonHash,
        };
    }

    public Response Step(int action, int numTicks)
    {
        if (_game == null || _brain == null || _runner == null)
            return new Response { Ok = false, Error = "session not reset" };
        if (numTicks < 1) numTicks = 1;
        if (numTicks > 240) numTicks = 240;

        bool accepted = _brain.TryExecuteAction(action);
        if (accepted)
        {
            if (_brain.DesiredDirection.SqrMagnitude > (Float)0.001f)
                _game.State.AddComponent(_player, _brain.CreateMoveAction());
            if (_brain.WantsToInteract && _brain.CurrentTarget != Entity.Null)
            {
                SimHarness.InjectOverlap(_game, _player, _brain.CurrentTarget);
                _game.State.AddComponent(_player, new InteractAction { UserId = _userId });
            }
        }

        for (int i = 0; i < numTicks; i++)
        {
            _game.Dispatcher.Update(_game.State);
            SimHarness.MockNavigation(_game);
            _runner.Tick();
            _tick++;
            if (_tick >= _maxTicks) break;
        }

        var curSnapshot = SimHarness.Capture(_game);
        var deltas = SimHarness.ComputeDeltas(_prevSnapshot, curSnapshot, numTicks);
        bool done = _tick >= _maxTicks || curSnapshot.FinalBuilt > 0;
        _prevSnapshot = curSnapshot;

        return new Response
        {
            Ok = true,
            Obs = ObservationBuilder.Build(_game, _player, _tick),
            Deltas = deltas,
            Done = done,
            Accepted = accepted,
            ValidActions = _brain.GetValidActions(),
            Tick = _tick,
        };
    }
}
