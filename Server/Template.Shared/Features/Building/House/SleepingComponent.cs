using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("b2c3d4e5-f6a7-4b8c-9d01-2e3f4a5b6c7d")]
public struct SleepingComponent : IComponent
{
    public int TicksRemaining;
    public int TotalTicks;
    public Entity House;
    public int DayAdvanced; // 0/1 — set when AdvanceDay has fired mid-sleep
}
