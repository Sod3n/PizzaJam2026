using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

public static class CooldownUnit
{
    /// <summary>Real countdown — CooldownSystem decrements TicksRemaining every tick.</summary>
    public const int Ticks = 0;
    /// <summary>Binary flag with day-bound reset — non-zero means "on cooldown",
    /// SleepLogic.AdvanceDay zeroes all Day-unit cooldowns. TicksRemaining doesn't tick down.</summary>
    public const int Days = 1;
}

/// <summary>
/// Unified per-entity cooldown carrier. <c>MaxTicks</c> is the configured ceiling
/// (read by demolish to set post-destroy cooldown); <c>TicksRemaining</c> is the live state —
/// non-zero means interactions on the host entity are gated. <c>Unit</c> selects how it counts down:
/// <see cref="CooldownUnit.Ticks"/> = real per-tick decrement, <see cref="CooldownUnit.Days"/> = binary
/// flag cleared on day advance.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("c001d09a-1234-4d5e-9f7a-8b0c1d2e3f4a")]
public struct CooldownComponent : IComponent
{
    public int MaxTicks;
    public int TicksRemaining;
    public int Unit;
}
