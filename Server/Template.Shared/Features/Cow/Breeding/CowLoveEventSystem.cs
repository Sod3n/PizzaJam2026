using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Owns the "love event" feature: a delayed event after every N successful breeds where one cow
// falls for another (highest-tier cow in a house) and starts following the player. Pairing them
// in a love house guarantees a tier upgrade on the next breed.
//
// The Update tick decrements the scheduled timer and fires the event when it hits zero.
// CowBreedingSystem calls OnBreedSuccess after a successful breed to schedule the next event.
public class CowLoveEventSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        // Toggle = registration. If you don't want love events, don't register this system.
        if (!CowSystemHelpers.TryGetGlobalResourcesEntity(state, out var grEntity)) return;

        bool fired;
        Entity targetPlayer;
        int breedCountForLove;
        {
            ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            if (gr.LoveEventTimer <= 0) return;
            gr.LoveEventTimer--;
            fired = gr.LoveEventTimer <= 0;
            targetPlayer = fired ? gr.LoveEventCowTarget : Entity.Null;
            breedCountForLove = fired ? gr.LoveEventBreedCount : 0;
            if (fired)
            {
                gr.LoveEventCowTarget = Entity.Null;
                gr.LoveEventBreedCount = 0;
            }
        }

        if (fired && targetPlayer != Entity.Null && state.HasComponent<PlayerStateComponent>(targetPlayer))
        {
            ILogger.Log($"[CowLoveEventSystem] Love event timer expired — triggering deferred love event for player {targetPlayer.Id}");
            TriggerLoveEvent(state, targetPlayer, breedCountForLove);
        }
    }

    public static void OnBreedSuccess(EntityWorld state, Entity playerEntity, int breedCount)
    {
        if (breedCount <= 0 || !Balance.Love.Enabled) return;
        if (!CowSystemHelpers.TryGetGlobalResourcesEntity(state, out var grEntity)) return;

        ref var gr = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
        if (breedCount < gr.NextLoveBreedCount) return;
        if (gr.LoveEventTimer > 0) return;

        var timerSeed = new DeterministicRandom((uint)(breedCount * 31337));
        int timer = timerSeed.NextInt(Balance.Love.EventDelayTicksMin, Balance.Love.EventDelayTicksMax);
        if (timer == 0) timer = 1;
        gr.LoveEventTimer = timer;
        gr.LoveEventCowTarget = playerEntity;
        gr.LoveEventBreedCount = breedCount;
        ILogger.Log($"[CowLoveEventSystem] Love event threshold reached: breedCount={breedCount} >= NextLoveBreedCount={gr.NextLoveBreedCount}. Timer set to {timer} ticks ({timer / 60f:F1}s)");

        var nextSeed = new DeterministicRandom((uint)(breedCount * 31337 + 7));
        gr.NextLoveBreedCount = breedCount + nextSeed.NextInt(Balance.Love.NextEventBreedsMin, Balance.Love.NextEventBreedsMax);
        ILogger.Log($"[CowLoveEventSystem] Next love threshold set to {gr.NextLoveBreedCount}");
    }

    private static void TriggerLoveEvent(EntityWorld state, Entity playerEntity, int breedCount)
    {
        int cowsTotal = 0, cowsInHouses = 0, cowsFollowing = 0, cowsDepressed = 0;
        var candidates = new List<Entity>();
        Entity bestTarget = Entity.Null;
        int bestFood = -1;

        foreach (var ce in state.Filter<CowComponent>())
        {
            cowsTotal++;
            var c = state.GetComponent<CowComponent>(ce);
            if (state.HasComponent<HouseComponent>(c.HouseId)) cowsInHouses++;
            if (c.FollowingPlayer != Entity.Null) cowsFollowing++;
            if (c.IsDepressed) cowsDepressed++;

            if (!IsLoveEligible(state, c)) continue;
            candidates.Add(ce);
            if (c.PreferredFood > bestFood)
            {
                bestFood = c.PreferredFood;
                bestTarget = ce;
            }
        }

        ILogger.Log($"[CowLoveEventSystem] TriggerLoveEvent: total={cowsTotal} inHouses={cowsInHouses} following={cowsFollowing} depressed={cowsDepressed}");

        if (bestTarget == Entity.Null)
        {
            ILogger.Log($"[CowLoveEventSystem] Love event skipped: no valid target cow found");
            return;
        }

        if (candidates.Count <= 1)
        {
            ILogger.Log($"[CowLoveEventSystem] Love event skipped: no eligible lover cow found");
            return;
        }

        var loveSeed = new DeterministicRandom((uint)(breedCount * 7 + bestTarget.Id));
        Entity lover;
        do { lover = candidates[loveSeed.NextInt(candidates.Count)]; } while (lover == bestTarget);

        state.GetComponent<CowComponent>(lover).LoveTarget = bestTarget;

        CowSystemHelpers.DetachCowFromHouse(state, lover, playerEntity);
        CowSystemHelpers.AddCowToFollowChain(state, playerEntity, lover);

        // Popup is deferred to the player's interaction — the lover follows with a need icon.
        string loverName = state.TryGetComponent<NameComponent>(lover, out var ln) ? ln.Name.ToString() : $"Cow #{lover.Id}";
        string targetName = state.TryGetComponent<NameComponent>(bestTarget, out var tn) ? tn.Name.ToString() : $"Cow #{bestTarget.Id}";
        ILogger.Log($"[CowLoveEventSystem] Love event! {loverName} (cow {lover.Id}) fell in love with {targetName} (cow {bestTarget.Id}) — waiting for player interaction");
    }

    private static bool IsLoveEligible(EntityWorld state, CowComponent cow)
    {
        if (cow.HouseId == Entity.Null) return false;
        if (!state.HasComponent<HouseComponent>(cow.HouseId)) return false;
        if (cow.FollowingPlayer != Entity.Null) return false;
        if (cow.IsDepressed) return false;
        if (cow.LoveTarget != Entity.Null) return false;
        return true;
    }
}
