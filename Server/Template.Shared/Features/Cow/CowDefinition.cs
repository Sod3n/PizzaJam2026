using System.Collections.Generic;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Definitions;

public static partial class CowDefinition
{
    static partial void OnEntityCreated(Context ctx, Entity entity, ref CowComponent component, Dictionary<string, Entity> childEntities)
    {
        component.SpawnPosition = ctx.GetComponent<Transform2D>(entity).Position;

        ref var spawnCounts = ref GetSpawnCounts(ctx);

        var random = new DeterministicRandom((uint)entity.Id + 2000);
        var skinComponent = GameData.GD.SkinsData.GenerateRandomSkin(ref random, ref spawnCounts);
        ctx.AddComponent(entity, skinComponent);

        int totalExhaust = 0;
        foreach (var skinId in skinComponent.Skins.Values)
        {
            var skinDef = GameData.GD.SkinsData.Get(skinId);
            if (skinDef != null)
                totalExhaust += skinDef.Exhaust;
        }
        totalExhaust = MapExhaustWeight(totalExhaust);

        // Weighted random: common cows prefer cheap food, rare cows prefer expensive food
        component.PreferredFood = FoodType.RandomPreferred(ref random);

        // Some cows also like a second food (different from primary).
        if (random.NextInt(100) < Balance.Cow.SecondaryPreferenceChancePercent)
        {
            int second;
            int safety = 0;
            do { second = random.NextInt(0, 4); safety++; }
            while (second == component.PreferredFood && safety < 8);
            component.SecondaryPreferredFood = second == component.PreferredFood ? -1 : second;
        }
        else
        {
            component.SecondaryPreferredFood = -1;
        }
        component.DiscoveredFoodMask = 0;

        ILogger.Log($"[CowDefinition] Created Cow {entity.Id} MaxExhaust={totalExhaust} Pref={component.PreferredFood} Pref2={component.SecondaryPreferredFood}");
        component.MaxExhaust = totalExhaust;
        component.MaxHorny = ComputeMaxHorny(totalExhaust);

        ctx.AddComponent(entity, NameComponent.RandomCow(ref random));

        // Enable avoidance so cows steer around the player
        ref var navAgent = ref ctx.GetComponent<NavigationAgent2D>(entity);
        navAgent.TargetDesiredDistance = 4f;
        navAgent.AvoidanceEnabled = true;
        navAgent.AvoidanceMask = 1u; // Detect player on collision layer 1
    }

    /// <summary>
    /// Re-roll <paramref name="cowEntity"/>'s skin from the budgeted pool (Exhaust ≤ budget),
    /// then refresh <see cref="CowComponent.MaxExhaust"/> + <see cref="CowComponent.MaxHorny"/>
    /// from the new skin. Pure state mutation — no static or process-wide context.
    /// </summary>
    public static void ApplyExhaustBudget(EntityWorld state, Entity cowEntity, int totalExhaustBudget)
    {
        if (totalExhaustBudget <= 0) return;
        if (!state.HasComponent<CowComponent>(cowEntity)) return;
        if (!state.HasComponent<SkinComponent>(cowEntity)) return;

        Entity countsEntity = Entity.Null;
        foreach (var e in state.Filter<SkinSpawnCountsComponent>()) { countsEntity = e; break; }
        if (countsEntity == Entity.Null) return;

        ref var spawnCounts = ref state.GetComponent<SkinSpawnCountsComponent>(countsEntity);
        var random = new DeterministicRandom((uint)cowEntity.Id + 5000);
        var newSkin = GameData.GD.SkinsData.GenerateRandomSkinBudgeted(ref random, ref spawnCounts, totalExhaustBudget);

        state.GetComponent<SkinComponent>(cowEntity) = newSkin;

        int total = 0;
        foreach (var skinId in newSkin.Skins.Values)
        {
            var def = GameData.GD.SkinsData.Get(skinId);
            if (def != null) total += def.Exhaust;
        }
        total = MapExhaustWeight(total);

        ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
        cow.MaxExhaust = total;
        cow.MaxHorny = ComputeMaxHorny(total);
    }

    /// <summary>
    /// Remap a summed skin-Exhaust *weight* onto the balance-tunable [MinExhaust, MaxExhaust] range
    /// using <see cref="Balance.Cow.ExhaustCurve"/>. Skin values become rank weights, actual milking
    /// difficulty lives in balance.
    /// </summary>
    internal static int MapExhaustWeight(int weightSum)
    {
        var (wMin, wMax) = GameData.GD.SkinsData.GetExhaustWeightBounds();
        int outMin = System.Math.Max(1, Balance.Cow.MinExhaust);
        int outMax = System.Math.Max(outMin, Balance.Cow.MaxExhaust);
        if (wMax <= wMin) return outMin;
        double t = (double)(weightSum - wMin) / (wMax - wMin);
        if (t < 0) t = 0;
        else if (t > 1) t = 1;
        t = System.Math.Pow(t, System.Math.Max(0.0001, Balance.Cow.ExhaustCurve));
        return System.Math.Max(1, (int)System.Math.Round(outMin + (outMax - outMin) * t));
    }

    internal static int ComputeMaxHorny(int totalExhaust)
    {
        double ratio = (double)Balance.Cow.HornyExhaustBaseline / System.Math.Max(1, totalExhaust);
        double scaled = Balance.Cow.MaxHorny * System.Math.Pow(ratio, Balance.Cow.HornyExhaustCurve);
        return System.Math.Max(60, (int)System.Math.Round(scaled));
    }

    private static ref SkinSpawnCountsComponent GetSpawnCounts(Context ctx)
    {
        foreach (var e in ctx.State.Filter<SkinSpawnCountsComponent>())
            return ref ctx.State.GetComponent<SkinSpawnCountsComponent>(e);
        throw new System.InvalidOperationException("SkinSpawnCountsComponent entity not found");
    }
}
