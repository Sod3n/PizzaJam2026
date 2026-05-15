using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("1a44a205-f92c-4bdd-81ec-e012cee9cf30")]
public struct CowJumpComponent : IComponent
{
    public int WindupTicksLeft;
    public int LeapTicksLeft;
    public int LeapDurationTicks;
}
