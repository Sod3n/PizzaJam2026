using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential)]
[StableId("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d")]
public struct GlobalResourcesComponent : IComponent
{
    public int Grass;
    public int Milk;
    public int Coins;
}
