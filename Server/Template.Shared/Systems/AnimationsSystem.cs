using Deterministic.GameFramework.ECS;
using Template.Shared.Components;

namespace Template.Shared.Systems;

public class AnimationsSystem : ISystem
{
    public void Update(EntityWorld state)
    {
        // Clean up stale enter/exit markers (age > 0 means they lasted a full tick)
        foreach (var entity in state.Filter<EnterStateComponent>())
        {
            ref var enter = ref state.GetComponent<EnterStateComponent>(entity);
            if (enter.Age > 0)
                state.RemoveComponent<EnterStateComponent>(entity);
            else
                enter.Age++;
        }

        foreach (var entity in state.Filter<ExitStateComponent>())
        {
            ref var exit = ref state.GetComponent<ExitStateComponent>(entity);
            if (exit.Age > 0)
                state.RemoveComponent<ExitStateComponent>(entity);
            else
                exit.Age++;
        }

        // Advance generic state timers
        foreach (var entity in state.Filter<StateComponent>())
        {
            ref var s = ref state.GetComponent<StateComponent>(entity);
            if (!s.IsEnabled || s.MaxTime <= 0) continue;

            s.CurrentTime++;
            if (s.CurrentTime >= s.MaxTime)
            {
                s.IsEnabled = false;
                state.AddComponent(entity, new ExitStateComponent { Key = s.Key, Age = 0 });
            }
        }

    }
}
