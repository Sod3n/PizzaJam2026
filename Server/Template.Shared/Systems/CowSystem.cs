using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Template.Shared.Components;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Physics2D.Components;

namespace Template.Shared.Systems;

public class CowSystem : ISystem
{
    public const int PhaseDurationTicks = 60; // 1 second per phase (Enter/Exit)

    public void Update(EntityWorld state)
    {
        // Handle Players Milking States
        foreach (var playerEntity in state.Filter<PlayerStateComponent>())
        {
            ref var playerState = ref state.GetComponent<PlayerStateComponent>(playerEntity);
            
            if (playerState.State == (int)PlayerState.EnteringMilking)
            {
                HandleEntering(state, playerEntity, ref playerState);
            }
            else if (playerState.State == (int)PlayerState.ExitingMilking)
            {
                HandleExiting(state, playerEntity, ref playerState);
            }
            else if (playerState.State == (int)PlayerState.Milking)
            {
                // Just ensure hidden
                HideEntity(state, playerEntity);
                if (state.HasComponent<CowComponent>(playerState.InteractionTarget))
                {
                    HideEntity(state, playerState.InteractionTarget);
                }
            }
        }

        // Handle Cow Exhaust & Visibility
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            ref var transform = ref state.GetComponent<Transform2D>(cowEntity);
            
            if (cow.Exhaust >= cow.MaxExhaust)
            {
                Vector2? targetPos = null;

                // If exhausted, move to House
                if (state.HasComponent<Transform2D>(cow.HouseId))
                {
                    targetPos = state.GetComponent<Transform2D>(cow.HouseId).Position;
                }
                
                HideEntity(state, cowEntity, targetPos);

                // Decay Exhaust
                var gameTime = state.GetCustomData<IGameTime>();
                if (gameTime != null)
                {
                    var random = new DeterministicRandom((uint)(cowEntity.Id + gameTime.CurrentTick));
                    // 1% chance per tick to recover
                    if (random.NextInt(0, 100) == 0)
                    {
                        cow.Exhaust--;
                    }
                }
            }
            else
            {
                // Cow is fine -> Ensure visible
                if (state.HasComponent<HiddenComponent>(cowEntity))
                {
                    UnhideEntity(state, cowEntity);
                    
                    // Reset position to spawn point when reappearing
                    transform.Position = cow.SpawnPosition;
                }
            }
        }
    }

    private void HandleEntering(EntityWorld state, Entity playerEntity, ref PlayerStateComponent playerState)
    {
        // 1. Hide
        HideEntity(state, playerEntity);
        if (state.HasComponent<CowComponent>(playerState.InteractionTarget))
        {
             HideEntity(state, playerState.InteractionTarget);
        }

        // 2. Timer
        playerState.MilkingTimer++;
        if (playerState.MilkingTimer >= PhaseDurationTicks)
        {
            playerState.State = (int)PlayerState.Milking;
            playerState.MilkingTimer = 0;
            System.Console.WriteLine($"[CowSystem] Entering Done. Player {playerEntity.Id} is now Milking.");
        }
    }

    private void HandleExiting(EntityWorld state, Entity playerEntity, ref PlayerStateComponent playerState)
    {
        // 1. Hide (Keep hidden during exit)
        HideEntity(state, playerEntity);
         if (state.HasComponent<CowComponent>(playerState.InteractionTarget))
        {
             HideEntity(state, playerState.InteractionTarget);
        }

        // 2. Timer
        playerState.MilkingTimer++;
        if (playerState.MilkingTimer >= PhaseDurationTicks)
        {
            // Unhide
            UnhideEntity(state, playerEntity);
            
            // Unhide Cow if it exists (and check if it needs to stay hidden due to exhaust?)
            // Actually, if cow is exhausted, CowSystem update loop will hide it again in the next pass/same pass.
            // But we should unhide the "interaction hiding" layer.
            // The CowSystem logic for exhaust handles hiding separately.
            // But wait, we are using the SAME HiddenComponent?
            // Yes.
            // If we remove HiddenComponent here, but the cow is exhausted, the CowSystem Main Loop needs to re-add it?
            // The CowSystem main loop runs AFTER this loop? Or before?
            // They are in the same Update function. Players loop first, then Cows loop.
            // If we unhide here, the Cow loop later will check `if (cow.Exhaust >= cow.MaxExhaust)` and re-hide it if needed.
            // So it's safe to unhide here.
            
            if (state.HasComponent<CowComponent>(playerState.InteractionTarget))
            {
                UnhideEntity(state, playerState.InteractionTarget);
                
                // If we moved the cow to house position, we might want to move it back?
                // Or let the Cow loop handle it?
                // Cow loop:
                // else { if (Hidden) Unhide; Reset Pos; }
                // So yes, Cow loop will handle reset.
            }

            playerState.State = (int)PlayerState.Idle;
            playerState.InteractionTarget = Entity.Null;
            playerState.MilkingTimer = 0;
            System.Console.WriteLine($"[CowSystem] Exiting Done. Player {playerEntity.Id} is now Idle.");
        }
    }

    private void HideEntity(EntityWorld state, Entity entity, Vector2? positionOverride = null)
    {
        if (!state.HasComponent<HiddenComponent>(entity))
        {
            uint prevLayer = 1;
            uint prevMask = 1;
            
            if (state.HasComponent<CharacterBody2D>(entity))
            {
                ref var body = ref state.GetComponent<CharacterBody2D>(entity);
                prevLayer = body.CollisionLayer;
                prevMask = body.CollisionMask;
                
                // Disable Physics
                body.CollisionLayer = 0;
                body.CollisionMask = 0;
                body.Velocity = Vector2.Zero;
            }
            
            state.AddComponent(entity, new HiddenComponent 
            { 
                PreviousLayer = prevLayer, 
                PreviousMask = prevMask 
            });
        }
        
        // Always enforce position if provided (even if already hidden, in case target moved)
        if (positionOverride.HasValue && state.HasComponent<Transform2D>(entity))
        {
            ref var transform = ref state.GetComponent<Transform2D>(entity);
            transform.Position = positionOverride.Value;
        }
    }

    private void UnhideEntity(EntityWorld state, Entity entity)
    {
        if (state.HasComponent<HiddenComponent>(entity))
        {
            var hidden = state.GetComponent<HiddenComponent>(entity);
            
            if (state.HasComponent<CharacterBody2D>(entity))
            {
                ref var body = ref state.GetComponent<CharacterBody2D>(entity);
                body.CollisionLayer = hidden.PreviousLayer;
                body.CollisionMask = hidden.PreviousMask;
            }
            
            state.RemoveComponent<HiddenComponent>(entity);
        }
    }
}
