using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Owns the breed feature end-to-end:
//   Breed exit dispatch (regular crossbreed via cow target, or love-house breed via house target)
//   Outcome roll (failure on tier-gap, depression, twins on same-tier success)
//   Helper unlock (mutually exclusive with cow spawn at unlock thresholds)
//   Cow spawn (skin crossbreed, exhaust calc, food preference, parent linkage)
//   Parent return + cooldown stamp on the love house
//   Calls into CowLoveEventSystem to schedule the next love event after a successful breed.
public class CowBreedingSystem : ISystem
{
    private struct BreedOutcome
    {
        public bool Failed;
        public bool SameTier;
        public bool GuaranteedUpgrade;
        public int TierGap;
    }

    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<ExitStateComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;
            var exit = state.GetComponent<ExitStateComponent>(playerEntity);
            if (exit.Age > 0) continue;
            if (exit.Key != StateKeys.Breed) continue;

            HandleBreedComplete(state, playerEntity);
        }
    }

    private static void HandleBreedComplete(EntityWorld state, Entity playerEntity)
    {
        ILogger.Log($"[CowBreedingSystem] Breed complete for player {playerEntity.Id}");

        var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
        Entity target = ps.InteractionTarget;
        Entity followingCow = ps.FollowingCow;

        if (state.HasComponent<LoveHouseComponent>(target))
        {
            HandleLoveHouseBreedComplete(state, playerEntity, target);
            return;
        }

        // Regular crossbreed: target is the other cow
        if (state.HasComponent<CowComponent>(target) && state.HasComponent<CowComponent>(followingCow)
            && state.HasComponent<SkinComponent>(target) && state.HasComponent<SkinComponent>(followingCow))
        {
            SpawnCrossbredCow(state, playerEntity, followingCow, target);
        }

        CowSystemHelpers.ClearInteractionAndIdle(state, playerEntity);
    }

    private static void HandleLoveHouseBreedComplete(EntityWorld state, Entity playerEntity, Entity loveHouseEntity)
    {
        ILogger.Log($"[CowBreedingSystem] Love house breed complete for player {playerEntity.Id}");

        state.UnhideEntity(playerEntity);

        var loveHouse = state.GetComponent<LoveHouseComponent>(loveHouseEntity);
        Entity cow1 = loveHouse.CowId1;
        Entity cow2 = loveHouse.CowId2;

        state.UnhideEntity(cow1);
        state.UnhideEntity(cow2);

        Entity babyCow = Entity.Null;

        if (state.HasComponent<CowComponent>(cow1) && state.HasComponent<CowComponent>(cow2)
            && state.HasComponent<SkinComponent>(cow1) && state.HasComponent<SkinComponent>(cow2))
        {
            // RNG order is part of the gameplay contract: failure roll first, twin roll second (success only).
            var gameTime = state.GetCustomData<IGameTime>();
            uint breedSeed = (uint)((cow1.Id * 7919 + cow2.Id * 104729) ^ (gameTime?.CurrentTick ?? 0));
            var breedRandom = new DeterministicRandom(breedSeed);

            var outcome = RollBreedOutcome(state, cow1, cow2, ref breedRandom);

            if (outcome.Failed)
            {
                ApplyBreedFailure(state, cow1, cow2, outcome);
            }
            else
            {
                int breedCount = IncrementBreedCounter(state);
                babyCow = ApplyBreedSuccess(state, playerEntity, cow1, cow2, loveHouseEntity, outcome, breedCount, ref breedRandom);
                CowLoveEventSystem.OnBreedSuccess(state, playerEntity, breedCount);
            }
        }

        ReleaseBreedingPair(state, loveHouseEntity, cow1, cow2);

        if (state.HasComponent<CowComponent>(babyCow))
            CowSystemHelpers.AddCowToFollowChain(state, playerEntity, babyCow);

        CowSystemHelpers.ClearInteractionAndIdle(state, playerEntity);

        ILogger.Log($"[CowBreedingSystem] Love house breed complete. Released cows {cow1.Id} and {cow2.Id} back to player {playerEntity.Id}");
    }

    private static BreedOutcome RollBreedOutcome(EntityWorld state, Entity cow1, Entity cow2, ref DeterministicRandom breedRandom)
    {
        var parentACow = state.GetComponent<CowComponent>(cow1);
        var parentBCow = state.GetComponent<CowComponent>(cow2);

        bool sameTier = parentACow.PreferredFood == parentBCow.PreferredFood;
        bool guaranteedUpgrade = parentACow.LoveTarget == cow2 || parentBCow.LoveTarget == cow1;
        int tierGap = System.Math.Abs(parentACow.PreferredFood - parentBCow.PreferredFood);

        if (guaranteedUpgrade)
        {
            ILogger.Log($"[CowBreedingSystem] Love pair bred! Guaranteed tier upgrade for cows {cow1.Id} and {cow2.Id}");
            if (state.HasComponent<CowComponent>(cow1))
                state.GetComponent<CowComponent>(cow1).LoveTarget = Entity.Null;
            if (state.HasComponent<CowComponent>(cow2))
                state.GetComponent<CowComponent>(cow2).LoveTarget = Entity.Null;
        }

        bool failed = false;
        if (Balance.Cow.DepressionEnabled && !sameTier && !guaranteedUpgrade)
        {
            int failChance = tierGap switch
            {
                1 => Balance.Breed.FailChanceTier1,
                2 => Balance.Breed.FailChanceTier2,
                _ => Balance.Breed.FailChanceTier3Plus,
            };
            failed = breedRandom.NextInt(100) < failChance;
        }

        return new BreedOutcome
        {
            Failed = failed,
            SameTier = sameTier,
            GuaranteedUpgrade = guaranteedUpgrade,
            TierGap = tierGap,
        };
    }

    private static void ApplyBreedFailure(EntityWorld state, Entity cow1, Entity cow2, BreedOutcome outcome)
    {
        ILogger.Log($"[CowBreedingSystem] Breed FAILED! Cows {cow1.Id} and {cow2.Id} are depressed (tier gap: {outcome.TierGap})");
        const int DepressionDurationTicks = Balance.Cow.DepressionTicks;
        if (state.HasComponent<CowComponent>(cow1))
            state.GetComponent<CowComponent>(cow1).EnterDepression(DepressionDurationTicks);
        if (state.HasComponent<CowComponent>(cow2))
            state.GetComponent<CowComponent>(cow2).EnterDepression(DepressionDurationTicks);
    }

    private static int IncrementBreedCounter(EntityWorld state)
    {
        if (!CowSystemHelpers.TryGetGlobalResourcesEntity(state, out var grEntity)) return 0;

        ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
        gr.TotalBreedCount++;
        int newCount = gr.TotalBreedCount;
        ILogger.Log($"[CowBreedingSystem] Breed #{newCount} succeeded. NextLoveBreedCount={gr.NextLoveBreedCount}");

        // First breed initializes the love threshold
        if (Balance.Love.Enabled && gr.NextLoveBreedCount == 0)
        {
            var loveSeed = new DeterministicRandom((uint)(newCount ^ 0xBEEF));
            gr.NextLoveBreedCount = newCount + loveSeed.NextInt(Balance.Love.NextEventBreedsMin, Balance.Love.NextEventBreedsMax);
            ILogger.Log($"[CowBreedingSystem] Love threshold initialized to {gr.NextLoveBreedCount}");
        }
        return newCount;
    }

    private static Entity ApplyBreedSuccess(EntityWorld state, Entity playerEntity, Entity cow1, Entity cow2,
        Entity loveHouseEntity, BreedOutcome outcome, int breedCount, ref DeterministicRandom breedRandom)
    {
        // Helper unlock takes precedence over cow spawn at unlock thresholds.
        if (TrySpawnHelper(state, playerEntity, cow1, cow2, loveHouseEntity, breedCount, outcome.GuaranteedUpgrade))
            return Entity.Null;

        var gameTime = state.GetCustomData<IGameTime>();
        ILogger.Log($"[CowBreedingSystem] PRE-SPAWN NextEntityId={state.NextEntityId} tick={gameTime?.CurrentTick}");
        var babyCow = SpawnCrossbredCow(state, playerEntity, cow1, cow2, breedCount);
        ILogger.Log($"[CowBreedingSystem] POST-SPAWN babyCow={babyCow.Id} NextEntityId={state.NextEntityId}");

        // Twins on same-pref breeds — second calf follows the first; gets folded into the chain
        // when the baby is added (player ← chain ← baby ← twin).
        if (outcome.SameTier && babyCow != Entity.Null
            && breedRandom.NextInt(100) < Balance.Cow.TwinChancePercent)
        {
            var babySkin = state.GetComponent<SkinComponent>(babyCow);
            var twinCow = SpawnCrossbredCow(state, playerEntity, cow1, cow2, 0, twinSkin: babySkin);
            if (twinCow != Entity.Null)
            {
                ref var twinComp = ref state.GetComponent<CowComponent>(twinCow);
                twinComp.FollowingPlayer = playerEntity;
                twinComp.FollowTarget = babyCow;
                ILogger.Log($"[CowBreedingSystem] TWINS! Second calf {twinCow.Id} born from same-pref breed");
            }
        }

        return babyCow;
    }

    private static bool TrySpawnHelper(EntityWorld state, Entity playerEntity, Entity cow1, Entity cow2,
        Entity loveHouseEntity, int breedCount, bool guaranteedUpgrade)
    {
        if (guaranteedUpgrade) return false;
        if (!CowSystemHelpers.GetHelpersEnabled(state)) return false;

        int neededHelper = GetNextNeededHelper(state);
        if (neededHelper < 0) return false;

        if (!CowSystemHelpers.TryGetGlobalResourcesEntity(state, out var grEntity)) return false;

        int spawnedCount = state.GetComponent<GlobalResourcesComponent>(grEntity).HelpersSpawned;
        if (spawnedCount >= Balance.Helper.HelperUnlockBreeds.Length) return false;
        if (breedCount < Balance.Helper.HelperUnlockBreeds[spawnedCount]) return false;

        var spawnPos = state.TryGetComponent<Transform2D>(loveHouseEntity, out var lhTransform)
            ? lhTransform.Position + new Vector2(2, 0)
            : new Vector2(0, 0);

        var ctx = state.Ctx(playerEntity);
        var babyHelper = HelperDefinition.Create(ctx, spawnPos, neededHelper, playerEntity);
        {
            ref var helperComp = ref state.GetComponent<HelperComponent>(babyHelper);
            helperComp.ParentA = cow1;
            helperComp.ParentB = cow2;
        }
        state.AddComponent(babyHelper, new BreedBornComponent());

        ref var gr2 = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
        gr2.HelpersSpawned++;
        int spawnedNow = gr2.HelpersSpawned;
        var gt = state.GetCustomData<IGameTime>();
        float hMin = gt != null ? gt.CurrentTick / 60f / 60f : -1;
        ILogger.Log($"[CowBreedingSystem] Helper unlocked: #{spawnedNow} at breed #{breedCount} ({hMin:F1}m)!");
        return true;
    }

    // The player can cycle to any other helper role via the role sign — this is just the seed.
    private const int DefaultHelperRole = HelperType.Gatherer;
    private static int GetNextNeededHelper(EntityWorld state)
    {
        if (!CowSystemHelpers.TryGetGlobalResourcesEntity(state, out var grEntity)) return -1;
        int spawnedCount = state.GetComponent<GlobalResourcesComponent>(grEntity).HelpersSpawned;
        if (spawnedCount >= Balance.Helper.HelperUnlockBreeds.Length) return -1;
        return DefaultHelperRole;
    }

    private static void ReleaseBreedingPair(EntityWorld state, Entity loveHouseEntity, Entity cow1, Entity cow2)
    {
        if (state.TryResolve<LoveHouseArchetype>(loveHouseEntity, out var loveHouseRef))
        {
            loveHouseRef.ClearCowSlot(cow1);
            loveHouseRef.ClearCowSlot(cow2);
        }

        if (cow1 != Entity.Null) CowSystemHelpers.ReturnCowToHouse(state, cow1);
        if (cow2 != Entity.Null) CowSystemHelpers.ReturnCowToHouse(state, cow2);

        // Cooldown stamp: Days unit, cleared by SleepLogic.AdvanceDay. Any non-zero TicksRemaining
        // means "on cooldown" — actual ticks don't decay per-tick because the unit is days.
        if (state.HasComponent<CooldownComponent>(loveHouseEntity))
        {
            ref var cd = ref state.GetComponent<CooldownComponent>(loveHouseEntity);
            if (cd.MaxTicks <= 0) cd.MaxTicks = 1;
            cd.TicksRemaining = cd.MaxTicks;
            cd.Unit = CooldownUnit.Days;
        }
        else
        {
            state.AddComponent(loveHouseEntity, new CooldownComponent
            {
                MaxTicks = 1,
                TicksRemaining = 1,
                Unit = CooldownUnit.Days,
            });
        }
    }

    private static Entity SpawnCrossbredCow(EntityWorld state, Entity playerEntity, Entity parentA, Entity parentB,
        int breedCount = 0, SkinComponent? twinSkin = null)
    {
        var skinA = state.GetComponent<SkinComponent>(parentA);
        var skinB = state.GetComponent<SkinComponent>(parentB);

        var spawnPos = state.TryGetComponent<Transform2D>(parentB, out var parentBTransform)
            ? parentBTransform.Position + new Vector2(2, 0)
            : new Vector2(0, 0);

        var context = state.Ctx(playerEntity);
        var newCow = CowDefinition.Create(context, spawnPos);

        var gameTime = state.GetCustomData<IGameTime>();
        uint seed = (uint)(newCow.Id ^ (gameTime?.CurrentTick ?? 0));
        var random = new DeterministicRandom(seed);

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
            ILogger.Log($"[CowBreedingSystem] Guaranteed max Megaaaabooba drop at breed #{breedCount}!");
        }

        state.AddComponent(newCow, crossbredSkin);

        int totalExhaust = 0;
        foreach (var skinId in crossbredSkin.Skins.Values)
        {
            var skinDef = GameData.GD.SkinsData.Get(skinId);
            if (skinDef != null)
                totalExhaust += skinDef.Exhaust;
        }
        if (totalExhaust <= 0) totalExhaust = 10;
        // Round up to nearest multiple of 4 so milking always completes cleanly
        totalExhaust = ((totalExhaust + 3) / 4) * 4;

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

        ref var newCowComp = ref state.GetComponent<CowComponent>(newCow);
        newCowComp.MaxExhaust = totalExhaust;
        newCowComp.ParentA = parentA;
        newCowComp.ParentB = parentB;
        newCowComp.PreferredFood = preferredFood;
        newCowComp.SecondaryPreferredFood = secondaryPref;
        newCowComp.DiscoveredFoodMask = 0;

        ILogger.Log($"[CowBreedingSystem] Bred new cow {newCow.Id} with MaxExhaust: {totalExhaust}, PreferredFood: {preferredFood}");
        state.AddComponent(newCow, new BreedBornComponent());
        return newCow;
    }
}
