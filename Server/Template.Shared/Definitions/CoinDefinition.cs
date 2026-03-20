using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.TwoD;
using Template.Shared.Components;
using Deterministic.GameFramework.DAR;

using Deterministic.GameFramework.Physics2D.Components;

namespace Template.Shared.Definitions;

[EntityDefinition(
    typeof(Transform2D), 
    typeof(CoinComponent),
    typeof(Area2D),
    typeof(CollisionShape2D))]
public static partial class CoinDefinition
{
    public static Entity Create(Context ctx, Vector2 position, int value)
    {
        var entity = ctx.CreateEntity<CoinComponent>();
        
        ref var coin = ref ctx.GetComponent<CoinComponent>(entity);
        coin.Value = value;
        
        ctx.AddComponent(entity, new Transform2D(position, 0, Vector2.One));
        
        // Add Area2D for collision detection (Trigger)
        ctx.AddComponent(entity, new Area2D 
        { 
            Monitoring = true,
            Monitorable = false, // Coins don't need to be monitored by others
            CollisionMask = uint.MaxValue, // Monitor Everything
            CollisionLayer = 2,
            OverlappingEntities = new List8<int>()
        });
        
        // Add CollisionShape
        ctx.AddComponent(entity, CollisionShape2D.CreateCircle(20));
        
        return entity;
    }
}
