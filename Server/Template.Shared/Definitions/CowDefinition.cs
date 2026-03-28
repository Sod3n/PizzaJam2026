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

        // Assign random food preference (Grass, Carrot, Apple, or Mushroom)
        component.PreferredFood = random.NextInt(0, 4); // 0-3 inclusive

        System.Console.WriteLine($"[CowDefinition] Created Cow {entity.Id} with MaxExhaust: {totalExhaust}, PreferredFood: {component.PreferredFood} (Skins: {skinComponent.Skins.Count})");
        component.MaxExhaust = totalExhaust;

        // Disable avoidance so cow follows player closely
        ref var navAgent = ref ctx.GetComponent<NavigationAgent2D>(entity);
        navAgent.AvoidanceMask = 0;
    }
}
