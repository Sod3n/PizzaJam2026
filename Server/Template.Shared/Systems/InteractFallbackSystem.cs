using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Systems;

// Runs LAST (registered after all per-feature interaction systems). Anything still wearing
// an InteractRequestComponent at this point was clicked but no feature claimed it — show a
// BuildingInfo popup if the target is a known building, then clean up the marker.
//
// "Claim" means "produced real feedback (Success or MissingResource)". Silent precondition
// fails (e.g. cow is currently milking, house has no cow assigned) leave the marker for
// this fallback to handle.
public class InteractFallbackSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            string infoKey = GetBuildingInfoKey(state, req.Target);
            if (infoKey != null)
            {
                state.AddComponent(req.Target, new EnterStateComponent { Key = StateKeys.BuildingInfo, Param = infoKey, Age = 0 });
            }
            state.RemoveComponent<InteractRequestComponent>(playerEntity);
        }
    }

    private static string GetBuildingInfoKey(EntityWorld state, Entity entity)
    {
        if (state.HasComponent<SellPointComponent>(entity)) return StateKeys.InfoSellPoint;
        if (state.HasComponent<HouseComponent>(entity)) return StateKeys.InfoHouse;
        if (state.HasComponent<LoveHouseComponent>(entity)) return StateKeys.InfoLoveHouse;
        if (state.HasComponent<CarrotFarmComponent>(entity)) return StateKeys.InfoCarrotFarm;
        if (state.HasComponent<AppleOrchardComponent>(entity)) return StateKeys.InfoAppleOrchard;
        if (state.HasComponent<MushroomCaveComponent>(entity)) return StateKeys.InfoMushroomCave;
        if (state.HasComponent<HelperAssistantComponent>(entity)) return StateKeys.InfoHelperAssistant;
        if (state.HasComponent<DecorationComponent>(entity)) return StateKeys.InfoDecoration;
        if (state.HasComponent<WarehouseComponent>(entity)) return StateKeys.InfoWarehouse;
        return null;
    }
}
