using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public static class EntityWorldExtensions
{
    public static void HideEntity(this EntityWorld state, Entity entity)
    {
        if (state.HasComponent<HiddenComponent>(entity)) return;

        uint prevLayer = (uint)CollisionLayer.Physics;
        uint prevMask = (uint)CollisionLayer.Physics;

        if (state.HasComponent<CharacterBody2D>(entity))
        {
            ref var body = ref state.GetComponent<CharacterBody2D>(entity);
            prevLayer = body.CollisionLayer;
            prevMask = body.CollisionMask;

            body.CollisionLayer = 0;
            body.CollisionMask = 0;
            body.Velocity = Vector2.Zero;
        }

        state.AddComponent(entity, new HiddenComponent
        {
            PreviousLayer = prevLayer,
            PreviousMask = prevMask
        });
    }

    public static void UnhideEntity(this EntityWorld state, Entity entity)
    {
        if (!state.HasComponent<HiddenComponent>(entity)) return;

        var hidden = state.GetComponent<HiddenComponent>(entity);

        if (state.HasComponent<CharacterBody2D>(entity))
        {
            ref var body = ref state.GetComponent<CharacterBody2D>(entity);
            body.CollisionLayer = hidden.PreviousLayer;
            body.CollisionMask = hidden.PreviousMask;
        }

        state.RemoveComponent<HiddenComponent>(entity);
    }

    public static void DisableCollisions(this EntityWorld state, Entity entity)
    {
        if (!state.HasComponent<CharacterBody2D>(entity)) return;

        ref var body = ref state.GetComponent<CharacterBody2D>(entity);
        body.CollisionLayer = 0;
        body.CollisionMask = 0;
    }

    public static void EnableCollisions(this EntityWorld state, Entity entity,
        uint layer = (uint)CollisionLayer.Physics, uint mask = (uint)CollisionLayer.Physics)
    {
        if (!state.HasComponent<CharacterBody2D>(entity)) return;

        ref var body = ref state.GetComponent<CharacterBody2D>(entity);
        body.CollisionLayer = layer;
        body.CollisionMask = mask;
    }
}
