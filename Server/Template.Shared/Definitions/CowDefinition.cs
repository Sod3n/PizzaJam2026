using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Reactive;

namespace Template.Shared.Definitions;

[EntityDefinition(
    typeof(Transform2D),
    typeof(CowComponent),
    typeof(CharacterBody2D),
    typeof(CollisionShape2D),
    typeof(Area2D),
    typeof(SkinComponent))]
public static partial class CowDefinition
{
    public static Entity Create(Context ctx, Vector2 position)
    {
        var entity = ctx.CreateEntity<CowComponent>();
        
        ref var cow = ref ctx.GetComponent<CowComponent>(entity);
        cow.Exhaust = 0;
        cow.IsMilking = false;
        cow.SpawnPosition = position;
        
        ctx.AddComponent(entity, new Transform2D(position, 0, Vector2.One));
        
        var characterBody = CharacterBody2D.Default;
        characterBody.CollisionLayer = (uint)CollisionLayer.Physics;
        characterBody.CollisionMask = (uint)(CollisionLayer.Physics | CollisionLayer.Zone);
        ctx.AddComponent(entity, characterBody);
        
        ctx.AddComponent(entity, CollisionShape2D.CreateCircle(0.5f));
        
        // Initialize SkinComponent
        var random = new DeterministicRandom((uint)entity.Id + 2000); // Offset to avoid identical seeds
        var skinComponent = Template.Shared.GameData.GD.SkinsData.GenerateRandomSkin(ref random);
        ctx.AddComponent(entity, skinComponent);
        
        // Calculate MaxExhaust from Skins
        int totalExhaust = 0;
        foreach (var skinId in skinComponent.Skins.Values)
        {
            var skinDef = Template.Shared.GameData.GD.SkinsData.Get(skinId);
            if (skinDef != null)
            {
                totalExhaust += skinDef.Exhaust;
            }
        }
        
        // Default if no skins or 0 exhaust
        if (totalExhaust <= 0) totalExhaust = 10;
        
        System.Console.WriteLine($"[CowDefinition] Created Cow {entity.Id} with MaxExhaust: {totalExhaust} (Skins: {skinComponent.Skins.Count})");
        
        cow.MaxExhaust = totalExhaust;
        
        return entity;
    }
}
