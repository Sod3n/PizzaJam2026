using Deterministic.GameFramework.ECS;
using Template.Shared.Components;
using Deterministic.GameFramework.Types;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.Utils.Logging;

namespace Template.Shared.Systems;

public class CowSystem : ISystem
{
    public const int PhaseDurationTicks = 60;

    public void Update(EntityWorld state)
    {
        // Handle state exit transitions (milking_enter completed, milking_exit completed)
        foreach (var playerEntity in state.Filter<ExitStateComponent>())
        {
            if (!state.HasComponent<PlayerStateComponent>(playerEntity)) continue;

            var exit = state.GetComponent<ExitStateComponent>(playerEntity);
            ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
            ref var playerState = ref state.GetComponent<PlayerStateComponent>(playerEntity);

            if (exit.Key == "milking_enter")
                HandleEnterComplete(state, playerEntity, ref sc, ref playerState);
            else if (exit.Key == "milking_exit")
                HandleExitComplete(state, playerEntity, ref sc, ref playerState);
        }

        // Handle active milking states (ensure hidden)
        foreach (var playerEntity in state.Filter<PlayerStateComponent>())
        {
            if (!state.HasComponent<StateComponent>(playerEntity)) continue;

            ref var sc = ref state.GetComponent<StateComponent>(playerEntity);
            if (!sc.IsEnabled) continue;

            ref var playerState = ref state.GetComponent<PlayerStateComponent>(playerEntity);

            if (sc.Key == "milking" || sc.Key == "milking_exit")
            {
                state.HideEntity( playerEntity);
                if (state.HasComponent<CowComponent>(playerState.InteractionTarget))
                    state.HideEntity( playerState.InteractionTarget);
            }
        }

        // Handle Cow Exhaust & Visibility
        foreach (var cowEntity in state.Filter<CowComponent>())
        {
            ref var cow = ref state.GetComponent<CowComponent>(cowEntity);

            if (cow is { Exhaust: > 0, IsMilking: false })
            {
                var gameTime = state.GetCustomData<IGameTime>();
                if (gameTime != null)
                {
                    var random = new DeterministicRandom((uint)(cowEntity.Id ^ gameTime.CurrentTick));
                    if (random.NextInt(0, 240) == 0)
                    {
                        cow.Exhaust--;
                    }
                }
            }
            else if (!cow.IsMilking)
            {
                if (state.HasComponent<HiddenComponent>(cowEntity))
                {
                    state.UnhideEntity( cowEntity);
                }
            }
        }
    }

    private void HandleEnterComplete(EntityWorld state, Entity playerEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        ILogger.Log($"[CowSystem] TRANSITION: milking_enter → milking");

        sc.Key = "milking";
        sc.CurrentTime = 0;
        sc.MaxTime = 0;
        sc.IsEnabled = true;

        state.AddComponent(playerEntity, new EnterStateComponent { Key = "milking", Age = 0 });

        var cowEntity = playerState.InteractionTarget;

        state.HideEntity( cowEntity);
        state.HideEntity( playerEntity);

        ILogger.Log($"[CowSystem] Player {playerEntity.Id} is now Milking.");
    }

    private void HandleExitComplete(EntityWorld state, Entity playerEntity, ref StateComponent sc, ref PlayerStateComponent playerState)
    {
        state.UnhideEntity( playerEntity);

        if (state.HasComponent<CowComponent>(playerState.InteractionTarget))
        {
            ref var cow = ref state.GetComponent<CowComponent>(playerState.InteractionTarget);
            cow.IsMilking = false;
            state.UnhideEntity( playerState.InteractionTarget);
        }

        playerState.InteractionTarget = Entity.Null;

        sc.Key = "";
        sc.CurrentTime = 0;
        sc.MaxTime = 0;
        sc.IsEnabled = false;

        state.AddComponent(playerEntity, new EnterStateComponent { Key = "idle", Age = 0 });

        ILogger.Log($"[CowSystem] Player {playerEntity.Id} is now Idle.");
    }

}
