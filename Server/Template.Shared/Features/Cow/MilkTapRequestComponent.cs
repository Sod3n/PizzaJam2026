using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

// Set on the player when an InteractAction fires while the player is in active Milking state.
// CowMilkTapSystem advances the milk cycle in response.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("ecff64aa-af7f-4409-af3a-59a44bc85ef5")]
public struct MilkTapRequestComponent : IComponent
{
}
