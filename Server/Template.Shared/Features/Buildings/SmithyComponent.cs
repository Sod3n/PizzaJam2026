using Deterministic.GameFramework.ECS;
using System.Runtime.InteropServices;

namespace Template.Shared.Components;

/// <summary>Building that dispenses a hammer entity when interacted with.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("5717c8d1-aaaa-4d5e-9f7a-8b0c1d2e3f4a")]
public struct SmithyComponent : IComponent
{
}
