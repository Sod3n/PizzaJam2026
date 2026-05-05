using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("cf001234-5678-9abc-def0-aabbccddf001")]
public struct WarehouseComponent : IComponent
{
    public int Enabled; // 1 = helpers auto-deposit, 0 = normal behavior
}
