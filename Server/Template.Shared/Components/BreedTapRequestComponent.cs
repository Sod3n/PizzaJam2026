using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

// Set on the player when an InteractAction fires while the player is in active Breed state.
// CowBreedTapSystem advances the breed cycle in response.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("c2564999-038e-4112-ad3a-ba2ebc8a9b80")]
public struct BreedTapRequestComponent : IComponent
{
}
