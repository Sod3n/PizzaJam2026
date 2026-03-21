using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.TwoD;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Deterministic.GameFramework.Types;

namespace Template.Shared.Actions;

public class InteractActionService : ActionService<InteractAction, PlayerEntity>
{
    private const float InteractionRange = 2.0f;
    private const float InteractionRangeSq = InteractionRange * InteractionRange;

    protected override void ExecuteProcess(InteractAction action, ref PlayerEntity playerComp, Context ctx)
    {
        Entity playerEntity = ctx.Entity;

        // Verify UserID match (optional security check)
        if (playerComp.UserId != action.UserId)
        {
            System.Console.WriteLine($"[Interact] UserId mismatch. Entity: {playerComp.UserId}, Action: {action.UserId}");
            return;
        }

        if (!ctx.State.HasComponent<Transform2D>(playerEntity))
            return;

        var playerPos = ctx.State.GetComponent<Transform2D>(playerEntity).Position;

        // 2. Find nearest interactable
        Entity target = FindNearestInteractable(ctx, playerPos);

        if (target == Entity.Null)
        {
            // System.Console.WriteLine("[Interact] No target found.");
            return;
        }

        // 3. Handle Interaction based on target type
        
        // Get Global Resources
        Entity globalResEntity = GetGlobalResourcesEntity(ctx);
        if (globalResEntity == Entity.Null) return; // Should not happen if set up correctly
        ref var globalRes = ref ctx.State.GetComponent<GlobalResourcesComponent>(globalResEntity);

        if (ctx.State.HasComponent<GrassComponent>(target))
        {
            HandleGrassInteraction(ctx, target, ref globalRes);
        }
        else if (ctx.State.HasComponent<CowComponent>(target))
        {
            HandleCowInteraction(ctx, playerEntity, target, ref globalRes);
        }
        else if (ctx.State.HasComponent<SellPointComponent>(target))
        {
            HandleSellPointInteraction(ctx, ref globalRes);
        }
        else if (ctx.State.HasComponent<LandComponent>(target))
        {
            HandleLandInteraction(ctx, target, ref globalRes);
        }
    }

    private void HandleGrassInteraction(Context ctx, Entity grassEntity, ref GlobalResourcesComponent globalRes)
    {
        ref var grass = ref ctx.State.GetComponent<GrassComponent>(grassEntity);
        
        grass.Durability -= 1;
        globalRes.Grass += 1;
        
        System.Console.WriteLine($"[Interact] Grass harvested. Durability: {grass.Durability}, Global Grass: {globalRes.Grass}");

        if (grass.Durability <= 0)
        {
            ctx.State.DeleteEntity(grassEntity);
        }
    }

    private void HandleCowInteraction(Context ctx, Entity playerEntity, Entity cowEntity, ref GlobalResourcesComponent globalRes)
    {
        ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
        
        // Start Milking Process (From Idle)
        if (ctx.State.HasComponent<PlayerStateComponent>(playerEntity))
        {
            ref var playerState = ref ctx.State.GetComponent<PlayerStateComponent>(playerEntity);
            
            // 1. Idle -> Entering
            if (playerState.State == (int)PlayerState.Idle)
            {
                // Check initial conditions
                if (globalRes.Grass <= 0)
                {
                    System.Console.WriteLine($"[Interact] Cannot start milking. No grass!");
                    return;
                }
                
                if (cow.Exhaust >= cow.MaxExhaust)
                {
                    System.Console.WriteLine($"[Interact] Cannot start milking. Cow is exhausted ({cow.Exhaust}/{cow.MaxExhaust})!");
                    return;
                }

                playerState.State = (int)PlayerState.EnteringMilking;
                playerState.InteractionTarget = cowEntity;
                playerState.MilkingTimer = 0;
                
                if (ctx.State.HasComponent<Transform2D>(playerEntity))
                {
                    playerState.ReturnPosition = ctx.State.GetComponent<Transform2D>(playerEntity).Position;
                }

                System.Console.WriteLine($"[Interact] Entering House (1s)...");
                return;
            }

            // 2. Milking -> Produce (Clicker)
            if (playerState.State == (int)PlayerState.Milking)
            {
                // Check Resources
                if (globalRes.Grass > 0 && cow.Exhaust < cow.MaxExhaust)
                {
                    globalRes.Grass--;
                    globalRes.Milk++;
                    cow.Exhaust++;
                    
                    System.Console.WriteLine($"[Interact] Clicked! Milk Produced. Grass: {globalRes.Grass}, Milk: {globalRes.Milk}, Exhaust: {cow.Exhaust}/{cow.MaxExhaust}");
                    
                    // Auto-Exit if run out
                    if (globalRes.Grass <= 0 || cow.Exhaust >= cow.MaxExhaust)
                    {
                        System.Console.WriteLine($"[Interact] Resources depleted/Full. Exiting House (1s)...");
                        playerState.State = (int)PlayerState.ExitingMilking;
                        playerState.MilkingTimer = 0;
                    }
                }
                else
                {
                    // Should be caught by the Auto-Exit above, but manual trigger if state desync
                     System.Console.WriteLine($"[Interact] Cannot milk. Exiting House (1s)...");
                     playerState.State = (int)PlayerState.ExitingMilking;
                     playerState.MilkingTimer = 0;
                }
                return;
            }

            // 3. Busy (Entering or Exiting)
            if (playerState.State == (int)PlayerState.EnteringMilking || playerState.State == (int)PlayerState.ExitingMilking)
            {
                 System.Console.WriteLine($"[Interact] Busy ({((PlayerState)playerState.State)})...");
                 return;
            }
        }
    }

    private void HandleSellPointInteraction(Context ctx, ref GlobalResourcesComponent globalRes)
    {
        if (globalRes.Milk > 0)
        {
            globalRes.Milk -= 1;
            globalRes.Coins += 1;
            System.Console.WriteLine($"[Interact] Sold Milk. Coins: {globalRes.Coins}, Milk: {globalRes.Milk}");
        }
        else
        {
            System.Console.WriteLine($"[Interact] Nothing to sell.");
        }
    }

    private void HandleLandInteraction(Context ctx, Entity landEntity, ref GlobalResourcesComponent globalRes)
    {
        if (globalRes.Coins > 0)
        {
            ref var land = ref ctx.State.GetComponent<LandComponent>(landEntity);
            
            globalRes.Coins -= 1;
            land.CurrentCoins += 1;
            
            System.Console.WriteLine($"[Interact] Invested in Land. Progress: {land.CurrentCoins}/{land.Threshold}");

            if (land.CurrentCoins >= land.Threshold)
            {
                // Spawn House and Cow
                var transform = ctx.State.GetComponent<Transform2D>(landEntity);
                var position = transform.Position;
                
                // Destroy Land
                ctx.State.DeleteEntity(landEntity);
                
                // Spawn House
                var houseEntity = HouseDefinition.Create(ctx, position);
                
                // Spawn Cow near House
                var cowPosition = position + new Vector2(2, 2); // Offset slightly
                var cowEntity = CowDefinition.Create(ctx, cowPosition);
                
                // Link Cow and House
                ref var house = ref ctx.State.GetComponent<HouseComponent>(houseEntity);
                house.CowId = cowEntity;
                
                ref var cow = ref ctx.State.GetComponent<CowComponent>(cowEntity);
                cow.HouseId = houseEntity;
                
                System.Console.WriteLine($"[Interact] Land purchased! House and Cow spawned.");
            }
        }
        else
        {
            System.Console.WriteLine($"[Interact] Not enough coins to invest in land.");
        }
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

        return nearest;
    }

    private void CheckNearest(Context ctx, Vector2 pos, System.Collections.Generic.IEnumerable<Entity> filter, ref Entity nearest, ref Float minDistSq)
    {
        foreach (var entity in filter)
        {
            if (ctx.State.HasComponent<HiddenComponent>(entity)) continue;

            if (ctx.State.HasComponent<Transform2D>(entity))
            {
                var targetPos = ctx.State.GetComponent<Transform2D>(entity).Position;
                var distSq = Vector2.DistanceSquared(pos, targetPos);
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    nearest = entity;
                }
            }
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
