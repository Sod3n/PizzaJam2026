using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("131a66f5-08d7-408f-9d5b-90a7e8b9b880")]
public struct RejectedComponent : IComponent
{
    public const int MaxDuration = 2;
    public int Duration = 0;

    public RejectedComponent()
    {
    }
}