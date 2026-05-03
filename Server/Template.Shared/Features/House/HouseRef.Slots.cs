using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public readonly ref partial struct HouseRef
{
    public Entity CowSlot => House.CowId;

    /// <summary>
    /// Place <paramref name="cowEntity"/> in this house's cow slot.
    /// Returns the previously-occupying cow (or <see cref="Entity.Null"/> if the slot was empty
    /// or its prior occupant is no longer a valid cow).
    /// </summary>
    public Entity AssignCow(Entity cowEntity)
    {
        var prev = House.CowId;
        House.CowId = cowEntity;
        return _world.HasComponent<CowComponent>(prev) ? prev : Entity.Null;
    }

    public void ClearCowSlot() => House.CowId = Entity.Null;
}
