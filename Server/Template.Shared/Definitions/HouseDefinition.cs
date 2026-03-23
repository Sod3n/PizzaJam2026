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
    typeof(HouseComponent),
    typeof(CollisionShape2D),
    typeof(Area2D))] // For interacting with the house/cow inside?
public static partial class HouseDefinition
{
    public static Entity Create(Context ctx, Vector2 position)
    {
        var entity = ctx.CreateEntity<HouseComponent>();
        
        ctx.AddComponent(entity, new Transform2D(position, 0, Vector2.One));
        
        var staticBody = StaticBody2D.Default;
        ctx.AddComponent(entity, staticBody);
 
        
        // Static collider for walls
        ctx.AddComponent(entity, CollisionShape2D.CreateRectangle(new Vector2(2, 2)));
        
        return entity;
    }
}
