using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("a1c2e3d4-7b8c-4d9e-9f01-2b3c4d5e6f70")]
public struct LandSignComponent : IComponent
{
    public Entity LandId;
    public int SelectedType;
}
