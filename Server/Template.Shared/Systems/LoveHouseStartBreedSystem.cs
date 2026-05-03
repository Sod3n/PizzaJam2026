using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Actions;
using Template.Shared.Components;
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

            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var loveHouseEntity = req.Target;
            if (!state.HasComponent<LoveHouseComponent>(loveHouseEntity)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            var ctx = new Context(state, playerEntity, null!);
            if (InteractActionService.IsOnCooldown(ctx, loveHouseEntity)) continue;

            var lh = state.GetComponent<LoveHouseComponent>(loveHouseEntity);
            if (lh.CowId1 == Entity.Null || lh.CowId2 == Entity.Null) continue;

            StartBreed(state, playerEntity, loveHouseEntity);
        }
    }

    private static void StartBreed(EntityWorld state, Entity playerEntity, Entity loveHouseEntity)
    {
        int breedCost = Balance.Breed.MinCost;
        int heartPercent = Balance.Breed.HeartDefault;

        Entity cow1, cow2;
        {
            var loveHouse = state.GetComponent<LoveHouseComponent>(loveHouseEntity);
            cow1 = loveHouse.CowId1;
            cow2 = loveHouse.CowId2;
        }

        if (state.HasComponent<CowComponent>(cow1) && state.HasComponent<CowComponent>(cow2))
        {
            var c1 = state.GetComponent<CowComponent>(cow1);
            var c2 = state.GetComponent<CowComponent>(cow2);
            breedCost = System.Math.Max(Balance.Breed.MinCost, (c1.MaxExhaust + c2.MaxExhaust) / 2);

            bool sameTier = c1.PreferredFood == c2.PreferredFood;
            bool isLovePair = c1.LoveTarget == cow2 || c2.LoveTarget == cow1;

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
                heartPercent = tierGap switch
                {
                    1 => Balance.Breed.HeartTierGap1,
                    2 => Balance.Breed.HeartTierGap2,
                    _ => Balance.Breed.HeartTierGap3Plus,
                };

                if (Balance.Cow.DepressionEnabled)
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
                    bool willFail = breedRandom.NextInt(100) < failChance;
                    if (willFail)
                        breedCost *= Balance.Breed.FailCostMultiplier;
                }
            }
        }

        {
            ref var loveHouse = ref state.GetComponent<LoveHouseComponent>(loveHouseEntity);
            loveHouse.BreedProgress = 0;
            loveHouse.BreedCost = breedCost;
            loveHouse.HeartPercent = heartPercent;
        }

        StatePhase phase;
        {
            ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
            StateDefinitions.Begin(ref sc, StateKeys.Breed);
            phase = sc.Phase;
        }

        Vector2 returnPos = default;
        bool hasReturnPos = state.HasComponent<Transform2D>(playerEntity);
        if (hasReturnPos) returnPos = state.GetComponent<Transform2D>(playerEntity).Position;
        {
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.InteractionTarget = loveHouseEntity;
            if (hasReturnPos) ps.ReturnPosition = returnPos;
        }

        state.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Breed, Phase = phase, Age = 0 });

        ILogger.Log($"[LoveHouseStartBreedSystem] Started breeding at love house {loveHouseEntity.Id}, cost={breedCost}");

        var ctx = new Context(state, playerEntity, null!);
        InteractFeedback.Success(ctx, playerEntity, loveHouseEntity);
    }
}
