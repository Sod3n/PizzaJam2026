using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Physics2D.Components;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Utils.Logging;

namespace Template.Shared.Actions;

public class InteractActionService : ActionService<InteractAction, PlayerEntity>
{
    public const int NotEnoughResourceDurationTicks = 2;

    protected override void ExecuteProcess(InteractAction action, ref PlayerEntity playerComp, Context ctx)
    {
        Entity playerEntity = ctx.Entity;

        if (playerComp.UserId != action.UserId) return;
        if (!ctx.State.HasComponent<PlayerStateComponent>(playerEntity)) return;
        if (!ctx.State.HasComponent<StateComponent>(playerEntity)) return;

        ref var playerState = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
        ref var sc = ref ctx.State.GetComponent<StateComponent>(playerEntity);

        // If actively milking, handle milk clicks
        if (sc.Key == StateKeys.Milking && sc.Phase == StatePhase.Active && sc.IsEnabled)
        {
            HandleMilkingClick(ctx, playerEntity, ref playerState, ref sc);
            return;
        }

        // If actively breeding, handle breed clicks
        if (sc.Key == StateKeys.Breed && sc.Phase == StatePhase.Active && sc.IsEnabled)
        {
            HandleBreedingClick(ctx, playerEntity, ref playerState, ref sc);
            return;
        }

        // Skip interaction if in any other active state
        if (sc.IsEnabled) return;

        // Find nearest interactable from interaction zone overlaps
        Entity nearestTarget = FindNearestFromZone(ctx, playerEntity, ref playerState);
        if (nearestTarget == Entity.Null) return;

        Entity globalResEntity = GetGlobalResourcesEntity(ctx);
        if (globalResEntity == Entity.Null) return;
        ref var globalRes = ref ctx.State.GetComponent<GlobalResourcesComponent>(globalResEntity);

        bool success = false;
        string missingResource = null;
        string gainedResource = null;
        Entity interactedTarget = nearestTarget;

        // Helper interaction → transfer resources to helper
        if (ctx.State.HasComponent<HelperComponent>(nearestTarget))
        {
            success = HandleHelperInteraction(ctx, nearestTarget, ref globalRes);
            if (success)
            {
                ctx.State.AddComponent(nearestTarget, new EnterStateComponent { Key = StateKeys.Interacted, Param = "", Age = 0 });
            }
        }
        // Cow interaction → always tame (add to follow chain)
        else if (ctx.State.HasComponent<CowComponent>(nearestTarget))
        {
            success = HandleCowTame(ctx, playerEntity, nearestTarget, ref playerState, ref sc);
            if (success) return;
        }
        // House interaction
        else if (ctx.State.HasComponent<HouseComponent>(nearestTarget))
        {
            if (playerState.FollowingCow != Entity.Null)
            {
                // Have following cows → assign first cow to house
                success = HandleHouseAssign(ctx, playerEntity, nearestTarget, ref playerState, ref sc);
                if (success) return;
            }
            else
            {
                // No following cows → milk the cow in this house
                ref var house = ref ctx.State.GetComponent<HouseComponent>(nearestTarget);
                if (house.CowId != Entity.Null && ctx.State.HasComponent<CowComponent>(house.CowId))
                {
                    success = HandleHouseMilk(ctx, playerEntity, nearestTarget, house.CowId, ref playerState, ref sc, ref globalRes, out missingResource);
                    if (success) return;
                }
            }
        }
        // Love House interaction
        else if (ctx.State.HasComponent<LoveHouseComponent>(nearestTarget))
        {
            ref var lh = ref ctx.State.GetComponent<LoveHouseComponent>(nearestTarget);
            bool bothFull = lh.CowId1 != Entity.Null && lh.CowId2 != Entity.Null;
            if (bothFull)
            {
                // Both slots full → start breeding
                success = HandleLoveHouseStartBreed(ctx, playerEntity, nearestTarget, ref playerState, ref sc, ref globalRes, out missingResource);
                if (success) return;
            }
            else if (playerState.FollowingCow != Entity.Null)
            {
                // Has following cows and love house has empty slot → assign cow
                success = HandleLoveHouseAssign(ctx, playerEntity, nearestTarget, ref playerState, ref sc, ref globalRes, out missingResource);
                if (success) return;
            }
        }
        else if (ctx.State.HasComponent<FoodSignComponent>(nearestTarget))
        {
            success = HandleFoodSignInteraction(ctx, nearestTarget);
        }
        else if (ctx.State.HasComponent<GrassComponent>(nearestTarget))
        {
            var foodType = ctx.State.GetComponent<GrassComponent>(nearestTarget).FoodType;
            success = HandleFoodInteraction(ctx, nearestTarget, ref globalRes);
            if (success) gainedResource = FoodTypeToKey(foodType);
        }
        else if (ctx.State.HasComponent<SellPointComponent>(nearestTarget))
        {
            success = HandleSellPointInteraction(ctx, ref globalRes, out missingResource);
            if (success) gainedResource = StateKeys.Coins;
        }
        else if (ctx.State.HasComponent<LandComponent>(nearestTarget))
        {
            success = HandleLandInteraction(ctx, playerEntity, nearestTarget, ref globalRes, out missingResource);
        }
        else if (ctx.State.HasComponent<FinalStructureComponent>(nearestTarget))
        {
            success = HandleFinalStructureInteraction(ctx, nearestTarget, ref globalRes, out missingResource);
        }

        if (success)
        {
            ctx.State.AddComponent(interactedTarget, new EnterStateComponent { Key = StateKeys.Interacted, Param = gainedResource ?? "", Age = 0 });
            if (gainedResource != null)
                ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.GainedResource, Param = gainedResource, Age = 0 });
            ILogger.Log($"[InteractActionService] Marked {interactedTarget.Id} as successfully interacted, gained: {gainedResource}");
        }
        else if (missingResource != null)
        {
            sc.Key = StateKeys.NotEnoughResource;
            sc.CurrentTime = 0;
            sc.MaxTime = NotEnoughResourceDurationTicks;
            sc.IsEnabled = true;

            // Show not-enough popup above target, not above player
            ctx.State.AddComponent(nearestTarget, new EnterStateComponent { Key = StateKeys.NotEnoughResource, Param = missingResource, Age = 0 });
            ILogger.Log($"[InteractActionService] Not enough {missingResource} for interaction with {nearestTarget.Id}");
        }
    }

    private void HandleMilkingClick(Context ctx, Entity playerEntity, ref PlayerStateComponent playerState, ref StateComponent sc)
    {
        var cowEntity = playerState.InteractionTarget;
        if (cowEntity == Entity.Null || !ctx.State.HasComponent<CowComponent>(cowEntity)) return;

        Entity globalResEntity = GetGlobalResourcesEntity(ctx);
        if (globalResEntity == Entity.Null) return;
        ref var globalRes = ref ctx.State.GetComponent<GlobalResourcesComponent>(globalResEntity);
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);

        // Determine which food to use from the house's food sign selection
        int foodToUse = FoodType.Grass; // default
        if (cow.HouseId != Entity.Null && ctx.State.HasComponent<HouseComponent>(cow.HouseId))
        {
            var house = ctx.State.GetComponent<HouseComponent>(cow.HouseId);
            foodToUse = house.SelectedFood;
        }

        int exhaustPerClick = System.Math.Max(1, playerState.ClickMultiplier);
        bool produced = InteractionLogic.MilkCow(ctx.State, cowEntity, foodToUse, exhaustPerClick, out bool cowDone);

        Entity target = cowEntity;
        cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
        if (cow.HouseId != Entity.Null)
            target = cow.HouseId;

        ctx.State.AddComponent(target, new EnterStateComponent { Key = StateKeys.Interacted, Param = produced ? "milk_ok" : "milk_fail", Age = 0 });

        if (cowDone)
        {
            StateDefinitions.BeginExit(ref sc);
            ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = sc.Key, Phase = sc.Phase, Age = 0 });
        }
    }

    private void HandleBreedingClick(Context ctx, Entity playerEntity, ref PlayerStateComponent playerState, ref StateComponent sc)
    {
        var loveHouseEntity = playerState.InteractionTarget;
        if (loveHouseEntity == Entity.Null || !ctx.State.HasComponent<LoveHouseComponent>(loveHouseEntity)) return;

        ref var loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);
        loveHouse.BreedProgress++;

        // Compute breed luck for visual heart feedback
        int heartPercent = 50;
        if (ctx.State.HasComponent<CowComponent>(loveHouse.CowId1) && ctx.State.HasComponent<CowComponent>(loveHouse.CowId2))
        {
            var c1 = ctx.State.GetComponent<CowComponent>(loveHouse.CowId1);
            var c2 = ctx.State.GetComponent<CowComponent>(loveHouse.CowId2);
            bool sameTier = c1.PreferredFood == c2.PreferredFood;
            if (sameTier)
                heartPercent = loveHouse.SameTierBreedCount >= LoveHouseComponent.GuaranteedUpgradeEvery - 1 ? 90 : 70;
            else
            {
                int tierGap = System.Math.Abs(c1.PreferredFood - c2.PreferredFood);
                heartPercent = tierGap switch { 1 => 45, 2 => 25, _ => 15 };
            }
        }

        ctx.State.AddComponent(loveHouseEntity, new EnterStateComponent { Key = StateKeys.Interacted, Param = $"breed_{heartPercent}", Age = 0 });
        ILogger.Log($"[InteractActionService] Breed click {loveHouse.BreedProgress}/{loveHouse.BreedCost} at love house {loveHouseEntity.Id}");

        if (loveHouse.BreedProgress >= loveHouse.BreedCost)
        {
            StateDefinitions.BeginExit(ref sc);
            ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = sc.Key, Phase = sc.Phase, Age = 0 });
        }
    }

    private bool HandleCowTame(Context ctx, Entity playerEntity, Entity cowEntity, ref PlayerStateComponent playerState, ref StateComponent sc)
    {
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);

        // Clicking a cow that's following us → dismiss it (stop following)
        if (cow.FollowingPlayer == playerEntity)
        {
            // Find next cow in chain (the one following this cow)
            Entity next = Entity.Null;
            foreach (var ce in ctx.State.Filter<CowComponent>())
            {
                if (ce == cowEntity) continue;
                if (ctx.State.GetComponent<CowComponent>(ce).FollowTarget == cowEntity
                    && ctx.State.GetComponent<CowComponent>(ce).FollowingPlayer == playerEntity)
                { next = ce; break; }
            }

            if (playerState.FollowingCow == cowEntity)
            {
                // First in chain: promote next
                playerState.FollowingCow = next;
                if (next != Entity.Null)
                {
                    ref var nc = ref ctx.State.GetComponent<CowComponent>(next);
                    nc.FollowTarget = playerEntity;
                }
            }
            else
            {
                // Mid-chain: relink next to follow what this cow was following
                Entity myTarget = cow.FollowTarget;
                if (next != Entity.Null)
                {
                    ref var nc = ref ctx.State.GetComponent<CowComponent>(next);
                    nc.FollowTarget = myTarget;
                }
            }

            cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
            cow.FollowingPlayer = Entity.Null;
            cow.FollowTarget = Entity.Null;
            ILogger.Log($"[InteractActionService] Player {playerEntity.Id} dismissed cow {cowEntity.Id} from follow chain");
            return true;
        }

        // Can't tame a cow that's being milked, depressed, or already following someone
        if (cow.IsMilking) return false;
        if (cow.IsDepressed) return false;
        if (cow.FollowingPlayer != Entity.Null) return false;

        StateDefinitions.Begin(ref sc, StateKeys.Taming);
        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Taming, Phase = sc.Phase, Age = 0 });
        ctx.State.AddComponent(cowEntity, new EnterStateComponent { Key = StateKeys.Interacted, Age = 0 });

        playerState.InteractionTarget = cowEntity;

        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} taming cow {cowEntity.Id}");
        return true;
    }

    private bool HandleHouseMilk(Context ctx, Entity playerEntity, Entity houseEntity, Entity cowEntity, ref PlayerStateComponent playerState, ref StateComponent sc, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
        ref var house = ref ctx.State.GetComponent<HouseComponent>(houseEntity);

        if (cow.IsMilking) return false;
        if (cow.IsDepressed) return false;
        if (globalRes.GetFood(house.SelectedFood) <= 0 || cow.Exhaust >= cow.MaxExhaust)
        {
            missingResource = FoodTypeToKey(house.SelectedFood);
            return false;
        }

        cow.IsMilking = true;

        StateDefinitions.Begin(ref sc, StateKeys.Milking);
        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Milking, Phase = sc.Phase, Age = 0 });

        playerState.InteractionTarget = cowEntity;

        if (ctx.State.HasComponent<Transform2D>(playerEntity))
            playerState.ReturnPosition = ctx.State.GetComponent<Transform2D>(playerEntity).Position;

        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} milking cow {cowEntity.Id} at house {houseEntity.Id}");
        return true;
    }

    private bool IsCowInChain(Context ctx, Entity firstCow, Entity target)
    {
        var current = firstCow;
        int safety = 0;
        while (current != Entity.Null && safety < 100)
        {
            if (current == target) return true;
            if (!ctx.State.HasComponent<CowComponent>(current)) break;
            // Walk the chain: find the next cow whose FollowTarget is this cow
            Entity next = Entity.Null;
            foreach (var cowEntity in ctx.State.Filter<CowComponent>())
            {
                ref var c = ref ctx.State.GetComponent<CowComponent>(cowEntity);
                if (c.FollowTarget == current)
                {
                    next = cowEntity;
                    break;
                }
            }
            current = next;
            safety++;
        }
        return false;
    }

    private Entity FindNearestFromZone(Context ctx, Entity playerEntity, ref PlayerStateComponent playerState)
    {
        var zoneEntity = playerState.InteractionZone;
        if (zoneEntity == Entity.Null || !ctx.State.HasComponent<Area2D>(zoneEntity)) return Entity.Null;

        ref var area = ref ctx.State.GetComponent<Area2D>(zoneEntity);
        if (!area.HasOverlappingBodies) return Entity.Null;

        var playerPos = ctx.State.GetComponent<Transform2D>(playerEntity).Position;
        Entity nearest = Entity.Null;
        Float minDistSq = 999999f;

        for (int i = 0; i < area.OverlappingEntities.Count; i++)
        {
            var entity = new Entity(area.OverlappingEntities[i]);
            if (entity == playerEntity) continue;
            if (entity == zoneEntity) continue;
            if (!ctx.State.HasComponent<Transform2D>(entity)) continue;

            var pos = ctx.State.GetComponent<Transform2D>(entity).Position;
            var distSq = Vector2.DistanceSquared(playerPos, pos);

            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                nearest = entity;
            }
        }

        return nearest;
    }

    private bool HandleFoodSignInteraction(Context ctx, Entity signEntity)
    {
        ref var sign = ref ctx.State.GetComponent<FoodSignComponent>(signEntity);

        // Cycle: Grass → Carrot → Apple → Mushroom → Grass
        sign.SelectedFood = (sign.SelectedFood + 1) % 4;

        // Also update the linked house's SelectedFood
        if (sign.HouseId != Entity.Null && ctx.State.HasComponent<HouseComponent>(sign.HouseId))
        {
            ref var house = ref ctx.State.GetComponent<HouseComponent>(sign.HouseId);
            house.SelectedFood = sign.SelectedFood;
        }

        ILogger.Log($"[InteractActionService] Food sign {signEntity.Id} cycled to food type {sign.SelectedFood}");
        return true;
    }

    private bool HandleFoodInteraction(Context ctx, Entity foodEntity, ref GlobalResourcesComponent globalRes)
    {
        ref var grass = ref ctx.State.GetComponent<GrassComponent>(foodEntity);
        grass.Durability -= 1;
        globalRes.AddFood(grass.FoodType, 1);

        if (grass.Durability <= 0)
        {
            ctx.State.DeleteEntity(foodEntity);
            return false;
        }
        return true;
    }

    private bool HandleHouseAssign(Context ctx, Entity playerEntity, Entity houseEntity, ref PlayerStateComponent playerState, ref StateComponent sc)
    {
        StateDefinitions.Begin(ref sc, StateKeys.Assign);
        playerState.InteractionTarget = houseEntity;

        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Assign, Phase = sc.Phase, Age = 0 });

        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} assigning cow {playerState.FollowingCow.Id} to house {houseEntity.Id}");
        return true;
    }

    private bool HandleLoveHouseAssign(Context ctx, Entity playerEntity, Entity loveHouseEntity, ref PlayerStateComponent playerState, ref StateComponent sc, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;

        var cowToAssign = playerState.FollowingCow;
        if (cowToAssign == Entity.Null) return false;

        ref var loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);

        // Check if love house already has 2 cows (full)
        if (loveHouse.CowId1 != Entity.Null && loveHouse.CowId2 != Entity.Null) return false;

        // Find next cow in chain (to promote after removing first)
        Entity nextCow = Entity.Null;
        foreach (var ce in ctx.State.Filter<CowComponent>())
        {
            var c = ctx.State.GetComponent<CowComponent>(ce);
            if (c.FollowTarget == cowToAssign && c.FollowingPlayer != Entity.Null)
            { nextCow = ce; break; }
        }

        // Remove cow from follow chain, preserve previous house for return after breeding
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowToAssign);
        if (cow.PreviousHouseId == Entity.Null)
            cow.PreviousHouseId = cow.HouseId;
        cow.FollowingPlayer = Entity.Null;
        cow.FollowTarget = Entity.Null;
        cow.HouseId = loveHouseEntity;

        if (ctx.State.HasComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowToAssign))
        {
            ref var body = ref ctx.State.GetComponent<Deterministic.GameFramework.Physics2D.Components.CharacterBody2D>(cowToAssign);
            body.Velocity = Deterministic.GameFramework.Types.Vector2.Zero;
        }

        // Re-get love house ref after touching other components
        loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);

        // Assign to first empty slot, position cow on corresponding side
        bool isFirstSlot = loveHouse.CowId1 == Entity.Null;
        if (isFirstSlot)
            loveHouse.CowId1 = cowToAssign;
        else
            loveHouse.CowId2 = cowToAssign;

        // Cow will walk to love house via CowFollowSystem navigation

        // Promote next cow in chain
        if (nextCow != Entity.Null)
        {
            ref var nextCowComp = ref ctx.State.GetComponent<CowComponent>(nextCow);
            nextCowComp.FollowTarget = playerEntity;
            playerState.FollowingCow = nextCow;
        }
        else
        {
            playerState.FollowingCow = Entity.Null;
        }

        // Show interacted feedback for assignment
        ctx.State.AddComponent(loveHouseEntity, new EnterStateComponent { Key = StateKeys.Interacted, Age = 0 });
        ILogger.Log($"[InteractActionService] Assigned cow {cowToAssign.Id} to love house {loveHouseEntity.Id}");

        return true;
    }

    private bool HandleLoveHouseStartBreed(Context ctx, Entity playerEntity, Entity loveHouseEntity, ref PlayerStateComponent playerState, ref StateComponent sc, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;

        ref var loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);

        // Count cows vs houses to check room for calf
        int cowCount = 0;
        foreach (var _ in ctx.State.Filter<CowComponent>())
            cowCount++;
        int houseCount = 0;
        foreach (var _ in ctx.State.Filter<HouseComponent>())
            houseCount++;

        if (cowCount >= houseCount)
        {
            missingResource = StateKeys.Houses;
            return false;
        }

        // Set breed cost and heart visual feedback based on cow exhaust/tier values
        int breedCost = 5;
        int heartPercent = 50;
        loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);
        if (ctx.State.HasComponent<CowComponent>(loveHouse.CowId1) && ctx.State.HasComponent<CowComponent>(loveHouse.CowId2))
        {
            var c1 = ctx.State.GetComponent<CowComponent>(loveHouse.CowId1);
            var c2 = ctx.State.GetComponent<CowComponent>(loveHouse.CowId2);
            breedCost = System.Math.Max(3, (c1.MaxExhaust + c2.MaxExhaust) / 2);

            bool sameTier = c1.PreferredFood == c2.PreferredFood;
            if (sameTier)
                heartPercent = loveHouse.SameTierBreedCount >= LoveHouseComponent.GuaranteedUpgradeEvery - 1 ? 95 : 85;
            else
            {
                int tierGap = System.Math.Abs(c1.PreferredFood - c2.PreferredFood);
                heartPercent = tierGap switch { 1 => 45, 2 => 25, _ => 15 };

                // Pre-roll fail check (same logic as CowSystem.HandleLoveHouseBreedComplete)
                var gameTime = ctx.State.GetCustomData<IGameTime>();
                uint breedSeed = (uint)((loveHouse.CowId1.Id * 7919 + loveHouse.CowId2.Id * 104729) ^ (gameTime?.CurrentTick ?? 0));
                var breedRandom = new DeterministicRandom(breedSeed);
                int failChance = tierGap switch { 1 => 50, 2 => 75, _ => 90 };
                bool willFail = breedRandom.NextInt(100) < failChance;
                if (willFail)
                    breedCost *= 2;
            }
        }
        loveHouse = ref ctx.State.GetComponent<LoveHouseComponent>(loveHouseEntity);
        loveHouse.BreedProgress = 0;
        loveHouse.BreedCost = breedCost;
        loveHouse.HeartPercent = heartPercent;

        StateDefinitions.Begin(ref sc, StateKeys.Breed);
        playerState.InteractionTarget = loveHouseEntity;

        if (ctx.State.HasComponent<Transform2D>(playerEntity))
            playerState.ReturnPosition = ctx.State.GetComponent<Transform2D>(playerEntity).Position;

        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Breed, Phase = sc.Phase, Age = 0 });

        ILogger.Log($"[InteractActionService] Started breeding at love house {loveHouseEntity.Id}, cost={breedCost}");
        return true;
    }

    private bool HandleCrossbreed(Context ctx, Entity playerEntity, Entity targetCow, ref PlayerStateComponent playerState, ref StateComponent sc, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;

        ref var targetCowComp = ref ctx.State.GetComponent<CowComponent>(targetCow);

        // Can't breed with a cow that's being milked or following someone else
        if (targetCowComp.IsMilking) return false;
        if (targetCowComp.FollowingPlayer != Entity.Null) return false;

        // Count total cows and houses to check if there's room
        int cowCount = 0;
        foreach (var _ in ctx.State.Filter<CowComponent>())
            cowCount++;

        int houseCount = 0;
        foreach (var _ in ctx.State.Filter<HouseComponent>())
            houseCount++;

        if (cowCount >= houseCount)
        {
            missingResource = StateKeys.Houses;
            return false;
        }

        StateDefinitions.Begin(ref sc, StateKeys.Breed);
        playerState.InteractionTarget = targetCow;

        ctx.State.AddComponent(playerEntity, new EnterStateComponent { Key = StateKeys.Breed, Phase = sc.Phase, Age = 0 });

        ILogger.Log($"[InteractActionService] Player {playerEntity.Id} breeding cow {playerState.FollowingCow.Id} with {targetCow.Id}");
        return true;
    }

    private bool HandleSellPointInteraction(Context ctx, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;
        int clickMult = 1;
        if (ctx.State.HasComponent<PlayerStateComponent>(ctx.Entity))
            clickMult = System.Math.Max(1, ctx.State.GetComponent<PlayerStateComponent>(ctx.Entity).ClickMultiplier);

        int coins = InteractionLogic.SellFromGlobal(ctx.State, clickMult);
        if (coins > 0) return true;
        missingResource = StateKeys.Milk;
        return false;
    }

    private bool HandleLandInteraction(Context ctx, Entity playerEntity, Entity landEntity, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;

        if (globalRes.Coins > 0)
        {
            var ps = ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
            int coinsPerClick = System.Math.Max(1, ps.ClickMultiplier);
            int coins = System.Math.Min(coinsPerClick, globalRes.Coins);

            int deposited = InteractionLogic.DepositToLand(ctx.State, landEntity, coins, leaveOneForPlayer: false, out bool landComplete);
            globalRes.Coins -= deposited;

            if (landComplete)
            {
                var transform = ctx.State.GetComponent<Transform2D>(landEntity);
                var position = transform.Position;
                var land = ctx.State.GetComponent<LandComponent>(landEntity);
                var landType = land.Type;
                int gridX = land.Arm;
                int gridY = land.Ring;
                ctx.State.DeleteEntity(landEntity);

                CompleteLandBuilding(ctx, position, landType, gridX, gridY);
                return false;
            }
            return deposited > 0;
        }
        missingResource = StateKeys.Coins;
        return false;
    }

    private static void DestroyNearbyProps(Context ctx, Vector2 position, float radius)
    {
        float radiusSq = radius * radius;
        var toDelete = new System.Collections.Generic.List<Entity>();
        foreach (var entity in ctx.State.Filter<PropComponent>())
        {
            var propPos = ctx.State.GetComponent<Transform2D>(entity).Position;
            float dx = (float)(propPos.X - position.X);
            float dy = (float)(propPos.Y - position.Y);
            if (dx * dx + dy * dy < radiusSq)
                toDelete.Add(entity);
        }
        foreach (var entity in toDelete)
            ctx.State.DeleteEntity(entity);
    }

    private bool HandleFinalStructureInteraction(Context ctx, Entity finalEntity, ref GlobalResourcesComponent globalRes, out string missingResource)
    {
        missingResource = null;
        if (globalRes.Coins <= 0)
        {
            missingResource = StateKeys.Coins;
            return false;
        }

        ref var final = ref ctx.State.GetComponent<FinalStructureComponent>(finalEntity);

        if (final.CurrentCoins >= final.Threshold) return false;

        int clickPower = 1;
        if (ctx.State.HasComponent<PlayerStateComponent>(ctx.Entity))
            clickPower = System.Math.Max(1, ctx.State.GetComponent<PlayerStateComponent>(ctx.Entity).ClickMultiplier);

        int deposit = System.Math.Min(clickPower, globalRes.Coins);
        deposit = System.Math.Min(deposit, final.Threshold - final.CurrentCoins);
        globalRes.Coins -= deposit;
        final.CurrentCoins += deposit;
        return true;
    }

    private bool HandleHelperInteraction(Context ctx, Entity helperEntity, ref GlobalResourcesComponent globalRes)
    {
        ref var helper = ref ctx.State.GetComponent<HelperComponent>(helperEntity);

        if (helper.Type == HelperType.Seller)
        {
            // Transfer milk products from global to seller's bag
            int transferred = 0;
            int capacity = helper.BagCapacity - helper.GetBagTotal();

            while (transferred < capacity && globalRes.Milk > 0) { globalRes.Milk--; helper.BagMilk++; transferred++; }
            while (transferred < capacity && globalRes.VitaminShake > 0) { globalRes.VitaminShake--; helper.BagVitaminShake++; transferred++; }
            while (transferred < capacity && globalRes.AppleYogurt > 0) { globalRes.AppleYogurt--; helper.BagAppleYogurt++; transferred++; }
            while (transferred < capacity && globalRes.PurplePotion > 0) { globalRes.PurplePotion--; helper.BagPurplePotion++; transferred++; }

            if (transferred > 0)
            {
                ILogger.Log($"[InteractActionService] Loaded {transferred} milk products into Seller helper {helperEntity.Id}");
                return true;
            }
        }
        else if (helper.Type == HelperType.Builder)
        {
            // Give builder all available coins (up to bag capacity)
            int needed = helper.BagCapacity - helper.BagCoins;
            int toGive = System.Math.Max(0, System.Math.Min(needed, globalRes.Coins));
            if (toGive > 0)
            {
                globalRes.Coins -= toGive;
                helper.BagCoins += toGive;
                ILogger.Log($"[InteractActionService] Gave {toGive} coins to Builder helper {helperEntity.Id}");
                return true;
            }
        }

        return false;
    }

    private static string FoodTypeToKey(int foodType) => foodType switch
    {
        FoodType.Grass => StateKeys.Grass,
        FoodType.Carrot => StateKeys.Carrot,
        FoodType.Apple => StateKeys.Apple,
        FoodType.Mushroom => StateKeys.Mushroom,
        _ => StateKeys.Food
    };

    private Entity GetGlobalResourcesEntity(Context ctx)
    {
        foreach (var entity in ctx.State.Filter<GlobalResourcesComponent>())
        {
            return entity;
        }
        return Entity.Null;
    }

    /// <summary>
    /// Shared logic for completing a land purchase — builds the structure and spawns neighbors.
    /// Called by both player interaction and builder helper.
    /// The land entity should already be deleted before calling this.
    /// </summary>
    public static void CompleteLandBuilding(Context ctx, Vector2 position, int landType, int gridX, int gridY)
    {
        // Destroy nearby props to clear space
        DestroyNearbyProps(ctx, position, 4f);

        // Find the player entity from context for helper spawning
        Entity playerEntity = ctx.Entity;

        switch (landType)
        {
            case LandType.LoveHouse:
                LoveHouseDefinition.Create(ctx, position);
                break;
            case LandType.SellPoint:
                SellPointDefinition.Create(ctx, position);
                break;
            case LandType.FinalStructure:
                FinalStructureDefinition.Create(ctx, position, 0);
                break;
            case LandType.CarrotFarm:
                CarrotFarmDefinition.Create(ctx, position);
                break;
            case LandType.AppleOrchard:
                AppleOrchardDefinition.Create(ctx, position);
                break;
            case LandType.MushroomCave:
                MushroomCaveDefinition.Create(ctx, position);
                break;
            case LandType.HelperAssistant:
                {
                    HelperAssistantDefinition.Create(ctx, position);
                    // Assistant is a pet (follows player, no bag) — x2 click speed
                    var assistant = HelperPetDefinition.Create(ctx, position, HelperType.Assistant, playerEntity);
                    ctx.State.AddComponent(assistant, new BreedBornComponent());
                    if (ctx.State.HasComponent<PlayerStateComponent>(playerEntity))
                    {
                        ref var ps = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
                        ps.AssistantHelper = assistant;
                        ps.ClickMultiplier = 2;
                        var gt1 = ctx.State.GetCustomData<IGameTime>();
                        ILogger.Log($"[Building] HelperAssistant built at {(gt1 != null ? gt1.CurrentTick / 60f / 60f : -1):F1}m — ClickMultiplier=4");
                    }
                    break;
                }
            case LandType.UpgradeAssistant:
                {
                    UpgradeAssistantDefinition.Create(ctx, position);
                    var upgradePet = HelperPetDefinition.Create(ctx, position, HelperType.Assistant, playerEntity);
                    ctx.State.AddComponent(upgradePet, new BreedBornComponent());
                    if (ctx.State.HasComponent<PlayerStateComponent>(playerEntity))
                    {
                        ref var ps = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
                        ps.ClickMultiplier = 5;
                        var gt2 = ctx.State.GetCustomData<IGameTime>();
                        ILogger.Log($"[Building] UpgradeAssistant built at {(gt2 != null ? gt2.CurrentTick / 60f / 60f : -1):F1}m — ClickMultiplier=12");
                    }
                    break;
                }
            case LandType.UpgradeGatherer:
                UpgradeGathererDefinition.Create(ctx, position);
                SpawnUpgradePet(ctx, position, playerEntity, HelperType.Gatherer);
                break;
            case LandType.UpgradeBuilder:
                UpgradeBuilderDefinition.Create(ctx, position);
                SpawnUpgradePet(ctx, position, playerEntity, HelperType.Builder);
                break;
            case LandType.UpgradeSeller:
                UpgradeSellerDefinition.Create(ctx, position);
                SpawnUpgradePet(ctx, position, playerEntity, HelperType.Seller);
                break;
            case LandType.Decoration:
                DecorationDefinition.Create(ctx, position);
                break;
            default:
                HouseDefinition.Create(ctx, position);
                break;
        }

        StarGrid.SpawnNeighbors(ctx, gridX, gridY);
    }

    /// <summary>
    /// Find the player's helper of the given type and spawn a pet that follows it.
    /// The pet signals the x2 upgrade — HelperSystem detects it and applies the boost.
    /// </summary>
    private static void SpawnUpgradePet(Context ctx, Vector2 position, Entity playerEntity, int helperType)
    {
        // Find the helper to upgrade
        Entity targetHelper = Entity.Null;
        foreach (var he in ctx.State.Filter<HelperComponent>())
        {
            var h = ctx.State.GetComponent<HelperComponent>(he);
            if (h.OwnerPlayer == playerEntity && h.Type == helperType)
            { targetHelper = he; break; }
        }

        if (targetHelper == Entity.Null) return; // helper not unlocked yet — shouldn't happen if land was locked

        var pet = HelperPetDefinition.Create(ctx, position, helperType, targetHelper);
        ctx.State.AddComponent(pet, new BreedBornComponent());
        var gt = ctx.State.GetCustomData<IGameTime>();
        float min = gt != null ? gt.CurrentTick / 60f / 60f : -1;
        ILogger.Log($"[UpgradePet] Upgraded helper type={helperType} at {min:F1}m");
    }

    /// <summary>Forwarding stub for backward compatibility.</summary>
    public static bool MilkCow(EntityWorld state, Entity cowEntity, int foodToUse, int exhaustPerClick, out bool cowDone)
        => InteractionLogic.MilkCow(state, cowEntity, foodToUse, exhaustPerClick, out cowDone);
}
