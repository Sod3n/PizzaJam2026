using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.DAR;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Player-on-player interact: shove the target a bit. If a helper-player clicks the main
// player, also dump the helper-player's bag into global storage.
//
// Match preconditions:
//   target has PlayerEntity
//   target != playerEntity
//   target does NOT have HelperPlayerComponent (those go through HelperPlayerExchangeSystem)
//
// Always claims via Success (push always applies, even with no velocity change).
public class PlayerVsPlayerSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;

            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var targetEntity = req.Target;
            if (targetEntity == playerEntity) continue;
            if (!state.HasComponent<PlayerEntity>(targetEntity)) continue;
            if (state.HasComponent<HelperPlayerComponent>(targetEntity)) continue;

            HandlePlayerPush(state, playerEntity, targetEntity);
        }
    }

    private static void HandlePlayerPush(EntityWorld state, Entity playerEntity, Entity targetEntity)
    {
        var ctx = state.Ctx(playerEntity);

        ApplyPlayerPush(ctx, playerEntity, targetEntity);

        bool isHelperPlayer = state.HasComponent<HelperPlayerComponent>(playerEntity);
        string gainedResource = null;

        if (isHelperPlayer)
        {
            var grEntity = InteractFeedback.GetGlobalResourcesEntity(state);
            if (grEntity != Entity.Null)
            {
                ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
                DumpHelperPlayerBagToGlobal(ctx, playerEntity, ref globalRes, out gainedResource);
            }
        }

        InteractFeedback.Success(ctx, playerEntity, targetEntity, gainedResource);
    }

    private const float PlayerPushImpulse = 6.0f;

    private static void ApplyPlayerPush(Context ctx, Entity from, Entity target)
    {
        if (!ctx.State.TryGetComponent<Transform2D>(from, out var fromTransform)) return;
        if (!ctx.State.TryGetComponent<Transform2D>(target, out var targetTransform)) return;
        if (!ctx.State.HasComponent<CharacterBody2D>(target)) return;

        var diff = targetTransform.Position - fromTransform.Position;
        Float distSq = diff.SqrMagnitude;
        if (distSq < (Float)0.0001f) return;

        Float dist = Float.Sqrt(distSq);
        var dir = diff / dist;
        ref var body = ref ctx.State.GetComponent<CharacterBody2D>(target);
        body.Velocity = body.Velocity + dir * (Float)PlayerPushImpulse;
    }

    private static bool DumpHelperPlayerBagToGlobal(Context ctx, Entity helperPlayerEntity, ref GlobalResourcesComponent globalRes, out string gainedResource)
    {
        gainedResource = null;
        ref var hp = ref ctx.State.GetComponent<HelperPlayerComponent>(helperPlayerEntity);
        if (!hp.HasAnyResources()) return false;

        if (hp.BagGrass > 0) gainedResource = StateKeys.Grass;
        else if (hp.BagCarrot > 0) gainedResource = StateKeys.Carrot;
        else if (hp.BagApple > 0) gainedResource = StateKeys.Apple;
        else if (hp.BagMushroom > 0) gainedResource = StateKeys.Mushroom;
        else if (hp.BagMilk > 0) gainedResource = StateKeys.Milk;
        else if (hp.BagCoins > 0) gainedResource = StateKeys.Coins;

        globalRes.AddFood(FoodType.Grass, hp.BagGrass);
        globalRes.AddFood(FoodType.Carrot, hp.BagCarrot);
        globalRes.AddFood(FoodType.Apple, hp.BagApple);
        globalRes.AddFood(FoodType.Mushroom, hp.BagMushroom);
        globalRes.AddMilkProduct(MilkProduct.Milk, hp.BagMilk);
        globalRes.Coins += hp.BagCoins;
        hp.ClearBag();
        return true;
    }
}
