using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("a3b4c5d6-e7f8-4a9b-0c1d-2e3f4a5b6c7d")]
public struct StateComponent : IComponent
{
    public FixedString32 Key;
    public int CurrentTime;
    public int MaxTime; // 0 = indefinite (no auto-advance)
    public bool IsEnabled;
}
