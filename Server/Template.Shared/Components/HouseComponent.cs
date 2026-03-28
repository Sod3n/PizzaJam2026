// Component struct — source of truth for fields
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("cb92c8de-61fa-f358-acef-82ab01ca21fe")]
public struct HouseComponent : IComponent
{
    public Entity CowId;
    public int SelectedFood; // FoodType constant — set by the food sign
}
