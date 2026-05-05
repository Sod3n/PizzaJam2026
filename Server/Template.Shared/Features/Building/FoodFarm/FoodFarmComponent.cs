using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("a2f3b4c5-d6e7-4f8a-9b0c-1d2e3f4a5b6c")]
public struct FoodFarmComponent : IComponent
{
    public int FoodType; // FoodType.Carrot, Apple, or Mushroom
}
