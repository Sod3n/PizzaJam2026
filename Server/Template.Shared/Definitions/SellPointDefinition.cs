using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Reactive;
using Deterministic.GameFramework.Types;

namespace Template.Shared.Definitions;

[EntityDefinition(
    typeof(Transform2D),
    typeof(SellPointComponent),
    typeof(StaticBody2D),
    typeof(CollisionShape2D))]
public static partial class SellPointDefinition
{
    public static Entity Create(Context ctx, Vector2 position)
    {
        var entity = ctx.CreateEntity<SellPointComponent>();

        ctx.AddComponent(entity, new Transform2D(position, 0, Vector2.One));

        var body = new StaticBody2D();
        body.CollisionLayer = (uint)CollisionLayer.Interactable;
        body.CollisionMask = (uint)CollisionLayer.Zone;
        ctx.AddComponent(entity, body);

        ctx.AddComponent(entity, CollisionShape2D.CreateCircle(1.5f));
        
        return entity;
    }
}
