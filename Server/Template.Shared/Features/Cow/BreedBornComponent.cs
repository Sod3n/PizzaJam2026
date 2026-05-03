using System.Runtime.InteropServices;
using Deterministic.GameFramework.ECS;

namespace Template.Shared.Components;

/// <summary>Marker component added to entities that were just born from breeding. Used to trigger the breed result overlay on the client.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[StableId("f3e2d1c0-b9a8-4f7e-9d6c-5b4a3c2d1e0f")]
public struct BreedBornComponent : IComponent { }
