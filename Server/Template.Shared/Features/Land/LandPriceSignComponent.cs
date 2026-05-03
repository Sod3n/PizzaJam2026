using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("f7a8b9c0-d1e2-4f3a-4b5c-6d7e8f9091a2")]
public struct LandPriceSignComponent : IComponent
{
    public Entity LandId;
}
