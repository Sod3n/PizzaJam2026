// Component struct — source of truth for fields
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("a3b4c5d6-e7f8-1234-5678-9abcdef01234")]
public struct LoveHouseComponent : IComponent
{
    public Entity CowId1;
    public Entity CowId2;
    public int BreedProgress; // Clicks so far during breeding
    public int BreedCost;     // Total clicks needed (set from cow exhaust values)
    public int HeartPercent; // Visual feedback: % chance of showing heart vs broken-heart per click
    public int CooldownTicksRemaining; // Ticks remaining before love house can breed again

    public const int BreedCooldownTicks = 5400; // 90 seconds at 60 ticks/sec
}
