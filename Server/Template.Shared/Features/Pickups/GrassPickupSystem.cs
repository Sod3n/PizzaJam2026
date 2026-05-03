using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Shared.Systems;

// Click on grass/food → pick up a single unit. Main player drops it into global resources;
// helper-player adds it to their personal bag (no-op silently if their bag is full).
public class GrassPickupSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        foreach (var playerEntity in state.Filter<InteractRequestComponent>())
        {
            var req = state.GetComponent<InteractRequestComponent>(playerEntity);
            var foodEntity = req.Target;
            if (!state.HasComponent<GrassComponent>(foodEntity)) continue;

            if (state.HasComponent<HelperPlayerComponent>(playerEntity))
                TryHelperPlayerPickup(state, playerEntity, foodEntity);
            else
                TryMainPlayerPickup(state, playerEntity, foodEntity);
        }
    }

    private static void TryMainPlayerPickup(EntityWorld state, Entity playerEntity, Entity foodEntity)
    {
        var ctx = state.Ctx(playerEntity);
        var grEntity = InteractFeedback.GetGlobalResourcesEntity(state);
        if (grEntity == Entity.Null) return;

        int foodType = state.GetComponent<GrassComponent>(foodEntity).FoodType;

        {
            ref var globalRes = ref state.GetComponent<GlobalResourcesComponent>(grEntity);
            ref var grass = ref state.GetComponent<GrassComponent>(foodEntity);
            grass.Durability -= 1;
            globalRes.AddFood(grass.FoodType, 1);
        }

        bool deleted = false;
        {
            var grass = state.GetComponent<GrassComponent>(foodEntity);
            if (grass.Durability <= 0)
            {
                state.DeleteEntity(foodEntity);
                deleted = true;
            }
        }

        string gainedKey = InteractFeedback.FoodTypeToKey(foodType);
        if (deleted)
        {
            state.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.GainedResource, Param = gainedKey, Age = 0 });
            state.RemoveComponent<InteractRequestComponent>(playerEntity);
        }
        else
        {
            InteractFeedback.Success(ctx, playerEntity, foodEntity, gainedKey);
        }
    }

    private static void TryHelperPlayerPickup(EntityWorld state, Entity playerEntity, Entity foodEntity)
    {
        var ctx = state.Ctx(playerEntity);

        {
            ref var hp = ref state.GetComponent<HelperPlayerComponent>(playerEntity);
            if (hp.IsBagFull()) return;
        }

        int foodType = state.GetComponent<GrassComponent>(foodEntity).FoodType;

        {
            ref var hp = ref state.GetComponent<HelperPlayerComponent>(playerEntity);
            ref var grass = ref state.GetComponent<GrassComponent>(foodEntity);
            grass.Durability -= 1;
            switch (grass.FoodType)
            {
                case FoodType.Grass: hp.BagGrass++; break;
                case FoodType.Carrot: hp.BagCarrot++; break;
                case FoodType.Apple: hp.BagApple++; break;
                case FoodType.Mushroom: hp.BagMushroom++; break;
            }
        }

        bool deleted = false;
        {
            var grass = state.GetComponent<GrassComponent>(foodEntity);
            if (grass.Durability <= 0)
            {
                state.DeleteEntity(foodEntity);
                deleted = true;
            }
        }

        string gainedKey = InteractFeedback.FoodTypeToKey(foodType);
        if (deleted)
        {
            state.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.GainedResource, Param = gainedKey, Age = 0 });
            state.RemoveComponent<InteractRequestComponent>(playerEntity);
        }
        else
        {
            InteractFeedback.Success(ctx, playerEntity, foodEntity, gainedKey);
        }
    }
}
