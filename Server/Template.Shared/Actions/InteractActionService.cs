using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.TwoD;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.Utils.Logging;

namespace Template.Shared.Actions;

public class InteractActionService : ActionService<InteractAction, PlayerEntity>
{
    private static readonly Float InteractionRange = 2.0f;
    private static readonly Float InteractionRangeSq = InteractionRange * InteractionRange;

    protected override void ExecuteProcess(InteractAction action, ref PlayerEntity playerComp, Context ctx)
    {
        Entity playerEntity = ctx.Entity;

        if (playerComp.UserId != action.UserId) return;
        if (!ctx.State.HasComponent<Transform2D>(playerEntity)) return;

        var playerPos = ctx.State.GetComponent<Transform2D>(playerEntity).Position;
        Entity target = FindNearestInteractable(ctx, playerPos);

        if (target == Entity.Null) return;

        Entity globalResEntity = GetGlobalResourcesEntity(ctx);
        if (globalResEntity == Entity.Null) return;
        ref var globalRes = ref ctx.State.GetComponent<GlobalResourcesComponent>(globalResEntity);

        bool success = false;

        if (ctx.State.HasComponent<GrassComponent>(target))
        {
            success = HandleGrassInteraction(ctx, target, ref globalRes);
        }
        else if (ctx.State.HasComponent<CowComponent>(target))
        {
            success = HandleCowInteraction(ctx, playerEntity, target, ref globalRes, ref target);
        }
        else if (ctx.State.HasComponent<SellPointComponent>(target))
        {
            success = HandleSellPointInteraction(ctx, ref globalRes);
        }
        else if (ctx.State.HasComponent<LandComponent>(target))
        {
            success = HandleLandInteraction(ctx, target, ref globalRes);
        }
        else if (ctx.State.HasComponent<FinalStructureComponent>(target))
        {
            success = HandleFinalStructureInteraction(ctx, target, ref globalRes);
        }

        // Only mark if the specific interaction logic succeeded AND target still exists
        if (success)
        {
            ctx.State.AddComponent(target, new InteractedComponent());
            ILogger.Log($"[InteractActionService] Marked {target.Id} as successfully interacted");
        }
    }

    private bool HandleGrassInteraction(Context ctx, Entity grassEntity, ref GlobalResourcesComponent globalRes)
    {
        ref var grass = ref ctx.State.GetComponent<GrassComponent>(grassEntity);
        grass.Durability -= 1;
        globalRes.Grass += 1;

        if (grass.Durability <= 0)
        {
            ctx.State.DeleteEntity(grassEntity);
        }
        return true; // Always success if grass found
    }

    private bool HandleCowInteraction(Context ctx, Entity playerEntity, Entity cowEntity, ref GlobalResourcesComponent globalRes, ref Entity target)
    {
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
        if (!ctx.State.HasComponent<PlayerStateComponent>(playerEntity)) return false;

        ref var playerState = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
        
        // Attempting to Start Milking
        if (playerState.State == (int)PlayerState.Idle)
        {
            if (globalRes.Grass <= 0 || cow.Exhaust >= cow.MaxExhaust) return false;

            playerState.State = (int)PlayerState.EnteringMilking;
            playerState.InteractionTarget = cowEntity;
            playerState.MilkingTimer = 0;
            
            if (ctx.State.HasComponent<Transform2D>(playerEntity))
                playerState.ReturnPosition = ctx.State.GetComponent<Transform2D>(playerEntity).Position;
            
            return true;
        }
        
        // Clicking while Milking
        if (playerState.State == (int)PlayerState.Milking)
        {
            if (globalRes.Grass > 0 && cow.Exhaust < cow.MaxExhaust)
            {
                globalRes.Grass--;
                globalRes.Milk++;
                cow.Exhaust++;
                
                target = ctx.GetComponent<CowComponent>(cowEntity).HouseId;
                
                if (globalRes.Grass <= 0 || cow.Exhaust >= cow.MaxExhaust)
                {
                    playerState.State = (int)PlayerState.ExitingMilking;
                    playerState.MilkingTimer = 0;
                }
                return true;
            }
        }
        
        return false;
    }

    private bool HandleSellPointInteraction(Context ctx, ref GlobalResourcesComponent globalRes)
    {
        if (globalRes.Milk > 0)
        {
            globalRes.Milk -= 1;
            globalRes.Coins += 1;
            return true;
        }
        return false;
    }

    private bool HandleLandInteraction(Context ctx, Entity landEntity, ref GlobalResourcesComponent globalRes)
    {
        if (globalRes.Coins > 0)
        {
            ref var land = ref ctx.State.GetComponent<LandComponent>(landEntity);
            globalRes.Coins -= 1;
            land.CurrentCoins += 1;

            if (land.CurrentCoins >= land.Threshold)
            {
                var transform = ctx.State.GetComponent<Transform2D>(landEntity);
                var position = transform.Position;
                ctx.State.DeleteEntity(landEntity);
                
                var houseEntity = HouseDefinition.Create(ctx, position);
                var cowPosition = position + new Vector2(2, 2);
                var cowEntity = CowDefinition.Create(ctx, cowPosition);
                
                ref var house = ref ctx.State.GetComponent<HouseComponent>(houseEntity);
                house.CowId = cowEntity;
                
                ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
                cow.HouseId = houseEntity;
            }
            return true;
        }
        return false;
    }
    
    private bool HandleFinalStructureInteraction(Context ctx, Entity finalEntity, ref GlobalResourcesComponent globalRes)
    {
        if (globalRes.Coins <= 0) return false;
        
        ref var final = ref ctx.State.GetComponent<FinalStructureComponent>(finalEntity);
            
        if (final.CurrentCoins >= final.Threshold) return false;
            
        globalRes.Coins -= 1;
        final.CurrentCoins += 1;
        return true;
    }

    private Entity FindNearestInteractable(Context ctx, Vector2 position)
    {
        Entity nearest = Entity.Null;
        Float minDistSq = new Float(InteractionRangeSq);

        // Generic check for all entities with relevant components
        // In a real optimized game, we'd use a spatial partition or specific lists.
        // Here we iterate filtered entities.
        
        // This is inefficient if there are many entities, but fine for template.
        // Better: Iterate specific component types.
        
        CheckNearest(ctx, position, ctx.State.Filter<GrassComponent>(), ref nearest, ref minDistSq);
        CheckNearest(ctx, position, ctx.State.Filter<CowComponent>(), ref nearest, ref minDistSq);
        CheckNearest(ctx, position, ctx.State.Filter<SellPointComponent>(), ref nearest, ref minDistSq);
        CheckNearest(ctx, position, ctx.State.Filter<LandComponent>(), ref nearest, ref minDistSq);
        CheckNearest(ctx, position, ctx.State.Filter<FinalStructureComponent>(), ref nearest, ref minDistSq);

        return nearest;
    }

    private void CheckNearest(Context ctx, Vector2 pos, System.Collections.Generic.IEnumerable<Entity> filter, ref Entity nearest, ref Float minDistSq)
    {
        foreach (var entity in filter)
        {
            if (ctx.State.HasComponent<HiddenComponent>(entity) && ctx.State.HasComponent<CowComponent>(entity))
            {
                var cow = ctx.State.GetComponent<CowComponent>(entity);
                if (!cow.IsMilking)
                {
                    continue;
                }
            }

            if (!ctx.State.HasComponent<Transform2D>(entity)) continue;
            
            var targetPos = ctx.State.GetComponent<Transform2D>(entity).Position;
            var distSq = Vector2.DistanceSquared(pos, targetPos);
            
            if (distSq >= minDistSq) continue;
            
            minDistSq = distSq;
            nearest = entity;
        }
    }

    private Entity GetGlobalResourcesEntity(Context ctx)
    {
        foreach (var entity in ctx.State.Filter<GlobalResourcesComponent>())
        {
            return entity; // Assume singleton
        }
        return Entity.Null;
    }
}
