using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared.Definitions;

public static partial class CowDefinition
{
    static partial void OnEntityCreated(Context ctx, Entity entity, ref CowComponent component, Dictionary<string, Entity> childEntities)
    {
        component.SpawnPosition = ctx.GetComponent<Transform2D>(entity).Position;

        // Generate random skin and calculate MaxExhaust from skin data
        var random = new DeterministicRandom((uint)entity.Id + 2000);
        var skinComponent = GameData.GD.SkinsData.GenerateRandomSkin(ref random);
        ctx.AddComponent(entity, skinComponent);

        int totalExhaust = 0;
        foreach (var skinId in skinComponent.Skins.Values)
        {
            var skinDef = GameData.GD.SkinsData.Get(skinId);
            if (skinDef != null)
                totalExhaust += skinDef.Exhaust;
        }
        if (totalExhaust <= 0) totalExhaust = 10;

        // Weighted random: common cows prefer cheap food, rare cows prefer expensive food
        component.PreferredFood = FoodType.RandomPreferred(ref random);

        System.Console.WriteLine($"[CowDefinition] Created Cow {entity.Id} with MaxExhaust: {totalExhaust}, PreferredFood: {component.PreferredFood} (Skins: {skinComponent.Skins.Count})");
        component.MaxExhaust = totalExhaust;

        // Enable avoidance so cows steer around the player
        ref var navAgent = ref ctx.GetComponent<NavigationAgent2D>(entity);
        navAgent.AvoidanceEnabled = true;
        navAgent.AvoidanceMask = 1u; // Detect player on collision layer 1
    }
}
