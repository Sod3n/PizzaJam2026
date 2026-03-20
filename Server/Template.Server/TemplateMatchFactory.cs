using Deterministic.GameFramework.Network.Server;
using Template.Shared.Factories;
using Template.Shared.Features.Player;
using Guid = System.Guid;

namespace Template.Server;

public class TemplateMatchFactory : IMatchFactory
{
    public Match CreateMatch(Guid matchId)
    {
        // 1. Create Game using Shared Factory (ensures consistent setup)
        var game = TemplateGameFactory.CreateGame(tickRate: 60);
        
        var match = new Match(matchId, game);
        
        var connectionLogic = new PlayerConnectionLogic(game.Loop);
        
        match.OnPlayerJoined += connectionLogic.OnPlayerJoined;
        match.OnPlayerLeft += connectionLogic.OnPlayerLeft;
        
        return match; 
    }
}