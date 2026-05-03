using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("a1b2c3d4-e5f6-4a5b-8c7d-9e0f1a2b3c4d")]
public struct SkinComponent : IComponent
{
    // Maps Slot Name (e.g. "Hair") to Skin ID
    public Dictionary16<FixedString32, int> Skins;

    // Maps Slot Name to packed RGB color (0xRRGGBB)
    public Dictionary16<FixedString32, int> Colors;
}
