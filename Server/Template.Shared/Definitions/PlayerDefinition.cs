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
    typeof(PlayerStateComponent),
    typeof(StateComponent))]
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
        characterBody.CollisionMask = (uint)CollisionLayer.Physics;
        characterBody.CollisionLayer = (uint)CollisionLayer.Physics;
        
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

        // Create interaction zone (child entity with Area2D)
        var zone = ctx.State.CreateEntity();
        var zoneTransform = new Transform2D(Vector2.Zero, 0, Vector2.One);
        zoneTransform.Parent = entity;
        zoneTransform.DestroyOnUnparent = true;
        ctx.State.AddComponent(zone, zoneTransform);
        ctx.State.AddComponent(zone, new Area2D
        {
            Monitoring = true,
            Monitorable = false,
            CollisionLayer = (uint)CollisionLayer.Zone,
            CollisionMask = (uint)(CollisionLayer.Physics | CollisionLayer.Interactable),
        });
        ctx.State.AddComponent(zone, CollisionShape2D.CreateCircle(2.0f));

        // Initialize PlayerStateComponent
        ctx.AddComponent(entity, new PlayerStateComponent
        {
            InteractionTarget = Entity.Null,
            ReturnPosition = position,
            InteractionZone = zone,
            FollowingCow = Entity.Null
        });

        // Initialize StateComponent (idle = disabled, empty key)
        ctx.AddComponent(entity, new StateComponent
        {
            Key = "",
            CurrentTime = 0,
            MaxTime = 0,
            IsEnabled = false
        });
        
        return entity;
    }
}
