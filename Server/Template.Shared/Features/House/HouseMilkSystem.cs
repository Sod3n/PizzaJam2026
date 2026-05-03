using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Owns the "click a house with no following cow → start milking the cow inside" interaction.
//
// Match preconditions (mutually exclusive with HouseAssign / HouseAssignHelper / HouseHelperPlayer):
//   target has HouseComponent
//   player is NOT a helper-player
//   player has no FollowingCow
//   the house has a cow assigned (CowId set, has CowComponent)
//
// On match, run resource gates (cow not already milking/depressed/exhausted, food + prerequisite
// product available). On full success, set the player into Milking state; emit success feedback.
// On a resource shortfall, emit MissingResource feedback. Silent skips (cow milking/depressed)
// leave the marker for InteractFallbackSystem to handle (BuildingInfo popup).
public class HouseMilkSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;
            if (!state.HasComponent<StateComponent>(playerEntity)) continue;

            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var houseEntity = req.Target;
            if (!state.HasComponent<HouseComponent>(houseEntity)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            var ps = state.GetComponent<PlayerStateComponent>(playerEntity);
            if (ps.FollowingCow != Entity.Null) continue;

            var house = state.GetComponent<HouseComponent>(houseEntity);
            var cowEntity = house.CowId;
            if (cowEntity == Entity.Null || !state.HasComponent<CowComponent>(cowEntity)) continue;

            TryStartMilking(state, playerEntity, houseEntity, cowEntity);
        }
    }

    private static void TryStartMilking(EntityWorld state, Entity playerEntity, Entity houseEntity, Entity cowEntity)
    {
        var ctx = state.Ctx(playerEntity);

        var cow = state.GetComponent<CowComponent>(cowEntity);
        if (cow.IsMilking) return;        // silent — fallback shows InfoHouse
        if (cow.IsDepressed) return;      // silent — fallback shows InfoHouse

        if (cow.Exhaust >= cow.MaxExhaust)
        {
            InteractFeedback.MissingResource(ctx, playerEntity, houseEntity, StateKeys.CowTired);
            return;
        }

        int selectedFood = cow.SelectedFood;
        int cowMaxTier = FoodType.MaxTier(cow.PreferredFood);
        if (selectedFood < 0 || selectedFood > cowMaxTier)
        {
            InteractFeedback.MissingResource(ctx, playerEntity, houseEntity, InteractFeedback.FoodTypeToKey(selectedFood));
            return;
        }

        var grEntity = InteractFeedback.GetGlobalResourcesEntity(state);
        if (grEntity == Entity.Null) return;

        var globalRes = state.GetComponent<GlobalResourcesComponent>(grEntity);
        if (globalRes.GetFood(selectedFood) <= 0)
        {
            InteractFeedback.MissingResource(ctx, playerEntity, houseEntity, InteractFeedback.FoodTypeToKey(selectedFood));
            return;
        }

        int prereq = FoodType.PrerequisiteProduct(selectedFood);
        if (prereq >= 0 && globalRes.GetMilkProduct(prereq) <= 0)
        {
            InteractFeedback.MissingResource(ctx, playerEntity, houseEntity, InteractFeedback.MilkProductToKey(prereq));
            return;
        }

        // All gates passed — start the milking state.
        {
            ref var c = ref state.GetComponent<CowComponent>(cowEntity);
            c.IsMilking = true;
        }
        StatePhase phase;
        {
            ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
            StateDefinitions.Begin(ref sc, StateKeys.Milking);
            phase = sc.Phase;
        }
        state.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Milking, Phase = phase, Age = 0 });

        Vector2 returnPos = default;
        bool hasReturnPos = state.HasComponent<Transform2D>(playerEntity);
        if (hasReturnPos) returnPos = state.GetComponent<Transform2D>(playerEntity).Position;
        {
            ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            ps.InteractionTarget = cowEntity;
            if (hasReturnPos) ps.ReturnPosition = returnPos;
        }

        ILogger.Log($"[HouseMilkSystem] Player {playerEntity.Id} milking cow {cowEntity.Id} at house {houseEntity.Id}");
        InteractFeedback.Success(ctx, playerEntity, houseEntity);
    }
}
