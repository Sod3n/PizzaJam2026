using Deterministic.GameFramework.Types;
using Template.Shared.Components;

namespace Template.Shared;

public struct StateDefinition
{
    public int EnterDuration;
    public int ActiveDuration; // 0 = indefinite
    public int ExitDuration;

    public StateDefinition(int enter, int active, int exit)
    {
        EnterDuration = enter;
        ActiveDuration = active;
        ExitDuration = exit;
    }
}

public static class StateDefinitions
{
    public const int PhaseDurationTicks = 60;

    public static bool TryGet(FixedString32 key, out StateDefinition def)
    {
        if (key == StateKeys.Milking) { def = new(PhaseDurationTicks, 0, PhaseDurationTicks); return true; }
        if (key == StateKeys.Taming) { def = new(0, PhaseDurationTicks, 0); return true; }
        if (key == StateKeys.Assign) { def = new(0, PhaseDurationTicks, 0); return true; }
        if (key == StateKeys.Breed) { def = new(PhaseDurationTicks, 0, PhaseDurationTicks); return true; }
        def = default;
        return false;
    }

    public static void Begin(ref StateComponent sc, FixedString32 key)
    {
        sc.Key = key;
        sc.IsEnabled = true;
        sc.CurrentTime = 0;

        if (TryGet(key, out var def) && def.EnterDuration > 0)
        {
            sc.Phase = StatePhase.Enter;
            sc.MaxTime = def.EnterDuration;
        }
        else
        {
            sc.Phase = StatePhase.Active;
            sc.MaxTime = TryGet(key, out var d) ? d.ActiveDuration : 0;
        }
    }

    public static void BeginExit(ref StateComponent sc)
    {
        if (TryGet(sc.Key, out var def) && def.ExitDuration > 0)
        {
            sc.Phase = StatePhase.Exit;
            sc.CurrentTime = 0;
            sc.MaxTime = def.ExitDuration;
        }
        else
        {
            sc.IsEnabled = false;
        }
    }
}
