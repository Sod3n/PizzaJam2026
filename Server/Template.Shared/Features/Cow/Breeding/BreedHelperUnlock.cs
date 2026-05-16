using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Helper unlock mechanic: every Nth successful breed spawns a new helper instead of a calf.
// Mutually exclusive with cow spawn (TrySpawnHelper returns true → caller skips SpawnCrossbredCow).
public static class BreedHelperUnlock
{
    // The player can cycle to any other helper role via the role sign — this is just the seed.
    private const int DefaultHelperRole = HelperType.Gatherer;

    public static bool TrySpawnHelper(EntityWorld state, Entity playerEntity, Entity cow1, Entity cow2,
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

        // Auto-attach to player so they can immediately assign to a house without an
        // extra pickup click. Only if hands are free — don't drop a helper they were
        // already carrying.
        if (state.HasComponent<PlayerStateComponent>(playerEntity))
        {
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            if (ps.FollowingHelper == Entity.Null)
                ps.FollowingHelper = babyHelper;
        }

        ref var gr2 = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
        gr2.HelpersSpawned++;
        int spawnedNow = gr2.HelpersSpawned;
        var gt = state.GetCustomData<IGameTime>();
        float hMin = gt != null ? gt.CurrentTick / 60f / 60f : -1;
        ILogger.Log($"[BreedHelperUnlock] Helper unlocked: #{spawnedNow} at breed #{breedCount} ({hMin:F1}m)!");
        return true;
    }

    private static int GetNextNeededHelper(EntityWorld state)
    {
        if (!CowSystemHelpers.TryGetGlobalResourcesEntity(state, out var grEntity)) return -1;
        int spawnedCount = state.GetComponent<GlobalResourcesComponent>(grEntity).HelpersSpawned;
        if (spawnedCount >= Balance.Helper.HelperUnlockBreeds.Length) return -1;
        return DefaultHelperRole;
    }
}
