using Deterministic.GameFramework.ECS;

namespace Template.Shared.Components;

public static class CowComponentExtensions
{
    public static void ClearFollowChain(ref this CowComponent cow)
    {
        cow.FollowingPlayer = Entity.Null;
        cow.FollowTarget = Entity.Null;
    }

    public static void EndMilking(ref this CowComponent cow)
    {
        cow.IsMilking = false;
        cow.MilkClickCounter = 0;
    }

    public static void SettleIntoHouse(ref this CowComponent cow, Entity houseEntity)
    {
        cow.ClearFollowChain();
        cow.HouseId = houseEntity;
    }

    public static void EnterDepression(ref this CowComponent cow, int ticks)
    {
        cow.IsDepressed = true;
        cow.DepressionTicksRemaining = ticks;
    }
}
