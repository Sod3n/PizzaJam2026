using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("b4c5d6e7-f8a9-4b0c-1d2e-3f4a5b6c7d8e")]
public struct EnterStateComponent : IComponent
{
    public FixedString32 Key;
    public FixedString32 Param;
    public int Age;
}
