using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e")]
public struct GrassComponent : IComponent
{
    public int Durability;
    public int MaxDurability;
}
