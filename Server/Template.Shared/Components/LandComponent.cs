using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("e5f6a7b8-c9d0-4e1f-2a3b-4c5d6e7f8a9b")]
public struct LandComponent : IComponent
{
    public int CurrentCoins;
    public int Threshold;
}
