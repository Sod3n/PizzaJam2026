using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

// Set on the player when InteractActionService decides "open dispatch by target type."
// Per-feature systems (HouseMilkSystem, CowTameSystem, ...) consume it: each checks if the
// target matches its component type + player preconditions, executes, then removes the marker.
// Mutually exclusive preconditions across systems mean only one claims a given request.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("755712a8-61e8-4d21-85a7-bdb228bf37b7")]
public struct InteractRequestComponent : IComponent
{
    public Entity Target;
}
