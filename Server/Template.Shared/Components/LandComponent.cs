// Component struct — source of truth for fields
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("861f6742-9fbc-055e-b43d-b0f04d1b057f")]
public struct LandComponent : IComponent
{
    public int CurrentCoins;
    public int Threshold;
}
