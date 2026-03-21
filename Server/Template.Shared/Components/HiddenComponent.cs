using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential)]
[StableId("fc48881c-3e29-4bbb-8585-751a65979f96")]
public struct HiddenComponent : IComponent
{
    public uint PreviousLayer;
    public uint PreviousMask;
}
