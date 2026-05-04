using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Starts breeding on a love house with both cow slots full. Computes breed cost + heart visual
// from the pair (love-pair > same-tier > tier-gap), pre-rolls the fail outcome to scale the
// cost when depression is enabled. Strategic — main player only, no cooldown.
public class LoveHouseStartBreedSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;
            if (!state.HasComponent<StateComponent>(playerEntity)) continue;

            var loveHouseEntity = state.GetComponent<InteractRequestComponent>(playerEntity).Target;
            if (!state.TryResolve<LoveHouseArchetype>(loveHouseEntity, out var loveHouseRef)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            var ctx = state.Ctx(playerEntity);
            if (InteractActionService.IsOnCooldown(ctx, loveHouseEntity)) continue;

            if (loveHouseRef.CowSlot1 == Entity.Null || loveHouseRef.CowSlot2 == Entity.Null) continue;

            StartBreed(state, playerEntity, loveHouseRef);
        }
    }

    private static void StartBreed(EntityWorld state, Entity playerEntity, LoveHouseRef loveHouseRef)
    {
        var loveHouseEntity = loveHouseRef.Entity;
        Entity cow1 = loveHouseRef.CowSlot1;
        Entity cow2 = loveHouseRef.CowSlot2;

        var (breedCost, heartPercent) = ComputeBreedOutcome(state, cow1, cow2);

        {
            ref var lh = ref loveHouseRef.LoveHouse;
            lh.BreedProgress = 0;
            lh.BreedCost = breedCost;
            lh.HeartPercent = heartPercent;
        }

        BeginBreedState(state, playerEntity, loveHouseEntity);

        ILogger.Log($"[LoveHouseStartBreedSystem] Started breeding at love house {loveHouseEntity.Id}, cost={breedCost}");

        InteractFeedback.Success(state.Ctx(playerEntity), playerEntity, loveHouseEntity);
    }

    private static (int breedCost, int heartPercent) ComputeBreedOutcome(EntityWorld state, Entity cow1, Entity cow2)
    {
        int breedCost = Balance.Breed.MinCost;
        int heartPercent = Balance.Breed.HeartDefault;

        if (!state.HasComponent<CowComponent>(cow1) || !state.HasComponent<CowComponent>(cow2))
            return (breedCost, heartPercent);

        var c1 = state.GetComponent<CowComponent>(cow1);
        var c2 = state.GetComponent<CowComponent>(cow2);
        breedCost = System.Math.Max(Balance.Breed.MinCost, (c1.MaxExhaust + c2.MaxExhaust) / 2);

        bool isLovePair = c1.LoveTarget == cow2 || c2.LoveTarget == cow1;
        bool sameTier = c1.PreferredFood == c2.PreferredFood;

        if (isLovePair)
        {
            heartPercent = Balance.Breed.HeartLovePair;
        }
        else if (sameTier)
        {
            heartPercent = Balance.Breed.HeartSameTierPre;
        }
        else
        {
            int tierGap = System.Math.Abs(c1.PreferredFood - c2.PreferredFood);
            heartPercent = TierGapHeart(tierGap);

            if (Balance.Cow.DepressionEnabled && RollBreedFail(state, cow1, cow2, tierGap))
                breedCost *= Balance.Breed.FailCostMultiplier;
        }

        return (breedCost, heartPercent);
    }

    private static int TierGapHeart(int tierGap) => tierGap switch
    {
        1 => Balance.Breed.HeartTierGap1,
        2 => Balance.Breed.HeartTierGap2,
        _ => Balance.Breed.HeartTierGap3Plus,
    };

    private static bool RollBreedFail(EntityWorld state, Entity cow1, Entity cow2, int tierGap)
    {
        var gameTime = state.GetCustomData<IGameTime>();
        uint breedSeed = (uint)((cow1.Id * 7919 + cow2.Id * 104729) ^ (gameTime?.CurrentTick ?? 0));
        var breedRandom = new DeterministicRandom(breedSeed);
        int failChance = tierGap switch
        {
            1 => Balance.Breed.FailChanceTier1,
            2 => Balance.Breed.FailChanceTier2,
            _ => Balance.Breed.FailChanceTier3Plus,
        };
        return breedRandom.NextInt(100) < failChance;
    }

    private static void BeginBreedState(EntityWorld state, Entity playerEntity, Entity loveHouseEntity)
    {
        StatePhase phase;
        {
            ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
            StateDefinitions.Begin(ref sc, StateKeys.Breed);
            phase = sc.Phase;
        }

        ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
        ps.InteractionTarget = loveHouseEntity;
        if (state.TryGetComponent<Transform2D>(playerEntity, out var pt))
            ps.ReturnPosition = pt.Position;

        state.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Breed, Phase = phase, Age = 0 });
    }
}
