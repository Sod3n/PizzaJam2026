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
    typeof(LandComponent),
    typeof(Area2D),
    typeof(CollisionShape2D))]
public static partial class LandDefinition
{
    public static Entity Create(Context ctx, Vector2 position, int threshold)
    {
        var entity = ctx.CreateEntity<LandComponent>();
        
        ref var land = ref ctx.GetComponent<LandComponent>(entity);
        land.Threshold = threshold;
        land.CurrentCoins = 0;
        
        ctx.AddComponent(entity, new Transform2D(position, 0, Vector2.One));
        
        // Interaction area
        ctx.AddComponent(entity, new Area2D 
        { 
            Monitoring = true,
            Monitorable = true,
            CollisionMask = 0, 
            CollisionLayer = 4, // Interaction Layer
        });
        
        ctx.AddComponent(entity, CollisionShape2D.CreateRectangle(new Vector2(2, 2)));
        
        return entity;
    }
}
