using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("c5d6e7f8-a9b0-4c1d-2e3f-4a5b6c7d8e9f")]
public struct ExitStateComponent : IComponent
{
    public FixedString32 Key;
    public int Age;
}
