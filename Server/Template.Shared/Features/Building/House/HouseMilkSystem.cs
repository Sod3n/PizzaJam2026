using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;
using Template.Shared;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;
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

            var houseEntity = state.GetComponent<InteractRequestComponent>(playerEntity).Target;
            if (!state.TryResolve<HouseArchetype>(houseEntity, out var houseRef)) continue;
            if (state.HasComponent<HelperPlayerComponent>(playerEntity)) continue;

            if (state.GetComponent<PlayerStateComponent>(playerEntity).FollowingCow != Entity.Null) continue;

            var cowEntity = houseRef.CowSlot;
            if (cowEntity == Entity.Null || !state.HasComponent<CowComponent>(cowEntity)) continue;

            TryStartMilking(state, playerEntity, houseRef, cowEntity);
        }
    }

    private static void TryStartMilking(EntityWorld state, Entity playerEntity, HouseRef houseRef, Entity cowEntity)
    {
        var ctx = state.Ctx(playerEntity);
        var houseEntity = houseRef.Entity;

        if (!CowReadyToMilk(ctx, playerEntity, houseEntity, cowEntity)) return;
        if (!ResourcesAvailable(ctx, state, playerEntity, houseEntity, cowEntity)) return;

        BeginMilking(state, playerEntity, cowEntity);

        ILogger.Log($"[HouseMilkSystem] Player {playerEntity.Id} milking cow {cowEntity.Id} at house {houseEntity.Id}");
        InteractFeedback.Success(ctx, playerEntity, houseEntity);
    }

    private static bool CowReadyToMilk(Context ctx, Entity playerEntity, Entity houseEntity, Entity cowEntity)
    {
        var cow = ctx.State.GetComponent<CowComponent>(cowEntity);
        if (cow.IsMilking) return false;
        if (cow.IsDepressed) return false;

        if (cow.Exhaust >= cow.MaxExhaust)
        {
            InteractFeedback.MissingResource(ctx, playerEntity, houseEntity, StateKeys.CowTired);
            return false;
        }

        if (cow.SelectedFood < 0 || cow.SelectedFood > FoodType.Mushroom)
        {
            InteractFeedback.MissingResource(ctx, playerEntity, houseEntity, InteractFeedback.FoodTypeToKey(cow.SelectedFood));
            return false;
        }
        return true;
    }

    private static bool ResourcesAvailable(Context ctx, EntityWorld state, Entity playerEntity, Entity houseEntity, Entity cowEntity)
    {
        var grEntity = InteractFeedback.GetGlobalResourcesEntity(state);
        if (grEntity == Entity.Null) return false;

        int selectedFood = state.GetComponent<CowComponent>(cowEntity).SelectedFood;
        var globalRes = state.GetComponent<GlobalResourcesComponent>(grEntity);
        if (globalRes.GetFood(selectedFood) <= 0)
        {
            InteractFeedback.MissingResource(ctx, playerEntity, houseEntity, InteractFeedback.FoodTypeToKey(selectedFood));
            return false;
        }

        int prereq = FoodType.PrerequisiteProduct(selectedFood);
        if (prereq >= 0 && globalRes.GetMilkProduct(prereq) <= 0)
        {
            InteractFeedback.MissingResource(ctx, playerEntity, houseEntity, InteractFeedback.MilkProductToKey(prereq));
            return false;
        }
        return true;
    }

    private static void BeginMilking(EntityWorld state, Entity playerEntity, Entity cowEntity)
    {
        state.GetComponent<CowComponent>(cowEntity).IsMilking = true;

        StatePhase phase;
        {
            ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
            StateDefinitions.Begin(ref sc, StateKeys.Milking);
            phase = sc.Phase;
        }
        state.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Milking, Phase = phase, Age = 0 });

        ref var ps = ref state.GetComponent<PlayerStateComponent>(playerEntity);
        ps.InteractionTarget = cowEntity;
        if (state.TryGetComponent<Transform2D>(playerEntity, out var pt))
            ps.ReturnPosition = pt.Position;
    }
}
