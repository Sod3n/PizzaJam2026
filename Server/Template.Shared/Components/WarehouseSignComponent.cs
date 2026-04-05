using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("cf001234-5678-9abc-def0-aabbccddf002")]
public struct WarehouseSignComponent : IComponent
{
    public Entity WarehouseId;
    public int Enabled; // mirrors WarehouseComponent.Enabled — cycles on interaction
}
