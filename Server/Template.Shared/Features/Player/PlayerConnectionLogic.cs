using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Template.Shared.Actions;
using Guid = System.Guid;

namespace Template.Shared.Features.Player;

public class PlayerConnectionLogic
{
    private readonly GameLoop _gameLoop;

    public PlayerConnectionLogic(GameLoop gameLoop)
    {
        _gameLoop = gameLoop;
    }

    public void OnPlayerJoined(Guid playerId)
    {
        // Fire on the NEXT tick, not the current one. This runs on the network thread
        // (called from GamePacketProcessor.JoinMatchAsync → match.AddPlayer). If we target
        // the loop's current tick, there's a race where Schedule lands between
        // ExecuteActions(N) and PruneHistory(N+1) — the action is never executed and
        // then silently pruned, leaving the server without a PlayerEntity. Scheduling
        // for CurrentTick+1 guarantees the action is picked up on the next tick's
        // ExecuteActions, regardless of where the loop thread currently is.
        _gameLoop.ScheduleOnTick(_gameLoop.CurrentTick + 1, new AddPlayerAction(playerId), World.Entity);
    }

    public void OnPlayerLeft(Guid playerId)
    {
        _gameLoop.ScheduleOnTick(_gameLoop.CurrentTick + 1, new RemovePlayerAction(playerId), World.Entity);
    }
}
