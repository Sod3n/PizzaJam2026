using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

public static class SellProduct
{
    public const int Milk = 0;
    public const int Cow = 1;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("e5f6a7b8-c9d0-4e1f-2a3b-4c5d6e7f8091")]
public struct SellPointSignComponent : IComponent
{
    public Entity SellPointId;
    public int CurrentProduct; // 0 = Milk, 1 = Cow
}
