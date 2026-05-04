using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Owns the breed feature end-to-end:
//   Breed exit dispatch (regular crossbreed via cow target, or love-house breed via house target)
//   Outcome roll (failure on tier-gap, depression, twins on same-tier success)
//   Helper unlock (mutually exclusive with cow spawn at unlock thresholds — see BreedHelperUnlock)
//   Cow spawn (skin crossbreed, exhaust calc, food preference, parent linkage — see CowSpawn)
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

        if (state.HasComponent<CowComponent>(target) && state.HasComponent<CowComponent>(followingCow)
            && state.HasComponent<SkinComponent>(target) && state.HasComponent<SkinComponent>(followingCow))
        {
            CowSpawn.SpawnCrossbredCow(state, playerEntity, followingCow, target);
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

        Entity babyCow = TryRunBreed(state, playerEntity, loveHouseEntity, cow1, cow2);

        ReleaseBreedingPair(state, loveHouseEntity, cow1, cow2);

        if (state.HasComponent<CowComponent>(babyCow))
            CowSystemHelpers.AddCowToFollowChain(state, playerEntity, babyCow);

        CowSystemHelpers.ClearInteractionAndIdle(state, playerEntity);

        ILogger.Log($"[CowBreedingSystem] Love house breed complete. Released cows {cow1.Id} and {cow2.Id} back to player {playerEntity.Id}");
    }

    private static Entity TryRunBreed(EntityWorld state, Entity playerEntity, Entity loveHouseEntity, Entity cow1, Entity cow2)
    {
        if (!state.HasComponent<CowComponent>(cow1) || !state.HasComponent<CowComponent>(cow2)
            || !state.HasComponent<SkinComponent>(cow1) || !state.HasComponent<SkinComponent>(cow2))
            return Entity.Null;

        // RNG order is part of the gameplay contract: failure roll first, twin roll second (success only).
        var gameTime = state.GetCustomData<IGameTime>();
        uint breedSeed = (uint)((cow1.Id * 7919 + cow2.Id * 104729) ^ (gameTime?.CurrentTick ?? 0));
        var breedRandom = new DeterministicRandom(breedSeed);

        var outcome = RollBreedOutcome(state, cow1, cow2, ref breedRandom);

        if (outcome.Failed)
        {
            ApplyBreedFailure(state, cow1, cow2, outcome);
            return Entity.Null;
        }

        int breedCount = IncrementBreedCounter(state);
        var babyCow = ApplyBreedSuccess(state, playerEntity, cow1, cow2, loveHouseEntity, outcome, breedCount, ref breedRandom);
        CowLoveEventSystem.OnBreedSuccess(state, playerEntity, breedCount);
        return babyCow;
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
        if (BreedHelperUnlock.TrySpawnHelper(state, playerEntity, cow1, cow2, loveHouseEntity, breedCount, outcome.GuaranteedUpgrade))
            return Entity.Null;

        var gameTime = state.GetCustomData<IGameTime>();
        ILogger.Log($"[CowBreedingSystem] PRE-SPAWN NextEntityId={state.NextEntityId} tick={gameTime?.CurrentTick}");
        var babyCow = CowSpawn.SpawnCrossbredCow(state, playerEntity, cow1, cow2, breedCount);
        ILogger.Log($"[CowBreedingSystem] POST-SPAWN babyCow={babyCow.Id} NextEntityId={state.NextEntityId}");

        if (outcome.SameTier && babyCow != Entity.Null
            && breedRandom.NextInt(100) < Balance.Cow.TwinChancePercent)
        {
            CowSpawn.SpawnTwin(state, playerEntity, cow1, cow2, babyCow);
        }

        return babyCow;
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

        StampBreedCooldown(state, loveHouseEntity);
    }

    // Cooldown unit is Days, cleared by SleepLogic.AdvanceDay. Any non-zero TicksRemaining
    // means "on cooldown" — actual ticks don't decay per-tick because the unit is days.
    private static void StampBreedCooldown(EntityWorld state, Entity loveHouseEntity)
    {
        if (state.HasComponent<CooldownComponent>(loveHouseEntity))
        {
            ref var cd = ref state.GetComponent<CooldownComponent>(loveHouseEntity);
            if (cd.MaxTicks <= 0) cd.MaxTicks = 1;
            cd.TicksRemaining = cd.MaxTicks;
            cd.Unit = CooldownUnit.Days;
            return;
        }

        state.AddComponent(loveHouseEntity, new CooldownComponent
        {
            MaxTicks = 1,
            TicksRemaining = 1,
            Unit = CooldownUnit.Days,
        });
    }
}
