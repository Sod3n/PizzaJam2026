// Component struct — source of truth for fields
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("ba8aface-147f-ca54-be3e-b1547f32b73e")]
public struct FinalStructureComponent : IComponent
{
    public int CurrentCoins;
    public int Threshold;
}
