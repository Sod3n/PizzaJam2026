using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Navigation2D.Components;
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
    typeof(SkinComponent),
    typeof(StateComponent),
    typeof(NavigationAgent2D))]
public static partial class CowDefinition
{
    public static Entity Create(Context ctx, Vector2 position)
    {
        var entity = ctx.CreateEntity<CowComponent>();

        ref var cow = ref ctx.GetComponent<CowComponent>(entity);
        cow.Exhaust = 0;
        cow.IsMilking = false;
        cow.SpawnPosition = position;
        cow.FollowingPlayer = Entity.Null;
        cow.HouseId = Entity.Null;

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

        // Navigation agent for following behavior
        var navAgent = NavigationAgent2D.Default;
        navAgent.MaxSpeed = 10f;
        navAgent.TargetDesiredDistance = 2.0f;
        navAgent.PathDesiredDistance = 1.0f;
        navAgent.Radius = 0.5f;
        navAgent.IsNavigationFinished = true;
        navAgent.AvoidanceMask = 0; // Disable avoidance — cow follows player closely
        ctx.AddComponent(entity, navAgent);

        return entity;
    }
}
