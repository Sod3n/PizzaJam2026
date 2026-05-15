using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("69b87123-8f85-478c-bed5-391327c28b10")]
public struct CaughtComponent : IComponent
{
    public int TicksRemaining;
    public int TotalTicks;
    public Entity CowEntity;
    public Float CowOffsetX;
    public Float CowOffsetY;
}
