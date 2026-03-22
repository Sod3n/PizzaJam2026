using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Deterministic.GameFramework.DAR;

namespace Template.Shared.Definitions;

[EntityDefinition(
    typeof(Transform2D), 
    typeof(PlayerEntity), 
    typeof(CharacterBody2D),
    typeof(CollisionShape2D),
    typeof(SkinComponent),
    typeof(PlayerStateComponent))]
public static partial class PlayerDefinition
{
    public static Entity Create(Context ctx, System.Guid userId, Vector2 position, Float angle)
    {
        var entity = ctx.CreateEntity<PlayerEntity>();
        
        ref var player = ref ctx.GetComponent<PlayerEntity>(entity);
        player.UserId = userId;
        player.Id = entity;
        player.Name = new FixedString32("Player");
        
        // Transform2D handles Position and Rotation
        ctx.AddComponent(entity, new Transform2D(position, angle, Vector2.One));
        
        // CharacterBody2D handles Velocity and Physics properties
        // Set strict collision mask: Only collide with Layer 1 (Environment)
        // Ignore Layer 2 (Coins)
        var characterBody = CharacterBody2D.Default;
        characterBody.CollisionMask = 1; 
        characterBody.CollisionLayer = 1;
        
        ctx.AddComponent(entity, characterBody);
        
        ref var body = ref ctx.GetComponent<CharacterBody2D>(entity);
        body.Velocity = Vector2.Zero;
        body.UpDirection = new Vector2(0, -1);
        
        // Add a collider (defaulting to a small circle for now)
        ctx.AddComponent(entity, CollisionShape2D.CreateCircle(0.5f));
        
        // Initialize SkinComponent with a deterministic seed based on entity ID
        var random = new DeterministicRandom((uint)entity.Id + 1000); // Offset to avoid identical seeds
        var skinComponent = Template.Shared.GameData.GD.SkinsData.GenerateRandomSkin(ref random);
        ctx.AddComponent(entity, skinComponent);

        // Initialize PlayerStateComponent
        ctx.AddComponent(entity, new PlayerStateComponent 
        { 
            State = (int)PlayerState.Idle,
            InteractionTarget = Entity.Null,
            MilkingTimer = 0,
            ReturnPosition = position
        });
        
        return entity;
    }
}
