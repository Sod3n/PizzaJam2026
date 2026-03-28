using System;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Network.Server;
using Template.Shared.Factories;
using Template.Shared.Features.Player;
using Guid = System.Guid;

namespace Template.Server;

public class TemplateMatchFactory : IMatchFactory
{
    public Match CreateMatch(Guid matchId, byte[]? initialState = null)
    {
        // 1. Create Game using Shared Factory (ensures consistent setup)
        var game = TemplateGameFactory.CreateGame(tickRate: 60);

        // 2. If we have a saved state, restore it (overwrites entities from scene OnEnter)
        if (initialState != null && initialState.Length > 8)
        {
            long savedTick = BitConverter.ToInt64(initialState, 0);
            byte[] stateData = new byte[initialState.Length - 8];
            Array.Copy(initialState, 8, stateData, 0, stateData.Length);

            StateSerializer.Deserialize(game.State, stateData);
            game.Loop.ForceSetTick(savedTick);

            Console.WriteLine($"[MatchFactory] Restored saved state at tick {savedTick} ({stateData.Length} bytes)");
        }

        var match = new Match(matchId, game);

        var connectionLogic = new PlayerConnectionLogic(game.Loop);

        match.OnPlayerJoined += connectionLogic.OnPlayerJoined;
        match.OnPlayerLeft += connectionLogic.OnPlayerLeft;

        return match;
    }
}