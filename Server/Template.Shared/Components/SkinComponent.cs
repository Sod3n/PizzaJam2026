using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Types;

namespace Template.Shared.Components;

[StableId("a1b2c3d4-e5f6-4a5b-8c7d-9e0f1a2b3c4d")]
public struct SkinComponent : IComponent
{
    // Maps Slot Name (e.g. "Hair") to Skin ID
    public Dictionary16<FixedString32, int> Skins;
}
