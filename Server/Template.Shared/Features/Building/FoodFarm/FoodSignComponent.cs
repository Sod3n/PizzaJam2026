using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("c3d4e5f6-a7b8-4c9d-0e1f-2a3b4c5d6e7f")]
public struct FoodSignComponent : IComponent
{
    public Entity HouseId;
    public int SelectedFood; // FoodType constant — cycles on interaction
}
