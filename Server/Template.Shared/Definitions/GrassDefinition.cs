using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Reactive;

namespace Template.Shared.Definitions;

[EntityDefinition(
    typeof(Transform2D),
    typeof(GrassComponent),
    typeof(StaticBody2D),
    typeof(CollisionShape2D))]
public static partial class GrassDefinition
{
    public static Entity Create(Context ctx, Vector2 position)
    {
        var entity = ctx.CreateEntity<GrassComponent>();

        ref var grass = ref ctx.GetComponent<GrassComponent>(entity);
        grass.Durability = 3;
        grass.MaxDurability = 3;

        ctx.AddComponent(entity, new Transform2D(position, 0, Vector2.One));

        var body = new StaticBody2D();
        body.CollisionLayer = (uint)CollisionLayer.Interactable;
        body.CollisionMask = (uint)CollisionLayer.Zone;
        ctx.AddComponent(entity, body);

        ctx.AddComponent(entity, CollisionShape2D.CreateCircle(1.0f));
        
        return entity;
    }
}
