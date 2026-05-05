using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

public static class CowSpawn
{
    public static Entity SpawnCrossbredCow(EntityWorld state, Entity playerEntity, Entity parentA, Entity parentB,
        int breedCount = 0, SkinComponent? twinSkin = null)
    {
        var spawnPos = state.TryGetComponent<Transform2D>(parentB, out var parentBTransform)
            ? parentBTransform.Position + new Vector2(2, 0)
            : new Vector2(0, 0);

        var context = state.Ctx(playerEntity);
        var newCow = CowDefinition.Create(context, spawnPos);

        var gameTime = state.GetCustomData<IGameTime>();
        uint seed = (uint)(newCow.Id ^ (gameTime?.CurrentTick ?? 0));
        var random = new DeterministicRandom(seed);

        var crossbredSkin = BuildCrossbredSkin(state, parentA, parentB, breedCount, twinSkin, ref random);
        state.AddComponent(newCow, crossbredSkin);

        int totalExhaust = ComputeExhaust(crossbredSkin);
        var (preferredFood, secondaryPref) = RollFoodPreferences(state, parentA, parentB, ref random);

        ref var newCowComp = ref state.GetComponent<CowComponent>(newCow);
        newCowComp.MaxExhaust = totalExhaust;
        newCowComp.ParentA = parentA;
        newCowComp.ParentB = parentB;
        newCowComp.PreferredFood = preferredFood;
        newCowComp.SecondaryPreferredFood = secondaryPref;
        newCowComp.DiscoveredFoodMask = 0;

        ILogger.Log($"[CowSpawn] Bred new cow {newCow.Id} with MaxExhaust: {totalExhaust}, PreferredFood: {preferredFood}");
        state.AddComponent(newCow, new BreedBornComponent());
        return newCow;
    }

    public static void SpawnTwin(EntityWorld state, Entity playerEntity, Entity cow1, Entity cow2, Entity babyCow)
    {
        var babySkin = state.GetComponent<SkinComponent>(babyCow);
        var twinCow = SpawnCrossbredCow(state, playerEntity, cow1, cow2, 0, twinSkin: babySkin);
        if (twinCow == Entity.Null) return;

        ref var twinComp = ref state.GetComponent<CowComponent>(twinCow);
        twinComp.FollowingPlayer = playerEntity;
        twinComp.FollowTarget = babyCow;
        ILogger.Log($"[CowSpawn] TWINS! Second calf {twinCow.Id} born from same-pref breed");
    }

    private static SkinComponent BuildCrossbredSkin(EntityWorld state, Entity parentA, Entity parentB,
        int breedCount, SkinComponent? twinSkin, ref DeterministicRandom random)
    {
        var skinA = state.GetComponent<SkinComponent>(parentA);
        var skinB = state.GetComponent<SkinComponent>(parentB);

        ref var spawnCounts = ref CowSystemHelpers.GetSpawnCounts(state);
        var crossbredSkin = twinSkin ?? GameData.GD.SkinsData.CrossbreedSkin(ref random, skinA, skinB, ref spawnCounts);

        if (breedCount == GlobalResourcesComponent.GuaranteedMegaBreed)
        {
            var topKey = new FixedString32("Top");
            int megaId = GameData.GD.SkinsData.GetRandomMaxMegaId(ref random);
            if (crossbredSkin.Skins.ContainsKey(topKey))
                crossbredSkin.Skins[topKey] = megaId;
            else
                crossbredSkin.Skins.Add(topKey, megaId);
            ILogger.Log($"[CowSpawn] Guaranteed max Megaaaabooba drop at breed #{breedCount}!");
        }

        return crossbredSkin;
    }

    // Round up to nearest multiple of 4 so milking always completes cleanly.
    private static int ComputeExhaust(SkinComponent skin)
    {
        int totalExhaust = 0;
        foreach (var skinId in skin.Skins.Values)
        {
            var skinDef = GameData.GD.SkinsData.Get(skinId);
            if (skinDef != null)
                totalExhaust += skinDef.Exhaust;
        }
        if (totalExhaust <= 0) totalExhaust = 10;
        return ((totalExhaust + 3) / 4) * 4;
    }

    private static (int preferred, int secondary) RollFoodPreferences(EntityWorld state, Entity parentA, Entity parentB, ref DeterministicRandom random)
    {
        var parentACow = state.GetComponent<CowComponent>(parentA);
        var parentBCow = state.GetComponent<CowComponent>(parentB);
        int inheritChance = Balance.Cow.BreedInheritParentChancePercent;
        int prefRoll = random.NextInt(100);

        int preferredFood;
        if (prefRoll < inheritChance) preferredFood = parentACow.PreferredFood;
        else if (prefRoll < inheritChance * 2) preferredFood = parentBCow.PreferredFood;
        else preferredFood = FoodType.RandomPreferred(ref random);

        int secondaryPref = -1;
        if (random.NextInt(100) < Balance.Cow.SecondaryPreferenceChancePercent)
        {
            int second;
            int safety = 0;
            do { second = random.NextInt(0, 4); safety++; }
            while (second == preferredFood && safety < 8);
            secondaryPref = second == preferredFood ? -1 : second;
        }

        return (preferredFood, secondaryPref);
    }
}
