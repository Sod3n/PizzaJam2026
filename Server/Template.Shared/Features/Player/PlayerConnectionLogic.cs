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
        _gameLoop.Schedule(new AddPlayerAction(playerId), World.Entity);
    }

    public void OnPlayerLeft(Guid playerId)
    {
        _gameLoop.Schedule(new RemovePlayerAction(playerId), World.Entity);
    }
}
