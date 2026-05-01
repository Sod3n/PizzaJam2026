using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("e1f2a3b4-c5d6-4e7f-8901-2a3b4c5d6e7f")]
public struct PlayerHouseComponent : IComponent
{
    public int CooldownTicksRemaining;

    public const int SleepCooldownTicks = 7200;
}
