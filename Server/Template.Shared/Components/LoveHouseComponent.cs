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
}
