using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("d4e5f6a7-b8c9-4d0e-1f2a-3b4c5d6e7f8a")]
public struct HouseComponent : IComponent
{
    public Entity CowId;
}
