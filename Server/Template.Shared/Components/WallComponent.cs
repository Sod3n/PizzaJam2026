using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d")]
public struct WallComponent : IComponent
{
}
