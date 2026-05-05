using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

public static class HammerState
{
    /// <summary>Lying on the ground, available for pickup.</summary>
    public const int Idle = 0;
    /// <summary>Carried by a player (referenced by PlayerStateComponent.CarriedEntity).</summary>
    public const int Carried = 1;
}

/// <summary>
/// Carryable demolish tool. Picked up from a Smithy (or off the ground), consumed when
/// used to demolish a building, dropped in place when the player interacts with empty air.
/// Mirrors HelperPetComponent's pickup pattern.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("9a112c00-bbbb-4d5e-9f7a-8b0c1d2e3f4a")]
public struct HammerComponent : IComponent
{
    public int State;
}
