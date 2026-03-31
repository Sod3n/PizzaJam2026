using System.Collections.Generic;
using Deterministic.GameFramework.ECS;

namespace Template.Shared.Tests;

public class BotCoordinator
{
    private readonly HashSet<int> _claimedEntities = new();
    private int _breederBotIndex = -1; // only one bot breeds at a time

    /// <summary>Try to claim an entity. Returns false if another bot already claimed it.</summary>
    public bool TryClaim(int botIndex, Entity entity)
    {
        if (entity == Entity.Null) return false;
        return _claimedEntities.Add(entity.Id);
    }

    /// <summary>Release all claims — called at start of each tick.</summary>
    public void ResetClaims() => _claimedEntities.Clear();

    /// <summary>Check if an entity is already claimed by another bot.</summary>
    public bool IsClaimed(Entity entity) => entity != Entity.Null && _claimedEntities.Contains(entity.Id);

    /// <summary>Try to become the breeder bot. Only one bot breeds at a time.</summary>
    public bool TryClaimBreeder(int botIndex)
    {
        if (_breederBotIndex < 0 || _breederBotIndex == botIndex)
        {
            _breederBotIndex = botIndex;
            return true;
        }
        return false;
    }

    /// <summary>Release breeder role (called when breeding is complete).</summary>
    public void ReleaseBreeder(int botIndex)
    {
        if (_breederBotIndex == botIndex)
            _breederBotIndex = -1;
    }

    public bool IsBreeder(int botIndex) => _breederBotIndex == botIndex;
}
