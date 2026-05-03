using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public enum FollowTargetKind : byte
{
    Missing,
    Player,
    Cow,
}

public readonly ref struct FollowTargetRef
{
    private readonly EntityWorld _world;
    public readonly Entity Entity;
    public readonly FollowTargetKind Kind;

    internal FollowTargetRef(EntityWorld world, Entity entity, FollowTargetKind kind)
    {
        _world = world;
        Entity = entity;
        Kind = kind;
    }

    public bool TryGetPosition(out Vector2 position)
    {
        if (Kind == FollowTargetKind.Missing)
        {
            position = default;
            return false;
        }
        position = _world.GetComponent<Transform2D>(Entity).Position;
        return true;
    }
}

public static class FollowTargetResolution
{
    public static FollowTargetRef ResolveFollowTarget(this EntityWorld world, Entity entity)
    {
        if (!world.HasComponent<Transform2D>(entity))
            return new FollowTargetRef(world, entity, FollowTargetKind.Missing);
        if (world.HasComponent<PlayerEntity>(entity))
            return new FollowTargetRef(world, entity, FollowTargetKind.Player);
        if (world.HasComponent<CowComponent>(entity))
            return new FollowTargetRef(world, entity, FollowTargetKind.Cow);
        return new FollowTargetRef(world, entity, FollowTargetKind.Missing);
    }
}
