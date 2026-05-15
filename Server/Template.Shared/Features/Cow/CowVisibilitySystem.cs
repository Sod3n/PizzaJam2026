using Deterministic.GameFramework.ECS;
using Template.Shared.Components;
using Template.Shared.Definitions;

namespace Template.Shared.Systems;

// Single source of truth for who is hidden during cow-related interactions:
//   - Hide player + target cow during Milking.Active/Exit phases
//   - Hide player + both love-house cows during the Breed phase
//   - Unhide cows when not actively breeding/milking and not currently depressed
//
// Depressed cows stay visible (but non-interactable) — the unhide forces them out of any
// breed-pen hide that lingered after a failed breed.
public class CowVisibilitySystem : ISystem
{
    public void Update(EntityWorld state)
    {
        UpdateActiveStateHide(state);
        UpdateUnhide(state);
    }

    private static void UpdateActiveStateHide(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<PlayerStateComponent>())
        {
            if (!state.HasComponent<StateComponent>(playerEntity)) continue;

            var sc = state.GetComponent<StateComponent>(playerEntity);
            if (!sc.IsEnabled) continue;

            var ps = state.GetComponent<PlayerStateComponent>(playerEntity);

            if (sc.Key == StateKeys.Milking && (sc.Phase == StatePhase.Active || sc.Phase == StatePhase.Exit))
            {
                state.HideEntity(playerEntity);
                if (state.HasComponent<CowComponent>(ps.InteractionTarget))
                    state.HideEntity(ps.InteractionTarget);
            }
            else if (sc.Key == StateKeys.Breed)
            {
                state.HideEntity(playerEntity);
                if (state.HasComponent<LoveHouseComponent>(ps.InteractionTarget))
                {
                    var lh = state.GetComponent<LoveHouseComponent>(ps.InteractionTarget);
                    if (lh.CowId1 != Entity.Null) state.HideEntity(lh.CowId1);
                    if (lh.CowId2 != Entity.Null) state.HideEntity(lh.CowId2);
                }
            }
        }
    }

    private static void UpdateUnhide(EntityWorld state)
    {
        foreach (var cowRef in state.Filter<CowArchetype>())
        {
            var cowEntity = cowRef.Entity;
            var cow = cowRef.Cow;

            if (cow.IsDepressed)
            {
                if (state.HasComponent<HiddenComponent>(cowEntity))
                    state.UnhideEntity(cowEntity);
            }
            else if (!cow.IsMilking)
            {
                if (state.HasComponent<HiddenComponent>(cowEntity) && !IsCowInActiveBreeding(state, cowEntity))
                    state.UnhideEntity(cowEntity);
            }
        }
    }

    private static bool IsCowInActiveBreeding(EntityWorld state, Entity cowEntity)
    {
        var houseId = state.GetComponent<CowComponent>(cowEntity).HouseId;
        if (houseId == Entity.Null || !state.HasComponent<LoveHouseComponent>(houseId)) return false;

        foreach (var playerEntity in state.Filter<PlayerStateComponent>())
        {
            if (!state.HasComponent<StateComponent>(playerEntity)) continue;
            var sc = state.GetComponent<StateComponent>(playerEntity);
            if (!sc.IsEnabled || sc.Key != StateKeys.Breed) continue;
            var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
            if (ps.InteractionTarget == houseId) return true;
        }
        return false;
    }
}
