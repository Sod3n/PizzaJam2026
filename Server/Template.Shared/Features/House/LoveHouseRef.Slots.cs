using Deterministic.GameFramework.ECS;

namespace Template.Shared.Definitions;

public readonly ref partial struct LoveHouseRef
{
    public Entity CowSlot1 => LoveHouse.CowId1;
    public Entity CowSlot2 => LoveHouse.CowId2;

    public void ClearCowSlot(Entity cowEntity)
    {
        if (cowEntity == Entity.Null) return;
        ref var lh = ref LoveHouse;
        if (lh.CowId1 == cowEntity) lh.CowId1 = Entity.Null;
        if (lh.CowId2 == cowEntity) lh.CowId2 = Entity.Null;
    }
}
