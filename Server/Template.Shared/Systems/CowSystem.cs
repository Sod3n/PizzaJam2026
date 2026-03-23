using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Template.Shared.Components;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.Utils.Logging;

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
            
            if (cow is { Exhaust: > 0, IsMilking: false })
            {
                // Decay Exhaust
                var gameTime = state.GetCustomData<IGameTime>();
                if (gameTime != null)
                {
                    // Use a more stable seed based on the cow entity and the current tick
                    var random = new DeterministicRandom((uint)(cowEntity.Id ^ gameTime.CurrentTick));
                    // 1% chance per tick to recover
                    if (random.NextInt(0, 240) == 0)
                    {
                        cow.Exhaust--;
                    }
                }
            }
            else if (!cow.IsMilking)
            {
                // Cow is fine -> Ensure visible
                if (state.HasComponent<HiddenComponent>(cowEntity))
                {
                    UnhideEntity(state, cowEntity);
                }
            }
        }
    }

    private void HandleEntering(EntityWorld state, Entity playerEntity, ref PlayerStateComponent playerState)
    {
        // 2. Timer
        playerState.MilkingTimer++;
        if (playerState.MilkingTimer >= PhaseDurationTicks)
        {
            ILogger.Log($"[CowSystem] TRANSITION: Entering → Milking");
            playerState.State = (int)PlayerState.Milking;
            playerState.MilkingTimer = 0;
            var cowEntity = playerState.InteractionTarget;
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
            
            cow.IsMilking = true;
            
            HideEntity(state, cowEntity);
            
            ILogger.Log($"[CowSystem] Entering Done. Player {playerEntity.Id} is now Milking.");
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
            
            if (state.HasComponent<CowComponent>(playerState.InteractionTarget))
            {
                var cowEntity = playerState.InteractionTarget;
                ref var cow = ref state.GetComponent<CowComponent>(cowEntity);
                cow.IsMilking = false;
            }

            playerState.State = (int)PlayerState.Idle;
            playerState.InteractionTarget = Entity.Null;
            playerState.MilkingTimer = 0;
            ILogger.Log($"[CowSystem] Exiting Done. Player {playerEntity.Id} is now Idle.");
        }
    }

    private void HideEntity(EntityWorld state, Entity entity)
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
