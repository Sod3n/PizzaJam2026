using System.Runtime.InteropServices;
using Deterministic.GameFramework.ECS;

namespace Template.Shared.Components;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("a7b3c9d2-e4f5-4a6b-8c1d-9e0f2a3b4c5d")]
public struct HelperPetComponent : IComponent
{
    public Entity FollowTarget;
    public int HelperType;
}
