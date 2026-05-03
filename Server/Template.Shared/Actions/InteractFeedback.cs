using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Actions;

// Shared feedback emission for the per-feature interaction systems.
//
// Pattern:
//   On a match: feature system runs work, then calls Success / MissingResource.
//   On a non-match: feature system SKIPS WITHOUT REMOVING the marker — the fallback
//   system handles unclaimed markers (BuildingInfo popup or silent cleanup).
//
// "Claim" the marker only when you produce real feedback. Silent precondition fails
// (e.g. "cow currently milking") should leave the marker for the fallback.
public static class InteractFeedback
{
    public const int NotEnoughResourceDurationTicks = 2;

    /// Successful interaction — bounce/squish on the target, optional GainedResource popup on the player.
    public static void Success(Context ctx, Entity playerEntity, Entity target, string gainedResource = null)
    {
        ctx.State.AddComponent(target, new EnterStateComponent { Key = StateKeys.Interacted, Param = gainedResource ?? "", Age = 0 });
        if (!string.IsNullOrEmpty(gainedResource))
            ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.GainedResource, Param = gainedResource, Age = 0 });
        ctx.State.RemoveComponent<InteractRequestComponent>(playerEntity);
    }

    /// Missing-resource feedback — sets the player into NotEnoughResource state and shows the
    /// reason on the target.
    public static void MissingResource(Context ctx, Entity playerEntity, Entity target, string resourceKey)
    {
        if (ctx.State.HasComponent<StateComponent>(playerEntity))
        {
            ref var sc = ref ctx.State.GetComponent<StateComponent>(playerEntity);
            sc.Key = StateKeys.NotEnoughResource;
            sc.CurrentTime = 0;
            sc.MaxTime = NotEnoughResourceDurationTicks;
            sc.IsEnabled = true;
        }
        ctx.State.AddComponent(target, new EnterStateComponent { Key = StateKeys.NotEnoughResource, Param = resourceKey, Age = 0 });
        ctx.State.RemoveComponent<InteractRequestComponent>(playerEntity);
    }

    public static string FoodTypeToKey(int foodType) => foodType switch
    {
        FoodType.Grass => StateKeys.Grass,
        FoodType.Carrot => StateKeys.Carrot,
        FoodType.Apple => StateKeys.Apple,
        FoodType.Mushroom => StateKeys.Mushroom,
        _ => StateKeys.Food
    };

    public static string MilkProductToKey(int milkProduct) => milkProduct switch
    {
        MilkProduct.Milk => StateKeys.Milk,
        MilkProduct.CarrotMilkshake => StateKeys.CarrotMilkshake,
        MilkProduct.VitaminMix => StateKeys.VitaminMix,
        MilkProduct.PurplePotion => StateKeys.PurplePotion,
        _ => StateKeys.Milk
    };

    public static Entity GetGlobalResourcesEntity(EntityWorld state)
    {
        foreach (var entity in state.Filter<GlobalResourcesComponent>())
            return entity;
        return Entity.Null;
    }
}
