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
            if (s.CurrentTime < s.MaxTime) continue;

            // Timer expired — advance phase or complete
            if (StateDefinitions.TryGet(s.Key, out var def))
                AdvancePhase(state, entity, ref s, ref def);
            else
                Complete(state, entity, ref s);
        }
    }

    private void AdvancePhase(EntityWorld state, Entity entity, ref StateComponent s, ref StateDefinition def)
    {
        if (s.Phase == StatePhase.Enter)
        {
            // Always transition to Active after Enter (ActiveDuration 0 = indefinite, handled by timer check)
            TransitionTo(state, entity, ref s, StatePhase.Active, def.ActiveDuration);
        }
        else if (s.Phase == StatePhase.Active)
        {
            if (def.ExitDuration > 0)
                TransitionTo(state, entity, ref s, StatePhase.Exit, def.ExitDuration);
            else
                Complete(state, entity, ref s);
        }
        else if (s.Phase == StatePhase.Exit)
        {
            Complete(state, entity, ref s);
        }
    }

    private void TransitionTo(EntityWorld state, Entity entity, ref StateComponent s, StatePhase phase, int duration)
    {
        s.Phase = phase;
        s.CurrentTime = 0;
        s.MaxTime = duration;
        state.AddComponent(entity, new EnterStateComponent { Key = s.Key, Phase = phase, Age = 0 });
    }

    private void Complete(EntityWorld state, Entity entity, ref StateComponent s)
    {
        s.IsEnabled = false;
        state.AddComponent(entity, new ExitStateComponent { Key = s.Key, Age = 0 });
    }
}
