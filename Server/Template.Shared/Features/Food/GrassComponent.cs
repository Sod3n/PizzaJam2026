// Component struct — source of truth for fields
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("432c54c2-ff73-9454-b2ad-483a305898ca")]
public struct GrassComponent : IComponent
{
    public int Durability;
    public int MaxDurability;
    public int FoodType; // FoodType.Grass, Carrot, Apple, Mushroom
}
