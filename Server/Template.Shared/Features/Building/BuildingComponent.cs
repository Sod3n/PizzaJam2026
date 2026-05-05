using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;
using Template.Shared.Components;

namespace Template.Shared.Components;

// Marker + type tag on every built structure (any LandType reachable via CompleteLandBuilding).
// Single source of truth: presence => demolishable + has a known building type.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("3fb8add3-c35a-4514-b272-cc5d84be1deb")]
public struct BuildingComponent : IComponent
{
    public LandType Type;
}
